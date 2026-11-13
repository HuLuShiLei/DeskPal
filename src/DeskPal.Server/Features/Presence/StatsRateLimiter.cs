namespace DeskPal.Server.Features.Presence;

/// <summary>Limits how often a user may ask room members for usage stats. Kept in memory; requests over the limit are dropped.</summary>
public sealed class StatsRateLimiter(TimeProvider time)
{
    public const int PerMinute = 10;
    private readonly Dictionary<Guid, Queue<DateTimeOffset>> _recent = new();
    private readonly Lock _lock = new();

    public bool TryAcquire(Guid userId)
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            if (!_recent.TryGetValue(userId, out var queue))
            {
                _recent[userId] = queue = new Queue<DateTimeOffset>();
            }

            while (queue.Count > 0 && now - queue.Peek() >= TimeSpan.FromMinutes(1))
            {
                queue.Dequeue();
            }

            if (queue.Count >= PerMinute)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}