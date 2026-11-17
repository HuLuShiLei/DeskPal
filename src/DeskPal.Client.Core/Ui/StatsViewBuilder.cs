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

    internal static StatsLegend[] CategoryLegend =>
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
    /// <summary>Category changes smaller than this are not listed.</summary>
    private const long MinChangeSeconds = 5 * 60;

    private const int MaxChanges = 3;

    private static readonly DayOfWeek[] WeekFromMonday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <param name="owner">Name of the member the report came from; null for my own stats, whose ranking rows open the detail page.</param>
    /// <param name="filter">Legend key to show only; ignored when the range has no such category.</param>
    /// <param name="today">Today of whoever the stats belong to.</param>
    /// <param name="myOffset">My UTC offset now in minutes, to say whose local time the page is in; null says nothing.</param>
    public static StatsView Build(UsageReport report, StatsScale scale, StatsRankBy rankBy, DateOnly anchor, DateOnly today, string? owner = null, string? filter = null, int? myOffset = null)
    {
        var range = StatsRange.Of(scale, anchor);
        var stateLegend = StateLegend;
        var fullLegend = CategoryLegend
            .Concat(stateLegend.Select(s => new StatsLegend("hidden-" + s.Key, UiLanguage.Format(Strings.Legend_Hidden, s.Name), s.Tone, Hidden: true)))
            .ToList();
        string KeyOf(PresenceState state, ActivityCategory category) => CategoryKey(category) ?? "hidden-" + StateKey(state, category);

        var counted = report.Days.Where(d => UsageStates.Counts(d.State)).ToList();
        var used = counted.Select(d => KeyOf(d.State, d.Category))
            .Concat(report.Apps.Concat(report.Titles).Select(i => KeyOf(i.State, i.Category)))
            .ToHashSet();
        var legend = fullLegend.Where(l => used.Contains(l.Key)).ToList();
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

        if (myOffset is { } mine && Zone(report, owner, mine) is { } zone)
        {
            notes.Add(zone);
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

        var previousRange = Previous(scale, anchor);
        var compare = filter is null ? Compare(report.Previous?.Sum(p => p.Seconds), scale, total, average, previousRange, scale == StatsScale.Day && range.From == today) : null;
        var changes = compare is null || report.Previous is not { } previous
            ? []
            : Changes(counted, previous, scale, shownDays, previousRange.Days, KeyOf, fullLegend);

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
            compare,
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
            Ranking(report, rankBy, legend, stateLegend, KeyOf, Shown, mine: owner is null),
            notes.Count == 0 ? null : string.Join(Strings.SentenceSeparator, notes),
            empty,
            changes,
            filter is null ? Bounds(report.Bounds, scale, range) : null,
            scale == StatsScale.Day ? null : Rhythm(report.WeekHours, scale, shownFrom, shownDays, KeyOf, Shown, shownLegend?.Tone));
    }

    public static string Duration(long seconds) => PresenceFormatter.Duration(seconds);

    /// <summary>
    /// Whose local time the page is in. Always said for a member; for my own stats only when part of the range was
    /// recorded in another time zone.
    /// </summary>
    private static string? Zone(UsageReport report, string? owner, int myOffset)
    {
        var recorded = report.Offsets ?? [];
        if (owner is null)
        {
            var other = recorded.Where(o => o != myOffset).ToList();
            return other.Count == 0 ? null : UiLanguage.Format(Strings.Stats_MyOtherZones, string.Join(Strings.ListSeparator, other.Select(Utc)));
        }

        var current = report.Offset;
        var offsets = recorded.Count > 0 ? recorded : current is { } c ? [c] : [];
        if (offsets.Count == 0)
        {
            return null;
        }

        var text = offsets.Count == 1 ? UiLanguage.Format(Strings.Stats_ZoneCompared, Utc(offsets[0]), Relative(offsets[0] - myOffset)) : string.Join(Strings.ListSeparator, offsets.Select(Utc));
        return UiLanguage.Format(offsets.All(o => o == current) ? Strings.Stats_PeerZone : Strings.Stats_PeerRecordedZone, owner, text);
    }

    /// <summary>"UTC+8", "UTC−5", "UTC+5:30".</summary>
    internal static string Utc(int minutes)
    {
        var sign = minutes < 0 ? "\u2212" : "+";
        var abs = Math.Abs(minutes);
        return abs % 60 == 0 ? $"UTC{sign}{abs / 60}" : $"UTC{sign}{abs / 60}:{abs % 60:00}";
    }

    private static string Relative(int minutes) => minutes == 0
        ? Strings.Zone_SameAsMine
        : UiLanguage.Format(minutes > 0 ? Strings.Zone_AheadOfMe : Strings.Zone_BehindMe, Duration(Math.Abs(minutes) * 60L));

    /// <summary>Differences per category, as daily averages for a week or month like the total.</summary>
    private static List<StatsChange> Changes(
        IEnumerable<UsageTotal> current,
        IEnumerable<UsageTotal> previous,
        StatsScale scale,
        int days,
        int previousDays,
        Func<PresenceState, ActivityCategory, string> keyOf,
        IReadOnlyList<StatsLegend> legend)
    {
        Dictionary<string, long> PerKey(IEnumerable<UsageTotal> totals, int d) => totals
            .Where(t => UsageStates.Counts(t.State))
            .GroupBy(t => keyOf(t.State, t.Category))
            .ToDictionary(g => g.Key, g => scale == StatsScale.Day ? g.Sum(t => t.Seconds) : g.Sum(t => t.Seconds) / Math.Max(1, d));

        var now = PerKey(current, days);
        var before = PerKey(previous, previousDays);
        return now.Keys.Union(before.Keys)
            .Select(key => (Key: key, Diff: now.GetValueOrDefault(key) - before.GetValueOrDefault(key), Legend: legend.FirstOrDefault(l => l.Key == key)))
            .Where(c => c.Legend is not null && Math.Abs(c.Diff) >= MinChangeSeconds)
            .OrderByDescending(c => Math.Abs(c.Diff))
            .Take(MaxChanges)
            .Select(c => new StatsChange(c.Key, c.Legend!.Name, c.Legend.Tone, (c.Diff > 0 ? "+" : "\u2212") + Duration(Math.Abs(c.Diff)), c.Legend.Hidden))
            .ToList();
    }

    private static string? Bounds(IReadOnlyList<UsageDayBounds>? bounds, StatsScale scale, StatsRange range)
    {
        var days = bounds?.Where(b => b.Date >= range.From && b.Date <= range.To).ToList();
        if (days is not { Count: > 0 })
        {
            return null;
        }

        return scale == StatsScale.Day
            ? UiLanguage.Format(Strings.Stats_Bounds, DayClock(days[0].First), DayClock(days[0].Last))
            : UiLanguage.Format(Strings.Stats_AverageBounds, DayClock((int)days.Average(b => b.First)), DayClock((int)days.Average(b => b.Last)));
    }

    /// <summary>Time of day; after midnight it says it is the next day.</summary>
    private static string DayClock(int seconds) => seconds >= 86400 ? UiLanguage.Format(Strings.Stats_NextDay, Clock(seconds - 86400)) : Clock(seconds);

    /// <param name="from">First day the range shows; with <paramref name="days"/>, how many of each weekday a month has so far.</param>
    private static StatsRhythm? Rhythm(
        IReadOnlyList<UsageWeekHour>? weekHours,
        StatsScale scale,
        DateOnly from,
        int days,
        Func<PresenceState, ActivityCategory, string> keyOf,
        Func<string, bool> shown,
        string? tone)
    {
        if (weekHours is not { Count: > 0 })
        {
            return null;
        }

        var weekdays = Enumerable.Range(0, days).Select(i => from.AddDays(i).DayOfWeek).GroupBy(d => d).ToDictionary(g => g.Key, g => g.Count());
        var seconds = weekHours
            .Where(w => UsageStates.Counts(w.State) && shown(keyOf(w.State, w.Category)))
            .GroupBy(w => (w.Day, w.Hour))
            .ToDictionary(g => g.Key, g => g.Sum(w => w.Seconds));
        var tip = scale == StatsScale.Week ? Strings.Stats_RhythmTip : Strings.Stats_RhythmAverageTip;
        var rows = WeekFromMonday.Select(day =>
        {
            var count = weekdays.GetValueOrDefault(day);
            var name = PresenceFormatter.Weekday(day);
            var cells = Enumerable.Range(0, 24).Select(hour =>
            {
                var s = count == 0 ? 0 : seconds.GetValueOrDefault((day, hour)) / count;
                return new StatsRhythmCell(s, UiLanguage.Format(tip, name, hour, hour + 1, Duration(s)));
            }).ToList();
            return new StatsRhythmRow(UiLanguage.Format(Strings.Stats_RhythmDay, name), cells);
        }).ToList();
        return new StatsRhythm(rows, tone, rows.SelectMany(r => r.Cells).Select(c => c.Seconds).DefaultIfEmpty(0).Max());
    }

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
        bool mine)
    {
        StatsLegend Legend(string key) => legend.FirstOrDefault(l => l.Key == key) ?? stateLegend[^1];
        StatsDetailKey? Detail(string? app, string? title) => mine && app is not null ? new StatsDetailKey(app, title) : null;

        var rows = rankBy switch
        {
            StatsRankBy.Category => report.Days
                .Where(d => UsageStates.Counts(d.State))
                .GroupBy(d => keyOf(d.State, d.Category))
                .Where(g => shown(g.Key))
                .Select(g =>
                {
                    var l = Legend(g.Key);
                    return (Legend: l, Name: l.Name, Sub: (string?)null, Seconds: g.Sum(d => d.Seconds), Detail: (StatsDetailKey?)null, Filter: (string?)g.Key);
                })
                .ToList(),
            StatsRankBy.App => report.Apps
                .GroupBy(a => (Key: keyOf(a.State, a.Category), a.App))
                .Where(g => shown(g.Key.Key))
                .Select(g =>
                {
                    var l = Legend(g.Key.Key);
                    var app = AppNames.LocalizeOrNull(g.Key.App);
                    return (Legend: l, Name: app ?? l.Name, Sub: l.Hidden || app is null ? null : l.Name, Seconds: g.Sum(a => a.Seconds), Detail: Detail(g.Key.App, null), Filter: (string?)null);
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
                    return (Legend: l, Name: name, Sub: sub, Seconds: g.Sum(t => t.Seconds), Detail: Detail(g.Key.App, g.Key.Title), Filter: (string?)null);
                })
                .ToList(),
        };

        var top = rows.Where(r => r.Seconds >= 60).OrderByDescending(r => r.Seconds).Take(UsageAggregator.TopCount).ToList();
        var max = top.Count == 0 ? 1 : top[0].Seconds;
        return top.Select(r => new StatsRankItem(r.Name, r.Sub, r.Legend.Tone, Duration(r.Seconds), Math.Round((double)r.Seconds / max, 3), r.Legend.Hidden, r.Detail, r.Filter)).ToList();
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

    internal static string Title(StatsScale scale, StatsRange range, DateOnly today)
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

    internal static string Clock(int seconds) => $"{seconds / 3600}:{seconds % 3600 / 60:00}";

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

    internal static string? CategoryKey(ActivityCategory category) => category switch
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