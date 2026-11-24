using System.Diagnostics;
using DeskPal.Client.Platform;
using DeskPal.Client.Services;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Ui;

/// <summary>
/// The panel, same behavior as the Windows PanelWindow and the macOS PanelController: the page lives in a web view
/// that is released after the window has been hidden for a while and created again on the next open. If the page
/// does not report ready in time, or its process ends, the view is created again. Runs on the GTK thread.
/// Wayland does not let a program place its own window or keep it above others, so the panel opens where the
/// desktop puts it and "always on top" is not offered on Linux.
/// </summary>
public sealed class PanelController : IUserPrompt
{
    private static readonly TimeSpan ReleaseAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);
    private const int MaxLoadAttempts = 3;
    private const long JustShownMilliseconds = 1000;

    private readonly PanelBridge _bridge;
    private readonly AppSettings _settings;
    private readonly Gtk.Application _application;
    private readonly ILogger<PanelController> _logger;
    private readonly Stopwatch _loading = new();
    private readonly Dictionary<int, TaskCompletionSource<bool>> _dialogs = new();
    private Gtk.Window? _window;
    private WebHost? _web;
    private TaskCompletionSource? _ready;
    private UiTimer? _release;
    private UiTimer? _push;
    private UiTimer? _tick;
    private UiTimer? _loadTimeout;
    private int _nextDialogId;
    private int _loadAttempts;
    private long _shownAt;
    private bool _visible;

    public PanelController(PanelBridge bridge, AppSettings settings, Gtk.Application application, ILogger<PanelController> logger)
    {
        _bridge = bridge;
        _settings = settings;
        _application = application;
        _logger = logger;
        bridge.Prompt = this;
        bridge.Changed += () => MainThread.Post(SchedulePush);
    }

    /// <summary>The page chose to quit, the same as Quit in the tray menu.</summary>
    public event Action? QuitRequested;

    public bool IsVisible => _visible;

    public bool JustShown => Environment.TickCount64 - _shownAt < JustShownMilliseconds;

    public void ShowAndActivate()
    {
        var window = EnsureWindow();
        if (!_visible)
        {
            _shownAt = Environment.TickCount64;
        }

        _visible = true;
        LinuxPlatform.ActiveWindow = window;
        window.SetVisible(true);
        window.Present();
        OnShown();
        _ = EnsureWebViewAsync();
    }

    public void Hide()
    {
        _visible = false;
        _window?.SetVisible(false);
        OnHidden();
    }

    public void CloseForExit()
    {
        _release?.Stop();
        _tick?.Stop();
        _push?.Stop();
        DisposeWebView();
        _window?.Destroy();
        _window = null;
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

    private Gtk.Window EnsureWindow()
    {
        if (_window is not null)
        {
            return _window;
        }

        var window = WindowChrome.Create(_application, "DeskPal", 420, 680, resizable: true);
        window.SetSizeRequest(400, 480);

        // Alt+F4 and the desktop's close action hide the panel to the tray; the app keeps running.
        window.OnCloseRequest += (_, _) =>
        {
            Hide();
            return true;
        };
        _window = window;
        return window;
    }

    private void OnShown()
    {
        _release?.Stop();
        _release = null;
        _tick ??= UiTimer.Every(TimeSpan.FromSeconds(30), SchedulePush);
        SchedulePush();
    }

    private void OnHidden()
    {
        _tick?.Stop();
        _tick = null;
        _release?.Stop();
        _release = UiTimer.Once(ReleaseAfter, ReleaseWebView);
    }

    private Task EnsureWebViewAsync()
    {
        if (_ready is not null)
        {
            return _ready.Task;
        }

        _ready = new TaskCompletionSource();
        var window = EnsureWindow();
        var web = new WebHost("index.html", _settings.Theme, OnMessage);
        web.ProcessTerminated += () => MainThread.Post(() =>
        {
            if (_web == web)
            {
                _logger.LogWarning("Panel web content process ended");
                RestartWebView();
            }
        });
        _web = web;
        window.SetChild(web.View);
        _logger.LogInformation("Loading the panel page (attempt {Attempt})", _loadAttempts + 1);
        _loading.Restart();
        _loadTimeout?.Stop();
        _loadTimeout = UiTimer.Once(LoadTimeout, OnLoadTimeout);
        return _ready.Task;
    }

    private void OnLoadTimeout()
    {
        _loadTimeout = null;
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
        if (_window is null || WebCommand.Parse(json) is not { } command || WindowChrome.Handle(_window, command))
        {
            return;
        }

        switch (command.Type)
        {
            case "ready":
                _loadTimeout?.Stop();
                _loadTimeout = null;
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
        try
        {
            var result = await _bridge.HandleAsync(command);
            if (command.Id is { } id && web == _web)
            {
                web?.Post(new { type = "result", id, ok = result.Ok, error = result.Error, data = result.Data });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {Type} failed", command.Type);
            if (command.Id is { } id && web == _web)
            {
                web?.Post(new { type = "result", id, ok = false, error = ex.Message });
            }
        }
    }

    private void SchedulePush()
    {
        if (IsVisible && _push is null)
        {
            _push = UiTimer.Once(TimeSpan.FromMilliseconds(200), () =>
            {
                _push = null;
                PushState();
            });
        }
    }

    private void PushState()
    {
        _push?.Stop();
        _push = null;
        if (_ready?.Task.IsCompleted == true && _web is not null)
        {
            _web.Post(new { type = "state", state = _bridge.BuildState() });
        }
    }

    private void ReleaseWebView()
    {
        _release = null;
        if (IsVisible)
        {
            return;
        }

        DisposeWebView();
    }

    private void DisposeWebView()
    {
        _loadTimeout?.Stop();
        _loadTimeout = null;
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
        if (web is not null)
        {
            _window?.SetChild(null);
            web.Close();
        }

        ready?.TrySetResult();
    }
}
