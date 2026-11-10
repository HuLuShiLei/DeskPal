namespace DeskPal.Shared.Messaging;

public static class MessageTypes
{
    public const string PresenceUpdate = "presence.update";
    public const string Poke = "interaction.poke";

    public const string RoomMemberJoined = "room.memberJoined";
    public const string RoomMemberLeft = "room.memberLeft";
    public const string RoomOwnerChanged = "room.ownerChanged";
    public const string RoomDissolved = "room.dissolved";
    public const string RoomRulesChanged = "rules.roomRulesChanged";

    /// <summary>Types a client is allowed to publish through the hub. Everything else is server-originated.</summary>
    public static readonly IReadOnlySet<string> ClientPublishable = new HashSet<string> { PresenceUpdate, Poke };
}

public sealed record PokePayload(string? Text, Guid? TargetUserId);

public sealed record RoomEventPayload(Guid? UserId);