using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeskPal.Shared;

namespace DeskPal.Client.Core.Usage;

/// <summary>Where this computer keeps the sync folder and key of the signed-in account.</summary>
public interface IFolderSyncStore
{
    FolderSyncConfig? Load();

    void Save(FolderSyncConfig config);

    void Delete();
}

/// <param name="Folder">The folder the user chose; the files go into <c>DeskPal\&lt;userId&gt;</c> inside it.</param>
/// <param name="Salt">The salt in the folder's key file when the PIN was entered. Another salt there means the folder was set up again.</param>
public sealed record FolderSyncConfig(string Folder, string Salt, byte[] Key);

public enum FolderSyncMode
{
    Off,
    On,

    /// <summary>On, but the folder cannot be reached right now, e.g. the drive is not connected.</summary>
    Unavailable,
}

public enum FolderCheck
{
    /// <summary>Nothing synced there yet: the user sets a new PIN.</summary>
    New,

    /// <summary>Another computer already syncs there: the user enters its PIN.</summary>
    Existing,
    NotWritable,
}

public enum FolderSyncEnable
{
    Enabled,
    WrongPin,
    NotWritable,
}

/// <summary>
/// Syncs usage stats through a folder that the user's own cloud drive or sync app copies between computers. The server is
/// not involved. Each computer writes only its own records, one encrypted file per day under its own directory, and
/// replaces what it has of another computer with that computer's files, so no file is written by two computers.
/// </summary>
public sealed partial class FolderSync
{
    public const string RootName = "DeskPal";
    private const string KeyFileName = "key.json";
    private const string Extension = ".dps";

    private readonly SqliteUsageStore _db;
    private readonly IFolderSyncStore _store;
    private readonly Guid _userId;
    private readonly Func<DateOnly> _today;
    private readonly StatsSyncCrypto.KdfParameters? _kdf;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FolderSyncConfig? _config;

    /// <param name="kdf">Key derivation used when a folder is set up here; null for <see cref="StatsSyncCrypto.DefaultParameters"/>.</param>
    public FolderSync(SqliteUsageStore db, IFolderSyncStore store, Guid userId, Func<DateOnly> today, StatsSyncCrypto.KdfParameters? kdf = null)
    {
        _db = db;
        _store = store;
        _userId = userId;
        _today = today;
        _kdf = kdf;
        _config = store.Load();
        Mode = _config is null ? FolderSyncMode.Off : FolderSyncMode.On;
    }

    public event Action? Changed;

    /// <summary>Last known state. Starts from the stored settings and is corrected by the next <see cref="SyncAsync"/>.</summary>
    public FolderSyncMode Mode { get; private set; }

    public string? Folder => _config?.Folder;

    /// <summary>Whether the folder can be used, and whether another computer already syncs there.</summary>
    public FolderCheck Check(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return FolderCheck.NotWritable;
            }

