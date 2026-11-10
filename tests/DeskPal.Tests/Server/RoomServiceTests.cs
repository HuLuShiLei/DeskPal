using DeskPal.Server.Data;
using DeskPal.Server.Features.Auth;
using DeskPal.Server.Features.Rooms;
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
        _rooms = new RoomService(_db, _time, Options.Create(new RoomOptions { MaxOwnedRoomsPerUser = 2, MaxMembersPerRoom = 2, InviteLifetimeHours = 24 }));
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

        Assert.Equal(409, (await _rooms.JoinAsync(_bob, invite.Code)).Status);
        Assert.Equal(409, (await _rooms.JoinAsync(_carol, invite.Code)).Status);
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
    public async Task Privacy_profile_is_per_room_member()
    {
        var room = await CreateFullRoomAsync();
        var levels = new Dictionary<DeskPal.Shared.Presence.ActivityCategory, DeskPal.Shared.Presence.PrivacyLevel>
        {
            [DeskPal.Shared.Presence.ActivityCategory.Video] = DeskPal.Shared.Presence.PrivacyLevel.Detail,
        };

        await _rooms.SetPrivacyAsync(_alice, room, new DeskPal.Shared.Api.PrivacyProfileDto(levels));

        Assert.Single((await _rooms.GetPrivacyAsync(_alice, room)).Value!.Levels);
        Assert.Empty((await _rooms.GetPrivacyAsync(_bob, room)).Value!.Levels);
        Assert.Equal(404, (await _rooms.GetPrivacyAsync(_carol, room)).Status);
    }

    [Fact]
    public void Password_hash_round_trips()
    {
        var hash = PasswordHasher.Hash("password123");

        Assert.True(PasswordHasher.Verify("password123", hash));
        Assert.False(PasswordHasher.Verify("password124", hash));
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