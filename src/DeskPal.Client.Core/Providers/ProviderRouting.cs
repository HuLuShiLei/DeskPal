namespace DeskPal.Client.Core.Providers;

/// <summary>One plugin as the router sees it.</summary>
/// <param name="Healthy">Starting or running. A plugin that failed gives its capabilities to the next one until it works again.</param>
public sealed record RoutingEntry(string Id, bool Enabled, bool Healthy, IReadOnlySet<ProviderCapability> Provides);

/// <summary>
/// Decides which plugin supplies each capability. The plugin list is ordered like a mod list: for every capability the
/// first enabled, healthy plugin that supplies it wins, and the plugins below it are not heard for that capability.
/// So a user's plugin placed above the built-in one replaces exactly what it supplies and nothing else.
/// </summary>
public static class ProviderRouting
{
    public static IReadOnlyDictionary<ProviderCapability, string> Route(IEnumerable<RoutingEntry> ordered)
    {
        var owners = new Dictionary<ProviderCapability, string>();
        foreach (var entry in ordered)
        {
            if (!entry.Enabled || !entry.Healthy)
            {
                continue;
            }

            foreach (var capability in entry.Provides)
            {
                owners.TryAdd(capability, entry.Id);
            }
        }

        return owners;
    }
}
