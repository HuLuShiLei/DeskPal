using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Providers;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Providers;

/// <summary>
/// The plugin that comes with DeskPal on a platform: the platform's collectors, run as one unit. Shown in the plugin
/// list like any other plugin and can be turned off or outranked, but runs inside DeskPal rather than as a program.
/// Each run creates the collectors afresh and hands them the run's <see cref="SignalSink"/> (they take it in their
/// constructor in place of the activity bus), so turning the plugin off really stops them.
/// </summary>
public class OfficialProvider(
    IServiceProvider services,
    string id,
    string name,
    IReadOnlySet<ProviderCapability> provides,
    IReadOnlyList<Type> parts) : IActivityProvider
{
    private readonly ILogger _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DeskPal.OfficialPlugin");
    private ProviderHealth _health = ProviderHealth.Starting;
    private IReadOnlyList<PluginNote> _failures = [];

    public string Id => id;

    public string Name => name;

    public string? Version => null;

    public bool Official => true;

    public virtual IReadOnlySet<ProviderCapability> Provides => provides;

    public ProviderHealth Health => _health;

    /// <summary>Parts that failed to start, plus what a platform adds.</summary>
    public virtual IReadOnlyList<PluginNote> Notes => _failures;

    public virtual IReadOnlyList<string> Actions => [];

    public event Action? Changed;

    public virtual Task<CommandResult> RunActionAsync(string action) => Task.FromResult(CommandResult.Fail(Strings.Plugin_UnknownAction));

    public async Task RunAsync(SignalSink sink, CancellationToken cancellation)
    {
        var started = new List<IHostedService>();
        var failures = new List<PluginNote>();
        foreach (var type in parts)
        {
            try
            {
                var part = (IHostedService)ActivatorUtilities.CreateInstance(services, type, sink);
                await part.StartAsync(cancellation);
                started.Add(part);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One collector that cannot start (a missing system service) does not take the others down.
                _logger.LogError(ex, "{Part} did not start", type.Name);
                failures.Add(new PluginNote("plugin.partFailed", $"{type.Name}: {ex.Message}", "warn"));
            }
        }

        _failures = failures;
        _health = started.Count > 0 ? ProviderHealth.Running : ProviderHealth.Failed;
        RaiseChanged();
        try
        {
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            for (var i = started.Count - 1; i >= 0; i--)
            {
                try
                {
                    await started[i].StopAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{Part} did not stop cleanly", started[i].GetType().Name);
                }

                (started[i] as IDisposable)?.Dispose();
            }

            _health = ProviderHealth.Starting;
            _failures = [];
            OnStopped();
        }
    }

    /// <summary>Called after the parts stopped, when the plugin was turned off or DeskPal exits.</summary>
    protected virtual void OnStopped()
    {
    }

    protected void RaiseChanged() => Changed?.Invoke();
}

public static class ProviderServiceExtensions
{
    /// <summary>
    /// Registers the plugin host, the settings page's view of it, and nothing else; platforms add their official
    /// plugin with <see cref="AddOfficialProvider"/> or by registering their own <see cref="IActivityProvider"/>.
    /// </summary>
    public static IServiceCollection AddPluginHost(this IServiceCollection services)
    {
        services.AddSingleton<ProviderHost>();
        services.AddSingleton<IProviderControl>(sp => sp.GetRequiredService<ProviderHost>());
        services.AddHostedService(sp => sp.GetRequiredService<ProviderHost>());
        return services;
    }

    /// <summary>The platform's collectors as one built-in plugin. Each type takes a <see cref="SignalSink"/> to publish to.</summary>
    public static IServiceCollection AddOfficialProvider(
        this IServiceCollection services, string id, string name, IReadOnlySet<ProviderCapability> provides, params Type[] parts)
    {
        services.AddSingleton<IActivityProvider>(sp => new OfficialProvider(sp, id, name, provides, parts));
        return services;
    }
}
