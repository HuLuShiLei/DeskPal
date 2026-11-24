using DeskPal.Client.Core.Localization;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Services;
using DeskPal.Client.Ui;
using DeskPal.Shared.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Providers;

/// <summary>
/// Runs the plugin list. Every activity fact on every platform comes through here: the platform's official plugin
/// (its collectors) and the user's own plugins are treated alike. The list is ordered like a mod list; for each
/// capability the first enabled, healthy plugin that supplies it is used (<see cref="ProviderRouting"/>) and only
/// that plugin's facts reach the <see cref="ActivityBus"/>.
/// When a capability changes hands, the new owner's last fact is published at once (collectors only publish on
/// change), or the capability is cleared when nobody supplies it.
/// New official plugins join the list switched on; new files in the plugins folder join it at the top switched off,
/// so nothing the user dropped into the folder runs until they turn it on.
/// </summary>
public sealed class ProviderHost : BackgroundService, IProviderControl
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<IActivityProvider> _officials;
    private readonly ActivityBus _bus;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger<ProviderHost> _logger;
    private readonly Lock _lock = new();

    /// <summary>In list order.</summary>
    private List<Slot> _slots = [];
    private readonly CapabilityRouter _router = new();
    private readonly DetailRouter _details = new();
    private CancellationToken _stopping;
    private bool _started;

    public ProviderHost(
        IEnumerable<IActivityProvider> officials,
        ActivityBus bus,
        AppSettings settings,
        SettingsStore store,
        ILoggerFactory loggers)
    {
        _officials = officials.ToList();
        _bus = bus;
        _settings = settings;
        _store = store;
        _loggers = loggers;
        _logger = loggers.CreateLogger<ProviderHost>();
        foreach (var official in _officials)
        {
            official.Changed += OnProviderChanged;
        }
    }

    public event Action? Changed;

    public string Folder
    {
        get
        {
            var folder = Path.Combine(SettingsStore.Directory, "plugins");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public PluginsView View
    {
        get
        {
            lock (_lock)
            {
                var items = _slots.Select(slot =>
                {
                    var p = slot.Provider;
                    var provides = ProviderCapabilities.Known.Where(p.Provides.Contains).ToList();
                    var owns = provides
                        .Where(c => c == ProviderCapability.Detail
                            ? _details.ActiveId == p.Id
                            : _router.Owners.TryGetValue(c, out var owner) && owner == p.Id)
                        .Select(c => c.Name())
                        .ToList();
                    var state = !slot.Enabled ? "disabled" : p.Health switch
                    {
                        ProviderHealth.Running => "running",
                        ProviderHealth.Failed => "failed",
                        _ => "starting",
                    };
                    var apps = p.Provides.Contains(ProviderCapability.Detail) ? p.Apps.Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
                    return new PluginView(p.Id, p.Name, p.Version, p.Official, slot.Enabled, provides.Select(c => c.Name()).ToList(), owns, state, p.Notes, p.Actions, apps);
                }).ToList();
                var unprovided = ProviderCapabilities.All.Where(c => !_router.Owners.ContainsKey(c)).Select(c => c.Name()).ToList();
                return new PluginsView(items, unprovided, Folder);
            }
        }
    }

    public CommandResult SetEnabled(string id, bool enabled)
    {
        lock (_lock)
        {
            var slot = _slots.FirstOrDefault(s => s.Provider.Id == id);
            if (slot is null)
            {
                return CommandResult.Fail(UiLanguage.Format(Strings.Plugin_NotFound, id));
            }

            if (slot.Enabled == enabled)
            {
                return CommandResult.Success;
            }

            _logger.LogInformation("Plugin {Id} turned {State}", id, enabled ? "on" : "off");
            slot.Enabled = enabled;
            if (enabled)
            {
                Start(slot);
            }
            else
            {
                Stop(slot);
            }

            SaveLocked();
            RerouteLocked();
        }

        Changed?.Invoke();
        return CommandResult.Success;
    }

    public CommandResult Move(string id, int offset)
    {
        lock (_lock)
        {
            var index = _slots.FindIndex(s => s.Provider.Id == id);
            if (index < 0)
            {
                return CommandResult.Fail(UiLanguage.Format(Strings.Plugin_NotFound, id));
            }

            var target = Math.Clamp(index + offset, 0, _slots.Count - 1);
            if (target == index)
            {
                return CommandResult.Success;
            }

            var slots = _slots.ToList();
            var slot = slots[index];
            slots.RemoveAt(index);
            slots.Insert(target, slot);
            _slots = slots;
            SaveLocked();
            RerouteLocked();
        }

        Changed?.Invoke();
        return CommandResult.Success;
    }

    public Task<CommandResult> RunActionAsync(string id, string action)
    {
        IActivityProvider? provider;
        lock (_lock)
        {
            provider = _slots.FirstOrDefault(s => s.Provider.Id == id)?.Provider;
        }

        return provider is null
            ? Task.FromResult(CommandResult.Fail(UiLanguage.Format(Strings.Plugin_NotFound, id)))
            : provider.RunActionAsync(action);
    }

    public void Refresh()
    {
        var changed = false;
        lock (_lock)
        {
            if (_started)
            {
                changed = SyncLocked();
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        lock (_lock)
        {
            _stopping = stoppingToken;
            _started = true;
            SyncLocked();
            foreach (var slot in _slots.Where(s => s.Enabled))
            {
                Start(slot);
            }

            RerouteLocked();
        }

        Changed?.Invoke();
        try
        {
            using var timer = new PeriodicTimer(ScanInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
        }

        Task[] running;
        lock (_lock)
        {
            running = _slots.Select(s => s.Task).OfType<Task>().ToArray();
            foreach (var slot in _slots)
            {
                Stop(slot);
            }
        }

        await Task.WhenAny(Task.WhenAll(running), Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    /// <summary>
    /// Brings the list in line with the settings, the official plugins and the plugins folder. Returns whether
    /// anything changed. A file that disappeared is dropped from the list and stopped; its settings row is kept so it
    /// comes back where it was.
    /// </summary>
    private bool SyncLocked()
    {
        var files = PluginFiles.Find(Folder);
        var known = _slots.ToDictionary(s => s.Provider.Id);
        var rows = _settings.Plugins.Where(r => r.Id.Length > 0).GroupBy(r => r.Id).Select(g => g.First()).ToList();
        var rowIds = rows.Select(r => r.Id).ToHashSet();
        var settingsChanged = false;

        // Files the user added since last time go to the top of the list, switched off.
        var added = files.Select(ExternalProvider.IdFor).Where(id => !rowIds.Contains(id)).ToList();
        if (added.Count > 0)
        {
            rows.InsertRange(0, added.Select(id => new PluginSetting { Id = id, Enabled = false }));
            settingsChanged = true;
        }

        // Official plugins join at the bottom, switched on.
        foreach (var official in _officials.Where(o => !rowIds.Contains(o.Id)))
        {
            rows.Add(new PluginSetting { Id = official.Id, Enabled = true });
            settingsChanged = true;
        }

        var slots = new List<Slot>();
        foreach (var row in rows)
        {
            if (known.TryGetValue(row.Id, out var existing))
            {
                if (existing.Provider is ExternalProvider external && !files.Contains(external.File))
                {
                    continue;
                }

                slots.Add(existing);
                continue;
            }

            IActivityProvider? provider = _officials.FirstOrDefault(o => o.Id == row.Id);
            if (provider is null && row.Id.StartsWith("user:", StringComparison.Ordinal) && files.Contains(row.Id[5..]))
            {
                provider = CreateExternal(row);
            }

            if (provider is not null)
            {
                slots.Add(new Slot(provider) { Enabled = row.Enabled });
            }
        }

        var removed = _slots.Where(s => !slots.Contains(s)).ToList();
        foreach (var slot in removed)
        {
            _logger.LogInformation("Plugin {Id} is gone from the plugins folder", slot.Provider.Id);
            Stop(slot);
        }

        var listChanged = removed.Count > 0 || !slots.SequenceEqual(_slots);
        _slots = slots;
        if (settingsChanged)
        {
            _settings.Plugins = rows;
            Save();
        }

        foreach (var slot in slots.Where(s => s.Enabled && s.Task is null && _started && !_stopping.IsCancellationRequested))
        {
            Start(slot);
        }

        if (listChanged || settingsChanged)
        {
            RerouteLocked();
        }

        return listChanged || settingsChanged;
    }

    private ExternalProvider CreateExternal(PluginSetting row)
    {
        var knownApps = row.Apps is { } appNames ? new HashSet<string>(appNames, StringComparer.OrdinalIgnoreCase) : null;
        HashSet<ProviderCapability>? known = null;
        if (row.Provides is { } names)
        {
            known = [];
            foreach (var name in names)
            {
                if (ProviderCapabilities.TryParse(name, out var capability))
                {
                    known.Add(capability);
                }
            }
        }

        var provider = new ExternalProvider(
            Path.Combine(Folder, row.Id[5..]),
            known,
            knownApps,
            () => TimeSpan.FromMinutes(AccountIdle.Normalize(_settings.IdleMinutes)),
            _loggers.CreateLogger("DeskPal.UserPlugin"));
        provider.Changed += OnProviderChanged;
        provider.ProvidesAnnounced += (provides, apps) => RememberProvides(provider.Id, provides, apps);
        return provider;
    }

    private void RememberProvides(string id, IReadOnlySet<ProviderCapability> provides, IReadOnlySet<string> apps)
    {
        lock (_lock)
        {
            _settings.Plugins = _settings.Plugins
                .Select(r => r.Id != id ? r : new PluginSetting
                {
                    Id = r.Id,
                    Enabled = r.Enabled,
                    Provides = ProviderCapabilities.Known.Where(provides.Contains).Select(c => c.Name()).ToList(),
                    Apps = apps.Count == 0 ? null : apps.Order(StringComparer.OrdinalIgnoreCase).ToList(),
                })
                .ToList();
            Save();
        }
    }

    private void Start(Slot slot)
    {
        if (slot.Task is not null || !_started)
        {
            return;
        }

        var run = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
        var provider = slot.Provider;
        var sink = new SignalSink(signal => OnSignal(provider.Id, signal));
        slot.Run = run;
        slot.Task = Task.Run(async () =>
        {
            try
            {
                await provider.RunAsync(sink, run.Token);
            }
            catch (OperationCanceledException) when (run.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin {Id} stopped with an error", provider.Id);
            }
        }, CancellationToken.None);
    }

    private void Stop(Slot slot)
    {
        if (slot.Run is null)
        {
            return;
        }

        slot.Run.Cancel();
        var run = slot.Run;
        slot.Task?.ContinueWith(_ => run.Dispose(), TaskScheduler.Default);
        slot.Run = null;
        slot.Task = null;
        _router.Forget(slot.Provider.Id);
        if (_details.Forget(slot.Provider.Id) is { } window)
        {
            _bus.Publish(window);
        }
    }

    private void OnSignal(string id, ActivitySignal signal)
    {
        bool detailMoved;
        lock (_lock)
        {
            var active = _details.ActiveId;
            if (signal is DetailSignal detail)
            {
                if (_details.Report(id, detail) is { } window)
                {
                    _bus.Publish(window);
                }
            }
            else if (_router.Report(id, signal) is { } owned)
            {
                _bus.Publish(owned is ForegroundSignal foreground ? _details.Foreground(foreground) : owned);
            }

            detailMoved = active != _details.ActiveId;
        }

        if (detailMoved)
        {
            Changed?.Invoke();
        }
    }

    private void OnProviderChanged()
    {
        lock (_lock)
        {
            RerouteLocked();
        }

        Changed?.Invoke();
    }

    /// <summary>Works out the owners again and hands each capability that moved to its new owner.</summary>
    private void RerouteLocked()
    {
        var moves = _router.Reroute(_slots.Select(s => new RoutingEntry(
            s.Provider.Id,
            s.Enabled,
            s.Provider.Health != ProviderHealth.Failed,
            s.Provider.Provides)));
        foreach (var (capability, owner, signal) in moves)
        {
            _logger.LogInformation("{Capability} now comes from {Owner}", capability.Name(), owner ?? "(nobody)");
            if (signal is not null)
            {
                _bus.Publish(signal is ForegroundSignal foreground ? _details.Foreground(foreground) : signal);
            }
        }

        var window = _details.Update(_slots
            .Where(s => s.Provider.Provides.Contains(ProviderCapability.Detail))
            .Select(s => new DetailEntry(s.Provider.Id, s.Enabled, s.Provider.Health != ProviderHealth.Failed, s.Provider.Apps)));
        if (window is not null)
        {
            _bus.Publish(window);
        }
    }

    private void SaveLocked()
    {
        var rows = _settings.Plugins.ToDictionary(r => r.Id);
        var ordered = _slots.Select(s => new PluginSetting
        {
            Id = s.Provider.Id,
            Enabled = s.Enabled,
            Provides = rows.TryGetValue(s.Provider.Id, out var row) ? row.Provides : null,
            Apps = row?.Apps,
        }).ToList();

        // Rows of files that are missing right now keep their place at the end.
        ordered.AddRange(_settings.Plugins.Where(r => _slots.All(s => s.Provider.Id != r.Id)));
        _settings.Plugins = ordered;
        Save();
    }

    private void Save()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saving the plugin list failed");
        }
    }

    private sealed class Slot(IActivityProvider provider)
    {
        public IActivityProvider Provider { get; } = provider;

        public bool Enabled { get; set; }

        public CancellationTokenSource? Run { get; set; }

        public Task? Task { get; set; }
    }
}
