using System.Runtime.InteropServices;

namespace DeskPal.Client.Ui;

/// <summary>
/// Windows whose title bar is drawn by the page, like on Windows and macOS. GTK's own title bar is replaced by an
/// empty, hidden one, which keeps the window's shadow and resize borders. The page sends <c>window.drag</c> when
/// the mouse goes down in its title bar; the window manager then moves the window (the only way under Wayland,
/// where a program cannot set its own position).
/// </summary>
public static partial class WindowChrome
{
    public static Gtk.Window Create(Gtk.Application application, string title, int width, int height, bool resizable)
    {
        var window = Gtk.Window.New();
        window.SetApplication(application);
        window.SetTitle(title);
        window.SetDefaultSize(width, height);
        window.SetResizable(resizable);
        window.SetIconName("deskpal");
        var hidden = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        hidden.SetVisible(false);
        window.SetTitlebar(hidden);
        return window;
    }

    public static bool Dark(string? theme) => theme switch
    {
        "dark" => true,
        "light" => false,
        _ => Gtk.Settings.GetDefault()?.GtkApplicationPreferDarkTheme == true || SystemPrefersDark(),
    };

    /// <summary>Handles the page's title bar messages. Returns false for other messages.</summary>
    public static bool Handle(Gtk.Window window, WebCommand command)
    {
        switch (command.Type)
        {
            case "window.drag":
                BeginMove(window, Number(command, "x"), Number(command, "y"));
                return true;
            case "window.dblclick":
                return true;
            case "window.minimize":
                window.Minimize();
                return true;
            case "window.close":
                window.Close();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Asks the window manager to move the window with the pointer until the button is released.</summary>
    private static void BeginMove(Gtk.Window window, double x, double y)
    {
        var surface = window.GetSurface();
        var pointer = Gdk.Display.GetDefault()?.GetDefaultSeat()?.GetPointer();
        if (surface is null || pointer is null)
        {
            return;
        }

        gdk_toplevel_begin_move(surface.Handle.DangerousGetHandle(), pointer.Handle.DangerousGetHandle(), 1, x, y, 0);
    }

    private static double Number(WebCommand command, string name) =>
        command.Args.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Number ? value.GetDouble() : 0;

    /// <summary>The desktop-wide preference (GNOME 42+ and others), as GTK_THEME-independent as it gets without libadwaita.</summary>
    private static bool SystemPrefersDark()
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("gsettings") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add("get");
            info.ArgumentList.Add("org.gnome.desktop.interface");
            info.ArgumentList.Add("color-scheme");
            using var process = System.Diagnostics.Process.Start(info);
            if (process is null || !process.WaitForExit(1000))
            {
                return false;
            }

            return process.StandardOutput.ReadToEnd().Contains("prefer-dark", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [LibraryImport("libgtk-4.so.1")]
    private static partial void gdk_toplevel_begin_move(IntPtr toplevel, IntPtr device, int button, double x, double y, uint timestamp);
}
