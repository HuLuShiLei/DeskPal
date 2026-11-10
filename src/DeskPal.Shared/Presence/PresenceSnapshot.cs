namespace DeskPal.Shared.Presence;

/// <summary>Payload of a presence.update message, already filtered by the sender's privacy profile for that Room.</summary>
public sealed record PresenceSnapshot
{
    public PresenceState State { get; init; }
    public ActivityCategory Category { get; init; }
    public string? AppDisplayName { get; init; }
    public string? Detail { get; init; }
    public string? CustomText { get; init; }
    public DateTimeOffset Since { get; init; }

    public static PresenceSnapshot Offline(DateTimeOffset since) => new() { State = PresenceState.Offline, Since = since };
}