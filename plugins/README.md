# 现成的插件

可以直接用的 DeskPal 插件。用法：把文件复制到插件文件夹（Windows 是 `%APPDATA%\DeskPal\plugins\`，Linux 是 `~/.config/DeskPal/plugins/`，并 `chmod +x`），在 DeskPal「设置 → 插件」里点「刷新」并开启它。

| 文件 | 系统 | 作用 | 验证情况 |
| --- | --- | --- | --- |
| `windows-qq.ps1` | Windows | QQ 在前台时，显示当前会话名（好友或群） | QQ NT 9.9.37 主窗口 |
| `windows-wechat.ps1` | Windows | 微信在前台时，显示当前会话名（好友或群） | 微信 4.2.0.53 主窗口。单独弹出的聊天窗口未验证；旧版微信 3.x 不支持 |
| `linux-qq.py` | Linux | QQ 在前台时，显示当前会话名（好友或群），主窗口和单独的聊天窗口都按当前窗口读 | Ubuntu 26.04（GNOME Wayland）真机验证：切换会话时读到会话名，切到没有会话名的窗口时报 null |

这些插件都只提供「补充标题」（`detail`），放在插件列表的任何位置都可以，当前窗口、网站、暂离等仍由内置插件提供。应用和分类不变：QQ、微信还是“在聊天”。想让某个群算工作，在首页「纠正分类」里按窗口标题设置。

它们怎么读：通过系统的无障碍接口（Windows 的 UI Automation、Linux 的 AT-SPI）只读会话顶部的名称这一个元素，不读消息内容，不注入，也不读软件的数据文件。软件改版后界面结构可能变化，读不到时插件什么都不报，DeskPal 显示原来的窗口标题。

补充的标题和普通窗口标题一样，受「不记录标题」和房间隐私设置控制，只有把“在聊天”设为显示详情的房间才能看到。

## Linux 的 QQ 插件

`linux-qq.py` 用 Python 3 和 AT-SPI（Ubuntu 桌面自带 `python3-gi`；Arch 装 `python-gobject` 和 `at-spi2-core`）。QQ 要打开辅助功能才读得到，GNOME 上运行一次 `gsettings set org.gnome.desktop.interface toolkit-accessibility true`，再完全退出 QQ（包括托盘图标）重新打开。`python3` 要是系统自带的那个（conda 等环境里的 Python 通常没有 `gi`）。在终端里直接运行 `./linux-qq.py`，切到 QQ 的聊天，应当每换一个会话出现一行 `detail`。

它按 QQ 当前激活的窗口读，所以主窗口和单独弹出的聊天窗口都可以；切到图片查看器等没有会话名的窗口时报 null，DeskPal 显示原来的窗口标题。

自己写插件见 [docs/provider-guide.md](../docs/provider-guide.md)。放进这个文件夹的 `.ps1` 要存成带 BOM 的 UTF-8，否则 Windows PowerShell 5.1 读错中文。
