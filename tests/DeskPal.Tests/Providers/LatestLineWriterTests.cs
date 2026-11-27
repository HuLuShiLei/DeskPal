using DeskPal.Client.Core.Providers;

namespace DeskPal.Tests.Providers;

public class LatestLineWriterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    [Fact]
    public void First_line_goes_out_at_once()
    {
        var throttle = new LineThrottle(Second);
        throttle.Offer("a");

        Assert.Equal(("a", TimeSpan.Zero), throttle.Next(T0));
    }

    [Fact]
    public void Changes_within_the_interval_collapse_into_the_last()
    {
        var throttle = new LineThrottle(Second);
        throttle.Offer("a");
        throttle.Written("a", T0);

        throttle.Offer("b");
        throttle.Offer("c");

        Assert.Equal((null, TimeSpan.FromMilliseconds(700)), throttle.Next(T0.AddMilliseconds(300)));
        Assert.Equal(("c", TimeSpan.Zero), throttle.Next(T0.AddSeconds(1)));
        throttle.Written("c", T0.AddSeconds(1));
        Assert.Equal((null, TimeSpan.Zero), throttle.Next(T0.AddSeconds(5)));
    }

    [Fact]
    public void The_same_line_is_not_written_again()
    {
        var throttle = new LineThrottle(Second);
        throttle.Offer("a");
        throttle.Written("a", T0);

        throttle.Offer("a");
        Assert.Equal((null, TimeSpan.Zero), throttle.Next(T0.AddSeconds(5)));

        // Changed and changed back before it went out: nothing to write.
        throttle.Offer("b");
        throttle.Offer("a");
        Assert.Equal((null, TimeSpan.Zero), throttle.Next(T0.AddSeconds(5)));
    }

    [Fact]
    public async Task A_plugin_that_does_not_read_never_blocks_the_caller()
    {
        var release = new TaskCompletionSource();
        var written = new List<string>();
        var writer = new LatestLineWriter(async (line, _) =>
        {
            written.Add(line);
            if (written.Count == 1)
            {
                await release.Task;
            }
        }, TimeSpan.Zero);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var loop = writer.RunAsync(stop.Token);

        writer.Post("1");
        await WaitUntil(() => written.Count == 1);

        // The first write hangs, like a full pipe; posting returns at once and only the latest line is kept.
        var posting = Task.Run(() =>
        {
            for (var i = 2; i <= 1000; i++)
            {
                writer.Post(i.ToString());
            }
        });
        Assert.Same(posting, await Task.WhenAny(posting, Task.Delay(TimeSpan.FromSeconds(5))));

        release.SetResult();
        await WaitUntil(() => written.Count == 2);
        await Task.Delay(100);
        Assert.Equal(["1", "1000"], written);

        await stop.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task A_failed_write_ends_the_loop_quietly()
    {
        Exception? failure = null;
        var writer = new LatestLineWriter((_, _) => throw new IOException("pipe closed"), TimeSpan.Zero, ex => failure = ex);
        var loop = writer.RunAsync(CancellationToken.None);

        writer.Post("1");

        Assert.Same(loop, await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.IsType<IOException>(failure);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
