using System.Globalization;

namespace DeskPal.Client.Core.Localization;

/// <summary>
/// The language of the client's text. Only Simplified Chinese and English are shipped; any other Windows language gets English.
/// The app sets it once at startup and again when the user picks another language; tests leave it unset and use the thread's UI culture.
/// </summary>
public static class UiLanguage
{
    public const string FollowSystem = "system";
    public const string Chinese = "zh-CN";
    public const string English = "en";

    /// <summary>zh-CN or en, the language the text is in right now.</summary>
    public static string Current => Of(Culture);

    public static CultureInfo Culture => Strings.Culture ?? CultureInfo.CurrentUICulture;

    /// <summary>system, zh-CN or en, as stored in the settings.</summary>
    public static string Normalize(string? setting) => setting is Chinese or English ? setting : FollowSystem;

    public static string Resolve(string? setting, CultureInfo system) => Normalize(setting) switch
    {
        Chinese => Chinese,
        English => English,
        _ => Of(system),
    };

    public static void Apply(string language) => Strings.Culture = CultureInfo.GetCultureInfo(language == English ? English : Chinese);

    public static string Format(string format, params object?[] args) => string.Format(Culture, format, args);

    private static string Of(CultureInfo culture) => culture.TwoLetterISOLanguageName == "zh" ? Chinese : English;
}