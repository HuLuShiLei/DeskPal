using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Presence;
using DeskPal.Shared.Stats;

namespace DeskPal.Client.Core.Ui;

/// <summary>Turns a usage report into chart data and text for the stats page.</summary>
public static class StatsViewBuilder
{
    /// <summary>Names and tones of state spans on the timeline. "relax" only comes from older versions, which did not tell a hidden video from a hidden game.</summary>
    private static StatsLegend[] StateLegend =>
    [
        new("work", Strings.Legend_Work, "work"),
        new("chat", Strings.Legend_Chat, "chat"),
        new("video", Strings.Legend_Video, "relax"),
        new("game", Strings.Legend_Game, "game"),
        new("relax", Strings.Legend_VideoOrGame, "relax"),
        new("browse", Strings.Legend_Browse, "browse"),
        new("other", Strings.Legend_Other, "other"),
    ];

    private static StatsLegend[] CategoryLegend =>
    [
        new("Work", Strings.Category_Work, "work"),
        new("Communication", Strings.Category_Communication, "chat"),
        new("Video", Strings.Category_Video, "relax"),
        new("Game", Strings.Category_Game, "game"),
        new("Browsing", Strings.Category_Browsing, "browse"),
        new("Other", Strings.Category_Other, "other"),
    ];

