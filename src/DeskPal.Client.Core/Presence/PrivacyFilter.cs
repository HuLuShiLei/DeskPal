using DeskPal.Shared.Presence;

namespace DeskPal.Client.Core.Presence;

public static class PrivacyFilter
{
    /// <summary>Produces what a Room is allowed to see, based on this member's privacy profile in that Room.</summary>
    public static PresenceSnapshot Apply(CommittedPresence committed, PrivacyProfile profile)
    {
        var presence = committed.Presence;
        var snapshot = new PresenceSnapshot
        {
            State = presence.State,
            Since = committed.Since,
        };

        if (presence.Category == ActivityCategory.None)
        {
            return snapshot;
        }

        return profile.LevelFor(presence.Category) switch
        {
            PrivacyLevel.Detail => snapshot with { Category = presence.Category, AppDisplayName = presence.AppDisplayName, Detail = presence.Detail },
            PrivacyLevel.Category => snapshot with { Category = presence.Category, AppDisplayName = presence.AppDisplayName },
            _ => snapshot,
        };
    }
}