# DeskPal

Windows、macOS 和 Linux 状态共享客户端：同一个房间的成员实时看到对方在工作、聊天还是放松（以及在看什么、玩什么）。不截图，只采集前台应用、浏览器域名、空闲、锁屏、媒体播放这几类信号，在本机分类、按该房间的隐私设置过滤后，再通过 SignalR 推送。可以加入多个房间，同一时间最多进入一个；设计按后续游戏化预留了扩展点。

对用户用中文。页面和会显示在页面上的错误信息写「房间」，不写 Room；代码、协议字段和类型名仍用 Room。Presence、Envelope 保持英文。界面有简体中文和英文两套文案，都在客户端，新增文案两种语言都要写；Server 只返回错误码（`ApiErrorCodes`）。

各目录还有自己的 `AGENTS.md`（Server、Shared、Client.Core、三个客户端、`web/` 页面），改到那里的代码时一并遵守。

## 开发方式

- 个人项目，用 Multica 小队开发，流程按小队的 skill。本文件和各目录的 `AGENTS.md` 只写项目事实。
- 三个平台都要能编译、能运行。改共用层（Shared、Client.Core、Client.App、`web/`）时三个客户端都要编译通过；只能在某个平台上看的效果，交付时写明要在哪个平台上看什么。
- 在 `agent/<issue编号>-<主题>` 分支上提交并推送。**不要直接提交或推送 `main`**：推送 main 会自动部署生产 Server、发布测试版客户端。推送 main、打 tag、发版只在用户要求时做，按 skill `deskpal-release`。
- 已有外部用户，客户端会自动更新。Server 和客户端一起发布，协议不为旧客户端做兼容（见 `src/DeskPal.Shared/AGENTS.md`）；数据库迁移要保证存量数据。
- 临时文件放 `.local/tmp/<用途>/`（已加入 `.gitignore`），用完删除。

## 先读什么

| 要做的事 | 先读 |
|---|---|
| 了解整体设计、协议、数据模型、各平台差异 | `docs/architecture.md` |
| 已定的口径、偏离 plan 的地方 | `docs/progress.md` |
| 下一步做什么 | `docs/roadmap.md` |
| 已知限制和技术债 | `docs/known-issues.md` |
| 插件协议 | `docs/provider-guide.md` |

## 项目结构

```
src/DeskPal.Shared/       Envelope、MessageTypes、状态枚举、DTO、RulePattern（Server 和客户端共用）
src/DeskPal.Server/       ASP.NET Core：Features/{Auth,Rooms,Presence,Rules}、Data（EF Core + SQLite + 迁移）
src/DeskPal.Client.Core/  纯逻辑，无平台依赖：事件总线、分层分类器、状态判定、防抖、隐私过滤、文案、PanelState
src/DeskPal.Client.App/   三个客户端共用的服务：连接、房间、状态推送、统计、同步、更新、面板命令（PanelBridge）、启动流程、插件宿主（Providers/ProviderHost）。平台能力走 Platform/IClientPlatform
src/DeskPal.Client/       Windows 托盘程序（WPF + WebView2）：Collectors、Native、Platform、Ui；web/ 是面板和登录页的前端（Vite + Preact），三个平台共用
src/DeskPal.Client.Mac/   macOS 菜单栏程序（net10.0-macos，AppKit + WKWebView）
src/DeskPal.Client.Linux/ Linux 托盘程序（GTK 4 + WebKitGTK 6.0）；gnome-extension/、browser-extension/ 随包发布
tools/MacCheck/           不在 Mac 上时检查 macOS 客户端能否编译
plugin-examples/          插件代码示例，只作参考，不随程序发布
tests/DeskPal.Tests/      xUnit：Client.Core 逻辑、Server 服务、PresenceHub 集成测试
.github/workflows/        ci（测试 + 三个平台的安装包）、docker（Server 镜像）、rule-candidates（每天生成规则库 PR）
installer/                Windows 的 Inno Setup 安装向导
deploy/                   Server 的 Dockerfile、Portainer stack、Traefik 路由、部署说明
```

## 命令

三个平台通用：

```bash
dotnet tool restore                      # 新机器先还原 dotnet-ef（本地工具）
dotnet test tests/DeskPal.Tests          # 全量单元测试和集成测试
cd src/DeskPal.Server && dotnet run      # Server，http://localhost:5180
cd src/DeskPal.Client/web && npm run dev # 面板前端，http://localhost:5190/，模拟数据
dotnet ef migrations add <Name> --project src/DeskPal.Server --output-dir Data/Migrations
```

客户端：

| 客户端 | 在本平台编译和运行 | 在其他平台上只检查编译 |
|---|---|---|
| Windows | `dotnet run --project src/DeskPal.Client` | `dotnet build src/DeskPal.Client -p:EnableWindowsTargeting=true -p:SkipWebBuild=true` |
| macOS | `dotnet build src/DeskPal.Client.Mac -r osx-arm64`（SDK、workload、Xcode 版本见 `ci.yml` 的 `mac` 任务） | `dotnet build tools/MacCheck` |
| Linux | `dotnet run --project src/DeskPal.Client.Linux`（需要 GTK 4、WebKitGTK 6.0 和桌面会话） | `dotnet build src/DeskPal.Client.Linux` |

