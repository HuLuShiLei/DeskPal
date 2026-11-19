using System.Text.Json;
using DeskPal.Shared;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Services;

/// <summary>Local copy of the builtin rule library so classification works before the server answers.</summary>
public sealed class BuiltinRuleCache
{
    private static readonly string FilePath = Path.Combine(SettingsStore.Directory, "builtin-rules.json");

    public RuleSetDto Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<RuleSetDto>(File.ReadAllText(FilePath), DeskPalJson.Options) ?? Empty;
            }
        }
        catch (JsonException)
        {
        }

        return Empty;
    }

    public void Save(RuleSetDto set)
    {
        Directory.CreateDirectory(SettingsStore.Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(set, DeskPalJson.Options));
    }

    private static RuleSetDto Empty => new(0, []);
}