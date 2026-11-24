using System.Diagnostics;
using DeskPal.Client.Native;
using DeskPal.Client.Ui;
using Tmds.DBus.Protocol;

namespace DeskPal.Client.Platform;

/// <summary>Linux side of <see cref="IClientPlatform"/>. Dialogs and the clipboard are used on the GTK thread.</summary>
public sealed class LinuxPlatform : IClientPlatform
{
    /// <summary>The window dialogs belong to; set by the panel and the login window while they are open.</summary>
    public static Gtk.Window? ActiveWindow { get; set; }

    public string Name => "linux";

    public bool AutoStartEnabled => AutoStart.IsEnabled;

    public void SetAutoStart(bool enabled) => AutoStart.Set(enabled);

    public void CopyText(string text) => Gdk.Display.GetDefault()?.GetClipboard().SetText(text);

    public async Task<string?> PickSaveFileAsync(string title, string fileName)
    {
        var dialog = JsonDialog(title);
        dialog.SetInitialName(fileName);
        return await PickAsync(() => dialog.SaveAsync(Parent()));
    }

    public async Task<string?> PickOpenFileAsync(string title) => await PickAsync(() => JsonDialog(title).OpenAsync(Parent()));

    public async Task<string?> PickFolderAsync(string title)
    {
        var dialog = Gtk.FileDialog.New();
        dialog.SetTitle(title);
        dialog.SetModal(true);
        return await PickAsync(() => dialog.SelectFolderAsync(Parent()));
    }

    public void OpenExternal(string target) => WebHost.OpenExternal(target);

    public IUserPrompt FallbackPrompt { get; } = new AlertPrompt();

    /// <summary>The path to start DeskPal with: the AppImage when run from one, else the program itself.</summary>
    public static string LaunchPath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image && File.Exists(image) ? image : Environment.ProcessPath ?? "DeskPal";

    /// <summary>Desktop notification through org.freedesktop.Notifications, e.g. "you were removed from a room".</summary>
    public static async void Notify(string title, string message)
    {
        try
        {
            var connection = await Bus.SessionAsync();
            await Bus.CallAsync(
                connection,
                "org.freedesktop.Notifications",
                "/org/freedesktop/Notifications",
                "org.freedesktop.Notifications",
                "Notify",
                "susssasa{sv}i",
                (ref MessageWriter w) =>
                {
                    w.WriteString("DeskPal");
                    w.WriteUInt32(0);
                    w.WriteString("deskpal");
                    w.WriteString(title);
                    w.WriteString(message);
                    w.WriteArrayEnd(w.WriteArrayStart(DBusType.String));
                    w.WriteArrayEnd(w.WriteArrayStart(DBusType.DictEntry));
                    w.WriteInt32(-1);
                });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
        }
    }

    /// <summary>Starts a new copy of the app, e.g. back to the login page. The current one must exit right after.</summary>
    public static void Relaunch(string? arg)
    {
        var info = new ProcessStartInfo(LaunchPath) { UseShellExecute = false };
        if (arg is not null)
        {
            info.ArgumentList.Add(arg);
        }

        Process.Start(info)?.Dispose();
    }

    private static Gtk.Window Parent() => ActiveWindow ?? throw new InvalidOperationException("No window is open.");

    private static Gtk.FileDialog JsonDialog(string title)
    {
        var dialog = Gtk.FileDialog.New();
        dialog.SetTitle(title);
        dialog.SetModal(true);
        var filter = Gtk.FileFilter.New();
        filter.SetName("JSON");
        filter.AddPattern("*.json");
        dialog.SetDefaultFilter(filter);
        return dialog;
    }

