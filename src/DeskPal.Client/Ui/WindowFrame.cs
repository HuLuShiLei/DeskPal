using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace DeskPal.Client.Ui;

/// <summary>
/// Hides the native title bar so the page can draw its own. The window keeps its frame, so Windows 11 still
/// rounds the corners and draws the shadow. The page marks its title bar with CSS <c>app-region: drag</c>.
/// </summary>
public static class WindowFrame
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    public static void Apply(Window window)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var preference = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        };
    }

    /// <summary>Handles the title bar buttons. Returns false for other messages.</summary>
    public static bool Handle(Window window, string type)
    {
        switch (type)
        {
            case "window.minimize":
                window.WindowState = WindowState.Minimized;
                return true;
            case "window.close":
                window.Close();
                return true;
            default:
                return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
