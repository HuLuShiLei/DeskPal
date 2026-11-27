namespace DeskPal.Client.Core.Providers;

/// <summary>
/// Decides when the next line goes to a plugin's standard input: only the latest line is kept, a line equal to the
/// last one written is not written again, and lines are at least <see cref="Interval"/> apart. Not thread safe.
/// </summary>
public sealed class LineThrottle(TimeSpan interval)
{
    private string? _pending;
    private string? _written;
    private DateTimeOffset? _writtenAt;

    public TimeSpan Interval { get; } = interval;

    /// <summary>Replaces whatever was waiting.</summary>
    public void Offer(string line) => _pending = line == _written ? null : line;

    /// <summary>The line to write now; or null and how long to wait (zero when there is nothing to write).</summary>
    public (string? Line, TimeSpan Wait) Next(DateTimeOffset now)
    {
        if (_pending is null)
        {
            return (null, TimeSpan.Zero);
        }

        if (_writtenAt is { } at && now - at < Interval)
        {
            return (null, Interval - (now - at));
        }

        return (_pending, TimeSpan.Zero);
    }

    /// <summary>Call after <paramref name="line"/> was written (or failed to be): it is not offered again.</summary>
    public void Written(string line, DateTimeOffset now)
    {
        _written = line;
        _writtenAt = now;
        if (_pending == line)
        {
            _pending = null;
        }
    }
}

/// <summary>
/// Writes lines to a plugin's standard input without ever making the caller wait: <see cref="Post"/> only stores the
/// line, and <see cref="RunAsync"/> writes the latest one when the previous write finished and the interval passed.
/// A plugin that does not read its input only blocks this writer's loop; the lines in between are dropped.
/// Note that writes to an anonymous pipe are synchronous underneath in .NET: a stuck write holds one thread-pool thread
/// until the plugin reads or exits (the host kills it when it is turned off or restarted). DeskPal itself never waits.
/// </summary>
public sealed class LatestLineWriter(Func<string, CancellationToken, Task> write, TimeSpan interval, Action<Exception>? failed = null)
{
    private readonly Lock _lock = new();
    private readonly LineThrottle _throttle = new(interval);
    private readonly SemaphoreSlim _wake = new(0, 1);

    public void Post(string line)
    {
        lock (_lock)
        {
            _throttle.Offer(line);
        }

        try
        {
            if (_wake.CurrentCount == 0)
            {
                _wake.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Already woken.
        }
    }

    /// <summary>Runs until cancelled or a write fails (the plugin closed its input or exited).</summary>
    public async Task RunAsync(CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                await _wake.WaitAsync(cancellation);
                while (true)
                {
                    string? line;
                    TimeSpan wait;
                    lock (_lock)
                    {
                        (line, wait) = _throttle.Next(DateTimeOffset.UtcNow);
                    }

                    if (line is null)
                    {
                        if (wait <= TimeSpan.Zero)
                        {
                            break;
                        }

                        await Task.Delay(wait, cancellation);
                        continue;
                    }

                    try
                    {
                        await write(line, cancellation);
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            _throttle.Written(line, DateTimeOffset.UtcNow);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            failed?.Invoke(ex);
        }
    }
}
