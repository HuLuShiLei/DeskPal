namespace DeskPal.Shared.Api;

/// <summary>
/// Settings that follow the account. A field is null until it is set on any computer; a PATCH changes only the fields it
/// carries. <paramref name="MemberOrder"/> is merged per room, and an empty list puts the room back in the default order.
/// </summary>
/// <param name="SwitchDelaySeconds">How long a new status must last before it is shared with the room.</param>
/// <param name="MemberOrder">Per room, the order the user dragged the other members into.</param>
public sealed record UserPreferences(
    int? SwitchDelaySeconds = null,
    string? Theme = null,
    string? Language = null,
    string? CloseAction = null,
    bool? Topmost = null,
    Dictionary<Guid, List<Guid>>? MemberOrder = null);

public static class AccountPreferences
{
    public static readonly int[] SwitchDelayOptions = [0, 3, 5, 10, 30];

    public static readonly string[] Themes = ["system", "light", "dark"];

    public static readonly string[] Languages = ["system", "zh-CN", "en"];

    public static readonly string[] CloseActions = ["ask", "minimize", "tray", "quit"];

    public const int MaxOrderedRooms = 100;

    public const int MaxOrderedMembers = 100;

    public static bool IsValid(UserPreferences p) =>
        (p.SwitchDelaySeconds is null || SwitchDelayOptions.Contains(p.SwitchDelaySeconds.Value))
        && (p.Theme is null || Themes.Contains(p.Theme))
        && (p.Language is null || Languages.Contains(p.Language))
        && (p.CloseAction is null || CloseActions.Contains(p.CloseAction))
        && (p.MemberOrder is null || (p.MemberOrder.Count <= MaxOrderedRooms && p.MemberOrder.Values.All(v => v is not null && v.Count <= MaxOrderedMembers)));

    /// <param name="keepRoom">Rooms the user is still in; orders for other rooms are dropped.</param>
    public static UserPreferences Merge(UserPreferences stored, UserPreferences patch, Func<Guid, bool> keepRoom)
    {
        Dictionary<Guid, List<Guid>>? order = stored.MemberOrder;
        if (patch.MemberOrder is not null)
        {
            order = new Dictionary<Guid, List<Guid>>(stored.MemberOrder ?? []);
            foreach (var (room, members) in patch.MemberOrder)
            {
                if (members.Count == 0)
                {
                    order.Remove(room);
                }
                else
                {
                    order[room] = members.Distinct().ToList();
                }
            }
        }

        order = order?.Where(o => keepRoom(o.Key)).ToDictionary(o => o.Key, o => o.Value);
        return new UserPreferences(
            patch.SwitchDelaySeconds ?? stored.SwitchDelaySeconds,
            patch.Theme ?? stored.Theme,
            patch.Language ?? stored.Language,
            patch.CloseAction ?? stored.CloseAction,
            patch.Topmost ?? stored.Topmost,
            order);
    }
}