using System.Text.Json;
using DeskPal.Server.Data;
using DeskPal.Shared;
using DeskPal.Shared.Api;
using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Features.Rules;

/// <summary>Replaces the builtin rule table when builtin-rules.json carries a different version than the database.</summary>
public static class BuiltinRuleSeeder
{
    private const string VersionKey = "builtin-rules-version";

    public static async Task<int> GetStoredVersionAsync(DeskPalDbContext db)
    {
        var meta = await db.AppMeta.FindAsync(VersionKey);
        return meta is null ? 0 : int.Parse(meta.Value);
    }

    public static RuleDto ToDto(BuiltinRule rule) => new(
        rule.Kind,
        rule.Pattern,
        rule.Category,
        rule.Name,
        rule.Names is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(rule.Names, DeskPalJson.Options));

    public static async Task SeedAsync(DeskPalDbContext db, string jsonPath, ILogger logger)
    {
        await using var stream = File.OpenRead(jsonPath);
        var set = await JsonSerializer.DeserializeAsync<RuleSetDto>(stream, DeskPalJson.Options)
            ?? throw new InvalidOperationException($"Cannot read {jsonPath}");

        if (await GetStoredVersionAsync(db) == set.Version)
        {
            return;
        }

        await db.BuiltinRules.ExecuteDeleteAsync();
        var seen = new HashSet<(RuleKind, string)>();
        foreach (var rule in set.Rules)
        {
            var pattern = RulePattern.Normalize(rule.Kind, rule.Pattern);
            if (seen.Add((rule.Kind, pattern)))
            {
                db.BuiltinRules.Add(new BuiltinRule
                {
                    Kind = rule.Kind,
                    Pattern = pattern,
                    Category = rule.Category,
                    Name = rule.Name,
                    Names = rule.Names is { Count: > 0 } names ? JsonSerializer.Serialize(names, DeskPalJson.Options) : null,
                });
            }
        }

        var meta = await db.AppMeta.FindAsync(VersionKey);
        if (meta is null)
        {
            db.AppMeta.Add(new AppMeta { Key = VersionKey, Value = set.Version.ToString() });
        }
        else
        {
            meta.Value = set.Version.ToString();
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Builtin rules updated to version {Version}, {Count} rules", set.Version, seen.Count);
    }
}