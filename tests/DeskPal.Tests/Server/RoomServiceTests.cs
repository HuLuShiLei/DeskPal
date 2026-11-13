using DeskPal.Server.Data;
using DeskPal.Server.Features.Auth;
using DeskPal.Server.Features.Rooms;
using DeskPal.Shared.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeskPal.Tests.Server;

public sealed class RoomServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DeskPalDbContext _db;
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly RoomService _rooms;
    private readonly Guid _alice;
    private readonly Guid _bob;
    private readonly Guid _carol;

    public RoomServiceTests()
    {
        _connection.Open();
        _db = new DeskPalDbContext(new DbContextOptionsBuilder<DeskPalDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _rooms = new RoomService(_db, _time, Options.Create(new RoomOptions { MaxOwnedRoomsPerUser = 2, MaxMembersPerRoom = 20, InviteLifetimeHours = 24 }));
        _alice = AddUser("alice");
        _bob = AddUser("bob");
        _carol = AddUser("carol");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Create_is_limited_per_owner()
    {
        Assert.True((await _rooms.CreateAsync(_alice, "A")).IsSuccess);
        Assert.True((await _rooms.CreateAsync(_alice, "B")).IsSuccess);

        var third = await _rooms.CreateAsync(_alice, "C");

        Assert.Equal(409, third.Status);
        Assert.True((await _rooms.CreateAsync(_bob, "Bob's")).IsSuccess);
    }

    [Fact]
    public async Task Join_by_invite_respects_member_limit_and_duplicates()
    {
        var room = (await _rooms.CreateAsync(_alice, "Home")).Value!;
        var invite = (await _rooms.CreateInviteAsync(_alice, room.Id)).Value!;

        var joined = await _rooms.JoinAsync(_bob, invite.Code.ToLowerInvariant());
        Assert.Equal(2, joined.Value!.Members.Count);

        Assert.Equal(20, joined.Value.MaxMembers);
        Assert.Equal(409, (await _rooms.JoinAsync(_bob, invite.Code)).Status);

        var carol = await _rooms.JoinAsync(_carol, invite.Code);
        Assert.True(carol.IsSuccess);
        Assert.Equal(3, carol.Value!.Members.Count);
    }

    [Fact]
    public async Task Invite_expires_and_regenerating_invalidates_the_old_code()
    {
        var room = (await _rooms.CreateAsync(_alice, "Home")).Value!;
        var first = (await _rooms.CreateInviteAsync(_alice, room.Id)).Value!;
        var second = (await _rooms.CreateInviteAsync(_alice, room.Id)).Value!;

        Assert.Equal(404, (await _rooms.JoinAsync(_bob, first.Code)).Status);

        _time.Now = _time.Now.AddHours(25);
        Assert.Equal(404, (await _rooms.JoinAsync(_bob, second.Code)).Status);
    }

    [Fact]
    public async Task Owner_must_transfer_before_leaving()
    {
        var room = await CreateFullRoomAsync();

        Assert.Equal(409, (await _rooms.LeaveAsync(_alice, room)).Status);
        Assert.Equal(403, (await _rooms.TransferOwnerAsync(_bob, room, _bob)).Status);
        Assert.Equal(400, (await _rooms.TransferOwnerAsync(_alice, room, _carol)).Status);

        var transferred = await _rooms.TransferOwnerAsync(_alice, room, _bob);
        Assert.Equal(_bob, transferred.Value!.OwnerId);
        Assert.True((await _rooms.LeaveAsync(_alice, room)).IsSuccess);
        Assert.Single((await _rooms.ListForUserAsync(_bob)).Single().Members);
    }

    [Fact]
    public async Task Only_owner_can_dissolve_and_dissolving_removes_members_and_invites()
    {
        var room = await CreateFullRoomAsync();

        Assert.Equal(403, (await _rooms.DissolveAsync(_bob, room)).Status);

        var dissolved = await _rooms.DissolveAsync(_alice, room);
        Assert.Equal([_alice, _bob], dissolved.Value!.OrderBy(id => id == _alice ? 0 : 1));
        Assert.Empty(await _rooms.ListForUserAsync(_bob));
        Assert.Equal(0, await _db.RoomInvites.CountAsync());
    }

    [Fact]
    public async Task Only_owner_can_rename_and_name_is_validated()
    {
        var room = await CreateFullRoomAsync();

        Assert.Equal(403, (await _rooms.RenameAsync(_bob, room, "Bob's")).Status);
        Assert.Equal(404, (await _rooms.RenameAsync(_carol, room, "Carol's")).Status);
        Assert.Equal(400, (await _rooms.RenameAsync(_alice, room, "   ")).Status);
        Assert.Equal(400, (await _rooms.RenameAsync(_alice, room, new string('名', 31))).Status);

        var renamed = await _rooms.RenameAsync(_alice, room, "  我们家  ");
        Assert.True(renamed.Value!.Changed);
        Assert.Equal("我们家", renamed.Value.Room.Name);
        Assert.Equal("我们家", (await _rooms.ListForUserAsync(_bob)).Single().Name);
        Assert.False((await _rooms.RenameAsync(_alice, room, "我们家")).Value!.Changed);
    }

    [Fact]
    public async Task Privacy_profile_is_per_room_member()
    {
        var room = await CreateFullRoomAsync();
        var levels = new Dictionary<DeskPal.Shared.Presence.ActivityCategory, DeskPal.Shared.Presence.PrivacyLevel>
        {
            [DeskPal.Shared.Presence.ActivityCategory.Video] = DeskPal.Shared.Presence.PrivacyLevel.Detail,
        };

        await _rooms.SetPrivacyAsync(_alice, room, new DeskPal.Shared.Api.PrivacyProfileDto(levels));

        Assert.Single((await _rooms.GetPrivacyAsync(_alice, room)).Value!.Levels);
        Assert.Equal(404, (await _rooms.GetPrivacyAsync(_carol, room)).Status);
    }

    [Fact]
    public async Task New_members_share_only_the_state()
    {
        var room = await CreateFullRoomAsync();

        foreach (var user in new[] { _alice, _bob })
        {
            var levels = (await _rooms.GetPrivacyAsync(user, room)).Value!.Levels;
            var profile = new DeskPal.Shared.Presence.PrivacyProfile { Levels = levels };
            Assert.All(
                Enum.GetValues<DeskPal.Shared.Presence.ActivityCategory>().Where(c => c != DeskPal.Shared.Presence.ActivityCategory.None),
                c => Assert.Equal(DeskPal.Shared.Presence.PrivacyLevel.StateOnly, profile.LevelFor(c)));
        }
    }

    [Fact]
    public void Password_hash_round_trips()
    {
        var hash = PasswordHasher.Hash("password123");

        Assert.True(PasswordHasher.Verify("password123", hash));
        Assert.False(PasswordHasher.Verify("password124", hash));
    }

    [Fact]
    public async Task Leaving_does_not_block_rejoining_with_the_same_invite()
    {
        var room = (await _rooms.CreateAsync(_alice, "Home")).Value!;
        var invite = (await _rooms.CreateInviteAsync(_alice, room.Id)).Value!;
        Assert.True((await _rooms.JoinAsync(_bob, invite.Code)).IsSuccess);

        Assert.True((await _rooms.LeaveAsync(_bob, room.Id)).IsSuccess);
        Assert.True((await _rooms.JoinAsync(_bob, invite.Code)).IsSuccess);
        Assert.Equal(0, await _db.RoomBans.CountAsync());
    }

    [Fact]
    public async Task Kicked_member_cannot_rejoin_and_leaves_the_list()
    {
        var room = await CreateFullRoomAsync();

        Assert.Equal(403, (await _rooms.KickAsync(_bob, room, _alice)).Status);
        Assert.Equal(400, (await _rooms.KickAsync(_alice, room, _alice)).Status);
        Assert.Equal(404, (await _rooms.KickAsync(_alice, room, _carol)).Status);

        var kicked = await _rooms.KickAsync(_alice, room, _bob);
        Assert.True(kicked.IsSuccess);
        Assert.Empty(await _rooms.ListForUserAsync(_bob));
        Assert.Single((await _rooms.ListForUserAsync(_alice)).Single().Members);
        Assert.Equal(1, await _db.RoomBans.CountAsync());

        var current = await _db.RoomInvites.SingleAsync(i => i.RoomId == room);
        var rejected = await _rooms.JoinAsync(_bob, current.Code);
        Assert.Equal(403, rejected.Status);
        Assert.Equal(ApiErrorCodes.RoomBanned, rejected.Error);

        var renewed = (await _rooms.CreateInviteAsync(_alice, room)).Value!;
        Assert.Equal(0, await _db.RoomBans.CountAsync());
        Assert.Equal(404, (await _rooms.JoinAsync(_bob, current.Code)).Status);
        Assert.True((await _rooms.JoinAsync(_bob, renewed.Code)).IsSuccess);
        Assert.Equal(2, (await _rooms.ListForUserAsync(_alice)).Single().Members.Count);

        Assert.True((await _rooms.KickAsync(_alice, room, _bob)).IsSuccess);
        Assert.Equal(1, await _db.RoomBans.CountAsync());
        Assert.True((await _rooms.DissolveAsync(_alice, room)).IsSuccess);
        Assert.Equal(0, await _db.RoomBans.CountAsync());
    }

    [Fact]
    public async Task Configured_cap_raises_an_existing_room_and_rejects_the_next_person()
    {
        var room = (await _rooms.CreateAsync(_alice, "Home")).Value!;
        var stored = await _db.Rooms.SingleAsync(r => r.Id == room.Id);
        stored.MaxMembers = 8;
        await _db.SaveChangesAsync();

        var capped = new RoomService(_db, _time, Options.Create(new RoomOptions { MaxMembersPerRoom = 3, InviteLifetimeHours = 24 }));
        var invite = (await capped.CreateInviteAsync(_alice, room.Id)).Value!;
        Assert.Equal(3, (await capped.ListForUserAsync(_alice)).Single().MaxMembers);
        Assert.True((await capped.JoinAsync(_bob, invite.Code)).IsSuccess);
        Assert.True((await capped.JoinAsync(_carol, invite.Code)).IsSuccess);

        var rejected = await capped.JoinAsync(AddUser("dave"), invite.Code);
        Assert.Equal(409, rejected.Status);
        Assert.Equal(ApiErrorCodes.RoomFull, rejected.Error);
        Assert.Equal("3", rejected.Args![ApiErrorCodes.LimitArg]);
    }

    [Fact]
    public async Task Blocks_are_listed_only_for_the_blocker_and_checked_both_ways()
    {
        var room = await CreateFullRoomAsync();
        var invite = (await _rooms.CreateInviteAsync(_alice, room)).Value!;
        await _rooms.JoinAsync(_carol, invite.Code);

        Assert.Equal(400, (await _rooms.BlockAsync(_bob, room, _bob)).Status);
        Assert.Equal(404, (await _rooms.BlockAsync(_bob, room, AddUser("dave"))).Status);
        Assert.True((await _rooms.BlockAsync(_bob, room, _alice)).Value);
        Assert.False((await _rooms.BlockAsync(_bob, room, _alice)).Value);

        Assert.Equal([_alice], (await _rooms.ListForUserAsync(_bob)).Single().Blocked);
        Assert.Empty((await _rooms.ListForUserAsync(_alice)).Single().Blocked!);
        Assert.Equal([_alice], await _rooms.GetBlockPeersAsync(room, _bob));
        Assert.Equal([_bob], await _rooms.GetBlockPeersAsync(room, _alice));
        Assert.Empty(await _rooms.GetBlockPeersAsync(room, _carol));

        Assert.True((await _rooms.UnblockAsync(_bob, room, _alice)).Value);
        Assert.False((await _rooms.UnblockAsync(_bob, room, _alice)).Value);
        Assert.Empty(await _rooms.GetBlockPeersAsync(room, _alice));
        Assert.Equal(404, (await _rooms.UnblockAsync(AddUser("erin"), room, _alice)).Status);
    }

    [Fact]
    public async Task Blocks_survive_leaving_and_are_removed_with_the_room()
    {
        var room = await CreateFullRoomAsync();
        await _rooms.BlockAsync(_alice, room, _bob);
        await _rooms.LeaveAsync(_bob, room);

        Assert.Empty((await _rooms.ListForUserAsync(_alice)).Single().Blocked!);
        var invite = (await _rooms.CreateInviteAsync(_alice, room)).Value!;
        await _rooms.JoinAsync(_bob, invite.Code);
        Assert.Equal([_bob], (await _rooms.ListForUserAsync(_alice)).Single().Blocked);

        await _rooms.DissolveAsync(_alice, room);
        Assert.Equal(0, await _db.RoomBlocks.CountAsync());
    }

    private async Task<Guid> CreateFullRoomAsync()
    {
        var room = (await _rooms.CreateAsync(_alice, "Home")).Value!;
        var invite = (await _rooms.CreateInviteAsync(_alice, room.Id)).Value!;
        await _rooms.JoinAsync(_bob, invite.Code);
        return room.Id;
    }

    private Guid AddUser(string name)
    {
        var user = new User { Id = Guid.NewGuid(), UserName = name, DisplayName = name, PasswordHash = "x", CreatedAt = _time.Now };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user.Id;
    }
}
