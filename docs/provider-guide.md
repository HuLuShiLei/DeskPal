# 插件编写指南

DeskPal 显示你的状态，要先知道五件事：你在用哪个窗口、在看哪个网站、是不是暂离了、屏幕锁了没有、正在播放什么。这些信息都由**插件**提供。

每个平台都自带一个官方插件（Windows 内置、macOS 内置、Linux 内置），装好就能用。你也可以自己写插件，放进插件文件夹，替换或补充官方插件提供的某几项，或者让某个软件显示更具体的内容，例如 QQ 正在看的会话（见「只补充某个软件的标题」）。

让 AI 帮你写：把仓库里的 `.cursor/skills/deskpal-write-provider/` 整个目录给它（Cursor 会自动识别；其他工具把 `SKILL.md` 的内容贴给它），再说明你的系统、桌面环境和想要的效果。

## 插件列表怎么起作用

「设置 → 常规 → 插件」打开插件页。列表像游戏的 mod 列表：官方插件和你自己的插件排在一起，**越靠上越优先**。

五项信息分开算：每一项都取**最上面一个开启的、提供这一项的**插件。每个插件折叠成两行，第二行是它正在提供的几项；点开后的标签是它提供的全部几项，高亮的是正在使用的，灰色的是被上面的插件盖住了。需要处理的插件（没有正常运行、有提示）显示黄色的感叹号，并且一开始就是展开的。

举个例子，Linux 上用 Hyprland（内置插件读不到它的窗口）：

```
Hyprland 窗口   [当前窗口]                     ← 你写的，只提供当前窗口
Linux 内置      [当前窗口] [网站] [暂离] [锁屏] [媒体]
```

结果是：当前窗口来自你的插件；网站、暂离、锁屏、媒体仍然来自 Linux 内置。你只需要写自己环境里缺的那一项。

其他规则：

- 你放进文件夹的插件默认**关闭**，打开开关时 DeskPal 会再确认一次。官方插件默认开启，也可以关掉。
- 某个插件出错或退出时，它提供的几项自动交给下面的插件，等它恢复后再拿回来。
- 某一项没有任何插件提供时，列表顶部会提示。例如没人提供“当前窗口”，房间里只会显示在线、暂离等。

## 插件文件夹

| 系统 | 位置 | 能当插件的文件 |
| --- | --- | --- |
| Windows | `%APPDATA%\DeskPal\plugins\` | `.exe`、`.cmd`、`.bat`、`.ps1`、`.py`（需要装好 Python，命令行能直接运行 `python`） |
| macOS | `~/Library/Application Support/DeskPal/plugins/` | 任何有可执行权限的文件（`chmod +x`） |
| Linux | `~/.config/DeskPal/plugins/` | 任何有可执行权限的文件（`chmod +x`） |

插件页上的「打开插件文件夹」按钮直接打开这个目录。放进新文件后点右上角的刷新按钮（或等十秒）。只看文件夹本身，不看子目录；以 `.` 开头的文件不算。

## 五分钟上手

Linux 或 macOS，新建 `plugins/hello.sh`：

```sh
#!/bin/sh
echo '{"v":1,"type":"hello","name":"hello","version":"0.1","provides":["foreground"]}'
while :; do
  echo '{"v":1,"type":"foreground","process":"code","name":"VS Code","title":"main.rs"}'
  sleep 5
