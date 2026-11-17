using DeskPal.Shared.Presence;
using DeskPal.Shared.Stats;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Sums segments and per-day totals into a <see cref="UsageReport"/> for a range of local days. Each segment counts in
/// the local time it was recorded at (<see cref="UsageSegment.Start"/>'s offset).
/// </summary>
public static class UsageAggregator
{
    public const int TopCount = 20;

    /// <summary>Shorter spans are left out of the single-day timeline; they would not be visible anyway.</summary>
    private const int MinSpanSeconds = 30;

    /// <summary>Spans of the same state closer than this are joined on the timeline.</summary>
    private const int JoinGapSeconds = 60;

    /// <summary>Use before this hour counts towards the day before for <see cref="UsageDayBounds"/>.</summary>
    public const int DayStartHour = 4;

    /// <summary>Apps and titles are not cut to <see cref="TopCount"/> here, so that <see cref="StatsFilter"/> can merge hidden ones first.</summary>
    /// <param name="segments">Segments of the range; those until <see cref="DayStartHour"/> on the day after only end the last day's <see cref="UsageDayBounds"/>.</param>
    public static UsageReport Build(
        IEnumerable<UsageSegment> segments,
        IEnumerable<UsageRollup> rollups,
        DateOnly from,
        DateOnly to)
    {
        var single = from == to;
        var days = new Dictionary<(DateOnly, PresenceState, ActivityCategory), long>();
        var hours = new Dictionary<(int, PresenceState, ActivityCategory), long>();
        var apps = new Dictionary<(PresenceState, ActivityCategory, string?), long>();
        var titles = new Dictionary<(PresenceState, ActivityCategory, string?, string?), long>();
        var spans = new List<UsageSpan>();
        var weekHours = new Dictionary<(DayOfWeek, int, PresenceState, ActivityCategory), long>();
        var bounds = new Dictionary<DateOnly, (int First, int Last)>();
        var offsets = new SortedSet<int>();

        void Count(DateOnly date, PresenceState state, ActivityCategory category, string? app, string? title, long seconds)
        {
            if (seconds <= 0)
            {
                return;
            }

            days[(date, state, category)] = days.GetValueOrDefault((date, state, category)) + seconds;
            if (!UsageStates.Counts(state))
            {
                return;
            }

            apps[(state, category, app)] = apps.GetValueOrDefault((state, category, app)) + seconds;
            titles[(state, category, app, title)] = titles.GetValueOrDefault((state, category, app, title)) + seconds;
        }

        foreach (var segment in segments)
        {
            if (UsageStates.Counts(segment.State) && segment.End > segment.Start)
            {
                var day = DateOnly.FromDateTime(segment.Start.DateTime.AddHours(-DayStartHour));
                if (day >= from && day <= to)
                {
                    var midnight = LocalMidnight(day, segment.Start.Offset);
                    var first = (int)(segment.Start - midnight).TotalSeconds;
                    var last = (int)(segment.End - midnight).TotalSeconds;
                    bounds[day] = bounds.TryGetValue(day, out var b) ? (Math.Min(b.First, first), Math.Max(b.Last, last)) : (first, last);
                }
            }

            foreach (var (date, start, end) in SplitByDay(segment.Start, segment.End))
            {
                if (date < from || date > to)
                {
                    continue;
                }

                offsets.Add((int)segment.Start.Offset.TotalMinutes);
                Count(date, segment.State, segment.Category, segment.App, segment.Title, (long)(end - start).TotalSeconds);
                var midnight = LocalMidnight(date, segment.Start.Offset);
                var startSecond = (int)(start - midnight).TotalSeconds;
                var endSecond = (int)(end - midnight).TotalSeconds;
                if (single)
                {
                    spans.Add(new UsageSpan(startSecond, endSecond, segment.State, segment.Category));
                }

                if (!UsageStates.Counts(segment.State))
                {
                    continue;
                }

                for (var hour = startSecond / 3600; hour * 3600 < endSecond && hour < 24; hour++)
                {
                    var seconds = Math.Min(endSecond, (hour + 1) * 3600) - Math.Max(startSecond, hour * 3600);
                    var weekKey = (date.DayOfWeek, hour, segment.State, segment.Category);
                    weekHours[weekKey] = weekHours.GetValueOrDefault(weekKey) + seconds;
                    if (single)
                    {
                        var key = (hour, segment.State, segment.Category);
                        hours[key] = hours.GetValueOrDefault(key) + seconds;
                    }
                }
            }
        }

        foreach (var rollup in rollups)
        {
            if (rollup.Date >= from && rollup.Date <= to)
            {
                Count(rollup.Date, rollup.State, rollup.Category, rollup.App, rollup.Title, rollup.Seconds);
            }
        }

        return new UsageReport(
            from,
            to,
            days.Select(d => new UsageTotal(d.Key.Item1, -1, d.Key.Item2, d.Key.Item3, d.Value)).OrderBy(d => d.Date).ThenBy(d => d.State).ThenBy(d => d.Category).ToList(),
            hours.Select(h => new UsageTotal(from, h.Key.Item1, h.Key.Item2, h.Key.Item3, h.Value)).OrderBy(h => h.Hour).ThenBy(h => h.State).ThenBy(h => h.Category).ToList(),
            JoinSpans(spans),
            apps.Select(a => new UsageItem(a.Key.Item1, a.Key.Item2, a.Key.Item3, null, a.Value)).OrderByDescending(a => a.Seconds).ToList(),
            titles.Select(t => new UsageItem(t.Key.Item1, t.Key.Item2, t.Key.Item3, t.Key.Item4, t.Value)).OrderByDescending(t => t.Seconds).ToList(),
            WeekHours: weekHours.Select(w => new UsageWeekHour(w.Key.Item1, w.Key.Item2, w.Key.Item3, w.Key.Item4, w.Value)).OrderBy(w => w.Day).ThenBy(w => w.Hour).ThenBy(w => w.State).ThenBy(w => w.Category).ToList(),
            Bounds: bounds.Select(b => new UsageDayBounds(b.Key, b.Value.First, b.Value.Last)).OrderBy(b => b.Date).ToList(),
            Offsets: offsets.ToList());
    }

