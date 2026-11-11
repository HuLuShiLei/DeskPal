# DeskPal 进度

新会话先读这份，了解哪些已经定了、做到了哪里。每次改了行为或做了决定，在最后的“变更日志”追加一条。

## 已定

| 主题 | 决定 | 原因 |
|---|---|---|
| 采集方式 | 不截图、不装钩子、不注入、不提权，只用普通用户权限的 API | 用户要求不截图；钩子和注入会被公司 EDR 当成键盘记录器 |
| 分类 | 内置规则库 + Room 规则 + 个人规则 + 推测，用户不需要自己配置进程名和域名 | 用户明确不想逐个配置 |
| 识别不出来 | 归为 `Other`，显示“正在使用 XX”，不算放松 | 宁可少报放松，不误报 |
| 麦克风 | 不采集，没有“会议中” | 麦克风被占用也可能是语音聊天或打游戏 |
| 聊天软件分类 | QQ、微信、Telegram 等个人聊天归为 `Communication`（在聊天）；企业微信、飞书、钉钉、邮箱、会议软件归为 `Work` | 用户选定 |
| 界面文案 | 书面用语：工作中、在聊天、正在浏览网页、正在使用 XX、在看视频、在看社交网站、在放松；不用“忙”“摸鱼”。本机识别分标题、应用、详情三行 | 用户要求 |
| 浏览器 | 前台是浏览器时显示“正在浏览网页”，后面是域名和页面标题，不按视频、社交、购物等细分。个人规则和 Room 规则仍可以把某个网站改成其他类别。默认隐私是“显示类别和应用”，Room 成员看到域名；标题只在“显示详情”时发出 | 用户要求浏览网页不做细分 |
| Room 改名 | 只有所有者可以改，1 到 30 个字符 | 用户选定 |
| 界面技术 | 面板和登录页用 .NET 宿主 + 标准 WebView2 的 HTML 界面（Preact），托盘菜单保持原生；以后的桌宠用 WPF 原生透明窗口。见 [ui-tech-evaluation.md](ui-tech-evaluation.md) | AI 能在浏览器里看到和测试界面；透明窗口里的 WebView2 静止时也占 CPU |
| 面板 | 一个窗口三个标签页：状态、Room、设置。Room 设置和应用设置分开。隐藏 5 分钟后释放 WebView2。更新的确认和提示显示在面板里 | 用户选定 |
| WebView2 Runtime | 安装包带 `--framework webview2`，安装时缺少就自动安装；启动时再检测一次，缺少时提示下载 | 少数 LTSC、精简版系统没有 |
| 隐私 | 按 Room、按类别配置；默认不发窗口标题 | 同一个人在伴侣 Room 和同事 Room 可以显示不同程度 |
| 通信 | Server 中转 + SignalR，不做 P2P；以后加端到端加密 | 公司网络打洞成功率低；多人 Room 和离线状态需要 Server；见 plan-v1 第八节 |
| Server 职责 | 只按 `Type` 和 `RoomId` 路由，不解析 Payload；实时状态只在内存里 | 给端到端加密留位置，少存数据 |
| 模型 | 按 Room 设计，原型阶段人数上限 2 | 以后会变成多人 Room，再往后可能做游戏化 |
| Room 生命周期 | 参考 QQ 群：不自动删除；每人最多拥有 5 个 Room；邀请码 24 小时过期，重新生成后旧码失效；所有者要先转让才能退出；所有者可以解散 | 用户选定 |
| 技术栈 | .NET 10：WPF 托盘客户端，ASP.NET Core + EF Core + SQLite，JWT，Docker 镜像（GHCR）+ Portainer + 已有 Traefik | 用户指定 C#，有自己的服务器 |
| 客户端更新 | Velopack。测试版通道 `beta`，正式版通道 `win`。安装测试版后默认继续接收测试版；正式版安装后要在托盘菜单勾选「加入测试版」才接收。启动后和每 6 小时检查一次，后台下载。已下载的包只在退出或选择「立即重启并更新」时安装，启动时不自动安装。取消测试版会删除已下载的包。不强制重启。从测试版回到较低正式版时先询问。只保留最近 8 个测试版。Velopack 只读取最近 10 个 GitHub Release，要给正式版留位置。GitHub Release 说明附带更新内容，取提交标题：测试版相对上一次发布，正式版相对上一个正式版，最多 40 条 | 试用人员要尽快拿到修复；GitHub Release 单个文件小于 2 GiB，没有总容量和流量限制 |
| 发布 | 代码在 GitHub `HuLuShiLei/DeskPal`；推送默认分支自动构建 Server 镜像，并调用 Portainer Business Edition 的 webhook 拉取重建；客户端测试版在 main 改了客户端时自动发 Pre-release，正式版只在手动推送 `vX.Y.Z` 标签时发布。安装包文件名是 `DeskPal-<版本号>-Setup.exe`。对外地址 `https://deskpal.hafuhafu.cn:36443`，也是客户端默认地址 | 运营商封了 80/443，只能用 36443；3 节点以内的 Business Edition 许可证免费；试用期要小步更新；用户要求安装包文件名带版本号 |
| 客户端版本 | 不考虑旧客户端。不再使用的状态和类别从枚举删除，不为旧版本保留分支 | 用户要求 |

