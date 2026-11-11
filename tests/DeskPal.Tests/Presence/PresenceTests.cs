using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Presence;

public class PresenceResolverTests
{
    private static readonly ClassificationResult Video = new(ActivityCategory.Video, "哔哩哔哩", RuleLayer.Builtin);

    [Fact]
    public void Priority_is_paused_then_manual_then_locked_then_idle_then_classification()
    {
        var all = TestData.Browser("bilibili.com", "视频 - Google Chrome")
            .Apply(new LockSignal(true))
            .Apply(new IdleSignal(true));

        Assert.Equal(PresenceState.Paused, PresenceResolver.Resolve(all, Video, "午休", paused: true).State);
        Assert.Equal(PresenceState.Custom, PresenceResolver.Resolve(all, Video, "午休", paused: false).State);
        Assert.Equal(PresenceState.Locked, PresenceResolver.Resolve(all, Video, null, false).State);
        Assert.Equal(PresenceState.Away, PresenceResolver.Resolve(all with { IsLocked = false }, Video, null, false).State);
        Assert.Equal(PresenceState.Slacking, PresenceResolver.Resolve(all with { IsLocked = false, IsIdle = false }, Video, null, false).State);
    }

    [Fact]
    public void Idle_while_watching_fullscreen_video_is_not_away()
    {
        var snapshot = TestData.Browser("bilibili.com", fullscreen: true)
            .Apply(new MediaSignal(true, "番剧", null, "chrome"))
            .Apply(new IdleSignal(true));

        Assert.Equal(PresenceState.Slacking, PresenceResolver.Resolve(snapshot, Video, null, false).State);
    }

    [Fact]
    public void Detail_strips_browser_suffix_and_prefers_media_title_when_slacking()
    {
        var browser = TestData.Browser("bilibili.com", "【4K】猫猫合集 - Google Chrome");
        Assert.Equal("【4K】猫猫合集", PresenceResolver.Resolve(browser, Video, null, false).Detail);

        var music = TestData.App("cloudmusic").Apply(new MediaSignal(true, "晴天", "周杰伦", "cloudmusic"));
        var result = PresenceResolver.Resolve(music, new ClassificationResult(ActivityCategory.Music, "网易云音乐", RuleLayer.Builtin), null, false);
        Assert.Equal("晴天 - 周杰伦", result.Detail);
    }
}

public class PresenceDebouncerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly LocalPresence Working = new(PresenceState.Working, ActivityCategory.Work, "VS Code", "a.cs", null);
    private static readonly LocalPresence Video = new(PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫", null);

    private static PresenceDebouncer Create() => new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

    [Fact]
    public void First_presence_is_committed_immediately()
    {
        Assert.NotNull(Create().Offer(Working, T0));
    }

    [Fact]
    public void Change_is_committed_after_staying_stable_and_since_is_when_it_started()
    {
        var d = Create();
        d.Offer(Working, T0);

        Assert.Null(d.Offer(Video, T0.AddSeconds(1)));
        Assert.Null(d.Offer(Video, T0.AddSeconds(20)));
        var committed = d.Offer(Video, T0.AddSeconds(31));

        Assert.Equal(PresenceState.Slacking, committed!.Presence.State);
        Assert.Equal(T0.AddSeconds(1), committed.Since);
    }

    [Fact]
    public void Quick_switch_back_never_broadcasts()
    {
        var d = Create();
        d.Offer(Working, T0);

        Assert.Null(d.Offer(Video, T0.AddSeconds(1)));
        Assert.Null(d.Offer(Working, T0.AddSeconds(5)));
        Assert.Null(d.Offer(Working, T0.AddSeconds(60)));
        Assert.Equal(PresenceState.Working, d.Current!.Presence.State);
    }

    [Fact]
    public void Lock_and_unlock_are_immediate()
    {
        var d = Create();
        d.Offer(Working, T0);
        var locked = new LocalPresence(PresenceState.Locked, ActivityCategory.None, null, null, null);

        Assert.Equal(PresenceState.Locked, d.Offer(locked, T0.AddSeconds(1))!.Presence.State);
        Assert.Equal(PresenceState.Working, d.Offer(Working, T0.AddSeconds(2))!.Presence.State);
    }

    [Fact]
    public void Detail_change_is_rate_limited_and_keeps_since()
    {
        var d = Create();
        d.Offer(Working, T0);

        Assert.Null(d.Offer(Working with { Detail = "b.cs" }, T0.AddSeconds(10)));
        var committed = d.Offer(Working with { Detail = "c.cs" }, T0.AddSeconds(31));

        Assert.Equal("c.cs", committed!.Presence.Detail);
        Assert.Equal(T0, committed.Since);
    }
}

