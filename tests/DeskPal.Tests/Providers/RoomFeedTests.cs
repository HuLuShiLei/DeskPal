using System.Text.Json;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Providers;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;

namespace DeskPal.Tests.Providers;

public class RoomFeedTests
{
    private static readonly Guid RoomId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Me = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Bob = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Carol = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Dave = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Since = new(2026, 11, 27, 8, 30, 0, TimeSpan.FromHours(8));

    private static readonly string[] MemberFields = ["id", "name", "owner", "me", "blocked", "state", "category", "since", "client"];
    private static readonly string[] BlockedFields = ["id", "name", "owner", "me", "blocked"];

    private static RoomDto Room(params Guid[] blocked) => new(
        RoomId,
        "摸鱼小组",
        Bob,
        20,
        Since,
        [
            new RoomMemberDto(Bob, "张三", RoomRole.Owner, Since),
            new RoomMemberDto(Me, "我", RoomRole.Member, Since),
            new RoomMemberDto(Carol, "李四", RoomRole.Member, Since),
            new RoomMemberDto(Dave, "王五", RoomRole.Member, Since),
        ],
        blocked);

    private static PresenceSnapshot Sent(ActivityCategory category, PrivacyLevel level, string app, string detail, string client = "windows") =>
        PrivacyFilter.Apply(
            new CommittedPresence(new LocalPresence(PresenceState.Slacking, category, app, detail), Since),
            new PrivacyProfile { Levels = new() { [category] = level } }) with { Client = client };