    /// <param name="anchor">Any day in the range being shown.</param>
    /// <param name="today">Today on this computer; later days cannot be opened.</param>
    /// <param name="owner">Name of the member the report came from; null for my own stats.</param>
    /// <param name="targetOf">What a personal rule for a recorded app name would be saved against; null when ranking rows cannot be changed.</param>
    /// <param name="filter">Legend key to show only; ignored when the range has no such category.</param>
    public static StatsView Build(UsageReport report, StatsScale scale, StatsRankBy rankBy, DateOnly anchor, DateOnly today, string? owner = null, Func<string, StatsRuleTarget?>? targetOf = null, string? filter = null)
    {
        var range = StatsRange.Of(scale, anchor);
        var stateLegend = StateLegend;
        var legend = CategoryLegend
            .Concat(stateLegend.Select(s => new StatsLegend("hidden-" + s.Key, UiLanguage.Format(Strings.Legend_Hidden, s.Name), s.Tone, Hidden: true)))
            .ToList();
        string KeyOf(PresenceState state, ActivityCategory category) => CategoryKey(category) ?? "hidden-" + StateKey(state, category);

        var counted = report.Days.Where(d => UsageStates.Counts(d.State)).ToList();
        var used = counted.Select(d => KeyOf(d.State, d.Category))
            .Concat(report.Apps.Concat(report.Titles).Select(i => KeyOf(i.State, i.Category)))
            .ToHashSet();
        legend = legend.Where(l => used.Contains(l.Key)).ToList();
        var shownLegend = filter is null ? null : legend.FirstOrDefault(l => l.Key == filter);
        filter = shownLegend?.Key;
        bool Shown(string key) => filter is null || key == filter;

        var total = counted.Where(d => Shown(KeyOf(d.State, d.Category))).Sum(d => d.Seconds);
        var away = report.Days.Where(d => d.State == PresenceState.Away).Sum(d => d.Seconds);
        var locked = report.Days.Where(d => d.State == PresenceState.Locked).Sum(d => d.Seconds);

        var bars = scale == StatsScale.Day
            ? Enumerable.Range(0, 24).Select(hour =>
            {
                var parts = Parts(report.Hours.Where(h => h.Hour == hour), KeyOf, Shown);
                return new StatsBar(hour % 6 == 0 ? hour.ToString() : "", $"{hour}:00–{hour + 1}:00 · {Duration(parts.Sum(p => p.Seconds))}", null, parts);
            }).ToList()
            : Enumerable.Range(0, range.Days).Select(i =>
            {
                var date = range.From.AddDays(i);
                var parts = Parts(counted.Where(d => d.Date == date), KeyOf, Shown);
                var label = scale == StatsScale.Week ? PresenceFormatter.Weekday(date.DayOfWeek) : date.Day == 1 || date.Day % 5 == 0 ? date.Day.ToString() : "";
                var day = PresenceFormatter.DateWithWeekday(date, today);
                var tip = date > today ? UiLanguage.Format(Strings.Stats_NotYet, day) : $"{day} · {Duration(parts.Sum(p => p.Seconds))}";
                return new StatsBar(label, tip, date <= today ? date : null, parts);
            }).ToList();

        long barMax;
        string barMaxLabel;
        if (scale == StatsScale.Day)
        {
            barMax = 3600;
            barMaxLabel = Duration(barMax);
        }
        else
        {
            var hours = Math.Max(1, (long)Math.Ceiling(bars.Select(b => b.Parts.Sum(p => p.Seconds)).DefaultIfEmpty(0).Max() / 3600.0));
            barMax = hours * 3600;
            barMaxLabel = Duration(barMax);
        }

        var shownFrom = report.SharedDays is null ? range.From : Max(range.From, report.From);
        var shownTo = Min(range.To, today);
        var shownDays = Math.Max(0, shownTo.DayNumber - shownFrom.DayNumber + 1);
        var average = shownDays == 0 ? 0 : total / shownDays;

        var summary = new List<string>();
        if (scale != StatsScale.Day && total > 0)
        {
            summary.Add(UiLanguage.Format(Strings.Stats_Total, Duration(total)));
        }

        if (filter is null && away >= 60)
        {
            summary.Add(UiLanguage.Format(Strings.Stats_Away, Duration(away)));
        }

        if (filter is null && locked >= 60)
        {
            summary.Add(UiLanguage.Format(Strings.Stats_Locked, Duration(locked)));
        }

        var notes = new List<string>();
        if (owner is not null && report.SharedDays is { } shared)
        {
            notes.Add(UiLanguage.Format(Strings.Stats_SharedDays, owner, PresenceFormatter.Days(shared)));
        }

        var hiddenShown = rankBy switch
        {
            StatsRankBy.App => report.Apps.Any(a => a.App is null && Shown(KeyOf(a.State, a.Category))),
            StatsRankBy.Title => report.Titles.Any(t => t.App is null && Shown(KeyOf(t.State, t.Category))),
            _ => false,
        } || bars.Any(b => b.Parts.Any(p => p.Key.StartsWith("hidden-", StringComparison.Ordinal)));
        if (owner is not null && hiddenShown)
        {
            notes.Add(Strings.Stats_HiddenCategories);
        }

        if (owner is not null && rankBy == StatsRankBy.Title && report.Titles.Any(t => t.App is not null && t.Title is null && Shown(KeyOf(t.State, t.Category))))
        {
            notes.Add(Strings.Stats_HiddenTitles);
        }

        if (scale == StatsScale.Day && total > 0 && report.Timeline.Count == 0)
        {
            notes.Add(Strings.Stats_NoTimeline);
        }

        string? empty = null;
        if (counted.Sum(d => d.Seconds) == 0 && away == 0 && locked == 0)
        {
            empty = owner is null ? Strings.Stats_EmptyMine : Strings.Stats_EmptyOther;
        }

        var totalLabel = scale == StatsScale.Day ? Strings.Stats_UsageTime : Strings.Stats_DailyAverage;
        return new StatsView(
            scale,
            rankBy,
            filter,
            anchor,
            owner,
            Title(scale, range, today),
            shownLegend is null ? totalLabel : UiLanguage.Format(Strings.Stats_Of, totalLabel, shownLegend.Name),
            Duration(scale == StatsScale.Day ? total : average),
            filter is null ? Compare(report.PreviousSeconds, scale, total, average, Previous(scale, anchor), scale == StatsScale.Day && range.From == today) : null,
            summary.Count == 0 ? null : string.Join(" · ", summary),
            report.SharedDays is not { } days || range.From > today.AddDays(1 - days),
            range.To < today,
            legend,
            bars,
            barMax,
            barMaxLabel,
            scale == StatsScale.Day
                ? report.Timeline.Where(s => filter is null || (UsageStates.Counts(s.State) && KeyOf(s.State, s.Category) == filter)).Select(Span).ToList()
                : [],
            Ranking(report, rankBy, legend, stateLegend, KeyOf, Shown, owner is null ? targetOf : null),
            notes.Count == 0 ? null : string.Join(Strings.SentenceSeparator, notes),
            empty);
    }

