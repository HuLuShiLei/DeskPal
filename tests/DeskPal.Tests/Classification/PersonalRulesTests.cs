using DeskPal.Client.Core.Classification;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Classification;

public class PersonalRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 1, 0, 0, TimeSpan.Zero);
    private static readonly CorrectionTarget XMind = new(RuleKind.Process, "xmind", "XMind");

    [Fact]
    public void Cache_belongs_to_one_account()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deskpal-rules-{Guid.NewGuid():N}.json");
        try
        {
            var alice = Guid.NewGuid();
            var store = new PersonalRuleStore(path);
            store.Save(alice, [new UserRuleDto(RuleKind.Process, "wechat", ActivityCategory.Work, "微信", Start)]);

            Assert.Equal("wechat", Assert.Single(store.Load(alice)).Pattern);
            Assert.Empty(store.Load(Guid.NewGuid()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unclassified_usage_counts_foreground_time_and_skips_long_gaps()
    {
        var usage = new UnclassifiedUsage();
        usage.Record(XMind, Start, TimeZoneInfo.Utc);
        usage.Record(XMind, Start.AddSeconds(5), TimeZoneInfo.Utc);
        usage.Record(null, Start.AddSeconds(10), TimeZoneInfo.Utc);
        usage.Record(null, Start.AddSeconds(15), TimeZoneInfo.Utc);
        usage.Record(XMind, Start.AddSeconds(20), TimeZoneInfo.Utc);
        usage.Record(XMind, Start.AddMinutes(10), TimeZoneInfo.Utc);

        var app = Assert.Single(usage.Top(TimeSpan.Zero, 5));
        Assert.Equal(TimeSpan.FromSeconds(10), app.Today);
    }

    [Fact]
    public void Unclassified_usage_resets_at_midnight_and_forgets_classified_apps()
    {
        var usage = new UnclassifiedUsage();
        usage.Record(XMind, Start, TimeZoneInfo.Utc);
        usage.Record(XMind, Start.AddSeconds(20), TimeZoneInfo.Utc);
        Assert.Single(usage.Top(TimeSpan.Zero, 5));

        usage.Forget("xmind");
        Assert.Empty(usage.Top(TimeSpan.Zero, 5));

        usage.Record(XMind, Start.AddSeconds(25), TimeZoneInfo.Utc);
        usage.Record(XMind, Start.AddSeconds(45), TimeZoneInfo.Utc);
        usage.Record(XMind, new DateTimeOffset(Start.Date.AddDays(1).AddSeconds(1), TimeSpan.Zero), TimeZoneInfo.Utc);
        Assert.Empty(usage.Top(TimeSpan.Zero, 5));
    }

    [Fact]
    public void Unclassified_usage_keeps_counting_after_a_restart()
    {
        var before = new UnclassifiedUsage();
        before.Record(XMind, Start, TimeZoneInfo.Utc);
        before.Record(null, Start.AddSeconds(20), TimeZoneInfo.Utc);
        var (day, apps) = before.Export();

        var after = new UnclassifiedUsage();
        after.Restore(day!.Value, apps);
        after.Record(XMind, Start.AddMinutes(5), TimeZoneInfo.Utc);
        after.Record(null, Start.AddMinutes(5).AddSeconds(10), TimeZoneInfo.Utc);

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(after.Top(TimeSpan.Zero, 5)).Today);
    }

    [Fact]
    public void Unclassified_usage_ignores_a_saved_day_it_is_not_counting()
    {
        var usage = new UnclassifiedUsage();
        Assert.Null(usage.Export().Day);
        usage.Record(XMind, Start, TimeZoneInfo.Utc);

        usage.Restore(DateOnly.FromDateTime(Start.Date).AddDays(-1), [new UnclassifiedApp(RuleKind.Process, "xmind", "XMind", TimeSpan.FromHours(1))]);

        Assert.Empty(usage.Top(TimeSpan.Zero, 5));
    }

    [Fact]
    public void Browser_sites_are_not_counted()
    {
        var usage = new UnclassifiedUsage();
        var site = new CorrectionTarget(RuleKind.Domain, "example.com", "example.com");
        usage.Record(site, Start, TimeZoneInfo.Utc);
        usage.Record(site, Start.AddSeconds(20), TimeZoneInfo.Utc);

        Assert.Empty(usage.Top(TimeSpan.Zero, 5));
    }
}
