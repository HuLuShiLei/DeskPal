using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Presence;

/// <summary>
/// A new presence must stay unchanged for StableFor before it is committed, so quick window switches are not broadcast.
/// Paused, manual status and lock are committed at once, and so is leaving them.
/// Detail-only changes (same app, new title) are committed at most once per DetailInterval.
/// </summary>
public sealed class PresenceDebouncer(TimeSpan stableFor, TimeSpan detailInterval)
{
    private static readonly HashSet<PresenceState> Immediate = [PresenceState.Paused, PresenceState.Custom, PresenceState.Locked];

    private CommittedPresence? _committed;
    private DateTimeOffset _lastCommitAt;
    private LocalPresence? _pending;
    private DateTimeOffset _pendingSince;

    public CommittedPresence? Current => _committed;

    public TimeSpan StableFor => stableFor;

    /// <summary>When the pending presence will be committed if it stays unchanged; null when nothing is waiting.</summary>
    public DateTimeOffset? PendingDeadline => _pending is null ? null : _pendingSince + stableFor;

    /// <returns>The newly committed presence, or null when nothing should be sent.</returns>
    public CommittedPresence? Offer(LocalPresence candidate, DateTimeOffset now)
    {
        if (_committed is null || Immediate.Contains(candidate.State) || Immediate.Contains(_committed.Presence.State))
        {
            if (_committed is not null && _committed.Presence == candidate)
            {
                _pending = null;
                return null;
            }

            if (_committed is not null && _committed.Presence.Key == candidate.Key)
            {
                return CommitDetail(candidate, now);
            }

            return Commit(candidate, now, now);
        }

        if (_committed.Presence.Key == candidate.Key)
        {
            _pending = null;
            return _committed.Presence.Detail != candidate.Detail ? CommitDetail(candidate, now) : null;
        }

        if (_pending is null || _pending.Key != candidate.Key)
        {
            _pending = candidate;
            _pendingSince = now;
        }
        else
        {
            _pending = candidate;
        }

        return now - _pendingSince >= stableFor ? Commit(candidate, _pendingSince, now) : null;
    }

    private CommittedPresence? CommitDetail(LocalPresence candidate, DateTimeOffset now)
    {
        if (now - _lastCommitAt < detailInterval)
        {
            return null;
        }

        return Commit(candidate, _committed!.Since, now);
    }

    private CommittedPresence Commit(LocalPresence presence, DateTimeOffset since, DateTimeOffset now)
    {
        _pending = null;
        _lastCommitAt = now;
        _committed = new CommittedPresence(presence, since);
        return _committed;
    }
}