done
```

然后 `chmod +x hello.sh`，在插件页点刷新按钮，打开 hello 的开关，用上箭头把它移到内置插件上面。首页会显示你在用 VS Code。

Windows，新建 `plugins\hello.cmd`：

```bat
@echo off
echo {"v":1,"type":"hello","name":"hello","version":"0.1","provides":["foreground"]}
:loop
echo {"v":1,"type":"foreground","process":"code","name":"VS Code","title":"main.rs"}
rem 等 5 秒。不能用 timeout 命令：DeskPal 运行插件时没有键盘输入，timeout 会立即返回。
ping -n 6 127.0.0.1 >nul
goto loop
```

这个插件说的是假话，但它已经是一个完整的插件：**一直运行，每行往标准输出写一个 JSON 对象**。接下来只要把假数据换成真的查询。

## 插件要输出什么

每行一个 JSON 对象，`type` 说明是哪种消息。

### hello：我是谁、提供哪几项

```json
{"v":1,"type":"hello","name":"Hyprland 窗口","version":"0.1","provides":["foreground"]}
```

放在第一行。`name` 和 `version` 显示在设置的插件列表里。`provides` 列出你提供的几项：`foreground`（当前窗口）、`domain`（网站）、`idle`（暂离）、`lock`（锁屏）、`media`（媒体），以及 `detail`（补充标题，见下文，要同时写 `apps`）。

不写 `provides` 时按 `["foreground", "domain"]` 处理。**没写在 `provides` 里的消息，DeskPal 不会采用。**

### foreground：当前窗口

```json
{"v":1,"type":"foreground","process":"firefox","name":"Firefox","title":"收件箱 — Mozilla Firefox","fullscreen":false,"path":"/usr/lib/firefox/firefox"}
```

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `process` | 是 | 程序名，分类规则靠它匹配。写完整路径或带 `.exe` 也可以，DeskPal 只取文件名 |
| `name` | 否 | 显示给人看的应用名，不写就显示 `process` |
| `title` | 否 | 窗口标题 |
| `fullscreen` | 否 | 是否全屏，`true` 或 `false` |
| `path` | 否 | 程序的完整路径，用来认出游戏库目录里的游戏 |

窗口没变就不要重复输出。

**`process` 写什么**：默认分类按程序文件名认应用，例如 `code`、`firefox`、`chrome`、`msedge`、`steam`。最稳妥的是取窗口所属进程的可执行文件名，例如 Linux 上 `basename "$(readlink /proc/$PID/exe)"`。如果得到的是 `electron`、`python3`、`java`、`wine` 这类运行时的名字，改用窗口的应用标识（Wayland 的 `app_id`、X11 的窗口类名），否则所有 Electron 应用都会被当成同一个。

### domain：网站

```json
{"v":1,"type":"domain","domain":"github.com"}
```

当前窗口是浏览器时，正在看的网站的域名。**只给域名，不给完整网址。**读不到时写 `"domain":null`。

每次浏览器窗口或标题变化后，都要再输出一次 `domain`（哪怕和上次一样）：DeskPal 会等这一行来确认网站。

### idle：暂离

```json
{"v":1,"type":"idle","seconds":42}
```

距离上次键盘或鼠标输入过了多少秒。**每隔几秒输出一次**（5 秒左右合适）。算不算暂离由 DeskPal 按用户设置的时间判断，插件不用管。插件超过两分钟不报，就不再算暂离。

### lock：锁屏

```json
{"v":1,"type":"lock","locked":true}
```

锁屏和解锁时各输出一次。

### media：正在播放

```json
{"v":1,"type":"media","playing":true,"title":"Blue in Green","artist":"Miles Davis","source":"spotify"}
```

`source` 是播放器的程序名（和 `process` 一样的写法）。停止或暂停时输出 `{"v":1,"type":"media","playing":false}`。

### detail：补充标题

```json
{"v":1,"type":"detail","process":"QQ","window":"QQ","title":"张三"}
```

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `process` | 是 | 这个标题属于哪个程序，写法和 `foreground` 的 `process` 一样 |
| `window` | 否 | 你看到的窗口标题。写了的话，只有当前窗口的标题和它一样时才生效；不写则这个程序的所有窗口都用 |
| `title` | 是 | 用来代替窗口标题的内容。写 `null` 表示收回 |

详见下面的「只补充某个软件的标题」。

## 规则

- 一行一个 JSON 对象，UTF-8；一行最长 8192 个字符，标题超过 512 个字符会被截断。`v` 可以不写，写了必须是 `1`。
- 每秒超过 50 行，多出的会被丢掉。没有变化时不要重复输出（`idle` 除外）。
- 每写一行就刷新输出缓冲（Python 用 `print(..., flush=True)`）。不刷新的话 DeskPal 要很久才收得到。
- 标准输出里只能有协议行。调试信息写到标准错误，它会出现在 DeskPal 的日志里（设置 → 日志文件）；插件退出时，最后一行错误显示在插件列表里。
- 插件要一直运行。它退出后 DeskPal 会隔 1 到 60 秒重新启动它。
- 用 `jq`、`json.dumps`、`ConvertTo-Json` 之类的工具生成 JSON，不要自己拼字符串：标题里出现引号就会出错。
- 不认识的 `type` 会被忽略，留给以后的版本。
- Windows 的 `.ps1` 要存成**带 BOM 的 UTF-8**。系统自带的 Windows PowerShell 5.1 把不带 BOM 的文件当作本地编码读，里面有中文就会出错。

## 只补充某个软件的标题

有些软件的窗口标题里没有你想显示的内容。例如 QQ 的主窗口标题一直是“QQ”，房间成员只能看到“在聊天 · QQ”。补充标题的插件可以在这个软件处于前台时，把窗口标题换成更具体的内容，例如正在看的会话名。

```json
{"v":1,"type":"hello","name":"QQ 会话","version":"0.1","provides":["detail"],"apps":["QQ"]}
{"v":1,"type":"detail","process":"QQ","window":"QQ","title":"张三"}
```

- `apps` 列出你管的程序，写法和 `process` 一样，不区分大小写。只有这些程序在前台时，你补充的标题才会生效。
- 当前窗口仍由官方插件（或你放在上面的其他插件）提供，你只提供标题。应用名和分类都不变：QQ 还是“在聊天”。
- 每次窗口或你读到的内容变化时报一次。DeskPal 会记住你最后报的值：切到别的程序再切回来，不用重报。
- 写上 `window`（你看到的窗口标题），这样同一程序的其他窗口（例如单独弹出的聊天窗口）不会误用这个标题。
- 浏览器不能补充标题。浏览器的标题本身就是网页标题。
- 补充的标题和普通窗口标题一样处理：受“不记录标题”和房间的隐私设置控制，只有把“在聊天”设为显示详情的房间才能看到。
- 这类插件不用移到官方插件上面，放在列表任何位置都可以。多个插件都给同一个程序补充时，用最上面的那个。

现成的样例是 `examples/windows-qq.ps1`（Windows，QQ NT）：它通过 Windows 的 UI Automation（系统的无障碍接口）只读会话顶部的名称，不读消息内容，不注入 QQ，也不读 QQ 的数据文件。QQ 改版后界面结构可能变化，读不到时它什么都不报，DeskPal 显示原来的窗口标题。

## 插件做不到的事

插件只负责陈述事实。之后的分类、你设置的“不记录标题”、隐私过滤，都由 DeskPal 照常执行，插件改变不了，也看不到别人的状态。

DeskPal 自己不截屏、不读按键内容、不读窗口里的内容。你的插件读什么由你自己决定，建议也守住这几条：只读需要的那一项信息，不联网。插件是以你的用户身份运行的普通程序：只运行你看得懂、信得过的插件。

## Linux：浏览器网站

Linux 上没有可靠的办法从浏览器外面读到网址，所以 Linux 内置插件配了一个 **DeskPal 浏览器扩展**：它把当前标签页的域名告诉本机的 DeskPal。只发域名，只发给本机（`127.0.0.1`），不读无痕窗口。

安装：在「设置 → 插件 → Linux 内置」里点“获取浏览器扩展”，扩展会放到 `~/.local/share/DeskPal/browser-extension/` 并打开这个文件夹。

- **Chrome、Edge、Brave、Vivaldi、Opera**：打开扩展管理页（例如 `edge://extensions`、`chrome://extensions`），打开“开发者模式”，点“加载已解压的扩展程序”，选中这个文件夹。
- **Firefox**：正式版 Firefox 只能长期安装经过 Mozilla 签名的扩展。临时试用：打开 `about:debugging#/runtime/this-firefox`，点“临时载入附加组件”，选中文件夹里的 `manifest.json`，重启 Firefox 后要重新载入。Firefox ESR、开发者版和 Nightly 可以在 `about:config` 里把 `xpinstall.signatures.required` 设为 `false` 后长期安装。

