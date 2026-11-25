using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Ui;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Usage;

/// <summary>The personal rule a ranking row of my stats would be saved against: the domain for sites, the process for apps.</summary>
public static class UsageRuleTargets
{
    /// <param name="processes">The process each app name was recorded with, over all recorded history.</param>
    /// <param name="rules">Personal rules, then builtin ones. A named process rule tells the process of records made before it was kept.</param>
    public static Func<string, StatsRuleTarget?> Create(IReadOnlyDictionary<string, string> processes, IEnumerable<RuleDto> rules)
    {
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (rule.Kind == RuleKind.Process && rule.Name is { Length: > 0 } name)
            {
                named.TryAdd(name, rule.Pattern);
            }
        }

        return app =>
        {
            if (app == SensitiveText.IpAddressName)
            {
                return null;
            }

            var process = processes.GetValueOrDefault(app);
            if (process is not null ? Browsers.IsBrowser(process) : UsageReclassifier.IsHost(app))
            {
                var host = RulePattern.NormalizeHost(app);
                return host == app && host.Contains('.') ? new StatsRuleTarget(nameof(RuleKind.Domain), host, app) : null;
            }

            // An app without a description is named after its process, so the name is the last guess.
            // Records without a process match the rule by its name, so a wrong guess still changes them.
            var pattern = RulePattern.Normalize(RuleKind.Process, process ?? named.GetValueOrDefault(app) ?? app);
            return pattern.Length == 0 ? null : new StatsRuleTarget(nameof(RuleKind.Process), pattern, app);
        };
    }
}