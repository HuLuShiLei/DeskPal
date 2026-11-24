# 全平台插件化：改动记录（2026-11-23，第二轮）

第一轮（Linux 客户端和 Linux 专用的识别插件）见 [changes-2026-11-23-linux.md](changes-2026-11-23-linux.md)。这一轮按用户的要求把插件推广到三个平台。写插件见 [provider-guide.md](provider-guide.md)。

## 用户的要求

- 全平台统一成插件方式。Windows 和 macOS 的采集器也拿出来做成插件，只是默认就提供，像 RimWorld 的本体和官方 DLC 也显示在 mod 列表里。
- 网站这块：用户自己写的插件只要输出 `domain` 字段，DeskPal 不关心数据从哪来、浏览器扩展和插件之间怎么通信。
- Linux 要默认带一个既能读窗口标题、又能读网站的插件，并配一个为 Linux 准备的浏览器扩展，放进仓库。

开工前问过用户、按回答执行的四点：

| 问题 | 用户的选择 |
|---|---|
| 官方插件怎么运行 | 在 DeskPal 程序里运行（实现同一个接口、显示在同一个列表），不拆成独立程序 |
| Linux 官方插件 | 写进程序（C#），另外保留脚本模板给用户参考 |
| 浏览器扩展怎么和插件通信 | 本机 HTTP（127.0.0.1），不用浏览器的本机消息 |
| 插件管哪些信息 | 全部：当前窗口、网站、暂离、锁屏、媒体 |

没有问、由我决定的：

| 事项 | 做法 | 理由 |
|---|---|---|
| 多个插件怎么共存 | 列表有先后，**每一项能力分别**取最上面一个开启的、声明了它的插件 | 用户插件只写缺的那一项（例如 Hyprland 只写窗口），其余照旧用官方插件；比“同一时间只用一个插件”实用 |
| 插件出错时 | 它的能力交给下一个插件，恢复后拿回来 | 用户插件崩溃不会让状态中断 |
| 新放进文件夹的插件 | 加在最上面，默认关闭，开启时确认 | 放进文件夹不等于同意运行 |
| 能力怎么声明 | hello 消息里的 `provides`；不写时按“当前窗口 + 网站” | 第一轮的插件不用改也能用 |
| 暂离怎么报 | 插件报“距上次输入的秒数”，阈值由 DeskPal 按用户设置判断 | 阈值是用户的设置，插件不该知道 |
| 浏览器扩展的端口 | 固定 127.0.0.1:24913 | 扩展要事先知道地址；选在 Linux 临时端口范围（32768 以上）之外 |

## 改了什么

### 共用层

| 文件 | 改动 |
|---|---|
| `Client.Core/Providers/ProviderCapability.cs`（新） | 五项能力，名字、信号 → 能力、没人提供时的清空值 |
| `Client.Core/Providers/ProviderRouting.cs`、`CapabilityRouter.cs`（新） | 路由规则；主人变化时补发新主人的最后一个值（采集器只在变化时报） |
| `Client.Core/Providers/IdleTracker.cs`（新） | 把插件报的秒数按用户设置换算成暂离，两次报告之间继续计时，超过 2 分钟不报就不算暂离 |
| `Client.Core/Providers/ProviderProtocol.cs` | hello 加 `provides`；新消息 `idle`、`lock`、`media`；`NormalizeDomain` 公开 |
| `Client.Core/Pipeline/ActivityBus.cs` | 加 `Unsubscribe`（浏览器桥随插件开关订阅和退订） |
| `Client.Core/Ui/PanelState.cs` | `SettingsView.Provider`（只有 Linux）换成 `SettingsView.Plugins`（全平台）：`PluginsView`、`PluginView`、`PluginNote` |
| `Client.App/Providers/IActivityProvider.cs`（新） | 插件接口和 `SignalSink` |
| `Client.App/Providers/OfficialProvider.cs`（新） | 把采集器作为一组运行的官方插件；`AddPluginHost`、`AddOfficialProvider` |
| `Client.App/Providers/ExternalProvider.cs` | 实现插件接口；支持新消息；只采用声明过的能力；**domain 不再去重**（管线每次浏览器窗口变化后都等一次 domain，见下文“顺带修掉的问题”） |
| `Client.App/Providers/PluginFiles.cs`（新） | 哪些文件是插件、怎么启动（Windows 的 `.cmd`/`.bat`/`.ps1`/`.py` 交给对应程序） |
| `Client.App/Providers/ProviderHost.cs`（新） | 列表、启停、路由、设置持久化，实现 `IProviderControl` |
| `Client.App/Providers/IProviderControl.cs` | 改成插件列表的操作：开关、移动、插件按钮、刷新、文件夹 |
| `Client.App/Services/ClientStartup.cs` | 三个平台都注册插件宿主（在 `PresenceCoordinator` 之后，保证它先订阅） |
| `Client.App/Services/AppSettings.cs` | `Provider`（单个文件名）换成 `Plugins`（`{ id, enabled, provides }` 列表，整体替换） |
| `Client.App/Ui/PanelBridge.cs` | 命令 `provider.*` 换成 `plugins.setEnabled`、`plugins.move`、`plugins.action`、`plugins.refresh`、`plugins.openFolder` |
| `Client.Core/Localization/Strings*.resx` | `Provider_*` 换成 `Plugin_*` |

