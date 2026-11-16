using DeskPal.Shared.Api;

namespace DeskPal.Client.Services;

/// <summary>Applies the account's away time. When the account has none yet, the local value is the one to upload.</summary>
public static class IdleSync
{
    /// <returns>true when <paramref name="settings"/> holds a value the account does not have yet.</returns>
    public static bool Apply(AppSettings settings, int? accountMinutes)
    {
        if (accountMinutes is int minutes && AccountIdle.IsAllowed(minutes))
        {
            settings.IdleMinutes = minutes;
            return false;
        }

        settings.IdleMinutes = AccountIdle.Normalize(settings.IdleMinutes);
        return true;
    }
}