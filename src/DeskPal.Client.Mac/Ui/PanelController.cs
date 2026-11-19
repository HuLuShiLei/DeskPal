using System.Diagnostics;
using AppKit;
using CoreGraphics;
using DeskPal.Client.Platform;
using DeskPal.Client.Services;
using Foundation;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Ui;

/// <summary>
/// The panel, same behavior as the Windows PanelWindow: the page lives in a WKWebView that is released after the
/// window has been hidden for a while and created again on the next open. If the page does not report ready in time,
/// or its process ends, the view is created again. Runs on the main thread.
/// </summary>
public sealed class PanelController : IUserPrompt
{
    private static readonly TimeSpan ReleaseAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);
    private const int MaxLoadAttempts = 3;
    private const long JustShownMilliseconds = 1000;

    private readonly PanelBridge _bridge;
    private readonly AppSettings _settings;
    private readonly ILogger<PanelController> _logger;
    private readonly Stopwatch _loading = new();
    private readonly Dictionary<int, TaskCompletionSource<bool>> _dialogs = new();
    private NSWindow? _window;
    private WebHost? _web;
    private TaskCompletionSource? _ready;
    private NSTimer? _release;
    private NSTimer? _push;
    private NSTimer? _tick;
    private NSTimer? _loadTimeout;
    private int _nextDialogId;
    private int _loadAttempts;
    private long _shownAt;
    private bool _placed;

    public PanelController(PanelBridge bridge, AppSettings settings, ILogger<PanelController> logger)
    {
        _bridge = bridge;
        _settings = settings;
        _logger = logger;
        bridge.Prompt = this;
        bridge.Changed += () => MainThread.Post(SchedulePush);
        bridge.TopmostChanged += topmost => MainThread.Post(() =>
        {
            if (_window is not null)
            {
                _window.Level = topmost ? NSWindowLevel.Floating : NSWindowLevel.Normal;
            }
        });
        bridge.ThemeChanged += theme => MainThread.Post(() =>
        {
            if (_window is not null)
            {
                WindowChrome.ApplyTheme(_window, theme);
            }
        });
    }

    /// <summary>The page chose to quit, the same as Quit in the menu bar menu.</summary>
    public event Action? QuitRequested;

    public bool IsVisible => _window?.IsVisible == true && !_window.IsMiniaturized;

    public bool JustShown => Environment.TickCount64 - _shownAt < JustShownMilliseconds;

    public void ShowAndActivate()
    {
        var window = EnsureWindow();
        if (!_placed)
        {
            var area = (window.Screen ?? NSScreen.MainScreen)?.VisibleFrame ?? new CGRect(0, 0, 1440, 900);
            window.SetFrameOrigin(new CGPoint(area.Right - window.Frame.Width - 16, area.Y + 16));
            _placed = true;
        }

        if (!IsVisible)
        {
            _shownAt = Environment.TickCount64;
        }

        if (window.IsMiniaturized)
        {
            window.Deminiaturize(null);
        }

        MainThread.Activate();
        window.MakeKeyAndOrderFront(null);
        OnShown();
        _ = EnsureWebViewAsync();
    }

    public void Hide()
    {
        _window?.OrderOut(null);
        OnHidden();
    }

    public void CloseForExit()
    {
        StopTimer(ref _release);
        StopTimer(ref _tick);
        StopTimer(ref _push);
        DisposeWebView();
        _window?.Close();
    }

    public async Task<bool> AskAsync(PromptKind kind, string message)
    {
        if (!MainThread.IsCurrent)
        {
            return await MainThread.InvokeAsync(() => AskAsync(kind, message));
        }

        ShowAndActivate();
        await EnsureWebViewAsync();
        if (_web is null)
        {
            return await new AlertPrompt().AskAsync(kind, message);
        }

        var id = ++_nextDialogId;
        var answer = new TaskCompletionSource<bool>();
        _dialogs[id] = answer;
        _web.Post(new
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

    private NSWindow EnsureWindow()
    {
        if (_window is not null)
        {
            return _window;
        }

        var window = WindowChrome.Create(new CGSize(420, 680), resizable: true);
        window.MinSize = new CGSize(400, 480);
        window.Title = "DeskPal";
        window.Level = _settings.Topmost ? NSWindowLevel.Floating : NSWindowLevel.Normal;
        WindowChrome.ApplyTheme(window, _settings.Theme);

        // Cmd+W and the window menu close the panel to the menu bar; the app keeps running.
        window.WindowShouldClose = _ =>
        {
            Hide();
            return false;
        };
        window.DidMiniaturize += (_, _) => OnHidden();
        window.DidDeminiaturize += (_, _) => OnShown();
        _window = window;
        return window;
    }

    private void OnShown()
    {
        StopTimer(ref _release);
        _tick ??= NSTimer.CreateRepeatingScheduledTimer(30, _ => SchedulePush());
        SchedulePush();
    }

    private void OnHidden()
    {
        StopTimer(ref _tick);
        StopTimer(ref _release);
        _release = NSTimer.CreateScheduledTimer(ReleaseAfter.TotalSeconds, _ => ReleaseWebView());
    }

    private Task EnsureWebViewAsync()
    {
        if (_ready is not null)
        {
            return _ready.Task;
        }

        _ready = new TaskCompletionSource();
        var window = EnsureWindow();
        var content = window.ContentView!;
        var web = new WebHost(content.Bounds, "index.html", _settings.Theme, OnMessage);
        web.ProcessTerminated += () => MainThread.Post(() =>
        {
            if (_web == web)
            {
                _logger.LogWarning("Panel web content process ended");
                RestartWebView();
            }
        });
        _web = web;
        content.AddSubview(web.View);
        _logger.LogInformation("Loading the panel page (attempt {Attempt})", _loadAttempts + 1);
        _loading.Restart();
        StopTimer(ref _loadTimeout);
        _loadTimeout = NSTimer.CreateScheduledTimer(LoadTimeout.TotalSeconds, _ => OnLoadTimeout());
        return _ready.Task;
    }

    private void OnLoadTimeout()
    {
        StopTimer(ref _loadTimeout);
        if (_web is null || _ready is not { Task.IsCompleted: false })
        {
            return;
        }

        _logger.LogWarning("Panel page did not report ready within {Seconds} s", LoadTimeout.TotalSeconds);
        if (IsVisible)
        {
            RestartWebView();
        }
        else
        {
            DisposeWebView();
        }
    }

    private void RestartWebView()
    {
        _loadAttempts++;
        DisposeWebView();
        if (_loadAttempts >= MaxLoadAttempts)
        {
            _logger.LogWarning("Panel page failed {Attempts} times in a row", _loadAttempts);
            _loadAttempts = 0;
            Hide();
            _ = new AlertPrompt().AskAsync(PromptKind.Warning, Strings.Panel_LoadStalled);
            return;
        }

        if (IsVisible)
        {
            _ = EnsureWebViewAsync();
        }
    }

    private async void OnMessage(string json)
    {
        if (_window is null || WebCommand.Parse(json) is not { } command || WindowChrome.Handle(_window, command.Type))
        {
            return;
        }

        switch (command.Type)
        {
            case "ready":
                StopTimer(ref _loadTimeout);
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
                QuitRequested?.Invoke();
                return;
        }

        var web = _web;
        var result = await _bridge.HandleAsync(command);
        if (command.Id is { } id && web == _web)
        {
            web?.Post(new { type = "result", id, ok = result.Ok, error = result.Error, data = result.Data });
        }
    }

    private void SchedulePush()
    {
        if (IsVisible && _push is null)
        {
            _push = NSTimer.CreateScheduledTimer(0.2, _ =>
            {
                _push = null;
                PushState();
            });
        }
    }

    private void PushState()
    {
        StopTimer(ref _push);
        if (_ready?.Task.IsCompleted == true && _web is not null)
        {
            _web.Post(new { type = "state", state = _bridge.BuildState() });
        }
    }

    private void ReleaseWebView()
    {
        StopTimer(ref _release);
        if (IsVisible)
        {
            return;
        }

        DisposeWebView();
    }

    private void DisposeWebView()
    {
        StopTimer(ref _loadTimeout);
        _loading.Reset();
        foreach (var pending in _dialogs.Values)
        {
            pending.TrySetResult(false);
        }

        _dialogs.Clear();
        var web = _web;
        var ready = _ready;
        _web = null;
        _ready = null;
        web?.Close();
        ready?.TrySetResult();
    }

    private static void StopTimer(ref NSTimer? timer)
    {
        timer?.Invalidate();
        timer = null;
    }
}
