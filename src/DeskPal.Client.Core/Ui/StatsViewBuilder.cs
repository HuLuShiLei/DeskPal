using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Presence;
using DeskPal.Shared.Stats;

namespace DeskPal.Client.Core.Ui;

/// <summary>Turns a usage report into chart data and text for the stats page.</summary>
public static class StatsViewBuilder
{
    private static readonly StatsLegend[] StateLegend =
    [
        new("work", "工作", "work"),
        new("chat", "聊天", "chat"),
        new("relax", "放松", "relax"),
        new("browse", "浏览网页", "browse"),
        new("other", "其他", "other"),
    ];

    private static readonly StatsLegend[] CategoryLegend =
    [
        new("Work", "工作", "work"),
        new("Communication", "聊天", "chat"),
        new("Video", "视频", "relax"),
        new("Game", "游戏", "game"),
        new("Browsing", "浏览网页", "browse"),
        new("Other", "其他", "other"),
    ];

    private static readonly string[] Weekdays = ["日", "一", "二", "三", "四", "五", "六"];

    /// <param name="anchor">Any day in the range being shown.</param>
    /// <param name="today">Today on this computer; later days cannot be opened.</param>
    /// <param name="owner">Name of the member the report came from; null for my own stats.</param>
    public static StatsView Build(UsageReport report, StatsScale scale, StatsGranularity granularity, DateOnly anchor, DateOnly today, string? owner = null)
    {
        var range = StatsRange.Of(scale, anchor);
        var legend = (granularity == StatsGranularity.State ? StateLegend : CategoryLegend)
            .Concat(StateLegend.Select(s => new StatsLegend("hidden-" + s.Key, $"{s.Name}（未公开类别）", s.Tone, Hidden: true)))
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
                var label = scale == StatsScale.Week ? Weekdays[(int)date.DayOfWeek] : date.Day == 1 || date.Day % 5 == 0 ? date.Day.ToString() : "";
                var tip = date > today ? $"{DayName(date)} · 还没到" : $"{DayName(date)} · {Duration(parts.Sum(p => p.Seconds))}";
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
            barMaxLabel = "1 小时";
        }
        else
        {
            var hours = Math.Max(1, (long)Math.Ceiling(bars.Select(b => b.Parts.Sum(p => p.Seconds)).DefaultIfEmpty(0).Max() / 3600.0));
            barMax = hours * 3600;
            barMaxLabel = $"{hours} 小时";
        }

        var shownFrom = report.SharedDays is null ? range.From : Max(range.From, report.From);
        var shownTo = Min(range.To, today);
        var shownDays = Math.Max(0, shownTo.DayNumber - shownFrom.DayNumber + 1);
        var average = shownDays == 0 ? 0 : total / shownDays;

        var summary = new List<string>();
        if (scale != StatsScale.Day && total > 0)
        {
            summary.Add($"共 {Duration(total)}");
        }

        if (away >= 60)
        {
            summary.Add($"暂离 {Duration(away)}");
        }

        if (locked >= 60)
        {
            summary.Add($"锁屏 {Duration(locked)}");
        }

        var notes = new List<string>();
        if (owner is not null && report.SharedDays is { } shared)
        {
            notes.Add($"{owner} 公开了最近 {shared} 天的统计");
        }

        var hiddenShown = granularity switch
        {
            StatsGranularity.Category => report.Apps.Any(a => a.Category == ActivityCategory.None),
            StatsGranularity.Title => report.Titles.Any(t => t.Category == ActivityCategory.None),
            _ => false,
        } || bars.Any(b => b.Parts.Any(p => p.Key.StartsWith("hidden-", StringComparison.Ordinal)));
        if (owner is not null && hiddenShown)
        {
            notes.Add("部分类别对方只公开了状态");
        }

        if (owner is not null && granularity == StatsGranularity.Title && report.Titles.Any(t => t.Category != ActivityCategory.None && t.Title is null))
        {
            notes.Add("部分类别对方没有公开标题，显示为应用名");
        }

        if (scale == StatsScale.Day && total > 0 && report.Timeline.Count == 0)
        {
            notes.Add("这一天超过了保留天数，只有按天汇总的时长，没有时间轴");
        }

