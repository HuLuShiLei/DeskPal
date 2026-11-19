using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Personal;

public static class TitleFilter
{
    /// <summary>
    /// Drops the detail when a no-title rule matches, right after the presence is resolved, so the room, the home page and
    /// the usage stats never get it.
    /// </summary>
    /// <param name="process">Process in front, for <see cref="NoTitleKind.Process"/>.</param>
    /// <param name="domain">Normalized host when a browser is in front, for <see cref="NoTitleKind.Domain"/>.</param>
    public static LocalPresence Apply(LocalPresence presence, IReadOnlyList<NoTitleRule> rules, string? process, string? domain)
    {
        if (string.IsNullOrWhiteSpace(presence.Detail) || rules.Count == 0)
        {
            return presence;
        }

        var app = presence.AppDisplayName?.Trim();
        var title = presence.Detail.Trim();
        return rules.Any(r => r.Matches(app, process, domain, title)) ? presence with { Detail = null } : presence;
    }
}