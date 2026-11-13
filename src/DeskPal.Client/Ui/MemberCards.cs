using System.Windows;
using System.Windows.Threading;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Services;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Ui;

/// <summary>Keeps one card window per pinned member in step with the panel state.</summary>
public sealed class MemberCards
{
    private const double Margin = 16;
    private const double Gap = 8;
    private readonly PanelBridge _bridge;
    private readonly PinnedMembers _pins;
    private readonly PanelWindow _panel;
    private readonly AppSettings _settings;
    private readonly ILogger<MemberCards> _logger;
    private readonly Dictionary<Guid, MemberCardWindow> _cards = new();
    private readonly DispatcherTimer _sync;
    private readonly DispatcherTimer _tick;
    private bool _closed;

    public MemberCards(PanelBridge bridge, PinnedMembers pins, PanelWindow panel, AppSettings settings, ILogger<MemberCards> logger)
    {
        _bridge = bridge;
        _pins = pins;
        _panel = panel;
        _settings = settings;
        _logger = logger;
        var dispatcher = Application.Current.Dispatcher;
        _sync = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => Sync(), dispatcher);
        _sync.Stop();
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => Sync(), dispatcher);
        _tick.Stop();
        bridge.Changed += () => dispatcher.BeginInvoke(Schedule);
        bridge.ThemeChanged += theme => dispatcher.BeginInvoke(() =>
        {
            foreach (var card in _cards.Values)
            {
                card.ApplyTheme(theme);
            }

            Schedule();
        });
    }

    public void CloseAll()
    {
        _closed = true;
        _sync.Stop();
        _tick.Stop();
        var cards = _cards.Values.ToList();
        _cards.Clear();
        foreach (var card in cards)
        {
            card.Close();
        }
    }

    private void Schedule()
    {
        if (!_closed && !_sync.IsEnabled)
        {
            _sync.Start();
        }
    }

    private void Sync()
    {
        _sync.Stop();
        if (_closed)
        {
            return;
        }

        var state = _bridge.BuildState();
        _pins.Retain(state.SelectedRoomId, state.Members.Select(m => m.UserId));
        foreach (var userId in _cards.Keys.Where(id => !_pins.Contains(id)).ToList())
        {
            if (_cards.Remove(userId, out var stale))
            {
                stale.Close();
            }
        }

        foreach (var member in state.Members.Where(m => _pins.Contains(m.UserId)))
        {
            if (!_cards.TryGetValue(member.UserId, out var card))
            {
                card = Open(member.UserId);
            }

            card.Update(new { type = "card", member, detail = Detail(member.View), theme = state.Settings.Theme });
        }

        if (_cards.Count == 0)
        {
            _tick.Stop();
        }
        else if (!_tick.IsEnabled)
        {
            _tick.Start();
        }
    }

    private MemberCardWindow Open(Guid userId)
    {
        var slot = Enumerable.Range(0, _cards.Count + 1).First(i => _cards.Values.All(c => c.Slot != i));
        var card = new MemberCardWindow(userId, slot, _settings.Theme, _logger);
        var area = SystemParameters.WorkArea;
        var perColumn = Math.Max(1, (int)((area.Height - Margin) / (MemberCardWindow.CardHeight + Gap)));
        card.Left = area.Right - Margin - MemberCardWindow.CardWidth - slot / perColumn * (MemberCardWindow.CardWidth + Gap);
        card.Top = area.Top + Margin + slot % perColumn * (MemberCardWindow.CardHeight + Gap);
        card.OpenPanel += _panel.ShowAndActivate;
        card.Closed += (_, _) =>
        {
            if (_cards.Remove(userId))
            {
                _pins.Unpin(userId);
            }
        };
        _cards[userId] = card;
        card.Show();
        return card;
    }

    /// <summary>App and title on one line; the card has no room for the panel's two-line title.</summary>
    private static string? Detail(PresenceView view)
    {
        var line = PresenceFormatter.OneLine(view with { Headline = "" });
        return line.Length == 0 ? null : line;
    }
}