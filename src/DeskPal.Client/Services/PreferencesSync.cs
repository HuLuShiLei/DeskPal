using DeskPal.Client.Core.Preferences;
using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Services;

/// <summary>Moves the settings that follow the account between <see cref="AppSettings"/> and the server.</summary>
public static class PreferencesSync
{
    public static UserPreferences Local(AppSettings s) => new(
        SwitchDelay.Normalize(s.SwitchDelaySeconds),
        PreferencesResolver.Pick(s.Theme, AccountPreferences.Themes),
        PreferencesResolver.Pick(s.Language, AccountPreferences.Languages),
        PreferencesResolver.Pick(s.CloseAction, AccountPreferences.CloseActions),
        s.Topmost,
        s.MemberOrder.Where(o => o.Value.Count > 0).ToDictionary(o => o.Key, o => o.Value));

    /// <summary>Applies the account's values after signing in and uploads what the account is missing.</summary>
    public static async Task ApplyAsync(AppSettings settings, SettingsStore store, ApiClient api, UserPreferences? account)
    {
        var (use, upload) = PreferencesResolver.Resolve(Local(settings), account, settings.PreferencesUnsynced);
        settings.SwitchDelaySeconds = use.SwitchDelaySeconds!.Value;
        settings.Theme = use.Theme!;
        settings.Language = use.Language!;
        settings.CloseAction = use.CloseAction!;
        settings.Topmost = use.Topmost!.Value;
        settings.MemberOrder = use.MemberOrder!.ToDictionary(o => o.Key, o => o.Value.ToList());
        store.Save(settings);
        if (upload is not null)
        {
            await UploadAsync(settings, store, api, upload);
        }
    }

    /// <summary>
    /// Sends a change that is already saved here. When the server cannot be reached, all the local values are sent at the
    /// next sign-in instead of being replaced by the account's.
    /// </summary>
    public static async Task UploadAsync(AppSettings settings, SettingsStore store, ApiClient api, UserPreferences change)
    {
        try
        {
            await api.UpdatePreferencesAsync(settings.PreferencesUnsynced ? Local(settings) : change);
            if (settings.PreferencesUnsynced)
            {
                settings.PreferencesUnsynced = false;
                store.Save(settings);
            }
        }
        catch (ApiException ex) when (ex.Status == 0 || ex.Status >= 500)
        {
            settings.PreferencesUnsynced = true;
            store.Save(settings);
        }
    }
}