    public static string Duration(long seconds) => PresenceFormatter.Duration(seconds);

    private static List<StatsPart> Parts(IEnumerable<UsageTotal> totals, Func<PresenceState, ActivityCategory, string> keyOf, Func<string, bool> shown) => totals
        .Where(t => UsageStates.Counts(t.State))
        .GroupBy(t => keyOf(t.State, t.Category))
        .Where(g => shown(g.Key))
        .Select(g => new StatsPart(g.Key, g.Sum(t => t.Seconds)))
        .Where(p => p.Seconds > 0)
        .ToList();

    private static List<StatsRankItem> Ranking(
        UsageReport report,
        StatsRankBy rankBy,
        IReadOnlyList<StatsLegend> legend,
        StatsLegend[] stateLegend,
        Func<PresenceState, ActivityCategory, string> keyOf,
        Func<string, bool> shown,
        Func<string, StatsRuleTarget?>? targetOf)
    {
        StatsLegend Legend(string key) => legend.FirstOrDefault(l => l.Key == key) ?? stateLegend[^1];
        StatsRuleTarget? Target(string? app, ActivityCategory category) =>
            app is null || targetOf?.Invoke(app) is not { } target ? null : target with { Category = category.ToString() };

        var rows = rankBy switch
        {
            StatsRankBy.Category => report.Days
                .Where(d => UsageStates.Counts(d.State))
                .GroupBy(d => keyOf(d.State, d.Category))
                .Where(g => shown(g.Key))
                .Select(g =>
                {
                    var l = Legend(g.Key);
                    return (Legend: l, Name: l.Name, Sub: (string?)null, Seconds: g.Sum(d => d.Seconds), Target: (StatsRuleTarget?)null, Filter: (string?)g.Key);
                })
                .ToList(),
            StatsRankBy.App => report.Apps
                .GroupBy(a => (Key: keyOf(a.State, a.Category), a.App))
                .Where(g => shown(g.Key.Key))
                .Select(g =>
                {
                    var l = Legend(g.Key.Key);
                    var app = AppNames.LocalizeOrNull(g.Key.App);
                    return (Legend: l, Name: app ?? l.Name, Sub: l.Hidden || app is null ? null : l.Name, Seconds: g.Sum(a => a.Seconds), Target: Target(g.Key.App, g.First().Category), Filter: (string?)null);
                })
                .ToList(),
            _ => report.Titles
                .GroupBy(t => (Key: keyOf(t.State, t.Category), t.App, t.Title))
                .Where(g => shown(g.Key.Key))
                .Select(g =>
                {
                    var l = Legend(g.Key.Key);
                    var app = AppNames.LocalizeOrNull(g.Key.App);
                    var name = g.Key.Title ?? app ?? l.Name;
                    string? sub = l.Hidden ? null : g.Key.Title is not null && app is not null ? $"{app} · {l.Name}" : app is null ? null : l.Name;
                    return (Legend: l, Name: name, Sub: sub, Seconds: g.Sum(t => t.Seconds), Target: Target(g.Key.App, g.First().Category), Filter: (string?)null);
                })
                .ToList(),
        };

        var top = rows.Where(r => r.Seconds >= 60).OrderByDescending(r => r.Seconds).Take(UsageAggregator.TopCount).ToList();
        var max = top.Count == 0 ? 1 : top[0].Seconds;
        return top.Select(r => new StatsRankItem(r.Name, r.Sub, r.Legend.Tone, Duration(r.Seconds), Math.Round((double)r.Seconds / max, 3), r.Legend.Hidden, r.Target, r.Filter)).ToList();
    }
    private static StatsSpanView Span(UsageSpan span)
    {
        var state = StateLegend.First(s => s.Key == StateKey(span.State, span.Category));
        var (name, tone) = span.State switch
        {
            PresenceState.Away => (Strings.State_Away, "away"),
            PresenceState.Locked => (Strings.State_Locked, "offline"),
            _ => (state.Name, state.Tone),
        };
        return new StatsSpanView(span.Start, span.End, tone, $"{Clock(span.Start)}–{Clock(span.End)} {name} · {Duration(span.End - span.Start)}");
    }

