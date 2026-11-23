#!/usr/bin/env python3
"""DeskPal 识别插件模板（Python，只用标准库）。把 read_window 换成你的环境的查询方式即可。

用法：复制到 ~/.config/DeskPal/providers/，chmod +x，然后在 DeskPal「设置 → 应用识别」里选它。
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


def main():
    emit({"v": 1, "type": "hello", "name": "my-provider", "version": "0.1"})
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
