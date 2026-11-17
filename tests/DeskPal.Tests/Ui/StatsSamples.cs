using System.Text.Json;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Core.Usage;
using DeskPal.Shared;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Ui;

/// <summary>A month of made-up usage for the stats page. Also written to web/src/mock/fixtures/stats.json.</summary>
public static class StatsSamples
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    public static readonly DateOnly Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(PanelSamples.Now, Zone).DateTime);

    /// <summary>How Bob shares in the sample room: work and chat only as the state, games with titles.</summary>
    public static readonly PrivacyProfile BobPrivacy = new()
    {
        Levels =
        {
            [ActivityCategory.Work] = PrivacyLevel.StateOnly,
            [ActivityCategory.Communication] = PrivacyLevel.StateOnly,
            [ActivityCategory.Video] = PrivacyLevel.Category,
            [ActivityCategory.Game] = PrivacyLevel.Detail,
            [ActivityCategory.Browsing] = PrivacyLevel.Category,
            [ActivityCategory.Other] = PrivacyLevel.Category,
        },
        ShareStats = true,
        StatsDays = 7,
    };

    /// <summary>Bob records in UTC−5 at the same local times; I am in UTC+8.</summary>
    public const int BobOffset = -5 * 60;

    public const int MyOffset = 8 * 60;

    /// <summary>Bob's date at <see cref="PanelSamples.Now"/>, where it is 2:00 in the morning: his today is still empty.</summary>
    public static readonly DateOnly BobToday = DateOnly.FromDateTime(PanelSamples.Now.ToOffset(TimeSpan.FromMinutes(BobOffset)).DateTime);

    /// <param name="offset">UTC offset the sample is recorded at, in minutes.</param>
    public static IReadOnlyList<UsageSegment> Segments(int offset = MyOffset)
    {
        var list = new List<UsageSegment>();
        for (var i = 0; i < 31; i++)
        {
            var day = Today.AddDays(-i);
            var shift = i % 3 * 10;
            void Add(int fromMinute, int toMinute, PresenceState state, ActivityCategory category, string? app, string? title)
            {
                var start = At(day, fromMinute, offset);
                var end = At(day, toMinute, offset);
                if (i == 0 && end > PanelSamples.Now)
                {
                    end = PanelSamples.Now;
                }

                if (end > start)
                {
                    list.Add(new UsageSegment(start, end, state, category, app, title));
                }
            }

            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                Add(10 * 60, 12 * 60 + shift, PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", PanelSamples.LongTitle);
                Add(14 * 60, 17 * 60, PresenceState.Slacking, ActivityCategory.Game, "原神", "深境螺旋");
                Add(20 * 60, 21 * 60 + shift, PresenceState.Browsing, ActivityCategory.Browsing, "知乎", "如何评价这个周末");
                continue;
            }

            Add(9 * 60 + 10, 10 * 60 + 40, PresenceState.Working, ActivityCategory.Work, "Cursor", "plan.md - idea1 - Cursor");
            Add(10 * 60 + 40, 10 * 60 + 55 + shift, PresenceState.Communicating, ActivityCategory.Communication, "微信", "项目群");
            Add(10 * 60 + 55 + shift, 12 * 60, PresenceState.Working, ActivityCategory.Work, "Cursor", "StatsPage.tsx - idea1 - Cursor");
            Add(12 * 60, 12 * 60 + 40, PresenceState.Away, ActivityCategory.None, null, null);
            Add(12 * 60 + 40, 13 * 60 + 20, PresenceState.Slacking, ActivityCategory.Video, "哔哩哔哩", PanelSamples.LongTitle);
            Add(13 * 60 + 30, 15 * 60 + 30, PresenceState.Working, ActivityCategory.Work, "Visual Studio Code", "architecture.md - docs");
            Add(15 * 60 + 30, 15 * 60 + 50, PresenceState.Browsing, ActivityCategory.Browsing, "知乎", "如何评价手写 SVG 图表");
            Add(15 * 60 + 50, 18 * 60 - shift, PresenceState.Working, ActivityCategory.Work, "Cursor", "plan.md - idea1 - Cursor");
            Add(18 * 60, 19 * 60, PresenceState.Locked, ActivityCategory.None, null, null);
            if (i % 2 == 0)
            {
                Add(20 * 60, 21 * 60 + 30, PresenceState.Slacking, ActivityCategory.Game, "原神", "深境螺旋");
            }

            Add(21 * 60 + 30, 22 * 60, PresenceState.Other, ActivityCategory.Other, "XMind", "使用统计方案");
        }

        return list;
    }

    /// <summary>Every page of the sample stats: mine as "Day-App" and so on, Bob's as "member-Day-App", my week of games alone as "Week-App-Game".</summary>
    public static IReadOnlyDictionary<string, StatsView> Views()
    {
        var segments = Segments();
        var bobSegments = Segments(BobOffset);
        var views = new Dictionary<string, StatsView>();
        foreach (var scale in Enum.GetValues<StatsScale>())
        {
            var range = StatsRange.Of(scale, Today);
            var previous = StatsRange.Previous(scale, Today);
            var to = range.To > Today ? Today : range.To;
            var previousTotals = UsageAggregator.Totals(UsageAggregator.Build(segments, [], previous.From, previous.To));
            var mine = UsageAggregator.Build(segments, [], range.From, to) with { Previous = previousTotals };
            var bobRange = StatsRange.Of(scale, BobToday);
            var bobPrevious = StatsRange.Previous(scale, BobToday);
            var first = BobToday.AddDays(1 - BobPrivacy.StatsDays);
            var from = bobRange.From < first ? first : bobRange.From;
            var bobTo = bobRange.To > BobToday ? BobToday : bobRange.To;
            var bob = StatsFilter.Apply(UsageAggregator.Build(bobSegments, [], from, bobTo), BobPrivacy) with
            {
                SharedDays = BobPrivacy.StatsDays,
                Today = BobToday,
                Offset = BobOffset,
                Previous = scale == StatsScale.Day ? UsageAggregator.Totals(UsageAggregator.Build(bobSegments, [], bobPrevious.From, bobPrevious.To)) : null,
            };
            foreach (var rankBy in Enum.GetValues<StatsRankBy>())
            {
                views[$"{scale}-{rankBy}"] = StatsViewBuilder.Build(mine, scale, rankBy, Today, Today, myOffset: MyOffset);
                views[$"member-{scale}-{rankBy}"] = StatsViewBuilder.Build(bob, scale, rankBy, BobToday, BobToday, "Bob", myOffset: MyOffset);
            }

            if (scale == StatsScale.Week)
            {
                views["Week-App-Game"] = StatsViewBuilder.Build(mine, scale, StatsRankBy.App, Today, Today, filter: "Game");
            }
        }

        return views;
    }

    /// <summary>Detail pages of every sample app in the day, week and month of today, as "Day-Cursor". Written to web/src/mock/fixtures/stats-apps.json.</summary>
    public static IReadOnlyDictionary<string, StatsAppView> AppViews()
    {
        var segments = Segments();
        var views = new Dictionary<string, StatsAppView>();
        foreach (var scale in Enum.GetValues<StatsScale>())
        {
            var range = StatsRange.Of(scale, Today);
            var to = range.To > Today ? Today : range.To;
            foreach (var app in Targets.Keys)
            {
                var detail = UsageAppDetail.Build(segments, [], app, null, range.From, to);
                views[$"{scale}-{app}"] = StatsAppViewBuilder.Build(detail, app, null, scale, Today, Today, Target(app));
            }
        }

        return views;
    }

    private static readonly Dictionary<string, StatsRuleTarget> Targets = new()
    {
        ["哔哩哔哩"] = new("Process", "bilibili", "哔哩哔哩"),
        ["原神"] = new("Process", "yuanshen", "原神"),
        ["知乎"] = new("Domain", "zhihu.com", "知乎"),
        ["Cursor"] = new("Process", "cursor", "Cursor"),
        ["微信"] = new("Process", "weixin", "微信"),
        ["Visual Studio Code"] = new("Process", "code", "Visual Studio Code"),
        ["XMind"] = new("Process", "xmind", "XMind"),
    };

    private static StatsRuleTarget? Target(string app) => Targets.GetValueOrDefault(app);

    private static DateTimeOffset At(DateOnly day, int minute, int offset) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(offset)).AddMinutes(minute);
}