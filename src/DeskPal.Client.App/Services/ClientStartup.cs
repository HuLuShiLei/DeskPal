using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Diagnostics;
using DeskPal.Client.Core.Personal;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Core.Updates;
using DeskPal.Client.Platform;
using DeskPal.Client.Providers;
using DeskPal.Client.Ui;
using DeskPal.Shared.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// Startup steps shared by the Windows, macOS and Linux clients: restoring the saved session, signing in from the
/// login page, registering the background services and the plugin host, and loading the builtin rules.
/// The platform adds its official plugin (its collectors) and its windows.
/// </summary>
public static class ClientStartup
{
    /// <summary>Everything except the platform's official plugin and the windows.</summary>
    public static IServiceCollection AddDeskPalClient(
        this IServiceCollection s,
        AppSettings settings,
        SettingsStore store,
        ApiClient api,
        ClientLogFiles logs,
        IClientPlatform platform)
    {
        s.AddSingleton(logs);
        s.AddSingleton(settings);
        s.AddSingleton(store);
        s.AddSingleton(api);
        s.AddSingleton(platform);
        s.AddSingleton<ActivityBus>();
        s.AddSingleton<LayeredClassifier>();
        s.AddSingleton(new PersonalDataStore(Path.Combine(SettingsStore.Directory, "personal")));
        s.AddSingleton<UnclassifiedUsage>();
        s.AddSingleton<UsageService>();
        s.AddSingleton<StatsExchange>();
        s.AddSingleton<StatsSyncService>();
        s.AddSingleton<FolderSyncService>();
        s.AddSingleton<PersonalRules>();
        s.AddSingleton<PersonalDataService>();
        s.AddSingleton<BuiltinRuleCache>();
        s.AddSingleton<HubService>();
        s.AddSingleton<RoomState>();
        s.AddSingleton<PresenceCoordinator>();
        s.AddHostedService<UnclassifiedUsageStore>();
        s.AddHostedService(sp => sp.GetRequiredService<PresenceCoordinator>());
        s.AddHostedService<UsageReporter>();
        s.AddHostedService(sp => sp.GetRequiredService<StatsSyncService>());
        s.AddHostedService(sp => sp.GetRequiredService<FolderSyncService>());
        s.AddHostedService(sp => sp.GetRequiredService<PersonalDataService>());

        // After the presence coordinator, so it is listening on the bus before the first plugin reports.
        s.AddPluginHost();
        s.AddSingleton<UpdateService>();
        s.AddSingleton(sp => new WhatsNewStore(
            ReadChangelog(),
            Path.Combine(SettingsStore.Directory, "changelog-seen.json"),
            sp.GetRequiredService<UpdateService>().VersionLabel));
        s.AddSingleton<PinnedMembers>();
        s.AddSingleton<PanelBridge>();
        return s;
    }

    /// <summary>
    /// Checks the saved token with the server and refreshes the account's settings. Returns false when the login page
    /// has to be shown; <paramref name="notice"/> then says why, when there is a reason worth telling.
    /// </summary>
    public static async Task<bool> RestoreSessionAsync(AppSettings settings, SettingsStore store, ApiClient api, ClientLogFiles logs, Ref<string?> notice)
    {
        if (settings.GetToken() is null)
        {
            return false;
        }

        try
        {
            var me = await api.MeAsync();
            settings.DisplayName = me.DisplayName;
            settings.ShareUsage = me.ShareUsage;
            settings.IsAdmin = me.IsAdmin;
            settings.PersonalSync = me.PersonalSync;
            var uploadIdle = IdleSync.Apply(settings, me.IdleMinutes);
            await PreferencesSync.ApplyAsync(settings, store, api, me.Preferences);
            if (uploadIdle)
            {
                try
                {
                    await api.SetIdleMinutesAsync(settings.IdleMinutes);
                }
                catch (ApiException ex)
                {
                    logs.Write("WRN", "DeskPal", $"Saving away time failed ({ex.Status})");
                }
            }

            logs.Write("INF", "DeskPal", "Saved session restored");
            return true;
        }
        catch (ApiException ex) when (ex.Status == 0)
        {
            logs.Write("INF", "DeskPal", "Saved session kept because the server was unreachable");
            return true;
        }
        catch (ApiException ex)
        {
            logs.Write("WRN", "DeskPal", $"Saved session rejected ({ex.Status})");
            if (ex.Status == 401)
            {
                notice.Value ??= Strings.Login_SessionExpired;
            }

            settings.SetToken(null);
            store.Save(settings);
            return false;
        }
    }

