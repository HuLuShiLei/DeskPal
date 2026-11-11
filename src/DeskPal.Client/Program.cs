using Velopack;

namespace DeskPal.Client;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 必须在 WPF 启动之前处理安装、更新和卸载参数。普通启动会直接返回。
        // 已下载的更新只在用户退出或选择立即重启时安装，避免退出登录或再次打开时被自动装上。
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}