using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Usage;

namespace DeskPal.Client.Core.Ui;

/// <summary>Turns the usage of one app, site or title into the detail page opened from a ranking row.</summary>
public static class StatsAppViewBuilder
{
    /// <param name="app">App name as recorded.</param>
    /// <param name="title">Only this title of the app; null for the whole app.</param>
    /// <param name="rule">What a personal rule for the app would be saved against; null when it cannot be changed.</param>
    public static StatsAppView Build(UsageAppDetail detail, string app, string? title, StatsScale scale, DateOnly anchor, DateOnly today, StatsRuleTarget? rule)
    {
        var range = StatsRange.Of(scale, anchor);
        var key = StatsViewBuilder.CategoryKey(detail.Category) ?? "Other";
        var legend = StatsViewBuilder.CategoryLegend.First(l => l.Key == key);
        var appName = AppNames.LocalizeOrNull(app) ?? app;
        var shownTo = range.To < today ? range.To : today;
        var shownDays = Math.Max(1, shownTo.DayNumber - range.From.DayNumber + 1);
        string Duration(long seconds) => StatsViewBuilder.Duration(seconds);
        static string Hours(int hour) => $"{hour}:00–{hour + 1}:00";
        StatsBar HourBar(int hour) =>
            new(hour % 6 == 0 ? hour.ToString() : "", $"{Hours(hour)} · {Duration(detail.Hours[hour])}", null, Part(detail.Hours[hour]));
        List<StatsPart> Part(long seconds) => seconds > 0 ? [new StatsPart(key, seconds)] : [];

        List<StatsBar> bars;
        long barMax;
        if (scale == StatsScale.Day)
        {
            bars = Enumerable.Range(0, 24).Select(HourBar).ToList();
            barMax = 3600;
        }
        else
        {
            bars = Enumerable.Range(0, range.Days).Select(i =>
            {
                var date = range.From.AddDays(i);
                var seconds = detail.Days.GetValueOrDefault(date);
                var label = scale == StatsScale.Week ? PresenceFormatter.Weekday(date.DayOfWeek) : date.Day == 1 || date.Day % 5 == 0 ? date.Day.ToString() : "";
                var day = PresenceFormatter.DateWithWeekday(date, today);
                var tip = date > today ? UiLanguage.Format(Strings.Stats_NotYet, day) : $"{day} · {Duration(seconds)}";
                return new StatsBar(label, tip, date <= today ? date : null, Part(seconds));
            }).ToList();
            barMax = Math.Max(1, (long)Math.Ceiling(detail.Days.Values.DefaultIfEmpty(0).Max() / 3600.0)) * 3600;
        }

        var hours = scale == StatsScale.Day ? [] : Enumerable.Range(0, 24).Select(HourBar).ToList();
        var hoursMax = Math.Max(60, detail.Hours.Max());

        var facts = new List<StatsFact>();
        if (detail.Sessions > 0)
        {
            facts.Add(new StatsFact(Strings.Stats_Sessions, UiLanguage.Format(Strings.Stats_SessionsValue, detail.Sessions)));
            facts.Add(new StatsFact(Strings.Stats_AverageSession, Duration(detail.Hours.Sum() / detail.Sessions)));
            facts.Add(new StatsFact(Strings.Stats_LongestSession, Duration(detail.Longest)));
            var peak = Enumerable.Range(0, 24).MaxBy(h => detail.Hours[h]);
            facts.Add(new StatsFact(Strings.Stats_PeakHour, Hours(peak)));
        }

        var titles = detail.Titles.Where(t => t.Seconds >= 60).ToList();
        var titleMax = titles.Count == 0 ? 1 : titles[0].Seconds;
        return new StatsAppView(
            scale,
            anchor,
            title ?? appName,
            title is null ? legend.Name : $"{appName} · {legend.Name}",
            StatsViewBuilder.Title(scale, range, today),
            true,
            range.To < today,
            scale == StatsScale.Day ? Strings.Stats_UsageTime : Strings.Stats_DailyAverage,
            Duration(scale == StatsScale.Day ? detail.Seconds : detail.Seconds / shownDays),
            scale != StatsScale.Day && detail.Seconds > 0 ? UiLanguage.Format(Strings.Stats_Total, Duration(detail.Seconds)) : null,
            facts,
            [legend],
            bars,
            barMax,
            Duration(barMax),
            hours,
            hoursMax,
            Duration(hoursMax),
            title is null
                ? titles.Select(t => new StatsRankItem(t.Title, null, legend.Tone, Duration(t.Seconds), Math.Round((double)t.Seconds / titleMax, 3), Detail: new StatsDetailKey(app, t.Title))).ToList()
                : [],
            rule is null ? null : rule with { Category = detail.Category.ToString() },
            detail.Seconds == 0 ? Strings.Stats_EmptyMine : null);
    }
}
