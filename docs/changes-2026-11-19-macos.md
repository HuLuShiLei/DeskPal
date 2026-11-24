# 2026-11-19 macOS 客户端改动记录

这次改动新增 macOS 客户端，同时把 Windows 客户端里和平台无关的部分抽成两端共用的项目。本文按文件列出全部改动、原因和验证情况。设计说明见 [architecture.md](architecture.md) 的「macOS 客户端」，限制见 [known-issues.md](known-issues.md) 的「macOS」，决定和偏离方案见 [progress.md](progress.md)。

改动只在本地工作区，没有提交，也没有推送。

## 一、前提和决定

| 事项 | 决定 |
|---|---|
| 是否签名 | 先出未签名的测试版（ad-hoc 签名），CI 预留签名和公证开关 `MAC_SIGN` |
| 芯片 | Apple 芯片（osx-arm64）和 Intel（osx-x64）各一个安装包 |
| 代码结构 | 抽共享层 `DeskPal.Client.App`，不复制一份 Windows 代码 |
| 提交方式 | 只改本地文件，由用户自己检查后提交、推送 |
| 界面技术 | `net10.0-macos`（C# 直接调用 AppKit）+ WKWebView，复用 `src/DeskPal.Client/web` |
| 权限 | 只申请「辅助功能」（读焦点窗口标题、浏览器地址），不申请屏幕录制和输入监控 |
| Server | 不改。协议、接口、数据库都不变，Mac 和 Windows 用户在同一个房间 |

## 二、功能可行性

| 功能 | macOS 做法 | 结论 |
|---|---|---|
| 前台应用 | `NSWorkspace.FrontmostApplication`，不需要权限 | 可以 |
| 窗口标题、全屏 | 辅助功能 `AXTitle`、`AXFullScreen`，或窗口正好盖住屏幕 | 可以，需要用户授权 |
| 浏览器网址 | 辅助功能：网页区域的 `AXURL`，或工具栏第一个文本框 | 可以，Firefox 不一定读得到 |
| 空闲 | `CGEventSourceSecondsSinceLastEventType`，不需要权限 | 可以 |
| 锁屏 | 分布式通知 `com.apple.screenIsLocked` / `screenIsUnlocked` | 可以 |
| 媒体播放 | 私有框架 MediaRemote | 降级：macOS 15.4 起读不到 |
| 内置规则 | Bundle ID 作为 `Process` 规则的内容 | 可以，规则库格式和 Server 不变 |
| 菜单栏图标、面板、置顶卡片、通知 | NSStatusItem、NSWindow + WKWebView、浮动 NSPanel、通知中心 | 可以 |
| 本机密钥 | 权限 600 的密钥文件 + AES-GCM | 可以（没用钥匙串，原因见第五节） |
| 开机自启 | `~/Library/LaunchAgents` | 可以 |
| 安装和自动更新 | Velopack 的 `.pkg`，按芯片分通道 | 可以，未签名时每次更新要重新授权辅助功能 |

## 三、文件改动

### 3.1 新项目 `src/DeskPal.Client.App`（两端共用）

