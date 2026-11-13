using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Presence;
using DeskPal.Shared.Stats;

namespace DeskPal.Client.Core.Ui;

/// <summary>Turns a usage report into chart data and text for the stats page.</summary>
public static class StatsViewBuilder
{
    private static StatsLegend[] StateLegend =>
    [
        new("work", Strings.Legend_Work, "work"),
        new("chat", Strings.Legend_Chat, "chat"),
        new("relax", Strings.Legend_Relax, "relax"),
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
    public static StatsView Build(UsageReport report, StatsScale scale, StatsGranularity granularity, DateOnly anchor, DateOnly today, string? owner = null)
    {
        var range = StatsRange.Of(scale, anchor);
        var stateLegend = StateLegend;
        var legend = (granularity == StatsGranularity.State ? stateLegend : CategoryLegend)
            .Concat(stateLegend.Select(s => new StatsLegend("hidden-" + s.Key, UiLanguage.Format(Strings.Legend_Hidden, s.Name), s.Tone, Hidden: true)))
            .ToList();
        string KeyOf(PresenceState state, ActivityCategory category) =>
            granularity == StatsGranularity.State ? StateKey(state, category) : CategoryKey(category) ?? "hidden-" + StateKey(state, category);

        var counted = report.Days.Where(d => UsageStates.Counts(d.State)).ToList();
        var total = counted.Sum(d => d.Seconds);
        var away = report.Days.Where(d => d.State == PresenceState.Away).Sum(d => d.Seconds);
        var locked = report.Days.Where(d => d.State == PresenceState.Locked).Sum(d => d.Seconds);

        var bars = scale == StatsScale.Day
            ? Enumerable.Range(0, 24).Select(hour =>
            {
                var parts = Parts(report.Hours.Where(h => h.Hour == hour), KeyOf);
                return new StatsBar(hour % 6 == 0 ? hour.ToString() : "", $"{hour}:00–{hour + 1}:00 · {Duration(parts.Sum(p => p.Seconds))}", null, parts);
            }).ToList()
            : Enumerable.Range(0, range.Days).Select(i =>
            {
                var date = range.From.AddDays(i);
                var parts = Parts(counted.Where(d => d.Date == date), KeyOf);
                var label = scale == StatsScale.Week ? PresenceFormatter.Weekday(date.DayOfWeek) : date.Day == 1 || date.Day % 5 == 0 ? date.Day.ToString() : "";
                var day = PresenceFormatter.DateWithWeekday(date, today);
                var tip = date > today ? UiLanguage.Format(Strings.Stats_NotYet, day) : $"{day} · {Duration(parts.Sum(p => p.Seconds))}";
                return new StatsBar(label, tip, date <= today ? date : null, parts);
            }).ToList();

        var used = bars.SelectMany(b => b.Parts).Select(p => p.Key)
            .Concat(granularity == StatsGranularity.State ? [] : report.Apps.Concat(report.Titles).Select(i => KeyOf(i.State, i.Category)))
            .ToHashSet();
        legend = legend.Where(l => used.Contains(l.Key)).ToList();

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

        if (away >= 60)
        {
            summary.Add(UiLanguage.Format(Strings.Stats_Away, Duration(away)));
        }

        if (locked >= 60)
        {
            summary.Add(UiLanguage.Format(Strings.Stats_Locked, Duration(locked)));
        }

        var notes = new List<string>();
        if (owner is not null && report.SharedDays is { } shared)
        {
            notes.Add(UiLanguage.Format(Strings.Stats_SharedDays, owner, shared));
        }

        var hiddenShown = granularity switch
        {
            StatsGranularity.Category => report.Apps.Any(a => a.Category == ActivityCategory.None),
            StatsGranularity.Title => report.Titles.Any(t => t.Category == ActivityCategory.None),
            _ => false,
        } || bars.Any(b => b.Parts.Any(p => p.Key.StartsWith("hidden-", StringComparison.Ordinal)));
        if (owner is not null && hiddenShown)
        {
            notes.Add(Strings.Stats_HiddenCategories);
        }

        if (owner is not null && granularity == StatsGranularity.Title && report.Titles.Any(t => t.Category != ActivityCategory.None && t.Title is null))
        {
            notes.Add(Strings.Stats_HiddenTitles);
        }

        if (scale == StatsScale.Day && total > 0 && report.Timeline.Count == 0)
        {
            notes.Add(Strings.Stats_NoTimeline);
        }

        string? empty = null;
        if (total == 0 && away == 0 && locked == 0)
        {
            empty = owner is null ? Strings.Stats_EmptyMine : Strings.Stats_EmptyOther;
        }

        return new StatsView(
            scale,
            granularity,
            anchor,
            owner,
            Title(scale, range, today),
            scale == StatsScale.Day ? Strings.Stats_UsageTime : Strings.Stats_DailyAverage,
            Duration(scale == StatsScale.Day ? total : average),
            Compare(report.PreviousSeconds, scale, total, average, Previous(scale, anchor), scale == StatsScale.Day && range.From == today),
            summary.Count == 0 ? null : string.Join(" · ", summary),
            report.SharedDays is not { } days || range.From > today.AddDays(1 - days),
            range.To < today,
            legend,
            bars,
            barMax,
            barMaxLabel,
            scale == StatsScale.Day ? report.Timeline.Select(Span).ToList() : [],
            Ranking(report, granularity, legend, stateLegend, KeyOf),
            granularity switch
            {
                StatsGranularity.State => Strings.Stats_ByState,
                StatsGranularity.Category => Strings.Stats_TopApps,
                _ => Strings.Stats_TopTitles,
            },
            notes.Count == 0 ? null : string.Join(Strings.SentenceSeparator, notes),
            empty);
    }

    public static string Duration(long seconds) => PresenceFormatter.Duration(seconds);

    private static List<StatsPart> Parts(IEnumerable<UsageTotal> totals, Func<PresenceState, ActivityCategory, string> keyOf) => totals
        .Where(t => UsageStates.Counts(t.State))
        .GroupBy(t => keyOf(t.State, t.Category))
        .Select(g => new StatsPart(g.Key, g.Sum(t => t.Seconds)))
        .Where(p => p.Seconds > 0)
        .ToList();

    private static List<StatsRankItem> Ranking(UsageReport report, StatsGranularity granularity, IReadOnlyList<StatsLegend> legend, StatsLegend[] stateLegend, Func<PresenceState, ActivityCategory, string> keyOf)
    {
        StatsLegend Legend(string key) => legend.FirstOrDefault(l => l.Key == key) ?? stateLegend[^1];

        var rows = granularity switch
        {
            StatsGranularity.State => report.Days
                .Where(d => UsageStates.Counts(d.State))
                .GroupBy(d => StateKey(d.State, d.Category))
                .Select(g => (Legend: stateLegend.First(s => s.Key == g.Key), Name: stateLegend.First(s => s.Key == g.Key).Name, Sub: (string?)null, Seconds: g.Sum(d => d.Seconds)))
                .ToList(),
            StatsGranularity.Category => report.Apps
                .GroupBy(a => (Key: keyOf(a.State, a.Category), a.App))
                .Select(g =>
                {
                    var l = Legend(g.Key.Key);
                    return (Legend: l, Name: g.Key.App ?? l.Name, Sub: l.Hidden || g.Key.App is null ? null : l.Name, Seconds: g.Sum(a => a.Seconds));
                })
                .ToList(),
            _ => report.Titles
                .GroupBy(t => (Key: keyOf(t.State, t.Category), t.App, t.Title))
                .Select(g =>
                {
                    var l = Legend(g.Key.Key);
                    var name = g.Key.Title ?? g.Key.App ?? l.Name;
                    string? sub = l.Hidden ? null : g.Key.Title is not null && g.Key.App is not null ? $"{g.Key.App} · {l.Name}" : g.Key.App is null ? null : l.Name;
                    return (Legend: l, Name: name, Sub: sub, Seconds: g.Sum(t => t.Seconds));
                })
                .ToList(),
        };

        var top = rows.Where(r => r.Seconds >= 60).OrderByDescending(r => r.Seconds).Take(UsageAggregator.TopCount).ToList();
        var max = top.Count == 0 ? 1 : top[0].Seconds;
        return top.Select(r => new StatsRankItem(r.Name, r.Sub, r.Legend.Tone, Duration(r.Seconds), Math.Round((double)r.Seconds / max, 3), r.Legend.Hidden)).ToList();
    }

    private static StatsSpanView Span(UsageSpan span)
    {
        var state = StateLegend.First(s => s.Key == StateKey(span.State, ActivityCategory.None));
        var (name, tone) = span.State switch
        {
            PresenceState.Away => (Strings.State_Away, "away"),
            PresenceState.Locked => (Strings.State_Locked, "offline"),
            _ => (state.Name, state.Tone),
        };
        return new StatsSpanView(span.Start, span.End, tone, $"{Clock(span.Start)}–{Clock(span.End)} {name} · {Duration(span.End - span.Start)}");
    }

    /// <param name="today">Today is not over yet, so it is not compared with a whole day.</param>
    private static string? Compare(long? previous, StatsScale scale, long total, long average, StatsRange previousRange, bool today)
    {
        // Nothing recorded before (new install, another computer) cannot be told apart from a day off, so say nothing.
        if (previous is not { } before || before == 0)
        {
            return null;
        }

        if (today)
        {
            return UiLanguage.Format(Strings.Stats_YesterdayTotal, Duration(before));
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