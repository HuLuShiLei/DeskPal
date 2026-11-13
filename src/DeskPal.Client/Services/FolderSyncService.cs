using System.Security.Cryptography;
using System.Text.Json;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// Syncs the usage stats of the signed-in account through the folder the user chose, when the user turned it on.
/// Runs at start, every 10 minutes and once more on exit.
/// </summary>
public sealed class FolderSyncService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly AppSettings _settings;
    private readonly ILogger<FolderSyncService> _logger;
    private readonly FolderSync _sync;
    private volatile bool _allDays = true;

    public FolderSyncService(UsageService usage, AppSettings settings, ILogger<FolderSyncService> logger)
    {
        _settings = settings;
        _logger = logger;
        var path = Path.Combine(SettingsStore.Directory, "usage", $"{settings.UserId:N}.folder-sync");
        _sync = new FolderSync(usage.Store, new DpapiFolderStore(path), settings.UserId, () => UsageService.Today);
        _sync.Changed += () => Changed?.Invoke();
        usage.DaysChanged += () => _allDays = true;
    }

    public event Action? Changed;

    public FolderSyncMode Mode => _sync.Mode;

    public string? Folder => _sync.Folder;

    public FolderCheck Check(string folder) => _sync.Check(folder);

    public async Task<FolderSyncEnable> EnableAsync(string folder, string pin)
    {
        var result = await _sync.EnableAsync(folder, pin);
        if (result == FolderSyncEnable.Enabled)
        {
            _logger.LogInformation("Usage stats folder sync turned on");
            RunSoon();
        }

        return result;
    }

    public async Task DisableAsync()
    {
        await _sync.DisableAsync();
        _logger.LogInformation("Usage stats folder sync stopped");
    }

    /// <summary>Signing out: writes what is left, then this computer no longer keeps the folder and the key.</summary>
    public async Task SignOutAsync()
    {
        if (_sync.Mode != FolderSyncMode.Off)
        {
            await Task.WhenAny(RunOnceAsync(), Task.Delay(TimeSpan.FromSeconds(5)));
        }

        await _sync.DisableAsync();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_sync.Mode != FolderSyncMode.Off)
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
            await Task.Run(() => _sync.SyncAsync(allDays));
            if (_sync.Mode == FolderSyncMode.Unavailable)
            {
                _allDays |= allDays;
            }
        }
        catch (Exception ex)
        {
            _allDays |= allDays;
            _logger.LogWarning(ex, "Usage stats folder sync failed");
        }
    }

    /// <summary>The folder and the key, the key encrypted with Windows DPAPI for the current Windows user.</summary>
    private sealed class DpapiFolderStore(string path) : IFolderSyncStore
    {
        public FolderSyncConfig? Load()
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var file = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path), DeskPalJson.Options);
                if (file is null)
                {
                    return null;
                }

                var key = ProtectedData.Unprotect(Convert.FromBase64String(file.Key), null, DataProtectionScope.CurrentUser);
                return new FolderSyncConfig(file.Folder, file.Salt, key);
            }
            catch (Exception ex) when (ex is IOException or JsonException or FormatException or CryptographicException)
            {
                return null;
            }
        }

        public void Save(FolderSyncConfig config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new ConfigFile(config.Folder, config.Salt, Convert.ToBase64String(ProtectedData.Protect(config.Key, null, DataProtectionScope.CurrentUser)));
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

        private sealed record ConfigFile(string Folder, string Salt, string Key);
    }
}