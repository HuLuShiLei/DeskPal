using DeskPal.Client.Ui;

namespace DeskPal.Client.Platform;

/// <summary>
/// What the shared client needs from the operating system. Windows, macOS and Linux each provide one; the panel commands
/// (<see cref="PanelBridge"/>) only talk to this, never to WPF or AppKit directly.
/// </summary>
public interface IClientPlatform
{
    /// <summary>"windows", "mac" or "linux" (<see cref="DeskPal.Shared.Presence.ClientKinds"/>). Sent to the page so it can pick icons and wording.</summary>
    string Name { get; }

    bool AutoStartEnabled { get; }

    void SetAutoStart(bool enabled);

    void CopyText(string text);

    /// <summary>Asks where to save a JSON file. null when the user cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string fileName);

    /// <summary>Asks for a JSON file to open. null when the user cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title);

    /// <summary>Asks for a folder. null when the user cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Opens a web link in the default browser, or a folder in the file manager.</summary>
    void OpenExternal(string target);

    /// <summary>Used for update prompts when the panel page cannot be shown.</summary>
    IUserPrompt FallbackPrompt { get; }
}
