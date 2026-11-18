using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeskPal.Client.Services;
using DeskPal.Client.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeskPal.Client.Ui;

/// <summary>
/// The status, Room and settings panel. The page lives in WebView2, which is released after the window has been
/// hidden for a while and created again on the next open. If the page does not report ready in time, or a WebView2
/// process fails, the WebView2 is created again.
/// </summary>
public sealed class PanelWindow : Window, IUserPrompt
{
    private static readonly TimeSpan ReleaseAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);
    private const int MaxLoadAttempts = 3;
    private const long JustShownMilliseconds = 1000;
    private readonly PanelBridge _bridge;
    private readonly AppSettings _settings;
    private readonly ILogger<PanelWindow> _logger;
    private readonly DispatcherTimer _release;
    private readonly DispatcherTimer _push;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _loadTimeout;
    private readonly Stopwatch _loading = new();
    private readonly Dictionary<int, TaskCompletionSource<bool>> _dialogs = new();
    private int _nextDialogId;
    private int _loadAttempts;
    private long _shownAt;
    private bool _placed;
    private WebView2? _web;
    private Border? _surface;
    private TaskCompletionSource? _ready;

    public PanelWindow(PanelBridge bridge, AppSettings settings, UpdateService updates, ILogger<PanelWindow> logger)
    {
        _bridge = bridge;
        _settings = settings;
        _logger = logger;
        Title = $"DeskPal {updates.VersionLabel} - {settings.DisplayName}";
        Width = 420;
        Height = 680;
        MinWidth = 400;
        MinHeight = 480;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = settings.Topmost;
        Background = WebUi.BrushFor(settings.Theme);
        FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowFrame.Apply(this);

        _release = new DispatcherTimer(ReleaseAfter, DispatcherPriority.Background, (_, _) => ReleaseWebView(), Dispatcher);
        _release.Stop();
        _push = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => PushState(), Dispatcher);
        _push.Stop();
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => SchedulePush(), Dispatcher);
        _loadTimeout = new DispatcherTimer(LoadTimeout, DispatcherPriority.Background, (_, _) => OnLoadTimeout(), Dispatcher);
        _loadTimeout.Stop();

        bridge.Prompt = this;
        bridge.Changed += () => Dispatcher.BeginInvoke(SchedulePush);
        updates.Changed += () => Dispatcher.BeginInvoke(PushState);
        bridge.TopmostChanged += topmost => Dispatcher.BeginInvoke(() => Topmost = topmost);
        bridge.ThemeChanged += theme => Dispatcher.BeginInvoke(() => ApplyChrome(theme));
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _release.Stop();
                _tick.Start();
                SchedulePush();
            }
            else
            {
                _tick.Stop();
                _release.Start();
            }
        };
    }

    public bool AllowClose { get; set; }

    /// <summary>The page chose to quit. The app handles this the same way as the tray Exit item.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Became visible less than a second ago, e.g. opened at startup just before a tray click.</summary>
    public bool JustShown => Environment.TickCount64 - _shownAt < JustShownMilliseconds;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ReleaseWebView();
        base.OnClosed(e);
    }

    public void ShowAndActivate()
    {
        if (!_placed)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 16;
            Top = area.Bottom - Height - 16;
            _placed = true;
        }

        if (!IsVisible)
        {
            _shownAt = Environment.TickCount64;
        }

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        _ = EnsureWebViewAsync();
    }

    public async Task<bool> AskAsync(PromptKind kind, string message)
    {
        ShowAndActivate();
        await EnsureWebViewAsync();
        if (_web?.CoreWebView2 is null)
        {
            return await MessageBoxPrompt.Instance.AskAsync(kind, message);
        }

        var id = ++_nextDialogId;
        var answer = new TaskCompletionSource<bool>();
        _dialogs[id] = answer;
        WebUi.Post(_web, new
        {
            type = "dialog",
            id,
            kind = kind switch
            {
                PromptKind.Confirm => "confirm",
                PromptKind.Warning => "warning",
                _ => "info",
            },
            message,
        });
        return await answer.Task;
    }

    private void ApplyChrome(string theme)
    {
        Background = WebUi.BrushFor(theme);
        if (_surface is not null)
        {
            _surface.Background = WebUi.SurfaceBrushFor(theme);
        }

        if (_web is not null)
        {
            _web.DefaultBackgroundColor = WebUi.ColorFor(theme);
        }
    }

    /// <summary>
    /// Leaves the resize border around the page. Under the content area the border shows the content color, so the
    /// strip continues the page instead of looking like a frame. The offsets follow .rail and .titlebar in styles.css.
    /// </summary>
    private Grid Host(WebView2 web)
    {
        const double railWidth = 60;
        const double titleBarHeight = 36;
        var edge = WindowFrame.ResizeBorder;
        web.Margin = new Thickness(edge);
        _surface = new Border
        {
            Background = WebUi.SurfaceBrushFor(_settings.Theme),
            Margin = new Thickness(edge + railWidth, edge + titleBarHeight, 0, 0),
        };
        var grid = new Grid();
        grid.Children.Add(_surface);
        grid.Children.Add(web);
        return grid;
    }

    private Task EnsureWebViewAsync()
    {
        if (_ready is not null)
        {
            return _ready.Task;
        }

        _ready = new TaskCompletionSource();
        if (!WebUi.RuntimeAvailable())
        {
            Content = Notice(Strings.Panel_RuntimeMissing, WebUi.DownloadUrl);
            _ready.SetResult();
            return _ready.Task;
        }

        ApplyChrome(_settings.Theme);
        var web = new WebView2 { DefaultBackgroundColor = WebUi.ColorFor(_settings.Theme) };
        _web = web;
        Content = Host(web);
        _logger.LogInformation("Loading the panel page (attempt {Attempt})", _loadAttempts + 1);
        _loading.Restart();
        _loadTimeout.Start();
        _ = InitializeAsync(web);
        return _ready.Task;
    }

    private async Task InitializeAsync(WebView2 web)
    {
        try
        {
            await WebUi.InitializeAsync(web, WebUi.Page("index.html", _settings.Theme), OnMessage);
        }
        catch (Exception ex)
        {
            if (_web == web)
            {
                _logger.LogWarning(ex, "Initializing WebView2 failed");
                RestartWebView(ex.Message);
            }

            return;
        }

        if (_web != web)
        {
            return;
        }

        _logger.LogInformation("Panel WebView2 created in {Milliseconds} ms", _loading.ElapsedMilliseconds);
        web.CoreWebView2.ProcessFailed += (_, e) => OnProcessFailed(web, e);
        web.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                _logger.LogWarning("Panel page navigation failed ({Status})", e.WebErrorStatus);
            }
        };
    }

    private void OnProcessFailed(WebView2 web, CoreWebView2ProcessFailedEventArgs e)
    {
        _logger.LogWarning("WebView2 process failed: {Kind}, {Reason}, exit code {ExitCode}", e.ProcessFailedKind, e.Reason, e.ExitCode);
        if (e.ProcessFailedKind is not (CoreWebView2ProcessFailedKind.BrowserProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive))
        {
            return;
        }

        // Disposing the control inside its own event handler is not safe.
        Dispatcher.BeginInvoke(() =>
        {
            if (_web == web)
            {
                RestartWebView(null);
            }
        });
    }

    private void OnLoadTimeout()
    {
        _loadTimeout.Stop();
        if (_web is null || _ready is not { Task.IsCompleted: false })
        {
            return;
        }

        _logger.LogWarning("Panel page did not report ready within {Seconds} s", LoadTimeout.TotalSeconds);
        if (IsVisible)
        {
            RestartWebView(null);
        }
        else
        {
            DisposeWebView();
            Content = null;
        }
    }

    /// <summary>Creates the WebView2 again while the window is open. After a few failures shows the error instead.</summary>
    private void RestartWebView(string? error)
    {
        _loadAttempts++;
        DisposeWebView();
        if (_loadAttempts >= MaxLoadAttempts)
        {
            _logger.LogWarning("Panel page failed {Attempts} times in a row", _loadAttempts);
            _loadAttempts = 0;
            Content = Notice(error is null ? Strings.Panel_LoadStalled : UiLanguage.Format(Strings.Panel_LoadFailed, error), null);
            return;
        }

        Content = null;
        if (IsVisible)
        {
            _ = EnsureWebViewAsync();
        }
    }

    private async void OnMessage(string json)
    {
        if (WebCommand.Parse(json) is not { } command || WindowFrame.Handle(this, command.Type))
        {
            return;
        }

        switch (command.Type)
        {
            case "ready":
                _loadTimeout.Stop();
                _loadAttempts = 0;
                if (_loading.IsRunning)
                {
                    _logger.LogInformation("Panel page ready in {Milliseconds} ms", _loading.ElapsedMilliseconds);
                    _loading.Reset();
                }

                _ready?.TrySetResult();
                PushState();
                return;
            case "dialog.answer":
                if (command.Args.TryGetProperty("dialogId", out var idValue) && _dialogs.Remove(idValue.GetInt32(), out var pending))
                {
                    pending.TrySetResult(command.Bool("ok"));
                }

                return;
            case "window.quit":
                QuitRequested?.Invoke(this, EventArgs.Empty);
                return;
        }

        var web = _web;
        var result = await _bridge.HandleAsync(command);
        if (command.Id is { } id && web == _web)
        {
            WebUi.Post(web, new { type = "result", id, ok = result.Ok, error = result.Error, data = result.Data });
        }
    }

    private void SchedulePush()
    {
        if (IsVisible && !_push.IsEnabled)
        {
            _push.Start();
        }
    }

    private void PushState()
    {
        _push.Stop();
        if (_ready?.Task.IsCompleted == true && _web?.CoreWebView2 is not null)
        {
            WebUi.Post(_web, new { type = "state", state = _bridge.BuildState() });
        }
    }

    private void ReleaseWebView()
    {
        _release.Stop();
        if (IsVisible && !AllowClose)
        {
            return;
        }

        DisposeWebView();
        Content = null;
    }

    private void DisposeWebView()
    {
        _loadTimeout.Stop();
        _loading.Reset();
        foreach (var pending in _dialogs.Values)
        {
            pending.TrySetResult(false);
        }

        _dialogs.Clear();
        var web = _web;
        var ready = _ready;
        _web = null;
        _surface = null;
        _ready = null;
        web?.Dispose();
        ready?.TrySetResult();
    }

    /// <summary>Native fallback without the page's title bar, so it can be dragged anywhere and closed with its own button.</summary>
    private FrameworkElement Notice(string text, string? link)
    {
        var panel = new StackPanel { Margin = new Thickness(24), Background = Brushes.Transparent };
        panel.MouseLeftButtonDown += (_, _) => DragMove();
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        if (link is not null)
        {
            var download = new Button { Content = Strings.Panel_OpenDownload, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
            download.Click += (_, _) => WebUi.OpenExternal(link);
            buttons.Children.Add(download);
        }

        var close = new Button { Content = Strings.Common_Close, Padding = new Thickness(12, 4, 12, 4) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        return panel;
    }
}