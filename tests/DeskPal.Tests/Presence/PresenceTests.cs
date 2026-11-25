using System.Globalization;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Presence;

public class PresenceResolverTests
{
    private static readonly ClassificationResult Video = new(ActivityCategory.Video, "哔哩哔哩", RuleLayer.Builtin);

    [Fact]
    public void Priority_is_paused_then_locked_then_idle_then_classification()
    {
        var all = TestData.Browser("bilibili.com", "视频 - Google Chrome")
            .Apply(new LockSignal(true))
            .Apply(new IdleSignal(true));

        Assert.Equal(PresenceState.Paused, PresenceResolver.Resolve(all, Video, paused: true).State);
        Assert.Equal(PresenceState.Locked, PresenceResolver.Resolve(all, Video, false).State);
        Assert.Equal(PresenceState.Away, PresenceResolver.Resolve(all with { IsLocked = false }, Video, false).State);
        Assert.Equal(PresenceState.Slacking, PresenceResolver.Resolve(all with { IsLocked = false, IsIdle = false }, Video, false).State);
    }

    [Fact]
    public void Idle_while_watching_fullscreen_video_is_not_away()
    {
        var snapshot = TestData.Browser("bilibili.com", fullscreen: true)
            .Apply(new MediaSignal(true, "番剧", null, "chrome"))
            .Apply(new IdleSignal(true));

        Assert.Equal(PresenceState.Slacking, PresenceResolver.Resolve(snapshot, Video, false).State);
    }

    [Fact]
    public void Detail_strips_browser_suffix_and_prefers_media_title_when_slacking()
    {
        var browser = TestData.Browser("bilibili.com", "【4K】猫猫合集 - Google Chrome");
        Assert.Equal("【4K】猫猫合集", PresenceResolver.Resolve(browser, Video, false).Detail);

        var music = TestData.App("cloudmusic").Apply(new MediaSignal(true, "晴天", "周杰伦", "cloudmusic"));
        var result = PresenceResolver.Resolve(music, new ClassificationResult(ActivityCategory.Music, "网易云音乐", RuleLayer.Builtin), false);
        Assert.Equal(PresenceState.Other, result.State);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void Browsing_keeps_the_page_title_instead_of_the_media_title()
    {
        var browser = TestData.Browser("www.bilibili.com", "【4K】猫猫合集 - Google Chrome")
            .Apply(new MediaSignal(true, "晴天", "周杰伦", "chrome"));
        var classification = new ClassificationResult(ActivityCategory.Browsing, "bilibili.com", RuleLayer.Browser);

        var result = PresenceResolver.Resolve(browser, classification, false);

        Assert.Equal(PresenceState.Browsing, result.State);
        Assert.Equal("bilibili.com", result.AppDisplayName);
        Assert.Equal("【4K】猫猫合集", result.Detail);
    }
}

public class PresenceDebouncerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly LocalPresence Working = new(PresenceState.Working, ActivityCategory.Work, "VS Code", "a.cs");
    private static readonly LocalPresence Video = new(PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫");

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
        Assert.Equal(T0.AddSeconds(31), d.PendingDeadline);
        var committed = d.Offer(Video, T0.AddSeconds(31));

        Assert.Equal(PresenceState.Slacking, committed!.Presence.State);
        Assert.Equal(T0.AddSeconds(1), committed.Since);
        Assert.Null(d.PendingDeadline);
    }

    [Fact]
    public void Quick_switch_back_never_broadcasts()
    {
        var d = Create();
        d.Offer(Working, T0);

        Assert.Null(d.Offer(Video, T0.AddSeconds(1)));
        Assert.Null(d.Offer(Working, T0.AddSeconds(5)));
        Assert.Null(d.PendingDeadline);
        Assert.Null(d.Offer(Working, T0.AddSeconds(60)));
        Assert.Equal(PresenceState.Working, d.Current!.Presence.State);
    }

