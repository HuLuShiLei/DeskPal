using AppKit;
using Foundation;

namespace DeskPal.Client.Platform;

/// <summary>AppKit objects are only touched on the main thread.</summary>
internal static class MainThread
{
    public static bool IsCurrent => NSThread.IsMain;

    /// <summary>Brings DeskPal to the front. A menu bar app has to, or its windows open behind the active app.</summary>
    public static void Activate()
    {
        var app = NSApplication.SharedApplication;
        if (OperatingSystem.IsMacOSVersionAtLeast(14))
        {
            app.Activate();
        }
        else
        {
#pragma warning disable CA1422 // The replacement only exists from macOS 14.
            app.ActivateIgnoringOtherApps(true);
#pragma warning restore CA1422
        }
    }

    public static void Post(Action action) => NSApplication.SharedApplication.BeginInvokeOnMainThread(() => Guard(action));

    public static T Invoke<T>(Func<T> func)
    {
        if (NSThread.IsMain)
        {
            return func();
        }

        T result = default!;
        NSApplication.SharedApplication.InvokeOnMainThread(() => result = func());
        return result;
    }

    public static Task<T> InvokeAsync<T>(Func<Task<T>> func)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            try
            {
                done.SetResult(await func());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        return done.Task;
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }
}
