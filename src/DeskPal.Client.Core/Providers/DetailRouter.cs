using DeskPal.Client.Core.Pipeline;

namespace DeskPal.Client.Core.Providers;

/// <summary>
/// What a detail plugin said about the window of <paramref name="Process"/>. Handled by the plugin host, never put on
/// the activity bus. <paramref name="Window"/> is the window title the plugin saw, or null for any window of the process;
/// a null <paramref name="Title"/> takes the detail back.
/// </summary>
public sealed record DetailSignal(string Process, string? Window, string? Title) : ActivitySignal;

/// <summary>One detail plugin as the router sees it. <paramref name="Apps"/> compares without case.</summary>
public sealed record DetailEntry(string Id, bool Enabled, bool Healthy, IReadOnlySet<string> Apps);

/// <summary>
/// Puts the title a detail plugin reported in place of the window title, for the apps that plugin names. The window
/// itself still comes from whoever owns <see cref="ProviderCapability.Foreground"/>; only the title changes, so the
/// classification is the same, and the no-title rules and the privacy filter run on the result as before.
/// A detail applies while the window belongs to one of the plugin's apps, the reported process is the foreground one
/// and, when the plugin said which window it saw, the window title still matches. The first enabled, healthy plugin in
/// the list wins. Browsers keep their own title: a title change there makes the pipeline wait for the site again.
/// Each plugin's last report is kept until it reports again, takes it back or stops, so switching away and back
/// needs no new report. Not thread-safe; the plugin host calls it under its lock.
/// </summary>
public sealed class DetailRouter
{
    private readonly Dictionary<string, DetailSignal> _reports = [];
    private IReadOnlyList<DetailEntry> _entries = [];
    private ForegroundSignal? _raw;
    private ForegroundSignal? _published;

    /// <summary>The plugin whose title is in use, or null.</summary>
    public string? ActiveId { get; private set; }

    /// <summary>The owner reported a window. Returns what to publish for it.</summary>
    public ForegroundSignal Foreground(ForegroundSignal raw)
    {
        _raw = raw;
        var now = Resolve()!;
        _published = now;
        return now;
    }

    /// <summary>A plugin reported a detail. Returns the window to publish when that changed what is shown.</summary>
    public ForegroundSignal? Report(string id, DetailSignal detail)
    {
        if (detail.Title is null)
        {
            _reports.Remove(id);
        }
        else
        {
            _reports[id] = detail;
        }

        return Republish();
    }

    /// <summary>Forgets a plugin that was turned off, removed or restarted.</summary>
    public ForegroundSignal? Forget(string id) => _reports.Remove(id) ? Republish() : null;

    /// <summary>The list, its switches or a plugin's health changed.</summary>
    public ForegroundSignal? Update(IEnumerable<DetailEntry> ordered)
    {
        _entries = ordered.ToList();
        return Republish();
    }

    private ForegroundSignal? Republish()
    {
        var now = Resolve();
        if (now == _published)
        {
            return null;
        }

        _published = now;
        return now;
    }

    private ForegroundSignal? Resolve()
    {
        ActiveId = null;
        if (_raw is not { } raw || raw.IsBrowser)
        {
            return _raw;
        }

        foreach (var entry in _entries)
        {
            if (!entry.Enabled || !entry.Healthy || !entry.Apps.Contains(raw.ProcessName)
                || !_reports.TryGetValue(entry.Id, out var detail) || !Matches(detail, raw))
            {
                continue;
            }

            ActiveId = entry.Id;
            return raw with { WindowTitle = detail.Title! };
        }

        return raw;
    }

    private static bool Matches(DetailSignal detail, ForegroundSignal raw) =>
        string.Equals(detail.Process, raw.ProcessName, StringComparison.OrdinalIgnoreCase)
        && (detail.Window is null || string.Equals(detail.Window, raw.WindowTitle.Trim(), StringComparison.Ordinal));
}