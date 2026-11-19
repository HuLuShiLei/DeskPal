using AppKit;
using CoreGraphics;
using Foundation;

namespace DeskPal.Client.Ui;

/// <summary>
/// Windows whose title bar is drawn by the page, like the Windows client. The native title bar is transparent and
/// its buttons hidden; the window keeps macOS's rounded corners, shadow and resizing. WKWebView has no CSS
/// app-region, so the page sends <c>window.drag</c> on mouse down in its title bar and the window follows the
/// mouse until the button is released.
/// </summary>
public static class WindowChrome
{
    public static NSWindow Create(CGSize size, bool resizable)
    {
        var style = NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.FullSizeContentView;
        if (resizable)
        {
            style |= NSWindowStyle.Resizable;
        }

        var window = new NSWindow(new CGRect(CGPoint.Empty, size), style, NSBackingStore.Buffered, false)
        {
            TitlebarAppearsTransparent = true,
            TitleVisibility = NSWindowTitleVisibility.Hidden,
            MovableByWindowBackground = false,
        };
        // The managed object owns the window; AppKit must not release it again on close.
        window.ReleaseWhenClosed(false);
        foreach (var button in new[] { NSWindowButton.CloseButton, NSWindowButton.MiniaturizeButton, NSWindowButton.ZoomButton })
        {
            if (window.StandardWindowButton(button) is { } native)
            {
                native.Hidden = true;
            }
        }

        // No full screen and no zoom: the page is laid out for a narrow window.
        window.CollectionBehavior = NSWindowCollectionBehavior.FullScreenNone | NSWindowCollectionBehavior.MoveToActiveSpace;
        return window;
    }

    public static void ApplyTheme(NSWindow window, string? theme)
    {
        window.Appearance = theme switch
        {
            "dark" => NSAppearance.GetAppearance(NSAppearance.NameDarkAqua),
            "light" => NSAppearance.GetAppearance(NSAppearance.NameAqua),
            _ => null,
        };
        window.BackgroundColor = Dark(theme)
            ? NSColor.FromRgb(0x20, 0x20, 0x20)
            : NSColor.FromRgb(0xE8, 0xEA, 0xEF);
    }

    public static bool Dark(string? theme) => theme switch
    {
        "dark" => true,
        "light" => false,
        _ => NSApplication.SharedApplication.EffectiveAppearance.Name.Contains("Dark", StringComparison.Ordinal),
    };

    /// <summary>Handles the page's title bar messages. Returns false for other messages.</summary>
    public static bool Handle(NSWindow window, string type)
    {
        switch (type)
        {
            case "window.drag":
                BeginDrag(window);
                return true;
            case "window.minimize":
                window.Miniaturize(null);
                return true;
            case "window.close":
                window.PerformClose(null);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Moves the window with the mouse while the left button stays down.</summary>
    public static void BeginDrag(NSWindow window)
    {
        if ((NSEvent.CurrentPressedMouseButtons & 1) == 0)
        {
            return;
        }

        var start = NSEvent.CurrentMouseLocation;
        var origin = window.Frame.Location;
        var timer = NSTimer.CreateRepeatingTimer(1.0 / 120, t =>
        {
            var mouse = NSEvent.CurrentMouseLocation;
            window.SetFrameOrigin(new CGPoint(origin.X + mouse.X - start.X, origin.Y + mouse.Y - start.Y));
            if ((NSEvent.CurrentPressedMouseButtons & 1) == 0)
            {
                t.Invalidate();
            }
        });
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common);
    }
}
