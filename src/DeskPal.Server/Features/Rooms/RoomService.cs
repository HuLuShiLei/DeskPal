using System.Security.Cryptography;
using System.Text.Json;
using DeskPal.Server.Common;
using DeskPal.Server.Data;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using DeskPal.Shared.Presence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeskPal.Server.Features.Rooms;

public sealed class RoomService(DeskPalDbContext db, TimeProvider time, IOptions<RoomOptions> options)
{
    private const string InviteAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string NotMember = "Room 不存在，或者你不是该 Room 的成员";

    private readonly RoomOptions _options = options.Value;

    public async Task<IReadOnlyList<RoomDto>> ListForUserAsync(Guid userId)
    {
        var roomIds = await db.RoomMembers.Where(m => m.UserId == userId).Select(m => m.RoomId).ToListAsync();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id))
            .Include(r => r.Members).ThenInclude(m => m.User)
            .ToListAsync();
        return rooms.OrderBy(r => r.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<ServiceResult<RoomDto>> CreateAsync(Guid userId, string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 30)
        {
            return ServiceResult<RoomDto>.Fail(400, "Room 名称需要 1 到 30 个字符");
        }

        var owned = await db.Rooms.CountAsync(r => r.OwnerId == userId);
        if (owned >= _options.MaxOwnedRoomsPerUser)
        {
            return ServiceResult<RoomDto>.Fail(409, $"每个用户最多创建 {_options.MaxOwnedRoomsPerUser} 个 Room");
        }

        var now = time.GetUtcNow();
        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerId = userId,
            MaxMembers = _options.MaxMembersPerRoom,
            CreatedAt = now,
        };
        room.Members.Add(new RoomMember { RoomId = room.Id, UserId = userId, Role = RoomRole.Owner, JoinedAt = now });
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return ServiceResult<RoomDto>.Ok((await GetDtoAsync(room.Id))!);
    }

    public async Task<ServiceResult<InviteDto>> CreateInviteAsync(Guid userId, Guid roomId)
    {
        if (!await IsMemberAsync(userId, roomId))
        {
            return ServiceResult<InviteDto>.Fail(404, NotMember);
        }

        db.RoomInvites.RemoveRange(db.RoomInvites.Where(i => i.RoomId == roomId));

        string code;
        do
        {
            code = RandomNumberGenerator.GetString(InviteAlphabet, 8);
        }
        while (await db.RoomInvites.AnyAsync(i => i.Code == code));

        var invite = new RoomInvite
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            Code = code,
            CreatedBy = userId,
            ExpiresAt = time.GetUtcNow().AddHours(_options.InviteLifetimeHours),
        };
        db.RoomInvites.Add(invite);
        await db.SaveChangesAsync();
        return ServiceResult<InviteDto>.Ok(new InviteDto(invite.Code, invite.ExpiresAt));
    }

    public async Task<ServiceResult<RoomDto>> JoinAsync(Guid userId, string? code)
    {
        code = code?.Trim().ToUpperInvariant() ?? "";
        var invite = await db.RoomInvites.SingleOrDefaultAsync(i => i.Code == code);
        if (invite is null || invite.ExpiresAt <= time.GetUtcNow())
        {
            return ServiceResult<RoomDto>.Fail(404, "邀请码无效或已过期");
        }

        var room = await db.Rooms.Include(r => r.Members).SingleAsync(r => r.Id == invite.RoomId);
        if (room.Members.Any(m => m.UserId == userId))
        {
            return ServiceResult<RoomDto>.Fail(409, "你已经是该 Room 的成员");
        }

        if (room.Members.Count >= room.MaxMembers)
        {
            return ServiceResult<RoomDto>.Fail(409, $"该 Room 已满（上限 {room.MaxMembers} 人）");
        }

        room.Members.Add(new RoomMember { RoomId = room.Id, UserId = userId, Role = RoomRole.Member, JoinedAt = time.GetUtcNow() });
        await db.SaveChangesAsync();
        return ServiceResult<RoomDto>.Ok((await GetDtoAsync(room.Id))!);
    }

    public async Task<ServiceResult<Unit>> LeaveAsync(Guid userId, Guid roomId)
    {
        var member = await db.RoomMembers.FindAsync(roomId, userId);
        if (member is null)
        {
            return ServiceResult<Unit>.Fail(404, NotMember);
        }

        if (member.Role == RoomRole.Owner)
        {
            return ServiceResult<Unit>.Fail(409, "所有者不能直接退出，请先转让所有者或解散 Room");
        }

        db.RoomMembers.Remove(member);
        await db.SaveChangesAsync();
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    public async Task<ServiceResult<RoomDto>> TransferOwnerAsync(Guid userId, Guid roomId, Guid newOwnerId)
    {
        var room = await db.Rooms.Include(r => r.Members).SingleOrDefaultAsync(r => r.Id == roomId);
        if (room is null || room.Members.All(m => m.UserId != userId))
        {
            return ServiceResult<RoomDto>.Fail(404, NotMember);
        }

        if (room.OwnerId != userId)
        {
            return ServiceResult<RoomDto>.Fail(403, "只有所有者可以转让所有者身份");
        }

        var target = room.Members.SingleOrDefault(m => m.UserId == newOwnerId);
        if (target is null || newOwnerId == userId)
        {
            return ServiceResult<RoomDto>.Fail(400, "只能转让给该 Room 的其他成员");
        }

        room.Members.Single(m => m.UserId == userId).Role = RoomRole.Member;
        target.Role = RoomRole.Owner;
        room.OwnerId = newOwnerId;
        await db.SaveChangesAsync();
        return ServiceResult<RoomDto>.Ok((await GetDtoAsync(room.Id))!);
    }

    /// <returns>Member ids at the time of dissolving, so callers can notify them.</returns>
    public async Task<ServiceResult<IReadOnlyList<Guid>>> DissolveAsync(Guid userId, Guid roomId)
    {
        var room = await db.Rooms.Include(r => r.Members).SingleOrDefaultAsync(r => r.Id == roomId);
        if (room is null || room.Members.All(m => m.UserId != userId))
        {
            return ServiceResult<IReadOnlyList<Guid>>.Fail(404, NotMember);
        }

        if (room.OwnerId != userId)
        {
            return ServiceResult<IReadOnlyList<Guid>>.Fail(403, "只有所有者可以解散 Room");
        }

        var memberIds = room.Members.Select(m => m.UserId).ToList();
        db.Rooms.Remove(room);
        await db.SaveChangesAsync();
        return ServiceResult<IReadOnlyList<Guid>>.Ok(memberIds);
    }

    public async Task<ServiceResult<PrivacyProfileDto>> GetPrivacyAsync(Guid userId, Guid roomId)
    {
        var member = await db.RoomMembers.FindAsync(roomId, userId);
        if (member is null)
        {
            return ServiceResult<PrivacyProfileDto>.Fail(404, NotMember);
        }

        var profile = member.PrivacyProfileJson is null
            ? PrivacyProfile.CreateDefault()
            : JsonSerializer.Deserialize<PrivacyProfile>(member.PrivacyProfileJson, DeskPalJson.Options) ?? PrivacyProfile.CreateDefault();
        return ServiceResult<PrivacyProfileDto>.Ok(new PrivacyProfileDto(profile.Levels));
    }

    public async Task<ServiceResult<PrivacyProfileDto>> SetPrivacyAsync(Guid userId, Guid roomId, PrivacyProfileDto dto)
    {
        var member = await db.RoomMembers.FindAsync(roomId, userId);
        if (member is null)
        {
            return ServiceResult<PrivacyProfileDto>.Fail(404, NotMember);
        }

        var profile = new PrivacyProfile { Levels = dto.Levels ?? new() };
        member.PrivacyProfileJson = JsonSerializer.Serialize(profile, DeskPalJson.Options);
        await db.SaveChangesAsync();
        return ServiceResult<PrivacyProfileDto>.Ok(new PrivacyProfileDto(profile.Levels));
    }

    public Task<bool> IsMemberAsync(Guid userId, Guid roomId) =>
        db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == userId);

    public Task<List<Guid>> GetRoomIdsForUserAsync(Guid userId) =>
        db.RoomMembers.Where(m => m.UserId == userId).Select(m => m.RoomId).ToListAsync();

    public Task<List<Guid>> GetMemberIdsAsync(Guid roomId) =>
        db.RoomMembers.Where(m => m.RoomId == roomId).Select(m => m.UserId).ToListAsync();

    private async Task<RoomDto?> GetDtoAsync(Guid roomId)
    {
        var room = await db.Rooms.Include(r => r.Members).ThenInclude(m => m.User).SingleOrDefaultAsync(r => r.Id == roomId);
        return room is null ? null : ToDto(room);
    }

    private static RoomDto ToDto(Room room) => new(
        room.Id,
        room.Name,
        room.OwnerId,
        room.MaxMembers,
        room.CreatedAt,
        room.Members
            .OrderBy(m => m.JoinedAt)
            .Select(m => new RoomMemberDto(m.UserId, m.User?.DisplayName ?? "", m.Role, m.JoinedAt))
            .ToList());
}