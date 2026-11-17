using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// A stretch of time with the same state, category, app and title. <see cref="Start"/> carries the UTC offset it was
/// recorded at; its local day and hour come from that offset, not from the time zone of whoever reads it. Never crosses
/// midnight of that local day.
/// </summary>
/// <param name="Category">Category when it was recorded. Never rewritten; <see cref="UsageReclassifier"/> applies the current personal rules when reading.</param>
/// <param name="Title">Window or media title. Kept on this computer only.</param>
/// <param name="Process">Process in front, without ".exe". Null for records made before it was kept.</param>
/// <param name="BaseCategory">Category without personal rules, so that removing a personal rule restores it. Null for older records.</param>
public sealed record UsageSegment(DateTimeOffset Start, DateTimeOffset End, PresenceState State, ActivityCategory Category, string? App, string? Title, string? Process = null, ActivityCategory? BaseCategory = null)
{
    public long Seconds => (long)(End - Start).TotalSeconds;
}

/// <summary>Seconds of one local day, kept after the segments of that day expired. Has no time of day. Same columns as <see cref="UsageSegment"/>.</summary>
public sealed record UsageRollup(DateOnly Date, PresenceState State, ActivityCategory Category, string? App, string? Title, long Seconds, string? Process = null, ActivityCategory? BaseCategory = null);

public interface IUsageWriter
{
    /// <returns>Id used to extend the segment later.</returns>
    long Add(UsageSegment segment);

    void UpdateEnd(long id, DateTimeOffset end);
}

public static class UsageStates
{
    /// <summary>Away, locked and paused time is recorded but not counted as usage.</summary>
    public static bool Counts(PresenceState state) =>
        state is not (PresenceState.Away or PresenceState.Locked or PresenceState.Paused or PresenceState.Offline);
}