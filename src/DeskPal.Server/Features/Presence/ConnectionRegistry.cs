namespace DeskPal.Server.Features.Presence;

public sealed class ConnectionRegistry
{
    private readonly Dictionary<Guid, HashSet<string>> _connections = new();
    private readonly Lock _lock = new();

    public void Add(Guid userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(userId, out var set))
            {
                _connections[userId] = set = new HashSet<string>();
            }

            set.Add(connectionId);
        }
    }

    /// <returns>True when the user has no connections left.</returns>
    public bool Remove(Guid userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(userId, out var set))
            {
                return true;
            }

            set.Remove(connectionId);
            if (set.Count > 0)
            {
                return false;
            }

            _connections.Remove(userId);
            return true;
        }
    }

    public IReadOnlyList<string> Get(Guid userId)
    {
        lock (_lock)
        {
            return _connections.TryGetValue(userId, out var set) ? set.ToList() : [];
        }
    }
}