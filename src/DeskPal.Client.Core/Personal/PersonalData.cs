using System.Text.Json.Serialization;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Personal;

/// <summary>A rule deleted on this computer, kept for a while so the deletion reaches the other computers through a sync folder.</summary>
public sealed record PersonalTombstone(string Key, DateTimeOffset DeletedAt);

/// <summary>
/// The personal rules and no-title rules of one account. This computer's copy is the one that counts; with account sync
/// on it follows the server.
/// </summary>
public sealed record PersonalData(
    IReadOnlyList<UserRuleDto> Rules,
    IReadOnlyList<NoTitleRuleDto> NoTitleRules,
    IReadOnlyList<PersonalTombstone>? Deleted = null)
{
    public static readonly PersonalData Empty = new([], [], []);

    [JsonIgnore]
    public IReadOnlyList<PersonalTombstone> Tombstones => Deleted ?? [];

    public static string Key(RuleKind kind, string pattern) => $"rule:{kind}:{pattern}";

    public static string Key(UserRuleDto rule) => Key(rule.Kind, rule.Pattern);

    public static string Key(NoTitleRule rule) => $"noTitle:{rule.Kind}:{rule.Pattern.ToLowerInvariant()}";

    public static string Key(NoTitleRuleDto rule) => Key(rule.Rule);

    public PersonalData WithRule(UserRuleDto rule) => this with
    {
        Rules = Rules.Where(r => Key(r) != Key(rule)).Prepend(rule).ToList(),
        Deleted = Tombstones.Where(t => t.Key != Key(rule)).ToList(),
    };

    public PersonalData WithoutRule(RuleKind kind, string pattern, DateTimeOffset now)
    {
        var key = Key(kind, pattern);
        return this with
        {
            Rules = Rules.Where(r => Key(r) != key).ToList(),
            Deleted = Tombstones.Where(t => t.Key != key).Append(new PersonalTombstone(key, now)).ToList(),
        };
    }

    public PersonalData WithNoTitle(NoTitleRuleDto rule) => this with
    {
        NoTitleRules = NoTitleRules.Where(r => Key(r) != Key(rule)).Append(rule).ToList(),
        Deleted = Tombstones.Where(t => t.Key != Key(rule)).ToList(),
    };

    public PersonalData WithoutNoTitle(NoTitleRule rule, DateTimeOffset now)
    {
        var key = Key(rule);
        return this with
        {
            NoTitleRules = NoTitleRules.Where(r => Key(r) != key).ToList(),
            Deleted = Tombstones.Where(t => t.Key != key).Append(new PersonalTombstone(key, now)).ToList(),
        };
    }

    /// <summary>Leaves out what could not have been saved here, e.g. from an edited file or a newer version.</summary>
    public PersonalData Sanitize()
    {
        var rules = new List<UserRuleDto>();
        foreach (var rule in Rules ?? [])
        {
            if (rule is not null && PersonalLimits.CheckRule(rule.Kind, rule.Pattern, rule.Category, out var pattern) is null && rules.All(r => Key(r) != Key(rule.Kind, pattern)))
            {
                rules.Add(rule with { Pattern = pattern, Name = PersonalLimits.CleanName(rule.Name) });
            }
        }

        var noTitle = new List<NoTitleRuleDto>();
        foreach (var item in NoTitleRules ?? [])
        {
            if (item is not null && NoTitleRule.Create(item.Kind, item.Pattern) is { } rule && noTitle.All(r => !r.Rule.SameAs(rule)))
            {
                noTitle.Add(item with { Pattern = rule.Pattern });
            }
        }

        return new PersonalData(
            rules.OrderByDescending(r => r.UpdatedAt).Take(PersonalLimits.MaxRules).ToList(),
            noTitle.OrderBy(r => r.UpdatedAt).Take(PersonalLimits.MaxNoTitleRules).ToList(),
            (Deleted ?? []).Where(t => t is { Key: not null }).DistinctBy(t => t.Key).ToList());
    }
}