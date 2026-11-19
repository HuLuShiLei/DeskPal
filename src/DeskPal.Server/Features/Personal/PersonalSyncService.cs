using System.Text.Json;
using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Rooms;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Features.Personal;

/// <summary>
/// Whether the account keeps its personal data (personal rules, no-title rules, away time, preferences) on the server.
/// Off by default; the client keeps its own copy either way.
/// </summary>
public sealed class PersonalSyncService(DeskPalDbContext db, TimeProvider time, RoomService rooms)
{
    public async Task<bool> IsOnAsync(Guid userId) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.PersonalSync).SingleOrDefaultAsync();

    /// <summary>
    /// Turns sync on and merges what the computer has: a rule is kept from whichever side changed it last, and the
    /// account's preferences win over the computer's. Answers with what the account keeps.
    /// </summary>
    public async Task<ServiceResult<PersonalDataDto>> EnableAsync(Guid userId, PersonalDataDto local)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return ServiceResult<PersonalDataDto>.Fail(401, ApiErrorCodes.AccountMissing);
        }

        var now = time.GetUtcNow();
        user.PersonalSync = true;
        await db.SharedRuleReports.Where(r => r.UserId == userId).ExecuteDeleteAsync();
        await MergeRulesAsync(userId, local.Rules ?? [], now);
        await MergeNoTitleAsync(userId, local.NoTitleRules ?? [], now);

        if (local.Preferences is { } preferences && AccountPreferences.IsValid(preferences))
        {
            var roomIds = (await rooms.GetRoomIdsForUserAsync(userId)).ToHashSet();
            user.Preferences = Write(AccountPreferences.Merge(preferences, ReadPreferences(user) ?? new UserPreferences(), roomIds.Contains));
        }

        if (user.IdleMinutes is null && local.IdleMinutes is int minutes && AccountIdle.IsAllowed(minutes))
        {
            user.IdleMinutes = minutes;
        }

        await db.SaveChangesAsync();
        return ServiceResult<PersonalDataDto>.Ok(await ReadAsync(user));
    }

    /// <summary>Turns sync off and deletes the personal data kept here. The computers keep their own copies.</summary>
    public async Task<ServiceResult<Unit>> DisableAsync(Guid userId)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return ServiceResult<Unit>.Fail(401, ApiErrorCodes.AccountMissing);
        }

        user.PersonalSync = false;
        user.Preferences = null;
        user.IdleMinutes = null;
        await db.UserRules.Where(r => r.UserId == userId).ExecuteDeleteAsync();
        await db.UserNoTitleRules.Where(r => r.UserId == userId).ExecuteDeleteAsync();
        await db.SaveChangesAsync();
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    public static UserPreferences? ReadPreferences(User user) =>
        user.Preferences is null ? null : JsonSerializer.Deserialize<UserPreferences>(user.Preferences, DeskPalJson.Options);

    public static string Write(UserPreferences preferences) => JsonSerializer.Serialize(preferences, DeskPalJson.Options);

    private async Task MergeRulesAsync(Guid userId, IReadOnlyList<UserRuleDto> incoming, DateTimeOffset now)
    {
        var stored = await db.UserRules.Where(r => r.UserId == userId).ToListAsync();
        foreach (var rule in incoming)
        {
            if (PersonalLimits.CheckRule(rule.Kind, rule.Pattern, rule.Category, out var pattern) is not null)
            {
                continue;
            }

            var updatedAt = rule.UpdatedAt > now ? now : rule.UpdatedAt;
            var existing = stored.FirstOrDefault(r => r.Kind == rule.Kind && r.Pattern == pattern);
            if (existing is null)
            {
                if (stored.Count >= PersonalLimits.MaxRules)
                {
                    continue;
                }

                existing = new UserRule { Id = Guid.NewGuid(), UserId = userId, Kind = rule.Kind, Pattern = pattern };
                db.UserRules.Add(existing);
                stored.Add(existing);
            }
            else if (existing.UpdatedAt >= updatedAt)
            {
                continue;
            }

            existing.Category = rule.Category;
            existing.Name = PersonalLimits.CleanName(rule.Name);
            existing.UpdatedAt = updatedAt;
        }
    }

    private async Task MergeNoTitleAsync(Guid userId, IReadOnlyList<NoTitleRuleDto> incoming, DateTimeOffset now)
    {
        var stored = await db.UserNoTitleRules.Where(r => r.UserId == userId).ToListAsync();
        foreach (var item in incoming)
        {
            if (NoTitleRule.Create(item.Kind, item.Pattern) is not { } rule)
            {
                continue;
            }

            var updatedAt = item.UpdatedAt > now ? now : item.UpdatedAt;
            var existing = stored.FirstOrDefault(r => new NoTitleRule(r.Kind, r.Pattern).SameAs(rule));
            if (existing is null)
            {
                if (stored.Count >= PersonalLimits.MaxNoTitleRules)
                {
                    continue;
                }

                existing = new UserNoTitleRule { Id = Guid.NewGuid(), UserId = userId, Kind = rule.Kind, Pattern = rule.Pattern };
                db.UserNoTitleRules.Add(existing);
                stored.Add(existing);
            }
            else if (existing.UpdatedAt >= updatedAt)
            {
                continue;
            }

            existing.Pattern = rule.Pattern;
            existing.UpdatedAt = updatedAt;
        }
    }

    private async Task<PersonalDataDto> ReadAsync(User user)
    {
        var rules = await db.UserRules.Where(r => r.UserId == user.Id).ToListAsync();
        var noTitle = await db.UserNoTitleRules.Where(r => r.UserId == user.Id).ToListAsync();
        return new PersonalDataDto(
            rules.OrderByDescending(r => r.UpdatedAt).Select(r => new UserRuleDto(r.Kind, r.Pattern, r.Category, r.Name, r.UpdatedAt)).ToList(),
            noTitle.OrderBy(r => r.UpdatedAt).Select(NoTitleRuleService.ToDto).ToList(),
            ReadPreferences(user),
            user.IdleMinutes);
    }
}