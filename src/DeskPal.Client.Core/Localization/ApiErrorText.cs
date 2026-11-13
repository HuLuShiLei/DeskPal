using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Localization;

/// <summary>Turns a Server error code into text in the current UI language. The Server never sends user-facing text.</summary>
public static class ApiErrorText
{
    public static string ResourceKey(string code) => "ApiError_" + code.Replace('.', '_');

    /// <returns>null when the code is unknown, e.g. a newer Server added it.</returns>
    public static string? Describe(string? code, IReadOnlyDictionary<string, string>? args = null)
    {
        if (string.IsNullOrEmpty(code) || Strings.ResourceManager.GetString(ResourceKey(code), UiLanguage.Culture) is not { } text)
        {
            return null;
        }

        if (args is not null)
        {
            foreach (var (name, value) in args)
            {
                text = text.Replace("{" + name + "}", value);
            }
        }

        return text;
    }
}