---
name: deskpal-write-provider
description: 给 DeskPal 写一个插件（provider），在 Windows、macOS 或 Linux 上提供当前窗口、浏览器网站、暂离、锁屏或正在播放的媒体中的一项或几项，替换或补充官方内置插件。用户说 DeskPal 识别不到应用或网站、显示“没有插件提供”、想支持某个桌面环境（Hyprland、Sway、KDE Wayland、niri 等）或某个软件、想自己决定某项信息从哪来、想让某个软件（例如 QQ 的当前会话）显示更具体的内容，或者要改插件协议、插件宿主本身时使用。
---

# 写一个 DeskPal 插件

DeskPal 的活动信息全部来自插件：每个平台有一个官方插件（`official:windows`、`official:macos`、`official:linux`，即原来的采集器，在程序内运行），用户可以在插件文件夹里放自己的插件（独立的可执行程序）。两种插件在「设置 → 插件」里排成一个列表。

面向用户的说明在 `docs/provider-guide.md`。协议的权威实现是 `src/DeskPal.Client.Core/Providers/ProviderProtocol.cs`；本文、指南、`scripts/check_provider.py` 必须和它一致。

## 列表怎么决定用谁

五项能力：`foreground`（当前窗口）、`domain`（浏览器网站）、`idle`（暂离）、`lock`（锁屏）、`media`（正在播放）。

列表有先后，像 mod 列表。**每一项能力分别取最上面一个开启、且没有出错的、声明了这一项的插件**（`ProviderRouting`）。所以用户插件只需要提供缺的那几项，放在官方插件上面，其余照旧由官方插件提供。插件出错时它的能力交给下一个，恢复后拿回来。

用户新放进文件夹的插件默认关闭，开启时界面会确认。

第六项 `detail`（补充标题）不按上面的规则抢主人：插件在 hello 里用 `apps` 声明管哪些程序，只在这些程序处于前台时把窗口标题换成更具体的内容（例如 QQ 的当前会话名），当前窗口本身仍来自 `foreground` 的主人。多个插件都能补充时取列表里最上面一个。实现在 `DetailRouter`，由 `ProviderHost` 在把前台窗口发到 `ActivityBus` 前合并。

## 先确认需要写什么

| 情况 | 要写什么 |
| --- | --- |
| Linux X11 会话 | 不用写，官方插件已支持 |
| Linux GNOME + Wayland | 不用写，在官方插件上点“安装 GNOME 扩展”，重新登录 |
| Linux 其他 Wayland 桌面 | 一个只提供 `foreground` 的插件，放在 Linux 内置上面 |
| Linux 上想按网站分类 | 通常不用写：装 DeskPal 浏览器扩展（Linux 内置插件上的“获取浏览器扩展”）。只有想换数据来源时才自己输出 `domain` |
| Windows、macOS | 官方插件都有；只有想改某一项的来源时才写 |
| 某个软件识别不准 | 优先让用户在 DeskPal 里改分类；只有需要读到额外信息时才写插件 |
| 想让某个软件显示更具体的内容（聊天软件的当前会话、编辑器的当前项目） | 一个只提供 `detail` 的插件，`apps` 写这个软件的进程名，放在列表任何位置都行。Windows 上先看窗口标题里有没有（官方插件已经上报标题），没有再用 UI Automation 读界面元素，参考仓库根目录的 `plugins/windows-qq.ps1`、`plugins/windows-wechat.ps1` |

不要凭猜测写：先让用户在终端里跑一遍你打算用的查询命令，把真实输出贴回来。Linux 上先让用户运行 `echo $XDG_SESSION_TYPE $XDG_CURRENT_DESKTOP`。

## 协议 v1

标准输出，UTF-8，一行一个 JSON 对象。

```json
{"v":1,"type":"hello","name":"my-plugin","version":"0.1","provides":["foreground","idle"]}
{"v":1,"type":"foreground","process":"firefox","name":"Firefox","title":"Inbox — Mozilla Firefox","fullscreen":false,"path":"/usr/lib/firefox/firefox"}
{"v":1,"type":"domain","domain":"github.com"}
{"v":1,"type":"idle","seconds":42}
{"v":1,"type":"lock","locked":true}
{"v":1,"type":"media","playing":true,"title":"Blue in Green","artist":"Miles Davis","source":"spotify"}
```