public class PrivacyFilterTests
{
    private static readonly CommittedPresence Video = new(
        new LocalPresence(PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫猫合集", null),
        DateTimeOffset.UnixEpoch);

    [Fact]
    public void Default_profile_shows_category_for_slacking_and_state_only_for_work()
    {
        var profile = PrivacyProfile.CreateDefault();
        var video = PrivacyFilter.Apply(Video, profile);
        var work = PrivacyFilter.Apply(new CommittedPresence(new LocalPresence(PresenceState.Working, ActivityCategory.Work, "Excel", "工资表.xlsx", null), DateTimeOffset.UnixEpoch), profile);

        Assert.Equal((ActivityCategory.Video, "哔哩哔哩", (string?)null), (video.Category, video.AppDisplayName, video.Detail));
        Assert.Equal((PresenceState.Working, ActivityCategory.None, (string?)null, (string?)null), (work.State, work.Category, work.AppDisplayName, work.Detail));
    }

    [Theory]
    [InlineData(PrivacyLevel.StateOnly, false, false)]
    [InlineData(PrivacyLevel.Category, true, false)]
    [InlineData(PrivacyLevel.Detail, true, true)]
    public void Levels_control_what_is_shared(PrivacyLevel level, bool hasApp, bool hasDetail)
    {
        var profile = new PrivacyProfile { Levels = { [ActivityCategory.Video] = level } };
        var result = PrivacyFilter.Apply(Video, profile);

        Assert.Equal(PresenceState.Slacking, result.State);
        Assert.Equal(hasApp, result.AppDisplayName is not null);
        Assert.Equal(hasDetail, result.Detail is not null);
    }
}

public class PresenceFormatterTests
{
    [Fact]
    public void Formats_headline_app_and_detail_separately()
    {
        Assert.Equal(new PresenceView("在看视频", "哔哩哔哩", "猫"), Format(PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫"));
        Assert.Equal(new PresenceView("正在使用 XMind", null, null), Format(PresenceState.Other, app: "XMind"));
        Assert.Equal(new PresenceView("午休中", null, null), PresenceFormatter.Format(new PresenceSnapshot { State = PresenceState.Custom, CustomText = "午休中" }));
    }

    [Theory]
    [InlineData(PresenceState.Working, ActivityCategory.Work, "工作中")]
    [InlineData(PresenceState.Meeting, ActivityCategory.Meeting, "工作中")]
    [InlineData(PresenceState.Communicating, ActivityCategory.Communication, "在聊天")]
    [InlineData(PresenceState.Slacking, ActivityCategory.Social, "在看社交网站")]
    [InlineData(PresenceState.Slacking, ActivityCategory.None, "在放松")]
    [InlineData(PresenceState.Other, ActivityCategory.Other, "工作中")]
    [InlineData(PresenceState.Away, ActivityCategory.None, "暂时离开")]
    [InlineData(PresenceState.Locked, ActivityCategory.None, "已锁屏")]
    public void Headlines_use_written_chinese(PresenceState state, ActivityCategory category, string expected)
    {
        Assert.Equal(expected, Format(state, category).Headline);
    }

    [Fact]
    public void Long_detail_is_shortened_for_display_only()
    {
        var title = new string('标', 40);
        var view = Format(PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", title);

        Assert.Equal(PresenceFormatter.DetailMaxLength, view.Detail!.Length);
        Assert.EndsWith("…", view.Detail);
        Assert.Equal(new string('标', 36), Format(PresenceState.Working, detail: new string('标', 36)).Detail);
    }

    [Fact]
    public void Meeting_is_not_offered_and_labels_avoid_slang()
    {
        Assert.DoesNotContain(ActivityCategory.Meeting, PresenceFormatter.SelectableCategories);
        Assert.DoesNotContain(ActivityCategory.None, PresenceFormatter.SelectableCategories);
        var labels = PresenceFormatter.SelectableCategories.Select(PresenceFormatter.CategoryName)
            .Concat(Enum.GetValues<PrivacyLevel>().Select(PresenceFormatter.PrivacyLevelName))
            .Concat(Enum.GetValues<PresenceState>().Select(s => Format(s, ActivityCategory.Video).Headline));
        Assert.DoesNotContain(labels, label => label.Contains('忙') || label.Contains("摸鱼") || label.Contains("开会") || label.Contains("沟通"));
    }

    private static PresenceView Format(PresenceState state, ActivityCategory category = ActivityCategory.None, string? app = null, string? detail = null) =>
        PresenceFormatter.Format(new PresenceSnapshot { State = state, Category = category, AppDisplayName = app, Detail = detail });

    [Fact]
    public void Duration_is_human_readable()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        Assert.Equal("刚刚", PresenceFormatter.Duration(t0, t0.AddSeconds(30)));
        Assert.Equal("12 分钟", PresenceFormatter.Duration(t0, t0.AddMinutes(12)));
        Assert.Equal("2 小时 5 分钟", PresenceFormatter.Duration(t0, t0.AddMinutes(125)));
    }
}