using DeskPal.Client.Core.Pipeline;

namespace DeskPal.Client.Core.Providers;

/// <summary>
/// One kind of fact a plugin can supply. The first five each come from exactly one plugin at a time;
/// <see cref="Detail"/> is per app and is merged into the window by <see cref="DetailRouter"/>.
/// </summary>
public enum ProviderCapability
{
    /// <summary>The window the user is working in.</summary>
    Foreground,

    /// <summary>Host of the page in the foreground browser.</summary>
    Domain,

    /// <summary>Time since the last keyboard or mouse input.</summary>
    Idle,

    /// <summary>Whether the screen is locked.</summary>
    Lock,

    /// <summary>What is playing.</summary>
    Media,

    /// <summary>A more specific title for the window of the apps the plugin names, like the open chat in a messenger.</summary>
    Detail,
}

public static class ProviderCapabilities
{
    /// <summary>The capabilities that have one owner each, in the order the settings page lists them.</summary>
    public static readonly IReadOnlyList<ProviderCapability> All =
        [ProviderCapability.Foreground, ProviderCapability.Domain, ProviderCapability.Idle, ProviderCapability.Lock, ProviderCapability.Media];

    /// <summary>Every capability a plugin can declare.</summary>
    public static readonly IReadOnlyList<ProviderCapability> Known = [.. All, ProviderCapability.Detail];

    /// <summary>What a plugin supplies when its hello line does not say: the window and the browser's site.</summary>
    public static readonly IReadOnlySet<ProviderCapability> PluginDefault =
        new HashSet<ProviderCapability> { ProviderCapability.Foreground, ProviderCapability.Domain };

    /// <summary>The name used in the protocol and the panel: foreground, domain, idle, lock, media, detail.</summary>
    public static string Name(this ProviderCapability capability) => capability switch
    {
        ProviderCapability.Foreground => "foreground",
        ProviderCapability.Domain => "domain",
        ProviderCapability.Idle => "idle",
        ProviderCapability.Lock => "lock",
        ProviderCapability.Media => "media",
        ProviderCapability.Detail => "detail",
        _ => throw new ArgumentOutOfRangeException(nameof(capability)),
    };

    public static bool TryParse(string? name, out ProviderCapability capability)
    {
        foreach (var candidate in Known)
        {
            if (string.Equals(candidate.Name(), name, StringComparison.Ordinal))
            {
                capability = candidate;
                return true;
            }
        }

        capability = default;
        return false;
    }

    /// <summary>The capability a signal belongs to.</summary>
    public static ProviderCapability Of(ActivitySignal signal) => signal switch
    {
        ForegroundSignal => ProviderCapability.Foreground,
        BrowserDomainSignal => ProviderCapability.Domain,
        IdleSignal => ProviderCapability.Idle,
        LockSignal => ProviderCapability.Lock,
        MediaSignal => ProviderCapability.Media,
        DetailSignal => ProviderCapability.Detail,
        _ => throw new ArgumentException($"Unknown signal {signal.GetType().Name}", nameof(signal)),
    };

    /// <summary>
    /// What to publish when nobody supplies a capability any more: no site, not idle, not locked, nothing playing.
    /// Null for the foreground window, which keeps its last value until someone reports a new one.
    /// </summary>
    public static ActivitySignal? Cleared(ProviderCapability capability) => capability switch
    {
        ProviderCapability.Domain => new BrowserDomainSignal(null),
        ProviderCapability.Idle => new IdleSignal(false),
        ProviderCapability.Lock => new LockSignal(false),
        ProviderCapability.Media => new MediaSignal(false, null, null, null),
        _ => null,
    };
}