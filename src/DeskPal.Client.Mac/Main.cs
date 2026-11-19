using AppKit;
using DeskPal.Client.Platform;
using DeskPal.Client.Services;
using Velopack;

namespace DeskPal.Client;

public static class Program
{
    public static void Main(string[] args)
    {
        // 必须最先处理 Velopack 的安装、更新参数。已下载的更新只在退出或选择立即重启时安装。
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        SecretProtection.Current = new FileKeyProtector(SettingsStore.Directory);
        NSApplication.Init();
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        app.Delegate = new AppDelegate(args);
        app.Run();
    }
}