| 文件 | 改动 |
|---|---|
| `DeskPal.Client.App.csproj` | 新建。net10.0，引用 Client.Core、Shared，包 SignalR.Client、Hosting、Velopack |
| `GlobalUsings.cs` | 新建。`System.IO`、`DeskPal.Client.Core.Localization` |
| `Platform/IClientPlatform.cs` | 新建。平台能力接口：平台名、开机自启、复制文字、保存/打开 JSON 文件对话框、选择文件夹、打开链接或文件夹、后备提示框 |
| `Platform/SecretProtection.cs` | 新建。`ISecretProtector`（`Protect`/`Unprotect`）和启动时设置的 `SecretProtection.Current` |
| `Services/ClientStartup.cs` | 新建。原来在 Windows `App.xaml.cs`、`LoginWindow.cs` 里的启动流程：`AddDeskPalClient`（注册服务）、`RestoreSessionAsync`（恢复登录）、`SubmitLoginAsync`（登录页提交）、`Wire`（Hub 消息分发、加载规则）、`RefreshBuiltinRulesAsync`、`RefreshRoomsAsync`、`SignOutServicesAsync`、读取 `CHANGELOG.md`（同时查找 `.app` 的 `Contents/Resources`）；另有 `Ref<T>` 用于异步方法返回提示文字 |
| `Ui/WebCommand.cs` | 新建。从 Windows 的 `WebUi.cs` 移出 `WebCommand`、`CommandResult`，内容不变 |
| `Services/AppPaths.cs` | 从 Windows 项目移入并修改：macOS 上日志在 `~/Library/Logs/DeskPal`，不写进 `.app`；Windows 不变 |
| `Services/AppSettings.cs` | 移入并修改：token 加解密改用 `SecretProtection`，同时捕获 `FormatException`；`SettingsStore.Directory` 在 macOS 上是 `~/Library/Application Support/DeskPal`，Windows 不变 |
| `Services/FolderSyncService.cs` | 移入并修改：DPAPI 改用 `SecretProtection`，`DpapiFolderStore` 改名 `ProtectedFolderStore`。文件格式不变 |
| `Services/StatsSyncService.cs` | 移入并修改：同上，`DpapiKeyStore` 改名 `ProtectedKeyStore` |
| `Services/UpdateService.cs` | 移入并修改：更新通道改为 `UpdatePolicy.Channel(beta, MacArchitecture)`；新增 `MacArchitecture`（macOS 上是 arm64 或 x64，Windows 上为空） |
| `Ui/PanelBridge.cs` | 移入并修改：构造函数多一个 `IClientPlatform`。剪贴板、保存和打开文件、选择文件夹、开机自启、打开日志目录改走 `IClientPlatform`；`Prompt` 默认值取 `platform.FallbackPrompt`。命令和返回值不变 |
| `Ui/UpdateInteraction.cs` | 从 `src/DeskPal.Client/Views/` 移入，命名空间 `DeskPal.Client.Views` 改为 `DeskPal.Client.Ui`；`MessageBoxPrompt` 移到 Windows 项目 |
| `Services/` 下其余 15 个文件 | 原样移入，内容不变：`ApiClient`、`BuiltinRuleCache`、`ClientFileLoggerProvider`、`GithubApiDownloader`、`HubService`、`IdleSync`、`PersonalDataService`、`PersonalRules`、`PreferencesSync`、`PresenceCoordinator`、`RoomState`、`StatsExchange`、`UnclassifiedUsageStore`、`UsageReporter`、`UsageService` |

### 3.2 Windows 客户端 `src/DeskPal.Client`

| 文件 | 改动 |
|---|---|
| `DeskPal.Client.csproj` | 引用 `DeskPal.Client.App` |
| `Program.cs` | 启动时设置 `SecretProtection.Current = new DpapiProtector()` |
| `Platform/WindowsPlatform.cs` | 新建。`WindowsPlatform`（原来写在 `PanelBridge` 里的 WPF 对话框、剪贴板、开机自启）、`DpapiProtector`、`MessageBoxPrompt`（从 `Views/UpdateInteraction.cs` 移来） |
| `App.xaml.cs` | 注册服务、恢复登录、Hub 消息分发、刷新规则和房间、退出登录改用 `ClientStartup`；只保留 Windows 自己的采集器、托盘、面板和卡片。行为不变 |
| `Ui/LoginWindow.cs` | 登录和注册改调 `ClientStartup.SubmitLoginAsync`，删除原来的 `SubmitAsync` |
| `Ui/PanelWindow.cs` | `using DeskPal.Client.Views` 改为 `using DeskPal.Client.Platform` |
| `Ui/WebUi.cs` | 删除移到共享项目的 `WebCommand`、`CommandResult` |
| `Services/AppPaths.cs` | 删除（移到共享项目） |
| `Views/UpdateInteraction.cs` | 删除（移到共享项目） |

服务注册顺序有一处变化：共享服务（统计、同步、个人数据）在采集器之前注册，所以退出时采集器先停。同步服务退出前的最后一次同步不依赖采集器，没有影响。

### 3.3 新项目 `src/DeskPal.Client.Mac`

