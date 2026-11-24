using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Ui;

namespace DeskPal.Client.Providers;

/// <summary>Where a plugin publishes. Only the capabilities the plugin currently owns reach the activity bus.</summary>
public sealed class SignalSink(Action<ActivitySignal> publish)
{
    public void Publish(ActivitySignal signal) => publish(signal);
}

public enum ProviderHealth
{
    Starting,
    Running,

    /// <summary>Not delivering. Its capabilities go to the next plugin in the list until it works again.</summary>
    Failed,
}

/// <summary>
/// A plugin: something that supplies some of the activity facts (window, site, idle, lock, media). The built-in
/// collectors of each platform are one (<see cref="OfficialProvider"/>), a program in the plugins folder is another
/// (<see cref="ExternalProvider"/>). <see cref="ProviderHost"/> runs the enabled ones and decides whose facts are used.
/// </summary>
public interface IActivityProvider
{
    /// <summary>official:&lt;platform&gt; or user:&lt;file name&gt;. Stable; stored in the settings.</summary>
    string Id { get; }

    string Name { get; }

    string? Version { get; }

    bool Official { get; }

    /// <summary>What it supplies now. May change while it runs (a plugin's hello line, a desktop without a window source).</summary>
    IReadOnlySet<ProviderCapability> Provides { get; }

    ProviderHealth Health { get; }

    IReadOnlyList<PluginNote> Notes { get; }

    /// <summary>Ids of the buttons it offers in Settings.</summary>
    IReadOnlyList<string> Actions { get; }

    /// <summary>Raised from any thread when anything above changed.</summary>
    event Action? Changed;

    /// <summary>Supplies facts to <paramref name="sink"/> until cancelled. Called again after being turned off and on.</summary>
    Task RunAsync(SignalSink sink, CancellationToken cancellation);

    Task<CommandResult> RunActionAsync(string action);
}
