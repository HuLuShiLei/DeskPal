# 现成的插件

可以直接用的 DeskPal 插件。用法：把文件复制到插件文件夹（Windows 是 `%APPDATA%\DeskPal\plugins\`，Linux 是 `~/.config/DeskPal/plugins/`，并 `chmod +x`），在 DeskPal「设置 → 插件」里点「刷新」并开启它。

| 文件 | 系统 | 作用 | 验证情况 |
| --- | --- | --- | --- |
| `windows-qq.ps1` | Windows | QQ 在前台时，显示当前会话名（好友或群） | QQ NT 9.9.37 主窗口 |
| `windows-wechat.ps1` | Windows | 微信在前台时，显示当前会话名（好友或群） | 微信 4.2.0.53 主窗口。单独弹出的聊天窗口未验证；旧版微信 3.x 不支持 |
| `linux-qq.py` | Linux | QQ 在前台时，显示当前会话名（好友或群），主窗口和单独的聊天窗口都按当前窗口读 | 按 Ubuntu 26.04（GNOME Wayland）上 QQ 的探测结果写成，只在沙箱里用 Chromium 模拟同样结构的页面验证过，未在真机运行 |

这些插件都只提供「补充标题」（`detail`），放在插件列表的任何位置都可以，当前窗口、网站、暂离等仍由内置插件提供。应用和分类不变：QQ、微信还是“在聊天”。想让某个群算工作，在首页「纠正分类」里按窗口标题设置。

它们怎么读：通过系统的无障碍接口（Windows 的 UI Automation、Linux 的 AT-SPI）只读会话顶部的名称这一个元素，不读消息内容，不注入，也不读软件的数据文件。软件改版后界面结构可能变化，读不到时插件什么都不报，DeskPal 显示原来的窗口标题。

补充的标题和普通窗口标题一样，受「不记录标题」和房间隐私设置控制，只有把“在聊天”设为显示详情的房间才能看到。

## Linux 的 QQ 插件

`linux-qq.py` 用 Python 3 和 AT-SPI（Ubuntu 桌面自带 `python3-gi`；Arch 装 `python-gobject` 和 `at-spi2-core`）。QQ 要打开辅助功能才读得到，GNOME 上运行一次 `gsettings set org.gnome.desktop.interface toolkit-accessibility true`，再完全退出 QQ（包括托盘图标）重新打开。`python3` 要是系统自带的那个（conda 等环境里的 Python 通常没有 `gi`）。在终端里直接运行 `./linux-qq.py`，切到 QQ 的聊天，应当每换一个会话出现一行 `detail`。

它按 QQ 当前激活的窗口读，所以主窗口和单独弹出的聊天窗口都可以；切到图片查看器等没有会话名的窗口时报 null，DeskPal 显示原来的窗口标题。

Linux 的微信 4.x（Qt 界面）在探测时没有出现在辅助功能里，要先按上面的办法打开辅助功能（或用 `QT_LINUX_ACCESSIBILITY_ALWAYS_ON=1 /opt/wechat/wechat` 启动）再探测一次，才能知道能不能做。

## macOS 和 Linux 的探测

macOS（辅助功能接口）和 Linux（AT-SPI）上能否做同样的插件，要先看 QQ、微信在那边公开了什么界面结构。`probes/` 里有两个探测脚本，请有 Mac 或 Linux 的人运行一次，把输出发回来：

| 文件 | 系统 | 用法 |
| --- | --- | --- |
| `probes/probe-linux.py` | Linux | `python3 probe-linux.py > probe.txt` |
| `probes/probe-macos.sh` | macOS | `sh probe-macos.sh > probe.txt` |
| `probes/probe-windows.ps1` | Windows | `powershell -ExecutionPolicy Bypass -File probe-windows.ps1 > probe.txt`。插件在某个版本上不起作用时用它看界面结构变了没有 |

运行前先打开 QQ 或微信并登录，切到一个聊天界面。

插件没效果时先检查：文件是否在 `%APPDATA%\DeskPal\plugins\`（仓库里的这个文件夹 DeskPal 不读）；「设置 → 插件」里点过「刷新」、开关已打开、状态不是“没有正常运行”；然后在终端里直接运行插件（`powershell -ExecutionPolicy Bypass -File windows-wechat.ps1`），切到微信看有没有输出 `detail` 行，`title` 为 null 说明界面结构变了，用探测脚本看。脚本只输出元素的类型、标识、类名和位置，名称和文字只显示首字和长度（例如「张…(2)」），不输出聊天内容、联系人或群名，也不联网。

- Linux：QQ、微信默认不打开辅助功能，脚本读不到时会在最后说明怎样从终端带参数重新启动它们。需要 `python3-gi`（Ubuntu 桌面自带，Arch 装 `python-gobject` 和 `at-spi2-core`）。
- macOS：第一次运行要在「系统设置 → 隐私与安全性 → 辅助功能」里允许终端，并允许终端控制“System Events”。
- 两个脚本都要求 LF 换行（这个文件夹的 `.gitattributes` 已固定）。从 Windows 转发时直接发仓库里的文件，不要用记事本另存。
- Linux 脚本在 Ubuntu 24.04 上对 Chromium 页面实际跑过，并在 Ubuntu 26.04 上读到了 QQ；名称为空的元素会改读文本接口，同样只输出首字和长度。macOS 脚本只做过语法检查，没在 Mac 上运行过。

自己写插件见 [docs/provider-guide.md](../docs/provider-guide.md)。放进这个文件夹的 `.ps1` 要存成带 BOM 的 UTF-8，否则 Windows PowerShell 5.1 读错中文。
