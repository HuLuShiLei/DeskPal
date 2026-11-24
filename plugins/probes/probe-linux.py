#!/usr/bin/env python3
"""DeskPal 探测脚本（Linux）：查看 QQ、微信等程序向辅助功能接口（AT-SPI）公开了什么界面结构。

只输出元素的角色、标识、类名、状态和位置。名称和文字一律只输出首字和长度（例如「张…(2)」），
不输出聊天内容、联系人或群名。不改动任何设置，不联网。

用法（在桌面会话的终端里运行，不要用 sudo）:
    python3 probe-linux.py                  # 探测 QQ 和微信
    python3 probe-linux.py feishu dingtalk  # 探测别的程序（写进程名）
    python3 probe-linux.py > probe.txt      # 保存结果，把 probe.txt 发给开发者

运行前先打开要探测的程序并登录，切到一个聊天界面。
需要 python3-gi 和 AT-SPI（Ubuntu 桌面自带；Arch 装 python-gobject 和 at-spi2-core）。
"""
import os
import re
import subprocess
import sys
import time

DEFAULT_TARGETS = ["qq", "wechat"]
MAX_NODES = 2500
MAX_DEPTH = 60
MAX_CHILDREN = 150
KEPT_ATTRIBUTES = ("id", "class", "tag", "xml-roles", "toolkit")


def mask(text):
    if not text:
        return ""
    text = str(text).strip()
    if not text:
        return ""
    return f"{text[0]}…({len(text)})"


def out(line=""):
    print(line, flush=True)


def section(title):
    out()
    out(f"== {title}")


def environment():
    section("环境")
    for key in ("XDG_SESSION_TYPE", "XDG_CURRENT_DESKTOP", "DESKTOP_SESSION"):
        out(f"{key}={os.environ.get(key, '')}")
    try:
        release = dict(
            line.rstrip().split("=", 1) for line in open("/etc/os-release", encoding="utf-8") if "=" in line
        )
        out(f"系统={release.get('PRETTY_NAME', '').strip(chr(34))}")
    except OSError:
        pass
    try:
        value = subprocess.run(
            ["gsettings", "get", "org.gnome.desktop.interface", "toolkit-accessibility"],
            capture_output=True, text=True, timeout=5).stdout.strip()
        out(f"gsettings toolkit-accessibility={value or '(无)'}")
    except (OSError, subprocess.TimeoutExpired):
        out("gsettings toolkit-accessibility=(没有 gsettings)")


def process_table(targets):
    """返回 {pid: (可执行文件名, 路径, 进程类型)}，只包含目标程序的进程。"""
    found = {}
    for entry in os.listdir("/proc"):
        if not entry.isdigit():
            continue
        pid = int(entry)
        try:
            path = os.readlink(f"/proc/{pid}/exe")
            with open(f"/proc/{pid}/cmdline", "rb") as f:
                args = f.read().split(b"\0")
        except OSError:
            continue
        name = os.path.basename(path).replace(" (deleted)", "")
        if name.lower() not in targets:
            continue
        # Chromium 和 Electron 会把命令行改写成一整串，所以在拼起来的文本里找
        joined = b" ".join(args)
        match = re.search(rb"--type=([\w-]+)", joined)
        kind = match.group(1).decode() if match else "main"
        found[pid] = (name, path, kind, joined)
    return found


def processes(targets):
    section("进程")
    table = process_table(targets)
    if not table:
        out(f"没有找到这些程序在运行：{', '.join(targets)}。请先打开并登录。")
        return table
    for pid, (name, path, kind, args) in sorted(table.items()):
        if kind != "main":
            continue
        flags = []
        try:
            with open(f"/proc/{pid}/environ", "rb") as f:
                # 只看这几个变量在不在，不输出环境变量的值以外的任何东西
                env = dict(item.split(b"=", 1) for item in f.read().split(b"\0") if b"=" in item)
            for key in (b"ACCESSIBILITY_ENABLED", b"QT_LINUX_ACCESSIBILITY_ALWAYS_ON", b"QT_ACCESSIBILITY", b"GTK_MODULES"):
                if key in env:
                    flags.append(f"{key.decode()}={env[key].decode(errors='replace')}")
        except OSError:
            flags.append("(读不到环境变量)")
        if b"--force-renderer-accessibility" in args:  # args 是拼起来的命令行
            flags.append("--force-renderer-accessibility")
        out(f"pid={pid} 程序={name} 路径={path} 辅助功能开关=[{' '.join(flags) or '无'}]")
    kinds = {}
    for _, (name, _, kind, _) in table.items():
        kinds[(name, kind)] = kinds.get((name, kind), 0) + 1
    out("进程类型：" + "，".join(f"{n}/{k}×{c}" for (n, k), c in sorted(kinds.items())))
    return table


