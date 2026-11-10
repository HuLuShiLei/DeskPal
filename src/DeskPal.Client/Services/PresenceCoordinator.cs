using DeskPal.Client.Core.Classification;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Presence;
using DeskPal.Shared.Messaging;
using DeskPal.Shared.Presence;
using Microsoft.Extensions.Hosting;

namespace DeskPal.Client.Services;

/// <summary>
/// Folds collector signals into a snapshot, classifies it, debounces the result, and publishes it to every Room
/// after applying that Room's privacy profile.
/// </summary>
public sealed class PresenceCoordinator : BackgroundService
{
    private readonly LayeredClassifier _classifier;
    private readonly RoomState _rooms;
    private readonly HubService _hub;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly PersonalRuleStore _personalRules;
    private readonly System.Threading.Channels.ChannelReader<ActivitySignal> _signals;
    private readonly PresenceDebouncer _debouncer;
    private readonly Lock _lock = new();

    public PresenceCoordinator(
        ActivityBus bus,
        LayeredClassifier classifier,
        RoomState rooms,
        HubService hub,
        AppSettings settings,
        SettingsStore store,
        PersonalRuleStore personalRules)
    {
        _classifier = classifier;
        _rooms = rooms;
        _hub = hub;
        _settings = settings;
        _store = store;
        _personalRules = personalRules;
        _signals = bus.Subscribe();
        _debouncer = new PresenceDebouncer(TimeSpan.FromSeconds(settings.StableSeconds), TimeSpan.FromSeconds(settings.StableSeconds));
        _classifier.SetPersonal(new RuleSet(_personalRules.Load()));
        _rooms.RoomsReloaded += PublishCurrentAsync;
        _hub.Connected += PublishCurrentAsync;
    }

    public event Action? LocalChanged;

    public ActivitySnapshot Snapshot { get; private set; } = ActivitySnapshot.Empty;

    public ClassificationResult? Classification { get; private set; }

    public LocalPresence? Local { get; private set; }

    public CommittedPresence? Committed => _debouncer.Current;

    public bool Paused => _settings.Paused;

    public string? ManualText => _settings.ManualText;

    public void SetPaused(bool paused)
    {
        _settings.Paused = paused;
        _store.Save(_settings);
        Evaluate();
    }

    public void SetManualText(string? text)
    {
        _settings.ManualText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        _store.Save(_settings);
        Evaluate();
    }

    public void ReloadPersonalRules()
    {
        _classifier.SetPersonal(new RuleSet(_personalRules.Load()));
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
        lock (_lock)
        {
            Classification = _classifier.Classify(Snapshot);
            Local = PresenceResolver.Resolve(Snapshot, Classification, _settings.ManualText, _settings.Paused);
            var ready = Snapshot.Foreground is not null || Local.State != PresenceState.Other;
            committed = ready ? _debouncer.Offer(Local, DateTimeOffset.UtcNow) : null;
        }

        LocalChanged?.Invoke();
        if (committed is not null)
        {
            _ = PublishAsync(committed);
        }
    }

    private Task PublishCurrentAsync() => _debouncer.Current is { } current ? PublishAsync(current) : Task.CompletedTask;

    private async Task PublishAsync(CommittedPresence committed)
    {
        foreach (var room in _rooms.Rooms)
        {
            var snapshot = PrivacyFilter.Apply(committed, _rooms.GetPrivacy(room.Id));
            await _hub.TryPublishAsync(Envelope.Create(MessageTypes.PresenceUpdate, room.Id, snapshot));
        }
    }
}