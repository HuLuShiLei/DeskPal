using System.Net;
using System.Text;
using System.Text.Json;
using DeskPal.Client.Core.Pipeline;
using DeskPal.Client.Core.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Providers;

/// <summary>
/// Part of the Linux official plugin: supplies the site of the foreground browser. On Linux nothing outside the
/// browser can read its address reliably, so the DeskPal browser extension (browser-extension/) tells us: it posts the
/// host of the active tab, and whether its browser has focus, to http://127.0.0.1:<see cref="Port"/>/v1/tab.
/// Only extensions get in: a request from a web page carries an http(s) Origin and is refused, and the custom
/// X-DeskPal header makes the browser ask first (CORS), which only extension origins pass. Nothing is ever sent
/// back but an empty answer, so a page that tried could at most claim a site, never read one.
/// The extension sends only the host, never the address, and nothing from private windows.
/// When the foreground window (from whichever plugin supplies it) is a browser, the host reported by that browser's
/// extension is published; after every browser window change it is published again, which the pipeline waits for.
/// </summary>
public sealed class BrowserBridge(SignalSink sink, ActivityBus bus, LinuxStatus status, ILogger<BrowserBridge> logger) : BackgroundService
{
    public const int Port = 24913;
    private const int MaxBody = 4096;
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(2.5);
    private static readonly TimeSpan RetryOpen = TimeSpan.FromSeconds(30);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Connection> _connections = [];
    private ForegroundSignal? _foreground;
    private string? _published;
    private bool _publishedOnce;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = bus.Subscribe();
        try
        {
            await Task.WhenAll(ServeAsync(stoppingToken), FollowForegroundAsync(reader, stoppingToken), ExpireAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            bus.Unsubscribe(reader);
        }
    }

    /// <summary>Keeps the local address open; when the port is taken, says so in Settings and tries again later.</summary>
    private async Task ServeAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                logger.LogWarning(ex, "Opening 127.0.0.1:{Port} for the browser extension failed", Port);
                status.SetBridgeError(ex.Message);
                await Task.Delay(RetryOpen, cancellation);
                continue;
            }

            status.SetBridgeError(null);
            logger.LogInformation("Waiting for the browser extension on 127.0.0.1:{Port}", Port);
            using var stop = cancellation.Register(listener.Stop);
            while (!cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    break;
                }

                try
                {
                    await HandleAsync(context);
                }
                catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
                {
                    logger.LogDebug(ex, "A request from the browser extension broke off");
                }
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var origin = request.Headers["Origin"];
        var fromExtension = IsExtensionOrigin(origin);
        if (origin is not null && !fromExtension)
        {
            // A web page, or a sandboxed frame ("null"). Pages have no business here.
            Finish(response, 403);
            return;
        }

        if (fromExtension)
        {
            response.Headers["Access-Control-Allow-Origin"] = origin;
            response.Headers["Vary"] = "Origin";
        }

        var path = request.Url?.AbsolutePath ?? "";
        switch (request.HttpMethod, path)
        {
            case ("OPTIONS", "/v1/tab" or "/v1/ping"):
                if (!fromExtension)
                {
                    Finish(response, 403);
                    return;
                }

                response.Headers["Access-Control-Allow-Methods"] = "GET, POST";
                response.Headers["Access-Control-Allow-Headers"] = "content-type, x-deskpal";
                response.Headers["Access-Control-Allow-Private-Network"] = "true";
                response.Headers["Access-Control-Max-Age"] = "600";
                Finish(response, 204);
                return;

            case ("GET", "/v1/ping"):
                var body = Encoding.UTF8.GetBytes("{\"app\":\"DeskPal\",\"protocol\":1}");
                response.ContentType = "application/json";
                response.StatusCode = 200;
                await response.OutputStream.WriteAsync(body);
                response.Close();
                return;

            case ("POST", "/v1/tab"):
                if (request.Headers["X-DeskPal"] != "1" || request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
                {
                    Finish(response, 400);
                    return;
                }

                if (request.ContentLength64 > MaxBody)
                {
                    Finish(response, 413);
                    return;
                }

                var report = await ReadReportAsync(request);
                if (report is null)
                {
                    Finish(response, 400);
                    return;
                }

                Finish(response, 204);
                Accept(report);
                return;

            default:
                Finish(response, 404);
                return;
        }
    }

    internal static bool IsExtensionOrigin(string? origin) =>
        origin is not null
        && (origin.StartsWith("chrome-extension://", StringComparison.Ordinal) || origin.StartsWith("moz-extension://", StringComparison.Ordinal));

    private static void Finish(HttpListenerResponse response, int status)
    {
        response.StatusCode = status;
        response.ContentLength64 = 0;
        response.Close();
    }

    private static async Task<Report?> ReadReportAsync(HttpListenerRequest request)
    {
        var buffer = new byte[MaxBody + 1];
        var length = 0;
        int read;
        while ((read = await request.InputStream.ReadAsync(buffer.AsMemory(length))) > 0)
        {
            length += read;
            if (length > MaxBody)
            {
                return null;
            }
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("instance", out var instance) || instance.ValueKind != JsonValueKind.String
                || instance.GetString() is not { Length: > 0 and <= 64 } id)
            {
                return null;
            }

            var browser = root.TryGetProperty("browser", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
            var focused = root.TryGetProperty("focused", out var f) && f.ValueKind == JsonValueKind.True;
            var domain = root.TryGetProperty("domain", out var d) && d.ValueKind == JsonValueKind.String ? ProviderProtocol.NormalizeDomain(d.GetString()) : null;
            return new Report(id, BrowserFamilies.Normalize(browser), focused, domain);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Accept(Report report)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            if (!_connections.TryGetValue(report.Instance, out var connection))
            {
                logger.LogInformation("Browser extension connected: {Browser}", BrowserFamilies.Display(report.Family));
                connection = new Connection(report.Instance);
                _connections[report.Instance] = connection;
            }

            if (report.Focused && !connection.Focused)
            {
                connection.FocusedAt = now;
            }

            connection.Family = report.Family;
            connection.Focused = report.Focused;
            connection.Domain = report.Domain;
            connection.LastSeen = now;
            UpdateLocked(force: false);
        }
    }

    private async Task FollowForegroundAsync(System.Threading.Channels.ChannelReader<ActivitySignal> reader, CancellationToken cancellation)
    {
        await foreach (var signal in reader.ReadAllAsync(cancellation))
        {
            if (signal is ForegroundSignal foreground)
            {
                lock (_lock)
                {
                    _foreground = foreground;

                    // Answer every browser window change, even with the same site: the pipeline waits for it.
                    UpdateLocked(force: foreground.IsBrowser);
                }
            }
        }
    }

    private async Task ExpireAsync(CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(cancellation))
        {
            lock (_lock)
            {
                var cutoff = DateTimeOffset.UtcNow - Fresh;
                foreach (var stale in _connections.Values.Where(c => c.LastSeen < cutoff).ToList())
                {
                    logger.LogInformation("Browser extension went quiet: {Browser}", BrowserFamilies.Display(stale.Family));
                    _connections.Remove(stale.Instance);
                }

                UpdateLocked(force: false);
            }
        }
    }

    private void UpdateLocked(bool force)
    {
        status.SetBrowsers(_connections.Values.Select(c => BrowserFamilies.Display(c.Family)).Distinct().Order(StringComparer.Ordinal).ToList());
        var domain = _foreground is { IsBrowser: true } foreground
            ? Pick(BrowserFamilies.OfProcess(foreground.ProcessName), _connections.Values)?.Domain
            : null;
        if (force || !_publishedOnce || domain != _published)
        {
            _published = domain;
            _publishedOnce = true;
            sink.Publish(new BrowserDomainSignal(domain));
        }
    }

    /// <summary>
    /// The extension that speaks for the foreground browser: the same kind of browser, focused first, most recently
    /// focused next. When the kind is unknown or no extension of that kind reports (a browser that names itself
    /// differently), the only focused browser, if exactly one is.
    /// </summary>
    internal static Connection? Pick(string? family, IEnumerable<Connection> connections)
    {
        var list = connections.ToList();
        if (family is not null)
        {
            var same = list.Where(c => c.Family == family).ToList();
            var chosen = same.Where(c => c.Focused).MaxBy(c => c.FocusedAt) ?? same.MaxBy(c => c.FocusedAt);
            if (chosen is not null)
            {
                return chosen;
            }
        }

        var focused = list.Where(c => c.Focused).ToList();
        return focused.Count == 1 ? focused[0] : null;
    }

    private sealed record Report(string Instance, string Family, bool Focused, string? Domain);

    internal sealed class Connection(string instance)
    {
        public string Instance { get; } = instance;

        public string Family { get; set; } = "other";

        public bool Focused { get; set; }

        public DateTimeOffset FocusedAt { get; set; }

        public DateTimeOffset LastSeen { get; set; }

        public string? Domain { get; set; }
    }
}

