using System.Text.Json;
using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Ui;
using DeskPal.Shared;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Ui;

public class PanelStateBuilderTests
{
    [Fact]
    public void Member_cards_use_three_lines_and_skip_me()
    {
        var state = PanelStateBuilder.Build(PanelSamples.Default());

        Assert.Equal(["Bob", "Carol"], state.Members.Select(m => m.Name));
        var bob = state.Members[0];
        Assert.True(bob.Online);
        Assert.Equal("在看视频", bob.View.Headline);
        Assert.Equal("哔哩哔哩", bob.View.App);
        Assert.EndsWith("…", bob.View.Detail);
        Assert.Equal("已持续 12 分钟", bob.Meta);

        var carol = state.Members[1];
        Assert.False(carol.Online);
        Assert.Equal("离线", carol.View.Headline);
        Assert.Equal("还没有收到状态", carol.Meta);
        Assert.Null(state.EmptyHint);
    }

    [Fact]
    public void My_status_shows_source_pending_sync_and_what_the_room_sees()
    {
        var me = PanelStateBuilder.Build(PanelSamples.Default()).Me;

        Assert.Equal(new PresenceView("工作中", "Cursor", "plan.md - idea1 - Cursor"), me.View);
        Assert.Equal("内置规则，稳定后同步", me.Source);
        Assert.Equal("该房间成员看到：正在使用 Cursor", me.Visible);
    }

    [Fact]
    public void Rooms_carry_ownership_and_privacy_lists_selectable_categories_only()
    {
        var state = PanelStateBuilder.Build(PanelSamples.Default());

        Assert.Equal([true, false], state.Rooms.Select(r => r.IsOwner));
        Assert.Equal(PanelSamples.MyRoom, state.SelectedRoomId);
        Assert.NotNull(state.Privacy);
        Assert.DoesNotContain(state.Privacy!.Rows, r => r.Category is ActivityCategory.Meeting or ActivityCategory.None);
        Assert.Equal(PrivacyLevel.StateOnly, state.Privacy.Rows.Single(r => r.Category == ActivityCategory.Communication).Level);
        Assert.Equal(3, state.Privacy.Levels.Count);
        Assert.DoesNotContain(state.Correction.Options, o => o.Value == ActivityCategory.Meeting);
        Assert.Equal(ActivityCategory.Work, state.Correction.Current);
    }

    [Fact]
    public void Without_room_shows_hint_and_no_privacy()
    {
        var state = PanelStateBuilder.Build(PanelSamples.NoRoom());

        Assert.Empty(state.Members);
        Assert.Null(state.SelectedRoomId);
        Assert.Null(state.Privacy);
        Assert.StartsWith("还没有房间", state.EmptyHint);
        Assert.Null(state.Me.Visible);
        Assert.Equal("内置规则", state.Me.Source);
    }

    [Fact]
    public void Alone_in_room_suggests_an_invite()
    {
        var input = PanelSamples.Default() with { SelectedRoom = PanelSamples.OwnedRoom with { Members = [PanelSamples.OwnedRoom.Members[0]] } };

        Assert.StartsWith("房间里还没有其他成员", PanelStateBuilder.Build(input).EmptyHint);
    }

    [Fact]
    public void Guest_while_reconnecting_and_paused()
    {
        var state = PanelStateBuilder.Build(PanelSamples.Guest());

        Assert.False(state.Connection.Connected);
        Assert.Equal("连接中断，正在重连…", state.Connection.Text);
        Assert.Equal(PanelSamples.TeamRoom, state.SelectedRoomId);
        var dave = Assert.Single(state.Members);
        Assert.True(dave.IsOwner);
        Assert.False(dave.Online);
        Assert.Equal("", dave.Meta);
        Assert.Equal("暂停分享", state.Me.View.Headline);
        Assert.True(state.Me.Paused);
        Assert.Equal("午休中", state.Me.ManualText);
        Assert.Null(state.Correction.Target);
    }

    [Fact]
    public void Before_first_recognition_shows_placeholder()
    {
        var state = PanelStateBuilder.Build(PanelSamples.Default() with { Local = null });

        Assert.Equal("正在识别…", state.Me.View.Headline);
        Assert.Null(state.Me.Source);
    }

    [Fact]
    public void Settings_show_the_update_activity_while_a_check_is_running()
    {
        var input = PanelSamples.Default();
        input = input with { Settings = input.Settings with { UpdateActivity = "正在下载新版本 0.2.0（40%）" } };

        Assert.Equal("正在下载新版本 0.2.0（40%）", PanelStateBuilder.Build(input).Settings.UpdateActivity);
    }

    [Fact]
    public void Browser_shows_the_page_without_a_guess_label()
    {
        var local = new LocalPresence(PresenceState.Browsing, ActivityCategory.Browsing, "bilibili.com", "猫猫合集", null);
        var input = PanelSamples.Default() with
        {
            Local = local,
            Classification = new ClassificationResult(ActivityCategory.Browsing, "bilibili.com", RuleLayer.Browser),
            Committed = new CommittedPresence(local, PanelSamples.Now.AddMinutes(-1)),
        };

        var me = PanelStateBuilder.Build(input).Me;

        Assert.Equal(new PresenceView("正在浏览网页", "bilibili.com", "猫猫合集"), me.View);
        Assert.Null(me.Source);
        Assert.Equal("该房间成员看到：正在浏览网页 · bilibili.com", me.Visible);
    }

    [Fact]
    public void Serializes_with_camel_case_and_enum_names()
    {
        var json = JsonSerializer.Serialize(PanelStateBuilder.Build(PanelSamples.Default()), DeskPalJson.Options);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal("00000000-0000-0000-0000-0000000000a1", root.GetProperty("selectedRoomId").GetString());
        Assert.Equal("在看视频", root.GetProperty("members")[0].GetProperty("view").GetProperty("headline").GetString());
        Assert.Equal("StateOnly", root.GetProperty("privacy").GetProperty("rows")[0].GetProperty("level").GetString());
        Assert.Equal("Work", root.GetProperty("correction").GetProperty("current").GetString());
    }
}
