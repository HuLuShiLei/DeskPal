namespace DeskPal.Shared.Messaging;

public static class MessageTypes
{
    public const string PresenceUpdate = "presence.update";

    public const string RoomMemberJoined = "room.memberJoined";
    public const string RoomMemberLeft = "room.memberLeft";
    public const string RoomKicked = "room.kicked";
    public const string RoomOwnerChanged = "room.ownerChanged";
    public const string RoomDissolved = "room.dissolved";
    public const string RoomRenamed = "room.renamed";

    /// <summary>A member changed the display name. Sent to every room of that member; RoomId is empty when the member has no room.</summary>
    public const string MemberRenamed = "room.memberRenamed";

    /// <summary>Sent only to the user's own connections. RoomId is empty.</summary>
    public const string PersonalRulesChanged = "rules.personalChanged";

    /// <summary>I blocked or unblocked a member. Sent only to my own connections; the other member is never told.</summary>
    public const string RoomBlocksChanged = "room.blocksChanged";

    /// <summary>The account signed in on another computer. Sent only to the old computer, whose connection is closed shortly after. RoomId is empty.</summary>
    public const string SessionReplaced = "auth.sessionReplaced";

    /// <summary>A member turned usage stats sharing on or off, or changed the days. Sent to the room so the members reload it.</summary>
    public const string MemberSharingChanged = "room.memberSharingChanged";

    /// <summary>Asks a room member for usage stats. Sent only to <see cref="Envelope.To"/>.</summary>
    public const string StatsRequest = "stats.request";

    /// <summary>Answer to <see cref="StatsRequest"/>. Sent only to <see cref="Envelope.To"/>.</summary>
    public const string StatsResponse = "stats.response";

    /// <summary>Types a client is allowed to publish through the hub. Everything else is server-originated.</summary>
    public static readonly IReadOnlySet<string> ClientPublishable = new HashSet<string> { PresenceUpdate, StatsRequest, StatsResponse };

    /// <summary>Client types delivered to one member (<see cref="Envelope.To"/>) instead of the whole room.</summary>
    public static readonly IReadOnlySet<string> Direct = new HashSet<string> { StatsRequest, StatsResponse };
}

public sealed record RoomEventPayload(Guid? UserId);
