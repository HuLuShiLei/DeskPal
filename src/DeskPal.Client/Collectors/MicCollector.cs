using System.Diagnostics;
using DeskPal.Client.Core.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace DeskPal.Client.Collectors;

/// <summary>
/// Windows records microphone usage per app under CapabilityAccessManager. LastUsedTimeStop == 0 means the app is using it now.
/// Only which app holds the microphone is read; no audio is accessed.
/// </summary>
public sealed class MicCollector(ActivityBus bus) : BackgroundService
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        MicSignal? last = null;
        do
        {
            var signal = Read();
            if (signal != last)
            {
                last = signal;
                bus.Publish(signal);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static MicSignal Read()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(ConsentStore);
            if (root is null)
            {
                return new MicSignal(false, null);
            }

            using (var nonPackaged = root.OpenSubKey("NonPackaged"))
            {
                if (nonPackaged is not null && FindActive(nonPackaged) is { } exeKey)
                {
                    return new MicSignal(true, DisplayNameFromPath(exeKey.Replace('#', '\\')));
                }
            }

            if (FindActive(root, skip: "NonPackaged") is { } packageKey)
            {
                return new MicSignal(true, packageKey.Split('_')[0]);
            }
        }
        catch
        {
        }

        return new MicSignal(false, null);
    }

    private static string? FindActive(RegistryKey parent, string? skip = null)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (name == skip)
            {
                continue;
            }

            using var key = parent.OpenSubKey(name);
            if (key?.GetValue("LastUsedTimeStart") is long start && start > 0 && key.GetValue("LastUsedTimeStop") is long stop && stop == 0)
            {
                return name;
            }
        }

        return null;
    }

    private static string DisplayNameFromPath(string path)
    {
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
            {
                return description.Trim();
            }
        }
        catch
        {
        }

        return Path.GetFileNameWithoutExtension(path);
    }
}