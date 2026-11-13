using System.Windows;
using System.Windows.Media;
using DeskPal.Client.Services;
using DeskPal.Shared.Api;
using Microsoft.Web.WebView2.Wpf;

namespace DeskPal.Client.Ui;

/// <summary>Login and registration page. Shown before the background services start.</summary>
public sealed class LoginWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ApiClient _api;
    private readonly WebView2 _web;
    private readonly string? _notice;

    /// <param name="notice">Why the user is back at the login page, e.g. signed in on another computer.</param>
    public LoginWindow(AppSettings settings, SettingsStore store, ApiClient api, string? notice = null)
    {
        _notice = notice;
        _settings = settings;
        _store = store;
        _api = api;
        _web = new WebView2 { DefaultBackgroundColor = WebUi.ColorFor(settings.Theme) };
        Title = Strings.Login_WindowTitle;
        Width = 380;
        Height = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = WebUi.BrushFor(settings.Theme);
        WindowFrame.Apply(this);
        Content = _web;
        Loaded += async (_, _) =>
        {
            try
            {
                await WebUi.InitializeAsync(_web, WebUi.Page("index.html?page=login", _settings.Theme), OnMessage);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, UiLanguage.Format(Strings.Login_LoadFailed, ex.Message), "DeskPal", MessageBoxButton.OK, MessageBoxImage.Warning);
                DialogResult = false;
            }
        };
        Closed += (_, _) => _web.Dispose();
    }

    private async void OnMessage(string json)
    {
        if (WebCommand.Parse(json) is not { } command || WindowFrame.Handle(this, command.Type))
        {
            return;
        }

        if (command.Type == "ready")
        {
            WebUi.Post(_web, new { type = "login", serverUrl = _settings.ServerUrl, userName = _settings.UserName ?? "", notice = _notice });
            return;
        }

        var result = command.Type is "auth.login" or "auth.register"
            ? await SubmitAsync(command, register: command.Type == "auth.register")
            : CommandResult.Fail(UiLanguage.Format(Strings.Error_Unsupported, command.Type));
        if (command.Id is { } id)
        {
            WebUi.Post(_web, new { type = "result", id, ok = result.Ok, error = result.Error });
        }

        if (result.Ok)
        {
            DialogResult = true;
        }
    }

    private async Task<CommandResult> SubmitAsync(WebCommand c, bool register)
    {
        var server = c.String("serverUrl").Trim().TrimEnd('/');
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return CommandResult.Fail(Strings.Login_InvalidServer);
        }

        var displayName = c.String("displayName").Trim();
        if (register && displayName.Length == 0)
        {
            return CommandResult.Fail(Strings.Login_DisplayNameRequired);
        }

        _settings.ServerUrl = server;
        try
        {
            var response = register
                ? await _api.RegisterAsync(new RegisterRequest(c.String("userName"), displayName, c.String("password")))
                : await _api.LoginAsync(new LoginRequest(c.String("userName"), c.String("password")));
            _settings.UserId = response.UserId;
            _settings.UserName = response.UserName;
            _settings.DisplayName = response.DisplayName;
            _settings.ShareUsage = response.ShareUsage;
            _settings.SetToken(response.Token);
            _store.Save(_settings);
            return CommandResult.Success;
        }
        catch (ApiException ex)
        {
            return CommandResult.Fail(ex.Message);
        }
    }
}