using DeskPal.Client.Core.Personal;
using DeskPal.Shared.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// Where the personal data (personal rules, no-title rules, away time, preferences) goes besides this computer: the
/// server when account sync is on, a sync folder, or an exported file. Account sync and the sync folder exclude each other.
/// </summary>
public sealed class PersonalDataService : BackgroundService
{
    private static readonly TimeSpan FolderInterval = TimeSpan.FromMinutes(2);

    private readonly ApiClient _api;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly PersonalRules _rules;
    private readonly ILogger<PersonalDataService> _logger;
    private readonly PersonalFolderSync _folder;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PersonalDataService(ApiClient api, AppSettings settings, SettingsStore store, PersonalRules rules, ILogger<PersonalDataService> logger)
    {
        _api = api;
        _settings = settings;
        _store = store;
        _rules = rules;
        _logger = logger;
        if (settings.DeviceId == Guid.Empty)
        {
            settings.DeviceId = Guid.NewGuid();
            store.Save(settings);
        }

        _folder = new PersonalFolderSync(settings.UserId, settings.DeviceId);
        rules.Edited += () => _ = SyncFolderAsync();
    }

    /// <summary>Raised from any thread when the state shown in Settings changed.</summary>
    public event Action? Changed;

    /// <summary>Raised after preferences came from the server, a file or the sync folder, so the panel applies them.</summary>
    public event Action? PreferencesApplied;

    public bool AccountSync => _settings.PersonalSync;

    public string? Folder => _settings.PersonalFolder;

    /// <summary>The sync folder is set but cannot be reached right now, e.g. the drive is not connected.</summary>
    public bool FolderUnavailable { get; private set; }

    /// <summary>Uploads this computer's data, merges it with what the account has, and keeps the result here.</summary>
    public async Task EnableAccountSyncAsync()
    {
        var merged = await _api.EnablePersonalSyncAsync(Current());
        _settings.PersonalSync = true;
        _settings.PreferencesUnsynced = false;
        _store.Save(_settings);
        _rules.Replace(new PersonalData(merged.Rules, merged.NoTitleRules, _rules.Data.Tombstones));
        IdleSync.Apply(_settings, merged.IdleMinutes);
        await PreferencesSync.ApplyAsync(_settings, _store, _api, merged.Preferences);
        _logger.LogInformation("Personal data sync with the account turned on");
        PreferencesApplied?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Deletes the server's copy. This computer keeps its own.</summary>
    public async Task DisableAccountSyncAsync()
    {
        await _api.DisablePersonalSyncAsync();
        _settings.PersonalSync = false;
        _store.Save(_settings);
        _logger.LogInformation("Personal data sync with the account turned off and the server copy deleted");
        Changed?.Invoke();
    }

    public void Export(string path) => File.WriteAllBytes(path, CurrentFile().Serialize());

    /// <returns>How many personal rules and no-title rules the file had.</returns>
    /// <exception cref="InvalidDataException">The file is not a personal data export.</exception>
    public async Task<(int Rules, int NoTitle)> ImportAsync(string path)
    {
        var file = PersonalFile.Parse(await File.ReadAllBytesAsync(path)) ?? throw new InvalidDataException(Strings.PersonalImport_NotFile);
        var imported = file.Data;
        var now = DateTimeOffset.UtcNow;
        var merged = PersonalMerge.Import(_rules.Data, imported, now);
        if (_settings.PersonalSync)
        {
            var saved = await _api.EnablePersonalSyncAsync(new PersonalDataDto(merged.Rules, merged.NoTitleRules));
            _rules.Replace(new PersonalData(saved.Rules, saved.NoTitleRules, merged.Tombstones), edited: true);
        }
        else
        {
            _rules.Replace(merged, edited: true);
        }

        if (TakePreferences(file, now))
        {
            await UploadPreferencesAsync();
        }

        return (imported.Rules.Count, imported.NoTitleRules.Count);
    }

    public bool CanUseFolder(string folder) => _folder.CanUse(folder);

    /// <summary>Starts syncing through <paramref name="folder"/>: reads the other computers' files, then writes this one's.</summary>
    public async Task EnableFolderAsync(string folder)
    {
        _settings.PersonalFolder = Path.GetFullPath(folder);
        _store.Save(_settings);
        _folder.Reset();
        _logger.LogInformation("Personal data sync folder turned on");
        await SyncFolderAsync();
    }

    /// <summary>Stops reading and writing the folder. Its files stay.</summary>
    public void StopFolder()
    {
        _settings.PersonalFolder = null;
        _store.Save(_settings);
        _folder.Reset();
        FolderUnavailable = false;
        Changed?.Invoke();
    }

    /// <summary>The away time or a preference was changed on this computer.</summary>
    public void PreferencesEdited()
    {
        _settings.PreferencesUpdatedAt = DateTimeOffset.UtcNow;
        _store.Save(_settings);
        _ = SyncFolderAsync();
    }

    public Task SignOutAsync()
    {
        StopFolder();
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncFolderAsync();
        using var timer = new PeriodicTimer(FolderInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncFolderAsync();
        }
    }

    private async Task SyncFolderAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_settings.PersonalFolder is not { } folder)
            {
                return;
            }

