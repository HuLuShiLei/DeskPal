using System.Text.Json;
using DeskPal.Shared;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Classification;

/// <summary>Personal overrides, saved only on this machine.</summary>
public sealed class PersonalRuleStore(string filePath)
{
    private readonly Lock _lock = new();

    public IReadOnlyList<RuleDto> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<RuleDto>>(File.ReadAllText(filePath), DeskPalJson.Options) ?? [];
        }
    }

    public IReadOnlyList<RuleDto> Upsert(RuleDto rule)
    {
        lock (_lock)
        {
            var pattern = RulePattern.Normalize(rule.Kind, rule.Pattern);
            var rules = Load().Where(r => r.Kind != rule.Kind || RulePattern.Normalize(r.Kind, r.Pattern) != pattern).ToList();
            rules.Insert(0, rule with { Pattern = pattern });
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(rules, DeskPalJson.Options));
            return rules;
        }
    }
}