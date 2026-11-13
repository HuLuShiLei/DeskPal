using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;

namespace DeskPal.Client.Ui;

/// <summary>
/// A small always-on-top card for one pinned room member. The whole page is a drag region, so double-clicks arrive
/// as non-client messages on this window. Closing the card unpins the member.
/// </summary>
public sealed class MemberCardWindow : Window
{
    public const double CardWidth = 280;
    public const double CardHeight = 84;
    private const int WmNcLButtonDblClk = 0x00A3;
    private readonly WebView2 _web;
    private object? _card;
    private bool _ready;

    public MemberCardWindow(Guid userId, int slot, string? theme, ILogger logger)
    {
        UserId = userId;
        Slot = slot;
        Title = "DeskPal";
        Width = CardWidth;
        Height = CardHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = WebUi.BrushFor(theme);
        WindowFrame.Apply(this);
        _web = new WebView2 { DefaultBackgroundColor = WebUi.ColorFor(theme) };
        Content = _web;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(Hook);
        Loaded += async (_, _) =>
        {
            try
            {
                await WebUi.InitializeAsync(_web, WebUi.Page("index.html?page=card", theme), OnMessage);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Initializing the member card failed");
                Close();
            }
        };
        Closed += (_, _) => _web.Dispose();
    }

    public Guid UserId { get; }

    /// <summary>Position in the stack along the right edge of the screen.</summary>
    public int Slot { get; }

    public event Action? OpenPanel;

    public void Update(object card)
    {
        _card = card;
        if (_ready)
        {
            WebUi.Post(_web, card);
        }
    }

    public void ApplyTheme(string theme)
    {
        Background = WebUi.BrushFor(theme);
        _web.DefaultBackgroundColor = WebUi.ColorFor(theme);
    }

    private void OnMessage(string json)
    {
        if (WebCommand.Parse(json) is not { } command || WindowFrame.Handle(this, command.Type))
        {
            return;
        }

        if (command.Type == "ready")
        {
            _ready = true;
            if (_card is not null)
            {
                WebUi.Post(_web, _card);
            }
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcLButtonDblClk)
        {
            handled = true;
            OpenPanel?.Invoke();
        }

        return IntPtr.Zero;
    }
}