- `DeskPal.slnx` 只有 Shared、Server、Client.Core、Client.App、Windows 客户端和测试，不含 macOS、Linux 客户端。不在 Windows 上编译它要加 `-p:EnableWindowsTargeting=true`。
- 编译客户端需要 Node 20.19 以上，会自动执行 `npm ci` 和 `npm run build`。没有 Node 时加 `-p:SkipWebBuild=true`，使用已有的 `web/dist`。
- 开发端口：Server 5180（默认的 5284 被 QQ 占用），前端开发服务器 5190。
- 客户端默认连接生产 Server `https://deskpal.hafuhafu.cn:36443`，本地联调在客户端 `settings.json` 的 `serverUrl` 改成 `http://localhost:5180`。登录页和设置页不显示、也不能修改 Server 地址，不要加回来。
- 根目录 `nuget.config` 只保留 nuget.org，不要删：有的机器全局配置里有连不上的公司源，会让还原卡住。

## 验证

- 每次改动：`dotnet test tests/DeskPal.Tests`，加上受影响项目的编译。
- 改了共用层：三个客户端都按上表编译一遍。
- 改了采集链路、Hub、通信协议、同步、数据库迁移、安装更新这类大改：再按 skill `deskpal-e2e-check` 做端到端自检。
- 只改界面：`npm run dev` 的模拟数据检查，见 `src/DeskPal.Client/web/AGENTS.md`。

## 硬约束

1. **不截图、不装键盘钩子、不注入、不提权。** 客户端以普通用户权限运行。后期游戏化如果要统计按键，只能用 Raw Input 计次数，不读按键内容。macOS 只申请「辅助功能」，不申请「屏幕录制」和「输入监控」。
2. **原始窗口标题和媒体标题不离开本机**，除非该房间成员把对应类别的隐私级别设为“显示详情”。上报前必须经过 `PrivacyFilter.Apply`。
3. **Server 只按 `Envelope.Type`、`RoomId` 和 `To` 路由，不解析 `Payload`。** 这是为端到端加密（`Enc` 字段）留的口子，`stats.response` 已经是密文。Server 自己生成的消息（离线、房间事件）例外。
4. **成员的最新状态只存在 Server 内存里**（`PresenceStore`），不写数据库。个人分类、不记录标题、暂离时长和偏好设置只在用户开启「同步到账号」后上传，关闭时 Server 立即删除；使用统计只在开启「同步到服务器」后上传密文。新增要存到 Server 的数据前，先对照 `docs/architecture.md` 的「数据模型」「个人数据」，涉及用户数据的口径要问用户。
5. **数据模型按房间设计，不按“一对”设计。** 人数上限来自配置 `Rooms:MaxMembersPerRoom`，默认 20。
6. **房间成员关系变化后，必须调用 `RoomNotifier` 同步 SignalR Group。** 否则退出房间的人还会继续收到消息。
7. **采集器**（三个客户端的 Collectors、Linux 的 Providers、用户插件）：只用普通用户权限就能调用的 API，不读其他进程内存；用 `PeriodicTimer` 轮询，只在值变化时发布（浏览器网站例外，每次浏览器窗口变化都要发）；发布到构造函数收的 `SignalSink`，不直接用 `ActivityBus`，不用 `AddHostedService` 单独注册，加进平台官方插件的部件列表；只发布事实，不做分类；只取最少的信息（浏览器只取 host，空闲只取时间戳）；系统调用都 try/catch，单个采集器失败只记 Debug 日志；可能很慢的调用放 `Task.Run` 加超时，上一次没返回就跳过这一轮。步骤见 skill `deskpal-add-signal`。
8. Server 运行时目录叫 `storage/`，源码目录叫 `Data/`。Windows 路径不区分大小写，不要把数据库目录改成 `data`。数据库路径只由连接串决定。

## 项目 skill

放在 `.claude/skills/`，Claude Code 和 Cursor 都会读取。

| skill | 什么时候用 |
|---|---|
| `deskpal-e2e-check` | 启动 Server 和模拟成员做端到端自检，再按平台跑真实客户端 |
| `deskpal-add-message-type` | 新增一种实时消息（例如摸鱼同步提醒、游戏事件） |
| `deskpal-add-signal` | 新增一种采集信号或采集器 |
| `deskpal-db-change` | 新增实体、字段，生成 EF Core 迁移 |
| `deskpal-builtin-rules` | 增删改内置分类规则库 |
| `deskpal-write-provider` | 写插件（provider），或修改插件协议、插件宿主 |
| `deskpal-ui-copy` | 新增或修改任何用户可见文字（中英文、对话框、提示、错误、CHANGELOG） |
| `deskpal-release` | 推送 main、写 CHANGELOG、打 tag、发正式版 |
