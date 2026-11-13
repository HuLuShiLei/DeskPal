using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Localization;

/// <summary>
/// App names from the builtin rules in the current UI language. Clients send and store the default name;
/// it is translated only where it is shown, so members on another language see it in theirs.
/// </summary>
public static class AppNames
{
    private static volatile IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _names =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    public static void SetBuiltin(IEnumerable<RuleDto> rules)
    {
        var names = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (rule.Name is { Length: > 0 } name && rule.Names is { Count: > 0 } translations)
            {
                names.TryAdd(name, translations);
            }
        }

        _names = names;
    }

    public static string Localize(string name) =>
        _names.TryGetValue(name, out var translations)
        && translations.TryGetValue(UiLanguage.Current, out var translated)
        && translated.Length > 0
            ? translated
            : name;

    public static string? LocalizeOrNull(string? name) => name is null ? null : Localize(name);
}
