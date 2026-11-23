namespace DeskPal.Client.Core.Providers;

/// <summary>
/// Turns a plugin's "seconds since the last input" reports into idle or not, using the threshold the user set.
/// Between reports the time keeps counting, so a plugin that reports every few seconds is accurate to that interval.
/// A plugin that stops reporting for longer than <see cref="Stale"/> no longer counts as idle.
/// </summary>
public sealed class IdleTracker
{
    public static readonly TimeSpan Stale = TimeSpan.FromMinutes(2);

    private double? _seconds;
    private DateTimeOffset _at;

    public void Report(double seconds, DateTimeOffset now)
    {
        _seconds = Math.Max(0, seconds);
        _at = now;
    }

    public bool IsIdle(DateTimeOffset now, TimeSpan threshold)
    {
        if (_seconds is not { } seconds || now - _at > Stale)
        {
            return false;
        }

        return TimeSpan.FromSeconds(seconds) + (now - _at) >= threshold;
    }
}
