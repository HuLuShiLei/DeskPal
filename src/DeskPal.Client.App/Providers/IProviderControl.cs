using DeskPal.Client.Core.Ui;
using DeskPal.Client.Ui;

namespace DeskPal.Client.Providers;

/// <summary>
/// Lets the settings page show and change where the foreground window comes from. Only the Linux client registers
/// one; without it the panel has no provider setting.
/// </summary>
public interface IProviderControl
{
    ProviderView View { get; }

    /// <summary>Raised from any thread when <see cref="View"/> changed.</summary>
    event Action? Changed;

    /// <summary>Switches to a plugin in the providers folder, or back to the built-in detection with null.</summary>
    CommandResult Select(string? plugin);

    /// <summary>Installs what the built-in detection needs on this desktop (the GNOME Shell extension).</summary>
    Task<CommandResult> InstallHelperAsync();

    /// <summary>The folder plugins are loaded from. Created when missing.</summary>
    string Folder { get; }
}
