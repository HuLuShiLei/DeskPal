# DeskPal 进度

新会话先读这份，了解哪些已经定了、做到了哪里。每次改了行为或做了决定，在最后的“变更日志”追加一条。

## 已定

| 主题 | 决定 | 原因 |
|---|---|---|
| 采集方式 | 不截图、不装钩子、不注入、不提权，只用普通用户权限的 API | 用户要求不截图；钩子和注入会被公司 EDR 当成键盘记录器 |
| 分类 | 内置规则库 + Room 规则 + 个人规则 + 推测，用户不需要自己配置进程名和域名 | 用户明确不想逐个配置 |
| 识别不出来 | 归为 `Other`，按“忙”处理 | 宁可少报摸鱼，不误报 |
| 隐私 | 按 Room、按类别配置；默认不发窗口标题 | 同一个人在伴侣 Room 和同事 Room 可以显示不同程度 |
| 通信 | Server 中转 + SignalR，不做 P2P；以后加端到端加密 | 公司网络打洞成功率低；多人 Room 和离线状态需要 Server；见 plan-v1 第八节 |
| Server 职责 | 只按 `Type` 和 `RoomId` 路由，不解析 Payload；实时状态只在内存里 | 给端到端加密留位置，少存数据 |
| 模型 | 按 Room 设计，原型阶段人数上限 2 | 以后会变成多人 Room，再往后可能做游戏化 |
| Room 生命周期 | 参考 QQ 群：不自动删除；每人最多拥有 5 个 Room；邀请码 24 小时过期，重新生成后旧码失效；所有者要先转让才能退出；所有者可以解散 | 用户选定 |
| 技术栈 | .NET 10：WPF 托盘客户端，ASP.NET Core + EF Core + SQLite，JWT，Docker 镜像（GHCR）+ Portainer + 已有 Traefik | 用户指定 C#，有自己的服务器 |
| 发布 | 代码在 GitHub `HuLuShiLei/DeskPal`；推送默认分支自动构建 Server 镜像；推送 `v*` 标签自动创建 Release 并附客户端 zip；对外地址 `https://deskpal.hafuhafu.cn:36443`，也是客户端默认地址 | 运营商封了 80/443，只能用 36443 |

## 已完成（第一期原型）

- Server：注册登录、Room 全部生命周期、隐私配置、Room 规则、内置规则库（266 条）、PresenceHub、Docker 部署文件、GitHub Actions（测试、客户端 exe、Server 镜像）。
- 客户端：6 个采集器、事件总线、分层分类、状态判断、防抖、隐私过滤、托盘和主窗口、登录、Room 菜单、戳一下、“这个判断不对”、手动状态、暂停分享、开机自启。
- 发布：GitHub Actions 三个任务（测试、客户端 exe、Server 镜像）都已跑通；Release `v0.1.0` 已发布；Traefik 主机上已新增 `/etc/traefik/conf/deskpal.yml`。
- 测试：35 个，全部通过。
- 本机端到端验证过：模拟成员发送的状态在界面正确显示；对方收到本机状态（`Since` 正确）、“戳一下”的文字、暂停分享（立即生效）。

## 还没验证

- 两台真实电脑之间联调，Server 部署到真实服务器。
- 浏览器域名（UI Automation）、麦克风、媒体播放、锁屏这几个信号没有专门触发过。
- 纠正、隐私设置、转让、解散这几个对话框没有在界面上点过。
- Server 镜像在 CI 里构建成功，但还没在 Portainer 上运行过；外网访问 `https://deskpal.hafuhafu.cn:36443/health` 没验证。

## 偏离方案

| 方案 | 实际 | 原因 |
|---|---|---|
| 客户端一个项目 | 拆出 `DeskPal.Client.Core` | 纯逻辑可以在 net10.0 下单元测试 |
| `SetWinEventHook` 事件驱动 | 每秒轮询 `GetForegroundWindow` | 更简单，不依赖消息循环；开销可以忽略 |
| Windows Toast 通知 | 托盘气泡 | 少一个依赖；不需要注册 AppUserModelID |
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
