using AppKit;
using CoreGraphics;
using DeskPal.Client.Platform;
using DeskPal.Client.Services;

namespace DeskPal.Client.Ui;

/// <summary>Login and registration page, shown before the background services start. Completes with true once signed in.</summary>
public sealed class LoginController
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ApiClient _api;
    private readonly string? _notice;
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NSWindow? _window;
    private WebHost? _web;

    public LoginController(AppSettings settings, SettingsStore store, ApiClient api, string? notice)
    {
        _settings = settings;
        _store = store;
        _api = api;
        _notice = notice;
    }

    /// <summary>Runs on the main thread.</summary>
    public Task<bool> ShowAsync()
    {
        var window = WindowChrome.Create(new CGSize(380, 640), resizable: false);
        window.Title = Strings.Login_WindowTitle;
        WindowChrome.ApplyTheme(window, _settings.Theme);
        window.WillClose += (_, _) => Finish(false);
        _window = window;
        _web = new WebHost(window.ContentView!.Bounds, "index.html?page=login", _settings.Theme, OnMessage);
        window.ContentView.AddSubview(_web.View);
        window.Center();
        MainThread.Activate();
        window.MakeKeyAndOrderFront(null);
        return _done.Task;
    }

    private async void OnMessage(string json)
    {
        if (_window is null || _web is null || WebCommand.Parse(json) is not { } command || WindowChrome.Handle(_window, command.Type))
        {
            return;
        }

        if (command.Type == "ready")
        {
            _web.Post(new { type = "login", userName = _settings.UserName ?? "", notice = _notice });
            return;
        }

        var result = await ClientStartup.SubmitLoginAsync(_settings, _store, _api, command);
        if (command.Id is { } id)
        {
            _web.Post(new { type = "result", id, ok = result.Ok, error = result.Error });
        }

        if (result.Ok)
        {
            Finish(true);
        }
    }

    private void Finish(bool signedIn)
    {
        if (!_done.TrySetResult(signedIn))
        {
            return;
        }

        var window = _window;
        var web = _web;
        _window = null;
        _web = null;
        MainThread.Post(() =>
        {
            web?.Close();
            window?.Close();
        });
    }
}
