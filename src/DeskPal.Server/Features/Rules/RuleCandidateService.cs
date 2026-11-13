using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeskPal.Server.Features.Rules;

/// <summary>
/// Collects what users shared (personal rules and unknown apps) and proposes builtin rules.
/// Only accounts that agreed to share and are old enough are counted, one vote per account.
/// Only processes: builtin rules never classify browser sites.
/// </summary>
public sealed class RuleCandidateService(DeskPalDbContext db, TimeProvider time, IOptions<RuleOptions> options)
{
    public const int MaxAppsPerReport = 50;
    private const int MaxUnknown = 100;

    public async Task<ServiceResult<Unit>> SetSharingAsync(Guid userId, bool enabled)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return ServiceResult<Unit>.Fail(401, ApiErrorCodes.AccountMissing);
        }

        user.ShareUsage = enabled;
        if (!enabled)
        {
            await db.UnknownAppReports.Where(r => r.UserId == userId).ExecuteDeleteAsync();
        }

        await db.SaveChangesAsync();
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    public async Task<ServiceResult<Unit>> ReportAsync(Guid userId, UsageReportRequest req)
    {
        var user = await db.Users.FindAsync(userId);
        if (user?.ShareUsage != true)
        {
            return ServiceResult<Unit>.Fail(403, ApiErrorCodes.SharingOff);
        }

        var now = time.GetUtcNow();
        var apps = (req.Apps ?? [])
            .Where(a => a.Kind == RuleKind.Process)
            .Select(a => a with { Pattern = RulePattern.Normalize(a.Kind, a.Pattern) })
            .Where(a => a.Pattern.Length is > 0 and <= 200)
            .DistinctBy(a => a.Pattern)
            .Take(MaxAppsPerReport)
            .ToList();
        var patterns = apps.Select(a => a.Pattern).ToList();
        var existing = await db.UnknownAppReports
            .Where(r => r.UserId == userId && r.Kind == RuleKind.Process && patterns.Contains(r.Pattern))
            .ToDictionaryAsync(r => r.Pattern);
        foreach (var app in apps)
        {
            if (!existing.TryGetValue(app.Pattern, out var report))
            {
                report = new UnknownAppReport { UserId = userId, Kind = RuleKind.Process, Pattern = app.Pattern };
                db.UnknownAppReports.Add(report);
            }

            var name = app.DisplayName?.Trim();
            report.DisplayName = string.IsNullOrEmpty(name) ? null : name.Length > 60 ? name[..60] : name;
            report.Minutes = Math.Clamp(app.Minutes, 0, 24 * 60);
            report.ReportedAt = now;
        }

        await db.SaveChangesAsync();
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    public async Task<RuleCandidatesResponse> GetCandidatesAsync()
    {
        var o = options.Value;
        var oldest = time.GetUtcNow().AddDays(-o.MinAccountAgeDays);
        var sharing = await db.Users.Where(u => u.ShareUsage == true).Select(u => new { u.Id, u.CreatedAt }).ToListAsync();
        var eligible = sharing.Where(u => u.CreatedAt <= oldest).Select(u => u.Id).ToHashSet();

        var builtin = await db.BuiltinRules.Where(r => r.Kind == RuleKind.Process).ToDictionaryAsync(r => r.Pattern, r => r.Category);
        var rules = (await db.UserRules.Where(r => r.Kind == RuleKind.Process).ToListAsync())
            .Where(r => eligible.Contains(r.UserId))
            .ToList();

        var consensus = new List<ConsensusCandidate>();
        foreach (var group in rules.GroupBy(r => r.Pattern))
        {
            var users = group.Count();
            var top = group.GroupBy(r => r.Category).OrderByDescending(g => g.Count()).First();
            ActivityCategory? current = builtin.TryGetValue(group.Key, out var c) ? c : null;
            if (users < o.MinUsers || top.Count() < users * o.MinAgreement || current == top.Key)
            {
                continue;
            }

            var name = top.Where(r => r.Name is not null).GroupBy(r => r.Name).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
            consensus.Add(new ConsensusCandidate(RuleKind.Process, group.Key, name, top.Key, users, top.Count(), current));
        }

        var known = builtin.Keys.Concat(rules.Select(r => r.Pattern)).ToHashSet();
        var reports = (await db.UnknownAppReports.Where(r => r.Kind == RuleKind.Process).ToListAsync())
            .Where(r => eligible.Contains(r.UserId) && !known.Contains(r.Pattern))
            .GroupBy(r => r.Pattern)
            .Where(g => g.Count() >= o.MinUsers)
            .Select(g => new UnknownAppCandidate(
                RuleKind.Process,
                g.Key,
                g.Where(r => r.DisplayName is not null).GroupBy(r => r.DisplayName).OrderByDescending(n => n.Count()).FirstOrDefault()?.Key,
                g.Count(),
                g.Sum(r => r.Minutes)))
            .OrderByDescending(c => c.Users)
            .ThenByDescending(c => c.TotalMinutes)
            .Take(MaxUnknown)
            .ToList();

        return new RuleCandidatesResponse(
            await BuiltinRuleSeeder.GetStoredVersionAsync(db),
            consensus.OrderByDescending(c => c.Users).ToList(),
            reports);
    }
}
