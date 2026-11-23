#!/usr/bin/env python3
"""DeskPal 插件模板（Python，只用标准库，三个平台都能用）。把 read_window 换成你的环境的查询方式即可。

用法：复制到插件文件夹（Windows %APPDATA%\\DeskPal\\plugins，macOS ~/Library/Application Support/DeskPal/plugins，
Linux ~/.config/DeskPal/plugins），Linux 和 macOS 上 chmod +x，然后在 DeskPal「设置 → 插件」里点“刷新”并开启它。
Windows 上 .py 插件需要装好 Python 并能在命令行里直接运行 python。
这个模板只提供“当前窗口”（PROVIDES）。要提供更多，在 PROVIDES 里加上，并输出对应的行，见 emit_* 函数。
"""
import json
import os
import sys
import time


def emit(message):
    # 一行一个 JSON 对象，写完立刻 flush，否则 DeskPal 要等缓冲区满才看得到。
    sys.stdout.write(json.dumps(message, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def read_window():
    """返回 dict(pid=..., app=..., title=..., fullscreen=...)；没有窗口时返回 None。"""
    return None  # 换成你的查询，例如调用桌面环境的命令行工具或 D-Bus 接口


def executable(pid):
    try:
        return os.readlink(f"/proc/{pid}/exe")
    except OSError:
        return None


# 这个插件提供哪几项：foreground（当前窗口）、domain（网站）、idle（暂离）、lock（锁屏）、media（媒体）。
# 没写的几项由 DeskPal 插件列表里排在下面的插件提供。
PROVIDES = ["foreground"]


def emit_idle(seconds):
    """距上次键盘鼠标输入的秒数。提供 idle 时每隔几秒输出一次；是否算暂离由 DeskPal 按用户的设置判断。"""
    emit({"v": 1, "type": "idle", "seconds": seconds})


def emit_media(playing, title=None, artist=None, source=None):
    """正在播放的内容；source 是播放器的程序名。停止时 playing=False。"""
    emit({"v": 1, "type": "media", "playing": playing, "title": title, "artist": artist, "source": source})


def main():
    emit({"v": 1, "type": "hello", "name": "my-plugin", "version": "0.1", "provides": PROVIDES})
    last = None
    while True:
        try:
            window = read_window()
        except Exception as error:  # 出错写到标准错误（会进 DeskPal 日志），然后继续
            print(f"read_window failed: {error}", file=sys.stderr, flush=True)
            window = None

        if window:
            path = executable(window["pid"]) if window.get("pid") else None
            # 规则按“程序文件名”匹配（code、firefox、steam…），优先用进程的可执行文件名。
            process = os.path.basename(path) if path else window.get("app")
            if process:
                message = {
                    "v": 1,
                    "type": "foreground",
                    "process": process,
                    "name": window.get("app") or process,
                    "title": window.get("title") or "",
                    "fullscreen": bool(window.get("fullscreen")),
                }
                if path:
                    message["path"] = path
                if message != last:  # 没变化就不输出
                    emit(message)
                    last = message
        time.sleep(1)


if __name__ == "__main__":
    try:
        main()
    except (BrokenPipeError, KeyboardInterrupt):
        pass
