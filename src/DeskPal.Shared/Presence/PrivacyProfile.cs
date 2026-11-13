namespace DeskPal.Shared.Presence;

public sealed class PrivacyProfile
{
    /// <summary>Days a member may choose to share usage stats for.</summary>
    public static readonly IReadOnlyList<int> StatsDayOptions = [1, 3, 7, 30];

    public const int DefaultStatsDays = 7;

    public Dictionary<ActivityCategory, PrivacyLevel> Levels { get; set; } = new();

    /// <summary>Room members may view my usage stats, at the detail of <see cref="Levels"/>.</summary>
    public bool ShareStats { get; set; }

    /// <summary>How many recent days the stats cover, one of <see cref="StatsDayOptions"/>.</summary>
    public int StatsDays { get; set; } = DefaultStatsDays;

    public PrivacyLevel LevelFor(ActivityCategory category) =>
        Levels.TryGetValue(category, out var level) ? level : DefaultLevel(category);

    public static PrivacyLevel DefaultLevel(ActivityCategory category) => category switch
    {
        ActivityCategory.Work or ActivityCategory.Meeting or ActivityCategory.Communication => PrivacyLevel.StateOnly,
        _ => PrivacyLevel.Category,
    };

    public static PrivacyProfile CreateDefault() => new();

    /// <summary>Saved when a member creates or joins a room: only the state is shared until the member chooses more.</summary>
    public static PrivacyProfile CreateStateOnly() => new()
    {
        Levels = Enum.GetValues<ActivityCategory>()
            .Where(c => c != ActivityCategory.None)
            .ToDictionary(c => c, _ => PrivacyLevel.StateOnly),
    };
}