| 文件 | 内容 |
|---|---|
| `DeskPal.Client.Mac.csproj` | net10.0-macos，macOS 13+，`osx-arm64;osx-x64`，`ApplicationId` 为 `com.hulushilei.deskpal`，不签名，`LinkMode=SdkOnly`。构建时执行 `web/` 的 `npm ci`、`npm run build`，结果放进 `Contents/Resources/web`；`CHANGELOG.md` 和图标作为资源 |
| `Info.plist` | `LSUIElement`（只在菜单栏，没有程序坞图标）、图标、最低系统版本、中英文本地化 |
| `Entitlements.plist` | 只在签名时使用：允许 JIT、未签名可执行内存、关闭库校验，不进沙盒 |
| `GlobalUsings.cs` | 同共享项目 |
| `Main.cs` | 先处理 Velopack 参数，设置 `FileKeyProtector`，以菜单栏程序模式启动 |
| `AppDelegate.cs` | 对应 Windows 的 `App`：单实例（`instance.lock` 排他锁）、恢复登录或显示登录页、检查辅助功能授权、启动后台服务、菜单栏图标（左键开关面板，右键或 Control 点击显示「打开」「退出」）、隐藏的主菜单（让 Cmd+C/V/A/W/M/Q 生效）、退出、退出登录后重启到登录页、安装更新。Cmd+Q 和系统退出走同样的退出流程；再次打开应用时显示面板 |
| `Native/MacNative.cs` | 辅助功能（AXUIElement）、CoreFoundation 和空闲时间的 P/Invoke |
| `Collectors/ForegroundCollector.cs` | 每秒读前台应用：进程名用 Bundle ID，没有时用可执行文件名；显示名称是本地化应用名；标题和全屏来自辅助功能。也包含 `ForegroundState` |
| `Collectors/BrowserDomainCollector.cs` | 前台是浏览器时，广度优先查找 `AXWebArea` 的 `AXURL`，找不到时取工具栏第一个文本框的值；只发 host，时机和 3 秒超时同 Windows |
| `Collectors/SystemCollectors.cs` | `IdleCollector`（不需要权限）、`SessionCollector`（锁屏和解锁通知） |
| `Collectors/MediaCollector.cs` | 通过 MediaRemote 读正在播放的状态、标题、艺术家和来源应用；框架不可用或连续失败 10 次后停止读取，不会让程序崩溃 |
| `Platform/MacPlatform.cs` | `MacPlatform`（剪贴板、NSSavePanel/NSOpenPanel、打开链接、通知中心、重启应用）、`AlertPrompt`、`LaunchAgent`（开机自启）、`FileKeyProtector`（本机密钥） |
| `Platform/MainThread.cs` | 在主线程执行 AppKit 调用；`Activate()` 在 macOS 14 起用新接口，之前用旧接口 |
| `Ui/WebHost.cs` | WKWebView 封装：`deskpal://app/` 协议从包内读页面，不开端口；页面和宿主收发消息；其他来源的链接用默认浏览器打开；网页进程退出时通知重建 |
| `Ui/WindowChrome.cs` | 透明标题栏、隐藏红黄绿按钮的窗口，按主题设置外观，处理页面的 `window.drag`、`window.minimize`、`window.close`；拖动时窗口跟随鼠标直到松开 |
| `Ui/PanelController.cs` | 对应 Windows 的 `PanelWindow`：首次显示在屏幕右下角、置顶、主题；200 毫秒合并推送、30 秒刷新、隐藏 5 分钟释放网页、15 秒没有 `ready` 或网页进程退出时重建，连续 3 次失败给出提示；更新确认在页面里显示；Cmd+W 隐藏到菜单栏 |
| `Ui/LoginController.cs` | 登录和注册窗口，提交后关闭；直接关闭窗口则退出程序 |
| `Ui/MemberCards.cs` | 置顶小卡片：不抢焦点的浮动 NSPanel，在所有桌面空间显示，从屏幕右上角往下排；整张可拖动，双击打开面板，× 取消置顶 |
| `Resources/DeskPal.icns` | 应用图标 |
| `Assets/make-icns.py` | 从 `src/DeskPal.Client/Assets/icon-source.png` 生成图标：和 `make-icon.ps1` 一样去掉白底，按 macOS 规范留边距 |

