using System.Diagnostics;
using AppKit;
using DeskPal.Client.Services;
using DeskPal.Client.Ui;
using Foundation;
using UniformTypeIdentifiers;
using UserNotifications;

namespace DeskPal.Client.Platform;

/// <summary>macOS side of <see cref="IClientPlatform"/>. Panels and the pasteboard are used on the main thread.</summary>
public sealed class MacPlatform : IClientPlatform
{
    public string Name => "mac";

    public bool AutoStartEnabled => LaunchAgent.IsEnabled;

    public void SetAutoStart(bool enabled) => LaunchAgent.Set(enabled);

    public void CopyText(string text) => MainThread.Invoke(() =>
    {
        var board = NSPasteboard.GeneralPasteboard;
        board.ClearContents();
        board.SetStringForType(text, NSPasteboardType.String.GetConstant()!);
        return true;
    });

    public Task<string?> PickSaveFileAsync(string title, string fileName) => MainThread.InvokeAsync(() =>
    {
        var panel = NSSavePanel.SavePanel;
        panel.Title = title;
        panel.NameFieldStringValue = fileName;
        panel.AllowedContentTypes = [UTTypes.Json];
        MainThread.Activate();
        return Task.FromResult(panel.RunModal() == (nint)(long)NSModalResponse.OK ? panel.Url?.Path : null);
    });

    public Task<string?> PickOpenFileAsync(string title) => MainThread.InvokeAsync(() =>
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = title;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = false;
        panel.AllowedContentTypes = [UTTypes.Json];
        MainThread.Activate();
        return Task.FromResult(panel.RunModal() == (nint)(long)NSModalResponse.OK ? panel.Url?.Path : null);
    });

    public Task<string?> PickFolderAsync(string title) => MainThread.InvokeAsync(() =>
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = title;
        panel.Message = title;
        panel.CanChooseFiles = false;
        panel.CanChooseDirectories = true;
        panel.CanCreateDirectories = true;
        panel.AllowsMultipleSelection = false;
        MainThread.Activate();
        return Task.FromResult(panel.RunModal() == (nint)(long)NSModalResponse.OK ? panel.Url?.Path : null);
    });

    public void OpenExternal(string target)
    {
        var url = Directory.Exists(target) || File.Exists(target) ? NSUrl.FromFilename(target) : NSUrl.FromString(target);
        if (url is not null)
        {
            MainThread.Post(() => NSWorkspace.SharedWorkspace.OpenUrl(url));
        }
    }

    public IUserPrompt FallbackPrompt { get; } = new AlertPrompt();

    /// <summary>Banner in Notification Center, e.g. "you were removed from a room". Asks for permission the first time.</summary>
    public static void Notify(string title, string message)
    {
        try
        {
            var center = UNUserNotificationCenter.Current;
            center.RequestAuthorization(UNAuthorizationOptions.Alert, (granted, _) =>
            {
                if (!granted)
                {
                    return;
                }

                var content = new UNMutableNotificationContent { Title = title, Body = message };
                var request = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, null);
                center.AddNotificationRequest(request, null);
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }

    /// <summary>The app bundle (…/DeskPal.app), or null when run from a build folder without one.</summary>
    public static string? BundlePath
    {
        get
        {
            var path = NSBundle.MainBundle.BundlePath;
            return path is not null && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? path : null;
        }
    }

    /// <summary>Starts a new copy of the app, e.g. back to the login page. The current one must exit right after.</summary>
    public static void Relaunch(string? arg)
    {
        if (BundlePath is { } bundle)
        {
            var args = new List<string> { "-n", bundle };
            if (arg is not null)
            {
                args.AddRange(["--args", arg]);
            }

            Process.Start("/usr/bin/open", args);
        }
        else if (Environment.ProcessPath is { } exe)
        {
            Process.Start(exe, arg ?? "");
        }
    }
}

/// <summary>Fallback when the panel page cannot be shown.</summary>
public sealed class AlertPrompt : IUserPrompt
{
    public Task<bool> AskAsync(PromptKind kind, string message) => MainThread.InvokeAsync(() =>
    {
        using var alert = new NSAlert
        {
            MessageText = "DeskPal",
            InformativeText = message,
            AlertStyle = kind == PromptKind.Warning ? NSAlertStyle.Warning : NSAlertStyle.Informational,
        };
        alert.AddButton(Strings.Common_Ok);
        if (kind == PromptKind.Confirm)
        {
            alert.AddButton(Strings.Common_Cancel);
        }

        MainThread.Activate();
        return Task.FromResult(alert.RunModal() == (nint)(long)NSAlertButtonReturn.First);
    });
}

/// <summary>
/// Start at login through a per-user LaunchAgent (~/Library/LaunchAgents). It opens the app bundle in the
/// background with --minimized, the same as the Windows Run key. No admin rights, works without code signing.
/// </summary>
internal static class LaunchAgent
{
    private const string Label = "com.hulushilei.deskpal.autostart";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    public static bool IsEnabled => File.Exists(PlistPath);

    public static void Set(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(PlistPath);
            return;
        }

        var program = MacPlatform.BundlePath is { } bundle
            ? new[] { "/usr/bin/open", "-g", "-a", bundle, "--args", "--minimized" }
            : new[] { Environment.ProcessPath ?? "", "--minimized" };
        using var plist = new NSMutableDictionary
        {
            [new NSString("Label")] = new NSString(Label),
            [new NSString("ProgramArguments")] = NSArray.FromStrings(program),
            [new NSString("RunAtLoad")] = NSNumber.FromBoolean(true),
            [new NSString("ProcessType")] = new NSString("Interactive"),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        if (!plist.WriteToUrl(NSUrl.FromFilename(PlistPath), out var error))
        {
            throw new IOException($"Writing the login item failed: {PlistPath} ({error?.LocalizedDescription})");
        }
    }
}
