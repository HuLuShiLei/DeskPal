using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeskPal.Client.Services;
using DeskPal.Client.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;

namespace DeskPal.Client.Ui;

/// <summary>
/// The status, Room and settings panel. The page lives in WebView2, which is released after the window has been
/// hidden for a while and created again on the next open.
/// </summary>
public sealed class PanelWindow : Window, IUserPrompt
{
    private static readonly TimeSpan ReleaseAfter = TimeSpan.FromMinutes(5);
    private readonly PanelBridge _bridge;
    private readonly ILogger<PanelWindow> _logger;
    private readonly DispatcherTimer _release;
    private readonly DispatcherTimer _push;
    private readonly DispatcherTimer _tick;
    private readonly Dictionary<int, TaskCompletionSource<bool>> _dialogs = new();
    private int _nextDialogId;
    private bool _placed;
    private WebView2? _web;
    private TaskCompletionSource? _ready;

    public PanelWindow(PanelBridge bridge, AppSettings settings, UpdateService updates, ILogger<PanelWindow> logger)
    {
        _bridge = bridge;
        _logger = logger;
        Title = $"DeskPal {updates.VersionLabel} - {settings.DisplayName}";
        Width = 420;
        Height = 680;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = settings.Topmost;
        Background = new SolidColorBrush(Color.FromRgb(WebUi.Background.R, WebUi.Background.G, WebUi.Background.B));
        FontFamily = new FontFamily("Microsoft YaHei UI");

        _release = new DispatcherTimer(ReleaseAfter, DispatcherPriority.Background, (_, _) => ReleaseWebView(), Dispatcher);
        _release.Stop();
        _push = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => PushState(), Dispatcher);
        _push.Stop();
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => SchedulePush(), Dispatcher);

        bridge.Prompt = this;
        bridge.Changed += () => Dispatcher.BeginInvoke(SchedulePush);
        updates.Changed += () => Dispatcher.BeginInvoke(PushState);
        bridge.TopmostChanged += topmost => Dispatcher.BeginInvoke(() => Topmost = topmost);
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

    private Task EnsureWebViewAsync()
    {
        if (_ready is not null)
        {
            return _ready.Task;
        }

        _ready = new TaskCompletionSource();
        if (!WebUi.RuntimeAvailable())
        {
            Content = Notice("没有找到 Microsoft Edge WebView2 Runtime，面板无法显示。状态同步不受影响。\n\n安装后重新启动 DeskPal。", WebUi.DownloadUrl);
            _ready.SetResult();
            return _ready.Task;
        }

        var web = new WebView2 { DefaultBackgroundColor = WebUi.Background };
        _web = web;
        Content = web;
        _ = InitializeAsync(web, _ready);
        return _ready.Task;
    }

    private async Task InitializeAsync(WebView2 web, TaskCompletionSource ready)
    {
        try
        {
            await WebUi.InitializeAsync(web, "index.html", OnMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initializing WebView2 failed");
            if (_web == web)
            {
                ReleaseWebView();
                Content = Notice($"面板加载失败：{ex.Message}", null);
            }

            ready.TrySetResult();
        }
    }

    private async void OnMessage(string json)
    {
        if (WebCommand.Parse(json) is not { } command)
        {
            return;
        }

        switch (command.Type)
        {
            case "ready":
                _ready?.TrySetResult();
                PushState();
                return;
            case "dialog.answer":
                if (command.Args.TryGetProperty("dialogId", out var idValue) && _dialogs.Remove(idValue.GetInt32(), out var pending))
                {
                    pending.TrySetResult(command.Bool("ok"));
                }

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

        foreach (var pending in _dialogs.Values)
        {
            pending.TrySetResult(false);
        }

        _dialogs.Clear();
        _web?.Dispose();
        _web = null;
        _ready = null;
        Content = null;
    }

    private static FrameworkElement Notice(string text, string? link)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        if (link is not null)
        {
            var button = new Button { Content = "打开下载页面", Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => WebUi.OpenExternal(link);
            panel.Children.Add(button);
        }

        return panel;
    }
}