## 已完成（第一期原型）

- Server：注册登录、Room 全部生命周期、隐私配置、Room 规则、内置规则库（266 条）、PresenceHub、Docker 部署文件、GitHub Actions（测试、客户端 exe、Server 镜像）。
- 客户端：5 个采集器、事件总线、分层分类、状态判断、防抖、隐私过滤、托盘和主窗口、登录、Room 菜单、戳一下、“这个判断不对”、手动状态、暂停分享、开机自启。
- 发布：GitHub Actions 三个任务（测试、客户端 exe、Server 镜像）都已跑通；Release `v0.1.0` 已发布；Traefik 主机上已新增 `/etc/traefik/conf/deskpal.yml`。
- 测试：88 个，全部通过。
- 第一批调整（[plan-2026-11-11-presence-ui.md](plan-2026-11-11-presence-ui.md)）：Room 改名、去掉麦克风、内置规则库版本 2、书面文案、本机识别分行显示。端到端自检过：改名广播、非所有者改名返回 403、对方卡片的长标题截断。
- 第二批调整（HTML 界面）：面板、登录页改为 WebView2 + Preact，删除 `MainWindow`、`MainViewModel`、`Views/Dialogs.cs` 和 CommunityToolkit.Mvvm。模拟数据下在浏览器里逐页检查过；真实客户端端到端检查过面板显示、“戳一下”、暂停分享、手动状态、改名，以及隐藏 5 分钟后释放 WebView2。
- 本机端到端验证过：模拟成员发送的状态在界面正确显示；对方收到本机状态（`Since` 正确）、“戳一下”的文字、暂停分享（立即生效）。

## 还没验证

- 两台真实电脑之间联调，Server 部署到真实服务器。
- 浏览器域名（UI Automation）、媒体播放、锁屏这几个信号没有专门触发过。
- 纠正、隐私设置、转让、解散在模拟数据下点过，没有连真实 Server 点过。
- 没有 WebView2 Runtime 的系统上安装（要在第一次正式发布前用虚拟机装一次）。
- Server 已通过 Portainer webhook 部署。从带 `revision` 的版本起，CI 会等到 `/health` 返回本次提交号。两台真实电脑登录还没验证。

## 偏离方案

