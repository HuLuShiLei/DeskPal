using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Turns the local presence, evaluated every few seconds, into segments. A new segment starts whenever the state,
/// category, app, title or UTC offset changes, and at local midnight. Gaps longer than <see cref="MaxGap"/> are not counted.
/// Times carry the offset of the time zone they were recorded in.
/// </summary>
/// <param name="zone">Fixed time zone for tests; null follows the system time zone as it changes.</param>
public sealed class UsageRecorder(IUsageWriter writer, TimeZoneInfo? zone = null)
{
    /// <summary>Longer gaps (sleep, a stalled loop) are not counted. Same as the unclassified app totals.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);

    /// <summary>An open segment is written at least this often, so a crash loses at most this much.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);

    public const int MaxTitleLength = 200;

    private readonly Lock _lock = new();
    private UsageSegment? _open;
    private long _id;
    private DateTimeOffset _saved;
    private DateTimeOffset? _holdSince;


    /// <summary>How long the open segment keeps going while the browser's new address is not read yet.</summary>
    public static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(5);

    /// <param name="presence">The resolved presence before debouncing and privacy filtering, with the no-title rules already applied; null stops the current segment.</param>
    /// <param name="process">Process in front; stored so the segment can be reclassified later.</param>

    /// <param name="baseCategory">Category without the personal rules; stored so a removed personal rule stops applying.</param>
    /// <param name="hold">The browser's tab changed and its address is not read yet: keep the open segment instead of starting one with the old site.</param>
    /// <param name="paused">Sharing is paused; <paramref name="presence"/> is still what is actually done.</param>
    public void Record(LocalPresence? presence, DateTimeOffset now, string? process = null, ActivityCategory? baseCategory = null, bool hold = false, bool paused = false)
    {
        now = TimeZoneInfo.ConvertTime(now, zone ?? TimeZoneInfo.Local);
        lock (_lock)
        {
            if (_open is not null)
            {
                if (now < _open.End || now - _open.End > MaxGap)
                {
                    Close();
                }
                else
                {
                    Extend(now);
                }
            }

            if (presence is null)
            {
                Close();
                _holdSince = null;
                return;
            }

            if (!hold)
            {
                _holdSince = null;
            }
            else if (_open is not null && _open.Paused == paused)
            {
                _holdSince ??= now;
                if (now - _holdSince.Value < MaxHold)
                {
                    return;
                }
            }

            var next = Normalize(presence, now, process, baseCategory) with { Paused = paused };
            if (_open is not null && SameKey(_open, next))
            {
                if (_open.End - _saved >= SaveEvery)
                {
                    Save();
                }

                return;
            }

            Close();
            Open(next);
        }
    }

    /// <summary>Writes the open segment, e.g. before the app exits.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_open is not null)
            {
                Save();
            }
        }
    }

    private void Extend(DateTimeOffset now)
    {
        var midnight = NextMidnight(_open!.Start);
        while (now > midnight)
        {
            _open = _open with { End = midnight };
            var key = _open;
            Close();
            Open(key with { Start = midnight, End = midnight });
            midnight = NextMidnight(midnight);
        }

        _open = _open with { End = now };
    }

    private void Open(UsageSegment segment)
    {
        _open = segment;
        _id = 0;
        _saved = segment.Start;
    }

    private void Close()
    {
        if (_open is null)
        {
            return;
        }

        Save();
        _open = null;
        _id = 0;
    }

    private void Save()
    {
        if (_open!.End <= _open.Start)
        {
            return;
        }

        if (_id == 0)
        {
            _id = writer.Add(_open);
        }
        else
        {
            writer.UpdateEnd(_id, _open.End);
        }

        _saved = _open.End;
    }

    private static UsageSegment Normalize(LocalPresence presence, DateTimeOffset now, string? process, ActivityCategory? baseCategory)
    {
        if (!UsageStates.Counts(presence.State))
        {
            return new UsageSegment(now, now, presence.State, ActivityCategory.None, null, null);
        }

        var app = Clean(presence.AppDisplayName);
        var title = Clean(presence.Detail);
        if (title is not null && string.Equals(title, app, StringComparison.OrdinalIgnoreCase))
        {
            title = null;
        }

        if (title is { Length: > MaxTitleLength })
        {
            title = title[..MaxTitleLength];
        }

        var name = Clean(process)?.ToLowerInvariant();
        return new UsageSegment(now, now, presence.State, presence.Category, app, title, name, name is null || baseCategory == presence.Category ? null : baseCategory);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool SameKey(UsageSegment a, UsageSegment b) =>
        a.State == b.State && a.Category == b.Category && a.App == b.App && a.Title == b.Title && a.Process == b.Process && a.BaseCategory == b.BaseCategory && a.Paused == b.Paused
        && a.Start.Offset == b.Start.Offset;

    /// <summary>Midnight in the offset the segment was started in, so a segment stays within its own local day.</summary>
    private static DateTimeOffset NextMidnight(DateTimeOffset time) => new(time.Date.AddDays(1), time.Offset);
}