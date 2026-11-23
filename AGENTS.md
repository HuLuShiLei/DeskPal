# DeskPal

Windows、macOS 和 Linux 状态共享客户端：同一个房间的成员实时看到对方在工作、聊天还是放松（以及在看什么、玩什么）。不截图，只采集前台应用、浏览器域名、空闲、锁屏、媒体播放这几类信号，在本机分类、按该房间的隐私设置过滤后，再通过 SignalR 推送。

当前可以加入多个房间，同一时间最多进入一个，也可以在房间菜单里选「无房间」。一个房间都没有时同样是无房间。设计按后续游戏化（类似 Steam 上 Bongo Cat 的“打字攒道具”）预留了扩展点。

对用户用中文。页面和会显示在页面上的错误信息写「房间」，不写 Room。代码、协议字段和类型名仍用 Room。Presence、Envelope 保持英文。

客户端界面有简体中文和英文两套文案，都在客户端：C# 的在 `DeskPal.Client.Core/Localization/Strings*.resx`，页面的在 `web/src/i18n/`。Server 只返回错误码（`ApiErrorCodes`），不返回给用户看的文字。新增文案两种语言都要写。

## 先读什么

| 要做的事 | 先读 |
|---|---|
| 了解整体设计、协议、数据模型 | `docs/architecture.md` |
| 已定的口径、做到哪了、偏离 plan 的地方 | `docs/progress.md` |
| 下一步做什么 | `docs/roadmap.md` |
| 已知限制和技术债 | `docs/known-issues.md` |
| 怎么开新对话继续开发 | `docs/vibe-coding-guide.md` |
| 已确认的原始 plan | `docs/plan-v1.md` |

## 项目结构

```
src/DeskPal.Shared/       Envelope、MessageTypes、状态枚举、DTO、RulePattern（Server 和客户端共用）
src/DeskPal.Server/       ASP.NET Core：Features/{Auth,Rooms,Presence,Rules}、Data（EF Core + SQLite + 迁移）
src/DeskPal.Client.Core/  纯逻辑，无 Windows 依赖：事件总线、分层分类器、状态判定、防抖、隐私过滤、文案
src/DeskPal.Client.App/   Windows、macOS、Linux 共用的客户端服务：连接、房间、状态推送、统计、同步、更新、面板命令（PanelBridge）、启动流程（ClientStartup）、插件宿主（Providers/ProviderHost，官方插件和用户插件按列表路由）。平台能力走 Platform/IClientPlatform、ISecretProtector
src/DeskPal.Client/       WPF 托盘程序：Collectors（Windows 官方插件的部件）、Native、Platform（WindowsPlatform、DPAPI）、Ui（WebView2 宿主）；web/ 是面板和登录页的前端（Vite + Preact），三个平台共用
src/DeskPal.Client.Mac/   macOS 菜单栏程序（net10.0-macos，AppKit + WKWebView）：Collectors（macOS 官方插件的部件：NSWorkspace、辅助功能）、Native、Platform、Ui。只能在 Mac 上编译
src/DeskPal.Client.Linux/ Linux 托盘程序（net10.0，GTK 4 + WebKitGTK 6.0，GirCore）：Providers（Linux 官方插件：X11 或 GNOME 扩展读窗口、BrowserBridge 收浏览器扩展报的网站）、Collectors（空闲、锁屏、MPRIS）、Native、Platform、Ui；gnome-extension/ 是随包发布的 GNOME Shell 扩展，browser-extension/ 是随包发布的浏览器扩展。任何系统都能编译，只能在 Linux 上运行
tools/MacCheck/           没有 Mac 时对 macOS 引用程序集做类型检查
tests/DeskPal.Tests/      xUnit：Client.Core 逻辑、RoomService、PresenceHub 集成测试
.github/workflows/        ci（测试 + 客户端 exe）、docker（Server 镜像推 GHCR）、rule-candidates（每天把多人一致的个人分类和推测出的游戏做成规则库 PR）
installer/                Inno Setup 安装向导：选择安装目录，内嵌 Velopack 的 Setup.exe
deploy/                   Dockerfile.server、Portainer stack.yml、Traefik 路由（已部署到 192.168.66.10）、部署说明
```

## 常用命令

