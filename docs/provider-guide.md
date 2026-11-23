# 识别插件编写指南（Linux）

DeskPal 需要知道“你现在在用哪个窗口”才能显示状态。Linux 的桌面环境很多，各自的查询方式不同，所以这部分做成了可以替换的**识别插件**（provider）：一个你自己写的小程序，告诉 DeskPal 当前窗口是什么。

识别插件目前只在 Linux 客户端开放。

让 AI 帮你写：把仓库里的 `.cursor/skills/deskpal-write-provider/` 整个目录给它（Cursor 会自动识别；其他工具把 `SKILL.md` 的内容贴给它即可），再说明你的桌面环境。

## 你需要写插件吗

在终端里运行：

```sh
echo $XDG_SESSION_TYPE $XDG_CURRENT_DESKTOP
```

| 结果 | 怎么办 |
| --- | --- |
| `x11 …` | 不需要。内置识别直接可用 |
| `wayland …GNOME` | 不需要。打开 DeskPal「设置 → 应用识别」，点“安装扩展”，然后注销并重新登录 |
| `wayland` 加其他桌面（Hyprland、Sway、KDE、niri…） | 需要一个插件 |

另外，内置识别在 Linux 上不读浏览器网址（浏览器只显示“浏览中”和标题）。想按网站分类，也可以写插件提供域名。

DeskPal「设置 → 应用识别」里的状态行会告诉你现在用的是哪种方式、是否正常。

## 五分钟上手

1. 新建文件 `~/.config/DeskPal/providers/hello.sh`（“设置 → 应用识别 → 插件文件夹”可以直接打开这个目录）：

   ```sh
   #!/bin/sh
   echo '{"v":1,"type":"hello","name":"hello","version":"0.1"}'
   while :; do
     echo '{"v":1,"type":"foreground","process":"code","name":"VS Code","title":"main.rs"}'
     sleep 5
   done
   ```

2. 给它可执行权限：`chmod +x ~/.config/DeskPal/providers/hello.sh`
3. 在 DeskPal「设置 → 应用识别 → 识别方式」里选 `hello.sh`。

首页会显示你在用 VS Code。这个插件说的是假话，但它已经是一个完整的插件：**一直运行，每行往标准输出写一个 JSON 对象**。接下来只要把假数据换成真的查询。

## 插件要输出什么

```json
{"v":1,"type":"foreground","process":"firefox","name":"Firefox","title":"收件箱 — Mozilla Firefox","fullscreen":false,"path":"/usr/lib/firefox/firefox"}
```

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `type` | 是 | `foreground`：当前窗口 |
| `process` | 是 | 程序名，分类规则靠它匹配。写完整路径也可以，DeskPal 只取文件名 |
| `name` | 否 | 显示给人看的应用名，不写就显示 `process` |
| `title` | 否 | 窗口标题 |
| `fullscreen` | 否 | 是否全屏，`true` 或 `false` |
| `path` | 否 | 程序的完整路径，用来认出游戏库目录里的游戏 |
| `v` | 否 | 协议版本，目前只有 `1` |

还有两种可选的行：

```json
{"v":1,"type":"hello","name":"my-provider","version":"0.1"}
{"v":1,"type":"domain","domain":"github.com"}
```

- `hello`：插件的名字和版本，显示在设置页，方便确认运行的是哪一个。放在第一行。
- `domain`：当前窗口是浏览器时，正在看的网站域名（只要域名，不要完整网址）。读不到时写 `"domain":null`。

### `process` 写什么

DeskPal 的默认分类按程序文件名认应用，例如 `code`、`firefox`、`chrome`、`steam`。最稳妥的做法是取窗口所属进程的可执行文件名：

```sh
basename "$(readlink /proc/$PID/exe)"
```

如果得到的是 `electron`、`python3`、`java`、`wine` 这类运行时的名字，改用窗口的应用标识（Wayland 的 `app_id` 或 X11 的窗口类名），否则所有 Electron 应用都会被当成同一个。

## 规则

- 一行一个 JSON 对象，UTF-8；一行最长 8192 个字符，标题超过 512 个字符会被截断。
- 窗口没变就不要重复输出。每秒超过 50 行，多出的会被丢掉。
- 每写一行就刷新输出缓冲（Python 用 `print(..., flush=True)`）。不刷新的话 DeskPal 要很久才收得到。
- 标准输出里只能有上面这些行。调试信息写到标准错误，它会出现在 DeskPal 的日志里（设置 → 关于 → 日志文件）。
- 插件要一直运行。它退出后 DeskPal 会隔 1 到 60 秒重新启动它。
- 没有窗口获得焦点时什么都不用输出。
- 用 `jq`、`json.dumps` 之类的工具生成 JSON，不要自己拼字符串：标题里出现引号就会出错。
- 文件名以 `.` 开头，或者没有可执行权限的文件，不会出现在列表里。

## 插件做不到的事

插件只负责说出“当前窗口是什么”。之后的分类、你设置的“不记录标题”、隐私过滤，都由 DeskPal 照常执行，插件改变不了，也看不到别人的状态。

DeskPal 自己不截屏、不读按键、不读窗口里的内容。请让你的插件也守住这几条，并且不要联网。插件是以你的用户身份运行的普通程序：只运行你看得懂、信得过的插件。

## 模板、例子和检查工具

都在仓库的 `.cursor/skills/deskpal-write-provider/` 目录：

| 文件 | 用途 |
| --- | --- |
| `scripts/template.sh` | bash 模板，只需要填“读取当前窗口”那一段（需要 `jq`） |
| `scripts/template.py` | Python 模板，只用标准库 |
| `scripts/check_provider.py` | 检查工具 |
| `examples/x11-xdotool.sh` | X11 + xdotool，已验证 |
| `examples/hyprland.sh` | Hyprland，按文档编写，未在真机验证 |
| `examples/sway.sh` | Sway，按文档编写，未在真机验证 |

写完先检查再安装：

```sh
python3 check_provider.py ./my-provider.sh
```

它会运行你的插件 8 秒（这期间切换几个窗口），按 DeskPal 的规则逐行检查，并指出问题，例如：

```
  正常 hello  my-provider 0.1
  正常 foreground  process=firefox  name='firefox'  title='Inbox'
  错误 不是 JSON（调试信息请写到标准错误，不要写到标准输出）

共 3 行，1 行错误，0 行需要注意。
结论：有问题，见上。
```

## 出问题时

| 现象 | 原因 |
| --- | --- |
| 列表里没有我的插件 | 没有可执行权限，或没放在 `~/.config/DeskPal/providers/` 下（不看子目录） |
| 一直显示“正在启动” | 插件还没输出过 `foreground` 行，或者没有刷新输出缓冲 |
| 显示“没有正常运行” | 插件退出了或启动失败，原因写在同一行；详细内容看日志 |
| 应用被归到“其他活动” | `process` 不是规则认识的名字。可以在 DeskPal 里给它设置分类，或换成可执行文件名 |
| 换回内置识别 | 识别方式选“内置” |
