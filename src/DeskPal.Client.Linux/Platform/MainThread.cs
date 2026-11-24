namespace DeskPal.Client.Platform;

/// <summary>
/// GTK objects are only touched on the thread that runs the GLib main loop. Continuations of awaits started there
/// come back to it through <see cref="GLibSynchronizationContext"/>.
/// </summary>
internal static class MainThread
{
    private const int PriorityDefault = 0;
    private static int _threadId;

    public static void Init()
    {
        _threadId = Environment.CurrentManagedThreadId;
        SynchronizationContext.SetSynchronizationContext(new GLibSynchronizationContext());
    }

    public static bool IsCurrent => Environment.CurrentManagedThreadId == _threadId;

    public static void Post(Action action) => GLib.Functions.IdleAdd(PriorityDefault, () =>
    {
        Guard(action);
        return false;
    });

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

    public static Task<T> InvokeAsync<T>(Func<T> func) => InvokeAsync(() => Task.FromResult(func()));

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

    private sealed class GLibSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => MainThread.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (IsCurrent)
            {
                d(state);
                return;
            }

            using var done = new ManualResetEventSlim();
            MainThread.Post(() =>
            {
                try
                {
                    d(state);
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait();
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}

/// <summary>A timer on the GLib main loop. Stop is safe after it has fired.</summary>
internal sealed class UiTimer
{
    private uint _id;

    private UiTimer()
    {
    }

    public static UiTimer Once(TimeSpan delay, Action action)
    {
        var timer = new UiTimer();
        timer._id = GLib.Functions.TimeoutAdd(0, (uint)Math.Max(1, delay.TotalMilliseconds), () =>
        {
            timer._id = 0;
            Run(action);
            return false;
        });
        return timer;
    }

    public static UiTimer Every(TimeSpan interval, Action action)
    {
        var timer = new UiTimer();
        timer._id = GLib.Functions.TimeoutAdd(0, (uint)Math.Max(1, interval.TotalMilliseconds), () =>
        {
            Run(action);
            return timer._id != 0;
        });
        return timer;
    }

    public void Stop()
    {
        if (_id != 0)
        {
            GLib.Functions.SourceRemove(_id);
            _id = 0;
        }
    }

    private static void Run(Action action)
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
