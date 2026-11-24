using System.Diagnostics;

namespace DeskPal.Client.Providers;

/// <summary>Which files in the plugins folder are plugins, and how each is started.</summary>
public static class PluginFiles
{
    /// <summary>
    /// On Windows a plugin is a program or a script Windows can run without the user picking a program:
    /// .exe, .cmd, .bat, .ps1, and .py when Python is installed.
    /// </summary>
    public static readonly IReadOnlyList<string> WindowsExtensions = [".exe", ".cmd", ".bat", ".ps1", ".py"];

    /// <summary>
    /// Files directly in <paramref name="folder"/> that can run as plugins. Hidden files are skipped. On macOS and Linux
    /// a plugin is any file with the executable bit (<c>chmod +x</c>), whatever its language.
    /// </summary>
    public static IReadOnlyList<string> Find(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(IsPlugin)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static bool IsPlugin(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith('.'))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
        }

        return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }

    /// <summary>How to start the plugin: the file itself, or the interpreter Windows would use for it.</summary>
    public static ProcessStartInfo StartInfo(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(path);
        }

        var info = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cmd" or ".bat" => new ProcessStartInfo("cmd.exe") { ArgumentList = { "/d", "/c", path } },
            ".ps1" => new ProcessStartInfo("powershell.exe") { ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path } },
            ".py" => new ProcessStartInfo("python.exe") { ArgumentList = { "-u", path } },
            _ => new ProcessStartInfo(path),
        };
        return info;
    }
}
