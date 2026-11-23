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
    private const string FoldersKey = "builtin-rules-folders";

    public static async Task<int> GetStoredVersionAsync(DeskPalDbContext db)
    {
        var meta = await db.AppMeta.FindAsync(VersionKey);
        return meta is null ? 0 : int.Parse(meta.Value);
    }

    /// <summary>Null until a builtin-rules.json with folders has been seeded.</summary>
    public static async Task<List<FolderRuleDto>?> GetStoredFoldersAsync(DeskPalDbContext db)
    {
        var meta = await db.AppMeta.FindAsync(FoldersKey);
        return meta is null ? null : JsonSerializer.Deserialize<List<FolderRuleDto>>(meta.Value, DeskPalJson.Options);
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

        var folders = (set.Folders ?? [])
            .Select(f => f with { Folder = f.Folder.Replace('/', '\\') })
            .Where(f => f.Folder.Length > 0)
            .DistinctBy(f => f.Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
        await SetMetaAsync(db, VersionKey, set.Version.ToString());
        await SetMetaAsync(db, FoldersKey, JsonSerializer.Serialize(folders, DeskPalJson.Options));
        await db.SaveChangesAsync();
        logger.LogInformation("Builtin rules updated to version {Version}, {Count} rules, {Folders} folders", set.Version, seen.Count, folders.Count);
    }

    private static async Task SetMetaAsync(DeskPalDbContext db, string key, string value)
    {
        var meta = await db.AppMeta.FindAsync(key);
        if (meta is null)
        {
            db.AppMeta.Add(new AppMeta { Key = key, Value = value });
        }
        else
        {
            meta.Value = value;
        }
    }
}