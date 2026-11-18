using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Preferences;

/// <summary>
/// Decides, when signing in, between this computer's settings and the ones on the account. The account wins, except for
/// fields it does not have yet and changes made here while the server could not be reached; those are uploaded.
/// </summary>
public static class PreferencesResolver
{
    /// <param name="local">This computer's values, every field set.</param>
    /// <param name="unsynced">Something was changed here while the server could not be reached.</param>
    /// <returns>The values to use here, and what to upload; null when the account already has everything.</returns>
    public static (UserPreferences Use, UserPreferences? Upload) Resolve(UserPreferences local, UserPreferences? account, bool unsynced)
    {
        if (unsynced)
        {
            return (local, local);
        }

        account ??= new UserPreferences();
        var use = new UserPreferences(
            account.SwitchDelaySeconds is int delay && AccountPreferences.SwitchDelayOptions.Contains(delay) ? delay : local.SwitchDelaySeconds,
            Known(account.Theme, AccountPreferences.Themes) ?? local.Theme,
            Known(account.Language, AccountPreferences.Languages) ?? local.Language,
            Known(account.CloseAction, AccountPreferences.CloseActions) ?? local.CloseAction,
            account.Topmost ?? local.Topmost,
            account.MemberOrder ?? local.MemberOrder);
        var upload = new UserPreferences(
            account.SwitchDelaySeconds is null ? local.SwitchDelaySeconds : null,
            account.Theme is null ? local.Theme : null,
            account.Language is null ? local.Language : null,
            account.CloseAction is null ? local.CloseAction : null,
            account.Topmost is null ? local.Topmost : null,
            account.MemberOrder is null && local.MemberOrder is { Count: > 0 } ? local.MemberOrder : null);
        return (use, upload == new UserPreferences() ? null : upload);
    }

    /// <summary>The value when it is one of <paramref name="options"/>, otherwise the first option, which is the default.</summary>
    public static string Pick(string? value, string[] options) => Known(value, options) ?? options[0];

    private static string? Known(string? value, string[] options) => value is not null && options.Contains(value) ? value : null;
}