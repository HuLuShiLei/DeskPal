using System.Diagnostics;
using System.Text.Json;
using DeskPal.Client.Platform;
using DeskPal.Shared;

namespace DeskPal.Client.Ui;

/// <summary>
/// WebKitGTK setup shared by the panel and the login window. Pages come from the web folder next to the program
/// through the deskpal:// scheme, so nothing listens on a port. The page posts JSON strings to
/// <c>window.webkit.messageHandlers.deskpal</c>, the same call as in macOS's WKWebView; the host answers by calling
/// <c>window.__deskpal(message)</c>.
/// </summary>
public sealed class WebHost
{
    public const string Scheme = "deskpal";
    private const string Host = "app";
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "web"));
    private static bool _schemeRegistered;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html",
        [".js"] = "text/javascript",
        [".mjs"] = "text/javascript",
        [".css"] = "text/css",
        [".json"] = "application/json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
    };

    private readonly Action<string> _onMessage;
    private readonly string _startUrl;
    private bool _closed;

    public WebHost(string page, string? theme, Action<string> onMessage)
    {
        _onMessage = onMessage;
        RegisterScheme();
        View = WebKit.WebView.New();
        View.SetHexpand(true);
        View.SetVexpand(true);
        var settings = View.GetSettings();
        settings.SetEnableDeveloperExtras(Debug);
        var color = new Gdk.RGBA();
        color.Parse(WindowChrome.Dark(theme) ? "#202020" : "#E8EAEF");
        View.SetBackgroundColor(color);

        var content = View.GetUserContentManager();
        WebKit.UserContentManager.ScriptMessageReceivedSignal.Connect(content, OnScriptMessage, false, "deskpal");
        content.RegisterScriptMessageHandler("deskpal", null);
        View.OnDecidePolicy += OnDecidePolicy;
        View.OnContextMenu += (_, _) => !Debug;
        View.OnWebProcessTerminated += (_, _) => ProcessTerminated?.Invoke();

        _startUrl = PageUrl(page, theme);
        View.LoadUri(_startUrl);
    }

    public WebKit.WebView View { get; }

    /// <summary>The page's process ended (crash or memory pressure). The owner creates the view again.</summary>
    public event Action? ProcessTerminated;

    private static bool Debug =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>Path and query under the web root, e.g. "index.html?page=login", plus theme, language and platform.</summary>
    public static string PageUrl(string page, string? theme)
    {
        var value = theme is "light" or "dark" ? theme : "system";
        var query = (page.Contains('?') ? "&" : "?") + "theme=" + value + "&lang=" + UiLanguage.Current + "&platform=linux";
#if DEBUG
        if (Environment.GetEnvironmentVariable("DESKPAL_UI_URL") is { Length: > 0 } dev)
        {
            return $"{dev.TrimEnd('/')}/{page}{query}";
        }
#endif
        return $"{Scheme}://{Host}/{page}{query}";
    }

    public void Post(object message)
    {
        var json = JsonSerializer.Serialize(message, DeskPalJson.Options);
        MainThread.Post(async () =>
        {
            if (_closed)
            {
                return;
            }

            try
            {
                await View.EvaluateJavascriptAsync($"window.__deskpal && window.__deskpal({json})");
            }
            catch (Exception ex)
            {
                // The page is gone or still loading; the next state push repeats what matters.
                System.Diagnostics.Debug.WriteLine(ex);
            }
        });
    }

    public void Close()
    {
        _closed = true;
        View.TryClose();
    }

    private void OnScriptMessage(WebKit.UserContentManager sender, WebKit.UserContentManager.ScriptMessageReceivedSignalArgs args)
    {
        if (!_closed && args.Value.IsString())
        {
            _onMessage(args.Value.ToString());
        }
    }

    /// <summary>Links to other origins open in the default browser.</summary>
    private bool OnDecidePolicy(WebKit.WebView sender, WebKit.WebView.DecidePolicySignalArgs args)
    {
        if (args.Decision is not WebKit.NavigationPolicyDecision navigation)
        {
            return false;
        }

        var uri = navigation.GetNavigationAction().GetRequest().GetUri();
        if (uri.StartsWith(Scheme + "://", StringComparison.Ordinal) || uri == "about:blank" || SameOrigin(uri, _startUrl))
        {
            return false;
        }

        navigation.Ignore();
        OpenExternal(uri);
        return true;
    }

    public static void OpenExternal(string target)
    {
        try
        {
            var info = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            info.ArgumentList.Add(target);
            // Programs started from an AppImage must not inherit its library path.
            info.Environment.Remove("LD_LIBRARY_PATH");
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
        }
    }

    private static bool SameOrigin(string a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var x) && Uri.TryCreate(b, UriKind.Absolute, out var y)
        && x.Scheme == y.Scheme && x.Host == y.Host && x.Port == y.Port;

    private static void RegisterScheme()
    {
        if (_schemeRegistered)
        {
            return;
        }

        _schemeRegistered = true;
        var context = WebKit.WebContext.GetDefault();
        context.RegisterUriScheme(Scheme, Serve);
        var security = context.GetSecurityManager();
        security.RegisterUriSchemeAsSecure(Scheme);
        security.RegisterUriSchemeAsCorsEnabled(Scheme);
    }

    /// <summary>Serves deskpal://app/… from the web folder.</summary>
    private static void Serve(WebKit.URISchemeRequest request)
    {
        var relative = Uri.UnescapeDataString((request.GetPath() ?? "/").TrimStart('/'));
        var path = Path.GetFullPath(Path.Combine(Root, relative.Length == 0 ? "index.html" : relative));
        byte[] body;
        var type = "text/plain";
        if (path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path))
        {
            body = File.ReadAllBytes(path);
            type = Types.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        }
        else
        {
            body = "Not found"u8.ToArray();
        }

        using var bytes = GLib.Bytes.New(body);
        var stream = Gio.MemoryInputStream.NewFromBytes(bytes);
        request.Finish(stream, body.Length, type);
    }
}
