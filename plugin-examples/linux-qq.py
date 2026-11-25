#!/usr/bin/env python3
"""DeskPal 插件（Linux）：QQ 在前台时，用当前会话名（好友或群）代替窗口标题。

通过 AT-SPI（Linux 的辅助功能接口）只读 QQ 当前窗口顶部 class 为 chat-header__contact-name 的那一个元素，
不读消息内容，不注入，不读 QQ 的数据文件，不联网。读不到时报 null，DeskPal 显示原来的窗口标题。

用法：复制到 ~/.config/DeskPal/plugins/，chmod +x，在 DeskPal「设置 → 插件」里点「刷新」并开启。
需要 python3-gi 和 gir1.2-atspi-2.0（Ubuntu 桌面自带；Arch 装 python-gobject 和 at-spi2-core），
并且 QQ 要打开辅助功能：GNOME 上运行一次
    gsettings set org.gnome.desktop.interface toolkit-accessibility true
然后完全退出 QQ（包括托盘图标）再打开。
在终端里直接运行这个文件可以看它的输出：切到 QQ 的聊天，应当出现 detail 行。
"""
import json
import os
import subprocess
import sys
import time

PROCESS = "qq"
HEADER_CLASS = "chat-header__contact-name"
# 这些区域里只有消息、输入框和联系人列表，找会话名时不进去
SKIPPED_CLASSES = ("chat-msg-area", "chat-input-area", "ml-area", "recent-contact", "contact-list", "expression-panel")
POLL_SECONDS = 1.0
RESEARCH_SECONDS = 3.0
APPS_SECONDS = 10.0
MAX_NODES = 4000


def emit(message):
    sys.stdout.write(json.dumps(message, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def fail(text):
    sys.stderr.write(text + "\n")
    sys.stderr.flush()
    sys.exit(1)


def accessibility_bus():
    """辅助功能总线连不上时 AT-SPI 库会直接让进程崩溃，所以先用 gdbus 问一次。"""
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
            return False
        return result.returncode == 0
    return True


def executable(pid):
    try:
        return os.path.basename(os.readlink(f"/proc/{pid}/exe")).replace(" (deleted)", "").lower()
    except OSError:
        return ""


def classes(node):
    try:
        return ((node.get_attributes() or {}).get("class") or "").split()
    except Exception:
        return []


def has_state(node, state):
    try:
        return node.get_state_set().contains(state)
    except Exception:
        return False


class Reader:
    def __init__(self, atspi):
        self.atspi = atspi
        self.apps = []
        self.pids = []
        self.apps_at = 0.0
        self.cache = {}  # 窗口的位置 -> 会话名元素
        self.missed = {}  # 窗口的位置 -> 上次没找到的时间

    def qq_apps(self, now):
        if now - self.apps_at < APPS_SECONDS and self.apps:
            return self.apps
        self.apps_at = now
        found = []
        desktop = self.atspi.get_desktop(0)
        for index in range(desktop.get_child_count()):
            try:
                app = desktop.get_child_at_index(index)
                if app is not None and executable(app.get_process_id()) == PROCESS:
                    found.append(app)
            except Exception:
                continue
        pids = [app.get_process_id() for app in found]
        if pids != self.pids:
            self.cache.clear()
            self.missed.clear()
        self.apps, self.pids = found, pids
        return found

    def active_frame(self, now):
        """QQ 的哪个窗口是当前窗口；QQ 不在前台时返回 None。"""
        for app in self.qq_apps(now):
            try:
                count = app.get_child_count()
            except Exception:
                self.apps_at = 0.0
                continue
            for index in range(count):
                try:
                    frame = app.get_child_at_index(index)
                except Exception:
                    continue
                if frame is not None and has_state(frame, self.atspi.StateType.ACTIVE):
                    return (app.get_process_id(), index), frame
        return None, None

    def find(self, frame):
        """在窗口里找会话名元素，跳过消息区和输入区。"""
        stack = [frame]
        seen = 0
        while stack and seen < MAX_NODES:
            node = stack.pop()
            seen += 1
            names = classes(node)
            if HEADER_CLASS in names:
                return node
            if any(skipped in names for skipped in SKIPPED_CLASSES):
                continue
            try:
                count = node.get_child_count()
            except Exception:
                continue
            for index in range(count - 1, -1, -1):
                try:
                    child = node.get_child_at_index(index)
                except Exception:
                    child = None
                if child is not None:
                    stack.append(child)
        return None

    def text(self, node):
        """元素的名称；Chromium 有时不给名称，就读它的文字。"""
        if not has_state(node, self.atspi.StateType.SHOWING) or has_state(node, self.atspi.StateType.DEFUNCT):
            return None
        try:
            name = (node.get_name() or "").strip()
        except Exception:
            return None
        if name:
            return name
        try:
            text = node.get_text_iface()
            if text is not None:
                value = self.atspi.Text.get_text(text, 0, self.atspi.Text.get_character_count(text))
                value = (value or "").replace("￼", "").strip()
                if value:
                    return value
        except Exception:
            pass
        parts = []
        try:
            for index in range(min(node.get_child_count(), 5)):
                child = node.get_child_at_index(index)
                if child is not None and (child.get_name() or "").strip():
                    parts.append(child.get_name().strip())
        except Exception:
            pass
        return " ".join(parts) or None

    def chat_name(self, key, frame, now):
        node = self.cache.get(key)
        if node is not None:
            name = self.text(node)
            if name:
                return name
            self.cache.pop(key, None)
        if now - self.missed.get(key, 0.0) < RESEARCH_SECONDS:
            return None
        node = self.find(frame)
        if node is None:
            self.missed[key] = now
            return None
        self.cache[key] = node
        self.missed.pop(key, None)
        return self.text(node)


def main():
    emit({"v": 1, "type": "hello", "name": "QQ 会话", "version": "0.1", "provides": ["detail"], "apps": [PROCESS]})
    if not accessibility_bus():
        fail("连不上辅助功能总线（AT-SPI）。请确认是在桌面会话里运行，Arch 需要安装 at-spi2-core。")
    try:
        import gi
        gi.require_version("Atspi", "2.0")
        from gi.repository import Atspi
    except (ImportError, ValueError):
        fail("缺少 AT-SPI 的 Python 绑定。Ubuntu：sudo apt install python3-gi gir1.2-atspi-2.0；Arch：sudo pacman -S python-gobject at-spi2-core")
    Atspi.set_timeout(1000, 5000)
    reader = Reader(Atspi)
    last = None
    while True:
        now = time.monotonic()
        try:
            key, frame = reader.active_frame(now)
            if frame is not None:
                name = reader.chat_name(key, frame, now)
                if name != last:
                    last = name
                    emit({"v": 1, "type": "detail", "process": PROCESS, "window": None, "title": name})
        except Exception as error:  # QQ 关掉或重启时，元素会突然失效
            reader.apps_at = 0.0
            reader.cache.clear()
            sys.stderr.write(f"读取失败：{type(error).__name__}\n")
        time.sleep(POLL_SECONDS)


if __name__ == "__main__":
    try:
        main()
    except (KeyboardInterrupt, BrokenPipeError):
        pass