### 三个平台

| 平台 | 改动 |
|---|---|
| Windows | 五个采集器构造函数的 `ActivityBus` 换成 `SignalSink`；`App.xaml.cs` 用 `AddOfficialProvider("official:windows", ...)` 注册，不再 `AddHostedService` |
| macOS | 同上，`AppDelegate.cs` 注册 `official:macos` |
| Linux | `ForegroundService` 删除，拆成 `LinuxWindowSource`（X11 或 GNOME 扩展）、`BrowserBridge`（新）、`LinuxStatus`、`LinuxOfficialProvider`；空闲、锁屏、媒体采集器换成 `SignalSink`；其他 Wayland 桌面上官方插件不声明“当前窗口”，这样排在它下面的用户插件也能提供 |

采集器的代码本身没有改，只是换了发布的地方和注册方式。

### Linux 浏览器扩展（新）

`src/DeskPal.Client.Linux/browser-extension/`：MV3，同一份文件在 Chromium 系（Chrome、Edge、Brave、Vivaldi、Opera）和 Firefox 121+ 上都能用。

- 只读当前窗口里活动标签页的域名，以及这个浏览器有没有焦点；只发给 `127.0.0.1:24913`；不在无痕窗口运行。权限：`tabs`、`alarms`。
- 事件触发（切标签、地址变化、窗口焦点变化）外加每 30 秒一次心跳。连不上时工具栏图标上显示“!”。
- `BrowserBridge` 那边：网页来源的请求一律拒绝，只回空响应，按前台浏览器的种类挑对应扩展的域名。细节见 `docs/architecture.md` 的「Linux 客户端 → 网站」。
- 插件上的“获取浏览器扩展”按钮把扩展复制到 `~/.local/share/DeskPal/browser-extension/` 并打开文件夹（AppImage 的路径每次启动都变）。

### 设置页

「设置 → 插件」一节，三个平台都有：每个插件一行，显示名称、版本、内置或自己添加、提供的能力（高亮的是正在使用的）、状态说明、插件自己的按钮；左边上下箭头调整顺序，右边开关；顶部提示没人提供的能力；底部是插件文件夹（刷新、打开）和编写指南。原来 Linux 专有的「应用识别」一节删除。

### 插件教程和工具

| 文件 | 改动 |
|---|---|
| `docs/provider-guide.md` | 重写：三个平台、列表怎么起作用、五种消息、Linux 浏览器扩展的安装 |
| `.cursor/skills/deskpal-write-provider/SKILL.md` | 重写，同上，另加“改插件系统本身时要一起改哪些文件” |
| `scripts/check_provider.py` | 支持新消息和 `provides`；Windows 上按 DeskPal 的方式启动 `.cmd`/`.ps1`/`.py` |
| `scripts/template.ps1`（新） | Windows 模板（系统自带 PowerShell），提供当前窗口和暂离 |
| `scripts/template.py`、`template.sh`、`examples/*.sh` | hello 加 `provides`，路径改成 `plugins` 文件夹 |
| `examples/macos-frontmost.sh`（新） | macOS 例子（osascript），未在真机验证 |
| `.cursor/skills/deskpal-add-signal/SKILL.md`、`.cursor/rules/client-collectors.mdc` | 新增信号要登记能力；采集器收 `SignalSink`，加进官方插件，不再单独 `AddHostedService` |

