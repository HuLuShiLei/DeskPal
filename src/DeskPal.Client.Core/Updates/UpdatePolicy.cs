namespace DeskPal.Client.Core.Updates;

/// <summary>客户端更新的通道和是否自动安装。安装和下载仍由 Velopack 完成。</summary>
public static class UpdatePolicy
{
    public const string RepositoryUrl = "https://github.com/HuLuShiLei/DeskPal";
    public const string StableChannel = "win";
    public const string BetaChannel = "beta";

    /// <summary>没有保存过选项时，安装的是测试版就继续接收测试版。</summary>
    public static bool ReceiveBeta(bool? setting, bool installedPrerelease) => setting ?? installedPrerelease;

    public static string Channel(bool receiveBeta) => receiveBeta ? BetaChannel : StableChannel;

    /// <summary>
    /// macOS 的包按芯片分开发布（osx-arm64、osx-x64），正式版和测试版各一个通道，和 Windows 的 win、beta 互不影响。
    /// <paramref name="macArchitecture"/> 为空时是 Windows。
    /// </summary>
    public static string Channel(bool receiveBeta, string? macArchitecture) => macArchitecture is null
        ? Channel(receiveBeta)
        : receiveBeta ? $"osx-{macArchitecture}-beta" : $"osx-{macArchitecture}";

    /// <summary>降级（从测试版回到较低的正式版）必须先问过用户。</summary>
    public static bool ApplyWithoutAsking(bool isDowngrade) => !isDowngrade;
}