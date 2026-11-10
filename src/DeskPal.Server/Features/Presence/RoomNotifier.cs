using DeskPal.Shared.Messaging;
using Microsoft.AspNetCore.SignalR;

namespace DeskPal.Server.Features.Presence;

/// <summary>Keeps SignalR group membership in sync with Room membership and broadcasts Room events.</summary>
public sealed class RoomNotifier(IHubContext<PresenceHub, IPresenceClient> hub, ConnectionRegistry connections, PresenceStore store)
{
    public static string GroupName(Guid roomId) => $"room:{roomId}";

    public async Task MemberJoinedAsync(Guid roomId, Guid userId)
    {
        var group = GroupName(roomId);
        var userConnections = connections.Get(userId);
        foreach (var connectionId in userConnections)
        {
            await hub.Groups.AddToGroupAsync(connectionId, group);
        }

        await hub.Clients.Group(group).Receive(ServerEnvelope(MessageTypes.RoomMemberJoined, roomId, userId));

        var existing = store.GetRoom(roomId);
        foreach (var envelope in existing)
        {
            await hub.Clients.Clients(userConnections).Receive(envelope);
        }
    }

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

    public Task RulesChangedAsync(Guid roomId, Guid changedBy) =>
        hub.Clients.Group(GroupName(roomId)).Receive(ServerEnvelope(MessageTypes.RoomRulesChanged, roomId, changedBy));

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