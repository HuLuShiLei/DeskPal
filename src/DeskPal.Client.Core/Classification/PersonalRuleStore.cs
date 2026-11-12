using System.Text.Json;
using DeskPal.Shared;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Classification;

/// <summary>
/// Local copy of the account's personal rules, so classification works before the server answers.
/// The server is the source of truth; the copy is ignored when it belongs to another account.
/// </summary>
public sealed class PersonalRuleStore(string filePath)
{
    private readonly Lock _lock = new();

    public IReadOnlyList<UserRuleDto> Load(Guid userId)
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(filePath) &&
                    JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(filePath), DeskPalJson.Options) is { } file &&
                    file.UserId == userId)
                {
                    return file.Rules ?? [];
                }
            }
            catch (JsonException)
            {
            }

            return [];
        }
    }

    public void Save(Guid userId, IReadOnlyList<UserRuleDto> rules)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(new CacheFile(userId, rules), DeskPalJson.Options));
        }
    }

    /// <summary>Newest first, so the latest change wins if two rules ever share a pattern.</summary>
    public static RuleSet ToRuleSet(IEnumerable<UserRuleDto> rules) =>
        new(rules.OrderByDescending(r => r.UpdatedAt).Select(r => new RuleDto(r.Kind, r.Pattern, r.Category, r.Name)));

    private sealed record CacheFile(Guid UserId, IReadOnlyList<UserRuleDto>? Rules);
}
