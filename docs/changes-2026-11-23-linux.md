# Linux 客户端与识别插件：改动记录（2026-11-23）

方案和取舍见 [plan-2026-11-23-linux-providers.md](plan-2026-11-23-linux-providers.md)；写插件见 [provider-guide.md](provider-guide.md)。本文记录实际改了什么、为什么、验证到哪一步。

## 结果

- 新增 Linux 客户端 `src/DeskPal.Client.Linux`（.NET 10，GTK 4 + WebKitGTK 6.0），和 Windows、macOS 用户在同一个房间，Server 没有改动。发布形式是 AppImage（x64）。
- “当前在用哪个窗口”由**识别来源**提供，三种：X11（内置）、GNOME 扩展（内置，随包发布）、识别插件（用户自己放的外部程序）。只在 Linux 上开放。
- 交付了插件教程：`docs/provider-guide.md`（给人）、`.cursor/skills/deskpal-write-provider/`（给 AI，含模板、例子、检查工具）。
- Windows、macOS 客户端的行为没有变化；没有给它们加插件入口。

## 用户没有逐条确认、由我决定的做法

开工前列给了用户，用户只回复了“开始做”和“读不到网址时只显示浏览中加标题可以接受”。以下几条按建议执行，需要调整请指出：

| 事项 | 做法 | 理由 |
|---|---|---|
| 插件形式 | 外部程序，标准输出每行一个 JSON | 任何语言都能写，崩溃不影响 DeskPal，协议容易讲清楚，适合让 AI 代写 |
| GNOME + Wayland | 随包发一个 GNOME Shell 扩展，在设置里一键安装，装后要重新登录 | Wayland 下只有 Shell 内部能知道当前窗口，没有别的办法 |
| 针对单个软件的增强插件 | 第一版不做 | 协议里一个时刻只有一个识别来源；以后要做再加 |
| 发布形式 | 只出 AppImage | Velopack 在 Linux 上只支持 AppImage，自动更新沿用同一套 |
| 置顶小卡片、面板置顶、面板定位 | Linux 上去掉 | Wayland 不允许程序自己定位窗口或置顶 |
| 浏览器网址 | 内置来源不读，浏览器显示“浏览中”加标题；插件可以提供域名 | Linux 上读地址栏要走辅助功能接口，各浏览器、各打包方式表现不一 |

## 新增

### `src/DeskPal.Client.Linux/`

| 文件 | 内容 |
|---|---|
| `Program.cs`、`LinuxApp.cs` | 入口和启动流程。`Gtk.Application` 保证单实例：再次启动只让已运行的实例打开面板，没有托盘的桌面靠这个打开面板。收到 SIGTERM 时正常退出 |
| `Platform/MainThread.cs` | GLib 主循环上的同步上下文和定时器 |
| `Platform/LinuxPlatform.cs` | `IClientPlatform` 的实现：文件对话框、通知、重启；`AutoStart`（`~/.config/autostart/deskpal.desktop`）；`LauncherEntry`（以 AppImage 运行时写 `~/.local/share/applications/io.github.hulushilei.DeskPal.desktop` 和图标，应用菜单和 Dock 才有名字和图标） |
| `Providers/ForegroundService.cs` | 选择并运行识别来源，实现 `IProviderControl`（设置页的“应用识别”） |
| `Providers/GnomeExtension.cs`、`gnome-extension/` | 扩展的安装、查询；扩展本身（GNOME Shell 45 到 50），只在会话总线上回答“当前窗口的标题、应用、进程” |
| `Native/` | `X11`（Xlib：活动窗口、空闲时间）、`Bus`（会话总线）、`LinuxProcess`（进程名的取法）、`DesktopEntries`（应用显示名） |
| `Collectors/` | 空闲（Mutter → X11 → logind）、锁屏（ScreenSaver 信号和 logind）、媒体（MPRIS） |
| `Ui/` | `WebHost`（WebKitGTK，页面走 `deskpal://app/`，不监听端口）、`WindowChrome`（隐藏系统标题栏、拖动）、`PanelController`、`LoginController`、`TrayIcon`（StatusNotifierItem + dbusmenu） |

### 共用层

| 文件 | 改动 |
|---|---|
| `Client.Core/Providers/ProviderProtocol.cs` | 插件协议 v1 的解析和上限（新文件），测试在 `tests/DeskPal.Tests/Providers/` |
| `Client.App/Providers/ExternalProvider.cs`、`IProviderControl.cs` | 运行插件进程、限速、失败后按 1/2/5/10/30/60 秒重启（新文件） |
| `Client.App/Platform/FileKeyProtector.cs` | 从 `DeskPal.Client.Mac/Platform/MacPlatform.cs` 移出，macOS 和 Linux 共用，代码未改 |
| `Client.App/Ui/PanelBridge.cs` | 可选的 `IProviderControl`；命令 `provider.select`、`provider.installHelper`、`provider.openFolder` |
| `Client.App/Services/AppSettings.cs`、`AppPaths.cs` | 设置项 `Provider`；Linux 的日志目录 `~/.local/state/DeskPal/logs` |
| `Client.Core/Ui/PanelState.cs` | `SettingsView.Provider`（`ProviderView`），非 Linux 为 null；8 个模拟数据文件（`web/src/mock/fixtures`）因此各多一行 `"provider": null` |
| `Client.Core/Classification/Browsers.cs` | 加入 Linux 上的浏览器进程名 |
| `Client.Core/Updates/UpdatePolicy.cs`、`UpdateService.cs` | 更新通道 `linux-x64`、`linux-x64-beta` |
| `Client.Core/Localization/Strings*.resx` | `Provider_NotFound`、`Provider_ExtensionInstalled` |
| `Shared/Presence/PresenceSnapshot.cs` | `ClientKinds.Linux` |

