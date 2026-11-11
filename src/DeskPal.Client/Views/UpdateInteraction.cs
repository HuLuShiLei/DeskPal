using System.Windows;
using DeskPal.Client.Services;

namespace DeskPal.Client.Views;

public static class UpdateInteraction
{
    public static async Task CheckAsync(UpdateService updates, Func<bool, Task> exitAsync, bool quietWhenCurrent)
    {
        var result = await updates.CheckAsync();
        switch (result.Kind)
        {
            case UpdateCheckKind.Ready:
                if (Confirm(result.Message + "要立即重启并更新吗？"))
                {
                    await exitAsync(true);
                }

                break;
            case UpdateCheckKind.DowngradeAvailable:
                if (Confirm(result.Message))
                {
                    var error = await updates.DownloadDowngradeAsync();
                    if (error is not null)
                    {
                        Info(error, MessageBoxImage.Warning);
                        return;
                    }

                    await exitAsync(true);
                }

                break;
            case UpdateCheckKind.Failed:
            case UpdateCheckKind.NotInstalled:
                Info(result.Message, result.Kind == UpdateCheckKind.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
                break;
            default:
                if (!quietWhenCurrent)
                {
                    Info(result.Message, MessageBoxImage.Information);
                }

                break;
        }
    }

    private static Window? VisibleOwner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsVisible);

    private static void Info(string message, MessageBoxImage image)
    {
        var owner = VisibleOwner();
        if (owner is null)
        {
            MessageBox.Show(message, "DeskPal", MessageBoxButton.OK, image);
        }
        else
        {
            MessageBox.Show(owner, message, "DeskPal", MessageBoxButton.OK, image);
        }
    }

    private static bool Confirm(string message)
    {
        var owner = VisibleOwner();
        return owner is null
            ? MessageBox.Show(message, "DeskPal", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK
            : MessageBox.Show(owner, message, "DeskPal", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    }
}