using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;
using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Features.Rules;

/// <summary>Personal rules belong to the account, so they follow the user into every room and onto every computer.</summary>
public sealed class UserRuleService(DeskPalDbContext db, TimeProvider time)
{
    public const int MaxRulesPerUser = 500;

    public async Task<List<UserRuleDto>> ListAsync(Guid userId)
    {
        var rules = await db.UserRules.Where(r => r.UserId == userId).ToListAsync();
        return rules.OrderByDescending(r => r.UpdatedAt).Select(ToDto).ToList();
    }

    public async Task<ServiceResult<UserRuleDto>> UpsertAsync(Guid userId, UpsertUserRuleRequest req)
    {
        var pattern = RulePattern.Normalize(req.Kind, req.Pattern);
        if (pattern.Length is < 1 or > 200)
        {
            return ServiceResult<UserRuleDto>.Fail(400, "规则内容不能为空，且不超过 200 个字符");
        }

        if (!Enum.IsDefined(req.Category) || req.Category is ActivityCategory.None or ActivityCategory.Meeting || req.Category.IsRetired())
        {
            return ServiceResult<UserRuleDto>.Fail(400, "请选择类别");
        }

        var rule = await db.UserRules.SingleOrDefaultAsync(r => r.UserId == userId && r.Kind == req.Kind && r.Pattern == pattern);
        if (rule is null)
        {
            if (await db.UserRules.CountAsync(r => r.UserId == userId) >= MaxRulesPerUser)
            {
                return ServiceResult<UserRuleDto>.Fail(409, $"个人分类最多 {MaxRulesPerUser} 条，请先删除不用的");
            }

            rule = new UserRule { Id = Guid.NewGuid(), UserId = userId, Kind = req.Kind, Pattern = pattern };
            db.UserRules.Add(rule);
        }

        var name = req.Name?.Trim();
        rule.Category = req.Category;
        rule.Name = string.IsNullOrEmpty(name) ? null : name.Length > 60 ? name[..60] : name;
        rule.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return ServiceResult<UserRuleDto>.Ok(ToDto(rule));
    }

    /// <returns>False when there was no such rule.</returns>
    public async Task<bool> DeleteAsync(Guid userId, RuleKind kind, string pattern)
    {
        var normalized = RulePattern.Normalize(kind, pattern);
        var deleted = await db.UserRules.Where(r => r.UserId == userId && r.Kind == kind && r.Pattern == normalized).ExecuteDeleteAsync();
        return deleted > 0;
    }

    private static UserRuleDto ToDto(UserRule r) => new(r.Kind, r.Pattern, r.Category, r.Name, r.UpdatedAt);
}
