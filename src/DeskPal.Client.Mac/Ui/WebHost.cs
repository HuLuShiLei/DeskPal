using System.Text.Json;
using AppKit;
using CoreGraphics;
using DeskPal.Client.Platform;
using DeskPal.Shared;
using Foundation;
using WebKit;

namespace DeskPal.Client.Ui;

/// <summary>
/// WKWebView setup shared by the panel, the login window and the member cards. Pages come from Contents/Resources/web
/// through the deskpal:// scheme, so nothing listens on a port. The page posts JSON strings to
/// <c>window.webkit.messageHandlers.deskpal</c>; the host answers by calling <c>window.__deskpal(message)</c>.
/// </summary>
public sealed class WebHost : NSObject, IWKScriptMessageHandler, IWKNavigationDelegate, IWKUIDelegate
{
    public const string Scheme = "deskpal";
    private const string Host = "app";
    private readonly Action<string> _onMessage;

    public WebHost(CGRect frame, string page, string? theme, Action<string> onMessage)
    {
        _onMessage = onMessage;
        var configuration = new WKWebViewConfiguration();
        configuration.SetUrlSchemeHandler(new BundleSchemeHandler(), Scheme);
        configuration.UserContentController.AddScriptMessageHandler(this, "deskpal");
        configuration.Preferences.SetValueForKey(NSNumber.FromBoolean(Debug), new NSString("developerExtrasEnabled"));
        View = new WKWebView(frame, configuration)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            NavigationDelegate = this,
            UIDelegate = this,
            AllowsBackForwardNavigationGestures = false,
            AllowsMagnification = false,
        };
        // No white flash before the page paints; the window behind shows the page's background color.
        View.SetValueForKey(NSNumber.FromBoolean(false), new NSString("drawsBackground"));
        StartUrl = PageUrl(page, theme);
        View.LoadRequest(new NSUrlRequest(StartUrl));
    }

    public WKWebView View { get; }

    public NSUrl StartUrl { get; }

    /// <summary>The page's process ended (crash or memory pressure). The owner creates the view again.</summary>
    public event Action? ProcessTerminated;

    private static bool Debug =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>Path and query under the web root, e.g. "index.html?page=login", plus theme, language and platform.</summary>
    public static NSUrl PageUrl(string page, string? theme)
    {
        var value = theme is "light" or "dark" ? theme : "system";
        var query = (page.Contains('?') ? "&" : "?") + "theme=" + value + "&lang=" + UiLanguage.Current + "&platform=mac";
#if DEBUG
        if (Environment.GetEnvironmentVariable("DESKPAL_UI_URL") is { Length: > 0 } dev)
        {
            return new NSUrl($"{dev.TrimEnd('/')}/{page}{query}");
        }
#endif
        return new NSUrl($"{Scheme}://{Host}/{page}{query}");
    }

    public void Post(object message)
    {
        var json = JsonSerializer.Serialize(message, DeskPalJson.Options);
        MainThread.Post(() => View.EvaluateJavaScript($"window.__deskpal && window.__deskpal({json})", null!));
    }

    public void Close()
    {
        View.Configuration.UserContentController.RemoveScriptMessageHandler("deskpal");
        View.NavigationDelegate = null!;
        View.UIDelegate = null!;
        View.StopLoading();
        View.RemoveFromSuperview();
        View.Dispose();
    }

    [Export("userContentController:didReceiveScriptMessage:")]
    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
    {
        if (message.Body is NSString text)
        {
            _onMessage(text.ToString());
        }
    }

    /// <summary>Links to other origins open in the default browser.</summary>
    [Export("webView:decidePolicyForNavigationAction:decisionHandler:")]
    public void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
    {
        var url = navigationAction.Request.Url;
        if (url is null || url.Scheme == Scheme || SameOrigin(url, StartUrl) || url.AbsoluteString == "about:blank")
        {
            decisionHandler(WKNavigationActionPolicy.Allow);
            return;
        }

        decisionHandler(WKNavigationActionPolicy.Cancel);
        NSWorkspace.SharedWorkspace.OpenUrl(url);
    }

    [Export("webViewWebContentProcessDidTerminate:")]
    public void ContentProcessDidTerminate(WKWebView webView) => ProcessTerminated?.Invoke();

    /// <summary>target=_blank links.</summary>
    [Export("webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:")]
    public WKWebView? CreateWebView(WKWebView webView, WKWebViewConfiguration configuration, WKNavigationAction navigationAction, WKWindowFeatures windowFeatures)
    {
        if (navigationAction.Request.Url is { } url)
        {
            NSWorkspace.SharedWorkspace.OpenUrl(url);
        }

        return null;
    }

    private static bool SameOrigin(NSUrl a, NSUrl b) =>
        a.Scheme == b.Scheme && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;
}

/// <summary>Serves deskpal://app/… from the web folder in the app bundle.</summary>
internal sealed class BundleSchemeHandler : NSObject, IWKUrlSchemeHandler
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(NSBundle.MainBundle.ResourcePath ?? AppContext.BaseDirectory, "web"));

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

    [Export("webView:startURLSchemeTask:")]
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        var url = urlSchemeTask.Request.Url;
        var relative = Uri.UnescapeDataString((url.Path ?? "/").TrimStart('/'));
        var path = Path.GetFullPath(Path.Combine(Root, relative.Length == 0 ? "index.html" : relative));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
        {
            Respond(urlSchemeTask, url, 404, "text/plain", []);
            return;
        }

        var type = Types.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        Respond(urlSchemeTask, url, 200, type, File.ReadAllBytes(path));
    }

    [Export("webView:stopURLSchemeTask:")]
    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
    }

    private static void Respond(IWKUrlSchemeTask task, NSUrl url, int status, string type, byte[] body)
    {
        using var headers = new NSMutableDictionary
        {
            [new NSString("Content-Type")] = new NSString(type),
            [new NSString("Content-Length")] = new NSString(body.Length.ToString()),
            [new NSString("Cache-Control")] = new NSString("no-store"),
        };
        using var response = new NSHttpUrlResponse(url, status, "HTTP/1.1", headers);
        task.DidReceiveResponse(response);
        task.DidReceiveData(NSData.FromArray(body));
        task.DidFinish();
    }
}