| type | 字段 | 说明 |
| --- | --- | --- |
| `hello` | `name`（必填）、`version`、`provides`、`apps` | 第一行。`provides` 不写时按 `["foreground","domain"]`。**没声明的能力，对应的消息会被丢弃**。`apps` 是 `detail` 适用的进程名列表（规范化方式同 `process`，不区分大小写，最多 32 个），声明了 `detail` 却没写 `apps` 时补充的标题不生效 |
| `foreground` | `process`（必填）、`name`、`title`、`fullscreen`、`path` | `process` 给完整路径或带 `.exe` 也行，DeskPal 只取文件名 |
| `domain` | `domain`（字符串或 null） | 只给域名。**每次浏览器窗口或标题变化后都要再发一次**，哪怕没变：管线会等它（`ActivitySnapshot.DomainPending`），所以宿主不对 domain 去重 |
| `idle` | `seconds`（≥0 的数字） | 距上次输入的秒数，**每 5 秒左右发一次**；阈值由 DeskPal 按用户设置判断（`IdleTracker`），超过 2 分钟没报就不算暂离 |
| `lock` | `locked`（布尔） | 变化时发 |
| `media` | `playing`（布尔，必填）、`title`、`artist`、`source` | `source` 是播放器程序名；停止时 `playing:false` |
| `detail` | `process`（必填）、`window`、`title` | 用 `title` 替换该程序窗口的标题；`title` 为 null 表示收回。`window` 写插件看到的窗口标题，只有当前窗口标题与它一致时才生效（不写则该程序所有窗口都用）。宿主记住每个插件最后一次报的值，切走再切回不用重报；变化时再报，宿主不去重。浏览器不适用 |

硬性规则：

- `v` 可省略；写了必须是 `1`。不认识的 `type` 和 `provides` 里不认识的名字会被忽略（留给以后的版本）。
- 一行最长 8192 字符；`title` 超过 512 字符截断；每秒超过 50 行的部分丢弃。
- 除 `idle`、`domain`、`detail` 外，**没变化就不要重复输出**。
- **每行写完立刻 flush**。标准输出只能有协议行，调试信息写标准错误（进 DeskPal 日志，退出时最后一行显示在列表里）。
- 插件要一直运行。退出后按 1、2、5、10、30、60 秒的间隔重启。DeskPal 关闭插件或退出时会结束插件及其子进程。
- 环境变量 `DESKPAL_PROTOCOL` 是协议版本；工作目录是插件所在目录；标准输入是关闭的（Windows 批处理里不能用 `timeout` 等待，用 `ping -n 6 127.0.0.1 >nul`）。

## 插件文件夹和文件类型

