using System.Runtime.InteropServices;
using CoreGraphics;
using Foundation;
using ObjCRuntime;

namespace DeskPal.Client.Native;

/// <summary>
/// The few C APIs the macOS collectors need. Accessibility (AX) reads the focused window's title and the browser's
/// address; it needs the user to allow DeskPal under Privacy &amp; Security → Accessibility. Nothing here reads keys,
/// the screen or window contents beyond the title and the address.
/// </summary>
internal static partial class MacNative
{
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public const int AxSuccess = 0;
    private const int AxValueCgPointType = 1;
    private const int AxValueCgSizeType = 2;

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AXIsProcessTrustedWithOptions(IntPtr options);

    [LibraryImport(ApplicationServices)]
    public static partial IntPtr AXUIElementCreateApplication(int pid);

    [LibraryImport(ApplicationServices)]
    private static partial int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [LibraryImport(ApplicationServices)]
    private static partial int AXUIElementSetMessagingTimeout(IntPtr element, float seconds);

    /// <summary>Writes a CGPoint or CGSize to <paramref name="result"/>, by <paramref name="type"/>.</summary>
    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static unsafe partial bool AXValueGetValue(IntPtr value, int type, void* result);

    [LibraryImport(CoreFoundationLib)]
    public static partial void CFRelease(IntPtr handle);

    [LibraryImport(CoreFoundationLib)]
    private static partial nint CFGetTypeID(IntPtr handle);

    [LibraryImport(CoreFoundationLib)]
    private static partial nint CFStringGetTypeID();

    [LibraryImport(CoreFoundationLib)]
    private static partial nint CFBooleanGetTypeID();

    [LibraryImport(CoreFoundationLib)]
    private static partial nint CFArrayGetTypeID();

    [LibraryImport(CoreFoundationLib)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFBooleanGetValue(IntPtr boolean);

    [LibraryImport(CoreFoundationLib)]
    private static partial nint CFArrayGetCount(IntPtr array);

    [LibraryImport(CoreFoundationLib)]
    private static partial IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [LibraryImport(CoreFoundationLib)]
    private static partial IntPtr CFRetain(IntPtr handle);

    /// <summary>kCGEventSourceStateHIDSystemState and kCGAnyInputEventType: time since the last key or mouse input anywhere.</summary>
    [LibraryImport(CoreGraphicsLib)]
    private static partial double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);

    public static TimeSpan IdleTime()
    {
        var seconds = CGEventSourceSecondsSinceLastEventType(1, uint.MaxValue);
        return double.IsFinite(seconds) && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    /// <summary>Whether DeskPal may use Accessibility. With <paramref name="prompt"/> macOS shows its own dialog and lists the app in Settings.</summary>
    public static bool AccessibilityTrusted(bool prompt)
    {
        if (!prompt)
        {
            return AXIsProcessTrustedWithOptions(IntPtr.Zero);
        }

        using var key = new NSString("AXTrustedCheckOptionPrompt");
        using var options = NSDictionary.FromObjectAndKey(NSNumber.FromBoolean(true), key);
        return AXIsProcessTrustedWithOptions(options.Handle);
    }

    public static void SetTimeout(IntPtr element, float seconds) => _ = AXUIElementSetMessagingTimeout(element, seconds);

    /// <summary>Copies an attribute. The caller releases the result with <see cref="CFRelease"/>.</summary>
    public static IntPtr Copy(IntPtr element, string attribute)
    {
        if (element == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        using var name = new NSString(attribute);
        return AXUIElementCopyAttributeValue(element, name.Handle, out var value) == AxSuccess ? value : IntPtr.Zero;
    }

    public static string? CopyString(IntPtr element, string attribute)
    {
        var value = Copy(element, attribute);
        if (value == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return CFGetTypeID(value) == CFStringGetTypeID() ? Runtime.GetNSObject<NSString>(value)?.ToString() : null;
        }
        finally
        {
            CFRelease(value);
        }
    }

    public static bool? CopyBool(IntPtr element, string attribute)
    {
        var value = Copy(element, attribute);
        if (value == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return CFGetTypeID(value) == CFBooleanGetTypeID() ? CFBooleanGetValue(value) : null;
        }
        finally
        {
            CFRelease(value);
        }
    }

    /// <summary>AXURL is a CFURL, toll-free bridged with NSURL.</summary>
    public static string? CopyUrl(IntPtr element, string attribute)
    {
        var value = Copy(element, attribute);
        if (value == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (CFGetTypeID(value) == CFStringGetTypeID())
            {
                return Runtime.GetNSObject<NSString>(value)?.ToString();
            }

            return Runtime.GetNSObject<NSUrl>(value)?.AbsoluteString;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            CFRelease(value);
        }
    }

    /// <summary>Window frame in global screen coordinates with the origin at the top left of the main screen.</summary>
    public static CGRect? CopyFrame(IntPtr window)
    {
        var position = Copy(window, "AXPosition");
        var size = Copy(window, "AXSize");
        try
        {
            if (position == IntPtr.Zero || size == IntPtr.Zero)
            {
                return null;
            }

            CGPoint point;
            CGSize extent;
            unsafe
            {
                if (!AXValueGetValue(position, AxValueCgPointType, &point) || !AXValueGetValue(size, AxValueCgSizeType, &extent))
                {
                    return null;
                }
            }

            return new CGRect(point, extent);
        }
        finally
        {
            if (position != IntPtr.Zero)
            {
                CFRelease(position);
            }

            if (size != IntPtr.Zero)
            {
                CFRelease(size);
            }
        }
    }

    /// <summary>Children of an element, each retained; release them with <see cref="CFRelease"/>.</summary>
    public static List<IntPtr> CopyChildren(IntPtr element)
    {
        var result = new List<IntPtr>();
        var value = Copy(element, "AXChildren");
        if (value == IntPtr.Zero)
        {
            return result;
        }

        try
        {
            if (CFGetTypeID(value) != CFArrayGetTypeID())
            {
                return result;
            }

            var count = CFArrayGetCount(value);
            for (nint i = 0; i < count; i++)
            {
                var child = CFArrayGetValueAtIndex(value, i);
                if (child != IntPtr.Zero)
                {
                    result.Add(CFRetain(child));
                }
            }

            return result;
        }
        finally
        {
            CFRelease(value);
        }
    }
}
