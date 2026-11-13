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
    internal const string InviteAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string NotMember = ApiErrorCodes.RoomNotMember;
    private const string InvalidName = ApiErrorCodes.RoomNameInvalid;

    private readonly RoomOptions _options = options.Value;

    public async Task<IReadOnlyList<RoomDto>> ListForUserAsync(Guid userId)
    {
        var roomIds = await db.RoomMembers.Where(m => m.UserId == userId).Select(m => m.RoomId).ToListAsync();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id))
            .Include(r => r.Members).ThenInclude(m => m.User)
            .ToListAsync();
        var blocks = await db.RoomBlocks.Where(b => b.BlockerId == userId).ToListAsync();
        var changed = false;
        foreach (var room in rooms)
        {
            changed |= ApplyMemberCap(room);
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }

        return rooms.OrderBy(r => r.CreatedAt).Select(r => ToDto(r, blocks.Where(b => b.RoomId == r.Id).Select(b => b.BlockedId))).ToList();
    }

    public async Task<ServiceResult<RoomDto>> CreateAsync(Guid userId, string? name)
    {
        name = name?.Trim() ?? "";
        if (!IsValidName(name))
        {
            return ServiceResult<RoomDto>.Fail(400, InvalidName);
        }

        var owned = await db.Rooms.CountAsync(r => r.OwnerId == userId);
        if (owned >= _options.MaxOwnedRoomsPerUser)
        {
            return ServiceResult<RoomDto>.Fail(409, ApiErrorCodes.RoomOwnLimit, _options.MaxOwnedRoomsPerUser);
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
        room.Members.Add(new RoomMember { RoomId = room.Id, UserId = userId, Role = RoomRole.Owner, JoinedAt = now, PrivacyProfileJson = StateOnlyPrivacy() });
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return ServiceResult<RoomDto>.Ok((await GetDtoAsync(room.Id, userId))!);
    }

    public async Task<ServiceResult<InviteDto>> CreateInviteAsync(Guid userId, Guid roomId)
    {
        if (!await IsMemberAsync(userId, roomId))
        {
            return ServiceResult<InviteDto>.Fail(404, NotMember);
        }

        db.RoomInvites.RemoveRange(db.RoomInvites.Where(i => i.RoomId == roomId));
        db.RoomBans.RemoveRange(db.RoomBans.Where(b => b.RoomId == roomId));

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
            return ServiceResult<RoomDto>.Fail(404, ApiErrorCodes.RoomInviteInvalid);
        }

        var room = await db.Rooms.Include(r => r.Members).SingleAsync(r => r.Id == invite.RoomId);
        if (room.Members.Any(m => m.UserId == userId))
        {
            return ServiceResult<RoomDto>.Fail(409, ApiErrorCodes.RoomAlreadyMember);
        }

        if (await db.RoomBans.AnyAsync(b => b.RoomId == room.Id && b.UserId == userId))
        {
            return ServiceResult<RoomDto>.Fail(403, ApiErrorCodes.RoomBanned);
        }

        ApplyMemberCap(room);
        if (room.Members.Count >= room.MaxMembers)
        {
            return ServiceResult<RoomDto>.Fail(409, ApiErrorCodes.RoomFull, room.MaxMembers);
        }

        room.Members.Add(new RoomMember { RoomId = room.Id, UserId = userId, Role = RoomRole.Member, JoinedAt = time.GetUtcNow(), PrivacyProfileJson = StateOnlyPrivacy() });
        await db.SaveChangesAsync();
        return ServiceResult<RoomDto>.Ok((await GetDtoAsync(room.Id, userId))!);
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
            return ServiceResult<Unit>.Fail(409, ApiErrorCodes.RoomOwnerCannotLeave);
        }

        db.RoomMembers.Remove(member);
        await db.SaveChangesAsync();
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    public async Task<ServiceResult<Unit>> KickAsync(Guid userId, Guid roomId, Guid targetUserId)
    {
        var room = await db.Rooms.Include(r => r.Members).SingleOrDefaultAsync(r => r.Id == roomId);
        if (room is null || room.Members.All(m => m.UserId != userId))
        {
            return ServiceResult<Unit>.Fail(404, NotMember);
        }

        if (room.OwnerId != userId)
        {
            return ServiceResult<Unit>.Fail(403, ApiErrorCodes.RoomOwnerOnlyKick);
        }

        if (targetUserId == room.OwnerId)
        {
            return ServiceResult<Unit>.Fail(400, ApiErrorCodes.RoomCannotKickOwner);
        }

        var target = room.Members.SingleOrDefault(m => m.UserId == targetUserId);
        if (target is null)
        {
            return ServiceResult<Unit>.Fail(404, ApiErrorCodes.RoomMemberMissing);
        }

        room.Members.Remove(target);
        if (!await db.RoomBans.AnyAsync(b => b.RoomId == roomId && b.UserId == targetUserId))
        {
            db.RoomBans.Add(new RoomBan
            {
                RoomId = roomId,
                UserId = targetUserId,
                BannedBy = userId,
                BannedAt = time.GetUtcNow(),
            });
        }

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
            return ServiceResult<RoomDto>.Fail(403, ApiErrorCodes.RoomOwnerOnlyTransfer);
        }

        var target = room.Members.SingleOrDefault(m => m.UserId == newOwnerId);
        if (target is null || newOwnerId == userId)
        {
            return ServiceResult<RoomDto>.Fail(400, ApiErrorCodes.RoomTransferTarget);
        }

        room.Members.Single(m => m.UserId == userId).Role = RoomRole.Member;
        target.Role = RoomRole.Owner;
        room.OwnerId = newOwnerId;
        await db.SaveChangesAsync();
        return ServiceResult<RoomDto>.Ok((await GetDtoAsync(room.Id, userId))!);
    }

    public async Task<ServiceResult<RenamedRoom>> RenameAsync(Guid userId, Guid roomId, string? name)
    {
        var room = await db.Rooms.Include(r => r.Members).SingleOrDefaultAsync(r => r.Id == roomId);
        if (room is null || room.Members.All(m => m.UserId != userId))
        {
            return ServiceResult<RenamedRoom>.Fail(404, NotMember);
        }

        if (room.OwnerId != userId)
        {
            return ServiceResult<RenamedRoom>.Fail(403, ApiErrorCodes.RoomOwnerOnlyRename);
        }

        name = name?.Trim() ?? "";
        if (!IsValidName(name))
        {
            return ServiceResult<RenamedRoom>.Fail(400, InvalidName);
        }

        var changed = room.Name != name;
        if (changed)
        {
            room.Name = name;
            await db.SaveChangesAsync();
        }

        return ServiceResult<RenamedRoom>.Ok(new RenamedRoom((await GetDtoAsync(room.Id, userId))!, changed));
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
            return ServiceResult<IReadOnlyList<Guid>>.Fail(403, ApiErrorCodes.RoomOwnerOnlyDissolve);
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

        return ServiceResult<PrivacyProfileDto>.Ok(ToDto(ReadPrivacy(member.PrivacyProfileJson)));
    }

    public async Task<ServiceResult<SavedPrivacy>> SetPrivacyAsync(Guid userId, Guid roomId, PrivacyProfileDto dto)
    {
        var member = await db.RoomMembers.FindAsync(roomId, userId);
        if (member is null)
        {
            return ServiceResult<SavedPrivacy>.Fail(404, NotMember);
        }

        if (dto.StatsDays is { } days && !PrivacyProfile.StatsDayOptions.Contains(days))
        {
            return ServiceResult<SavedPrivacy>.Fail(400, ApiErrorCodes.RoomStatsDaysInvalid);
        }

        var old = ReadPrivacy(member.PrivacyProfileJson);
        var profile = new PrivacyProfile
        {
            Levels = dto.Levels ?? old.Levels,
            ShareStats = dto.ShareStats ?? old.ShareStats,
            StatsDays = dto.StatsDays ?? old.StatsDays,
        };
        member.PrivacyProfileJson = JsonSerializer.Serialize(profile, DeskPalJson.Options);
        await db.SaveChangesAsync();
        var sharingChanged = profile.ShareStats != old.ShareStats || (profile.ShareStats && profile.StatsDays != old.StatsDays);
        return ServiceResult<SavedPrivacy>.Ok(new SavedPrivacy(ToDto(profile), sharingChanged));
    }

    private static PrivacyProfile ReadPrivacy(string? json) => json is null
        ? PrivacyProfile.CreateDefault()
        : JsonSerializer.Deserialize<PrivacyProfile>(json, DeskPalJson.Options) ?? PrivacyProfile.CreateDefault();

    private static PrivacyProfileDto ToDto(PrivacyProfile profile) => new(profile.Levels, profile.ShareStats, profile.StatsDays);

    /// <returns>True when the block is new, so callers only notify on a real change.</returns>
    public async Task<ServiceResult<bool>> BlockAsync(Guid userId, Guid roomId, Guid targetUserId)
    {
        if (await CheckBlockTargetAsync(userId, roomId, targetUserId) is { } error)
        {
            return ServiceResult<bool>.Fail(error.Status, error.Message);
        }

        if (await db.RoomBlocks.FindAsync(roomId, userId, targetUserId) is not null)
        {
            return ServiceResult<bool>.Ok(false);
        }

        db.RoomBlocks.Add(new RoomBlock { RoomId = roomId, BlockerId = userId, BlockedId = targetUserId, CreatedAt = time.GetUtcNow() });
        await db.SaveChangesAsync();
        return ServiceResult<bool>.Ok(true);
    }

    /// <returns>True when a block was removed.</returns>
    public async Task<ServiceResult<bool>> UnblockAsync(Guid userId, Guid roomId, Guid targetUserId)
    {
        if (!await IsMemberAsync(userId, roomId))
        {
            return ServiceResult<bool>.Fail(404, NotMember);
        }

        var block = await db.RoomBlocks.FindAsync(roomId, userId, targetUserId);
        if (block is null)
        {
            return ServiceResult<bool>.Ok(false);
        }

        db.RoomBlocks.Remove(block);
        await db.SaveChangesAsync();
        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>Members who must not get my presence and whose presence I must not get, whichever side blocked.</summary>
    public async Task<HashSet<Guid>> GetBlockPeersAsync(Guid roomId, Guid userId)
    {
        var blocks = await db.RoomBlocks
            .Where(b => b.RoomId == roomId && (b.BlockerId == userId || b.BlockedId == userId))
            .ToListAsync();
        return blocks.Select(b => b.BlockerId == userId ? b.BlockedId : b.BlockerId).ToHashSet();
    }

    private async Task<(int Status, string Message)?> CheckBlockTargetAsync(Guid userId, Guid roomId, Guid targetUserId)
    {
        if (!await IsMemberAsync(userId, roomId))
        {
            return (404, NotMember);
        }

        if (targetUserId == userId)
        {
            return (400, ApiErrorCodes.RoomCannotBlockSelf);
        }

        return await IsMemberAsync(targetUserId, roomId) ? null : (404, ApiErrorCodes.RoomMemberMissing);
    }

    public Task<bool> IsMemberAsync(Guid userId, Guid roomId) =>
        db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == userId);

    public Task<List<Guid>> GetRoomIdsForUserAsync(Guid userId) =>
        db.RoomMembers.Where(m => m.UserId == userId).Select(m => m.RoomId).ToListAsync();

    public Task<List<Guid>> GetMemberIdsAsync(Guid roomId) =>
        db.RoomMembers.Where(m => m.RoomId == roomId).Select(m => m.UserId).ToListAsync();

    private static bool IsValidName(string name) => name.Length is >= 1 and <= 30;

    private static string StateOnlyPrivacy() => JsonSerializer.Serialize(PrivacyProfile.CreateStateOnly(), DeskPalJson.Options);

    private bool ApplyMemberCap(Room room)
    {
        if (room.MaxMembers == _options.MaxMembersPerRoom)
        {
            return false;
        }

        room.MaxMembers = _options.MaxMembersPerRoom;
        return true;
    }

    private async Task<RoomDto?> GetDtoAsync(Guid roomId, Guid userId)
    {
        var room = await db.Rooms.Include(r => r.Members).ThenInclude(m => m.User).SingleOrDefaultAsync(r => r.Id == roomId);
        if (room is null)
        {
            return null;
        }

        var blocked = await db.RoomBlocks.Where(b => b.RoomId == roomId && b.BlockerId == userId).Select(b => b.BlockedId).ToListAsync();
        return ToDto(room, blocked);
    }

    /// <param name="blocked">My blocks in this room. Blocks of members who have left stay in the database but are not listed.</param>
    private static RoomDto ToDto(Room room, IEnumerable<Guid> blocked) => new(
        room.Id,
        room.Name,
        room.OwnerId,
        room.MaxMembers,
        room.CreatedAt,
        room.Members
            .OrderBy(m => m.JoinedAt)
            .Select(m =>
            {
                var privacy = ReadPrivacy(m.PrivacyProfileJson);
                return new RoomMemberDto(m.UserId, m.User?.DisplayName ?? "", m.Role, m.JoinedAt, privacy.ShareStats, privacy.ShareStats ? privacy.StatsDays : 0);
            })
            .ToList(),
        blocked.Where(id => room.Members.Any(m => m.UserId == id)).ToList());
}

public sealed record RenamedRoom(RoomDto Room, bool Changed);

/// <param name="SharingChanged">Stats sharing was turned on or off, or the shared days changed while on.</param>
public sealed record SavedPrivacy(PrivacyProfileDto Profile, bool SharingChanged);