| 方案 | 实际 | 原因 |
|---|---|---|
| 客户端一个项目 | 拆出 `DeskPal.Client.Core` | 纯逻辑可以在 net10.0 下单元测试 |
| `SetWinEventHook` 事件驱动 | 每秒轮询 `GetForegroundWindow` | 更简单，不依赖消息循环；开销可以忽略 |
| Windows Toast 通知 | 托盘气泡 | 少一个依赖；不需要注册 AppUserModelID |
| 麦克风占用算开会 | 不采集麦克风，枚举里也没有 `Meeting` | 麦克风被占用也可能是语音聊天或打游戏；用户要求不再保留旧客户端兼容 |
| 内置规则按增量更新 | 版本号不同就全量下载 | 规则只有几百条，全量下载更简单 |
| 前置 Caddy 自动配置 HTTPS | 用已有的 Traefik（DNS challenge 泛域名证书），对外地址 `https://deskpal.hafuhafu.cn:36443` | 用户已有 Traefik；运营商封了 80/443 |
| 端口 | 本地开发用 5180 | 5284 被 QQ 占用 |
| 数据库目录 `data/` | `storage/` | Windows 不区分大小写，会和源码目录 `Data/` 冲突 |

## 变更日志

| 日期 | 改动 |
|---|---|
| 2026-11-10 | 第一期原型完成：Server、客户端、测试、部署文件 |
| 2026-11-10 | 增加 AGENTS.md、`.cursor/rules`、`.cursor/skills`、`docs/` |
| 2026-11-10 | 部署改为 GHCR 镜像 + Portainer Stack + 已有 Traefik，去掉 Caddy；容器内数据库路径改为 `/data/deskpal.db`；Server 启动时按连接串创建数据库目录；增加 GitHub Actions |
| 2026-11-10 | 客户端默认服务器地址改为 `https://deskpal.hafuhafu.cn:36443`；Traefik 新增 `deskpal.yml` 路由 |
| 2026-11-10 | 发布 Release `v0.1.0`；同步 AGENTS.md、docs、server rule、e2e skill 里的部署和发布说明 |
| 2026-11-11 | main 构建成功后由 GitHub Actions 调用 Portainer webhook 更新容器；Traefik 只在 36443 公开 webhook token 路径 |
| 2026-11-11 | Portainer Community Edition 的 webhook 开关不可用，改为 Stack 内的 Watchtower；撤掉公网上的 webhook 路由 |
| 2026-11-11 | Portainer 升级到 Business Edition，恢复 webhook 自动更新，去掉 Watchtower |
| 2026-11-11 | 镜像写入 Git 提交号；webhook 使用 sha-<提交号> 标签，CI 等到 /health 的 revision 一致才算部署成功 |
| 2026-11-11 | 客户端用 Velopack 自动更新：main 发测试版，vX.Y.Z 发正式版；托盘菜单可加入测试版并手动检查更新 |
| 2026-11-11 | 已下载的更新只在退出或立即重启时安装，启动时不自动安装；取消测试版或下载失败后，不再把失效的包当成可安装 |
| 2026-11-11 | 测试版打包不再生成便携压缩包。vpk 按打包清单上传，先删掉清单里的文件会让 Release 发布失败 |
| 2026-11-11 | GitHub Issue 使用填空表单：试用人员选择「问题反馈」或「功能建议」，不从空白 Issue 开始 |
| 2026-11-11 | Room 改名（只有所有者，广播 `room.renamed`）；去掉麦克风采集和“会议中”；内置规则库版本 2（办公聊天和邮箱归为工作，个人聊天归为聊天）；文案改为书面用语，本机识别分行显示；界面技术定为 WebView2，待验证 |
| 2026-11-11 | 面板和登录页改为 WebView2 + Preact 的 HTML 界面，分状态、Room、设置三个标签页；更新提示显示在面板里；面板隐藏 5 分钟后释放 WebView2；设置里增加窗口置顶；安装包带 WebView2 检查；离线成员不能“戳一下” |
| 2026-11-11 | GitHub Release 说明附带更新内容，取自上一个版本以来的提交标题 |
| 2026-11-11 | 面板大小固定为 420×680，不能拖动边框调整 |
| 2026-11-11 | 前台是浏览器时显示“正在浏览网页”，后面是域名和页面标题，不再按网站分成视频、社交、购物等 |
| 2026-11-11 | GitHub Release 的安装包改为 `DeskPal-<版本号>-Setup.exe`。删除只为旧客户端保留的 `Meeting` |