装好后，插件列表里会显示“浏览器扩展已连接”。

**自己的插件也能用这个扩展。**扩展只和 Linux 内置插件说话。如果你写的插件只提供当前窗口，把它放在 Linux 内置上面、保持 Linux 内置开启就行：网站仍由 Linux 内置通过扩展提供，并且会按你的插件报告的前台浏览器来选。

想完全自己做（例如用别的浏览器扩展、别的通信方式），在你的插件里输出 `domain` 消息即可。DeskPal 不关心数据从哪来。

## 模板、例子和检查工具

都在 `.cursor/skills/deskpal-write-provider/` 目录：

| 文件 | 用途 |
| --- | --- |
| `scripts/template.sh` | bash 模板（Linux、macOS），只需要填“读取当前窗口”那一段（需要 `jq`） |
| `scripts/template.py` | Python 模板，只用标准库，三个平台都能用 |
| `scripts/template.ps1` | Windows 模板，用系统自带的 PowerShell，读前台窗口和暂离时间，已在 Windows 上验证 |
| `scripts/check_provider.py` | 检查工具 |
| `examples/x11-xdotool.sh` | X11 + xdotool，已验证 |
| `examples/windows-qq.ps1` | Windows，补充 QQ NT 的当前会话名，已在 QQ NT 9.9.37 上验证 |
| `examples/hyprland.sh` | Hyprland，按文档编写，未在真机验证 |
| `examples/sway.sh` | Sway，按文档编写，未在真机验证 |
| `examples/macos-frontmost.sh` | macOS，用 osascript，未在真机验证 |

