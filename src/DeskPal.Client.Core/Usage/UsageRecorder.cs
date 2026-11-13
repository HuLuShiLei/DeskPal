using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Turns the local presence, evaluated every few seconds, into segments. A new segment starts whenever the state,
/// category, app or title changes, and at local midnight. Gaps longer than <see cref="MaxGap"/> are not counted.
/// </summary>
public sealed class UsageRecorder(IUsageWriter writer, TimeZoneInfo? zone = null)
{
    /// <summary>Longer gaps (sleep, a stalled loop) are not counted. Same as the unclassified app totals.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);

    /// <summary>An open segment is written at least this often, so a crash loses at most this much.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);

    public const int MaxTitleLength = 200;

    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;
    private readonly Lock _lock = new();
    private IReadOnlyList<NoTitleRule> _noTitle = [];
    private UsageSegment? _open;
    private long _id;
    private DateTimeOffset _saved;

    public void SetNoTitleRules(IEnumerable<NoTitleRule> rules)
    {
        lock (_lock)
        {
            _noTitle = rules.ToList();
        }
    }

    /// <param name="presence">The resolved presence before debouncing and privacy filtering; null stops the current segment.</param>
    /// <param name="process">Process in front, for <see cref="NoTitleKind.Process"/>.</param>
    /// <param name="domain">Normalized host when a browser is in front, for <see cref="NoTitleKind.Domain"/>.</param>
    public void Record(LocalPresence? presence, DateTimeOffset now, string? process = null, string? domain = null)
    {
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
                return;
            }

            var next = Normalize(presence, now, process, domain);
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

    private UsageSegment Normalize(LocalPresence presence, DateTimeOffset now, string? process, string? domain)
    {
        if (!UsageStates.Counts(presence.State))
        {
            return new UsageSegment(now, now, presence.State, ActivityCategory.None, null, null);
        }

        var app = Clean(presence.AppDisplayName);
        var title = Clean(presence.Detail);
        if (title is not null && (string.Equals(title, app, StringComparison.OrdinalIgnoreCase) || _noTitle.Any(r => r.Matches(app, process, domain, title))))
        {
            title = null;
        }

        if (title is { Length: > MaxTitleLength })
        {
            title = title[..MaxTitleLength];
        }

        return new UsageSegment(now, now, presence.State, presence.Category, app, title);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool SameKey(UsageSegment a, UsageSegment b) =>
        a.State == b.State && a.Category == b.Category && a.App == b.App && a.Title == b.Title;

    private DateTimeOffset NextMidnight(DateTimeOffset time)
    {
        var next = TimeZoneInfo.ConvertTime(time, _zone).Date.AddDays(1);
        return new DateTimeOffset(next, _zone.GetUtcOffset(next));
    }
}