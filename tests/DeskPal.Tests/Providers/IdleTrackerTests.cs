using DeskPal.Client.Core.Providers;

namespace DeskPal.Tests.Providers;

public class IdleTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    [Fact]
    public void Nothing_reported_is_not_idle()
    {
        Assert.False(new IdleTracker().IsIdle(Start, Threshold));
    }

    [Fact]
    public void Time_keeps_counting_between_reports()
    {
        var tracker = new IdleTracker();
        tracker.Report(290, Start);

        Assert.False(tracker.IsIdle(Start, Threshold));
        Assert.True(tracker.IsIdle(Start.AddSeconds(10), Threshold));
    }

    [Fact]
    public void Input_resets_it()
    {
        var tracker = new IdleTracker();
        tracker.Report(400, Start);
        tracker.Report(0, Start.AddSeconds(5));

        Assert.False(tracker.IsIdle(Start.AddSeconds(6), Threshold));
    }

    [Fact]
    public void A_plugin_that_stopped_reporting_does_not_keep_the_user_idle()
    {
        var tracker = new IdleTracker();
        tracker.Report(400, Start);

        Assert.False(tracker.IsIdle(Start + IdleTracker.Stale + TimeSpan.FromSeconds(1), Threshold));
    }
}
