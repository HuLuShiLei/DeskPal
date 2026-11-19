using System.Runtime.InteropServices;
using AppKit;
using CoreFoundation;
using DeskPal.Client.Core.Pipeline;
using Foundation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ObjCRuntime;

namespace DeskPal.Client.Collectors;

/// <summary>
/// What Control Center's Now Playing shows (song or video title), read through the private MediaRemote framework.
/// macOS 15.4 and later no longer answer third-party apps; then this reports nothing playing and classification
/// works without media (desktop players are still recognized by their rules). Every call is guarded, so a missing
/// or changed framework cannot crash the app.
/// </summary>
public sealed class MediaCollector(ActivityBus bus, ILogger<MediaCollector> logger) : BackgroundService
{
    private static readonly MediaSignal Stopped = new(false, null, null, null);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(1.5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!MediaRemote.Available)
        {
            logger.LogInformation("MediaRemote is not available; media titles are not read");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        MediaSignal? last = null;
        var failures = 0;
        do
        {
            MediaSignal signal;
            try
            {
                signal = await ReadAsync();
                failures = 0;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Reading Now Playing failed");
                signal = Stopped;
                if (++failures >= 10)
                {
                    logger.LogInformation("Now Playing keeps failing; media titles are no longer read");
                    bus.Publish(Stopped);
                    return;
                }
            }

            if (signal != last)
            {
                last = signal;
                bus.Publish(signal);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static async Task<MediaSignal> ReadAsync()
    {
        if (!await WithTimeout(MediaRemote.IsPlayingAsync(), false))
        {
            return Stopped;
        }

        var (title, artist) = await WithTimeout(MediaRemote.NowPlayingAsync(), (null, null));
        var pid = await WithTimeout(MediaRemote.PlayingPidAsync(), 0);
        var source = pid > 0 ? NSRunningApplication.GetRunningApplication(pid)?.BundleIdentifier : null;
        return new MediaSignal(true, Blank(title), Blank(artist), source);
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, T fallback) =>
        await Task.WhenAny(task, Task.Delay(CallTimeout)) == task ? await task : fallback;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The three MediaRemote calls used, with their completion blocks.</summary>
internal static unsafe class MediaRemote
{
    private const string Framework = "/System/Library/PrivateFrameworks/MediaRemote.framework/MediaRemote";
    private static readonly DispatchQueue Queue = new("com.hulushilei.deskpal.media");

    public static bool Available { get; } = Probe();

    [DllImport(Framework)]
    private static extern void MRMediaRemoteGetNowPlayingApplicationIsPlaying(IntPtr queue, BlockLiteral* block);

    [DllImport(Framework)]
    private static extern void MRMediaRemoteGetNowPlayingInfo(IntPtr queue, BlockLiteral* block);

    [DllImport(Framework)]
    private static extern void MRMediaRemoteGetNowPlayingApplicationPID(IntPtr queue, BlockLiteral* block);

    public static Task<bool> IsPlayingAsync()
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<byte> callback = playing => done.TrySetResult(playing != 0);
        delegate* unmanaged<IntPtr, byte, void> trampoline = &BoolTrampoline;
        using var block = new BlockLiteral(trampoline, callback, typeof(MediaRemote), nameof(BoolTrampoline));
        MRMediaRemoteGetNowPlayingApplicationIsPlaying(Queue.Handle, &block);
        return done.Task;
    }

    public static Task<(string? Title, string? Artist)> NowPlayingAsync()
    {
        var done = new TaskCompletionSource<(string?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<IntPtr> callback = info =>
        {
            try
            {
                var dict = info == IntPtr.Zero ? null : Runtime.GetNSObject<NSDictionary>(info);
                done.TrySetResult((Text(dict, "kMRMediaRemoteNowPlayingInfoTitle"), Text(dict, "kMRMediaRemoteNowPlayingInfoArtist")));
            }
            catch (Exception)
            {
                done.TrySetResult((null, null));
            }
        };
        delegate* unmanaged<IntPtr, IntPtr, void> trampoline = &PointerTrampoline;
        using var block = new BlockLiteral(trampoline, callback, typeof(MediaRemote), nameof(PointerTrampoline));
        MRMediaRemoteGetNowPlayingInfo(Queue.Handle, &block);
        return done.Task;
    }

    public static Task<int> PlayingPidAsync()
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<int> callback = pid => done.TrySetResult(pid);
        delegate* unmanaged<IntPtr, int, void> trampoline = &IntTrampoline;
        using var block = new BlockLiteral(trampoline, callback, typeof(MediaRemote), nameof(IntTrampoline));
        MRMediaRemoteGetNowPlayingApplicationPID(Queue.Handle, &block);
        return done.Task;
    }

    private static string? Text(NSDictionary? dict, string key) =>
        dict?.ObjectForKey(new NSString(key)) is NSString value ? value.ToString() : null;

    private static bool Probe()
    {
        try
        {
            if (!NativeLibrary.TryLoad(Framework, out var handle))
            {
                return false;
            }

            return NativeLibrary.TryGetExport(handle, "MRMediaRemoteGetNowPlayingInfo", out _)
                && NativeLibrary.TryGetExport(handle, "MRMediaRemoteGetNowPlayingApplicationIsPlaying", out _)
                && NativeLibrary.TryGetExport(handle, "MRMediaRemoteGetNowPlayingApplicationPID", out _);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [UnmanagedCallersOnly]
    private static void BoolTrampoline(IntPtr block, byte value)
    {
        try
        {
            BlockLiteral.GetTarget<Action<byte>>(block)?.Invoke(value);
        }
        catch (Exception)
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void PointerTrampoline(IntPtr block, IntPtr value)
    {
        try
        {
            BlockLiteral.GetTarget<Action<IntPtr>>(block)?.Invoke(value);
        }
        catch (Exception)
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void IntTrampoline(IntPtr block, int value)
    {
        try
        {
            BlockLiteral.GetTarget<Action<int>>(block)?.Invoke(value);
        }
        catch (Exception)
        {
        }
    }
}