### 其他

- 模拟数据（`web/src/mock/fixtures`）的设置里多了一个插件列表样例（两个用户插件加 Windows 内置）。
- `AGENTS.md`、`architecture.md`（新增「插件」一节，Linux 一节改写）、`known-issues.md`（Linux 和新增的「插件」）、`CHANGELOG.md`、`progress.md` 已更新。
- `.github/workflows/ci.yml`：已应用发布说明补丁，Linux Release 的安装说明改用「设置 → 插件 → Linux 内置」，并补充浏览器扩展安装入口。

## 顺带修掉的问题

- **Linux 上浏览器的使用时长一直处在“等网站”状态**：每次浏览器窗口变化后，管线都会等一个网站信号（`ActivitySnapshot.DomainPending`），统计据此记录。第一轮的 Linux 代码只在启动时发过一次，用户插件的 domain 又被去重，所以浏览器窗口变化后一直在等。现在浏览器桥每次都回答，用户插件的 domain 不再去重。
- **X11 上窗口管理器自己的窗口被当成名为 `unknown` 的应用**，出现在「待归类」里。现在没有进程号也没有窗口类的窗口不报。

## 验证

| 项目 | 环境 | 结果 |
|---|---|---|
| 单元测试 | 这里和你的电脑 | 429 个全部通过（新增路由、交接、暂离换算、新消息的测试） |
| Windows 客户端编译 | 你的电脑 | 0 警告 0 错误（在 `.local\tmp` 的副本里编译，没有动工作区） |
| MacCheck | 你的电脑 | 0 警告 0 错误 |
| 前端 | 这里 | 类型检查、构建通过 |
| Linux 插件列表 | Ubuntu 24.04，X11 和 GNOME Shell 46 Wayland | 官方插件启动全部部件；开启只提供窗口的用户插件后只接管窗口，其余四项仍是官方插件；下移后窗口交回官方插件；顺序和开关写进设置 |
| 用户插件的新消息 | 同上 | 只提供暂离和媒体的插件放在上面时，暂离来自它（报 400 秒后显示暂离），窗口、网站、锁屏仍来自官方插件 |
| 浏览器扩展 | Chrome for Testing 158、Firefox 160，同时开着 | 两个都连上；前台是哪个浏览器就用哪个的网站；切标签后网站跟着变（github.com → bilibili.com，Firefox 是 zhihu.com）；Firefox 没有额外授权也能连 |
| 本机端口的防护 | 同上 | 网页 Origin 和 `null` Origin 403，缺自定义头 400，超长 413，扩展的预检放行 |
| Windows 模板 | 你的电脑，Windows PowerShell 5.1 | 用检查工具跑通，读到前台窗口和暂离秒数 |
| Windows 批处理例子 | 你的电脑 | 发现 `timeout` 在没有键盘输入时立即返回、每秒刷出几百行（检查工具报了出来），改用 `ping` 等待后通过 |

## 没有验证的

- **Windows 和 macOS 客户端没有实际运行过新代码**：Windows 只编译过，macOS 只做了类型检查。插件宿主是共用代码，在 Linux 上实际跑过；Windows 官方插件只是把原来的采集器换了注册方式，但启动、开关、和用户插件共存都还没在 Windows 上点过一遍。**合并前请在 Windows 上打开一次，看「设置 → 插件」和首页状态是否正常。**
- Edge（你朋友的默认浏览器）、Snap 版 Firefox、Brave 等没有装过扩展。
- 正式版 Firefox 只能临时载入扩展，长期使用需要 Mozilla 签名，没有做。
- Hyprland、Sway、macOS 的例子没有在真机上跑过。
- 朋友的 Ubuntu 26.04 和 Arch 仍然没有跑过。
