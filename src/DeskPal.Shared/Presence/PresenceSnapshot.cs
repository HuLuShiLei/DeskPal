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

    /// <summary>The sender's client, one of <see cref="ClientKinds"/>; null from older clients and in offline snapshots.</summary>
    public string? Client { get; init; }

    public static PresenceSnapshot Offline(DateTimeOffset since) => new() { State = PresenceState.Offline, Since = since };
}

public static class ClientKinds
{
    public const string Windows = "windows";
    public const string Mac = "mac";
    public const string Linux = "linux";

    public static string Current => OperatingSystem.IsMacOS() ? Mac : OperatingSystem.IsLinux() ? Linux : Windows;
}