### 3.4 `src/DeskPal.Client.Core`

| 文件 | 改动 |
|---|---|
| `Classification/Browsers.cs` | 加入 Mac 浏览器的 Bundle ID：Safari、Chrome（含 Beta/Dev/Canary）、Chromium、Edge、Firefox、Brave、Opera、Vivaldi、Arc、360、QQ 浏览器 |
| `Classification/LayeredClassifier.cs` | 推测层把路径里的 `/` 当作 `\` 匹配游戏目录（Mac 的 Steam 在 `~/Library/Application Support/Steam/steamapps/`）；判断启动器、崩溃上报等工具时只看文件名。游戏目录列表本身不变 |
| `Updates/UpdatePolicy.cs` | 新增 `Channel(receiveBeta, macArchitecture)`：Mac 通道是 `osx-arm64`、`osx-x64`，测试版加 `-beta`；Windows 仍是 `win`、`beta` |
| `Localization/Strings.resx`、`Strings.en.resx` | 新增 `Common_Ok`、`Common_Cancel`、`App_AlreadyRunningMac`（中英文） |

### 3.5 前端 `src/DeskPal.Client/web/src`

| 文件 | 改动 |
|---|---|
| `bridge.ts` | 支持 WKWebView：发消息用 `window.webkit.messageHandlers.deskpal.postMessage(JSON 字符串)`，宿主调用 `window.__deskpal(消息)`；新增消息 `window.drag`、`window.dblclick`。WebView2 和模拟宿主的处理不变 |
| `platform.ts` | 新建。读取地址里的 `platform` 参数，写到 `<html data-platform>`；macOS 上在标题栏和置顶卡片空白处按下鼠标时发 `window.drag`，双击时发 `window.dblclick`（按钮、菜单、输入框除外） |
| `main.tsx` | 启动时设置平台、注册拖动处理 |
| `ui.tsx` | macOS 上 `Icon` 改画 SVG 线条图标（和原来 36 个字形一一对应，跟随文字颜色和字号）；Windows 仍用 Segoe Fluent Icons |
| `host.tsx` | 提示条的图标改用 `Icon`，原来直接写字形 |
| `i18n/index.ts` | macOS 上替换 5 条文案：开机自启的说明、「最小化到系统托盘」改为「隐藏到菜单栏」、外观和语言说明里的「Windows」改为「macOS」；英文的「Start with Windows」改为「Open at login」 |
| `styles.css` | 字体列表在 Windows 字体之后加上 `-apple-system`、`PingFang SC`、`SF Mono`、`Menlo`（Windows 上显示不变）；新增 SVG 图标尺寸和 macOS 拖动区域的样式 |

### 3.6 Server

| 文件 | 改动 |
|---|---|
| `Features/Rules/builtin-rules.json` | 版本 7 → 8，新增 68 条 `Process` 规则，内容是 Mac 应用的小写 Bundle ID（现在共 411 条）。中文名称都带 `names.en`，格式和换行与原文件一致 |

新增的规则（按类别）：

- `Work`：VS Code、Cursor、Windsurf、Xcode、JetBrains 全家（IntelliJ IDEA 两个版本、PyCharm 两个版本、WebStorm、GoLand、CLion、DataGrip、Rider、PhpStorm、RubyMine）、Android Studio、Sublime Text、Zed、终端、iTerm2、Warp、WezTerm、Alacritty、Excel、Word、PowerPoint、Outlook、Teams（新旧两个）、Pages、Numbers、Keynote、WPS、企业微信、Slack、Zoom、Figma、Photoshop、Illustrator、Postman、Docker Desktop、Typora、Claude
- `Other`：OneNote、Obsidian、Notion、访达、备忘录、预览、邮件、ChatGPT、远程桌面、Parallels Desktop、VMware Fusion、TeamViewer、Steam、Epic Games
- `Communication`：微信、QQ、Telegram（两个版本）、WhatsApp、信息
- `Video`：IINA、VLC、mpv、QuickTime Player、Apple TV

拿不准 Bundle ID 的应用没有加（例如飞书、钉钉、Battle.net、哔哩哔哩），等每周的规则库候选 PR 或用户自己纠正分类。

### 3.7 测试 `tests/DeskPal.Tests`

| 文件 | 新增用例 |
|---|---|
| `Update/UpdatePolicyTests.cs` | 6 种组合：Windows 正式版和测试版、arm64 和 x64 的正式版和测试版通道 |
| `Classification/ClassifierTests.cs` | Mac 的 Steam、Epic 游戏路径算游戏；Mac 上游戏旁边的崩溃上报程序不算游戏；5 个 Mac 浏览器按 Bundle ID 识别；Bundle ID 规则不区分大小写 |
| `Classification/BuiltinRulesTests.cs` | 读取真实规则库：企业微信、Slack、Zoom 的 Bundle ID 是工作，微信、QQ 是聊天 |

### 3.8 CI `.github/workflows/ci.yml`

- 计算版本从 Windows 的 `client` 任务移到 `changes` 任务，作为输出给 Windows 和 Mac 共用，保证两边版本号相同。计算逻辑和 `CHANGELOG.md` 检查不变。
- 判断客户端是否有改动时，加上 `src/DeskPal.Client.App/` 和 `src/DeskPal.Client.Mac/`。
- 新增 `mac` 任务：在 macos-15 上，arm64 和 x64 各跑一次；编译 `.app`，用 vpk 打 `.pkg`。
  - macos workload 和 Xcode 固定为配套的版本，写在任务的 `env` 里：`XCODE_VERSION`（26.3）和 `MACOS_WORKLOAD`（`26.2.10233/10.0.100`，用 rollback 文件安装）。同时在任务里生成 `global.json`，固定使用 setup-dotnet 装的 SDK，不用 runner 上预装的更高版本。
  - 原因：最初写的是不带版本的 `dotnet workload install macos`，会装 NuGet 上最新的 workload，它要求的 Xcode 比 macos-15 runner 上的更新，编译会在第一步失败。
  - 以后升级：workload 版本见 NuGet 包 `Microsoft.NET.Sdk.macOS.Manifest-10.0.100`，确认 runner 上有它要求的 Xcode，再把两个值一起改。
  - 正式版和测试版发布时，等 Windows 任务建好 Release 后，用 `vpk upload --merge` 追加 Mac 的更新包和清单，上传 `DeskPal-<版本>-osx-<芯片>-Setup.pkg`。未签名时在 Release 说明末尾加一段 macOS 安装说明（只加一次）。
  - 其他分支手动运行或 PR 时只编译打包，不发布，安装包在本次运行的 Artifacts 里。
  - 仓库变量 `MAC_SIGN=true` 时导入证书、签名并公证，需要的 secret：`MAC_CERT_P12`、`MAC_CERT_PASSWORD`、`MAC_SIGN_APP_IDENTITY`、`MAC_SIGN_INSTALL_IDENTITY`、`MAC_NOTARY_APPLE_ID`、`MAC_NOTARY_PASSWORD`、`MAC_NOTARY_TEAM_ID`。

### 3.9 其他

| 文件 | 改动 |
|---|---|
| `DeskPal.slnx` | 加入 `DeskPal.Client.App`。不加 Mac 项目，否则 Windows 上 `dotnet build DeskPal.slnx` 会失败 |
| `tools/MacCheck/MacCheck.csproj` | 新建。没有 Mac 时检查 Mac 客户端能否编译：`dotnet build tools/MacCheck -p:MacRef=<Microsoft.macOS.dll>`（取自 NuGet 包 `Microsoft.macOS.Ref.net10.0_26.0`） |
| `CHANGELOG.md` | 0.1.4 一节最上面加一条 macOS 版（中英文） |
| `AGENTS.md` | 项目说明改为 Windows 和 macOS；项目结构加 `Client.App`、`Client.Mac`、`tools/MacCheck`；常用命令加 Mac 编译和类型检查；发布说明加 Mac 任务；硬约束 1 加上 macOS 只申请辅助功能 |
| `docs/architecture.md` | 组成表加两个项目；新增「macOS 客户端」一节；规则库条数改为版本 8、411 条；修正浏览器分类的旧说法（正在播放当前标签页的视频算看视频，和代码一致） |
| `docs/known-issues.md` | 新增「macOS」一节 |
| `docs/progress.md` | 「已定」加 macOS 版；「还没验证」加 Mac 上的验证清单；「偏离方案」加本机密钥和开机自启两条；变更日志加一条 |
| `docs/roadmap.md` | 现状加上 macOS 客户端（测试版） |

## 四、文件格式和兼容性

- Windows 本机文件（`settings.json`、同步密钥、同步文件夹配置）的格式和位置都不变，已安装的用户升级后不用重新登录。
- 协议、REST 接口、数据库没有变化，旧客户端不受影响。
- 规则库版本 8 只是多了规则，旧客户端照常下载使用；Windows 上这些 Bundle ID 规则匹配不到任何进程，不影响分类。

## 五、和最初方案不同的地方

| 最初说法 | 实际 | 原因 |
|---|---|---|
| token 和同步密钥放在钥匙串 | 放在 `~/Library/Application Support/DeskPal/local.key`（权限 600）加密的文件里 | 未签名的应用每次更新后，钥匙串会当成另一个应用，弹出登录密码提示。保护程度和 Windows 的 DPAPI（当前用户）相同 |
| 开机自启用 SMAppService | 用 `~/Library/LaunchAgents/com.hulushilei.deskpal.autostart.plist` | 不依赖签名，能带 `--minimized` 参数 |
| 单实例用命名 Mutex | 用设置目录下 `instance.lock` 的排他锁 | Mutex 必须在同一个线程释放，异步代码不能保证；文件锁在进程结束时自动释放 |

## 六、验证

在用户电脑上把 .NET 10 SDK 装在 `.local\dotnet`（不进 git，不改系统），在仓库的临时副本里验证，通过后再用 git patch 写回仓库，并逐个比对了 80 个文件的 SHA-256，全部一致。

| 项目 | 结果 |
|---|---|
| Windows 客户端 `dotnet build src/DeskPal.Client` | 0 警告，0 错误 |
| 全部测试 `dotnet test tests/DeskPal.Tests` | 343 个通过，0 个失败 |
| Mac 客户端类型检查 `tools/MacCheck` | 0 警告，0 错误 |
| 规则库校验脚本 `validate-rules.ps1` | 通过：版本 8，411 条 |
| 前端 `tsc --noEmit && vite build` | 通过 |
| CI 配置 actionlint | 通过 |
| 模拟数据截图 | macOS 模式下首页、设置、登录页、置顶卡片的图标和文字正常 |

`.local` 下保留了 `dotnet`（SDK）和 `macref\Microsoft.macOS.dll`（类型检查用），可以随时删除。

## 七、还没做和还没验证

- **没有生成安装包**：`.pkg` 只能在 macOS 上打。要把改动推到一个分支，在 GitHub Actions 里手动运行 CI，从 Artifacts 下载。CI 的 `mac` 任务还没跑过，可能要调整。
- **没有在真实的 Mac 上运行过**。要逐项验证：安装和打开、辅助功能授权、窗口标题和全屏、Safari 和 Chrome 的网址、空闲和锁屏、MediaRemote 在 15.4 前后的表现、菜单栏图标左右键、面板拖动和关闭方式、置顶卡片、登录和退出登录后重启、开机自启、文件和文件夹选择、自动更新（更新后是否要重新授权辅助功能）、和 Windows 电脑在同一个房间互看状态和统计。
- 正式发布时 `vpk upload --merge` 往 Windows 建好的 Release 追加文件，这一步还没在 GitHub 上跑过。

## 八、已知限制（摘要）

- 未签名：第一次打开要在「系统设置 → 隐私与安全性」允许；每次更新后要重新授权辅助功能。
- macOS 15.4 起读不到正在播放的媒体，网页视频和全屏播放不显示「在看视频」；播放器应用仍按规则识别。
- 窗口的最小化和关闭按钮在右上角，是页面画的，不是 macOS 的红黄绿按钮。
- 置顶卡片不抢焦点，点卡片上的按钮偶尔要点两下。
- 没有程序坞图标，Cmd+Tab 里看不到。

完整内容见 [known-issues.md](known-issues.md) 的「macOS」。
