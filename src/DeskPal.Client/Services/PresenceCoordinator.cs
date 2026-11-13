using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>
/// Folds collector signals into a snapshot, classifies it, debounces the result, and publishes it to the
/// room the user is currently in, after applying that room's privacy profile.
/// </summary>
public sealed class PresenceCoordinator : BackgroundService
{
    private readonly LayeredClassifier _classifier;
    private readonly RoomState _rooms;
    private readonly HubService _hub;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly UnclassifiedUsage _unclassified;
    private readonly ILogger<PresenceCoordinator> _logger;
    private readonly System.Threading.Channels.ChannelReader<ActivitySignal> _signals;
    private readonly PresenceDebouncer _debouncer;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly HashSet<Guid> _offlined = new();
    private readonly Timer _deadline;
    private string? _loggedPresence;

    public PresenceCoordinator(
        ActivityBus bus,
        LayeredClassifier classifier,
        RoomState rooms,
        HubService hub,
        AppSettings settings,
        SettingsStore store,
        PersonalRules personalRules,
        UnclassifiedUsage unclassified,
        ILogger<PresenceCoordinator> logger)
    {
        _classifier = classifier;
        _rooms = rooms;
        _hub = hub;
        _settings = settings;
        _logger = logger;
        _store = store;
        _unclassified = unclassified;
        _signals = bus.Subscribe();
        _debouncer = new PresenceDebouncer(TimeSpan.FromSeconds(settings.StableSeconds), TimeSpan.FromSeconds(settings.StableSeconds));
        _deadline = new Timer(_ => Evaluate());
        _rooms.RoomsReloaded += OnRoomsChangedAsync;
        _rooms.SelectionChanged += OnRoomsChangedAsync;
        personalRules.Changed += Evaluate;
        _hub.Connected += () => PublishActiveAsync(_debouncer.Current);
    }

    public event Action? LocalChanged;

    public ActivitySnapshot Snapshot { get; private set; } = ActivitySnapshot.Empty;

    public ClassificationResult? Classification { get; private set; }

    public LocalPresence? Local { get; private set; }

    public CommittedPresence? Committed => _debouncer.Current;

    public DateTimeOffset? PendingDeadline
    {
        get
        {
            lock (_lock)
            {
                return _debouncer.PendingDeadline;
            }
        }
    }

    public TimeSpan StableFor => _debouncer.StableFor;

    public bool Paused => _settings.Paused;

    public void SetPaused(bool paused)
    {
        _settings.Paused = paused;
        _store.Save(_settings);
        Evaluate();
    }

    public void Reevaluate() => Evaluate();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ticker = TickAsync(stoppingToken);
        await foreach (var signal in _signals.ReadAllAsync(stoppingToken))
        {
            lock (_lock)
            {
                Snapshot = Snapshot.Apply(signal);
            }

            Evaluate();
        }

        await ticker;
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            Evaluate();
        }
    }

    private void Evaluate()
    {
        CommittedPresence? committed;
        DateTimeOffset? deadline;
        DateTimeOffset now;
        lock (_lock)
        {
            Classification = _classifier.Classify(Snapshot);
            Local = PresenceResolver.Resolve(Snapshot, Classification, _settings.Paused);
            now = DateTimeOffset.UtcNow;
            var ready = Snapshot.Foreground is not null || Local.State != PresenceState.Other;
            committed = ready ? _debouncer.Offer(Local, now) : null;
            deadline = _debouncer.PendingDeadline;
            var unrecognized = Classification.Layer == RuleLayer.Fallback && Local.State == PresenceState.Other
                ? CorrectionTarget.From(Snapshot, Classification.AppDisplayName)
                : null;
            _unclassified.Record(unrecognized, now);
        }

        if (deadline is { } due)
        {
            // Commit right when the countdown on the panel ends instead of waiting for the next tick.
            var delay = due - now + TimeSpan.FromMilliseconds(50);
            _deadline.Change(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        LocalChanged?.Invoke();
        if (committed is not null)
        {
            _ = PublishAsync(committed);
        }
    }

    private async Task OnRoomsChangedAsync()
    {
        Reevaluate();
        await PublishActiveAsync(_debouncer.Current);
    }

    private Task PublishAsync(CommittedPresence committed) => PublishActiveAsync(committed);

    private async Task PublishActiveAsync(CommittedPresence? committed)
    {
        await _publishGate.WaitAsync();
        try
        {
            var selected = _rooms.SelectedRoom?.Id;
            var memberIds = _rooms.Rooms.Select(r => r.Id).ToHashSet();
            _offlined.RemoveWhere(id => !memberIds.Contains(id) || id == selected);
            foreach (var roomId in memberIds)
            {
                if (roomId == selected || !_offlined.Add(roomId))
                {
                    continue;
                }

                if (!await PublishOfflineAsync(roomId))
                {
                    _offlined.Remove(roomId);
                }
            }

            if (selected is null || committed is null)
            {
                return;
            }

            LogPresence(committed);
            var snapshot = PrivacyFilter.Apply(committed, _rooms.GetPrivacy(selected.Value));
            await _hub.TryPublishAsync(Envelope.Create(MessageTypes.PresenceUpdate, selected.Value, snapshot));
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private void LogPresence(CommittedPresence committed)
    {
        string? process;
        RuleLayer? layer;
        lock (_lock)
        {
            process = Snapshot.Foreground?.ProcessName;
            layer = Classification?.Layer;
        }

        var summary = $"{committed.Presence.State}|{committed.Presence.Category}|{layer}|{process}";
        if (summary == _loggedPresence)
        {
            return;
        }

        _loggedPresence = summary;
        _logger.LogInformation(
            "Presence {State} {Category} layer {Layer} process {Process}",
            committed.Presence.State,
            committed.Presence.Category,
            layer,
            process);
    }

    public override void Dispose()
    {
        _deadline.Dispose();
        base.Dispose();
    }

    private Task<bool> PublishOfflineAsync(Guid roomId) =>
        _hub.TryPublishAsync(Envelope.Create(
            MessageTypes.PresenceUpdate,
            roomId,
            PresenceSnapshot.Offline(DateTimeOffset.UtcNow)));
}