    private static JsonElement Member(JsonElement root, Guid id) =>
        root.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id);

    private static List<string> Keys(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToList();

    [Fact]
    public void Members_only_carry_the_listed_fields_and_no_titles()
    {
        var bob = new PresenceSnapshot
        {
            State = PresenceState.Slacking,
            Category = ActivityCategory.Game,
            AppDisplayName = "Steam",
            Detail = "机密.docx",
            CustomText = "秘密",
            Since = Since,
            Client = "windows",
        };
        var presence = new Dictionary<Guid, PresenceSnapshot> { [Bob] = bob };

        var line = RoomFeed.Build(Room(Dave), Me, id => presence.GetValueOrDefault(id), mine: null);

        Assert.DoesNotContain("机密.docx", line);
        Assert.DoesNotContain("秘密", line);
        Assert.DoesNotContain("Steam", line);
        Assert.DoesNotContain("detail", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customText", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"app\"", line);
        Assert.DoesNotContain('\n', line);

        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.Equal(["v", "type", "room", "members"], Keys(root));
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        Assert.Equal("room", root.GetProperty("type").GetString());
        Assert.Equal(["id", "name"], Keys(root.GetProperty("room")));
        Assert.Equal("摸鱼小组", root.GetProperty("room").GetProperty("name").GetString());
        Assert.Equal(4, root.GetProperty("members").GetArrayLength());

        var owner = Member(root, Bob);
        Assert.Equal(MemberFields, Keys(owner));
        Assert.Equal(("张三", true, false, false), (owner.GetProperty("name").GetString(), owner.GetProperty("owner").GetBoolean(), owner.GetProperty("me").GetBoolean(), owner.GetProperty("blocked").GetBoolean()));
        Assert.Equal(("Slacking", "Game", "2026-11-27T00:30:00Z", "windows"),
            (owner.GetProperty("state").GetString(), owner.GetProperty("category").GetString(), owner.GetProperty("since").GetString(), owner.GetProperty("client").GetString()));

        // Nothing received yet: offline, without a time.
        var carol = Member(root, Carol);
        Assert.Equal(MemberFields, Keys(carol));
        Assert.Equal(("Offline", "None", JsonValueKind.Null, JsonValueKind.Null),
            (carol.GetProperty("state").GetString(), carol.GetProperty("category").GetString(), carol.GetProperty("since").ValueKind, carol.GetProperty("client").ValueKind));

        // Blocked: like the panel, listed without any state.
        var dave = Member(root, Dave);
        Assert.Equal(BlockedFields, Keys(dave));
        Assert.True(dave.GetProperty("blocked").GetBoolean());
    }

    [Fact]
    public void Titles_stay_out_even_when_the_member_shows_details()
    {
        var bob = Sent(ActivityCategory.Video, PrivacyLevel.Detail, "bilibili.com", "机密的视频标题 - 哔哩哔哩");
        Assert.Equal("机密的视频标题 - 哔哩哔哩", bob.Detail);
        var presence = new Dictionary<Guid, PresenceSnapshot> { [Bob] = bob };

        var line = RoomFeed.Build(Room(), Me, id => presence.GetValueOrDefault(id), mine: null);

        Assert.DoesNotContain("机密", line);
        Assert.DoesNotContain("bilibili", line);
        using var json = JsonDocument.Parse(line);
        var member = Member(json.RootElement, Bob);
        Assert.Equal(MemberFields, Keys(member));
        Assert.Equal("Video", member.GetProperty("category").GetString());
    }

    [Fact]
    public void State_only_still_gives_the_category_like_the_panel()
    {
        var bob = Sent(ActivityCategory.Game, PrivacyLevel.StateOnly, "Steam", "存档 3");
        var presence = new Dictionary<Guid, PresenceSnapshot> { [Bob] = bob };

        var line = RoomFeed.Build(Room(), Me, id => presence.GetValueOrDefault(id), mine: null);

        using var json = JsonDocument.Parse(line);
        var member = Member(json.RootElement, Bob);
        Assert.Equal(("Slacking", "Game"), (member.GetProperty("state").GetString(), member.GetProperty("category").GetString()));
        Assert.DoesNotContain("Steam", line);
        Assert.DoesNotContain("存档", line);
    }

    [Fact]
    public void Me_is_what_the_room_sees_of_me()
    {
        var mine = Sent(ActivityCategory.Work, PrivacyLevel.Detail, "Visual Studio Code", "我的项目.cs", "linux");
        var stale = new PresenceSnapshot { State = PresenceState.Slacking, Category = ActivityCategory.Game, Since = Since };

        var line = RoomFeed.Build(Room(), Me, id => id == Me ? stale : null, mine);

        Assert.DoesNotContain("我的项目", line);
        Assert.DoesNotContain("Visual Studio Code", line);
        using var json = JsonDocument.Parse(line);
        var me = Member(json.RootElement, Me);
        Assert.Equal(MemberFields, Keys(me));
        Assert.True(me.GetProperty("me").GetBoolean());
        Assert.Equal(("Slacking", "Work", "linux"), (me.GetProperty("state").GetString(), me.GetProperty("category").GetString(), me.GetProperty("client").GetString()));
    }

    [Fact]
    public void Offline_members_have_no_client()
    {
        var presence = new Dictionary<Guid, PresenceSnapshot> { [Bob] = PresenceSnapshot.Offline(Since) with { Client = "mac" } };

        using var json = JsonDocument.Parse(RoomFeed.Build(Room(), Me, id => presence.GetValueOrDefault(id), mine: null));

        var bob = Member(json.RootElement, Bob);
        Assert.Equal(("Offline", "2026-11-27T00:30:00Z", JsonValueKind.Null),
            (bob.GetProperty("state").GetString(), bob.GetProperty("since").GetString(), bob.GetProperty("client").ValueKind));
    }

    [Theory]
    [InlineData("windows", "windows")]
    [InlineData("mac", "mac")]
    [InlineData("linux", "linux")]
    [InlineData("机密.docx - Word", null)]
    [InlineData("Windows", null)]
    [InlineData("", null)]
    public void Client_is_only_a_known_kind(string sent, string? expected)
    {
        var presence = new Dictionary<Guid, PresenceSnapshot>
        {
            [Bob] = new() { State = PresenceState.Working, Category = ActivityCategory.Work, Since = Since, Client = sent },
        };

        var line = RoomFeed.Build(Room(), Me, id => presence.GetValueOrDefault(id), mine: null);

        using var json = JsonDocument.Parse(line);
        var client = Member(json.RootElement, Bob).GetProperty("client");
        Assert.Equal(expected, client.ValueKind == JsonValueKind.Null ? null : client.GetString());
        Assert.DoesNotContain("机密", line);
    }

    [Fact]
    public void Me_before_anything_was_sent_is_offline()
    {
        using var json = JsonDocument.Parse(RoomFeed.Build(Room(), Me, _ => null, mine: null));

        var me = Member(json.RootElement, Me);
        Assert.Equal(("Offline", JsonValueKind.Null), (me.GetProperty("state").GetString(), me.GetProperty("since").ValueKind));
    }

    [Fact]
    public void Outside_any_room_the_list_is_empty()
    {
        Assert.Equal("""{"v":1,"type":"room","room":null,"members":[]}""", RoomFeed.Build(null, Me, _ => null, mine: null));
    }
}