写完先检查再开启：

```sh
python3 check_provider.py ./my-plugin.sh
```

它会运行你的插件 8 秒（这期间切换几个窗口），按 DeskPal 的规则逐行检查并指出问题，例如：

```
  正常 hello  my-plugin 0.1  provides=['foreground', 'idle']
  正常 foreground  process=firefox  name='Firefox'  title='Inbox'
  正常 idle  3.2 秒
  错误 不是 JSON（调试信息请写到标准错误，不要写到标准输出）

共 4 行，1 行错误，0 行需要注意。
结论：有问题，见上。
```

## 出问题时

| 现象 | 原因 |
| --- | --- |
| 列表里没有我的插件 | 不在插件文件夹里（不看子目录）；Linux、macOS 上没有可执行权限；Windows 上不是上面那几种文件类型。放好后点刷新按钮 |
| 一直显示“正在启动” | 插件还没输出过任何有效的行，或者没有刷新输出缓冲 |
| 显示“没有正常运行” | 插件退出了或启动失败，原因写在下面一行；详细内容看日志 |
| 插件在运行，但标签是灰的 | 上面有别的插件提供了同一项。用箭头把你的插件移上去 |
| 某一项没变化 | 这一项没写进 hello 的 `provides`，DeskPal 不采用 |
| 应用被归到“其他活动” | `process` 不是规则认识的名字。可以在 DeskPal 里给它设置分类，或换成可执行文件名 |
| 补充的标题没有显示 | hello 里没写 `apps`，或 `apps`、`process` 和前台程序的名字不一致；写了 `window` 但和当前窗口标题不一样；前台是浏览器。房间成员那边还要把“在聊天”等类别设为显示详情 |
| 想换回原样 | 关掉自己的插件，或把官方插件移回最上面 |
