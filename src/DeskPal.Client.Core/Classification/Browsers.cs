namespace DeskPal.Client.Core.Classification;

public static class Browsers
{
    /// <summary>Process names, without ".exe", whose window shows a web page named by the address bar.</summary>
    public static readonly IReadOnlySet<string> Processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "360se", "360chrome", "qqbrowser", "sogouexplorer", "2345explorer",
    };

    public static bool IsBrowser(string? process) => process is not null && Processes.Contains(process);
}
