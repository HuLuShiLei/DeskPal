using AppKit;
using CoreGraphics;
using DeskPal.Client.Core.Presence;
using DeskPal.Client.Core.Ui;
using DeskPal.Client.Platform;
using DeskPal.Client.Services;
using Foundation;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Ui;

/// <summary>
/// One small always-on-top card per pinned member, like on Windows: stacked from the top right of the screen, the
/// whole card drags, a double-click opens the panel, × unpins. Cards float above other windows on every Space and
/// never take focus. Runs on the main thread.
/// </summary>
public sealed class MemberCards
{
    private const double CardWidth = 280;
    private const double CardHeight = 84;
    private const double Margin = 16;
    private const double Gap = 8;
    private readonly PanelBridge _bridge;
    private readonly PinnedMembers _pins;
    private readonly PanelController _panel;
    private readonly AppSettings _settings;
    private readonly ILogger<MemberCards> _logger;
    private readonly Dictionary<Guid, Card> _cards = new();
    private NSTimer? _sync;
    private NSTimer? _tick;
    private bool _closed;

    public MemberCards(PanelBridge bridge, PinnedMembers pins, PanelController panel, AppSettings settings, ILogger<MemberCards> logger)
    {
        _bridge = bridge;
        _pins = pins;
        _panel = panel;
        _settings = settings;
        _logger = logger;
        bridge.Changed += () => MainThread.Post(Schedule);
        bridge.ThemeChanged += theme => MainThread.Post(() =>
        {
            foreach (var card in _cards.Values)
            {
                WindowChrome.ApplyTheme(card.Window, theme);
            }

            Schedule();
        });
    }

    public void CloseAll()
    {
        _closed = true;
        _sync?.Invalidate();
        _tick?.Invalidate();
        foreach (var card in _cards.Values.ToList())
        {
            card.Close();
        }

        _cards.Clear();
    }

    private void Schedule()
    {
        if (!_closed && _sync is null)
        {
            _sync = NSTimer.CreateScheduledTimer(0.2, _ =>
            {
                _sync = null;
                Sync();
            });
        }
    }

    private void Sync()
    {
        if (_closed)
        {
            return;
        }

        var state = _bridge.BuildState();
        _pins.Retain(state.SelectedRoomId, state.Members.Where(m => !m.Blocked).Select(m => m.UserId));
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

            card.Update(new { type = "card", member, detail = Detail(member.View), theme = state.Settings.Theme, locale = state.Locale });
        }

        if (_cards.Count == 0)
        {
            _tick?.Invalidate();
            _tick = null;
        }
        else
        {
            _tick ??= NSTimer.CreateRepeatingScheduledTimer(30, _ => Schedule());
        }
    }

    private Card Open(Guid userId)
    {
        var slot = Enumerable.Range(0, _cards.Count + 1).First(i => _cards.Values.All(c => c.Slot != i));
        var area = NSScreen.MainScreen?.VisibleFrame ?? new CGRect(0, 0, 1440, 900);
        var perColumn = Math.Max(1, (int)((area.Height - Margin) / (CardHeight + Gap)));
        var x = area.Right - Margin - CardWidth - slot / perColumn * (CardWidth + Gap);
        var top = area.Bottom - Margin - slot % perColumn * (CardHeight + Gap);
        var card = new Card(userId, slot, new CGRect(x, top - CardHeight, CardWidth, CardHeight), _settings.Theme, _logger);
        card.OpenPanel += _panel.ShowAndActivate;
        card.Closed += () =>
        {
            if (_cards.Remove(userId))
            {
                _pins.Unpin(userId);
            }
        };
        _cards[userId] = card;
        return card;
    }

    /// <summary>App and title on one line; the card has no room for the panel's two-line title.</summary>
    private static string? Detail(PresenceView view)
    {
        var line = PresenceFormatter.OneLine(view with { Headline = "" });
        return line.Length == 0 ? null : line;
    }

    private sealed class Card
    {
        private readonly WebHost _web;
        private object? _card;
        private bool _ready;
        private bool _closed;

        public Card(Guid userId, int slot, CGRect frame, string? theme, ILogger logger)
        {
            UserId = userId;
            Slot = slot;
            Window = new NSPanel(frame, NSWindowStyle.Borderless | NSWindowStyle.NonactivatingPanel, NSBackingStore.Buffered, false)
            {
                Level = NSWindowLevel.Floating,
                HidesOnDeactivate = false,
                HasShadow = true,
                IsOpaque = false,
                BackgroundColor = NSColor.Clear,
                CollectionBehavior = NSWindowCollectionBehavior.CanJoinAllSpaces | NSWindowCollectionBehavior.Stationary | NSWindowCollectionBehavior.FullScreenAuxiliary,
            };
            Window.ReleaseWhenClosed(false);
            WindowChrome.ApplyTheme(Window, theme);
            var content = Window.ContentView!;
            content.WantsLayer = true;
            content.Layer!.CornerRadius = 8;
            content.Layer.MasksToBounds = true;
            _web = new WebHost(content.Bounds, "index.html?page=card", theme, OnMessage);
            _web.ProcessTerminated += () => MainThread.Post(Close);
            content.AddSubview(_web.View);
            Window.OrderFrontRegardless();
            logger.LogDebug("Member card opened in slot {Slot}", slot);
        }

        public Guid UserId { get; }

        public int Slot { get; }

        public NSPanel Window { get; }

        public event Action? OpenPanel;

        public event Action? Closed;

        public void Update(object card)
        {
            _card = card;
            if (_ready)
            {
                _web.Post(card);
            }
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _web.Close();
            Window.Close();
            Closed?.Invoke();
        }

        private void OnMessage(string json)
        {
            switch (WebCommand.Parse(json)?.Type)
            {
                case "ready":
                    _ready = true;
                    if (_card is not null)
                    {
                        _web.Post(_card);
                    }

                    break;
                case "window.drag":
                    WindowChrome.BeginDrag(Window);
                    break;
                case "window.dblclick":
                    OpenPanel?.Invoke();
                    break;
                case "window.close":
                    Close();
                    break;
            }
        }
    }
}
