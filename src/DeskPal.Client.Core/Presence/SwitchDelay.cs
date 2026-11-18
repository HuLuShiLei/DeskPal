namespace DeskPal.Client.Core.Presence;

/// <summary>How long a new presence must stay unchanged before it is shared with the room. Usage stats are recorded without it.</summary>
public static class SwitchDelay
{
    public const int DefaultSeconds = 10;

    /// <summary>0 shares every change at once.</summary>
    public static int[] SecondOptions => DeskPal.Shared.Api.AccountPreferences.SwitchDelayOptions;

    /// <summary>Title-only changes are never shared more often than this, even with no delay.</summary>
    public static readonly TimeSpan MinDetailInterval = TimeSpan.FromSeconds(10);

    public static bool IsAllowed(int seconds) => SecondOptions.Contains(seconds);

    public static int Normalize(int seconds) => IsAllowed(seconds) ? seconds : DefaultSeconds;

    public static TimeSpan DetailInterval(int seconds) => TimeSpan.FromSeconds(Math.Max(seconds, MinDetailInterval.TotalSeconds));
}