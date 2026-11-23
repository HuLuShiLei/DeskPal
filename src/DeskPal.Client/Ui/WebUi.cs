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

    /// <summary>Outside the Velopack current folder, which is replaced on every update.</summary>
    public static string UserDataFolder => AppPaths.WebView2Directory;

    /// <summary>Shown while the page loads. Matches --bg in styles.css for the current Windows app theme.</summary>
    public static System.Drawing.Color Background => ColorFor(null);

    public static System.Windows.Media.SolidColorBrush BackgroundBrush => BrushFor(null);

    /// <summary>system follows Windows. light and dark are fixed.</summary>
    public static System.Drawing.Color ColorFor(string? theme) => ResolveDark(theme)
        ? System.Drawing.Color.FromArgb(0x20, 0x20, 0x20)
        : System.Drawing.Color.FromArgb(0xE8, 0xEA, 0xEF);

    public static System.Windows.Media.SolidColorBrush BrushFor(string? theme)
    {
        var c = ColorFor(theme);
        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
    }

    /// <summary>Matches --surface in styles.css, the background of the panel's content area.</summary>
    public static System.Windows.Media.SolidColorBrush SurfaceBrushFor(string? theme) => ResolveDark(theme)
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2B, 0x2B, 0x2B))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF1, 0xF2, 0xF5));

    public static bool ResolveDark(string? theme) => theme switch
    {
        "dark" => true,
        "light" => false,
        _ => IsDarkTheme(),
    };

    public static string Page(string page, string? theme)
    {
        var value = theme is "light" or "dark" ? theme : "system";
        return page + (page.Contains('?') ? "&" : "?") + "theme=" + value + "&lang=" + UiLanguage.Current;
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
            UiLanguage.Format(Strings.Runtime_Missing, consequence),
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
        // A new environment each time: one whose browser process has exited, or whose creation failed while the
        // previous instance was still shutting down after an update, must not be reused.
        var environment = await CoreWebView2Environment.CreateAsync(null, UserDataFolder);
        await web.EnsureCoreWebView2Async(environment);
        var core = web.CoreWebView2;
#if DEBUG
        const bool debug = true;
#else
        const bool debug = false;
#endif
        core.Settings.AreDevToolsEnabled = debug;
        core.Settings.AreDefaultContextMenusEnabled = false;
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
