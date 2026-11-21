namespace DeskPal.Client.Core.Ui;

/// <summary>
/// The order of room members on the status page: online members first, then offline and blocked ones
/// (<see cref="MemberItem.Trailing"/>), each group in the order the user dragged them into. The saved order covers every
/// member, so someone who comes back online returns to the place they had among the online members.
/// </summary>
public static class MemberOrder
{
    /// <summary>How long someone who went offline keeps their place, so a reconnect doesn't move them around.</summary>
    public static readonly TimeSpan OfflineDelay = TimeSpan.FromMinutes(1);

    public static List<MemberItem> Sort(IEnumerable<MemberItem> members, IReadOnlyList<Guid> saved) => members
        .OrderBy(m => m.Trailing)
        .ThenBy(m => Rank(saved, m.UserId))
        .ToList();

    /// <summary>
    /// Folds a reorder of the shown list back into the saved order. Within each group the members take the order they
    /// were dragged into, and the slots that group holds in the saved order stay where they are. Dragging a member into
    /// the other group has no effect.
    /// </summary>
    public static List<Guid> Merge(IReadOnlyList<Guid> saved, IReadOnlyList<MemberItem> shown, IEnumerable<Guid> requested)
    {
        var trailing = shown.Where(m => m.Trailing).Select(m => m.UserId).ToHashSet();
        var all = shown.Select(m => m.UserId).OrderBy(id => Rank(saved, id)).ToList();
        var dragged = requested.Distinct().Where(all.Contains).ToList();
        dragged.AddRange(all.Where(id => !dragged.Contains(id)));

        var leading = new Queue<Guid>(dragged.Where(id => !trailing.Contains(id)));
        var behind = new Queue<Guid>(dragged.Where(trailing.Contains));
        return all.Select(id => trailing.Contains(id) ? behind.Dequeue() : leading.Dequeue()).ToList();
    }

    private static int Rank(IReadOnlyList<Guid> order, Guid userId)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == userId)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}