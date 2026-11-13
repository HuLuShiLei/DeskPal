using DeskPal.Shared.Presence;
using DeskPal.Shared.Stats;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Reduces a report to what a room may see. Each category is shown at that room's privacy level for the category:
/// only the state, the category and app, or also the titles. The same rule as the live presence.
/// </summary>
public static class StatsFilter
{
    public static UsageReport Apply(UsageReport report, PrivacyProfile profile, int top = UsageAggregator.TopCount)
    {
        PrivacyLevel Level(ActivityCategory category) => category == ActivityCategory.None ? PrivacyLevel.StateOnly : profile.LevelFor(category);
        ActivityCategory Shown(ActivityCategory category) => Level(category) == PrivacyLevel.StateOnly ? ActivityCategory.None : category;

        static List<UsageTotal> Merge(IEnumerable<UsageTotal> totals) => totals
            .GroupBy(t => (t.Date, t.Hour, t.State, t.Category))
            .Select(g => new UsageTotal(g.Key.Date, g.Key.Hour, g.Key.State, g.Key.Category, g.Sum(t => t.Seconds)))
            .ToList();

        static List<UsageItem> Top(IEnumerable<UsageItem> items, int count) => items
            .GroupBy(i => (i.State, i.Category, i.App, i.Title))
            .Select(g => new UsageItem(g.Key.State, g.Key.Category, g.Key.App, g.Key.Title, g.Sum(i => i.Seconds)))
            .OrderByDescending(i => i.Seconds)
            .Take(count)
            .ToList();

        return report with
        {
            Days = Merge(report.Days.Select(d => d with { Category = Shown(d.Category) })),
            Hours = Merge(report.Hours.Select(h => h with { Category = Shown(h.Category) })),
            Apps = Top(report.Apps.Select(a => Level(a.Category) == PrivacyLevel.StateOnly
                ? a with { Category = ActivityCategory.None, App = null, Title = null }
                : a with { Title = null }), top),
            Titles = Top(report.Titles.Select(t => Level(t.Category) switch
            {
                PrivacyLevel.StateOnly => t with { Category = ActivityCategory.None, App = null, Title = null },
                PrivacyLevel.Category => t with { Title = null },
                _ => t,
            }), top),
        };
    }
}