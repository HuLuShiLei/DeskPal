using DeskPal.Shared.Presence;

namespace DeskPal.Shared.Api;

public enum RuleKind
{
    Process = 0,
    Domain = 1,
    TitleKeyword = 2,
}

public sealed record RuleDto(RuleKind Kind, string Pattern, ActivityCategory Category, string? Name = null);

public sealed record RuleSetDto(int Version, IReadOnlyList<RuleDto> Rules);

/// <summary>Response of GET /api/rules/builtin. Rules is null when the client already has the current version.</summary>
public sealed record BuiltinRulesResponse(int Version, IReadOnlyList<RuleDto>? Rules);

public sealed record RoomRuleDto(RuleKind Kind, string Pattern, ActivityCategory Category, string? Name, Guid CreatedBy, DateTimeOffset UpdatedAt);

public sealed record UpsertRoomRuleRequest(RuleKind Kind, string Pattern, ActivityCategory Category, string? Name);