using System.Globalization;
using System.Security.Cryptography;
using DeskPal.Client.Core.Usage;

namespace DeskPal.Client.Core.Personal;

/// <summary>
/// Syncs personal data through a folder that the user's own cloud drive or sync app copies between computers. Each
/// computer writes only its own file, <c>DeskPal\&lt;userId&gt;\personal\&lt;deviceId&gt;.json</c>, and reads the others'.
/// The folder may be the same one the usage stats sync through: those only read directories named after a computer.
/// </summary>
public sealed class PersonalFolderSync(Guid userId, Guid deviceId)
{
    public const string DirectoryName = "personal";

    private readonly Dictionary<string, string> _seen = [];
    private string? _written;

    public static string DirectoryOf(string folder, Guid userId) => Path.Combine(folder, FolderSync.RootName, userId.ToString("N"), DirectoryName);

    /// <summary>Whether files can be written to the folder.</summary>
    public bool CanUse(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return false;
            }

            var dir = DirectoryOf(folder, userId);
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Forgets what was read and written, e.g. after another folder was chosen.</summary>
    public void Reset()
    {
        _seen.Clear();
        _written = null;
    }

    /// <summary>Writes this computer's file when its content changed.</summary>
    /// <exception cref="IOException">The folder cannot be reached.</exception>
    public void Write(string folder, PersonalFile file)
    {
        var hash = Convert.ToHexString(SHA256.HashData((file with { SavedAt = default }).Serialize()));
        var dir = DirectoryOf(Existing(folder), userId);
        var path = Path.Combine(dir, $"{deviceId:N}.json");
        if (hash == _written && File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, file.Serialize());
        File.Move(temp, path, overwrite: true);
        _written = hash;
    }

    /// <summary>The other computers' files that changed since they were last read.</summary>
    /// <exception cref="IOException">The folder cannot be reached.</exception>
    public IReadOnlyList<PersonalFile> ReadChanged(string folder)
    {
        var dir = DirectoryOf(Existing(folder), userId);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var changed = new List<PersonalFile>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var device) || device == deviceId)
            {
                continue;
            }

            var info = new FileInfo(path);
            var stamp = string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}");
            if (_seen.TryGetValue(path, out var seen) && seen == stamp)
            {
                continue;
            }

            try
            {
                if (PersonalFile.Parse(File.ReadAllBytes(path)) is { } file)
                {
                    changed.Add(file);
                    _seen[path] = stamp;
                }
            }
            catch (IOException)
            {
                // Not fully copied by the sync app yet. Tried again next time.
            }
        }

        return changed;
    }

    private static string Existing(string folder) =>
        Directory.Exists(folder) ? folder : throw new DirectoryNotFoundException("The sync folder cannot be reached");
}