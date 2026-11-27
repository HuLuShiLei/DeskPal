---
name: deskpal-e2e-check
description: 对 DeskPal 做端到端自检，Windows、macOS、Linux 通用：启动本地 Server，用辅助程序建测试账号和房间、模拟房间成员收发状态（协议自检，不需要桌面）；在有桌面的平台上再以测试账号跑真实客户端，最后清理现场。改了采集器、PresenceHub、RoomState、PresenceCoordinator、通信协议、同步、数据库迁移这类大改后，或者用户说自检、联调、端到端测试时使用。只改界面、文案、样式不用。
---

# DeskPal 端到端自检

单元测试和 `PresenceHubTests` 覆盖不到真实的 Server 进程、采集和界面，这个流程补上这一块。分两部分：

- **A. 协议自检**：本地 Server 加几个模拟成员。任何平台都能做，不需要桌面，Multica 的后台任务也能做。
- **B. 真实客户端**：在对应平台的桌面上，以测试账号启动客户端。要用户先退出自己的 DeskPal，做之前必须先问。

改了哪一块就测哪一块：只动 Server、Hub、协议的，做 A 就够；动了采集、客户端状态、界面联动的，A 之后在能用的平台上做 B，其余平台写进「需要人看的」。

所有命令在仓库根目录运行。临时文件都在 `.local/tmp/e2e/`。辅助程序 `E2eHelper` 的命令记作：

```bash
H="dotnet run --project .claude/skills/deskpal-e2e-check/E2eHelper --"   # PowerShell：function H { dotnet run --project .claude/skills/deskpal-e2e-check/E2eHelper -- @args }
```

```
进度：
- [ ] 1. 启动 Server
- [ ] 2. 建测试账号和房间
- [ ] 3. A：模拟成员收发
- [ ] 4. B：准备测试客户端（先问用户）
- [ ] 5. B：启动客户端并验证
- [ ] 6. 清理并恢复
```

## 1. 启动 Server

```bash
cd src/DeskPal.Server && dotnet run      # 放到后台
curl http://localhost:5180/health        # 返回 {"status":"ok"}；Windows PowerShell 用 Invoke-RestMethod
```

## 2. 建测试账号和房间

```bash
$H setup
```

注册 `e2e_alice<随机数>`、`e2e_bob…`、`e2e_carol…`，alice 建「测试房间」，bob、carol 加入。token 只写进 `.local/tmp/e2e/accounts.json`，不要贴进对话。

## 3. A：模拟成员收发

`$H member <bob|carol|alice> <秒数> [video|working|paused|none]`：连上 Hub，发一条状态（`none` 只收不发），打印收到的消息。一个放后台收，另一个发：

```bash
$H member carol 15 none > .local/tmp/e2e/carol.log &
$H member bob 6 video
cat .local/tmp/e2e/carol.log
```

| 检查项 | 预期 |
|---|---|
| bob 发 `video` | carol 收到 bob 的 `presence.update`，`state` 是 `Slacking`、`category` 是 `Video` |
| bob 断开 | carol 收到 bob 的 `Offline` |
| bob 发 `paused` | carol 收到 `Paused`，不带类别、应用名和详情 |
| `$H block alice bob` 后 alice 发状态 | bob 收不到 alice 的状态，也收不到房间事件；carol 照常收到。`$H unblock alice bob` 后恢复 |

改了别的消息，就照 `Program.cs` 里 `Member` 的写法临时加一种状态或消息类型；不要把一次性的改动提交进来。

## 4. B：准备测试客户端

**先问用户**：客户端是单实例的（Windows 命名 Mutex、macOS `instance.lock`、Linux `Gtk.Application`），用户自己的 DeskPal 在运行时测试客户端起不来，而且它连的是生产环境。请用户从托盘或菜单栏退出，用户确认后再继续。

```bash
$H client-settings
```

它把本机的客户端设置目录（Windows `%APPDATA%\DeskPal`，macOS `~/Library/Application Support/DeskPal`，Linux `~/.config/DeskPal`，里面是用户真实的登录信息）整个移到 `.local/tmp/e2e/settings-backup/`，再写入 alice 的测试设置：连 `http://localhost:5180`，token 按该平台客户端的方式加密（Windows DPAPI，macOS、Linux 设置目录里的 `local.key`）。已有备份时它会拒绝执行，先 `restore-settings`。

