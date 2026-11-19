using DeskPal.Shared.Api;

namespace DeskPal.Client.Core.Personal;

public static class PersonalMerge
{
    /// <summary>How long a deletion is remembered; a computer that has not synced for longer may bring the rule back.</summary>
    public static readonly TimeSpan KeepDeleted = TimeSpan.FromDays(90);

    /// <summary>
    /// Each rule is taken from whichever side changed it last; deleting it counts as a change. On a tie
    /// <paramref name="mine"/> wins.
    /// </summary>
    public static PersonalData Merge(PersonalData mine, PersonalData theirs, DateTimeOffset now)
    {
        var entries = new Dictionary<string, Entry>();
        foreach (var side in new[] { mine, theirs })
        {
            foreach (var rule in side.Rules)
            {
                Offer(entries, PersonalData.Key(rule), new Entry(rule.UpdatedAt, rule, null));
            }

            foreach (var rule in side.NoTitleRules)
            {
                Offer(entries, PersonalData.Key(rule), new Entry(rule.UpdatedAt, null, rule));
            }

            foreach (var gone in side.Tombstones)
            {
                Offer(entries, gone.Key, new Entry(gone.DeletedAt, null, null));
            }
        }

        var values = entries.ToList();
        return new PersonalData(
            values.Select(e => e.Value.Rule).OfType<UserRuleDto>().OrderByDescending(r => r.UpdatedAt).ToList(),
            values.Select(e => e.Value.NoTitle).OfType<NoTitleRuleDto>().OrderBy(r => r.UpdatedAt).ToList(),
            values.Where(e => e.Value is { Rule: null, NoTitle: null } && now - e.Value.At < KeepDeleted)
                .Select(e => new PersonalTombstone(e.Key, e.Value.At))
                .ToList());
    }

    /// <summary>
    /// Adds the rules of an imported file. A rule in the file replaces the same one here and counts as changed now, so it
    /// reaches the other computers; rules only here are kept.
    /// </summary>
    public static PersonalData Import(PersonalData mine, PersonalData file, DateTimeOffset now)
    {
        var result = mine;
        foreach (var rule in file.Rules)
        {
            result = result.WithRule(rule with { UpdatedAt = now });
        }

        foreach (var rule in file.NoTitleRules)
        {
            result = result.WithNoTitle(rule with { UpdatedAt = now });
        }

        return result;
    }

    private static void Offer(Dictionary<string, Entry> entries, string key, Entry entry)
    {
        if (!entries.TryGetValue(key, out var current) || entry.At > current.At)
        {
            entries[key] = entry;
        }
    }

    private sealed record Entry(DateTimeOffset At, UserRuleDto? Rule, NoTitleRuleDto? NoTitle);
}