### 前端（`src/DeskPal.Client/web/src`）

`platform.ts`、`bridge.ts` 认识 `linux`；`ui.tsx` 非 Windows 用 SVG 图标，成员头像角标加 Linux；`SettingsPage.tsx` 新增“应用识别”一节（只在 Linux 出现），Linux 上隐藏面板置顶；`StatusPage.tsx` Linux 上隐藏置顶卡片按钮；`i18n` 新增 `provider.*`、`client.linux`。

### 其他

- `.cursor/skills/deskpal-write-provider/.gitattributes`：这个目录里的 `*.sh`、`*.py` 固定用 LF（带 CRLF 的脚本在 Linux 上跑不起来）。仓库其他文件不受影响。
- `.github/workflows/ci.yml` 是受保护文件，改动以补丁形式放在 `.local/ci-linux.patch`，没有直接改。内容：新增 `linux` 任务（ubuntu-24.04，发布、`vpk pack` 出 AppImage、合并进同一个 Release），并把 `src/DeskPal.Client.Linux/` 加入“客户端有改动”的判断。应用方法：`git apply .local/ci-linux.patch`。

## 验证

在一台 Ubuntu 24.04 的机器上实际运行过（GNOME Shell 46.0，和朋友 Ubuntu 上装的版本相同；.NET 10.0.115；WebKitGTK 6.0），不是只做了类型检查：

| 项目 | 结果 |
|---|---|
| 单元测试 | 401 个全部通过；前端 `npm run build` 通过 |
| X11 会话（Xvfb + openbox） | 注册、登录、面板、设置页正常；内置 X11 识别读到 xterm 的标题和进程；暂离（X11 空闲时间）正常 |
| 识别插件 | 放入示例插件、在设置里选择后首页显示插件给出的应用并正确分类；换回内置、DeskPal 退出时插件进程被结束 |
| GNOME Shell 46 + Wayland（无显示器模式） | 面板显示正常；未装扩展时提示并可一键安装；“重新登录”后扩展生效，读到其他窗口的标题、应用、进程；托盘图标和菜单（打开、退出）；关闭到托盘再打开；最小化；拖动窗口；Mutter 空闲时间 |
| 媒体 | 用一个假的 MPRIS 播放器验证能读到标题、歌手、来源进程 |
| AppImage | `dotnet publish` + `vpk pack` 打出 45 MB 的 AppImage，运行正常，更新检查能连到 GitHub，启动器条目和图标写入正确 |
| 示例插件 | `x11-xdotool.sh` 实际运行通过；`hyprland.sh`、`sway.sh` 只用伪造的命令输出跑通了 JSON 处理，**没有在真机上验证** |

运行中发现并修掉的问题：

1. 同步上下文装在 `async void OnStartup` 里，方法返回时被还原，之后的 `await` 回到线程池，面板在错误的线程上创建，程序崩溃。改为在 `Run()` 里安装。
2. SIGTERM 只停了后台服务，GTK 主循环不退出，进程留着。改为跟随宿主的 `ApplicationStopping` 退出。
3. GNOME 给没有 `.desktop` 的窗口一个 `window:3` 形式的临时编号，被当成了应用标识。扩展改为这种情况返回空。

## 没有验证的

- **朋友的两台机器**：Ubuntu 26.04（GNOME Shell 候选版本 50.1，扩展声明支持到 50，但 50 上没跑过）和 Arch（环境信息还没拿到）。
- CI 的 `linux` 任务没有跑过（补丁还没应用）。打包命令在本地按同样的参数跑通过。
- 真实浏览器（Edge、Snap 版 Firefox）的进程名和媒体来源是否与规则库一致。
- 锁屏信号（测试环境里的 logind 是模拟的）、开机自启、自动更新的下载和替换、睡眠唤醒后的重连。
- KDE、Hyprland、Sway 等其他桌面。
- Windows 客户端和 `tools/MacCheck` 在这次改动后的编译，见 progress.md。

## Windows 和 macOS 的插件

没有做，也没有改这两个平台的采集器。以后要开放时沿用同一套协议：`ExternalProvider` 和 `ProviderProtocol` 已经在共用层，需要做的是各平台的 `IProviderControl` 实现、设置页入口，以及决定插件和内置采集器谁优先。
