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
    /// <summary>
    /// WebView2 is a child window and swallows the mouse, so the resize border only works where the page does not
    /// cover the window. Resizable windows keep this much space around the page.
    /// </summary>
    public const double ResizeBorder = 4;

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int GwlStyle = -16;
    private const int WsMaximizeBox = 0x10000;

    public static void Apply(Window window)
    {
        var resizable = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(resizable ? ResizeBorder : 0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var preference = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
            if (resizable)
            {
                // Without the maximize box, double-clicking the page title bar or Win+Up does not maximize the
                // window, which would push the edges off screen because there is no native frame.
                _ = SetWindowLong(hwnd, GwlStyle, GetWindowLong(hwnd, GwlStyle) & ~WsMaximizeBox);
            }
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