    [Fact]
    public void Lock_and_unlock_are_immediate()
    {
        var d = Create();
        d.Offer(Working, T0);
        var locked = new LocalPresence(PresenceState.Locked, ActivityCategory.None, null, null);

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

    [Fact]
    public void No_delay_commits_at_once_but_title_changes_stay_rate_limited()
    {
        var d = new PresenceDebouncer(TimeSpan.Zero, SwitchDelay.DetailInterval(0));
        d.Offer(Working, T0);

        Assert.Equal(T0.AddSeconds(1), d.Offer(Video, T0.AddSeconds(1))!.Since);
        Assert.Null(d.PendingDeadline);
        Assert.Null(d.Offer(Video with { Detail = "狗" }, T0.AddSeconds(5)));
        Assert.Equal("狗", d.Offer(Video with { Detail = "狗" }, T0.AddSeconds(11))!.Presence.Detail);
    }

    [Fact]
    public void Shorter_delay_applies_to_what_is_already_pending()
    {
        var d = Create();
        d.Offer(Working, T0);
        Assert.Null(d.Offer(Video, T0.AddSeconds(1)));

        d.Configure(TimeSpan.FromSeconds(5), SwitchDelay.DetailInterval(5));
        Assert.Equal(T0.AddSeconds(6), d.PendingDeadline);
        Assert.Equal(T0.AddSeconds(1), d.Offer(Video, T0.AddSeconds(6))!.Since);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(7, SwitchDelay.DefaultSeconds)]
    [InlineData(-1, SwitchDelay.DefaultSeconds)]
    public void Switch_delay_accepts_only_the_listed_options(int seconds, int expected)
    {
        Assert.Equal(expected, SwitchDelay.Normalize(seconds));
    }
}

public class TitleAndIdleTests
{
    [Theory]
    [InlineData("(12) 猫猫合集_哔哩哔哩_bilibili", "猫猫合集_哔哩哔哩_bilibili")]
    [InlineData("[3] Inbox", "Inbox")]
    [InlineData("\u200e某个群组 – (255549)", "某个群组")]
    [InlineData("Telegram", "Telegram")]
    [InlineData("2026 年计划 (v2)", "2026 年计划 (v2)")]
    [InlineData("女流66的抖音直播间 - 抖音直播 - 正在播放音频 - 内存用量高 - 917 MB", "女流66的抖音直播间 - 抖音直播")]
    [InlineData("女流66的抖音直播间 - 抖音直播 - 正在播放音频", "女流66的抖音直播间 - 抖音直播")]
    [InlineData("Some video - YouTube - Audio playing - High memory usage - 1.2 GB", "Some video - YouTube")]
    [InlineData("Docs - Memory usage - 512 MB", "Docs")]
    [InlineData("下载 Ubuntu 24.04 - 5.7 GB", "下载 Ubuntu 24.04 - 5.7 GB")]
    [InlineData("Audio playing", "Audio playing")]
    [InlineData("  ", null)]
    public void Changing_counters_are_removed_from_titles(string title, string? expected)
    {
        Assert.Equal(expected, TitleText.Clean(title));
    }

    [Theory]
    [InlineData("123.222.222.32 堡垒机", "堡垒机")]
    [InlineData("堡垒机 - 10.0.0.1:8080", "堡垒机")]
    [InlineData("http://192.168.1.1:8080/login - 路由器管理", "路由器管理")]
    [InlineData("ssh root@172.16.0.5 - 终端", "ssh - 终端")]
    [InlineData("[fe80::1]:3389 远程桌面", "远程桌面")]
    [InlineData("向日葵远程控制 识别码：123 456 789 验证码：ab12cd", "向日葵远程控制")]
    [InlineData("ToDesk 123 456 789", "ToDesk")]
    [InlineData("张三 (138-1234-5678)", "张三")]
    [InlineData("联系 +86 13812345678 预约", "联系 预约")]
    [InlineData("客服 021-12345678", "客服")]
    [InlineData("发送给 someone@example.com - 邮件", "发送给 - 邮件")]
    [InlineData("身份证 11010519491231002X 办理", "身份证 办理")]
    [InlineData("QQ 123456789 的聊天", "QQ 的聊天")]
    [InlineData("12.34.56.78", null)]
    [InlineData("周报 2026-11-25 - 10:30", "周报 2026-11-25 - 10:30")]
    [InlineData("第 123456 期", "第 123456 期")]
    [InlineData("下载 Ubuntu 24.04 - 5.7 GB", "下载 Ubuntu 24.04 - 5.7 GB")]
    [InlineData("1080p 60fps 2024 版", "1080p 60fps 2024 版")]
    [InlineData("std::vector - Foo::Bar", "std::vector - Foo::Bar")]
    [InlineData("修改密码 - 设置", "修改密码 - 设置")]
    public void Sensitive_details_are_removed_from_titles(string title, string? expected)
    {
        Assert.Equal(expected, TitleText.Clean(title));
    }

