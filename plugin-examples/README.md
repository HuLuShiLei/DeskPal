# 插件示例

这里是 DeskPal 插件的代码示例，用来参考怎么写，**不保证在你的电脑上直接能用**。插件读的是别的软件和系统的界面结构：窗口查询命令、界面元素的名称、进程名都会随软件版本、系统版本、桌面环境变化。实测下来，同一个插件在不同电脑上经常读不到，所以这里只提供写法，不作为现成插件发布。

想用其中某个功能时，照着它的写法在自己电脑上改：先在终端里直接运行，确认输出正确，再用检查工具检查，最后放进插件文件夹开启。步骤见 [docs/provider-guide.md](../docs/provider-guide.md)。也可以把 `.claude/skills/deskpal-write-provider/` 和这个文件夹交给 AI，让它按你的电脑改。

| 文件 | 系统 | 作用 | 写的时候验证过的环境 |
| --- | --- | --- | --- |
| `windows-qq.ps1` | Windows | 只提供 `detail`：QQ 在前台时，显示当前会话名（好友或群） | QQ NT 9.9.37 主窗口 |
| `windows-wechat.ps1` | Windows | 只提供 `detail`：微信在前台时，显示当前会话名（好友或群） | 微信 4.2.0.53 主窗口。单独弹出的聊天窗口未验证；旧版微信 3.x 不支持 |
| `windows-steam.ps1` | Windows | 只提供 `detail`：显示 Steam 游戏的游戏内状态（好友列表里显示的那行） | 杀戮尖塔 2。要开启 Steam 的调试接口，见文件开头的说明 |
| `linux-qq.py` | Linux | 只提供 `detail`：QQ 在前台时，显示当前会话名，主窗口和单独的聊天窗口都按当前窗口读 | Ubuntu 26.04（GNOME Wayland）：切换会话时读到会话名，切到没有会话名的窗口时报 null |
| `room-digest.py` | 三个平台 | 只读不报：读取当前房间的成员和状态（`reads:["room"]`），把摘要写到 DeskPal 的日志。拿不到任何人的标题、应用名和网站 | Linux 上用检查工具和单元测试里的真实插件进程验证过；在真实房间里的效果待端到端自检 |
| `x11-xdotool.sh` | Linux | 用 xdotool 读 X11 的当前窗口 | X11 |
| `hyprland.sh` | Linux | 读 Hyprland 的当前窗口 | 按文档编写，未在真机验证 |
| `sway.sh` | Linux | 读 Sway 的当前窗口 | 按文档编写，未在真机验证 |
| `macos-frontmost.sh` | macOS | 用 osascript 读当前窗口 | 按文档编写，未在真机验证 |

只提供 `detail`（补充标题）的插件放在插件列表的任何位置都可以，当前窗口、网站、暂离等仍由内置插件提供。应用和分类不变：QQ、微信还是“在聊天”。想让某个群算工作，在首页「纠正分类」里按窗口标题设置。

聊天软件的几个示例怎么读：通过系统的无障碍接口（Windows 的 UI Automation、Linux 的 AT-SPI）只读会话顶部的名称这一个元素，不读消息内容，不注入，也不读软件的数据文件。软件改版后界面结构可能变化，读不到时插件什么都不报，DeskPal 显示原来的窗口标题。在自己电脑上改的时候，先列一遍目标窗口的界面元素，找到会话名对应的那个元素，再改脚本里的 AutomationId 或 class 名。

补充的标题和普通窗口标题一样，受「不记录标题」和房间隐私设置控制，只有把“在聊天”设为显示详情的房间才能看到。

## Linux 的 QQ 示例

`linux-qq.py` 用 Python 3 和 AT-SPI（Ubuntu 桌面自带 `python3-gi`；Arch 装 `python-gobject` 和 `at-spi2-core`）。QQ 要打开辅助功能才读得到，GNOME 上运行一次 `gsettings set org.gnome.desktop.interface toolkit-accessibility true`，再完全退出 QQ（包括托盘图标）重新打开。`python3` 要是系统自带的那个（conda 等环境里的 Python 通常没有 `gi`）。在终端里直接运行 `./linux-qq.py`，切到 QQ 的聊天，应当每换一个会话出现一行 `detail`。

它按 QQ 当前激活的窗口读，所以主窗口和单独弹出的聊天窗口都可以；切到图片查看器等没有会话名的窗口时报 null，DeskPal 显示原来的窗口标题。

## 编码

放进这个文件夹的 `.ps1` 要存成带 BOM 的 UTF-8，否则 Windows PowerShell 5.1 读错中文。`.sh`、`.py` 固定用 LF（见 `.gitattributes`），复制到 Linux、macOS 后要 `chmod +x`。