def describe(node, depth):
    role = node.get_role_name()
    parts = [role]
    try:
        accessible_id = node.get_accessible_id()
        if accessible_id:
            parts.append(f"aid={accessible_id}")
    except Exception:
        pass
    try:
        attributes = node.get_attributes() or {}
        for key in KEPT_ATTRIBUTES:
            if attributes.get(key):
                parts.append(f"{key}={attributes[key]}")
    except Exception:
        pass
    name = mask(node.get_name())
    if name:
        parts.append(f"name={name}")
    else:
        # Chromium 有时不给名称，文字只能从文本接口读到
        try:
            text = node.get_text_iface()
            if text is not None:
                value = mask(Atspi.Text.get_text(text, 0, Atspi.Text.get_character_count(text)).replace("\ufffc", ""))
                if value:
                    parts.append(f"text={value}")
        except Exception:
            pass
    try:
        description = mask(node.get_description())
        if description:
            parts.append(f"desc={description}")
    except Exception:
        pass
    try:
        states = node.get_state_set()
        marks = [label for label, state in (
            ("active", Atspi.StateType.ACTIVE), ("focused", Atspi.StateType.FOCUSED),
            ("showing", Atspi.StateType.SHOWING)) if states.contains(state)]
        if marks:
            parts.append("state=" + ",".join(marks))
    except Exception:
        pass
    try:
        box = node.get_extents(Atspi.CoordType.SCREEN)
        if box.width > 0 and box.height > 0:
            parts.append(f"rect={box.x},{box.y},{box.width},{box.height}")
    except Exception:
        pass
    return "  " * min(depth, 30) + " ".join(parts)


def walk(app):
    count = 0
    stack = [(app, 0)]
    while stack and count < MAX_NODES:
        node, depth = stack.pop()
        try:
            out(describe(node, depth))
        except Exception as error:
            out("  " * min(depth, 30) + f"(读取失败：{type(error).__name__})")
            continue
        count += 1
        if depth >= MAX_DEPTH:
            continue
        try:
            total = node.get_child_count()
        except Exception:
            continue
        children = []
        for index in range(min(total, MAX_CHILDREN)):
            try:
                child = node.get_child_at_index(index)
            except Exception:
                child = None
            if child is not None:
                children.append(child)
        if total > MAX_CHILDREN:
            out("  " * min(depth + 1, 30) + f"…还有 {total - MAX_CHILDREN} 个子元素没有列出")
        for child in reversed(children):
            stack.append((child, depth + 1))
    if stack:
        out(f"…达到 {MAX_NODES} 个元素的上限，后面没有列出")
    return count


def accessibility_bus():
    """辅助功能总线能不能连上。连不上时 AT-SPI 库会直接让进程崩溃，所以先用 gdbus 问一次。"""
    for command in (
        ["gdbus", "call", "--session", "--dest", "org.a11y.Bus", "--object-path", "/org/a11y/bus",
         "--method", "org.a11y.Bus.GetAddress"],
        ["dbus-send", "--session", "--print-reply", "--dest=org.a11y.Bus", "/org/a11y/bus", "org.a11y.Bus.GetAddress"],
    ):
        try:
            result = subprocess.run(command, capture_output=True, text=True, timeout=10)
        except FileNotFoundError:
            continue
        except subprocess.TimeoutExpired:
            return False, "询问辅助功能总线超时"
        if result.returncode == 0:
            return True, ""
        return False, (result.stderr or result.stdout).strip().splitlines()[-1] if (result.stderr or result.stdout).strip() else "失败"
    return True, ""  # 两个命令都没有时，直接试


