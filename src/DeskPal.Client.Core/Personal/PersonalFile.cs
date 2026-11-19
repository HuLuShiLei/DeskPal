using System.Text.Json;
using System.Text.Json.Serialization;
using DeskPal.Shared;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Personal;

/// <summary>
/// Personal data as written to an exported file and to a sync folder: the rules, the away time and the preferences. Not
/// encrypted.
/// </summary>
/// <param name="PreferencesUpdatedAt">When the away time or a preference last changed on the computer that wrote the file.</param>
public sealed record PersonalFile(
    string Format,
    int Version,
    DateTimeOffset SavedAt,
    IReadOnlyList<UserRuleDto> Rules,
    IReadOnlyList<NoTitleRuleDto> NoTitleRules,
    IReadOnlyList<PersonalTombstone>? Deleted = null,
    UserPreferences? Preferences = null,
    int? IdleMinutes = null,
    DateTimeOffset? PreferencesUpdatedAt = null)
{
    public const string FormatName = "deskpal-personal";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new(DeskPalJson.Options) { WriteIndented = true };

    [JsonIgnore]
    public PersonalData Data => new PersonalData(Rules ?? [], NoTitleRules ?? [], Deleted).Sanitize();

    public static PersonalFile Create(PersonalData data, UserPreferences? preferences, int? idleMinutes, DateTimeOffset? preferencesUpdatedAt, DateTimeOffset now) =>
        new(FormatName, CurrentVersion, now, data.Rules, data.NoTitleRules, data.Tombstones, preferences, idleMinutes, preferencesUpdatedAt);

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    /// <returns>null when the bytes are not a personal data file.</returns>
    public static PersonalFile? Parse(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<PersonalFile>(bytes, DeskPalJson.Options) is { Format: FormatName } file ? file : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}