using DeskPal.Client.Core.Diagnostics;
using Velopack.Locators;

namespace DeskPal.Client.Services;

/// <summary>
/// Windows: installed builds keep logs and the WebView2 cache in the folder chosen during setup. Settings and rules
/// stay in %APPDATA%\DeskPal (<see cref="SettingsStore.Directory"/>) so uninstalling does not sign the user out.
/// Runs outside an install, or with a read-only install folder, use the per-user folders.
/// macOS: nothing is written inside the app bundle, which would break its signature. Logs go to ~/Library/Logs/DeskPal.
/// </summary>
public static class AppPaths
{
    private static readonly string? InstallRoot = OperatingSystem.IsMacOS() ? null : InstallDirectory.Writable(LocateInstallRoot());

    public static string LogDirectory { get; } = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "DeskPal")
        : Path.Combine(InstallRoot ?? SettingsStore.Directory, "logs");

    /// <summary>Windows only.</summary>
    public static string WebView2Directory { get; } = Path.Combine(
        InstallRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPal"),
        "WebView2");

    private static string? LocateInstallRoot()
    {
        try
        {
            return VelopackLocator.Current.RootAppDir;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
