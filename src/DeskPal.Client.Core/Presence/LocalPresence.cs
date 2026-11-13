using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Presence;

/// <summary>Unfiltered presence on this machine. Detail may contain window or media titles and never leaves the machine as-is.</summary>
public sealed record LocalPresence(
    PresenceState State,
    ActivityCategory Category,
    string? AppDisplayName,
    string? Detail)
{
    public (PresenceState, ActivityCategory, string?) Key => (State, Category, AppDisplayName);
}

public sealed record CommittedPresence(LocalPresence Presence, DateTimeOffset Since);