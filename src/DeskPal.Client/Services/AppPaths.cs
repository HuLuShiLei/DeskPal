using DeskPal.Client.Core.Diagnostics;
using Velopack.Locators;

namespace DeskPal.Client.Services;

/// <summary>
/// Installed builds keep logs and the WebView2 cache in the folder chosen during setup. Settings and rules stay in
/// %APPDATA%\DeskPal (<see cref="SettingsStore.Directory"/>) so uninstalling does not sign the user out.
/// Runs outside an install, or with a read-only install folder, use the per-user folders.
/// </summary>
public static class AppPaths
{
    private static readonly string? InstallRoot = InstallDirectory.Writable(LocateInstallRoot());

    public static string LogDirectory { get; } = Path.Combine(InstallRoot ?? SettingsStore.Directory, "logs");

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