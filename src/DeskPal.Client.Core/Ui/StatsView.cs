namespace DeskPal.Client.Core.Ui;

public enum StatsScale
{
    Day,
    Week,
    Month,
}

/// <summary>What the ranking lists. The chart is always colored by category.</summary>
public enum StatsRankBy
{
    Category,
    App,
    Title,
}

/// <summary>Local days shown on one page of the stats: a day, a week from Monday, or a calendar month.</summary>
public sealed record StatsRange(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public static StatsRange Of(StatsScale scale, DateOnly anchor) => scale switch
    {
        StatsScale.Week => new StatsRange(anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)), anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7) + 6)),
        StatsScale.Month => new StatsRange(new DateOnly(anchor.Year, anchor.Month, 1), new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month))),
        _ => new StatsRange(anchor, anchor),
    };

    public static StatsRange Previous(StatsScale scale, DateOnly anchor) => scale switch
    {
        StatsScale.Week => Of(scale, anchor.AddDays(-7)),
        StatsScale.Month => Of(scale, anchor.AddMonths(-1)),
        _ => Of(scale, anchor.AddDays(-1)),
    };
}

/// <summary>One page of the stats, ready to draw. Built on demand by stats.query, not pushed with the panel state.</summary>
/// <param name="Owner">Name of the member whose stats these are; null for my own.</param>
/// <param name="Total">Usage of the day, or the daily average of a week or month.</param>
/// <param name="Compare">Difference from the period before, e.g. "比前一天多 32 分钟".</param>
/// <param name="Summary">Sum and the time not counted, e.g. "共 21 小时 · 暂离 2 小时 · 锁屏 10 小时".</param>
/// <param name="BarMax">Seconds at the top of the bar chart.</param>
/// <param name="Timeline">State spans of a single day, in seconds since local midnight; empty for a week or month.</param>
/// <param name="Filter">Legend key the totals, charts and ranking are limited to; null shows everything.</param>
/// <param name="Note">Shown above the charts, e.g. how far back a member shares.</param>
/// <param name="Empty">Shown instead of the charts when nothing was recorded.</param>
/// <param name="Changes">Categories that changed the most from the period before, shown under <paramref name="Compare"/>.</param>
/// <param name="Bounds">First and last use, e.g. "首次活动 8:52 · 最后活动 23:40"; averaged over a week or month. Only for my own stats.</param>
/// <param name="Rhythm">Usage per weekday and hour; only for a week or month of my own stats.</param>
public sealed record StatsView(
    StatsScale Scale,
    StatsRankBy RankBy,
    string? Filter,
    DateOnly Anchor,
    string? Owner,
    string Title,
    string TotalLabel,
    string Total,
    string? Compare,
    string? Summary,
    bool CanPrev,
    bool CanNext,
    IReadOnlyList<StatsLegend> Legend,
    IReadOnlyList<StatsBar> Bars,
    long BarMax,
    string BarMaxLabel,
    IReadOnlyList<StatsSpanView> Timeline,
    IReadOnlyList<StatsRankItem> Ranking,
    string? Note,
    string? Empty,
    IReadOnlyList<StatsChange> Changes,
    string? Bounds,
    StatsRhythm? Rhythm);

/// <param name="Text">Signed difference, e.g. "+1 小时 5 分钟".</param>
public sealed record StatsChange(string Key, string Name, string Tone, string Text, bool Hidden = false);

/// <summary>Rows from Monday to Sunday, 24 cells each.</summary>
/// <param name="Tone">Tone of the category shown alone; null for all usage.</param>
/// <param name="Max">Seconds of the darkest cell.</param>
public sealed record StatsRhythm(IReadOnlyList<StatsRhythmRow> Rows, string? Tone, long Max);

public sealed record StatsRhythmRow(string Label, IReadOnlyList<StatsRhythmCell> Cells);

/// <param name="Seconds">Seconds in this hour of this weekday; the average per such weekday over a month.</param>
public sealed record StatsRhythmCell(long Seconds, string Tip);

/// <param name="Hidden">Time of categories the member does not show; drawn hatched.</param>
public sealed record StatsLegend(string Key, string Name, string Tone, bool Hidden = false);

/// <param name="Label">Axis label; empty when the axis skips this bar.</param>
/// <param name="Date">Day to open in the day view when the bar is clicked; null for hours.</param>
public sealed record StatsBar(string Label, string Tip, DateOnly? Date, IReadOnlyList<StatsPart> Parts);

public sealed record StatsPart(string Key, long Seconds);

public sealed record StatsSpanView(int Start, int End, string Tone, string Tip);

/// <param name="Share">Width of the bar, 0 to 1, relative to the first item.</param>
/// <param name="Detail">Set on my own app and title rows; clicking opens the detail page with stats.app.</param>
/// <param name="Filter">Set on category rows: the legend key to show only that category.</param>
public sealed record StatsRankItem(string Name, string? Sub, string Tone, string Time, double Share, bool Hidden = false, StatsDetailKey? Detail = null, string? Filter = null);

/// <param name="App">App name as recorded.</param>
/// <param name="Title">Only this title of the app; null for the whole app.</param>
public sealed record StatsDetailKey(string App, string? Title = null);

/// <summary>Detail page of one app, site or title over the range of the stats page. Built on demand by stats.app.</summary>
/// <param name="Sub">Category, or app and category for a title.</param>
/// <param name="Bars">One bar per hour for a day, per day for a week or month; parts use the key of <paramref name="Legend"/>.</param>
/// <param name="Hours">Usage per hour of the day over a week or month; empty for a day, whose bars are already hourly.</param>
/// <param name="Titles">Titles used the longest; empty for a title.</param>
/// <param name="Rule">Personal rule the category is saved against; null when it cannot be changed.</param>
public sealed record StatsAppView(
    StatsScale Scale,
    DateOnly Anchor,
    string Name,
    string? Sub,
    string Title,
    bool CanPrev,
    bool CanNext,
    string TotalLabel,
    string Total,
    string? Summary,
    IReadOnlyList<StatsFact> Facts,
    IReadOnlyList<StatsLegend> Legend,
    IReadOnlyList<StatsBar> Bars,
    long BarMax,
    string BarMaxLabel,
    IReadOnlyList<StatsBar> Hours,
    long HoursMax,
    string HoursMaxLabel,
    IReadOnlyList<StatsRankItem> Titles,
    StatsRuleTarget? Rule,
    string? Empty);

public sealed record StatsFact(string Label, string Value);

/// <summary>A personal rule the page saves with rules.set, and the category the row has now.</summary>
/// <param name="Kind">Process or Domain.</param>
/// <param name="Name">App name as recorded, saved as the rule's name.</param>
public sealed record StatsRuleTarget(string Kind, string Pattern, string Name, string Category = "");