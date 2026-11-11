namespace DeskPal.Shared.Presence;

public enum ActivityCategory
{
    None = 0,
    Work = 1,
    // 2 不再使用。规则表把类别存成整数，后面的值不能往前移。
    Communication = 3,
    Video = 4,
    Music = 5,
    Shopping = 6,
    Social = 7,
    News = 8,
    Game = 9,
    Reading = 10,
    Other = 11,
    Browsing = 12,
}

public static class ActivityCategoryExtensions
{
    public static bool IsSlacking(this ActivityCategory category) => category is
        ActivityCategory.Video or ActivityCategory.Music or ActivityCategory.Shopping or
        ActivityCategory.Social or ActivityCategory.News or ActivityCategory.Game or ActivityCategory.Reading;

    public static PresenceState ToState(this ActivityCategory category) => category switch
    {
        ActivityCategory.Work => PresenceState.Working,
        ActivityCategory.Communication => PresenceState.Communicating,
        ActivityCategory.None => PresenceState.Other,
        ActivityCategory.Other => PresenceState.Other,
        ActivityCategory.Browsing => PresenceState.Browsing,
        _ => PresenceState.Slacking,
    };
}