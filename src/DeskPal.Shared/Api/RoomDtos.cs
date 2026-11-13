using DeskPal.Shared.Presence;

namespace DeskPal.Shared.Api;

public enum RoomRole
{
    Member = 0,
    Owner = 1,
}

public sealed record RoomMemberDto(Guid UserId, string DisplayName, RoomRole Role, DateTimeOffset JoinedAt);

/// <param name="Blocked">Members the requesting user has blocked in this room. Never includes other people's blocks.</param>
public sealed record RoomDto(
    Guid Id,
    string Name,
    Guid OwnerId,
    int MaxMembers,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RoomMemberDto> Members,
    IReadOnlyList<Guid>? Blocked = null);

public sealed record CreateRoomRequest(string Name);

public sealed record RenameRoomRequest(string Name);

public sealed record JoinRoomRequest(string Code);

public sealed record InviteDto(string Code, DateTimeOffset ExpiresAt);

public sealed record TransferOwnerRequest(Guid NewOwnerUserId);

public sealed record KickMemberRequest(Guid UserId);

public sealed record BlockMemberRequest(Guid UserId);

public sealed record PrivacyProfileDto(Dictionary<ActivityCategory, PrivacyLevel> Levels);
