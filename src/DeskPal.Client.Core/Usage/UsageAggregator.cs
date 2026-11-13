using DeskPal.Shared.Presence;
using DeskPal.Shared.Stats;

namespace DeskPal.Client.Core.Usage;

/// <summary>Sums segments and per-day totals into a <see cref="UsageReport"/> for a range of local days.</summary>
public static class UsageAggregator
{
    public const int TopCount = 20;

    /// <summary>Shorter spans are left out of the single-day timeline; they would not be visible anyway.</summary>
    private const int MinSpanSeconds = 30;

    /// <summary>Spans of the same state closer than this are joined on the timeline.</summary>
    private const int JoinGapSeconds = 60;

    /// <summary>Apps and titles are not cut to <see cref="TopCount"/> here, so that <see cref="StatsFilter"/> can merge hidden ones first.</summary>
    public static UsageReport Build(
        IEnumerable<UsageSegment> segments,
        IEnumerable<UsageRollup> rollups,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo zone)
    {
        var single = from == to;
        var days = new Dictionary<(DateOnly, PresenceState, ActivityCategory), long>();
        var hours = new Dictionary<(int, PresenceState, ActivityCategory), long>();
        var apps = new Dictionary<(PresenceState, ActivityCategory, string?), long>();
        var titles = new Dictionary<(PresenceState, ActivityCategory, string?, string?), long>();
        var spans = new List<UsageSpan>();

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
            foreach (var (date, start, end) in SplitByDay(segment.Start, segment.End, zone))
            {
                if (date < from || date > to)
                {
                    continue;
                }

                Count(date, segment.State, segment.Category, segment.App, segment.Title, (long)(end - start).TotalSeconds);
                if (!single)
                {
                    continue;
                }

                var midnight = LocalMidnight(date, zone);
                var startSecond = (int)(start - midnight).TotalSeconds;
                var endSecond = (int)(end - midnight).TotalSeconds;
                spans.Add(new UsageSpan(startSecond, endSecond, segment.State));
                if (!UsageStates.Counts(segment.State))
                {
                    continue;
                }

                for (var hour = startSecond / 3600; hour * 3600 < endSecond && hour < 24; hour++)
                {
                    var seconds = Math.Min(endSecond, (hour + 1) * 3600) - Math.Max(startSecond, hour * 3600);
                    var key = (hour, segment.State, segment.Category);
                    hours[key] = hours.GetValueOrDefault(key) + seconds;
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
            titles.Select(t => new UsageItem(t.Key.Item1, t.Key.Item2, t.Key.Item3, t.Key.Item4, t.Value)).OrderByDescending(t => t.Seconds).ToList());
    }

    /// <summary>Usage in the report, without away, locked and paused time.</summary>
    public static long ActiveSeconds(UsageReport report) => report.Days.Where(d => UsageStates.Counts(d.State)).Sum(d => d.Seconds);

    public static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static IEnumerable<(DateOnly Date, DateTimeOffset Start, DateTimeOffset End)> SplitByDay(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        while (start < end)
        {
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
            var next = LocalMidnight(date.AddDays(1), zone);
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
            if (joined.Count > 0 && joined[^1] is var last && last.State == span.State && span.Start - last.End <= JoinGapSeconds)
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