        string? empty = null;
        if (total == 0 && away == 0 && locked == 0)
        {
            empty = owner is null ? "这段时间没有记录。DeskPal 运行时才会记录使用时间。" : "对方这段时间没有记录。";
        }

        return new StatsView(
            scale,
            granularity,
            anchor,
            owner,
            Title(scale, range, today),
            scale == StatsScale.Day ? "使用时长" : "日均使用时长",
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
            Ranking(report, granularity, legend, KeyOf),
            granularity switch
            {
                StatsGranularity.State => "按状态",
                StatsGranularity.Category => "使用最多的应用和网站",
                _ => "使用最多的标题",
            },
            notes.Count == 0 ? null : string.Join("。", notes) + "。",
            empty);
    }

    public static string Duration(long seconds) => seconds switch
    {
        <= 0 => "0 分钟",
        < 60 => "不到 1 分钟",
        < 3600 => $"{seconds / 60} 分钟",
        _ => seconds % 3600 / 60 == 0 ? $"{seconds / 3600} 小时" : $"{seconds / 3600} 小时 {seconds % 3600 / 60} 分钟",
    };

    private static List<StatsPart> Parts(IEnumerable<UsageTotal> totals, Func<PresenceState, ActivityCategory, string> keyOf) => totals
        .Where(t => UsageStates.Counts(t.State))
        .GroupBy(t => keyOf(t.State, t.Category))
        .Select(g => new StatsPart(g.Key, g.Sum(t => t.Seconds)))
        .Where(p => p.Seconds > 0)
        .ToList();

    private static List<StatsRankItem> Ranking(UsageReport report, StatsGranularity granularity, IReadOnlyList<StatsLegend> legend, Func<PresenceState, ActivityCategory, string> keyOf)
    {
        StatsLegend Legend(string key) => legend.FirstOrDefault(l => l.Key == key) ?? StateLegend[^1];

        var rows = granularity switch
        {
            StatsGranularity.State => report.Days
                .Where(d => UsageStates.Counts(d.State))
                .GroupBy(d => StateKey(d.State, d.Category))
                .Select(g => (Legend: StateLegend.First(s => s.Key == g.Key), Name: StateLegend.First(s => s.Key == g.Key).Name, Sub: (string?)null, Seconds: g.Sum(d => d.Seconds)))
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
            PresenceState.Away => ("暂离", "away"),
            PresenceState.Locked => ("已锁屏", "offline"),
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
            return $"昨天全天 {Duration(before)}";
        }

        var (label, diff) = scale switch
        {
            StatsScale.Day => ("前一天", total - before),
            StatsScale.Week => ("上周日均", average - before / previousRange.Days),
            _ => ("上月日均", average - before / previousRange.Days),
        };
        return Math.Abs(diff) < 60 ? $"和{label}差不多" : $"比{label}{(diff > 0 ? "多" : "少")} {Duration(Math.Abs(diff))}";
    }

    private static StatsRange Previous(StatsScale scale, DateOnly anchor) => StatsRange.Previous(scale, anchor);

    private static string Title(StatsScale scale, StatsRange range, DateOnly today)
    {
        var current = range.From <= today && today <= range.To;
        return scale switch
        {
            StatsScale.Week => $"{DayName(range.From, today, weekday: false)} – {DayName(range.To, today, weekday: false)}{(current ? "（本周）" : "")}",
            StatsScale.Month => $"{range.From.Year} 年 {range.From.Month} 月{(current ? "（本月）" : "")}",
            _ => DayName(range.From, today) + (range.From == today ? "（今天）" : range.From == today.AddDays(-1) ? "（昨天）" : ""),
        };
    }

    private static string DayName(DateOnly date) => $"{date.Month} 月 {date.Day} 日 星期{Weekdays[(int)date.DayOfWeek]}";

    private static string DayName(DateOnly date, DateOnly today, bool weekday = true)
    {
        var text = $"{date.Month} 月 {date.Day} 日";
        if (date.Year != today.Year)
        {
            text = $"{date.Year} 年 " + text;
        }

        return weekday ? $"{text} 星期{Weekdays[(int)date.DayOfWeek]}" : text;
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