using DeskPal.Shared.Presence;

namespace DeskPal.Shared.Api;

public enum RuleKind
{
    Process = 0,
    Domain = 1,
    TitleKeyword = 2,
}

/// <param name="Name">Default name, sent to room members as is.</param>
/// <param name="Names">Name in other UI languages, keyed by language (en). Each client shows the default name in its own language.</param>
public sealed record RuleDto(RuleKind Kind, string Pattern, ActivityCategory Category, string? Name = null, IReadOnlyDictionary<string, string>? Names = null);

public sealed record RuleSetDto(int Version, IReadOnlyList<RuleDto> Rules);

/// <summary>Response of GET /api/rules/builtin. Rules is null when the client already has the current version.</summary>
public sealed record BuiltinRulesResponse(int Version, IReadOnlyList<RuleDto>? Rules);

/// <summary>A personal rule. It belongs to the account and applies in every room and on every computer.</summary>
public sealed record UserRuleDto(RuleKind Kind, string Pattern, ActivityCategory Category, string? Name, DateTimeOffset UpdatedAt);

public sealed record UpsertUserRuleRequest(RuleKind Kind, string Pattern, ActivityCategory Category, string? Name);

/// <summary>Whether the user agreed to share usage data for improving the builtin rules.</summary>
public sealed record SetSharingRequest(bool Enabled);

/// <summary>An app or site the builtin rules did not recognize, with today's foreground minutes. No titles.</summary>
public sealed record UnknownAppItem(RuleKind Kind, string Pattern, string? DisplayName, int Minutes);

public sealed record UsageReportRequest(IReadOnlyList<UnknownAppItem> Apps);

/// <summary>Many accounts put the same app in the same category with their personal rules.</summary>
public sealed record ConsensusCandidate(RuleKind Kind, string Pattern, string? Name, ActivityCategory Category, int Users, int Agreeing, ActivityCategory? Builtin);

/// <summary>Many accounts use an app that no rule recognizes.</summary>
public sealed record UnknownAppCandidate(RuleKind Kind, string Pattern, string? DisplayName, int Users, int TotalMinutes);

/// <summary>Response of GET /api/rules/candidates, read by the scheduled workflow that opens a pull request.</summary>
public sealed record RuleCandidatesResponse(int BuiltinVersion, IReadOnlyList<ConsensusCandidate> Consensus, IReadOnlyList<UnknownAppCandidate> Unknown);