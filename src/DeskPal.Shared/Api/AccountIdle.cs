namespace DeskPal.Shared.Api;

/// <summary>How long without input counts as away. The same choices on every computer of the account.</summary>
public static class AccountIdle
{
    public const int DefaultMinutes = 5;

    public static readonly int[] MinuteOptions = [1, 5, 10, 15, 30];

    public static bool IsAllowed(int minutes) => MinuteOptions.Contains(minutes);

    public static int Normalize(int minutes) => IsAllowed(minutes) ? minutes : DefaultMinutes;
}