using DeskPal.Client.Core.Providers;
using DeskPal.Client.Services;

namespace DeskPal.Client.Providers;

/// <summary>Where a plugin that reads the room gets its line from.</summary>
public interface IRoomLineSource
{
    /// <summary>The current line, built when asked.</summary>
    string Line { get; }

    /// <summary>Raised from any thread when <see cref="Line"/> may have changed.</summary>
    event Action? Changed;
}

/// <summary>
/// The current "room" line for plugins that read <see cref="ProviderReads.Room"/> (<see cref="RoomFeed"/>), from what
/// this client already has: the room I am in, its members' latest states and what I sent to it. Nothing new is asked
/// from or sent to the server.
/// </summary>
public sealed class RoomFeedSource : IRoomLineSource
{
    private readonly RoomState _rooms;
    private readonly PresenceCoordinator _presence;

    public RoomFeedSource(RoomState rooms, PresenceCoordinator presence)
    {
        _rooms = rooms;
        _presence = presence;
        rooms.Changed += OnChanged;
        presence.SharedChanged += OnChanged;
    }

    /// <summary>Raised from any thread when the room, its members or anyone's state may have changed.</summary>
    public event Action? Changed;

    public string Line
    {
        get
        {
            var room = _rooms.SelectedRoom;
            var shared = _presence.Shared;
            var mine = room is not null && shared is { } s && s.RoomId == room.Id ? s.Snapshot : null;
            return RoomFeed.Build(room, _rooms.MyUserId, userId => room is null ? null : _rooms.GetPresence(room.Id, userId), mine);
        }
    }

    private void OnChanged() => Changed?.Invoke();
}
