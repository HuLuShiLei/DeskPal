---
paths:
  - "src/DeskPal.Client/Collectors/**"
  - "src/DeskPal.Client/Native/**"
  - "src/DeskPal.Client.Mac/Collectors/**"
  - "src/DeskPal.Client.Linux/Collectors/**"
  - "src/DeskPal.Client.Linux/Providers/**"
---
<!-- Generated from .cursor/rules/client-collectors.mdc by DeskPal.Tests build. Do not edit. -->

# 采集器

- **只用普通用户权限就能调用的 API。** 不截图、不装全局钩子（`SetWindowsHookEx`）、不注入、不读其他进程的内存。这些行为会被公司的 EDR 当成恶意软件。
- 用轮询（`PeriodicTimer`），只在值变化时 `bus.Publish(...)`，不要每个周期都发布。`bus` 是构造函数里的 `SignalSink`（不是 `ActivityBus`）：采集器是平台官方插件的一部分，发布的事实由插件宿主按插件列表决定用不用。唯一的例外是浏览器网站：每次浏览器窗口变化后都要发一次，哪怕没变（管线等它）。
- 采集器只发布事实（`ActivitySignal` 的子类型），不做分类，也不决定状态。分类在 `DeskPal.Client.Core`。
- 能读到内容的地方只取最少的信息：浏览器只取 host，空闲只取最后一次输入的时间戳。
- 所有系统调用都要 try/catch。单个采集器失败只记 Debug 日志，不能拖垮整个程序。
- 可能很慢的调用（UI Automation）放到 `Task.Run` 里，加超时；上一次没返回就跳过这一轮。
- 新增采集器后，把它的类型加进平台官方插件的部件列表（Windows `App.xaml.cs`、macOS `AppDelegate.cs` 的 `AddOfficialProvider(...)`，Linux `LinuxOfficialProvider`），不要用 `AddHostedService` 单独注册。完整步骤见 skill `deskpal-add-signal`。
- 用户插件（插件文件夹里的程序）遵守同样的红线，见 skill `deskpal-write-provider`。