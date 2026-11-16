using DeskPal.Client.Core.Updates;

namespace DeskPal.Tests.Update;

public class UpdateCheckThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 7, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(429, null, false, true)]
    [InlineData(403, "0", false, true)]
    [InlineData(403, "12", true, true)]
    [InlineData(403, "12", false, false)]
    [InlineData(403, null, false, false)]
    [InlineData(404, "0", false, false)]
    [InlineData(200, "0", false, false)]
    public void Only_429_or_an_exhausted_403_is_a_rate_limit(int status, string? remaining, bool retryAfter, bool expected)
    {
        Assert.Equal(expected, UpdateCheckThrottle.IsRateLimited(status, remaining, retryAfter));
    }

    [Fact]
    public void Manual_check_reuses_up_to_date_for_five_minutes_per_channel()
    {
        var throttle = new UpdateCheckThrottle();
        throttle.UpToDate(beta: true, Now);

        Assert.True(throttle.RecentlyUpToDate(beta: true, Now.AddMinutes(4)));
        Assert.False(throttle.RecentlyUpToDate(beta: true, Now.AddMinutes(5)));
        Assert.False(throttle.RecentlyUpToDate(beta: false, Now.AddMinutes(1)));

        throttle.Forget(beta: true);
        Assert.False(throttle.RecentlyUpToDate(beta: true, Now.AddMinutes(1)));
    }

    [Fact]
    public void Rate_limit_blocks_until_reset()
    {
        var throttle = new UpdateCheckThrottle();

        var until = throttle.RateLimited(Now, Now.AddMinutes(20));

        Assert.Equal(Now.AddMinutes(20), until);
        Assert.Equal(until, throttle.BlockedUntil(Now.AddMinutes(19)));
        Assert.Null(throttle.BlockedUntil(Now.AddMinutes(20)));
    }

    [Fact]
    public void Missing_or_odd_reset_is_kept_between_one_minute_and_one_hour()
    {
        Assert.Equal(Now.AddHours(1), new UpdateCheckThrottle().RateLimited(Now, null));
        Assert.Equal(Now.AddHours(1), new UpdateCheckThrottle().RateLimited(Now, Now.AddDays(3)));
        Assert.Equal(Now.AddMinutes(1), new UpdateCheckThrottle().RateLimited(Now, Now.AddMinutes(-5)));
    }

    [Fact]
    public void A_shorter_reset_does_not_shorten_an_existing_block()
    {
        var throttle = new UpdateCheckThrottle();
        throttle.RateLimited(Now, Now.AddMinutes(30));

        Assert.Equal(Now.AddMinutes(30), throttle.RateLimited(Now.AddMinutes(1), Now.AddMinutes(10)));
    }
}