    /// <summary>
    /// Instants to read segments for local days <paramref name="from"/> to <paramref name="to"/>, whatever offset
    /// they were recorded at (UTC−12 to UTC+14). Segments outside the days are left out by <see cref="Build"/>.
    /// </summary>
    /// <param name="extraHours">Hours after the last day to read as well.</param>
    public static (DateTimeOffset From, DateTimeOffset To) Window(DateOnly from, DateOnly to, int extraHours = 0) =>
        (LocalMidnight(from, TimeSpan.FromHours(14)), LocalMidnight(to.AddDays(1), TimeSpan.FromHours(-12)).AddHours(extraHours));

    /// <summary>Usage in the report, without away, locked and paused time.</summary>
    public static long ActiveSeconds(UsageReport report) => report.Days.Where(d => UsageStates.Counts(d.State)).Sum(d => d.Seconds);

    /// <summary>Usage per state and category over the whole report, for <see cref="UsageReport.Previous"/>.</summary>
    public static IReadOnlyList<UsageTotal> Totals(UsageReport report) => report.Days
        .Where(d => UsageStates.Counts(d.State))
        .GroupBy(d => (d.State, d.Category))
        .Select(g => new UsageTotal(report.From, -1, g.Key.State, g.Key.Category, g.Sum(d => d.Seconds)))
        .OrderBy(t => t.State)
        .ThenBy(t => t.Category)
        .ToList();

    public static DateTimeOffset LocalMidnight(DateOnly date, TimeSpan offset) => new(date.ToDateTime(TimeOnly.MinValue), offset);

    /// <summary>Pieces of one local day each, in the offset of <paramref name="start"/>.</summary>
    internal static IEnumerable<(DateOnly Date, DateTimeOffset Start, DateTimeOffset End)> SplitByDay(DateTimeOffset start, DateTimeOffset end)
    {
        while (start < end)
        {
            var date = DateOnly.FromDateTime(start.DateTime);
            var next = LocalMidnight(date.AddDays(1), start.Offset);
            var pieceEnd = next < end ? next : end;
            yield return (date, start, pieceEnd);
            start = pieceEnd;
        }
    }

    private static List<UsageSpan> JoinSpans(List<UsageSpan> spans)
    {
        var joined = new List<UsageSpan>();
        foreach (var span in spans.OrderBy(s => s.Start))
        {
            if (joined.Count > 0 && joined[^1] is var last && last.State == span.State && last.Category == span.Category && span.Start - last.End <= JoinGapSeconds)
            {
                joined[^1] = last with { End = Math.Max(last.End, span.End) };
            }
            else
            {
                joined.Add(span);
            }
        }

        return joined.Where(s => s.End - s.Start >= MinSpanSeconds).ToList();
    }
}