using DeskPal.Client.Core.Pipeline;

namespace DeskPal.Client.Core.Providers;

/// <summary>
/// Keeps who owns each capability and the last fact each plugin reported, and says what to publish. Not thread-safe;
/// the plugin host calls it under its lock.
/// </summary>
public sealed class CapabilityRouter
{
    private readonly Dictionary<(string Id, ProviderCapability Capability), ActivitySignal> _last = [];
    private IReadOnlyDictionary<ProviderCapability, string> _owners = new Dictionary<ProviderCapability, string>();

    public IReadOnlyDictionary<ProviderCapability, string> Owners => _owners;

    /// <summary>A plugin reported a fact. Returns it when that plugin owns the capability, so it should be published.</summary>
    public ActivitySignal? Report(string id, ActivitySignal signal)
    {
        var capability = ProviderCapabilities.Of(signal);
        _last[(id, capability)] = signal;
        return _owners.TryGetValue(capability, out var owner) && owner == id ? signal : null;
    }

    /// <summary>Forgets what a plugin reported; for a plugin that was turned off or removed.</summary>
    public void Forget(string id)
    {
        foreach (var key in _last.Keys.Where(k => k.Id == id).ToList())
        {
            _last.Remove(key);
        }
    }

    /// <summary>
    /// Works out the owners again. For each capability that moved, returns the new owner's last fact (collectors only
    /// report changes, so the new owner may have said it long ago) or, when nobody supplies it any more or the new
    /// owner has not said anything yet, the cleared value.
    /// </summary>
    public IReadOnlyList<(ProviderCapability Capability, string? Owner, ActivitySignal? Signal)> Reroute(IEnumerable<RoutingEntry> ordered)
    {
        var owners = ProviderRouting.Route(ordered);
        var moves = new List<(ProviderCapability, string?, ActivitySignal?)>();
        foreach (var capability in ProviderCapabilities.All)
        {
            owners.TryGetValue(capability, out var now);
            _owners.TryGetValue(capability, out var before);
            if (now == before)
            {
                continue;
            }

            var signal = now is not null && _last.TryGetValue((now, capability), out var last) ? last : ProviderCapabilities.Cleared(capability);
            moves.Add((capability, now, signal));
        }

        _owners = owners;
        return moves;
    }
}
