namespace DeskPal.Shared.Presence;

public enum ActivityCategory
{
    None = 0,
    Work,
    Meeting,
    Communication,
    Video,
    Music,
    Shopping,
    Social,
    News,
    Game,
    Reading,
    Other,
    Browsing,
}

public static class ActivityCategoryExtensions
{
    /// <summary>Kept in the enum so old rules and snapshots still parse. Not produced.</summary>
    public static bool IsRetired(this ActivityCategory category) => category is
        ActivityCategory.Music or ActivityCategory.Shopping or ActivityCategory.Social or
        ActivityCategory.News or ActivityCategory.Reading;

    public static bool IsSlacking(this ActivityCategory category) => category is
        ActivityCategory.Video or ActivityCategory.Game;

    public static PresenceState ToState(this ActivityCategory category) => category switch
    {
        ActivityCategory.Work => PresenceState.Working,
        ActivityCategory.Meeting => PresenceState.Meeting,
        ActivityCategory.Communication => PresenceState.Communicating,
        ActivityCategory.None or ActivityCategory.Other or ActivityCategory.Music => PresenceState.Other,
        ActivityCategory.Browsing or ActivityCategory.Shopping or ActivityCategory.Social or ActivityCategory.News or ActivityCategory.Reading => PresenceState.Browsing,
        _ => PresenceState.Slacking,
    };
}
