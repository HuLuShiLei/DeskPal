using DeskPal.Client.Core.Providers;

namespace DeskPal.Client.Native;

/// <summary>
/// Names the program behind a window the way the rules expect: the executable's file name, the same word the
/// Windows rules use for many apps (code, chrome, msedge, steam, obs). When the executable is only a runtime
/// (electron, python, java, wine), the window's application id is used instead.
/// </summary>
internal static class LinuxProcess
{
    private static readonly string[] Runtimes =
    [
        "electron", "python", "java", "node", "wine", "wine64", "wine-preloader", "wine64-preloader", "bwrap", "sh", "bash", "perl", "ruby",
        "mono", "dotnet", "gjs", "gjs-console", "flatpak", "snap-confine", "env", "xwayland", "steam-runtime-launcher-service", "pressure-vessel-wrap",
    ];

    /// <param name="pid">Process that owns the window, or 0 when unknown.</param>
    /// <param name="appId">Wayland app id or X11 WM_CLASS; may be empty.</param>
    public static (string Process, string? Path) Describe(int pid, string? appId)
    {
        var path = pid > 0 ? ExecutablePath(pid) : null;
        var id = ProviderProtocol.NormalizeProcess(StripDesktop(appId));
        var exe = path is null ? "" : ProviderProtocol.NormalizeProcess(path);
        if (exe.Length == 0)
        {
            return (id.Length == 0 ? "unknown" : id, path);
        }

        if (IsRuntime(exe))
        {
            // Wine and Proton games: the Windows path in the command line tells games in a Steam library apart.
            var command = pid > 0 ? FirstArgument(pid) : null;
            if (exe.StartsWith("wine", StringComparison.OrdinalIgnoreCase) && command is { Length: > 0 } && command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return (ProviderProtocol.NormalizeProcess(command), command);
            }

            return (id.Length == 0 ? exe : id, path);
        }

        return (exe, path);
    }

    /// <summary>Executable name of a process, for media sources; null when it cannot be read.</summary>
    public static string? NameOf(int pid) => pid > 0 && ExecutablePath(pid) is { } path ? ProviderProtocol.NormalizeProcess(path) : null;

    private static bool IsRuntime(string exe)
    {
        var name = exe.ToLowerInvariant();
        return Runtimes.Any(r => name == r || (name.StartsWith(r, StringComparison.Ordinal) && name[r.Length..].All(c => char.IsAsciiDigit(c) || c == '.')));
    }

    private static string? StripDesktop(string? appId) =>
        appId is not null && appId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? appId[..^8] : appId;

    private static string? ExecutablePath(int pid)
    {
        try
        {
            var target = File.ResolveLinkTarget($"/proc/{pid}/exe", returnFinalTarget: false)?.FullName;
            return target is null ? null : target.EndsWith(" (deleted)", StringComparison.Ordinal) ? target[..^10] : target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FirstArgument(int pid)
    {
        try
        {
            var raw = File.ReadAllText($"/proc/{pid}/cmdline");
            var end = raw.IndexOf('\0');
            return end > 0 ? raw[..end] : raw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
