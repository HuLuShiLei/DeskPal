using System.Diagnostics;
using System.Text.Json;
using DeskPal.Client.Native;
using Tmds.DBus.Protocol;

namespace DeskPal.Client.Providers;

/// <summary>
/// The GNOME Shell extension that ships with DeskPal (gnome-extension/). On GNOME with Wayland no program can ask
/// which window is active; only code inside the shell can. The extension answers one question on the session bus:
/// the focused window's title, application and process. It does nothing else.
/// </summary>
internal static class GnomeExtension
{
    public const string Uuid = "deskpal@hulushilei.github.io";
    private const string BusName = "org.gnome.Shell";
    private const string ObjectPath = "/io/github/hulushilei/DeskPal";
    private const string Interface = "io.github.hulushilei.DeskPal.Windows";

    public sealed record Focused(string Title, string WmClass, string AppId, string AppName, int Pid, bool Fullscreen);

    private static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "gnome-extension", Uuid);

    private static string InstalledDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data ? data : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "gnome-shell",
        "extensions",
        Uuid);

    public static bool IsInstalled => File.Exists(Path.Combine(InstalledDirectory, "metadata.json"));

    public static bool IsBundled => File.Exists(Path.Combine(BundledDirectory, "metadata.json"));

    /// <summary>The focused window; null when no window has the focus. Throws when the extension does not answer.</summary>
    public static async Task<Focused?> GetFocusedAsync()
    {
        var connection = await Bus.SessionAsync();
        var json = await Bus.CallAsync(connection, BusName, ObjectPath, Interface, "GetFocused", static (Message m, object? _) => m.GetBodyReader().ReadString());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("pid", out var pid))
        {
            return null;
        }

        return new Focused(
            Text(root, "title"),
            Text(root, "wmClass"),
            Text(root, "appId"),
            Text(root, "appName"),
            pid.ValueKind == JsonValueKind.Number ? pid.GetInt32() : 0,
            root.TryGetProperty("fullscreen", out var fullscreen) && fullscreen.ValueKind == JsonValueKind.True);
    }

    /// <summary>
    /// Copies the extension into the user's extensions folder and turns it on. A running shell under Wayland only
    /// finds new extensions at the next login.
    /// </summary>
    public static async Task InstallAsync()
    {
        CopyBundled();
        if (await RunAsync("gnome-extensions", "enable", Uuid) == 0)
        {
            return;
        }

        // The running shell does not know the extension yet: add it to the list it reads at the next login.
        var (code, output) = await RunWithOutputAsync("gsettings", "get", "org.gnome.shell", "enabled-extensions");
        if (code != 0)
        {
            throw new IOException("gsettings is not available.");
        }

        var enabled = output.Split('\'').Where((_, i) => i % 2 == 1).ToList();
        if (!enabled.Contains(Uuid))
        {
            enabled.Add(Uuid);
            var value = "[" + string.Join(", ", enabled.Select(id => $"'{id}'")) + "]";
            if (await RunAsync("gsettings", "set", "org.gnome.shell", "enabled-extensions", value) != 0)
            {
                throw new IOException("Turning the extension on failed.");
            }
        }
    }

    /// <summary>Keeps an installed copy in step with the one that came with this version. Takes effect at the next login.</summary>
    public static void RefreshInstalled()
    {
        if (!IsInstalled || !IsBundled)
        {
            return;
        }

        try
        {
            var bundled = File.ReadAllText(Path.Combine(BundledDirectory, "metadata.json"));
            var installed = File.ReadAllText(Path.Combine(InstalledDirectory, "metadata.json"));
            if (bundled != installed)
            {
                CopyBundled();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void CopyBundled()
    {
        if (!IsBundled)
        {
            throw new IOException("The extension files are missing from this copy of DeskPal.");
        }

        Directory.CreateDirectory(InstalledDirectory);
        foreach (var file in Directory.EnumerateFiles(BundledDirectory))
        {
            File.Copy(file, Path.Combine(InstalledDirectory, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static async Task<int> RunAsync(params string[] command) => (await RunWithOutputAsync(command)).Code;

    private static async Task<(int Code, string Output)> RunWithOutputAsync(params string[] command)
    {
        try
        {
            var info = new ProcessStartInfo(command[0]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in command.Skip(1))
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return (-1, "");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            return (process.ExitCode, output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException)
        {
            return (-1, "");
        }
    }
}
