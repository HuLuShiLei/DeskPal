using DeskPal.Client.Platform;
using DeskPal.Client.Services;
using Velopack;

namespace DeskPal.Client;

public static class Program
{
    public static int Main(string[] args)
    {
        // 必须最先处理 Velopack 的安装、更新参数。已下载的更新只在退出或选择立即重启时安装。
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        SecretProtection.Current = new FileKeyProtector(SettingsStore.Directory);
        // GirCore 按模块登记本机库的名字（libgtk-4.so.1、libwebkitgtk-6.0.so.4），用到之前要先初始化。
        Gtk.Module.Initialize();
        WebKit.Module.Initialize();
        return new LinuxApp(args).Run();
    }
}
