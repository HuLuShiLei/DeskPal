using DeskPal.Shared.Presence;
using DeskPal.Shared.Stats;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Reduces a report to what a room may see. Each category is shown at that room's privacy level for the category:
/// only the state, also the app, or also the titles. The same rule as the live presence: the state tells the category
/// (work, chat, video, game, browsing, other), so the category is always kept. When and how long each day was used
/// (weekly rhythm, first and last use) is not shared at all.
/// </summary>
public static class StatsFilter
{
    public static UsageReport Apply(UsageReport report, PrivacyProfile profile, int top = UsageAggregator.TopCount)
    {
        PrivacyLevel Level(ActivityCategory category) => category == ActivityCategory.None ? PrivacyLevel.StateOnly : profile.LevelFor(category);

        static List<UsageItem> Top(IEnumerable<UsageItem> items, int count) => items
            .GroupBy(i => (i.State, i.Category, i.App, i.Title))
            .Select(g => new UsageItem(g.Key.State, g.Key.Category, g.Key.App, g.Key.Title, g.Sum(i => i.Seconds)))
            .OrderByDescending(i => i.Seconds)
            .Take(count)
            .ToList();

        return report with
        {
            Apps = Top(report.Apps.Select(a => Level(a.Category) == PrivacyLevel.StateOnly
                ? a with { App = null, Title = null }
                : a with { Title = null }), top),
            Titles = Top(report.Titles.Select(t => Level(t.Category) switch
            {
                PrivacyLevel.StateOnly => t with { App = null, Title = null },
                PrivacyLevel.Category => t with { Title = null },
                _ => t,
            }), top),
            WeekHours = null,
            Bounds = null,
        };
    }
}
