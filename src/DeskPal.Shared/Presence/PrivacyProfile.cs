namespace DeskPal.Shared.Presence;

public sealed class PrivacyProfile
{
    public Dictionary<ActivityCategory, PrivacyLevel> Levels { get; set; } = new();

    public PrivacyLevel LevelFor(ActivityCategory category) =>
        Levels.TryGetValue(category, out var level) ? level : DefaultLevel(category);

    public static PrivacyLevel DefaultLevel(ActivityCategory category) => category switch
    {
        ActivityCategory.Work or ActivityCategory.Meeting or ActivityCategory.Communication => PrivacyLevel.StateOnly,
        _ => PrivacyLevel.Category,
    };

    public static PrivacyProfile CreateDefault() => new();
}