Windows 上另外记下 `%LOCALAPPDATA%\DeskPal\WebView2` 是否已经存在（安装版也用这个目录），第 6 步据此决定删不删。

## 5. B：启动客户端并验证

| 平台 | 启动 | 前提 |
|---|---|---|
| Windows | `dotnet run --project src/DeskPal.Client` | 不是 Velopack 安装版，不会检查更新 |
| macOS | `dotnet run --project src/DeskPal.Client.Mac -r osx-arm64` | 「隐私与安全性 → 辅助功能」允许它，否则读不到标题和网址 |
| Linux | `dotnet run --project src/DeskPal.Client.Linux` | 桌面会话，GTK 4、WebKitGTK 6.0；GNOME Wayland 要装 DeskPal 的 GNOME 扩展才有当前窗口 |

同时在后台跑模拟成员（例如 `$H member bob 150 video`），读它的输出，并截图看客户端界面：

| 检查项 | 预期 | 平台 |
|---|---|---|
| 面板外观 | 没有系统标题栏；页面内标题栏中间是房间切换菜单，可以拖动窗口，最小化和关闭可用；左侧导航底部的圆点是绿色（已连接） | 全部（Windows 11 另有圆角和阴影） |
| 面板位置 | 第一次打开停靠在屏幕右下角 | Windows、macOS |
| 第一次打开 | 弹出“帮助改进默认分类”的询问；选择后 `GET /api/me` 的 `shareUsage` 和本机 `settings.json` 一致 | 全部 |
| Bob 的卡片 | 带状态色的“在看视频”标签，下面是“哔哩哔哩”和最多两行的标题，右上角“已持续 12 分钟” | 全部 |
| “我的状态” | 大字是房间成员当前看到的状态，悬停问号显示识别方式；切换到另一类应用后标题行右侧出现“n 秒后变为”的倒计时，进度条贴卡片底边，下方内容不上下跳动；归零后大字换成新状态，模拟成员同时收到 | 全部 |
| 纠正分类 | 保存后只有 alice 自己的连接收到 `rules.personalChanged`（模拟成员收不到），设置目录的 `personal-rules.json` 更新，「分类」页的「我的分类」出现这一条；切换房间后仍然生效 | 全部 |
| 模拟成员收到 alice 的状态 | 首条状态不是空的 `Other`；10 秒左右收到稳定后的状态 | 全部 |
| 勾选“暂停分享” | 模拟成员立即收到 `Paused` | 全部 |
| 房间页屏蔽 Bob | Bob 只收到一条 alice 的 `Offline`，之后收不到 alice 的状态和房间事件；Carol 照常收到；alice 的状态页里 Bob 显示「已屏蔽」。取消屏蔽后 Bob 立即收到 alice 的最新状态 | 全部（置顶卡片关闭只在 Windows、macOS） |
| 标题栏房间菜单选「无房间」 | 模拟成员立即收到 `Offline`，之后不再收到 alice 的状态；成员区提示「当前无房间。请创建或选择一个房间，也可以用邀请码加入。」；再选回该房间后收到当前状态 | 全部 |
| 点关闭，选「最小化到系统托盘」；5 分钟后 | WebView2 进程全部退出（下面的命令计数为 0）；再从托盘打开能正常显示 | Windows |

```powershell
Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object CommandLine -match 'DeskPal\\WebView2' | Measure-Object
```

改了采集就测采集。例如改了浏览器网站：打开 B 站，看“本机识别”是不是“正在浏览网页”，下面是域名和页面标题（Linux 要先装 DeskPal 浏览器扩展）。

做不了 B 的平台，把要看的检查项写进交付的「需要人看的」，注明平台。

## 6. 清理并恢复

1. 结束客户端、Server 和模拟成员进程。按进程名或记下的进程号结束，不要用会匹配到自己这条命令的模式（例如 `pkill -f DeskPal.Server` 会把执行它的 shell 一起结束）。
2. 做过 B 的：`$H restore-settings`，删除测试设置并把备份移回去。Windows 上第 4 步时 `%LOCALAPPDATA%\DeskPal\WebView2` 不存在的话，也删掉它。告诉用户可以重新打开自己的 DeskPal。
3. 删除 `src/DeskPal.Server/storage/`（测试账号都在这个数据库里）。路径要写全，不要写成 `data`：Windows 上它和源码目录 `Data` 是同一个目录。
4. 删除 `.local/tmp/e2e/`。Windows 上先把终端的工作目录切回仓库根目录，否则会提示目录正在使用。
