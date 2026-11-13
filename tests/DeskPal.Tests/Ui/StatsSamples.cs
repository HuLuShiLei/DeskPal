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

    public static IReadOnlyList<UsageSegment> Segments()
    {
        var list = new List<UsageSegment>();
        for (var i = 0; i < 31; i++)
        {
            var day = Today.AddDays(-i);
            var shift = i % 3 * 10;
            void Add(int fromMinute, int toMinute, PresenceState state, ActivityCategory category, string? app, string? title)
            {
                var start = At(day, fromMinute);
                var end = At(day, toMinute);
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

    /// <summary>Every page of the sample stats: mine as "Day-State" and so on, Bob's as "member-Day-State".</summary>
    public static IReadOnlyDictionary<string, StatsView> Views()
    {
        var segments = Segments();
        var views = new Dictionary<string, StatsView>();
        foreach (var scale in Enum.GetValues<StatsScale>())
        {
            var range = StatsRange.Of(scale, Today);
            var previous = StatsRange.Previous(scale, Today);
            var to = range.To > Today ? Today : range.To;
            var mine = UsageAggregator.Build(segments, [], range.From, to, Zone) with
            {
                PreviousSeconds = UsageAggregator.ActiveSeconds(UsageAggregator.Build(segments, [], previous.From, previous.To, Zone)),
            };
            var first = Today.AddDays(1 - BobPrivacy.StatsDays);
            var from = range.From < first ? first : range.From;
            var bob = StatsFilter.Apply(UsageAggregator.Build(segments, [], from, to, Zone), BobPrivacy) with
            {
                SharedDays = BobPrivacy.StatsDays,
                PreviousSeconds = scale == StatsScale.Day ? UsageAggregator.ActiveSeconds(UsageAggregator.Build(segments, [], previous.From, previous.To, Zone)) : null,
            };
            foreach (var granularity in Enum.GetValues<StatsGranularity>())
            {
                views[$"{scale}-{granularity}"] = StatsViewBuilder.Build(mine, scale, granularity, Today, Today);
                views[$"member-{scale}-{granularity}"] = StatsViewBuilder.Build(bob, scale, granularity, Today, Today, "Bob");
            }
        }

        return views;
    }

    private static DateTimeOffset At(DateOnly day, int minute) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).AddMinutes(minute);
}