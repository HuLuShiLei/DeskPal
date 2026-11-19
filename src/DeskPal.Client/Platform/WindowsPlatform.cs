using System.Security.Cryptography;
using System.Windows;
using DeskPal.Client.Services;
using DeskPal.Client.Ui;

namespace DeskPal.Client.Platform;

/// <summary>Windows side of <see cref="IClientPlatform"/>. Dialogs and the clipboard must be used on the UI thread.</summary>
public sealed class WindowsPlatform : IClientPlatform
{
    public string Name => "windows";

    public bool AutoStartEnabled => AutoStart.IsEnabled();

    public void SetAutoStart(bool enabled) => AutoStart.Set(enabled);

    public void CopyText(string text) => Clipboard.SetText(text);

    public Task<string?> PickSaveFileAsync(string title, string fileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = title, FileName = fileName, Filter = Strings.Export_Filter };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> PickOpenFileAsync(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = Strings.Export_Filter };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> PickFolderAsync(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }

    public void OpenExternal(string target) => WebUi.OpenExternal(target);

    public IUserPrompt FallbackPrompt => MessageBoxPrompt.Instance;
}

/// <summary>Windows DPAPI for the current Windows user.</summary>
public sealed class DpapiProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}

/// <summary>Fallback when the panel page cannot be shown.</summary>
public sealed class MessageBoxPrompt : IUserPrompt
{
    public static MessageBoxPrompt Instance { get; } = new();

    public Task<bool> AskAsync(PromptKind kind, string message)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsVisible);
        var buttons = kind == PromptKind.Confirm ? MessageBoxButton.OKCancel : MessageBoxButton.OK;
        var image = kind switch
        {
            PromptKind.Confirm => MessageBoxImage.Question,
            PromptKind.Warning => MessageBoxImage.Warning,
            _ => MessageBoxImage.Information,
        };
        var result = owner is null
            ? MessageBox.Show(message, "DeskPal", buttons, image)
            : MessageBox.Show(owner, message, "DeskPal", buttons, image);
        return Task.FromResult(result == MessageBoxResult.OK);
    }
}