```powershell
dotnet build DeskPal.slnx
dotnet test tests/DeskPal.Tests
cd src/DeskPal.Server; dotnet run            # http://localhost:5180
dotnet run --project src/DeskPal.Client      # 本机客户端
dotnet build src/DeskPal.Client.Mac -r osx-arm64   # 只能在 Mac 上：macos workload 和 Xcode 要配套，版本以 ci.yml 的 mac 任务的 XCODE_VERSION、MACOS_WORKLOAD 为准
dotnet build tools/MacCheck -p:MacRef=<Microsoft.macOS.dll>   # Windows 上检查 Mac 客户端能否编译，见 tools/MacCheck
dotnet build src/DeskPal.Client.Linux      # Linux 客户端。运行要在 Linux 桌面上，系统里有 GTK 4 和 WebKitGTK 6.0
cd src/DeskPal.Client/web; npm run dev  # 面板前端，http://localhost:5190/，用模拟数据
dotnet ef migrations add <Name> --project src/DeskPal.Server --output-dir Data/Migrations
```

- macOS 客户端：CI 的 `mac` 任务在 macos-15 上按芯片打 `DeskPal-<版本>-osx-arm64-Setup.pkg` 和 `osx-x64`，和 Windows 版同一个版本号、同一个 Release，更新通道是 `osx-arm64`、`osx-x64`（测试版加 `-beta`）。默认不签名；仓库变量 `MAC_SIGN=true` 并配好 ci.yml 里列出的 secret 后签名并公证。在其他分支手动运行 CI 只打包不发布，安装包在 Artifacts 里。
- Linux 客户端：CI 的 `linux` 任务在 ubuntu-24.04 上打 `DeskPal-<版本>-linux-x64.AppImage`，同一个版本号、同一个 Release，更新通道是 `linux-x64`（测试版 `linux-x64-beta`）。窗口和网站的来源见 `docs/architecture.md` 的「Linux 客户端」。
- 插件：三个平台的活动信息（当前窗口、网站、暂离、锁屏、媒体）都来自插件。各平台的采集器组成官方插件（在程序内运行），用户插件是插件文件夹里的程序，按设置里的列表顺序逐项决定用谁的。采集器构造函数收 `SignalSink`，不要直接往 `ActivityBus` 发布，也不要用 `AddHostedService` 单独注册。见 `docs/architecture.md` 的「插件」和 `docs/provider-guide.md`。
- 发布：推送 main 会自动构建 Server 镜像（`ghcr.io/hulushilei/deskpal-server:latest`），构建成功后调用 Portainer webhook 拉取并重建容器。webhook 地址放在 GitHub secret `PORTAINER_WEBHOOK_URL`，不进仓库。main 上改了客户端时，CI 会发测试版 Pre-release（`DeskPal-<版本>-Setup.exe`，例如 `DeskPal-0.1.2-dev.15-Setup.exe`）；打 `vX.Y.Z` 标签并推送，会发正式版 `DeskPal-<版本>-Setup.exe`（例如 `DeskPal-0.1.2-Setup.exe`）。远程仓库是 `https://github.com/HuLuShiLei/DeskPal.git`。
- **推送 main 前先更新根目录的 `CHANGELOG.md`**：把这次要推送、用户能感受到的改动整理成书面中文条目，写进最上面一节，和代码一起推送。最上面一节是下一个正式版的版本号（例如 `## 0.1.2`），测试版的改动也写在这一节。发布正式版 `vX.Y.Z` 后，下一次推送先新开 `## X.Y.(Z+1)`。CI 发布时检查最上面一节的版本号：正式版必须和标签一致，测试版不能低于自动算出的版本号，否则发布失败；改了客户端却没改 `CHANGELOG.md` 时只给出警告。客户端更新后，第一次打开面板时按界面语言显示还没看过的条目。每条下面缩进一行 `en:` 写英文；看过与否按中文比对，改了中文会再显示一次。
- 安装包是 `installer/DeskPal.iss` 编译的 Inno Setup 向导，用户可以选择安装目录（默认 `%LocalAppData%\Programs\DeskPal`）。安装版的日志在 `<安装目录>\logs`，设置和规则在 `%APPDATA%\DeskPal`。
- 客户端默认 Server 地址是生产环境 `https://deskpal.hafuhafu.cn:36443`，本地联调要在 `settings.json` 的 `serverUrl` 里改成 `http://localhost:5180`。登录页和设置页不显示、也不能修改 Server 地址，不要加回来。
- `dotnet-ef` 是本地工具（`dotnet-tools.json`），新机器先执行 `dotnet tool restore`。
- 根目录 `nuget.config` 只保留 nuget.org。本机全局配置里的公司源在当前网络下连不上，会让还原卡住，不要删这个文件。
- 开发端口是 5180。默认的 5284 被 QQ 占用。前端开发服务器是 5190。
- 编译客户端需要 Node（20.19 以上），会自动执行 `npm ci` 和 `npm run build`。没有 Node 时加 `-p:SkipWebBuild=true`，使用已有的 `web/dist`。
- 这是个人项目，开发流程走用户级 skill `personal-dev-workflow`。改完代码至少跑 `dotnet build` 和受影响的测试。改了采集链路、Hub、通信协议、同步、数据库迁移、安装更新这类大改，再按 skill `deskpal-e2e-check` 做一次端到端自检；只改界面时用 `npm run dev` 的模拟数据截图检查。

