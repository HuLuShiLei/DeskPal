using DeskPal.Shared.Presence;

namespace DeskPal.Shared.Api;

public enum RoomRole
{
    Member = 0,
    Owner = 1,
}

public sealed record RoomMemberDto(Guid UserId, string DisplayName, RoomRole Role, DateTimeOffset JoinedAt);

public sealed record RoomDto(Guid Id, string Name, Guid OwnerId, int MaxMembers, DateTimeOffset CreatedAt, IReadOnlyList<RoomMemberDto> Members);

public sealed record CreateRoomRequest(string Name);

public sealed record RenameRoomRequest(string Name);

public sealed record JoinRoomRequest(string Code);

public sealed record InviteDto(string Code, DateTimeOffset ExpiresAt);

public sealed record TransferOwnerRequest(Guid NewOwnerUserId);

public sealed record PrivacyProfileDto(Dictionary<ActivityCategory, PrivacyLevel> Levels);