| 系统 | 文件夹 | 能当插件的文件 |
| --- | --- | --- |
| Windows | `%APPDATA%\DeskPal\plugins\` | `.exe`；`.cmd`/`.bat`（cmd）；`.ps1`（powershell -ExecutionPolicy Bypass）；`.py`（`python -u`，要求 python 在 PATH 上） |
| macOS | `~/Library/Application Support/DeskPal/plugins/` | 有可执行位的文件 |
| Linux | `~/.config/DeskPal/plugins/` | 有可执行位的文件 |

只看文件夹本身，不看子目录，跳过以 `.` 开头的文件（`PluginFiles`）。Windows 的 `.ps1` 必须存成带 BOM 的 UTF-8，否则 Windows PowerShell 5.1 按本地编码读，含中文时解析失败。

## `process` 怎么取（最容易错）

分类规则按程序文件名匹配，三个平台共用一套：`code`、`firefox`、`chrome`、`msedge`、`steam`、`obs`…… 所以：

1. 首选窗口所属进程的可执行文件名（Linux `basename "$(readlink /proc/$PID/exe)"`；Windows 进程名不带 `.exe`）。
2. 可执行文件只是运行时（`electron`、`python3`、`java`、`wine`、`bwrap`）时，改用窗口的应用标识（Wayland `app_id` / X11 `WM_CLASS`）。
3. 拿不到进程号时，用应用标识。

## Linux 的浏览器网站

Linux 内置插件里有一个本机 HTTP 端点（`BrowserBridge`，`127.0.0.1:24913`），由仓库里的浏览器扩展（`src/DeskPal.Client.Linux/browser-extension/`，MV3，Chromium 系和 Firefox 121+）上报当前标签页的域名和是否有焦点。它按**列表里正在使用的** `foreground`（不管来自哪个插件）判断前台是不是浏览器、是哪种浏览器，再选对应扩展的域名。所以只提供 `foreground` 的用户插件放在 Linux 内置上面时，网站照样有。

端点只接受浏览器扩展的请求：带 http(s) 或 `null` Origin 的请求一律 403，必须带 `X-DeskPal: 1` 和 JSON 内容类型（网页跨域发不了，预检只放行扩展 Origin），请求体不超过 4 KB，只回空响应。扩展只发域名、不发网址，不在无痕窗口运行。改动扩展和 `BrowserBridge.cs` 要一起改。

## 隐私边界

插件只陈述事实。分类、不记录标题的规则、隐私过滤都在 DeskPal 里、插件输出之后执行，插件绕不过去：`detail` 补充的标题和普通窗口标题一样，只在房间成员把该类别设为“显示详情”时离开本机。

用户插件是用户自己运行的程序，读什么由用户自己决定，DeskPal 的硬性约束管的是 DeskPal 本身。替用户写插件时仍按这几条来：**不截屏、不读按键内容、不注入其他进程、不联网**；网站只取域名；用 UI Automation 读界面时只读需要的那一个元素（例如会话名），不读消息正文。

## 步骤

1. 选模板：`scripts/template.sh`（Linux、macOS，需要 jq）、`scripts/template.py`（三平台，标准库）、`scripts/template.ps1`（Windows 自带 PowerShell，读前台窗口和暂离，已在 Windows 上验证）。现成例子在 `examples/`：`x11-xdotool.sh` 已验证；`hyprland.sh`、`sway.sh`、`macos-frontmost.sh` 按文档写成、未在真机验证，先让用户验证。可以直接用的插件在仓库根目录 `plugins/`（不在 `examples/`）：`windows-steam.ps1`（`detail`，通过 Steam 客户端的 Chrome 调试接口 `127.0.0.1:8080` 读自己的 Rich Presence，按 `appmanifest` 的 `installdir` 认出游戏进程，游戏变化时重发 hello 更新 `apps`，已在杀戮尖塔 2 上验证）、`windows-qq.ps1`（QQ NT 9.9.37 验证，按 class 名找元素）、`windows-wechat.ps1`（微信 4.2.0.53 验证，微信 4.x 是 Qt 界面，按 AutomationId 找元素并缓存，因为界面里有整个消息列表，每秒全树搜索太费）。新写这类插件时，先在用户电脑上遍历一次 UI Automation 树找到目标元素（只输出类型、类名、AutomationId 和名称长度，不输出名称，避免读到聊天内容），优先用 AutomationId，其次 class 名。Linux 的 QQ 插件见 `plugins/linux-qq.py`：通过 AT-SPI 按当前 ACTIVE 窗口查找 `chat-header__contact-name`，名称为空时读文本接口，`window` 报 null；DeskPal 在 Linux 记录的进程名是可执行文件名 `qq`。
2. 在 hello 的 `provides` 里只写真正提供的几项，只改读取数据的那一段。Shell 里用 `jq` 生成 JSON，PowerShell 用 `ConvertTo-Json`，不要手拼字符串。
3. 检查：`python3 scripts/check_provider.py <插件文件>`（Windows 也能用）。它运行插件 8 秒，按 DeskPal 的规则逐行检查，期间让用户切换几个窗口。
4. 安装：放进插件文件夹（Linux、macOS 上 `chmod +x`），在「设置 → 插件」点刷新按钮，打开开关，用箭头移到官方插件上面（只提供 `detail` 的不用移）。点开插件后高亮的标签是实际在用的能力。

### 用 UI Automation 找界面元素（Windows）

Electron、Chromium、WPF、WinForms 程序一般都向 UI Automation 公开界面结构，Electron 的元素 `ClassName` 就是网页里的 CSS class。先在用户机器上列一遍目标窗口的元素（`AutomationElement.FromHandle` 加 `TreeWalker.ControlViewWalker` 遍历，打印控件类型、`Name`、`ClassName`、`AutomationId` 和位置，名称截短，只看结构，不要把聊天内容贴进对话），找到要的那个元素后，用 `PropertyCondition(ClassNameProperty 或 AutomationIdProperty)` 加 `FindFirst(Descendants)` 直接取。QQ NT 的会话名是 `chat-header__contact-name`，一次查找约 15 ms。Chromium 第一次被查询时才打开无障碍树，第一次可能取不到，下一轮再取即可。

## 常见桌面的查询方式

| 桌面 | 命令 | 备注 |
| --- | --- | --- |
| Hyprland | `hyprctl activewindow -j` | 有 `class`、`title`、`pid`、`fullscreen`；事件在 `$XDG_RUNTIME_DIR/hypr/$HYPRLAND_INSTANCE_SIGNATURE/.socket2.sock` |
| Sway | `swaymsg -t get_tree`，取 `focused == true` 的节点 | 有 `app_id`（XWayland 窗口是 `window_properties.class`）、`name`、`pid`、`fullscreen_mode`；事件用 `swaymsg -m -t subscribe '["window"]'` |
| niri | `niri msg --json focused-window` | 有 `app_id`、`title`、`pid` |
| KDE Plasma (Wayland) | KWin 脚本或 `kdotool getactivewindow` | 没有稳定的命令行接口，需要用户确认可用的工具 |
| X11 | `xdotool getactivewindow`、`xprop` | 官方插件已支持 |
| Windows | user32 `GetForegroundWindow`、`GetWindowText`、`GetLastInputInfo` | 见 `template.ps1` |
| macOS | `osascript -l JavaScript` 读 System Events | 读窗口标题需要给 DeskPal 辅助功能权限 |

以上除 X11 和 Windows 外都没有在本项目里实测过，以用户机器上的真实输出为准。

## 改插件系统本身时

| 改什么 | 一起改 |
| --- | --- |
| 协议（字段、消息类型、上限） | `ProviderProtocol.cs`、`tests/DeskPal.Tests/Providers/ProviderProtocolTests.cs`、`scripts/check_provider.py`、本文、`docs/provider-guide.md`。只加可选字段；不兼容的改动才提高 `v`，并让 DeskPal 同时接受旧版本 |
| 能力（新增一项） | `ProviderCapability` 及 `ProviderCapabilities`（名字、`Of`、`Cleared`）、协议、前端 `PluginCapability` 和 `plugins.cap.*` 文案 |
| 路由和交接 | `ProviderRouting`、`CapabilityRouter`（换主人时补发新主人的最后一个值，没人提供时发清空值）和它们的测试；`detail` 走 `DetailRouter` 和 `DetailRouterTests` |
| 宿主（列表、启停、设置） | `Client.App/Providers/ProviderHost.cs`；设置项 `AppSettings.Plugins`（整体替换，不原地修改） |
| 官方插件 | 各平台用 `AddOfficialProvider` 注册采集器，采集器构造函数收 `SignalSink`（不再直接收 `ActivityBus`）；Linux 是 `LinuxOfficialProvider`（带状态说明和按钮） |
| 设置页 | `PluginsView` / `PluginView` / `PluginNote`（`PanelState.cs`）、`SettingsPage.tsx` 的 `PluginsEntry`（设置页入口行）和 `PluginsPage`（插件页）、`bridge.ts` 的类型和 `plugins.*` 命令、`plugins.*` 文案；改完用 `DESKPAL_UPDATE_FIXTURES=1` 重新生成模拟数据，`?mock=no-room&platform=linux` 是 Linux 的插件列表 |
