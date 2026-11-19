namespace DeskPal.Client.Core.Classification;

public static class Browsers
{
    /// <summary>
    /// Process names whose window shows a web page named by the address bar: Windows exe names without ".exe",
    /// and macOS bundle identifiers (the macOS client publishes the bundle identifier as the process name).
    /// </summary>
    public static readonly IReadOnlySet<string> Processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "360se", "360chrome", "qqbrowser", "sogouexplorer", "2345explorer",
        "com.apple.safari", "com.apple.safaritechnologypreview",
        "com.google.chrome", "com.google.chrome.beta", "com.google.chrome.dev", "com.google.chrome.canary", "org.chromium.chromium",
        "com.microsoft.edgemac", "com.microsoft.edgemac.beta", "com.microsoft.edgemac.dev",
        "org.mozilla.firefox", "org.mozilla.firefoxdeveloperedition", "org.mozilla.nightly",
        "com.brave.browser", "com.operasoftware.opera", "com.vivaldi.vivaldi", "company.thebrowser.browser",
        "com.qihoo.360browser", "com.tencent.qqbrowser",
    };

    public static bool IsBrowser(string? process) => process is not null && Processes.Contains(process);
}
