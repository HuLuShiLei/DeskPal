using System.Security.Cryptography;
using System.Text.Json;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// Syncs the usage stats of the signed-in account with the encrypted copy on the server, when the user turned it on.
/// Runs at start, every 10 minutes and once more on exit.
/// </summary>
public sealed class StatsSyncService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly AppSettings _settings;
    private readonly ILogger<StatsSyncService> _logger;
    private readonly StatsSync _sync;
    private volatile bool _allDays = true;

    public StatsSyncService(UsageService usage, ApiClient api, AppSettings settings, ILogger<StatsSyncService> logger)
    {
        _settings = settings;
        _logger = logger;
        var keyPath = Path.Combine(SettingsStore.Directory, "usage", $"{settings.UserId:N}.sync-key");
        _sync = new StatsSync(usage.Store, new HttpSyncApi(api), new DpapiKeyStore(keyPath), () => UsageService.Today);
        _sync.Changed += () => Changed?.Invoke();
        usage.DaysChanged += () => _allDays = true;
    }

    public event Action? Changed;

    public StatsSyncMode Mode => _sync.Mode;

    /// <summary>Why the last sync failed, when it was a server limit the user can act on.</summary>
    public string? Error { get; private set; }

    /// <inheritdoc cref="StatsSync.EnableAsync"/>
    public async Task<bool> EnableAsync(string pin)
    {
        var enabled = await _sync.EnableAsync(pin);
        if (enabled)
        {
            _logger.LogInformation("Usage stats sync turned on");
            RunSoon();
        }

        return enabled;
    }

    public async Task<StatsSyncUnlock> UnlockAsync(string pin)
    {
        var result = await _sync.UnlockAsync(pin);
        if (result == StatsSyncUnlock.Unlocked)
        {
            _logger.LogInformation("Usage stats sync unlocked on this computer");
            RunSoon();
        }

        return result;
    }

    public async Task DisableAsync()
    {
        await _sync.DisableAsync();
        SetError(null);
        _logger.LogInformation("Usage stats sync turned off and the server copy deleted");
    }


    /// <summary>Signing out: uploads what is left, then this computer no longer keeps the key.</summary>
    public async Task SignOutAsync()
    {
        if (_sync.Mode == StatsSyncMode.On)
        {
            await Task.WhenAny(RunOnceAsync(), Task.Delay(TimeSpan.FromSeconds(5)));
        }

        _sync.ForgetKey();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_sync.Mode == StatsSyncMode.On && _settings.GetToken() is not null)
        {
            await Task.WhenAny(RunOnceAsync(), Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync();
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync();
        }
    }

    private void RunSoon() => _ = Task.Run(async () =>
    {
        _allDays = true;
        await RunOnceAsync();
    });

    private async Task RunOnceAsync()
    {
        if (_settings.GetToken() is null)
        {
            return;
        }

        var allDays = _allDays;
        _allDays = false;
        try
        {
            await _sync.SyncAsync(allDays);
            SetError(null);
        }
        catch (ApiException ex) when (ex.Status == 413)
        {
            SetError(ex.Message);
            _logger.LogWarning("Usage stats sync hit the server limit");
        }
        catch (ApiException ex)
        {
            _allDays |= allDays;
            _logger.LogWarning("Usage stats sync failed ({Status})", ex.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Usage stats sync failed");
        }
    }

    private void SetError(string? error)
    {
        if (Error != error)
        {
            Error = error;
            Changed?.Invoke();
        }
    }

    private sealed class HttpSyncApi(ApiClient api) : IStatsSyncApi
    {
        public Task<StatsSyncStatusDto> GetStatusAsync() => api.GetStatsSyncAsync();

        public async Task<StatsSyncStatusDto?> EnableAsync(EnableStatsSyncRequest request)
        {
            try
            {
                return await api.EnableStatsSyncAsync(request);
            }
            catch (ApiException ex) when (ex.Status == 409)
            {
                return null;
            }
        }

        public Task DisableAsync() => api.DisableStatsSyncAsync();

        public Task<StatsSyncDayInfo> UploadAsync(DateOnly date, byte[] data) => api.UploadStatsDayAsync(date, data);

        public async Task<StatsSyncDayDto?> DownloadAsync(DateOnly date)
        {
            try
            {
                return await api.DownloadStatsDayAsync(date);
            }
            catch (ApiException ex) when (ex.Status == 404)
            {
                return null;
            }
        }
    }

    /// <summary>The key, encrypted with Windows DPAPI for the current Windows user, next to the account's usage database.</summary>
    private sealed class DpapiKeyStore(string path) : IStatsSyncKeyStore
    {
        public StoredSyncKey? Load()
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var file = JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(path), DeskPalJson.Options);
                if (file is null)
                {
                    return null;
                }

                var key = ProtectedData.Unprotect(Convert.FromBase64String(file.Key), null, DataProtectionScope.CurrentUser);
                return new StoredSyncKey(file.Salt, key);
            }
            catch (Exception ex) when (ex is IOException or JsonException or FormatException or CryptographicException)
            {
                return null;
            }
        }

        public void Save(StoredSyncKey key)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new KeyFile(key.Salt, Convert.ToBase64String(ProtectedData.Protect(key.Key, null, DataProtectionScope.CurrentUser)));
            File.WriteAllText(path, JsonSerializer.Serialize(file, DeskPalJson.Options));
        }

        public void Delete()
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }

        private sealed record KeyFile(string Salt, string Key);
    }
}