namespace DeskPal.Client.Core.Ui;

/// <summary>
/// Room members pinned as cards on the desktop. Pins belong to the room they were made in and are dropped when the
/// selected room changes or the member is no longer in it. They are not saved.
/// </summary>
public sealed class PinnedMembers
{
    private readonly List<Guid> _users = [];

    public event Action? Changed;

    public Guid? RoomId { get; private set; }

    public IReadOnlyList<Guid> Users => _users;

    public bool Contains(Guid userId) => _users.Contains(userId);

    public void Pin(Guid roomId, Guid userId)
    {
        if (RoomId != roomId)
        {
            _users.Clear();
            RoomId = roomId;
        }

        if (!_users.Contains(userId))
        {
            _users.Add(userId);
            Changed?.Invoke();
        }
    }

    public void Unpin(Guid userId)
    {
        if (_users.Remove(userId))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Drops the pins that no longer apply and returns them.</summary>
    public IReadOnlyList<Guid> Retain(Guid? selectedRoomId, IEnumerable<Guid> members)
    {
        var current = members.ToHashSet();
        var removed = selectedRoomId == RoomId ? _users.Where(u => !current.Contains(u)).ToList() : _users.ToList();
        if (selectedRoomId != RoomId)
        {
            RoomId = null;
        }

        if (removed.Count == 0)
        {
            return removed;
        }

        _users.RemoveAll(removed.Contains);
        Changed?.Invoke();
        return removed;
    }
}