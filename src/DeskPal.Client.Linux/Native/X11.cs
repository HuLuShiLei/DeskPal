using System.Runtime.InteropServices;
using System.Text;

namespace DeskPal.Client.Native;

/// <summary>
/// The few Xlib calls needed on an X11 session: which window is active, its title, class, process and whether it is
/// full screen, and how long there has been no input. Any X client may read these; no permission is involved.
/// Uses its own connection to the X server, apart from GTK's.
/// </summary>
internal sealed unsafe partial class X11 : IDisposable
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXss = "libXss.so.1";
    private static readonly XErrorHandler IgnoreErrors = (_, _) => 0;
    private static bool _handlerSet;
    private readonly IntPtr _display;
    private readonly nuint _root;
    private readonly nuint _netActiveWindow;
    private readonly nuint _netWmName;
    private readonly nuint _netWmPid;
    private readonly nuint _netWmState;
    private readonly nuint _netWmStateFullscreen;
    private readonly nuint _utf8String;
    private readonly nuint _wmClass;
    private readonly nuint _wmName;
    private bool _screenSaverMissing;

    private X11(IntPtr display)
    {
        _display = display;
        _root = XDefaultRootWindow(display);
        _netActiveWindow = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
        _netWmName = XInternAtom(display, "_NET_WM_NAME", false);
        _netWmPid = XInternAtom(display, "_NET_WM_PID", false);
        _netWmState = XInternAtom(display, "_NET_WM_STATE", false);
        _netWmStateFullscreen = XInternAtom(display, "_NET_WM_STATE_FULLSCREEN", false);
        _utf8String = XInternAtom(display, "UTF8_STRING", false);
        _wmClass = XInternAtom(display, "WM_CLASS", false);
        _wmName = XInternAtom(display, "WM_NAME", false);
    }

    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    public sealed record ActiveWindow(string Title, string Class, int Pid, bool Fullscreen);

    /// <summary>null when there is no X server to talk to (a pure Wayland session) or libX11 is missing.</summary>
    public static X11? Open()
    {
        try
        {
            if (!_handlerSet)
            {
                // Xlib's default handler ends the process on errors such as a window closing between two requests.
                XSetErrorHandler(IgnoreErrors);
                _handlerSet = true;
            }

            var display = XOpenDisplay(null);
            return display == IntPtr.Zero ? null : new X11(display);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public ActiveWindow? ReadActiveWindow()
    {
        var active = ReadLongs(_root, _netActiveWindow, 1);
        if (active.Length == 0 || active[0] == 0)
        {
            return null;
        }

        var window = active[0];
        var title = ReadText(window, _netWmName) ?? ReadText(window, _wmName) ?? "";
        var classes = (ReadText(window, _wmClass, keepNulls: true) ?? "").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var pid = ReadLongs(window, _netWmPid, 1);
        var state = ReadLongs(window, _netWmState, 32);
        return new ActiveWindow(
            title.Trim(),
            classes.Length == 0 ? "" : classes[^1],
            pid.Length == 0 ? 0 : (int)pid[0],
            state.Contains(_netWmStateFullscreen));
    }

    /// <summary>Time since the last key or pointer input, from the screen saver extension; null when it is not available.</summary>
    public TimeSpan? IdleTime()
    {
        if (_screenSaverMissing)
        {
            return null;
        }

        try
        {
            var info = XScreenSaverAllocInfo();
            if (info == null)
            {
                return null;
            }

            try
            {
                return XScreenSaverQueryInfo(_display, _root, info) != 0 ? TimeSpan.FromMilliseconds(info->Idle) : null;
            }
            finally
            {
                XFree((IntPtr)info);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _screenSaverMissing = true;
            return null;
        }
    }

    public void Dispose() => XCloseDisplay(_display);

    /// <summary>Format 32 properties arrive as C longs, 8 bytes each on 64-bit systems.</summary>
    private nuint[] ReadLongs(nuint window, nuint property, int max)
    {
        if (XGetWindowProperty(_display, window, property, 0, max, false, 0, out _, out var format, out var count, out _, out var data) != 0 || data == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            if (format != 32 || count == 0)
            {
                return [];
            }

            var values = new nuint[(int)count];
            new ReadOnlySpan<nuint>((void*)data, (int)count).CopyTo(values);
            return values;
        }
        finally
        {
            XFree(data);
        }
    }

    private string? ReadText(nuint window, nuint property, bool keepNulls = false)
    {
        if (XGetWindowProperty(_display, window, property, 0, 1024, false, 0, out var type, out var format, out var count, out _, out var data) != 0 || data == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (format != 8 || count == 0)
            {
                return null;
            }

            var bytes = new ReadOnlySpan<byte>((void*)data, (int)count);
            // UTF8_STRING is UTF-8; the old STRING type is Latin-1.
            var text = type == _utf8String ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes);
            return keepNulls ? text : text.TrimEnd('\0');
        }
        finally
        {
            XFree(data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XScreenSaverInfo
    {
        public nuint Window;
        public int State;
        public int Kind;
        public nuint TilOrSince;
        public nuint Idle;
        public nuint EventMask;
    }

    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr XOpenDisplay(string? name);

    [LibraryImport(LibX11)]
    private static partial int XCloseDisplay(IntPtr display);

    [LibraryImport(LibX11)]
    private static partial nuint XDefaultRootWindow(IntPtr display);

    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XInternAtom(IntPtr display, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport(LibX11)]
    private static partial int XGetWindowProperty(
        IntPtr display,
        nuint window,
        nuint property,
        nint offset,
        nint length,
        [MarshalAs(UnmanagedType.Bool)] bool delete,
        nuint requestedType,
        out nuint actualType,
        out int actualFormat,
        out nuint itemCount,
        out nuint bytesAfter,
        out IntPtr data);

    [LibraryImport(LibX11)]
    private static partial int XFree(IntPtr data);

    [DllImport(LibX11)]
    private static extern IntPtr XSetErrorHandler(XErrorHandler handler);

    [LibraryImport(LibXss)]
    private static partial XScreenSaverInfo* XScreenSaverAllocInfo();

    [LibraryImport(LibXss)]
    private static partial int XScreenSaverQueryInfo(IntPtr display, nuint drawable, XScreenSaverInfo* info);
}
