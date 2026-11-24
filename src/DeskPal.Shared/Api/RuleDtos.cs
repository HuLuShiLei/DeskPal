using DeskPal.Shared.Presence;

namespace DeskPal.Shared.Api;

public enum RuleKind
{
    Process = 0,
    Domain = 1,
    TitleKeyword = 2,

    /// <summary>A desktop app while its window title contains a keyword. Pattern is "process/keyword", see <see cref="RulePattern.AppTitle"/>.</summary>
    AppTitle = 3,
}

/// <param name="Name">Default name, sent to room members as is.</param>
/// <param name="Names">Name in other UI languages, keyed by language (en). Each client shows the default name in its own language.</param>
public sealed record RuleDto(RuleKind Kind, string Pattern, ActivityCategory Category, string? Name = null, IReadOnlyDictionary<string, string>? Names = null);

/// <summary>
/// A desktop app installed under this folder gets the category, named after the folder below it (the game).
/// Written with \ and matched on Windows and macOS paths alike. Ending with \ names the next folder; otherwise the folder
/// that contains the match.
/// </summary>
public sealed record FolderRuleDto(string Folder, ActivityCategory Category);

/// <param name="Folders">Null from an older server or cache: the client keeps its own list.</param>
public sealed record RuleSetDto(int Version, IReadOnlyList<RuleDto> Rules, IReadOnlyList<FolderRuleDto>? Folders = null);

/// <summary>Response of GET /api/rules/builtin. Rules and Folders are null when the client already has the current version.</summary>
public sealed record BuiltinRulesResponse(int Version, IReadOnlyList<RuleDto>? Rules, IReadOnlyList<FolderRuleDto>? Folders = null);

/// <summary>A personal rule. It belongs to the account and applies in every room and on every computer.</summary>
public sealed record UserRuleDto(RuleKind Kind, string Pattern, ActivityCategory Category, string? Name, DateTimeOffset UpdatedAt);

public sealed record UpsertUserRuleRequest(RuleKind Kind, string Pattern, ActivityCategory Category, string? Name);

/// <summary>Whether the user agreed to share usage data for improving the builtin rules.</summary>
public sealed record SetSharingRequest(bool Enabled);

/// <summary>An app or site the builtin rules did not recognize, with today's foreground minutes. No titles.</summary>
/// <param name="Guessed">Category the client guessed without a rule (a game library folder, an Unreal game); null when it fell back to Other.</param>
public sealed record UnknownAppItem(RuleKind Kind, string Pattern, string? DisplayName, int Minutes, ActivityCategory? Guessed = null);

/// <param name="Rules">Personal rules for apps; only sent while account sync is off, and replace the earlier ones.</param>
public sealed record UsageReportRequest(IReadOnlyList<UnknownAppItem> Apps, IReadOnlyList<SharedRuleItem>? Rules = null);

/// <summary>Many accounts put the same app in the same category with their personal rules.</summary>
public sealed record ConsensusCandidate(RuleKind Kind, string Pattern, string? Name, ActivityCategory Category, int Users, int Agreeing, ActivityCategory? Builtin);

/// <summary>Accounts use an app that no rule recognizes.</summary>
/// <param name="Guessed">The category most of them guessed without a rule, or null when it fell back to Other.</param>
public sealed record UnknownAppCandidate(RuleKind Kind, string Pattern, string? DisplayName, int Users, int TotalMinutes, ActivityCategory? Guessed = null);

/// <summary>Response of GET /api/rules/candidates, read by the scheduled workflow that opens a pull request.</summary>
/// <param name="Consensus">Personal rules that agree enough to be written into the builtin rules.</param>
/// <param name="Unknown">Apps without any rule, for a person to decide.</param>
/// <param name="Guessed">Apps enough accounts had guessed the same way, to be written into the builtin rules with their names.</param>
/// <param name="Signals">Personal rules that differ from the builtin rules but do not agree enough yet, for a person to decide.</param>
public sealed record RuleCandidatesResponse(
    int BuiltinVersion,
    IReadOnlyList<ConsensusCandidate> Consensus,
    IReadOnlyList<UnknownAppCandidate> Unknown,
    IReadOnlyList<UnknownAppCandidate>? Guessed = null,
    IReadOnlyList<ConsensusCandidate>? Signals = null);