def accessibility(targets, table):
    section("辅助功能（AT-SPI）")
    ok, reason = accessibility_bus()
    if not ok:
        out(f"连不上辅助功能总线（{reason}）。")
        out("请在桌面会话的终端里运行（不要通过 ssh 或 sudo）。Arch 等精简桌面要装 at-spi2-core，")
        out("并确认登录后 /usr/lib/at-spi-bus-launcher 在运行（GNOME、KDE 会自动启动）。")
        return False, []
    try:
        import gi
        gi.require_version("Atspi", "2.0")
        global Atspi
        from gi.repository import Atspi
    except (ImportError, ValueError) as error:
        out(f"没有 AT-SPI 的 Python 绑定（{error}）。Ubuntu：sudo apt install python3-gi gir1.2-atspi-2.0；Arch：sudo pacman -S python-gobject at-spi2-core")
        return False, []

    Atspi.set_timeout(3000, 15000)
    desktop = Atspi.get_desktop(0)
    apps = []
    for index in range(desktop.get_child_count()):
        app = desktop.get_child_at_index(index)
        if app is None:
            continue
        try:
            apps.append((app, app.get_name() or "", app.get_process_id(), app.get_toolkit_name() or ""))
        except Exception:
            continue
    out("公开了辅助功能的程序：" + "，".join(f"{name or '?'}(pid {pid}, {toolkit})" for _, name, pid, toolkit in apps))

    matched = [(app, name, pid, toolkit) for app, name, pid, toolkit in apps
               if pid in table or name.lower() in targets]
    exposed = {table[pid][0].lower() for _, _, pid, _ in matched if pid in table}
    missing = sorted({name.lower() for name, _, kind, _ in table.values() if kind == "main"} - exposed)
    if missing:
        out(f"在运行但没有公开辅助功能：{', '.join(missing)}，见文末“下一步”。")
    if not matched:
        return False, missing

    ok = False
    for app, name, pid, toolkit in matched:
        section(f"界面结构：{name} pid={pid} 工具包={toolkit}")
        try:
            # 刚查询时，有的程序（Chromium、Qt）才开始建辅助功能树，稍等再读
            app.get_child_count()
            time.sleep(1.5)
            count = walk(app)
            out(f"共 {count} 个元素")
            ok = ok or count > 20
        except Exception as error:
            out(f"读取失败：{type(error).__name__}: {error}")
    return ok, missing


def next_steps(targets, found_tree, missing):
    section("下一步")
    if found_tree and not missing:
        out("已经读到界面结构。把这份输出发给开发者，他会据此找到会话名称所在的元素。")
        return
    if found_tree:
        out("已经读到部分程序的界面结构。" + "、".join(missing) + " 没有公开辅助功能：")
    else:
        out("没有读到足够的界面结构。")
    out("请完全退出程序（包括托盘图标），用下面的命令从终端重新启动，登录并打开一个聊天，再运行这个脚本：")
    out("  QQ（Electron）：ACCESSIBILITY_ENABLED=1 qq --force-renderer-accessibility")
    out("  微信（Qt）：QT_LINUX_ACCESSIBILITY_ALWAYS_ON=1 QT_ACCESSIBILITY=1 wechat")
    out("命令名不对时，用上面“进程”一节里的路径。GNOME 上也可以先运行：")
    out("  gsettings set org.gnome.desktop.interface toolkit-accessibility true")
    out("（这会让桌面上的程序都打开辅助功能，改回用 false。）")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    targets = [a.lower() for a in (args or DEFAULT_TARGETS)]
    out("DeskPal 探测（Linux）。名称只显示首字和长度。")
    environment()
    table = processes(targets)
    found_tree, missing = accessibility(targets, table) if table else (False, [])
    next_steps(targets, found_tree, missing)


if __name__ == "__main__":
    main()