    /// <summary>GTK reports a dismissed dialog as an error.</summary>
    private static async Task<string?> PickAsync(Func<Task<Gio.File?>> show)
    {
        try
        {
            return (await show())?.GetPath();
        }
        catch (GLib.GException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>Fallback when the panel page cannot be shown.</summary>
public sealed class AlertPrompt : IUserPrompt
{
    public Task<bool> AskAsync(PromptKind kind, string message) => MainThread.InvokeAsync(async () =>
    {
        var dialog = new Gtk.AlertDialog();
        dialog.SetMessage("DeskPal");
        dialog.SetDetail(message);
        dialog.SetModal(true);
        if (kind == PromptKind.Confirm)
        {
            dialog.SetButtons([Strings.Common_Cancel, Strings.Common_Ok]);
            dialog.SetCancelButton(0);
            dialog.SetDefaultButton(1);
        }
        else
        {
            dialog.SetButtons([Strings.Common_Ok]);
            dialog.SetDefaultButton(0);
        }

        try
        {
            var parent = LinuxPlatform.ActiveWindow;
            var choice = parent is null ? await ShowWithoutParentAsync(dialog) : await dialog.ChooseAsync(parent);
            return kind == PromptKind.Confirm ? choice == 1 : true;
        }
        catch (GLib.GException)
        {
            return false;
        }
    });

    /// <summary>Before any window exists, e.g. "already running".</summary>
    private static async Task<int> ShowWithoutParentAsync(Gtk.AlertDialog dialog)
    {
        var window = Gtk.Window.New();
        try
        {
            return await dialog.ChooseAsync(window);
        }
        finally
        {
            window.Destroy();
        }
    }
}

/// <summary>
/// Start at login through an entry in ~/.config/autostart, which every desktop that follows the XDG autostart
/// specification runs. It starts DeskPal with --minimized, the same as the Windows Run key.
/// </summary>
internal static class AutoStart
{
    private static string DesktopFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart",
        "deskpal.desktop");

    public static bool IsEnabled => File.Exists(DesktopFile);

    public static void Set(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(DesktopFile);
            return;
        }

        // Quoted as the Desktop Entry specification asks: inside double quotes, escape " ` $ and \.
        var path = LauncherEntry.Quote(LinuxPlatform.LaunchPath);
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopFile)!);
        File.WriteAllText(DesktopFile, $"""
            [Desktop Entry]
            Type=Application
            Name=DeskPal
            Exec="{path}" --minimized
            Icon={LauncherEntry.IconName}
            Terminal=false
            X-GNOME-Autostart-enabled=true

            """);
    }
}

/// <summary>
/// An AppImage is a single file the user keeps wherever they like, so nothing tells the desktop about it. On start
/// DeskPal writes its own entry for the application menu, which also gives its window the right name and icon in
/// docks and task switchers (they match the window's application id to this file). Only for AppImage runs.
/// </summary>
internal static class LauncherEntry
{
    public const string IconName = LinuxApp.ApplicationId;

    private static string DataHome =>
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data ? data : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    /// <summary>Quoted as the Desktop Entry specification asks: inside double quotes, escape " ` $ and \.</summary>
    public static string Quote(string path) => path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");

    public static void Ensure()
    {
        if (Environment.GetEnvironmentVariable("APPIMAGE") is not { Length: > 0 } image || !File.Exists(image))
        {
            return;
        }

        try
        {
            var icon = Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps", IconName + ".png");
            var source = Path.Combine(AppContext.BaseDirectory, "deskpal.png");
            if (File.Exists(source) && (!File.Exists(icon) || new FileInfo(icon).Length != new FileInfo(source).Length))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
                File.Copy(source, icon, overwrite: true);
            }

            var entry = Path.Combine(DataHome, "applications", LinuxApp.ApplicationId + ".desktop");
            var text = $"""
                [Desktop Entry]
                Type=Application
                Name=DeskPal
                Exec="{Quote(image)}"
                Icon={IconName}
                Terminal=false
                Categories=Network;
                StartupWMClass={LinuxApp.ApplicationId}

                """;
            if (!File.Exists(entry) || File.ReadAllText(entry) != text)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entry)!);
                File.WriteAllText(entry, text);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
        }
    }
}
