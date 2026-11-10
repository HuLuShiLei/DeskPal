using System.Collections.Concurrent;
using DeskPal.Shared.Messaging;

namespace DeskPal.Server.Features.Presence;

/// <summary>Latest presence.update envelope per Room member. Memory only, never written to the database.</summary>
public sealed class PresenceStore
{
    private readonly ConcurrentDictionary<(Guid RoomId, Guid UserId), Envelope> _latest = new();

    public void Set(Envelope envelope) => _latest[(envelope.RoomId, envelope.SenderId)] = envelope;

    public IReadOnlyList<Envelope> GetRoom(Guid roomId) =>
        _latest.Where(kv => kv.Key.RoomId == roomId).Select(kv => kv.Value).ToList();

    public void RemoveMember(Guid roomId, Guid userId) => _latest.TryRemove((roomId, userId), out _);

    public void RemoveRoom(Guid roomId)
    {
        foreach (var key in _latest.Keys.Where(k => k.RoomId == roomId))
        {
            _latest.TryRemove(key, out _);
        }
    }
}