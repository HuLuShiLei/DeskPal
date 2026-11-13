using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Usage;

/// <summary>A stretch of time with the same state, category, app and title. Never crosses local midnight.</summary>
/// <param name="Title">Window or media title. Kept on this computer only.</param>
public sealed record UsageSegment(DateTimeOffset Start, DateTimeOffset End, PresenceState State, ActivityCategory Category, string? App, string? Title)
{
    public long Seconds => (long)(End - Start).TotalSeconds;
}

/// <summary>Seconds of one local day, kept after the segments of that day expired. Has no time of day.</summary>
public sealed record UsageRollup(DateOnly Date, PresenceState State, ActivityCategory Category, string? App, string? Title, long Seconds);

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