    /// <param name="today">Today is not over yet and is not compared with anything.</param>
    private static string? Compare(long? previous, StatsScale scale, long total, long average, StatsRange previousRange, bool today)
    {
        // Nothing recorded before (new install, another computer) cannot be told apart from a day off, so say nothing.
        if (today || previous is not { } before || before == 0)
        {
            return null;
        }

        var (same, more, less, diff) = scale switch
        {
            StatsScale.Day => (Strings.Compare_DaySame, Strings.Compare_DayMore, Strings.Compare_DayLess, total - before),
            StatsScale.Week => (Strings.Compare_WeekSame, Strings.Compare_WeekMore, Strings.Compare_WeekLess, average - before / previousRange.Days),
            _ => (Strings.Compare_MonthSame, Strings.Compare_MonthMore, Strings.Compare_MonthLess, average - before / previousRange.Days),
        };
        return Math.Abs(diff) < 60 ? same : UiLanguage.Format(diff > 0 ? more : less, Duration(Math.Abs(diff)));
    }

    private static StatsRange Previous(StatsScale scale, DateOnly anchor) => StatsRange.Previous(scale, anchor);

    private static string Title(StatsScale scale, StatsRange range, DateOnly today)
    {
        var current = range.From <= today && today <= range.To;
        switch (scale)
        {
            case StatsScale.Week:
            {
                var text = $"{PresenceFormatter.Date(range.From, today)} – {PresenceFormatter.Date(range.To, today)}";
                return current ? UiLanguage.Format(Strings.Stats_ThisWeek, text) : text;
            }
            case StatsScale.Month:
            {
                var text = UiLanguage.Format(Strings.Date_YearMonth, range.From);
                return current ? UiLanguage.Format(Strings.Stats_ThisMonth, text) : text;
            }
            default:
            {
                var text = PresenceFormatter.DateWithWeekday(range.From, today);
                return range.From == today ? UiLanguage.Format(Strings.Stats_TodayTitle, text)
                    : range.From == today.AddDays(-1) ? UiLanguage.Format(Strings.Stats_YesterdayTitle, text)
                    : text;
            }
        }
    }

    private static string Clock(int seconds) => $"{seconds / 3600}:{seconds % 3600 / 60:00}";

    private static string StateKey(PresenceState state, ActivityCategory category) => state switch
    {
        PresenceState.Working or PresenceState.Meeting => "work",
        PresenceState.Communicating => "chat",
        PresenceState.Slacking when category == ActivityCategory.Music => "other",
        PresenceState.Slacking when category.IsRetired() => "browse",
        PresenceState.Slacking when category == ActivityCategory.Video => "video",
        PresenceState.Slacking when category == ActivityCategory.Game => "game",
        PresenceState.Slacking => "relax",
        PresenceState.Browsing => "browse",
        _ => "other",
    };

    private static string? CategoryKey(ActivityCategory category) => category switch
    {
        ActivityCategory.Work or ActivityCategory.Meeting => "Work",
        ActivityCategory.Communication => "Communication",
        ActivityCategory.Video => "Video",
        ActivityCategory.Game => "Game",
        ActivityCategory.Browsing or ActivityCategory.Shopping or ActivityCategory.Social or ActivityCategory.News or ActivityCategory.Reading => "Browsing",
        ActivityCategory.Music or ActivityCategory.Other => "Other",
        _ => null,
    };

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}