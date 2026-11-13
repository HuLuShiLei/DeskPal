using System.Security.Cryptography;
using System.Text.Json;
using DeskPal.Shared;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Usage;

/// <summary>The server side of usage stats sync, implemented over HTTP by the client.</summary>
public interface IStatsSyncApi
{
    Task<StatsSyncStatusDto> GetStatusAsync();

    /// <returns>null when sync is already on for the account, turned on from another computer.</returns>
    Task<StatsSyncStatusDto?> EnableAsync(EnableStatsSyncRequest request);

    Task DisableAsync();

    Task<StatsSyncDayInfo> UploadAsync(DateOnly date, byte[] data);

    Task<StatsSyncDayDto?> DownloadAsync(DateOnly date);
}

/// <summary>Where this computer keeps the sync key of the signed-in account.</summary>
public interface IStatsSyncKeyStore
{
    StoredSyncKey? Load();

    void Save(StoredSyncKey key);

    void Delete();
}

/// <param name="Salt">The salt the key was derived with. A different salt on the server means sync was turned on again with another PIN.</param>
public sealed record StoredSyncKey(string Salt, byte[] Key);

public enum StatsSyncMode
{
    Off,

    /// <summary>On for the account, but this computer does not have the key yet.</summary>
    NeedsPin,
    On,
}

public enum StatsSyncUnlock
{
    Unlocked,
    WrongPin,
    NotEnabled,
}

/// <summary>
/// Keeps the usage stats of this computer and the encrypted copy on the server in step. Downloads days that changed on the
/// server first and takes the other computers' records from them (<see cref="SqliteUsageStore.MergeDay"/>), then uploads
/// days whose content differs from what the server has.
/// </summary>
public sealed class StatsSync
{
    private readonly SqliteUsageStore _db;
    private readonly IStatsSyncApi _api;
    private readonly IStatsSyncKeyStore _keys;
    private readonly Func<DateOnly> _today;
    private readonly StatsSyncCrypto.KdfParameters? _kdf;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StoredSyncKey? _key;

    /// <param name="kdf">Key derivation used when sync is turned on here; null for <see cref="StatsSyncCrypto.DefaultParameters"/>.</param>
    public StatsSync(SqliteUsageStore db, IStatsSyncApi api, IStatsSyncKeyStore keys, Func<DateOnly> today, StatsSyncCrypto.KdfParameters? kdf = null)
    {
        _db = db;
        _api = api;
        _keys = keys;
        _today = today;
        _kdf = kdf;
        _key = keys.Load();
        Mode = _key is null ? StatsSyncMode.Off : StatsSyncMode.On;
    }

    public event Action? Changed;

    /// <summary>Last known state. Starts from the stored key and is corrected by the next <see cref="SyncAsync"/>.</summary>
    public StatsSyncMode Mode { get; private set; }