/// <summary>Kinds of browser, so a report from an extension can be matched with the foreground process.</summary>
internal static class BrowserFamilies
{
    private static readonly Dictionary<string, string> ByProcess = new(StringComparer.OrdinalIgnoreCase)
    {
        ["firefox"] = "firefox", ["firefox-esr"] = "firefox", ["firefox-bin"] = "firefox", ["firefox-developer-edition"] = "firefox",
        ["librewolf"] = "firefox", ["waterfox"] = "firefox", ["floorp"] = "firefox", ["zen"] = "firefox", ["zen-bin"] = "firefox",
        ["msedge"] = "edge", ["microsoft-edge"] = "edge", ["microsoft-edge-stable"] = "edge", ["microsoft-edge-beta"] = "edge", ["microsoft-edge-dev"] = "edge",
        ["chrome"] = "chrome", ["google-chrome"] = "chrome", ["google-chrome-stable"] = "chrome", ["google-chrome-beta"] = "chrome",
        ["chromium"] = "chrome", ["chromium-browser"] = "chrome",
        ["brave"] = "brave", ["brave-browser"] = "brave",
        ["vivaldi"] = "vivaldi", ["vivaldi-bin"] = "vivaldi",
        ["opera"] = "opera",
    };

    private static readonly Dictionary<string, string> Names = new()
    {
        ["firefox"] = "Firefox", ["edge"] = "Edge", ["chrome"] = "Chrome", ["brave"] = "Brave", ["vivaldi"] = "Vivaldi", ["opera"] = "Opera", ["other"] = "?",
    };

    public static string? OfProcess(string process) => ByProcess.TryGetValue(process, out var family) ? family : null;

    /// <summary>What the extension said it runs in, as one of the known kinds, or "other".</summary>
    public static string Normalize(string? reported)
    {
        var value = reported?.Trim().ToLowerInvariant() ?? "";
        return Names.ContainsKey(value) ? value : "other";
    }

    public static string Display(string family) => Names.TryGetValue(family, out var name) ? name : family;
}
