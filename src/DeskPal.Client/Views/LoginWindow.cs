using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskPal.Client.Services;
using DeskPal.Shared.Api;

namespace DeskPal.Client.Views;

public sealed class LoginWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ApiClient _api;
    private readonly TextBox _server = new() { Padding = new Thickness(4) };
    private readonly TextBox _userName = new() { Padding = new Thickness(4) };
    private readonly TextBox _displayName = new() { Padding = new Thickness(4) };
    private readonly PasswordBox _password = new() { Padding = new Thickness(4) };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _login = new() { Content = "登录", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _register = new() { Content = "注册", Width = 90 };

    public LoginWindow(AppSettings settings, SettingsStore store, ApiClient api)
    {
        _settings = settings;
        _store = store;
        _api = api;
        Title = "DeskPal 登录";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI");

        _server.Text = settings.ServerUrl;
        _userName.Text = settings.UserName ?? "";

        var root = new StackPanel { Margin = new Thickness(18) };
        AddField(root, "服务器地址", _server);
        AddField(root, "用户名", _userName);
        AddField(root, "密码", _password);
        AddField(root, "显示名称（仅注册时需要）", _displayName);
        root.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_login);
        buttons.Children.Add(_register);
        root.Children.Add(buttons);
        Content = root;

        _login.Click += async (_, _) => await SubmitAsync(register: false);
        _register.Click += async (_, _) => await SubmitAsync(register: true);
        Loaded += (_, _) => (_userName.Text.Length == 0 ? (Control)_userName : _password).Focus();
    }

    private static void AddField(Panel root, string label, Control input)
    {
        root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
        root.Children.Add(input);
    }

    private async Task SubmitAsync(bool register)
    {
        _error.Text = "";
        _settings.ServerUrl = _server.Text.Trim().TrimEnd('/');
        if (register && string.IsNullOrWhiteSpace(_displayName.Text))
        {
            _error.Text = "注册需要填写显示名称";
            return;
        }

        _login.IsEnabled = _register.IsEnabled = false;
        try
        {
            var response = register
                ? await _api.RegisterAsync(new RegisterRequest(_userName.Text, _displayName.Text, _password.Password))
                : await _api.LoginAsync(new LoginRequest(_userName.Text, _password.Password));
            _settings.UserId = response.UserId;
            _settings.UserName = response.UserName;
            _settings.DisplayName = response.DisplayName;
            _settings.SetToken(response.Token);
            _store.Save(_settings);
            DialogResult = true;
        }
        catch (ApiException ex)
        {
            _error.Text = ex.Message;
        }
        finally
        {
            _login.IsEnabled = _register.IsEnabled = true;
        }
    }
}