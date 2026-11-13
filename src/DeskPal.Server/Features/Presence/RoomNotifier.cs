using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.AspNetCore.SignalR;

namespace DeskPal.Server.Features.Presence;

/// <summary>Keeps SignalR group membership in sync with Room membership and broadcasts Room events.</summary>
public sealed class RoomNotifier(IHubContext<PresenceHub, IPresenceClient> hub, ConnectionRegistry connections, PresenceStore store)
{
    public static string GroupName(Guid roomId) => $"room:{roomId}";

    /// <param name="blockPeers">Members blocked by or blocking the new member; their cached presence is not replayed.</param>
    public async Task MemberJoinedAsync(Guid roomId, Guid userId, IReadOnlySet<Guid>? blockPeers = null)
    {
        var group = GroupName(roomId);
        var userConnections = connections.Get(userId);
        foreach (var connectionId in userConnections)
        {
            await hub.Groups.AddToGroupAsync(connectionId, group);
        }

        await hub.Clients.Group(group).Receive(ServerEnvelope(MessageTypes.RoomMemberJoined, roomId, userId));

        var existing = store.GetRoom(roomId).Where(e => blockPeers?.Contains(e.SenderId) != true);
        foreach (var envelope in existing)
        {
            await hub.Clients.Clients(userConnections).Receive(envelope);
        }
    }

    public Task KickedAsync(Guid roomId, Guid userId) =>
        hub.Clients.Clients(connections.Get(userId)).Receive(ServerEnvelope(MessageTypes.RoomKicked, roomId, userId));

    public async Task MemberLeftAsync(Guid roomId, Guid userId)
    {
        var group = GroupName(roomId);
        var userConnections = connections.Get(userId);
        foreach (var connectionId in userConnections)
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, group);
        }

        store.RemoveMember(roomId, userId);
        var envelope = ServerEnvelope(MessageTypes.RoomMemberLeft, roomId, userId);
        await hub.Clients.Group(group).Receive(envelope);
        await hub.Clients.Clients(userConnections).Receive(envelope);
    }

    public Task OwnerChangedAsync(Guid roomId, Guid newOwnerId) =>
        hub.Clients.Group(GroupName(roomId)).Receive(ServerEnvelope(MessageTypes.RoomOwnerChanged, roomId, newOwnerId));

    public Task RenamedAsync(Guid roomId) =>
        hub.Clients.Group(GroupName(roomId)).Receive(ServerEnvelope(MessageTypes.RoomRenamed, roomId, null));

    /// <summary>The user's own connections are in the same groups, so their other computers hear it too.</summary>
    public async Task MemberRenamedAsync(Guid userId, IReadOnlyList<Guid> roomIds)
    {
        foreach (var roomId in roomIds)
        {
            await hub.Clients.Group(GroupName(roomId)).Receive(ServerEnvelope(MessageTypes.MemberRenamed, roomId, userId));
        }

        if (roomIds.Count == 0)
        {
            await hub.Clients.Clients(connections.Get(userId)).Receive(ServerEnvelope(MessageTypes.MemberRenamed, Guid.Empty, userId));
        }
    }

    /// <summary>Tells the user's other computers to reload. Other room members never see personal rules.</summary>
    public Task PersonalRulesChangedAsync(Guid userId) =>
        hub.Clients.Clients(connections.Get(userId)).Receive(ServerEnvelope(MessageTypes.PersonalRulesChanged, Guid.Empty, userId));

    /// <summary>
    /// The blocked member sees the blocker go offline, the same as when the blocker quits, and is never told about the
    /// block. Only the blocker's own computers are told to reload.
    /// </summary>
    public async Task BlockedAsync(Guid roomId, Guid blockerId, Guid blockedId, DateTimeOffset now)
    {
        var offline = Envelope.Create(MessageTypes.PresenceUpdate, roomId, PresenceSnapshot.Offline(now)) with { SenderId = blockerId, Ts = now };
        await hub.Clients.Clients(connections.Get(blockedId)).Receive(offline);
        await hub.Clients.Clients(connections.Get(blockerId)).Receive(ServerEnvelope(MessageTypes.RoomBlocksChanged, roomId, blockedId));
    }

    /// <param name="stillBlocked">The other member has blocked me too, so nothing is exchanged yet.</param>
    public async Task UnblockedAsync(Guid roomId, Guid blockerId, Guid blockedId, bool stillBlocked)
    {
        await hub.Clients.Clients(connections.Get(blockerId)).Receive(ServerEnvelope(MessageTypes.RoomBlocksChanged, roomId, blockedId));
        if (stillBlocked)
        {
            return;
        }

        foreach (var envelope in store.GetRoom(roomId))
        {
            if (envelope.SenderId == blockerId)
            {
                await hub.Clients.Clients(connections.Get(blockedId)).Receive(envelope);
            }
            else if (envelope.SenderId == blockedId)
            {
                await hub.Clients.Clients(connections.Get(blockerId)).Receive(envelope);
            }
        }
    }

    public async Task DissolvedAsync(Guid roomId, IReadOnlyList<Guid> memberIds)
    {
        var group = GroupName(roomId);
        await hub.Clients.Group(group).Receive(ServerEnvelope(MessageTypes.RoomDissolved, roomId, null));
        foreach (var memberId in memberIds)
        {
            foreach (var connectionId in connections.Get(memberId))
            {
                await hub.Groups.RemoveFromGroupAsync(connectionId, group);
            }
        }

        store.RemoveRoom(roomId);
    }

    private static Envelope ServerEnvelope(string type, Guid roomId, Guid? userId) =>
        Envelope.Create(type, roomId, new RoomEventPayload(userId));
}
