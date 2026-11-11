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
    public static bool IsSlacking(this ActivityCategory category) => category is
        ActivityCategory.Video or ActivityCategory.Music or ActivityCategory.Shopping or
        ActivityCategory.Social or ActivityCategory.News or ActivityCategory.Game or ActivityCategory.Reading;

    public static PresenceState ToState(this ActivityCategory category) => category switch
    {
        ActivityCategory.Work => PresenceState.Working,
        ActivityCategory.Meeting => PresenceState.Meeting,
        ActivityCategory.Communication => PresenceState.Communicating,
        ActivityCategory.None => PresenceState.Other,
        ActivityCategory.Other => PresenceState.Other,
        ActivityCategory.Browsing => PresenceState.Browsing,
        _ => PresenceState.Slacking,
    };
}