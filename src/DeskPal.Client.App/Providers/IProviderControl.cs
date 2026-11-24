using DeskPal.Client.Core.Ui;
using DeskPal.Client.Ui;

namespace DeskPal.Client.Providers;

/// <summary>The settings page's view of the plugin list and the changes it can make.</summary>
public interface IProviderControl
{
    PluginsView View { get; }

    /// <summary>Raised from any thread when <see cref="View"/> changed.</summary>
    event Action? Changed;

    CommandResult SetEnabled(string id, bool enabled);

    /// <summary>Moves a plugin up (negative) or down (positive) the list; higher in the list wins.</summary>
    CommandResult Move(string id, int offset);

    Task<CommandResult> RunActionAsync(string id, string action);

    /// <summary>Reads the plugins folder again.</summary>
    void Refresh();

    /// <summary>Where the user's own plugins go. Created when missing.</summary>
    string Folder { get; }
}