            var root = Root(folder);
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return File.Exists(Path.Combine(root, KeyFileName)) ? FolderCheck.Existing : FolderCheck.New;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return FolderCheck.NotWritable;
        }
    }

    /// <summary>Starts syncing with <paramref name="folder"/>: sets it up with the PIN, or checks the PIN of the computer that set it up.</summary>
    /// <exception cref="ArgumentException">The PIN is not 8 digits.</exception>
    public async Task<FolderSyncEnable> EnableAsync(string folder, string pin)
    {
        if (!StatsSyncCrypto.IsValidPin(pin))
        {
            throw new ArgumentException("PIN must be 8 digits", nameof(pin));
        }

        if (Check(folder) == FolderCheck.NotWritable)
        {
            return FolderSyncEnable.NotWritable;
        }

        await _gate.WaitAsync();
        try
        {
            var keyPath = Path.Combine(Root(folder), KeyFileName);
            string salt;
            byte[] key;
            if (ReadKeyFile(keyPath) is { } existing)
            {
                salt = existing.Salt;
                key = await Task.Run(() => StatsSyncCrypto.DeriveKey(pin, salt));
                if (!StatsSyncCrypto.Verify(key, existing.KeyCheck))
                {
                    return FolderSyncEnable.WrongPin;
                }
            }
            else
            {
                salt = StatsSyncCrypto.NewSalt(_kdf);
                key = await Task.Run(() => StatsSyncCrypto.DeriveKey(pin, salt));
                WriteAtomically(keyPath, JsonSerializer.SerializeToUtf8Bytes(new KeyFile(salt, StatsSyncCrypto.KeyCheck(key)), DeskPalJson.Options));
            }

            _config = new FolderSyncConfig(Path.GetFullPath(folder), salt, key);
            _store.Save(_config);
            _db.ResetFolderSync();
            SetMode(FolderSyncMode.On);
            return FolderSyncEnable.Enabled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FolderSyncEnable.NotWritable;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops reading and writing the folder. The files in it and the stats on this computer stay.</summary>
    public async Task DisableAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Forget();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads the files other computers changed, then writes the days of this computer that changed.</summary>
    /// <param name="allDays">Write every local day, not only today and yesterday.</param>
    public async Task SyncAsync(bool allDays)
    {
        await _gate.WaitAsync();
        try
        {
            if (_config is not { } config)
            {
                return;
            }

            if (!Directory.Exists(config.Folder))
            {
                SetMode(FolderSyncMode.Unavailable);
                return;
            }

            var root = Root(config.Folder);
            var keyFile = ReadKeyFile(Path.Combine(root, KeyFileName));
            if (keyFile is null || keyFile.Salt != config.Salt)
            {
                Forget();
                return;
            }

            Read(root, config.Key);
            Write(root, config.Key, allDays);
            SetMode(FolderSyncMode.On);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetMode(FolderSyncMode.Unavailable);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Read(string root, byte[] key)
    {
        var stamps = _db.FolderRead();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out var device) || device == _db.DeviceId)
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*" + Extension))
            {
                if (ParseDay(Path.GetFileName(file)) is not { } date)
                {
                    continue;
                }

                var info = new FileInfo(file);
                var stamp = string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}");
                if (stamps.TryGetValue((device, date), out var seen) && seen == stamp)
                {
                    continue;
                }

                try
                {
                    var plain = StatsSyncCrypto.Open(key, File.ReadAllBytes(file));
                    if (JsonSerializer.Deserialize<UsageExport>(plain, DeskPalJson.Options) is not { Segments: not null, Rollups: not null } day)
                    {
                        continue;
                    }

                    _db.ReplaceDay(date, device, day);
                    _db.SetFolderRead(device, date, stamp);
                }
                catch (Exception ex) when (ex is CryptographicException or InvalidDataException or JsonException or IOException)
                {
                    // Not fully copied by the sync app yet, or not ours. Tried again next time.
                }
            }
        }
    }

    private void Write(string root, byte[] key, bool allDays)
    {
        var dir = Path.Combine(root, _db.DeviceId.ToString("N"));
        Directory.CreateDirectory(dir);
        var written = _db.FolderWritten();
        IEnumerable<DateOnly> dates = allDays ? _db.Dates(_db.DeviceId) : [_today().AddDays(-1), _today()];
        foreach (var date in dates)
        {
            var day = _db.ExportDay(date, _db.DeviceId);
            if (day.Segments.Count == 0 && day.Rollups.Count == 0)
            {
                continue;
            }

            var plain = JsonSerializer.SerializeToUtf8Bytes(day, DeskPalJson.Options);
            var hash = Convert.ToHexString(SHA256.HashData(plain));
            var path = Path.Combine(dir, DayFileName(date));
            if (written.TryGetValue(date, out var last) && last == hash && File.Exists(path))
            {
                continue;
            }

            WriteAtomically(path, StatsSyncCrypto.Seal(key, plain));
            _db.SetFolderWritten(date, hash);
        }
    }

    private void Forget()
    {
        _store.Delete();
        _config = null;
        _db.ResetFolderSync();
        SetMode(FolderSyncMode.Off);
    }

    private void SetMode(FolderSyncMode mode)
    {
        if (Mode != mode)
        {
            Mode = mode;
            Changed?.Invoke();
        }
    }

    private string Root(string folder) => Path.Combine(folder, RootName, _userId.ToString("N"));

    /// <summary>Written next to the target and then moved over it, so a sync app never copies half a file.</summary>
    private static void WriteAtomically(string path, byte[] data)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }

    private static KeyFile? ReadKeyFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<KeyFile>(File.ReadAllBytes(path), DeskPalJson.Options) is { Salt: not null, KeyCheck: not null } file ? file : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string DayFileName(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension;

    /// <returns>null for anything else, e.g. the copies a sync app makes on a conflict ("2026-11-13 (1).dps").</returns>
    private static DateOnly? ParseDay(string fileName) =>
        DayFile().IsMatch(fileName) && DateOnly.TryParseExact(fileName[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}\.dps$")]
    private static partial Regex DayFile();

    private sealed record KeyFile(string Salt, string KeyCheck);
}