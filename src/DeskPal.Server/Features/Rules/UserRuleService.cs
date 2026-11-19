using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Server.Features.Personal;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;
using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Features.Rules;

/// <summary>Personal rules belong to the account, so they follow the user into every room and onto every computer.</summary>
/// <remarks>Only kept while the account has personal sync on; otherwise the computers keep them on their own.</remarks>
public sealed class UserRuleService(DeskPalDbContext db, TimeProvider time, PersonalSyncService personal)
{
    public const int MaxRulesPerUser = PersonalLimits.MaxRules;

    public async Task<ServiceResult<List<UserRuleDto>>> ListAsync(Guid userId)
    {
        if (!await personal.IsOnAsync(userId))
        {
            return ServiceResult<List<UserRuleDto>>.Fail(403, ApiErrorCodes.PersonalSyncOff);
        }

        var rules = await db.UserRules.Where(r => r.UserId == userId).ToListAsync();
        return ServiceResult<List<UserRuleDto>>.Ok(rules.OrderByDescending(r => r.UpdatedAt).Select(ToDto).ToList());
    }

    public async Task<ServiceResult<UserRuleDto>> UpsertAsync(Guid userId, UpsertUserRuleRequest req)
    {
        if (!await personal.IsOnAsync(userId))
        {
            return ServiceResult<UserRuleDto>.Fail(403, ApiErrorCodes.PersonalSyncOff);
        }

        if (PersonalLimits.CheckRule(req.Kind, req.Pattern, req.Category, out var pattern) is { } error)
        {
            return ServiceResult<UserRuleDto>.Fail(400, error);
        }

        var rule = await db.UserRules.SingleOrDefaultAsync(r => r.UserId == userId && r.Kind == req.Kind && r.Pattern == pattern);
        if (rule is null)
        {
            if (await db.UserRules.CountAsync(r => r.UserId == userId) >= MaxRulesPerUser)
            {
                return ServiceResult<UserRuleDto>.Fail(409, ApiErrorCodes.RuleLimit, MaxRulesPerUser);
            }

            rule = new UserRule { Id = Guid.NewGuid(), UserId = userId, Kind = req.Kind, Pattern = pattern };
            db.UserRules.Add(rule);
        }

        rule.Category = req.Category;
        rule.Name = PersonalLimits.CleanName(req.Name);
        rule.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return ServiceResult<UserRuleDto>.Ok(ToDto(rule));
    }

    /// <returns>False when there was no such rule.</returns>
    public async Task<ServiceResult<bool>> DeleteAsync(Guid userId, RuleKind kind, string pattern)
    {
        if (!await personal.IsOnAsync(userId))
        {
            return ServiceResult<bool>.Fail(403, ApiErrorCodes.PersonalSyncOff);
        }

        var normalized = RulePattern.Normalize(kind, pattern);
        var deleted = await db.UserRules.Where(r => r.UserId == userId && r.Kind == kind && r.Pattern == normalized).ExecuteDeleteAsync();
        return ServiceResult<bool>.Ok(deleted > 0);
    }

    private static UserRuleDto ToDto(UserRule r) => new(r.Kind, r.Pattern, r.Category, r.Name, r.UpdatedAt);
}
