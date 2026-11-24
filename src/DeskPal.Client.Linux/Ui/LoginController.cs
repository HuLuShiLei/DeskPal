using DeskPal.Client.Platform;
using DeskPal.Client.Services;

namespace DeskPal.Client.Ui;

/// <summary>Login and registration page, shown before the background services start. Completes with true once signed in.</summary>
public sealed class LoginController(Gtk.Application application, AppSettings settings, SettingsStore store, ApiClient api, string? notice)
{
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Gtk.Window? _window;
    private WebHost? _web;

    /// <summary>Runs on the GTK thread.</summary>
    public Task<bool> ShowAsync()
    {
        var window = WindowChrome.Create(application, Strings.Login_WindowTitle, 380, 640, resizable: false);
        window.OnCloseRequest += (_, _) =>
        {
            Finish(false);
            return true;
        };
        _window = window;
        _web = new WebHost("index.html?page=login", settings.Theme, OnMessage);
        window.SetChild(_web.View);
        LinuxPlatform.ActiveWindow = window;
        window.Present();
        return _done.Task;
    }

    private async void OnMessage(string json)
    {
        if (_window is null || _web is null || WebCommand.Parse(json) is not { } command || WindowChrome.Handle(_window, command))
        {
            return;
        }

        if (command.Type == "ready")
        {
            _web.Post(new { type = "login", userName = settings.UserName ?? "", notice });
            return;
        }

        var web = _web;
        var result = await ClientStartup.SubmitLoginAsync(settings, store, api, command);
        if (command.Id is { } id)
        {
            web.Post(new { type = "result", id, ok = result.Ok, error = result.Error });
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
            if (LinuxPlatform.ActiveWindow == window)
            {
                LinuxPlatform.ActiveWindow = null;
            }

            web?.Close();
            window?.Destroy();
        });
    }
}