            try
            {
                var now = DateTimeOffset.UtcNow;
                var data = _rules.Data;
                var preferences = false;
                var changed = await Task.Run(() => _folder.ReadChanged(folder));
                foreach (var file in changed)
                {
                    data = PersonalMerge.Merge(data, file.Data, now);
                    preferences |= file.PreferencesUpdatedAt > (_settings.PreferencesUpdatedAt ?? DateTimeOffset.MinValue) && TakePreferences(file, file.PreferencesUpdatedAt.Value);
                }

                if (changed.Count > 0)
                {
                    _rules.Replace(data);
                }

                if (preferences)
                {
                    await UploadPreferencesAsync();
                }

                var mine = CurrentFile();
                await Task.Run(() => _folder.Write(folder, mine));
                SetUnavailable(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Personal data sync folder cannot be reached ({Type})", ex.GetType().Name);
                SetUnavailable(true);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <returns>true when the file had preferences, which are now in the settings.</returns>
    private bool TakePreferences(PersonalFile file, DateTimeOffset changedAt)
    {
        var taken = false;
        if (file.Preferences is { } preferences && AccountPreferences.IsValid(preferences))
        {
            PreferencesSync.Take(_settings, preferences);
            taken = true;
        }

        if (file.IdleMinutes is int minutes && AccountIdle.IsAllowed(minutes))
        {
            _settings.IdleMinutes = minutes;
            taken = true;
        }

        if (taken)
        {
            _settings.PreferencesUpdatedAt = changedAt;
            _store.Save(_settings);
            PreferencesApplied?.Invoke();
        }

        return taken;
    }

    private async Task UploadPreferencesAsync()
    {
        if (!_settings.PersonalSync)
        {
            return;
        }

        await PreferencesSync.UploadAsync(_settings, _store, _api, PreferencesSync.Local(_settings));
        try
        {
            await _api.SetIdleMinutesAsync(AccountIdle.Normalize(_settings.IdleMinutes));
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("Saving away time failed ({Status})", ex.Status);
        }
    }

    private void SetUnavailable(bool unavailable)
    {
        if (FolderUnavailable != unavailable)
        {
            FolderUnavailable = unavailable;
            Changed?.Invoke();
        }
    }

    private PersonalDataDto Current() =>
        new(_rules.Rules, _rules.NoTitleItems, PreferencesSync.Local(_settings), AccountIdle.Normalize(_settings.IdleMinutes));

    private PersonalFile CurrentFile() =>
        PersonalFile.Create(_rules.Data, PreferencesSync.Local(_settings), AccountIdle.Normalize(_settings.IdleMinutes), _settings.PreferencesUpdatedAt, DateTimeOffset.UtcNow);
}