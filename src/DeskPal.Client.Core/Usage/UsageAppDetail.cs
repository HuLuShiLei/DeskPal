using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Usage;

/// <summary>Usage of one app, site or title over a range of local days, for the detail page of a ranking row.</summary>
/// <param name="Days">Seconds per local day.</param>
/// <param name="Hours">Seconds per hour of the day over the whole range, 24 items.</param>
/// <param name="Sessions">Times it was used: segments less than <see cref="SessionGapSeconds"/> apart count once.</param>
/// <param name="Longest">Longest session in seconds.</param>
/// <param name="Titles">Titles used the longest, at most <see cref="TopTitles"/>.</param>
public sealed record UsageAppDetail(
    PresenceState State,
    ActivityCategory Category,
    long Seconds,
    IReadOnlyDictionary<DateOnly, long> Days,
    IReadOnlyList<long> Hours,
    int Sessions,
    long Longest,
    IReadOnlyList<(string Title, long Seconds)> Titles)
{
    public const int SessionGapSeconds = 60;

    public const int TopTitles = 10;

    /// <param name="segments">Segments already reclassified with the current personal rules.</param>
    /// <param name="title">Only this title of the app; null for the whole app.</param>
    public static UsageAppDetail Build(IEnumerable<UsageSegment> segments, IEnumerable<UsageRollup> rollups, string app, string? title, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        bool Matches(PresenceState state, string? a, string? t) =>
            UsageStates.Counts(state) && string.Equals(a, app, StringComparison.Ordinal) && (title is null || string.Equals(t, title, StringComparison.Ordinal));

        var days = new Dictionary<DateOnly, long>();
        var hours = new long[24];
        var titles = new Dictionary<string, long>(StringComparer.Ordinal);
        var categories = new Dictionary<(PresenceState, ActivityCategory), long>();
        var pieces = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        void Count(DateOnly date, PresenceState state, ActivityCategory category, string? t, long seconds)
        {
            days[date] = days.GetValueOrDefault(date) + seconds;
            categories[(state, category)] = categories.GetValueOrDefault((state, category)) + seconds;
            if (t is not null)
            {
                titles[t] = titles.GetValueOrDefault(t) + seconds;
            }
        }

        foreach (var segment in segments.Where(s => Matches(s.State, s.App, s.Title)))
        {
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(segment.Start, zone).DateTime);
            if (date < from || date > to || segment.End <= segment.Start)
            {
                continue;
            }

            Count(date, segment.State, segment.Category, segment.Title, segment.Seconds);
            pieces.Add((segment.Start, segment.End));
            var midnight = UsageAggregator.LocalMidnight(date, zone);
            var startSecond = (int)(segment.Start - midnight).TotalSeconds;
            var endSecond = (int)(segment.End - midnight).TotalSeconds;
            for (var hour = startSecond / 3600; hour * 3600 < endSecond && hour < 24; hour++)
            {
                hours[hour] += Math.Min(endSecond, (hour + 1) * 3600) - Math.Max(startSecond, hour * 3600);
            }
        }

        foreach (var rollup in rollups.Where(r => r.Date >= from && r.Date <= to && Matches(r.State, r.App, r.Title)))
        {
            Count(rollup.Date, rollup.State, rollup.Category, rollup.Title, rollup.Seconds);
        }

        var sessions = 0;
        long longest = 0;
        DateTimeOffset? start = null, end = null;
        foreach (var piece in pieces.OrderBy(p => p.Start))
        {
            if (end is { } e && (piece.Start - e).TotalSeconds <= SessionGapSeconds)
            {
                end = piece.End > e ? piece.End : e;
                continue;
            }

            if (start is { } s && end is { } last)
            {
                longest = Math.Max(longest, (long)(last - s).TotalSeconds);
            }

            sessions++;
            (start, end) = (piece.Start, piece.End);
        }

        if (start is { } first && end is { } final)
        {
            longest = Math.Max(longest, (long)(final - first).TotalSeconds);
        }

        var (state, category) = categories.Count == 0 ? (PresenceState.Other, ActivityCategory.Other) : categories.MaxBy(c => c.Value).Key;
        return new UsageAppDetail(
            state,
            category,
            days.Values.Sum(),
            days,
            hours,
            sessions,
            longest,
            titles.OrderByDescending(t => t.Value).Take(TopTitles).Select(t => (t.Key, t.Value)).ToList());
    }
}
