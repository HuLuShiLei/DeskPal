using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Shared.Api;
using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Features.Personal;

/// <summary>No-title rules of accounts that keep their personal data on the server.</summary>
public sealed class NoTitleRuleService(DeskPalDbContext db, TimeProvider time, PersonalSyncService personal)
{
    public async Task<ServiceResult<List<NoTitleRuleDto>>> ListAsync(Guid userId)
    {
        if (!await personal.IsOnAsync(userId))
        {
            return ServiceResult<List<NoTitleRuleDto>>.Fail(403, ApiErrorCodes.PersonalSyncOff);
        }

        var rules = await db.UserNoTitleRules.Where(r => r.UserId == userId).ToListAsync();
        return ServiceResult<List<NoTitleRuleDto>>.Ok(rules.OrderBy(r => r.UpdatedAt).Select(ToDto).ToList());
    }

    public async Task<ServiceResult<NoTitleRuleDto>> UpsertAsync(Guid userId, UpsertNoTitleRuleRequest req)
    {
        if (!await personal.IsOnAsync(userId))
        {
            return ServiceResult<NoTitleRuleDto>.Fail(403, ApiErrorCodes.PersonalSyncOff);
        }

        if (NoTitleRule.Create(req.Kind, req.Pattern) is not { } rule)
        {
            return ServiceResult<NoTitleRuleDto>.Fail(400, ApiErrorCodes.NoTitleInvalid);
        }

        var stored = await db.UserNoTitleRules.Where(r => r.UserId == userId).ToListAsync();
        var existing = stored.FirstOrDefault(r => new NoTitleRule(r.Kind, r.Pattern).SameAs(rule));
        if (existing is null)
        {
            if (stored.Count >= PersonalLimits.MaxNoTitleRules)
            {
                return ServiceResult<NoTitleRuleDto>.Fail(409, ApiErrorCodes.NoTitleLimit, PersonalLimits.MaxNoTitleRules);
            }

            existing = new UserNoTitleRule { Id = Guid.NewGuid(), UserId = userId, Kind = rule.Kind, Pattern = rule.Pattern };
            db.UserNoTitleRules.Add(existing);
        }

        existing.Pattern = rule.Pattern;
        existing.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return ServiceResult<NoTitleRuleDto>.Ok(ToDto(existing));
    }

    /// <returns>False when there was no such rule.</returns>
    public async Task<ServiceResult<bool>> DeleteAsync(Guid userId, NoTitleKind kind, string pattern)
    {
        if (!await personal.IsOnAsync(userId))
        {
            return ServiceResult<bool>.Fail(403, ApiErrorCodes.PersonalSyncOff);
        }

        var target = new NoTitleRule(kind, NoTitleRule.Create(kind, pattern)?.Pattern ?? pattern);
        var stored = await db.UserNoTitleRules.Where(r => r.UserId == userId && r.Kind == kind).ToListAsync();
        var matches = stored.Where(r => new NoTitleRule(r.Kind, r.Pattern).SameAs(target)).ToList();
        db.UserNoTitleRules.RemoveRange(matches);
        await db.SaveChangesAsync();
        return ServiceResult<bool>.Ok(matches.Count > 0);
    }

    public static NoTitleRuleDto ToDto(UserNoTitleRule r) => new(r.Kind, r.Pattern, r.UpdatedAt);
}