    /// <summary>Carries out auth.login and auth.register from the login page.</summary>
    public static async Task<CommandResult> SubmitLoginAsync(AppSettings settings, SettingsStore store, ApiClient api, WebCommand c)
    {
        if (c.Type is not ("auth.login" or "auth.register"))
        {
            return CommandResult.Fail(UiLanguage.Format(Strings.Error_Unsupported, c.Type));
        }

        var register = c.Type == "auth.register";
        var displayName = c.String("displayName").Trim();
        if (register && displayName.Length == 0)
        {
            return CommandResult.Fail(Strings.Login_DisplayNameRequired);
        }

        try
        {
            var response = register
                ? await api.RegisterAsync(new RegisterRequest(c.String("userName"), displayName, c.String("password"), c.OptionalString("signupCode")))
                : await api.LoginAsync(new LoginRequest(c.String("userName"), c.String("password")));
            if (settings.UserId != response.UserId)
            {
                settings.PreferencesUnsynced = false;
                settings.PersonalFolder = null;
            }

            settings.UserId = response.UserId;
            settings.UserName = response.UserName;
            settings.DisplayName = response.DisplayName;
            settings.ShareUsage = response.ShareUsage;
            settings.IsAdmin = response.IsAdmin;
            settings.PersonalSync = response.PersonalSync;
            settings.SetToken(response.Token);
            if (IdleSync.Apply(settings, response.IdleMinutes))
            {
                await api.SetIdleMinutesAsync(settings.IdleMinutes);
            }

            await PreferencesSync.ApplyAsync(settings, store, api, response.Preferences);
            return CommandResult.Success;
        }
        catch (ApiException ex)
        {
            return CommandResult.Fail(ex.Message);
        }
    }

    /// <summary>Connects the services to each other once the host is built. Call before the host starts.</summary>
    public static void Wire(IServiceProvider services)
    {
        var hub = services.GetRequiredService<HubService>();
        var rooms = services.GetRequiredService<RoomState>();
        var personalRules = services.GetRequiredService<PersonalRules>();
        hub.Received += rooms.Handle;
        hub.Received += personalRules.Handle;
        hub.Received += services.GetRequiredService<StatsExchange>().Handle;
        hub.Connected += personalRules.RefreshAsync;
        LoadCachedBuiltinRules(services);
        personalRules.Load();
    }

    public static void LoadCachedBuiltinRules(IServiceProvider services)
    {
        var cached = services.GetRequiredService<BuiltinRuleCache>().Load();
        var classifier = services.GetRequiredService<LayeredClassifier>();
        classifier.SetBuiltin(new RuleSet(cached.Rules));
        classifier.SetFolders(cached.Folders);
        AppNames.SetBuiltin(cached.Rules);
    }

    public static async Task RefreshBuiltinRulesAsync(IServiceProvider services)
    {
        var cache = services.GetRequiredService<BuiltinRuleCache>();
        var cached = cache.Load();
        try
        {
            // A cache saved before the rules had folders asks for everything again.
            var response = await services.GetRequiredService<ApiClient>().GetBuiltinRulesAsync(cached.Folders is null ? 0 : cached.Version);
            if (response.Rules is not null)
            {
                cache.Save(new RuleSetDto(response.Version, response.Rules, response.Folders));
                var classifier = services.GetRequiredService<LayeredClassifier>();
                classifier.SetBuiltin(new RuleSet(response.Rules));
                classifier.SetFolders(response.Folders);
                AppNames.SetBuiltin(response.Rules);
                services.GetRequiredService<PresenceCoordinator>().Reevaluate();
            }
        }
        catch (ApiException ex)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("DeskPal").LogWarning(ex, "Refreshing builtin rules failed");
        }
    }

    /// <summary>Retries for a while when the server is unreachable. Returns the last error when it gives up.</summary>
    public static async Task<ApiException?> RefreshRoomsAsync(RoomState rooms)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await rooms.RefreshAsync();
                return null;
            }
            catch (ApiException) when (attempt < 20)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * (attempt + 1))));
            }
            catch (ApiException ex)
            {
                return ex;
            }
        }
    }

    /// <summary>Signing out: lets the sync services finish and forget their keys.</summary>
    public static async Task SignOutServicesAsync(IServiceProvider services)
    {
        await services.GetRequiredService<StatsSyncService>().SignOutAsync();
        await services.GetRequiredService<FolderSyncService>().SignOutAsync();
        await services.GetRequiredService<PersonalDataService>().SignOutAsync();
    }

    /// <summary>Next to the exe on Windows; in Contents/Resources of the app bundle on macOS.</summary>
    private static string? ReadChangelog()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, Changelog.FileName),
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", Changelog.FileName),
        ];
        try
        {
            return candidates.FirstOrDefault(File.Exists) is { } path ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

/// <summary>An out value for async methods.</summary>
public sealed class Ref<T>(T value)
{
    public T Value { get; set; } = value;
}
