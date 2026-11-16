using DeskPal.Client.Core.Pipeline;

namespace DeskPal.Client.Core.Classification;

/// <summary>
/// Tells whether a system media session belongs to a process. The session names its app by the AppUserModelID,
/// which for desktop apps is usually the exe name (MSEdge, Chrome, Spotify.exe) and for Firefox a hash of the install path.
/// </summary>
public static class MediaSources
{
    /// <summary>The foreground program itself is playing, not a browser or player in the background.</summary>
    public static bool PlayingInForeground(ActivitySnapshot snapshot) =>
        snapshot.Media.IsPlaying && IsFrom(snapshot.Media.SourceApp, snapshot.Foreground?.ProcessName);

    public static bool IsFrom(string? sourceApp, string? process)
    {
        if (string.IsNullOrWhiteSpace(sourceApp) || string.IsNullOrWhiteSpace(process))
        {
            return false;
        }

        var name = Key(process);
        var source = Key(sourceApp);
        if (source == name || source.StartsWith(name + ".", StringComparison.Ordinal) || source.StartsWith(name + "_", StringComparison.Ordinal))
        {
            return true;
        }

        return name == "firefox" && source.Length == 16 && source.All(char.IsAsciiHexDigit);
    }

    private static string Key(string value)
    {
        var key = value.Trim();
        var bang = key.IndexOf('!');
        if (bang >= 0)
        {
            key = key[(bang + 1)..];
        }

        key = Path.GetFileName(key).ToLowerInvariant();
        return key.EndsWith(".exe", StringComparison.Ordinal) ? key[..^4] : key;
    }
}
