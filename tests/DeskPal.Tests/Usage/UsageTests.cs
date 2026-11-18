using System.Security.Cryptography;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;
using DeskPal.Shared.Stats;

namespace DeskPal.Tests.Usage;

public class UsageTests : IDisposable
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    private static readonly DateOnly Day = new(2026, 11, 11);
    private readonly List<string> _files = [];

    private static DateTimeOffset At(int hour, int minute, int second = 0, int dayOffset = 0) =>
        new DateTimeOffset(Day.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute, second)), TimeSpan.FromHours(8));

    private static LocalPresence Work(string title) => new(PresenceState.Working, ActivityCategory.Work, "Cursor", title);

    [Fact]
    public void Recorder_extends_a_segment_and_starts_a_new_one_when_the_title_changes()
    {
        var writer = new FakeWriter();
        var recorder = new UsageRecorder(writer, Zone);
        for (var s = 0; s <= 65; s += 5)
        {
            recorder.Record(Work("a.md"), At(9, 0).AddSeconds(s));
        }

        Assert.Single(writer.Rows);
        Assert.Equal(At(9, 1), writer.Rows[0].End);

        recorder.Record(Work("b.md"), At(9, 1, 10));
        recorder.Record(Work("b.md"), At(9, 1, 15));
        recorder.Flush();

        Assert.Equal(2, writer.Rows.Count);
        Assert.Equal(At(9, 1, 10), writer.Rows[0].End);
        Assert.Equal((At(9, 1, 10), At(9, 1, 15), "b.md"), (writer.Rows[1].Start, writer.Rows[1].End, writer.Rows[1].Title));
    }

    [Fact]
    public void Recorder_does_not_count_gaps_and_splits_at_local_midnight()
    {
        var writer = new FakeWriter();
        var recorder = new UsageRecorder(writer, Zone);
        recorder.Record(Work("a.md"), At(9, 0));
        recorder.Record(Work("a.md"), At(9, 0, 5));
        recorder.Record(Work("a.md"), At(9, 1));
        recorder.Record(Work("a.md"), At(9, 1, 5));
        recorder.Flush();

        Assert.Equal([(At(9, 0), At(9, 0, 5)), (At(9, 1), At(9, 1, 5))], writer.Rows.Select(r => (r.Start, r.End)));

        writer = new FakeWriter();
        recorder = new UsageRecorder(writer, Zone);
        recorder.Record(Work("a.md"), At(23, 59, 50));
        recorder.Record(Work("a.md"), At(23, 59, 55));
        recorder.Record(Work("a.md"), At(0, 0, 5, dayOffset: 1));
        recorder.Flush();

        Assert.Equal([(At(23, 59, 50), At(0, 0, 0, 1)), (At(0, 0, 0, 1), At(0, 0, 5, 1))], writer.Rows.Select(r => (r.Start, r.End)));
    }

    [Fact]
    public void Recorder_leaves_out_titles_of_listed_apps_and_details_of_away_time()
    {
        var writer = new FakeWriter();
        var recorder = new UsageRecorder(writer, Zone);
        recorder.SetNoTitleRules([new(NoTitleKind.App, "招商银行")]);
        recorder.Record(new LocalPresence(PresenceState.Browsing, ActivityCategory.Browsing, "招商银行", "我的账户 - 余额"), At(9, 0));
        recorder.Record(new LocalPresence(PresenceState.Away, ActivityCategory.Video, "哔哩哔哩", "猫"), At(9, 0, 5));
        recorder.Record(null, At(9, 0, 10));

        Assert.Equal(("招商银行", (string?)null), (writer.Rows[0].App, writer.Rows[0].Title));
        Assert.Equal((PresenceState.Away, ActivityCategory.None, (string?)null, (string?)null), (writer.Rows[1].State, writer.Rows[1].Category, writer.Rows[1].App, writer.Rows[1].Title));
    }

    [Theory]
    [InlineData(NoTitleKind.App, " 招商银行 ", "招商银行")]
    [InlineData(NoTitleKind.Process, "CMB.EXE", "cmb")]
    [InlineData(NoTitleKind.Domain, "https://www.Zhihu.com/question/1", "zhihu.com")]
    [InlineData(NoTitleKind.Domain, "*.zhihu.com", "zhihu.com")]
    [InlineData(NoTitleKind.Title, " 病历 ", "病历")]
    public void No_title_rules_are_normalized_like_personal_rules(NoTitleKind kind, string text, string pattern) =>
        Assert.Equal(pattern, NoTitleRule.Create(kind, text)!.Pattern);

    [Fact]
    public void No_title_rules_match_by_app_process_site_or_title_keyword()
    {
        var writer = new FakeWriter();
        var recorder = new UsageRecorder(writer, Zone);
        recorder.SetNoTitleRules(
        [
            NoTitleRule.Create(NoTitleKind.Process, "cmb.exe")!,
            NoTitleRule.Create(NoTitleKind.Domain, "zhihu.com")!,
            NoTitleRule.Create(NoTitleKind.Title, "病历")!,
        ]);

        var s = 0;
        void Record(string app, string title, string process, string? domain = null) =>
            recorder.Record(new LocalPresence(PresenceState.Other, ActivityCategory.Other, app, title), At(9, 0).AddSeconds(s += 5), process, domain);
        Record("招商银行", "我的账户", "CMB");
        Record("zhuanlan.zhihu.com", "如何评价", "chrome", "zhuanlan.zhihu.com");
        Record("notzhihu.com", "首页", "chrome", "notzhihu.com");
        Record("医院系统", "张三的病历", "his");
        Record("记事本", "购物清单", "notepad");
        recorder.Record(null, At(9, 0).AddSeconds(s + 5));

        Assert.Equal([null, null, "首页", null, "购物清单"], writer.Rows.Select(r => r.Title));
        Assert.Equal(["招商银行", "zhuanlan.zhihu.com", "notzhihu.com", "医院系统", "记事本"], writer.Rows.Select(r => r.App));
        Assert.Null(NoTitleRule.Create(NoTitleKind.Domain, "https://"));
    }

    [Fact]
    public void Aggregator_sums_days_hours_timeline_and_rankings_without_away_time()
    {
        UsageSegment[] segments =
        [
            new(At(9, 0), At(9, 30), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"),
            new(At(9, 30), At(9, 40), PresenceState.Away, ActivityCategory.None, null, null),
            new(At(9, 40), At(10, 10), PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫"),
        ];

        var report = UsageAggregator.Build(segments, [], Day, Day);

        Assert.Equal(3600, UsageAggregator.ActiveSeconds(report));
        Assert.Equal(600, report.Days.Single(d => d.State == PresenceState.Away).Seconds);
        Assert.Equal([(9, 1800L), (9, 1200L), (10, 600L)], report.Hours.Select(h => (h.Hour, h.Seconds)));
        Assert.Equal([PresenceState.Working, PresenceState.Away, PresenceState.Slacking], report.Timeline.Select(s => s.State));
        Assert.Equal(9 * 3600, report.Timeline[0].Start);
        Assert.DoesNotContain(report.Apps, a => a.State == PresenceState.Away);
        Assert.Equal(["Cursor", "哔哩哔哩"], report.Apps.Select(a => a.App!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Store_folds_expired_days_into_daily_totals()
    {
        using var store = NewStore();
        store.Add(new UsageSegment(At(9, 0, dayOffset: -1), At(9, 30, dayOffset: -1), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"));
        store.Add(new UsageSegment(At(10, 0, dayOffset: -1), At(10, 30, dayOffset: -1), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"));
        store.Add(new UsageSegment(At(9, 0), At(9, 10), PresenceState.Working, ActivityCategory.Work, "Cursor", "b.md"));
        store.AddView(new StatsViewRecord(Guid.NewGuid(), "房间", Guid.NewGuid(), "Bob", At(9, 0, dayOffset: -8)));
        store.AddView(new StatsViewRecord(Guid.NewGuid(), "房间", Guid.NewGuid(), "Carol", At(9, 0)));

        Assert.Equal(2, store.Compact(Day, At(12, 0)));

        var yesterday = Day.AddDays(-1);
        Assert.Empty(store.Segments(At(0, 0, dayOffset: -1), At(0, 0)));
        Assert.Equal(3600, Assert.Single(store.Rollups(yesterday, yesterday)).Seconds);
        var report = UsageAggregator.Build(store.Segments(At(0, 0, dayOffset: -1), At(0, 0)), store.Rollups(yesterday, yesterday), yesterday, yesterday);
        Assert.Equal(3600, UsageAggregator.ActiveSeconds(report));
        Assert.Empty(report.Timeline);
        Assert.Equal("a.md", Assert.Single(report.Titles).Title);
        Assert.Single(store.Segments(At(0, 0), At(0, 0, dayOffset: 1)));
        Assert.Equal(["Carol"], store.Views(At(0, 0, dayOffset: -30)).Select(v => v.ViewerName));
    }

    [Fact]
    public void Pruning_drops_old_view_records_and_keeps_every_segment()
    {
        using var store = NewStore();
        store.Add(new UsageSegment(At(9, 0, dayOffset: -400), At(9, 30, dayOffset: -400), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"));
        store.AddView(new StatsViewRecord(Guid.NewGuid(), "房间", Guid.NewGuid(), "Bob", At(9, 0, dayOffset: -8)));
        store.AddView(new StatsViewRecord(Guid.NewGuid(), "房间", Guid.NewGuid(), "Carol", At(9, 0)));

        store.PruneViews(At(12, 0));

        Assert.Equal("a.md", Assert.Single(store.Segments(At(0, 0, dayOffset: -400), At(0, 0, dayOffset: -399))).Title);
        Assert.Empty(store.Rollups(Day.AddDays(-400), Day));
        Assert.Equal(["Carol"], store.Views(At(0, 0, dayOffset: -30)).Select(v => v.ViewerName));
    }

    [Fact]
    public void Importing_the_same_export_twice_counts_once()
    {
        using var source = NewStore();
        source.Add(new UsageSegment(At(9, 0), At(9, 30), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"));
        source.Add(new UsageSegment(At(9, 0, dayOffset: -100), At(9, 30, dayOffset: -100), PresenceState.Slacking, ActivityCategory.Game, "原神", null));
        source.Compact(Day.AddDays(-90), At(12, 0));
        var export = source.Export(At(12, 0));

        using var target = NewStore();
        Assert.Equal(new ImportResult(1, 1), target.Import(export));
        Assert.Equal(new ImportResult(0, 0), target.Import(export));
        Assert.Throws<InvalidDataException>(() => target.Import(export with { SchemaVersion = 99 }));

        var report = UsageAggregator.Build(target.Segments(At(0, 0, dayOffset: -100), At(0, 0, dayOffset: 1)), target.Rollups(Day.AddDays(-100), Day), Day.AddDays(-100), Day);
        Assert.Equal(3600, UsageAggregator.ActiveSeconds(report));
    }

    [Fact]
    public void Filter_shows_each_category_at_the_room_privacy_level()
    {
        UsageSegment[] segments =
        [
            new(At(9, 0), At(10, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", "secret.md"),
            new(At(10, 0), At(10, 30), PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫"),
            new(At(20, 0), At(21, 0), PresenceState.Slacking, ActivityCategory.Game, "原神", "深渊"),
        ];
        var profile = new PrivacyProfile
        {
            Levels = { [ActivityCategory.Work] = PrivacyLevel.StateOnly, [ActivityCategory.Video] = PrivacyLevel.Category, [ActivityCategory.Game] = PrivacyLevel.Detail },
        };

        var shown = StatsFilter.Apply(UsageAggregator.Build(segments, [], Day, Day), profile);

        Assert.Contains(shown.Days, d => d is { State: PresenceState.Working, Category: ActivityCategory.Work, Seconds: 3600 });
        Assert.DoesNotContain(shown.Apps.Concat(shown.Titles), i => i.App == "Cursor" || i.Title == "secret.md");
        Assert.Contains(shown.Apps, a => a is { Category: ActivityCategory.Work, App: null, Seconds: 3600 });
        Assert.Contains(shown.Apps, a => a.App == "哔哩哔哩");
        Assert.DoesNotContain(shown.Titles, t => t.Title == "猫");
        Assert.Contains(shown.Titles, t => t.Title == "深渊");
    }

    [Fact]
    public void Only_the_requester_key_opens_the_response()
    {
        var report = UsageAggregator.Build([new UsageSegment(At(9, 0), At(9, 30), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md")], [], Day, Day);
        using var requester = new StatsCrypto.RequestKey();
        using var other = new StatsCrypto.RequestKey();
        var requestId = Guid.NewGuid();

        var response = StatsCrypto.Encrypt(requestId, requester.PublicKey, report);

        Assert.DoesNotContain("Cursor", response.Cipher);
        Assert.Equal(1800, UsageAggregator.ActiveSeconds(requester.Decrypt(response)));
        Assert.ThrowsAny<CryptographicException>(() => other.Decrypt(response));
        Assert.ThrowsAny<CryptographicException>(() => requester.Decrypt(response with { RequestId = Guid.NewGuid() }));
        var tampered = Convert.FromBase64String(response.Cipher!);
        tampered[0] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => requester.Decrypt(response with { Cipher = Convert.ToBase64String(tampered) }));
    }

    [Fact]
    public void Day_view_shows_usage_comparison_and_ranking()
    {
        UsageSegment[] segments =
        [
            new(At(9, 0), At(10, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"),
            new(At(10, 0), At(10, 10), PresenceState.Away, ActivityCategory.None, null, null),
            new(At(10, 10), At(10, 40), PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫"),
        ];
        var report = UsageAggregator.Build(segments, [], Day, Day) with { Previous = [new(Day, -1, PresenceState.Working, ActivityCategory.Work, 3600)] };

        var view = StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.App, Day, Day);

        Assert.Equal("11 月 11 日 星期三（今天）", view.Title);
        Assert.Equal(("使用时长", "1 小时 30 分钟"), (view.TotalLabel, view.Total));
        Assert.Null(view.Compare);
        Assert.Empty(view.Changes);
        var past = StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.App, Day, Day.AddDays(1));
        Assert.Equal("比前一天多 30 分钟", past.Compare);
        Assert.Equal([("视频", "+30 分钟")], past.Changes.Select(c => (c.Name, c.Text)));
        Assert.Null(StatsViewBuilder.Build(report with { Previous = [] }, StatsScale.Day, StatsRankBy.App, Day, Day).Compare);
        Assert.Equal("首次活动 9:00 · 最后活动 10:40", view.Bounds);
        Assert.Null(view.Rhythm);
        Assert.Equal("暂离 10 分钟", view.Summary);
        Assert.Equal(24, view.Bars.Count);
        Assert.Equal([("Cursor", "工作"), ("哔哩哔哩", "视频")], view.Ranking.Select(r => (r.Name, r.Sub)));
        Assert.Equal(["Work", "Video"], view.Legend.Select(l => l.Key));
        Assert.True(view.CanPrev);
        Assert.False(view.CanNext);
        Assert.Null(view.Note);
    }

    [Fact]
    public void Week_view_shows_the_daily_average_and_a_bar_per_day()
    {
        var monday = Day.AddDays(-2);
        UsageSegment[] segments =
        [
            new(At(9, 0, dayOffset: -2), At(11, 0, dayOffset: -2), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"),
            new(At(9, 0), At(10, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"),
        ];
        var report = UsageAggregator.Build(segments, [], monday, Day);

        var view = StatsViewBuilder.Build(report, StatsScale.Week, StatsRankBy.Category, Day, Day);

        Assert.Equal("11 月 9 日 – 11 月 15 日（本周）", view.Title);
        Assert.Equal(("日均使用时长", "1 小时"), (view.TotalLabel, view.Total));
        Assert.Equal("共 3 小时", view.Summary);
        Assert.Equal(["一", "二", "三", "四", "五", "六", "日"], view.Bars.Select(b => b.Label));
        Assert.Null(view.Bars[3].Date);
        Assert.Equal(2 * 3600, view.BarMax);
        Assert.Equal("工作", Assert.Single(view.Ranking).Name);
    }

    [Fact]
    public void A_member_view_says_how_far_back_it_goes_and_what_is_hidden()
    {
        UsageSegment[] segments =
        [
            new(At(9, 0), At(10, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"),
            new(At(10, 0), At(10, 30), PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", "猫"),
        ];
        var profile = new PrivacyProfile { Levels = { [ActivityCategory.Work] = PrivacyLevel.StateOnly, [ActivityCategory.Video] = PrivacyLevel.Category } };
        var report = StatsFilter.Apply(UsageAggregator.Build(segments, [], Day, Day), profile) with { SharedDays = 1 };

        var view = StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.Title, Day, Day, "Bob");

        Assert.Equal("Bob 公开了最近 1 天的统计。部分类别仅公开了状态。部分类别未公开标题，仅显示应用名。", view.Note);
        Assert.False(view.CanPrev);
        Assert.Contains(view.Ranking, r => r is { Name: "工作", Sub: null, Hidden: false });
        Assert.Contains(view.Ranking, r => r.Name == "哔哩哔哩");
    }

    [Fact]
    public void A_category_can_be_shown_alone()
    {
        UsageSegment[] segments =
        [
            new(At(9, 0), At(10, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"),
            new(At(10, 0), At(10, 10), PresenceState.Away, ActivityCategory.None, null, null),
            new(At(10, 10), At(10, 40), PresenceState.Slacking, ActivityCategory.Game, "原神", null),
            new(At(20, 0), At(20, 20), PresenceState.Slacking, ActivityCategory.Game, "Steam", null),
        ];
        var report = UsageAggregator.Build(segments, [], Day, Day) with { Previous = [new(Day, -1, PresenceState.Working, ActivityCategory.Work, 3600)] };

        var all = StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.Category, Day, Day.AddDays(1));
        Assert.Equal([("工作", "Work"), ("游戏", "Game")], all.Ranking.Select(r => (r.Name, r.Filter)));

        var game = StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.App, Day, Day.AddDays(1), filter: "Game");
        Assert.Equal("Game", game.Filter);
        Assert.Equal(("使用时长（游戏）", "50 分钟"), (game.TotalLabel, game.Total));
        Assert.Null(game.Compare);
        Assert.Null(game.Summary);
        Assert.Equal(["Work", "Game"], game.Legend.Select(l => l.Key));
        Assert.All(game.Bars.SelectMany(b => b.Parts), p => Assert.Equal("Game", p.Key));
        Assert.Equal(2, game.Timeline.Count);
        Assert.Equal(["原神", "Steam"], game.Ranking.Select(r => r.Name));

        Assert.Null(StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.App, Day, Day, filter: "Video").Filter);
    }

    [Fact]
    public void Personal_rules_reclassify_history_when_it_is_read()
    {
        UsageSegment[] segments =
        [
            new(At(9, 0), At(10, 0), PresenceState.Browsing, ActivityCategory.Browsing, "bilibili.com", "猫猫合集", "chrome"),
            new(At(10, 0), At(11, 0), PresenceState.Slacking, ActivityCategory.Game, "Sumire", null, "sumire", ActivityCategory.Other),
            new(At(11, 0), At(12, 0), PresenceState.Browsing, ActivityCategory.Browsing, "github.com", "PR", "chrome"),
            new(At(12, 0), At(13, 0), PresenceState.Browsing, ActivityCategory.Browsing, "bilibili.com", "旧记录"),
        ];
        var rules = new UsageReclassifier(
        [
            new UserRuleDto(RuleKind.Domain, "bilibili.com", ActivityCategory.Video, "bilibili.com", DateTimeOffset.UnixEpoch),
        ]);

        var read = segments.Select(rules.Apply).ToList();

        Assert.Equal((PresenceState.Slacking, ActivityCategory.Video), (read[0].State, read[0].Category));
        Assert.Equal((PresenceState.Other, ActivityCategory.Other), (read[1].State, read[1].Category));
        Assert.Same(segments[2], read[2]);
        Assert.Equal(ActivityCategory.Video, read[3].Category);

        var report = UsageAggregator.Build(read, [], Day, Day);
        Assert.Equal(2 * 3600, report.Days.Where(d => d.Category == ActivityCategory.Video).Sum(d => d.Seconds));
    }

    [Fact]
    public void Recorder_keeps_the_open_segment_while_the_browser_address_is_read()
    {
        var writer = new FakeWriter();
        var recorder = new UsageRecorder(writer, Zone);
        var github = new LocalPresence(PresenceState.Browsing, ActivityCategory.Browsing, "github.com", "PR");
        var bilibili = new LocalPresence(PresenceState.Slacking, ActivityCategory.Video, "bilibili.com", "猫猫合集");

        recorder.Record(github, At(9, 0), "chrome", baseCategory: ActivityCategory.Browsing);
        recorder.Record(github with { Detail = "猫猫合集" }, At(9, 0, 5), "chrome", hold: true);
        recorder.Record(bilibili, At(9, 0, 6), "chrome", baseCategory: ActivityCategory.Browsing);
        recorder.Record(bilibili, At(9, 0, 10), "chrome", baseCategory: ActivityCategory.Browsing);
        recorder.Flush();

        Assert.Equal([("github.com", At(9, 0), At(9, 0, 6)), ("bilibili.com", At(9, 0, 6), At(9, 0, 10))], writer.Rows.Select(r => (r.App, r.Start, r.End)));
        Assert.Equal(("chrome", (ActivityCategory?)null, ActivityCategory.Browsing), (writer.Rows[0].Process, writer.Rows[0].BaseCategory, writer.Rows[1].BaseCategory!.Value));

        writer = new FakeWriter();
        recorder = new UsageRecorder(writer, Zone);
        recorder.Record(github, At(9, 0), "chrome");
        for (var s = 1; s <= 8; s++)
        {
            recorder.Record(github with { Detail = "新标签页" }, At(9, 0, s), "chrome", hold: true);
        }

        recorder.Flush();
        Assert.Equal(["PR", "新标签页"], writer.Rows.Select(r => r.Title));
        Assert.Equal(At(9, 0, 6), writer.Rows[1].Start);
    }

    [Fact]
    public void Store_keeps_the_process_and_the_category_without_personal_rules()
    {
        using var store = NewStore();
        store.Add(new UsageSegment(At(9, 0), At(10, 0), PresenceState.Slacking, ActivityCategory.Game, "Sumire", null, "sumire", ActivityCategory.Other));
        store.Add(new UsageSegment(At(10, 0), At(11, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", null));

        var rows = store.Segments(At(0, 0), At(0, 0, dayOffset: 1));

        Assert.Equal([("sumire", ActivityCategory.Other), (null, null)], rows.Select(r => (r.Process, r.BaseCategory)));
        Assert.Equal("sumire", store.ProcessesByApp()["Sumire"]);
    }

    [Fact]
    public void Processes_of_app_names_come_from_all_history_including_daily_totals()
    {
        using var store = NewStore();
        store.Add(new UsageSegment(At(9, 0, dayOffset: -30), At(10, 0, dayOffset: -30), PresenceState.Slacking, ActivityCategory.Game, "Sumire", null, "sumire"));
        store.Add(new UsageSegment(At(9, 0), At(9, 10), PresenceState.Working, ActivityCategory.Work, "Cursor", null, "cursor"));
        store.Compact(Day, At(12, 0));

        var processes = store.ProcessesByApp();

        Assert.Equal("sumire", processes["Sumire"]);
        Assert.Equal("cursor", processes["Cursor"]);
    }

    [Fact]
    public void Every_app_name_of_my_stats_has_a_rule_target()
    {
        var processes = new Dictionary<string, string> { ["Cursor"] = "cursor", ["bilibili.com"] = "chrome", ["Google Chrome"] = "chrome" };
        RuleDto[] rules =
        [
            new(RuleKind.Process, "explorer", ActivityCategory.Work, "文件资源管理器"),
            new(RuleKind.Process, "qqnt", ActivityCategory.Communication, "QQ"),
            new(RuleKind.Process, "qq", ActivityCategory.Communication, "QQ"),
        ];
        var targetOf = UsageRuleTargets.Create(processes, rules);

        Assert.Equal(new StatsRuleTarget("Process", "cursor", "Cursor"), targetOf("Cursor"));
        Assert.Equal(new StatsRuleTarget("Domain", "bilibili.com", "bilibili.com"), targetOf("bilibili.com"));
        Assert.Equal(new StatsRuleTarget("Domain", "space.bilibili.com", "space.bilibili.com"), targetOf("space.bilibili.com"));
        Assert.Null(targetOf("Google Chrome"));
        Assert.Equal(new StatsRuleTarget("Process", "explorer", "文件资源管理器"), targetOf("文件资源管理器"));
        Assert.Equal(new StatsRuleTarget("Process", "qqnt", "QQ"), targetOf("QQ"));
        Assert.Equal(new StatsRuleTarget("Process", "probably stolen", "Probably Stolen"), targetOf("Probably Stolen"));
    }

    [Fact]
    public void A_rule_saved_against_a_guessed_process_still_changes_records_without_a_process()
    {
        var reclassifier = new UsageReclassifier([new UserRuleDto(RuleKind.Process, "probably stolen", ActivityCategory.Work, "Probably Stolen", DateTimeOffset.UtcNow)]);

        Assert.Equal((PresenceState.Working, ActivityCategory.Work), reclassifier.Apply(PresenceState.Slacking, ActivityCategory.Game, "Probably Stolen", null, null, null));
        Assert.Equal((PresenceState.Slacking, ActivityCategory.Game), reclassifier.Apply(PresenceState.Slacking, ActivityCategory.Game, "Probably Stolen", null, "ProbablyStolen", null));
    }

    [Fact]
    public void State_view_tells_video_from_games_and_my_rows_open_the_detail()
    {
        UsageSegment[] segments =
        [
            new(At(9, 0), At(10, 0), PresenceState.Slacking, ActivityCategory.Video, "bilibili.com", "猫"),
            new(At(10, 0), At(11, 0), PresenceState.Slacking, ActivityCategory.Game, "最终幻想XIV", null),
        ];
        var report = UsageAggregator.Build(segments, [], Day, Day);

        var state = StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.Category, Day, Day);
        Assert.Equal(["Video", "Game"], state.Legend.Select(l => l.Key));
        Assert.Equal(["relax", "game"], state.Timeline.Select(t => t.Tone));
        Assert.Equal(["视频", "游戏"], state.Ranking.Select(r => r.Name));
        Assert.All(state.Ranking, r => Assert.Null(r.Detail));

        var apps = StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.App, Day, Day);
        Assert.Equal(new StatsDetailKey("bilibili.com"), apps.Ranking.Single(r => r.Name == "bilibili.com").Detail);
        var titles = StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.Title, Day, Day);
        Assert.Equal(new StatsDetailKey("bilibili.com", "猫"), titles.Ranking.Single(r => r.Name == "猫").Detail);
        Assert.All(StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.App, Day, Day, "Bob").Ranking, r => Assert.Null(r.Detail));
    }

    [Fact]
    public void A_day_runs_from_four_in_the_morning_for_first_and_last_use()
    {
        UsageSegment[] segments =
        [
            new(At(2, 0), At(2, 30), PresenceState.Slacking, ActivityCategory.Game, "原神", null),
            new(At(8, 50), At(9, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", null),
            new(At(12, 0), At(13, 0), PresenceState.Away, ActivityCategory.None, null, null),
            new(At(23, 0), At(0, 0, dayOffset: 1), PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", null),
            new(At(0, 0, dayOffset: 1), At(1, 20, dayOffset: 1), PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", null),
            new(At(5, 0, dayOffset: 1), At(6, 0, dayOffset: 1), PresenceState.Working, ActivityCategory.Work, "Cursor", null),
        ];

        var report = UsageAggregator.Build(segments, [], Day, Day);

        Assert.Equal(new UsageDayBounds(Day, (8 * 60 + 50) * 60, (25 * 60 + 20) * 60), Assert.Single(report.Bounds!));
        Assert.Equal(30 * 60 + 10 * 60 + 60 * 60, UsageAggregator.ActiveSeconds(report));
        Assert.Equal("首次活动 8:50 · 最后活动 次日 1:20", StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.App, Day, Day).Bounds);
    }

    [Fact]
    public void Week_and_month_show_the_weekly_rhythm_and_average_bounds()
    {
        var monday = Day.AddDays(-2);
        UsageSegment[] segments =
        [
            new(At(9, 0, dayOffset: -2), At(10, 0, dayOffset: -2), PresenceState.Working, ActivityCategory.Work, "Cursor", null),
            new(At(9, 0, dayOffset: -2 - 7), At(9, 30, dayOffset: -2 - 7), PresenceState.Working, ActivityCategory.Work, "Cursor", null),
            new(At(21, 0), At(22, 0), PresenceState.Slacking, ActivityCategory.Game, "原神", null),
        ];

        var week = StatsViewBuilder.Build(UsageAggregator.Build(segments, [], monday, Day), StatsScale.Week, StatsRankBy.App, Day, Day);
        Assert.Equal(["周一", "周二", "周三", "周四", "周五", "周六", "周日"], week.Rhythm!.Rows.Select(r => r.Label));
        Assert.Equal(3600, week.Rhythm.Rows[0].Cells[9].Seconds);
        Assert.Equal("周一 9:00–10:00 · 1 小时", week.Rhythm.Rows[0].Cells[9].Tip);
        Assert.Equal(3600, week.Rhythm.Rows[2].Cells[21].Seconds);
        Assert.Null(week.Rhythm.Tone);
        Assert.Equal("平均首次活动 15:00 · 平均最后活动 16:00", week.Bounds);

        var games = StatsViewBuilder.Build(UsageAggregator.Build(segments, [], monday, Day), StatsScale.Week, StatsRankBy.App, Day, Day, filter: "Game");
        Assert.Equal(0, games.Rhythm!.Rows[0].Cells[9].Seconds);
        Assert.Equal("game", games.Rhythm.Tone);
        Assert.Null(games.Bounds);

        var monthRange = StatsRange.Of(StatsScale.Month, Day);
        var month = StatsViewBuilder.Build(UsageAggregator.Build(segments, [], monthRange.From, Day), StatsScale.Month, StatsRankBy.App, Day, Day);
        Assert.Equal(45 * 60, month.Rhythm!.Rows[0].Cells[9].Seconds);
        Assert.Equal("周一 9:00–10:00 · 平均 45 分钟", month.Rhythm.Rows[0].Cells[9].Tip);
    }

    [Fact]
    public void Week_comparison_lists_the_categories_that_changed_most_as_daily_averages()
    {
        var monday = Day.AddDays(-2);
        UsageSegment[] segments =
        [
            new(At(9, 0, dayOffset: -2), At(15, 0, dayOffset: -2), PresenceState.Working, ActivityCategory.Work, "Cursor", null),
            new(At(20, 0), At(20, 3), PresenceState.Communicating, ActivityCategory.Communication, "微信", null),
        ];
        var report = UsageAggregator.Build(segments, [], monday, Day) with
        {
            Previous =
            [
                new(monday.AddDays(-7), -1, PresenceState.Working, ActivityCategory.Work, 7 * 3600),
                new(monday.AddDays(-7), -1, PresenceState.Slacking, ActivityCategory.Video, 7 * 1800),
            ],
        };

        var view = StatsViewBuilder.Build(report, StatsScale.Week, StatsRankBy.App, Day, Day.AddDays(7));

        Assert.Equal([("工作", "work", "−8 分钟"), ("视频", "relax", "−30 分钟")], view.Changes.Select(c => (c.Name, c.Tone, c.Text)).Reverse());
    }

    [Fact]
    public void Room_members_do_not_get_the_rhythm_or_first_and_last_use()
    {
        var report = UsageAggregator.Build([new UsageSegment(At(9, 0), At(10, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", null)], [], Day, Day)
            with { Previous = [new(Day, -1, PresenceState.Working, ActivityCategory.Work, 1800)] };

        var shown = StatsFilter.Apply(report, new PrivacyProfile());

        Assert.Null(shown.WeekHours);
        Assert.Null(shown.Bounds);
        Assert.NotNull(shown.Previous);
        Assert.Null(StatsViewBuilder.Build(shown, StatsScale.Week, StatsRankBy.App, Day, Day, "Bob").Rhythm);
    }

    [Fact]
    public void App_detail_counts_sessions_hours_and_titles_of_one_app()
    {
        UsageSegment[] segments =
        [
            new(At(9, 0), At(9, 20), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"),
            new(At(9, 20, 30), At(9, 40), PresenceState.Working, ActivityCategory.Work, "Cursor", "b.md"),
            new(At(9, 40), At(9, 50), PresenceState.Communicating, ActivityCategory.Communication, "微信", "群"),
            new(At(9, 50), At(10, 30), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"),
            new(At(11, 0), At(11, 30), PresenceState.Away, ActivityCategory.None, "Cursor", null),
            new(At(9, 0, dayOffset: -1), At(9, 30, dayOffset: -1), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md"),
        ];

        var detail = UsageAppDetail.Build(segments, [], "Cursor", null, Day, Day);

        Assert.Equal((PresenceState.Working, ActivityCategory.Work), (detail.State, detail.Category));
        Assert.Equal(80 * 60 - 30, detail.Seconds);
        Assert.Equal(2, detail.Sessions);
        Assert.Equal(40 * 60, detail.Longest);
        Assert.Equal([("a.md", 60L * 60), ("b.md", 19 * 60 + 30)], detail.Titles);
        Assert.Equal(50 * 60 - 30, detail.Hours[9]);

        var view = StatsAppViewBuilder.Build(detail, "Cursor", null, StatsScale.Day, Day, Day, new StatsRuleTarget("Process", "cursor", "Cursor"));
        Assert.Equal(("Cursor", "工作"), (view.Name, view.Sub));
        Assert.Equal(new StatsRuleTarget("Process", "cursor", "Cursor", "Work"), view.Rule);
        Assert.Equal([("使用次数", "2 次"), ("平均每次", "39 分钟"), ("最长一次", "40 分钟"), ("最常用时段", "9:00–10:00")], view.Facts.Select(f => (f.Label, f.Value)));
        Assert.Equal(24, view.Bars.Count);
        Assert.Empty(view.Hours);
        Assert.Equal(["a.md", "b.md"], view.Titles.Select(t => t.Name));

        var title = StatsAppViewBuilder.Build(UsageAppDetail.Build(segments, [], "Cursor", "b.md", Day, Day), "Cursor", "b.md", StatsScale.Week, Day, Day, null);
        Assert.Equal(("b.md", "Cursor · 工作"), (title.Name, title.Sub));
        Assert.Equal(7, title.Bars.Count);
        Assert.Equal(24, title.Hours.Count);
        Assert.Empty(title.Titles);
    }

    private static readonly TimeSpan UsEast = TimeSpan.FromHours(-5);

    /// <summary>Local time in UTC−5 on <see cref="Day"/>.</summary>
    private static DateTimeOffset East(int hour, int minute, int dayOffset = 0) =>
        new(Day.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute)), UsEast);

    [Fact]
    public void Segments_count_on_the_day_and_hour_they_were_recorded_at_whoever_reads_them()
    {
        // 20:00 to 21:00 in UTC−5 on the 11th is 9:00 to 10:00 on the 12th in UTC+8.
        var report = UsageAggregator.Build([new UsageSegment(East(20, 0), East(21, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", null)], [], Day, Day);

        Assert.Equal(3600, UsageAggregator.ActiveSeconds(report));
        Assert.Equal(20, Assert.Single(report.Hours).Hour);
        Assert.Equal([-300], report.Offsets);
        Assert.Empty(UsageAggregator.Build([new UsageSegment(East(20, 0), East(21, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", null)], [], Day.AddDays(1), Day.AddDays(1)).Days);
    }

    [Fact]
    public void Store_keeps_the_offset_and_groups_days_by_it()
    {
        using var store = NewStore();
        store.Add(new UsageSegment(East(20, 0), East(21, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", null));
        store.Add(new UsageSegment(At(9, 0), At(9, 30), PresenceState.Working, ActivityCategory.Work, "Cursor", null));

        var window = UsageAggregator.Window(Day, Day);
        Assert.Equal([UsEast, TimeSpan.FromHours(8)], store.Segments(window.From, window.To).Select(s => s.Start.Offset).Order());
        Assert.Equal([Day], store.Dates());
        Assert.Equal(UsEast, Assert.Single(store.ExportDay(Day).Segments, s => s.Start.Offset == UsEast).Start.Offset);
        Assert.Equal(2, store.ExportDay(Day).Segments.Count);
        Assert.Empty(store.ExportDay(Day.AddDays(1)).Segments);
    }

    [Fact]
    public void Recorder_records_times_in_the_offset_of_its_time_zone()
    {
        var writer = new FakeWriter();
        var recorder = new UsageRecorder(writer, TimeZoneInfo.CreateCustomTimeZone("UTC-5", UsEast, "UTC-5", "UTC-5"));
        recorder.Record(Work("a.md"), At(9, 0).ToUniversalTime());
        recorder.Record(Work("a.md"), At(9, 0, 5).ToUniversalTime());
        recorder.Flush();

        var row = Assert.Single(writer.Rows);
        Assert.Equal((UsEast, At(9, 0)), (row.Start.Offset, row.Start));
    }

    [Fact]
    public void A_member_view_says_whose_local_time_it_is_in()
    {
        var report = UsageAggregator.Build([new UsageSegment(East(20, 0), East(21, 0), PresenceState.Working, ActivityCategory.Work, "Cursor", null)], [], Day, Day)
            with { SharedDays = 7, Today = Day, Offset = -300 };

        var view = StatsViewBuilder.Build(report, StatsScale.Day, StatsRankBy.App, Day, Day, "Bob", myOffset: 480);
        Assert.Equal("Bob 公开了最近 7 天的统计。时间按 Bob 的当地时间显示（UTC−5，比你晚 13 小时）。", view.Note);
        Assert.Equal("20:00–21:00 · 1 小时\n你的时间 11 月 12 日 9:00–10:00", view.Bars[20].Tip);
        Assert.Equal("7:00–8:00 · 0 分钟\n你的时间 11 月 11 日 20:00–21:00", view.Bars[7].Tip);
        Assert.Equal("20:00–21:00 工作 · 1 小时\n你的时间 11 月 12 日 9:00–10:00", Assert.Single(view.Timeline).Tip);
        var week = StatsViewBuilder.Build(report, StatsScale.Week, StatsRankBy.App, Day, Day, "Bob", myOffset: 480);
        Assert.EndsWith("\n你的时间 11 月 11 日 13:00 – 11 月 12 日 13:00", week.Bars[2].Tip);

        var moved = StatsViewBuilder.Build(report with { Offset = 480 }, StatsScale.Day, StatsRankBy.App, Day, Day, "Bob", myOffset: 480);
        Assert.Contains("时间按 Bob 记录时的当地时间显示（UTC−5，比你晚 13 小时）。", moved.Note);

        var india = StatsViewBuilder.Build(report with { Offsets = [330], Offset = 330 }, StatsScale.Day, StatsRankBy.App, Day, Day, "Bob", myOffset: 480);
        Assert.Contains("（UTC+5:30，比你晚 2 小时 30 分钟）", india.Note);

        var same = StatsViewBuilder.Build(report with { Offsets = [480], Offset = 480 }, StatsScale.Day, StatsRankBy.App, Day, Day, "Bob", myOffset: 480);
        Assert.Contains("（UTC+8，与你相同）", same.Note);

        var mine = StatsViewBuilder.Build(report with { Offsets = [-300, 480] }, StatsScale.Week, StatsRankBy.App, Day, Day, myOffset: 480);
        Assert.Equal("部分记录产生于其他时区，按记录时的当地时间显示（UTC−5）。", mine.Note);
        Assert.Null(StatsViewBuilder.Build(report with { Offsets = [480] }, StatsScale.Week, StatsRankBy.App, Day, Day, myOffset: 480).Note);
    }

    private SqliteUsageStore NewStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deskpal-usage-{Guid.NewGuid():N}.db");
        _files.Add(path);
        return new SqliteUsageStore(path, Guid.NewGuid(), Zone);
    }

    public void Dispose()
    {
        foreach (var file in _files)
        {
            foreach (var path in new[] { file, file + "-wal", file + "-shm" })
            {
                File.Delete(path);
            }
        }
    }

    private sealed class FakeWriter : IUsageWriter
    {
        public List<UsageSegment> Rows { get; } = [];

        public long Add(UsageSegment segment)
        {
            Rows.Add(segment);
            return Rows.Count;
        }

        public void UpdateEnd(long id, DateTimeOffset end) => Rows[(int)id - 1] = Rows[(int)id - 1] with { End = end };
    }
}