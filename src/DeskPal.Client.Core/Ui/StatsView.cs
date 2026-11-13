namespace DeskPal.Client.Core.Ui;

public enum StatsScale
{
    Day,
    Week,
    Month,
}

/// <summary>How fine the chart colors and the ranking are: by state, by category and app, or by title.</summary>
public enum StatsGranularity
{
    State,
    Category,
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
/// <param name="Note">Shown above the charts, e.g. how far back a member shares.</param>
/// <param name="Empty">Shown instead of the charts when nothing was recorded.</param>
public sealed record StatsView(
    StatsScale Scale,
    StatsGranularity Granularity,
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
    string RankingTitle,
    string? Note,
    string? Empty);

/// <param name="Hidden">Time of categories the member does not show; drawn hatched.</param>
public sealed record StatsLegend(string Key, string Name, string Tone, bool Hidden = false);

/// <param name="Label">Axis label; empty when the axis skips this bar.</param>
/// <param name="Date">Day to open in the day view when the bar is clicked; null for hours.</param>
public sealed record StatsBar(string Label, string Tip, DateOnly? Date, IReadOnlyList<StatsPart> Parts);

public sealed record StatsPart(string Key, long Seconds);

public sealed record StatsSpanView(int Start, int End, string Tone, string Tip);

/// <param name="Share">Width of the bar, 0 to 1, relative to the first item.</param>
public sealed record StatsRankItem(string Name, string? Sub, string Tone, string Time, double Share, bool Hidden = false);