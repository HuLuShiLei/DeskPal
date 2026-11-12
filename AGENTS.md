# DeskPal

Windows 状态共享客户端：同一个房间的成员实时看到对方在工作、聊天还是放松（以及在看什么、玩什么）。不截图，只采集前台应用、浏览器域名、空闲、锁屏、媒体播放这几类信号，在本机分类、按该房间的隐私设置过滤后，再通过 SignalR 推送。

当前可以加入多个房间，同一时间只进入一个。设计按后续游戏化（类似 Steam 上 Bongo Cat 的“打字攒道具”）预留了扩展点。

对用户用中文。页面和会显示在页面上的错误信息写「房间」，不写 Room。代码、协议字段和类型名仍用 Room。Presence、Envelope 保持英文。

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
src/DeskPal.Client/       WPF 托盘程序：Collectors、Native、Services、Ui（WebView2 宿主）、Views；web/ 是面板和登录页的前端（Vite + Preact）
tests/DeskPal.Tests/      xUnit：Client.Core 逻辑、RoomService、PresenceHub 集成测试
.github/workflows/        ci（测试 + 客户端 exe）、docker（Server 镜像推 GHCR）
deploy/                   Dockerfile.server、Portainer stack.yml、Traefik 路由（已部署到 192.168.66.10）、部署说明
```

## 常用命令

```powershell
dotnet build DeskPal.slnx
dotnet test tests/DeskPal.Tests
cd src/DeskPal.Server; dotnet run            # http://localhost:5180
dotnet run --project src/DeskPal.Client      # 本机客户端
cd src/DeskPal.Client/web; npm run dev  # 面板前端，http://localhost:5190/，用模拟数据
dotnet ef migrations add <Name> --project src/DeskPal.Server --output-dir Data/Migrations
```

- 发布：推送 main 会自动构建 Server 镜像（`ghcr.io/hulushilei/deskpal-server:latest`），构建成功后调用 Portainer webhook 拉取并重建容器。webhook 地址放在 GitHub secret `PORTAINER_WEBHOOK_URL`，不进仓库。main 上改了客户端时，CI 会发测试版 Pre-release（`DeskPal-<版本>-Setup.exe`，例如 `DeskPal-0.1.2-dev.15-Setup.exe`）；打 `vX.Y.Z` 标签并推送，会发正式版 `DeskPal-<版本>-Setup.exe`（例如 `DeskPal-0.1.2-Setup.exe`）。远程仓库是 `https://github.com/HuLuShiLei/DeskPal.git`。
- 客户端默认 Server 地址是生产环境 `https://deskpal.hafuhafu.cn:36443`，本地联调要在登录窗口或 `settings.json` 里改成 `http://localhost:5180`。
- `dotnet-ef` 是本地工具（`dotnet-tools.json`），新机器先执行 `dotnet tool restore`。
- 根目录 `nuget.config` 只保留 nuget.org。本机全局配置里的公司源在当前网络下连不上，会让还原卡住，不要删这个文件。
- 开发端口是 5180。默认的 5284 被 QQ 占用。前端开发服务器是 5190。
- 编译客户端需要 Node（20.19 以上），会自动执行 `npm ci` 和 `npm run build`。没有 Node 时加 `-p:SkipWebBuild=true`，使用已有的 `web/dist`。
- 改完代码至少跑 `dotnet build` 和 `dotnet test`。动了采集、Hub 或界面，再按 skill `deskpal-e2e-check` 做一次端到端自检。

## 硬约束

1. **不截图、不装键盘钩子、不注入、不提权。** 客户端以普通用户权限运行。后期游戏化如果要统计按键，只能用 Raw Input 计次数，不读按键内容。
2. **原始窗口标题和媒体标题不离开本机**，除非该 Room 成员把对应类别的隐私级别设为“显示详情”。上报前必须经过 `PrivacyFilter.Apply`。
3. **Server 只按 `Envelope.Type` 和 `RoomId` 路由，不解析 `Payload`。** 这是为以后做端到端加密（`Enc` 字段）留的口子。Server 自己生成的消息（离线、Room 事件）例外。
4. **成员的最新状态只存在 Server 内存里**（`PresenceStore`），不写数据库。数据库只存账号、Room、成员关系、邀请码、踢出后的禁止再次加入记录、隐私设置和规则。
5. **数据模型按 Room 设计，不按“一对”设计。** 人数上限来自配置 `Rooms:MaxMembersPerRoom`，默认 50。已有房间在列出或有人加入时改成当前配置。
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

## 项目 rule

`.cursor/rules/` 下的 rule 按路径自动生效：`server`（Server）、`shared-protocol`（Shared 协议）、`client-collectors`（采集器红线）、`client-core`（分类、状态、防抖语义）、`wpf-ui`（界面）、`builtin-rules-json`（内置规则库）。
