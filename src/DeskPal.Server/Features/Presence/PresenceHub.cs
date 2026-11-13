using DeskPal.Server.Common;
using DeskPal.Server.Features.Rooms;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DeskPal.Server.Features.Presence;

[Authorize]
public sealed class PresenceHub(RoomService rooms, ConnectionRegistry connections, PresenceStore store, StatsRateLimiter statsLimiter, TimeProvider time)
    : Hub<IPresenceClient>
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User!.GetUserId();
        connections.Add(userId, Context);

        foreach (var roomId in await rooms.GetRoomIdsForUserAsync(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomNotifier.GroupName(roomId));
            var peers = await rooms.GetBlockPeersAsync(roomId, userId);
            foreach (var envelope in store.GetRoom(roomId).Where(e => e.SenderId != userId && !peers.Contains(e.SenderId)))
            {
                await Clients.Caller.Receive(envelope);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User!.GetUserId();
        if (connections.Remove(userId, Context.ConnectionId))
        {
            var now = time.GetUtcNow();
            foreach (var roomId in await rooms.GetRoomIdsForUserAsync(userId))
            {
                var offline = Envelope.Create(MessageTypes.PresenceUpdate, roomId, PresenceSnapshot.Offline(now)) with
                {
                    SenderId = userId,
                    Ts = now,
                };
                store.Set(offline);
                var peers = await rooms.GetBlockPeersAsync(roomId, userId);
                await Clients.GroupExcept(RoomNotifier.GroupName(roomId), connections.Get(peers)).Receive(offline);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task Publish(Envelope envelope)
    {
        if (!MessageTypes.ClientPublishable.Contains(envelope.Type))
        {
            throw new HubException($"不支持的消息类型：{envelope.Type}");
        }

        var userId = Context.User!.GetUserId();
        if (!await rooms.IsMemberAsync(userId, envelope.RoomId))
        {
            throw new HubException("你不是该房间的成员");
        }

        var stamped = envelope with { SenderId = userId, Ts = time.GetUtcNow() };
        if (MessageTypes.Direct.Contains(stamped.Type))
        {
            await SendDirectAsync(stamped, userId);
            return;
        }

        stamped = stamped with { To = null };
        if (stamped.Type == MessageTypes.PresenceUpdate)
        {
            store.Set(stamped);
        }

        var peers = await rooms.GetBlockPeersAsync(stamped.RoomId, userId);
        await Clients.GroupExcept(RoomNotifier.GroupName(stamped.RoomId), [Context.ConnectionId, .. connections.Get(peers)]).Receive(stamped);
    }

    /// <summary>Delivered only when both are members of the room and neither blocked the other. Otherwise dropped without telling the sender why.</summary>
    private async Task SendDirectAsync(Envelope envelope, Guid senderId)
    {
        if (envelope.To is not { } to || to == senderId || !await rooms.IsMemberAsync(to, envelope.RoomId))
        {
            return;
        }

        if ((await rooms.GetBlockPeersAsync(envelope.RoomId, senderId)).Contains(to))
        {
            return;
        }

        if (envelope.Type == MessageTypes.StatsRequest && !statsLimiter.TryAcquire(senderId))
        {
            return;
        }

        await Clients.Clients(connections.Get(to)).Receive(envelope);
    }
}
