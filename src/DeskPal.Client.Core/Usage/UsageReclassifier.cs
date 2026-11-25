using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Applies the current personal rules to recorded usage when it is read, so that changing a category also changes the
/// past. Records are never rewritten: without a matching rule the category without personal rules is used, so removing
/// a rule restores it. Records are matched like the live classification: browsing by domain, title keyword, then process;
/// other apps by the app with a keyword of the title, then the app. Records kept without a title only match the app.
/// </summary>
public sealed class UsageReclassifier
{
    public static readonly UsageReclassifier None = new([]);

    private readonly RuleSet _rules;
    private readonly Dictionary<string, RuleDto> _byName = new(StringComparer.OrdinalIgnoreCase);

    public UsageReclassifier(IEnumerable<UserRuleDto> rules)
    {
        var newestFirst = rules.OrderByDescending(r => r.UpdatedAt).ToList();
        _rules = PersonalRuleStore.ToRuleSet(newestFirst);
        foreach (var rule in newestFirst)
        {
            // A rule for some titles of an app shares the app's name; it must not claim every record of that name.
            if (rule.Name is { Length: > 0 } name && rule.Kind != RuleKind.AppTitle)
            {
                _byName.TryAdd(name, new RuleDto(rule.Kind, rule.Pattern, rule.Category, rule.Name));
            }
        }
    }

    public (PresenceState State, ActivityCategory Category) Apply(PresenceState state, ActivityCategory category, string? app, string? title, string? process, ActivityCategory? baseCategory)
    {
        if (!UsageStates.Counts(state) || category == ActivityCategory.None)
        {
            return (state, category);
        }

        // A website opened by IP address is recorded without its address, so the rule that classified it can't be found again.
        var result = Match(app, title, process) is { } rule && !rule.Category.IsRetired() ? rule.Category
            : app == SensitiveText.IpAddressName ? category
            : baseCategory ?? category;
        return result == category ? (state, category) : (result.ToState(), result);
    }

    public UsageSegment Apply(UsageSegment s)
    {
        var (state, category) = Apply(s.State, s.Category, s.App, s.Title, s.Process, s.BaseCategory);
        return state == s.State && category == s.Category ? s : s with { State = state, Category = category };
    }

    public UsageRollup Apply(UsageRollup r)
    {
        var (state, category) = Apply(r.State, r.Category, r.App, r.Title, r.Process, r.BaseCategory);
        return state == r.State && category == r.Category ? r : r with { State = state, Category = category };
    }

    private RuleDto? Match(string? app, string? title, string? process)
    {
        // A record made in a browser is named by its domain. Older records have no process, so the name tells.
        var browser = process is not null ? Browsers.IsBrowser(process) : IsHost(app);
        if (browser)
        {
            if (app is not null && IsHost(app) && _rules.MatchDomain(app) is { } byDomain)
            {
                return byDomain;
            }

            if (_rules.MatchTitle(title) is { } byTitle)
            {
                return byTitle;
            }
        }
        else if (process is not null && _rules.MatchAppTitle(process, title) is { } byAppTitle)
        {
            return byAppTitle;
        }

        if (process is not null && _rules.MatchProcess(process) is { } byProcess)
        {
            return byProcess;
        }

        // A rule that matched at the time named the record: a site renamed by its rule, or an older record without a process.
        if (app is null || (process is not null && !browser) || _byName.GetValueOrDefault(app) is not { } byName)
        {
            return null;
        }

        return process is null || byName.Kind != RuleKind.Process ? byName : null;
    }

    internal static bool IsHost(string? app) =>
        app is { Length: > 0 } && !app.Contains(' ') && (app == "localhost" || (app.Contains('.') && RulePattern.NormalizeHost(app) == app));
}
