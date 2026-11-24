namespace DeskPal.Client.Providers;

/// <summary>What kind of desktop session this is, which decides where the foreground window can come from.</summary>
/// <param name="Kind">x11, gnome (GNOME on Wayland) or other (any other Wayland desktop).</param>
/// <param name="Desktop">XDG_CURRENT_DESKTOP, for messages.</param>
public sealed record LinuxSession(string Kind, string Desktop)
{
    public static LinuxSession Detect()
    {
        var wayland = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        var kind = !wayland ? "x11" : desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase) ? "gnome" : "other";
        return new LinuxSession(kind, desktop);
    }
}

/// <summary>
/// The Linux official plugin's state for the settings page, written by its parts (<see cref="LinuxWindowSource"/>,
/// <see cref="BrowserBridge"/>) and read by <see cref="LinuxOfficialProvider"/>. Outlives the parts, which are created
/// afresh each time the plugin starts.
/// </summary>
public sealed class LinuxStatus
{
    public LinuxSession Session { get; } = LinuxSession.Detect();

    /// <summary>starting, ok, needsExtension, needsRelogin or unavailable.</summary>
    public string Window { get; private set; } = "starting";

    /// <summary>Browsers whose DeskPal extension reported in the last few minutes, by display name.</summary>
    public IReadOnlyList<string> Browsers { get; private set; } = [];

    /// <summary>Why the local address the browser extension talks to could not be opened; null when it is open.</summary>
    public string? BridgeError { get; private set; }

    public event Action? Changed;

    public void SetWindow(string state) => Update(() => Window == state, () => Window = state);

    public void SetBrowsers(IReadOnlyList<string> browsers) => Update(() => Browsers.SequenceEqual(browsers), () => Browsers = browsers);

    public void SetBridgeError(string? error) => Update(() => BridgeError == error, () => BridgeError = error);

    /// <summary>Back to the state before the parts ran, when the plugin is turned off.</summary>
    public void Reset()
    {
        Window = "starting";
        Browsers = [];
        BridgeError = null;
        Changed?.Invoke();
    }

    private void Update(Func<bool> same, Action set)
    {
        if (same())
        {
            return;
        }

        set();
        Changed?.Invoke();
    }
}