    [Fact]
    public void A_website_opened_by_IP_address_is_named_IP_address_and_its_title_has_no_address()
    {
        var snapshot = TestData.Browser("192.168.66.10", "192.168.66.10:9000/#/home 堡垒机 - Google Chrome");
        var presence = PresenceResolver.Resolve(snapshot, new ClassificationResult(ActivityCategory.Browsing, "192.168.66.10", RuleLayer.Browser), paused: false);
        Assert.Equal((SensitiveText.IpAddressName, "堡垒机"), (presence.AppDisplayName, presence.Detail));
        Assert.Equal("example.com", SensitiveText.MaskHost("example.com"));

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("IP address", AppNames.Localize(SensitiveText.IpAddressName));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static readonly ClassificationResult Watching = new(ActivityCategory.Video, "bilibili.com", RuleLayer.Browser);

    [Fact]
    public void Watching_in_the_foreground_is_not_idle()
    {
        var windowed = TestData.Browser("bilibili.com", "猫猫合集_哔哩哔哩_bilibili - Google Chrome")
            .Apply(new MediaSignal(true, "猫猫合集", null, "Chrome"))
            .Apply(new IdleSignal(true));
        var presence = PresenceResolver.Resolve(windowed, Watching, paused: false);
        Assert.Equal((PresenceState.Slacking, "猫猫合集"), (presence.State, presence.Detail));

        var backgroundMedia = TestData.App("ffxiv_dx11", "最终幻想XIV", fullscreen: true)
            .Apply(new MediaSignal(true, "猫猫合集", null, "MSEdge"))
            .Apply(new IdleSignal(true));
        Assert.Equal(PresenceState.Away, PresenceResolver.Resolve(backgroundMedia, new ClassificationResult(ActivityCategory.Game, "最终幻想XIV", RuleLayer.Builtin), paused: false).State);

        var fullscreenPlayer = TestData.App("PotPlayerMini64", "电影.mkv", fullscreen: true).Apply(new IdleSignal(true));
        Assert.Equal(PresenceState.Slacking, PresenceResolver.Resolve(fullscreenPlayer, new ClassificationResult(ActivityCategory.Video, "PotPlayer", RuleLayer.Builtin), paused: false).State);
    }
}

public class PrivacyFilterTests
{
    private static readonly CommittedPresence Video = new(
        new LocalPresence(PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫猫合集"),
        DateTimeOffset.UnixEpoch);

    [Fact]
    public void Default_profile_shows_the_app_for_slacking_and_only_the_state_for_work()
    {
        var profile = PrivacyProfile.CreateDefault();
        var video = PrivacyFilter.Apply(Video, profile);
        var work = PrivacyFilter.Apply(new CommittedPresence(new LocalPresence(PresenceState.Working, ActivityCategory.Work, "Excel", "工资表.xlsx"), DateTimeOffset.UnixEpoch), profile);

        Assert.Equal((ActivityCategory.Video, "哔哩哔哩", (string?)null), (video.Category, video.AppDisplayName, video.Detail));
        Assert.Equal((PresenceState.Working, ActivityCategory.Work, (string?)null, (string?)null), (work.State, work.Category, work.AppDisplayName, work.Detail));

        var browsing = PrivacyFilter.Apply(new CommittedPresence(new LocalPresence(PresenceState.Browsing, ActivityCategory.Browsing, "bilibili.com", "猫猫合集"), DateTimeOffset.UnixEpoch), profile);
        Assert.Equal((PresenceState.Browsing, "bilibili.com", (string?)null), (browsing.State, browsing.AppDisplayName, browsing.Detail));
    }

    [Theory]
    [InlineData(PrivacyLevel.StateOnly, false, false)]
    [InlineData(PrivacyLevel.Category, true, false)]
    [InlineData(PrivacyLevel.Detail, true, true)]
    public void Levels_control_what_is_shared(PrivacyLevel level, bool hasApp, bool hasDetail)
    {
        var profile = new PrivacyProfile { Levels = { [ActivityCategory.Video] = level } };
        var result = PrivacyFilter.Apply(Video, profile);

        Assert.Equal((PresenceState.Slacking, ActivityCategory.Video), (result.State, result.Category));
        Assert.Equal(hasApp, result.AppDisplayName is not null);
        Assert.Equal(hasDetail, result.Detail is not null);
    }
}

public class PresenceFormatterTests
{
    [Fact]
    public void Formats_headline_app_and_detail_separately()
    {
        Assert.Equal(new PresenceView("在看视频", "哔哩哔哩", "猫", "relax"), Format(PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫"));
        Assert.Equal(new PresenceView("正在浏览网页", "bilibili.com", "猫猫合集", "browse"), Format(PresenceState.Browsing, ActivityCategory.Browsing, "bilibili.com", "猫猫合集"));
        Assert.Equal(new PresenceView("正在浏览网页", "bilibili.com", null, "browse"), Format(PresenceState.Browsing, ActivityCategory.Browsing, "bilibili.com", null));
        Assert.Equal(new PresenceView("正在浏览网页", null, "新标签页", "browse"), Format(PresenceState.Browsing, ActivityCategory.Browsing, null, "新标签页"));
        Assert.Equal(new PresenceView("其他活动", "XMind", null, "other"), Format(PresenceState.Other, app: "XMind"));
        Assert.Equal(new PresenceView("午休中", null, null, "other"), PresenceFormatter.Format(new PresenceSnapshot { State = PresenceState.Custom, CustomText = "午休中" }));
    }

    [Fact]
    public void Detail_matching_the_app_name_is_not_repeated()
    {
        Assert.Equal(new PresenceView("在聊天", "QQ", null, "chat"), Format(PresenceState.Communicating, ActivityCategory.Communication, "QQ", "QQ"));
        Assert.Equal(new PresenceView("在聊天", "QQ", null, "chat"), Format(PresenceState.Communicating, ActivityCategory.Communication, "QQ", " qq "));
        Assert.Equal(new PresenceView("其他活动", "XMind", null, "other"), Format(PresenceState.Other, ActivityCategory.Other, "XMind", "XMind"));
        Assert.Equal(new PresenceView("在聊天", "QQ", "QQ - 聊天", "chat"), Format(PresenceState.Communicating, ActivityCategory.Communication, "QQ", "QQ - 聊天"));
    }

    [Theory]
    [InlineData(PresenceState.Working, ActivityCategory.Work, "工作中")]
    [InlineData(PresenceState.Meeting, ActivityCategory.Meeting, "工作中")]
    [InlineData(PresenceState.Communicating, ActivityCategory.Communication, "在聊天")]
    [InlineData(PresenceState.Slacking, ActivityCategory.Social, "正在浏览网页")]
    [InlineData(PresenceState.Slacking, ActivityCategory.Music, "其他活动")]
    [InlineData(PresenceState.Slacking, ActivityCategory.Video, "在看视频")]
    [InlineData(PresenceState.Slacking, ActivityCategory.Game, "在玩游戏")]
    [InlineData(PresenceState.Slacking, ActivityCategory.None, "在放松")]
    [InlineData(PresenceState.Other, ActivityCategory.Other, "其他活动")]
    [InlineData(PresenceState.Away, ActivityCategory.None, "暂离")]
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
        Assert.DoesNotContain(PresenceFormatter.SelectableCategories, ActivityCategoryExtensions.IsRetired);
        Assert.True(PresenceFormatter.SelectableCategories.ToList().IndexOf(ActivityCategory.Browsing) < PresenceFormatter.SelectableCategories.ToList().IndexOf(ActivityCategory.Other));
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

/// <summary>AppNames is process-wide, so this runs alone.</summary>
[CollectionDefinition(nameof(AppNamesTests), DisableParallelization = true)]
public class AppNamesCollection;

[Collection(nameof(AppNamesTests))]
public class AppNamesTests
{
    [Fact]
    public void Builtin_names_are_shown_in_the_viewers_language()
    {
        AppNames.SetBuiltin([new RuleDto(RuleKind.Process, "testapp", ActivityCategory.Communication, "测试应用", new Dictionary<string, string> { ["en"] = "Test App" })]);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            var snapshot = new PresenceSnapshot { State = PresenceState.Communicating, Category = ActivityCategory.Communication, AppDisplayName = "测试应用", Detail = "测试应用" };
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            Assert.Equal(new PresenceView("在聊天", "测试应用", null, "chat"), PresenceFormatter.Format(snapshot));

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(new PresenceView("Chatting", "Test App", null, "chat"), PresenceFormatter.Format(snapshot));
            Assert.Equal("XMind", AppNames.Localize("XMind"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            AppNames.SetBuiltin([]);
        }
    }
}