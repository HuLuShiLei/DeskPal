using System.Text.RegularExpressions;

namespace DeskPal.Client.Core.Classification;

/// <summary>
/// Readable names for games recognized without a rule. Game executables often describe themselves as
/// "b1-Win64-Shipping", an engine name, or nothing at all; the Steam manifest or the library folder names the game instead.
/// Order: Steam manifest, a readable file description, the folder below the library folder, the Unreal project root.
/// </summary>
public sealed partial class GameNames
{
    private static readonly TimeSpan Rescan = TimeSpan.FromMinutes(5);
    private static readonly string[] Placeholders = ["bootstrappackagedgame", "unrealgame", "ue4game", "ue5game", "unityplayer"];
    private static readonly string[] Containers = ["games", "game", "steamapps", "common", "program files", "program files (x86)"];
    private const string SteamCommon = @"\steamapps\common\";

    private readonly Dictionary<string, (DateTimeOffset ScannedAt, Dictionary<string, string> Names)> _steam = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private readonly TimeProvider _time;

    public GameNames(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Unreal Engine shipping builds: FortniteClient-Win64-Shipping, b1-Win64-Shipping, FortniteClient-Mac-Shipping.</summary>
    public static bool IsUnrealShipping(string fileName) => ShippingSuffix().IsMatch(fileName);

    /// <param name="path">Executable path as reported, with \ or /.</param>
    /// <param name="folder">Library folder the path is under (written with \), or null.</param>
    /// <param name="displayName">What the collector found: the file description, or the process name without one.</param>
    public string Resolve(string path, string? folder, string displayName, string processName)
    {
        var normalized = path.Replace('/', '\\');
        var file = FileName(normalized);
        return SteamName(path, normalized)
            ?? (IsReadable(displayName, processName, file) ? displayName : null)
            ?? (folder is null ? null : FolderName(normalized, folder))
            ?? UnrealRoot(normalized)
            ?? Fallback(displayName);
    }

    internal static string FileName(string normalized)
    {
        var name = normalized[(normalized.LastIndexOf('\\') + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static bool IsReadable(string name, string processName, string file)
    {
        var trimmed = name.Trim();
        return trimmed.Length > 0
            && !trimmed.Any(c => c == '\uFFFD' || char.IsControl(c))
            && !trimmed.Equals(processName, StringComparison.OrdinalIgnoreCase)
            && !trimmed.Equals(file, StringComparison.OrdinalIgnoreCase)
            && !IsUnrealShipping(trimmed)
            && !Placeholders.Contains(trimmed.Replace(" ", "").ToLowerInvariant());
    }

    /// <summary>Ending with \ names the next folder; otherwise the folder that contains the match. Null when the file sits right there.</summary>
    private static string? FolderName(string normalized, string folder)
    {
        var at = normalized.IndexOf(folder, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        var start = folder.EndsWith('\\') ? at + folder.Length : at + (folder.StartsWith('\\') ? 1 : 0);
        var end = normalized.IndexOf('\\', start);
        return end > start ? Clean(normalized[start..end]) : null;
    }

    /// <summary>Packaged Unreal games: &lt;game&gt;\&lt;project&gt;\Binaries\Win64\&lt;project&gt;-Win64-Shipping.exe.</summary>
    private static string? UnrealRoot(string normalized)
    {
        var segments = normalized.Split('\\');
        var binaries = Array.FindLastIndex(segments, s => s.Equals("Binaries", StringComparison.OrdinalIgnoreCase));
        if (binaries < 1)
        {
            return null;
        }

        var root = binaries >= 2 ? Clean(segments[binaries - 2]) : null;
        return root is not null && !root.Contains(':') && !Containers.Contains(root.ToLowerInvariant()) ? root : Clean(segments[binaries - 1]);
    }

    private static string Fallback(string displayName)
    {
        var stripped = ShippingSuffix().Replace(displayName.Trim(), "");
        return stripped.Length > 0 ? stripped : displayName;
    }

    private static string? Clean(string segment)
    {
        var name = segment.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? segment[..^4] : segment;
        name = name.Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>The "name" of the appmanifest_*.acf whose "installdir" is the game folder under steamapps\common.</summary>
    private string? SteamName(string path, string normalized)
    {
        var at = normalized.IndexOf(SteamCommon, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        var start = at + SteamCommon.Length;
        var end = normalized.IndexOf('\\', start);
        if (end <= start)
        {
            return null;
        }

        var library = path[..(at + @"\steamapps".Length)];
        var installDir = normalized[start..end];
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            if (!_steam.TryGetValue(library, out var scan) || (!scan.Names.ContainsKey(installDir) && now - scan.ScannedAt > Rescan))
            {
                scan = (now, ReadManifests(library));
                _steam[library] = scan;
            }

            return scan.Names.GetValueOrDefault(installDir);
        }
    }

    private static Dictionary<string, string> ReadManifests(string library)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(library, "appmanifest_*.acf"))
            {
                string? name = null;
                string? installDir = null;
                foreach (var line in File.ReadLines(file))
                {
                    if (AcfValue().Match(line) is not { Success: true } match)
                    {
                        continue;
                    }

                    var value = match.Groups[2].Value.Replace("\\\"", "\"").Replace(@"\\", @"\").Trim();
                    if (match.Groups[1].Value.Equals("name", StringComparison.OrdinalIgnoreCase))
                    {
                        name ??= value;
                    }
                    else
                    {
                        installDir ??= value;
                    }
                }

                if (name is { Length: > 0 } && installDir is { Length: > 0 })
                {
                    names.TryAdd(installDir, name);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return names;
    }

    [GeneratedRegex(@"(-[a-z0-9]+)?-shipping$", RegexOptions.IgnoreCase)]
    private static partial Regex ShippingSuffix();

    [GeneratedRegex(@"^\s*""(name|installdir)""\s+""(.*)""\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AcfValue();
}
