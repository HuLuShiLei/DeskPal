using DeskPal.Client.Services;

namespace DeskPal.Client.Ui;

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

public static class UpdateInteraction
{
    public static async Task CheckAsync(UpdateService updates, Func<bool, Task> exitAsync, bool quietWhenCurrent, IUserPrompt prompt)
    {
        var result = await updates.CheckAsync(interactive: true);
        switch (result.Kind)
        {
            case UpdateCheckKind.Ready:
                if (await prompt.AskAsync(PromptKind.Confirm, result.Message + Strings.SentenceSeparator + Strings.Update_RestartNow))
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