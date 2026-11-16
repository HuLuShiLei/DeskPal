namespace DeskPal.Client.Core.Updates;

/// <summary>
/// 检查更新要调用 GitHub API，未登录时每个出口 IP 每小时 60 次，同一网络下的用户共用。
/// 手动检查在冷却时间内沿用上次「已是最新」的结果；被限流后到恢复时间之前不再请求。
/// </summary>
public sealed class UpdateCheckThrottle
{
    public static readonly TimeSpan ManualCooldown = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinimumWait = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumWait = TimeSpan.FromHours(1);

    private readonly object _gate = new();
    private readonly Dictionary<bool, DateTimeOffset> _upToDateAt = [];
    private DateTimeOffset? _blockedUntil;

    /// <summary>429 一定是限流；403 只有额度用完或带 Retry-After 时才是，其余 403 按普通失败处理。</summary>
    public static bool IsRateLimited(int statusCode, string? remaining, bool hasRetryAfter) =>
        statusCode == 429 || (statusCode == 403 && (hasRetryAfter || remaining?.Trim() == "0"));

    public DateTimeOffset? BlockedUntil(DateTimeOffset now)
    {
        lock (_gate)
        {
            return _blockedUntil is { } until && until > now ? until : null;
        }
    }

    public bool RecentlyUpToDate(bool beta, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _upToDateAt.TryGetValue(beta, out var at) && now - at < ManualCooldown && now >= at;
        }
    }

    public void UpToDate(bool beta, DateTimeOffset now)
    {
        lock (_gate)
        {
            _upToDateAt[beta] = now;
        }
    }

    public void Forget(bool beta)
    {
        lock (_gate)
        {
            _upToDateAt.Remove(beta);
        }
    }

    /// <summary>没有恢复时间时等 1 小时；恢复时间不可信时限制在 1 分钟到 1 小时之间。</summary>
    public DateTimeOffset RateLimited(DateTimeOffset now, DateTimeOffset? resetAt)
    {
        var until = resetAt ?? now + MaximumWait;
        if (until < now + MinimumWait)
        {
            until = now + MinimumWait;
        }
        else if (until > now + MaximumWait)
        {
            until = now + MaximumWait;
        }

        lock (_gate)
        {
            if (_blockedUntil is not { } current || until > current)
            {
                _blockedUntil = until;
            }

            return _blockedUntil.Value;
        }
    }
}