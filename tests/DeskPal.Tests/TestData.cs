using DeskPal.Client.Core.Pipeline;

namespace DeskPal.Tests;

internal static class TestData
{
    public static ActivitySnapshot App(string process, string title = "", string? path = null, bool fullscreen = false) =>
        ActivitySnapshot.Empty.Apply(new ForegroundSignal(process, path, process, title, fullscreen, IsBrowser: false));

    public static ActivitySnapshot Browser(string? domain, string title = "", bool fullscreen = false)
    {
        var snapshot = ActivitySnapshot.Empty.Apply(new ForegroundSignal("chrome", null, "Google Chrome", title, fullscreen, IsBrowser: true));
        return domain is null ? snapshot : snapshot.Apply(new BrowserDomainSignal(domain));
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}