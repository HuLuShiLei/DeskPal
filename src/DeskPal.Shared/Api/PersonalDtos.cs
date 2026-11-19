using System.Text.Json.Serialization;
using DeskPal.Shared.Presence;

namespace DeskPal.Shared.Api;

/// <summary>A no-title rule as saved on this computer and, with account sync on, on the server.</summary>
public sealed record NoTitleRuleDto(NoTitleKind Kind, string Pattern, DateTimeOffset UpdatedAt)
{
    [JsonIgnore]
    public NoTitleRule Rule => new(Kind, Pattern);
}

public sealed record UpsertNoTitleRuleRequest(NoTitleKind Kind, string Pattern);

/// <summary>
/// Personal rules, no-title rules, away time and preferences. Sent when account sync is turned on, and answered with what
/// the account keeps after merging.
/// </summary>
public sealed record PersonalDataDto(
    IReadOnlyList<UserRuleDto> Rules,
    IReadOnlyList<NoTitleRuleDto> NoTitleRules,
    UserPreferences? Preferences = null,
    int? IdleMinutes = null);

/// <summary>A personal rule for an app, reported with the usage only to propose builtin rules, while account sync is off.</summary>
public sealed record SharedRuleItem(string Pattern, ActivityCategory Category, string? Name);

/// <summary>Checks shared by the server and the client, which saves the same data on its own while account sync is off.</summary>
public static class PersonalLimits
{
    public const int MaxRules = 500;
    public const int MaxNoTitleRules = 200;
    public const int MaxPatternLength = 200;
    public const int MaxNameLength = 60;

    public static bool IsAssignable(ActivityCategory category) =>
        Enum.IsDefined(category) && category is not (ActivityCategory.None or ActivityCategory.Meeting) && !category.IsRetired();

    public static string? CleanName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }

    /// <returns>An <see cref="ApiErrorCodes"/> code, or null when the rule can be saved as <paramref name="normalized"/>.</returns>
    public static string? CheckRule(RuleKind kind, string? pattern, ActivityCategory category, out string normalized)
    {
        normalized = RulePattern.Normalize(kind, pattern);
        if (!Enum.IsDefined(kind) || normalized.Length is < 1 or > MaxPatternLength)
        {
            return ApiErrorCodes.RulePatternInvalid;
        }

        return IsAssignable(category) ? null : ApiErrorCodes.RuleCategoryRequired;
    }
}