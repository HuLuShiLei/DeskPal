using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using DeskPal.Client.Services;
using DeskPal.Shared;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeskPal.Client.Ui;

/// <summary>WebView2 setup shared by the panel and the login window. Pages come from the web folder next to the exe.</summary>
public static class WebUi
{
    public const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const string HostName = "app.deskpal.example";
    private static Task<CoreWebView2Environment>? _environment;

    /// <summary>Outside the Velopack current folder, which is replaced on every update.</summary>
    public static string UserDataFolder => AppPaths.WebView2Directory;

    /// <summary>Shown while the page loads. Matches --bg in styles.css for the current Windows app theme.</summary>
    public static System.Drawing.Color Background => IsDarkTheme()
        ? System.Drawing.Color.FromArgb(0x20, 0x20, 0x20)
        : System.Drawing.Color.FromArgb(0xF3, 0xF3, 0xF3);

    public static System.Windows.Media.SolidColorBrush BackgroundBrush
    {
        get
        {
            var c = Background;
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
        }
    }

    public static bool IsDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool RuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    public static void ShowMissingRuntime(string consequence)
    {
        var answer = MessageBox.Show(
            $"没有找到 Microsoft Edge WebView2 Runtime，{consequence}\n\n点击“确定”打开下载页面，安装后重新启动 DeskPal。",
            "DeskPal",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer == MessageBoxResult.OK)
        {
            OpenExternal(DownloadUrl);
        }
    }

    public static void OpenExternal(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <param name="page">Path and query under the web root, e.g. "index.html?page=login".</param>
    public static async Task InitializeAsync(WebView2 web, string page, Action<string> onMessage)
    {
        _environment ??= CoreWebView2Environment.CreateAsync(null, UserDataFolder);
        await web.EnsureCoreWebView2Async(await _environment);
        var core = web.CoreWebView2;
#if DEBUG
        const bool debug = true;
#else
        const bool debug = false;
#endif
        core.Settings.AreDevToolsEnabled = debug;
        core.Settings.AreDefaultContextMenusEnabled = debug;
        core.Settings.AreBrowserAcceleratorKeysEnabled = debug;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsNonClientRegionSupportEnabled = true;
        core.SetVirtualHostNameToFolderMapping(HostName, Path.Combine(AppContext.BaseDirectory, "web"), CoreWebView2HostResourceAccessKind.DenyCors);

        var start = StartUri(page);
        core.NavigationStarting += (_, e) =>
        {
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) && !SameOrigin(target, start))
            {
                e.Cancel = true;
                OpenExternal(e.Uri);
            }
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.WebMessageReceived += (_, e) => onMessage(e.WebMessageAsJson);
        core.Navigate(start.ToString());
    }

    public static void Post(WebView2? web, object message)
    {
        if (web?.CoreWebView2 is { } core)
        {
            core.PostWebMessageAsJson(JsonSerializer.Serialize(message, DeskPalJson.Options));
        }
    }

    private static Uri StartUri(string page)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("DESKPAL_UI_URL") is { Length: > 0 } dev)
        {
            return new Uri($"{dev.TrimEnd('/')}/{page}");
        }
#endif
        return new Uri($"https://{HostName}/{page}");
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        a.Scheme == b.Scheme && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;
}

/// <summary>A message from the page: <c>{ id, type, ...args }</c>.</summary>
public sealed record WebCommand(int? Id, string Type, JsonElement Args)
{
    public static WebCommand? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is null)
            {
                return null;
            }

            int? id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : null;
            return new WebCommand(id, type, root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string String(string name) =>
        Args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    public string? OptionalString(string name) =>
        Args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public bool Bool(string name) => Args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public Guid Guid(string name) => System.Guid.TryParse(String(name), out var id) ? id : throw new FormatException($"{name} 不是有效的 ID");
}

/// <summary>Reply to a <see cref="WebCommand"/>. Error text is shown to the user as is.</summary>
public sealed record CommandResult(bool Ok, string? Error = null, object? Data = null)
{
    public static CommandResult Success { get; } = new(true);

    public static CommandResult Fail(string error) => new(false, error);
}