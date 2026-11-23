---
name: deskpal-write-provider
description: 给 DeskPal 的 Linux 客户端写一个识别插件（provider），让它在内置识别不支持的桌面环境（Hyprland、Sway、KDE Wayland、niri 等）或特殊软件上得知“当前在用哪个窗口”。用户说 DeskPal 在 Linux 上识别不到应用、显示“需要识别插件”、想支持某个窗口管理器、想让浏览器网址参与分类，或者要改 provider 协议本身时使用。
---

# 写一个 DeskPal 识别插件

识别插件是一个**独立的可执行文件**（脚本或任何语言编译的程序）。DeskPal 启动它，读它的标准输出；它每行输出一个 JSON 对象，说明当前窗口是什么。只有 Linux 客户端支持（Windows、macOS 暂未开放，见 `docs/plan-2026-11-23-linux-providers.md`）。

面向用户的说明在 `docs/provider-guide.md`。协议的权威实现是 `src/DeskPal.Client.Core/Providers/ProviderProtocol.cs`；本文、指南、`scripts/check_provider.py` 和它必须一致。

## 先确认需不需要写

| 环境 | 结论 |
| --- | --- |
| X11 会话（任何桌面） | 内置识别可用，不需要插件 |
| GNOME + Wayland | 内置识别可用，在「设置 → 应用识别」里安装 DeskPal 的 GNOME 扩展并重新登录 |
| 其他 Wayland 桌面 | 需要插件 |
| 想让浏览器网址参与分类 | 需要插件（内置识别在 Linux 上不读网址） |

让用户运行 `echo $XDG_SESSION_TYPE $XDG_CURRENT_DESKTOP` 确认环境。不要凭猜测写：先让用户在终端里跑一遍你打算用的查询命令，把真实输出贴回来。

## 协议 v1

标准输出，UTF-8，一行一个 JSON 对象。

```json
{"v":1,"type":"hello","name":"my-provider","version":"0.1"}
{"v":1,"type":"foreground","process":"firefox","name":"Firefox","title":"Inbox — Mozilla Firefox","fullscreen":false,"path":"/usr/lib/firefox/firefox"}
{"v":1,"type":"domain","domain":"github.com"}
```

| type | 字段 | 说明 |
| --- | --- | --- |
| `foreground` | `process`（必填） | 规则匹配用的程序名。给完整路径或带 `.exe` 也行，DeskPal 只取文件名 |
| | `name` | 给人看的应用名，缺省用 `process` |
| | `title` | 窗口标题，缺省为空 |
| | `fullscreen` | `true` / `false`，缺省 `false` |
| | `path` | 程序完整路径，用来认出 Steam 库等目录里的游戏 |
| `domain` | `domain` | 前台浏览器当前页面的域名；读不到给 `null`。只在 `process` 是浏览器时有用 |
| `hello` | `name`（必填）、`version` | 可选的第一行，显示在设置页和日志里 |

硬性规则：

- `v` 可以省略；写了就必须是 `1`。不认识的 `type` 会被忽略（留给以后的版本）。
- 一行最长 8192 字符；`title` 超过 512 字符会被截断；每秒超过 50 行，多出的被丢弃。
- **窗口没变化就不要重复输出**。轮询间隔 1 秒左右即可；能订阅事件更好。
- **每行写完立刻 flush**（Python `flush=True`，C `fflush`，Node 直接 `console.log`）。
- 标准输出只能有协议行。调试信息写到**标准错误**，它会进 DeskPal 的日志（`~/.local/state/DeskPal/logs`）。
- 插件要一直运行。退出后 DeskPal 按 1、2、5、10、30、60 秒的间隔重启它。DeskPal 退出或换插件时会结束插件及其子进程。
- 没有窗口获得焦点时不输出（保持上一个状态）。
- 环境变量 `DESKPAL_PROTOCOL` 是 DeskPal 支持的协议版本；工作目录是插件所在目录。

## `process` 怎么取（最容易错）

分类规则按程序文件名匹配，和 Windows 版共用一套：`code`、`firefox`、`chrome`、`msedge`、`steam`、`obs`…… 所以：

1. 首选窗口所属进程的可执行文件名：`basename "$(readlink /proc/$PID/exe)"`。
2. 可执行文件只是运行时（`electron`、`python3`、`java`、`wine`、`bwrap`）时，改用窗口的应用标识（Wayland `app_id` / X11 `WM_CLASS`）。
3. 拿不到进程号时，用应用标识。

`name` 用应用标识或 `.desktop` 里的名字即可。

## 隐私边界

插件只陈述“当前窗口是什么”。分类、不记录标题的规则、隐私过滤都在 DeskPal 里、插件输出之后执行，插件绕不过去。写插件时同样遵守 DeskPal 的硬性约束：**不截屏、不读按键、不读窗口里的内容、不联网**；读浏览器网址只取域名，不取完整地址。

## 步骤

1. 从 `scripts/template.sh` 或 `scripts/template.py` 复制一份；现成例子在 `examples/`（`x11-xdotool.sh` 已验证；`hyprland.sh`、`sway.sh` 按文档写成、未在真机验证，先让用户验证）。
2. 只改“读取当前窗口”那一段。Shell 里用 `jq` 生成 JSON，不要手拼字符串（标题里有引号、反斜杠会坏）。
3. 检查：`python3 scripts/check_provider.py <插件文件>`。它运行插件 8 秒，按 DeskPal 的规则逐行检查，期间让用户切换几个窗口。
4. 安装：放进 `~/.config/DeskPal/providers/`，`chmod +x`。文件名就是设置里显示的名字；以 `.` 开头或没有可执行权限的文件不会列出。
5. 在 DeskPal「设置 → 应用识别 → 识别方式」里选它。状态行显示“识别插件运行正常”即可；失败原因也显示在那里。

## 常见桌面的查询方式

| 桌面 | 命令 | 备注 |
| --- | --- | --- |
| Hyprland | `hyprctl activewindow -j` | 有 `class`、`title`、`pid`、`fullscreen`；事件在 `$XDG_RUNTIME_DIR/hypr/$HYPRLAND_INSTANCE_SIGNATURE/.socket2.sock` |
| Sway | `swaymsg -t get_tree`，取 `focused == true` 的节点 | 有 `app_id`（XWayland 窗口是 `window_properties.class`）、`name`、`pid`、`fullscreen_mode`；事件用 `swaymsg -m -t subscribe '["window"]'` |
| niri | `niri msg --json focused-window` | 有 `app_id`、`title`、`pid` |
| KDE Plasma (Wayland) | KWin 脚本或 `kdotool getactivewindow` | 没有稳定的命令行接口，需要用户确认可用的工具 |
| X11 | `xdotool getactivewindow`、`xprop` | 内置已支持 |

以上除 X11 外都没有在本项目里实测过，以用户机器上的真实输出为准。

## 改协议本身时

新增字段或消息类型要同时改：`ProviderProtocol.cs`（解析和上限）、`tests/DeskPal.Tests/Providers/ProviderProtocolTests.cs`、`scripts/check_provider.py`、本文和 `docs/provider-guide.md`。旧插件必须继续可用：只加可选字段；不兼容的改动才提高 `v`，并让 DeskPal 同时接受旧版本。
