using System.Windows;
using DeskPal.Client.Services;

namespace DeskPal.Client.Views;

public enum PromptKind
{
    Confirm,
    Info,
    Warning,
}

/// <summary>Asks the user something. Info and warning prompts return true once dismissed.</summary>
public interface IUserPrompt
{
    Task<bool> AskAsync(PromptKind kind, string message);
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

public static class UpdateInteraction
{
    public static async Task CheckAsync(UpdateService updates, Func<bool, Task> exitAsync, bool quietWhenCurrent, IUserPrompt prompt)
    {
        var result = await updates.CheckAsync();
        switch (result.Kind)
        {
            case UpdateCheckKind.Ready:
                if (await prompt.AskAsync(PromptKind.Confirm, result.Message + "要立即重启并更新吗？"))
                {
                    await exitAsync(true);
                }

                break;
            case UpdateCheckKind.DowngradeAvailable:
                if (await prompt.AskAsync(PromptKind.Confirm, result.Message))
                {
                    var error = await updates.DownloadDowngradeAsync();
                    if (error is not null)
                    {
                        await prompt.AskAsync(PromptKind.Warning, error);
                        return;
                    }

                    await exitAsync(true);
                }

                break;
            case UpdateCheckKind.Failed:
            case UpdateCheckKind.NotInstalled:
                await prompt.AskAsync(result.Kind == UpdateCheckKind.Failed ? PromptKind.Warning : PromptKind.Info, result.Message);
                break;
            default:
                if (!quietWhenCurrent)
                {
                    await prompt.AskAsync(PromptKind.Info, result.Message);
                }

                break;
        }
    }
}