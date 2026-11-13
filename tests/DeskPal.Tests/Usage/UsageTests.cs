using System.Security.Cryptography;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Core.Usage;
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

        var report = UsageAggregator.Build(segments, [], Day, Day, Zone);

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
        var report = UsageAggregator.Build(store.Segments(At(0, 0, dayOffset: -1), At(0, 0)), store.Rollups(yesterday, yesterday), yesterday, yesterday, Zone);
        Assert.Equal(3600, UsageAggregator.ActiveSeconds(report));
        Assert.Empty(report.Timeline);
        Assert.Equal("a.md", Assert.Single(report.Titles).Title);
        Assert.Single(store.Segments(At(0, 0), At(0, 0, dayOffset: 1)));
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

        var report = UsageAggregator.Build(target.Segments(At(0, 0, dayOffset: -100), At(0, 0, dayOffset: 1)), target.Rollups(Day.AddDays(-100), Day), Day.AddDays(-100), Day, Zone);
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

        var shown = StatsFilter.Apply(UsageAggregator.Build(segments, [], Day, Day, Zone), profile);

        Assert.DoesNotContain(shown.Days, d => d.Category == ActivityCategory.Work);
        Assert.Contains(shown.Days, d => d is { State: PresenceState.Working, Category: ActivityCategory.None, Seconds: 3600 });
        Assert.DoesNotContain(shown.Apps.Concat(shown.Titles), i => i.App == "Cursor" || i.Title == "secret.md");
        Assert.Contains(shown.Apps, a => a.App == "哔哩哔哩");
        Assert.DoesNotContain(shown.Titles, t => t.Title == "猫");
        Assert.Contains(shown.Titles, t => t.Title == "深渊");
    }

    [Fact]
    public void Only_the_requester_key_opens_the_response()
    {
        var report = UsageAggregator.Build([new UsageSegment(At(9, 0), At(9, 30), PresenceState.Working, ActivityCategory.Work, "Cursor", "a.md")], [], Day, Day, Zone);
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
        var report = UsageAggregator.Build(segments, [], Day, Day, Zone) with { PreviousSeconds = 3600 };

        var view = StatsViewBuilder.Build(report, StatsScale.Day, StatsGranularity.Category, Day, Day);

        Assert.Equal("11 月 11 日 星期三（今天）", view.Title);
        Assert.Equal(("使用时长", "1 小时 30 分钟"), (view.TotalLabel, view.Total));
        Assert.Equal("昨天全天 1 小时", view.Compare);
        Assert.Equal("比前一天多 30 分钟", StatsViewBuilder.Build(report, StatsScale.Day, StatsGranularity.Category, Day, Day.AddDays(1)).Compare);
        Assert.Null(StatsViewBuilder.Build(report with { PreviousSeconds = 0 }, StatsScale.Day, StatsGranularity.Category, Day, Day).Compare);
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
        var report = UsageAggregator.Build(segments, [], monday, Day, Zone);

        var view = StatsViewBuilder.Build(report, StatsScale.Week, StatsGranularity.State, Day, Day);

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
        var report = StatsFilter.Apply(UsageAggregator.Build(segments, [], Day, Day, Zone), profile) with { SharedDays = 1 };

        var view = StatsViewBuilder.Build(report, StatsScale.Day, StatsGranularity.Title, Day, Day, "Bob");

        Assert.Equal("Bob 公开了最近 1 天的统计。部分类别仅公开了状态。部分类别未公开标题，仅显示应用名。", view.Note);
        Assert.False(view.CanPrev);
        Assert.Contains(view.Ranking, r => r is { Name: "工作（未公开类别）", Hidden: true });
        Assert.Contains(view.Ranking, r => r.Name == "哔哩哔哩");
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