    /// <summary>Reads the server state; when on, downloads newer days and uploads changed ones.</summary>
    /// <param name="allDays">Check every local day, not only today and yesterday.</param>
    public async Task SyncAsync(bool allDays)
    {
        await _gate.WaitAsync();
        try
        {
            var status = await _api.GetStatusAsync();
            if (Apply(status))
            {
                await DownloadAsync(status);
                IEnumerable<DateOnly> dates = allDays ? _db.Dates() : [_today().AddDays(-1), _today()];
                var stored = status.Days.Select(d => d.Date).ToHashSet();
                var missing = _db.SyncMarks().Keys.Where(d => !stored.Contains(d));
                await UploadAsync(dates.Union(missing), stored);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <returns>false when sync was already turned on from another computer; the mode is then <see cref="StatsSyncMode.NeedsPin"/>.</returns>
    /// <exception cref="ArgumentException">The PIN is not 8 digits.</exception>
    public async Task<bool> EnableAsync(string pin)
    {
        if (!StatsSyncCrypto.IsValidPin(pin))
        {
            throw new ArgumentException("PIN must be 8 digits", nameof(pin));
        }

        var salt = StatsSyncCrypto.NewSalt(_kdf);
        var key = await Task.Run(() => StatsSyncCrypto.DeriveKey(pin, salt));
        await _gate.WaitAsync();
        try
        {
            if (await _api.EnableAsync(new EnableStatsSyncRequest(salt, StatsSyncCrypto.KeyCheck(key))) is null)
            {
                Apply(await _api.GetStatusAsync());
                return false;
            }

            Unlock(new StoredSyncKey(salt, key));
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Enters the PIN on a computer where sync was turned on elsewhere.</summary>
    public async Task<StatsSyncUnlock> UnlockAsync(string pin)
    {
        await _gate.WaitAsync();
        try
        {
            var status = await _api.GetStatusAsync();
            if (!status.Enabled || status.Salt is null || status.KeyCheck is null)
            {
                Apply(status);
                return StatsSyncUnlock.NotEnabled;
            }

            if (!StatsSyncCrypto.IsValidPin(pin))
            {
                return StatsSyncUnlock.WrongPin;
            }

            var key = await Task.Run(() => StatsSyncCrypto.DeriveKey(pin, status.Salt));
            if (!StatsSyncCrypto.Verify(key, status.KeyCheck))
            {
                return StatsSyncUnlock.WrongPin;
            }

            Unlock(new StoredSyncKey(status.Salt, key));
            return StatsSyncUnlock.Unlocked;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Turns sync off for the account. The server deletes its copy; the stats on this computer stay.</summary>
    public async Task DisableAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await _api.DisableAsync();
            Forget(StatsSyncMode.Off);
        }
        finally
        {
            _gate.Release();
        }
    }


    /// <summary>Drops the key on this computer, e.g. when signing out. The server copy stays.</summary>
    public void ForgetKey()
    {
        _keys.Delete();
        _key = null;
        SetMode(StatsSyncMode.Off);
    }

    private void Unlock(StoredSyncKey key)
    {
        _keys.Save(key);
        _key = key;
        _db.ResetSync();
        SetMode(StatsSyncMode.On);
    }

    private void Forget(StatsSyncMode mode)
    {
        if (_key is not null)
        {
            _keys.Delete();
            _key = null;
        }

        _db.ResetSync();
        SetMode(mode);
    }

    /// <returns>Whether this computer can sync now.</returns>
    private bool Apply(StatsSyncStatusDto status)
    {
        if (!status.Enabled)
        {
            if (_key is not null)
            {
                Forget(StatsSyncMode.Off);
            }

            SetMode(StatsSyncMode.Off);
            return false;
        }

        if (_key is null || _key.Salt != status.Salt)
        {
            Forget(StatsSyncMode.NeedsPin);
            return false;
        }

        SetMode(StatsSyncMode.On);
        return true;
    }

    private async Task DownloadAsync(StatsSyncStatusDto status)
    {
        var marks = _db.SyncMarks();
        foreach (var day in status.Days)
        {
            if (marks.TryGetValue(day.Date, out var mark) && mark.Revision >= day.Revision)
            {
                continue;
            }

            if (await _api.DownloadAsync(day.Date) is not { } stored)
            {
                continue;
            }

            var plain = StatsSyncCrypto.Open(_key!.Key, Convert.FromBase64String(stored.Data));
            if (JsonSerializer.Deserialize<UsageExport>(plain, DeskPalJson.Options) is { Segments: not null, Rollups: not null } data)
            {
                _db.MergeDay(day.Date, data);
            }

            _db.SetSyncMark(day.Date, new SyncMark(Hash(plain), stored.Revision));
        }
    }

    /// <param name="stored">Dates on the server. A marked date missing there is sent again.</param>
    private async Task UploadAsync(IEnumerable<DateOnly> dates, IReadOnlySet<DateOnly> stored)
    {
        var marks = _db.SyncMarks();
        foreach (var date in dates)
        {
            var day = _db.ExportDay(date);
            if (day.Segments.Count == 0 && day.Rollups.Count == 0)
            {
                continue;
            }

            var plain = JsonSerializer.SerializeToUtf8Bytes(day, DeskPalJson.Options);
            var hash = Hash(plain);
            if (marks.TryGetValue(date, out var mark) && mark.Hash == hash && stored.Contains(date))
            {
                continue;
            }

            var saved = await _api.UploadAsync(date, StatsSyncCrypto.Seal(_key!.Key, plain));
            _db.SetSyncMark(date, new SyncMark(hash, saved.Revision));
        }
    }

    private void SetMode(StatsSyncMode mode)
    {
        if (Mode != mode)
        {
            Mode = mode;
            Changed?.Invoke();
        }
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}