## 硬约束

1. **不截图、不装键盘钩子、不注入、不提权。** 客户端以普通用户权限运行。后期游戏化如果要统计按键，只能用 Raw Input 计次数，不读按键内容。macOS 只申请「辅助功能」（读焦点窗口标题、浏览器地址），不申请「屏幕录制」和「输入监控」。
2. **原始窗口标题和媒体标题不离开本机**，除非该 Room 成员把对应类别的隐私级别设为“显示详情”。上报前必须经过 `PrivacyFilter.Apply`。
3. **Server 只按 `Envelope.Type`、`RoomId` 和 `To` 路由，不解析 `Payload`。** 这是为端到端加密（`Enc` 字段）留的口子，使用统计的 `stats.response` 已经是密文。Server 自己生成的消息（离线、Room 事件）例外。
4. **成员的最新状态只存在 Server 内存里**（`PresenceStore`），不写数据库。数据库只存账号、Room、成员关系、邀请码、注册邀请码、踢出后的禁止再次加入记录、成员之间的屏蔽记录、隐私设置、内置规则，开启「同步到账号」后的个人分类和不记录标题的规则（明文，关闭时立即删除），用户同意分享后上报的未识别应用（进程名、产品名、当天分钟数）和未开启「同步到账号」时一并上报的应用个人分类（进程名、类别、名称，只用于规则候选），以及用户开启「同步到服务器」后上传的使用统计（每天一份用 PIN 码加密的密文，Server 不解析，关闭时立即删除；只有 `StatsSync:AllowedUserNames` 名单里的账号能开启，名单外的在 Server 启动时关闭并删除）。暂离时长是账号上的一个整数；切换等待、外观、语言、关闭窗口时、窗口置顶、成员排序存在账号的 `Preferences`（JSON）里。个人分类、不记录标题、暂离时长和 `Preferences` 只在开启「同步到账号」（`User.PersonalSync`，默认关闭）后保存，登录后同步，不含使用内容；关闭时 Server 立即删除，本机保留。开机自启、加入测试版、暂停分享、选中的房间、两个同步文件夹路径只在本机。
5. **数据模型按 Room 设计，不按“一对”设计。** 人数上限来自配置 `Rooms:MaxMembersPerRoom`，默认 20。已有房间在列出或有人加入时改成当前配置。
6. **Room 成员关系变化后，必须调用 `RoomNotifier` 同步 SignalR Group。** 否则退出 Room 的人还会继续收到消息。
7. Server 运行时目录叫 `storage/`，源码目录叫 `Data/`。Windows 路径不区分大小写，不要把数据库目录改回 `data`。容器内数据库是 `/data/deskpal.db`（Linux 区分大小写，没有冲突），路径由连接串决定。
8. 默认不 commit、不 push。

## Windows 写文件

这台机器上 Write/StrReplace 工具可能写出 UTF-16。写文件按用户级 skill `write-file-windows` 来：`[System.IO.File]::WriteAllText` 加无 BOM 的 UTF-8，写完检查 NUL 字节数为 0。

临时文件放 `.local/tmp/<用途>/`（已加入 `.gitignore`），用完删除。

## 项目 skill

| skill | 什么时候用 |
|---|---|
| `deskpal-e2e-check` | 本机启动 Server、客户端和模拟成员做端到端自检 |
| `deskpal-add-message-type` | 新增一种实时消息（例如摸鱼同步提醒、游戏事件） |
| `deskpal-add-signal` | 新增一种采集信号或采集器 |
| `deskpal-db-change` | 新增实体、字段，生成 EF Core 迁移 |
| `deskpal-builtin-rules` | 增删改内置分类规则库 |
| `deskpal-write-provider` | 写插件（provider）提供当前窗口、网站、暂离、锁屏或媒体（三个平台），或修改插件协议、插件宿主 |
| `deskpal-ui-copy` | 新增或修改任何用户可见文字（中英文、对话框、提示、错误、CHANGELOG） |

## 项目 rule

`.cursor/rules/` 下的 rule 按路径自动生效：`server`（Server）、`shared-protocol`（Shared 协议）、`client-collectors`（采集器红线）、`client-core`（分类、状态、防抖语义）、`wpf-ui`（界面）、`builtin-rules-json`（内置规则库）。
