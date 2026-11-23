#!/usr/bin/env python3
"""检查一个 DeskPal 识别插件的输出是否符合协议 v1。

用法:
    python3 check_provider.py <插件文件> [秒数，默认 8]
    <某个命令> | python3 check_provider.py -          # 检查标准输入里的行

运行插件若干秒，按 DeskPal 的规则逐行检查。有错误时退出码为 1。
规则与 src/DeskPal.Client.Core/Providers/ProviderProtocol.cs 一致；两边要一起改。
"""
import json
import os
import subprocess
import sys
import threading
import time

VERSION = 1
MAX_LINE = 8192
MAX_TITLE = 512
MAX_NAME = 128
MAX_PATH = 1024
MAX_LINES_PER_SECOND = 50


def normalize_process(value):
    name = " ".join(value.split())[:MAX_PATH]
    cut = max(name.rfind("/"), name.rfind("\\"))
    if cut >= 0:
        name = name[cut + 1:]
    if name.lower().endswith(".exe"):
        name = name[:-4]
    return name[:MAX_NAME]


def is_text(obj, key):
    return key not in obj or obj[key] is None or isinstance(obj[key], str)


def check_line(line):
    """返回 (类别, 说明)。类别: ok / warn / error / skip。"""
    if not line.strip():
        return "skip", ""
    if len(line) > MAX_LINE:
        return "error", f"一行超过 {MAX_LINE} 个字符"
    try:
        obj = json.loads(line)
    except ValueError:
        return "error", "不是 JSON（调试信息请写到标准错误，不要写到标准输出）"
    if not isinstance(obj, dict):
        return "error", "不是 JSON 对象"
    if "v" in obj and (isinstance(obj["v"], bool) or obj["v"] != VERSION):
        return "error", f'"v" 只能是 {VERSION}'
    kind = obj.get("type")
    if not isinstance(kind, str):
        return "error", '缺少 "type"'

    if kind == "foreground":
        if not isinstance(obj.get("process"), str):
            return "error", 'foreground 必须有 "process"（字符串）'
        process = normalize_process(obj["process"])
        if not process:
            return "error", '"process" 是空的'
        for key in ("name", "title", "path"):
            if not is_text(obj, key):
                return "error", f'"{key}" 必须是字符串'
        if "fullscreen" in obj and not isinstance(obj["fullscreen"], bool):
            return "error", '"fullscreen" 只能是 true 或 false'
        notes = []
        if process != obj["process"]:
            notes.append(f'process 会按 "{process}" 处理')
        if len(obj.get("title") or "") > MAX_TITLE:
            notes.append(f"title 超过 {MAX_TITLE} 个字符，会被截断")
        if process.lower() in ("python", "python3", "java", "node", "electron", "wine", "sh", "bash"):
            notes.append("process 是解释器或运行时的名字，规则认不出具体应用；请改用应用自己的名字")
        text = f'foreground  process={process}  name={obj.get("name")!r}  title={obj.get("title")!r}'
        return ("warn" if notes else "ok"), text + ("    ← " + "；".join(notes) if notes else "")

    if kind == "domain":
        if not is_text(obj, "domain"):
            return "error", '"domain" 必须是字符串或 null'
        value = obj.get("domain")
        if value and ("/" in value or " " in value):
            return "warn", f"domain={value!r}    ← 只需要域名（例如 github.com），不要带路径"
        return "ok", f"domain  {value!r}"

    if kind == "hello":
        if not isinstance(obj.get("name"), str) or not obj["name"].strip():
            return "error", 'hello 必须有 "name"'
        if not is_text(obj, "version"):
            return "error", '"version" 必须是字符串'
        return "ok", f'hello  {obj["name"]} {obj.get("version") or ""}'

    return "warn", f'DeskPal 不认识 type="{kind}"，这一行会被忽略'


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    target = sys.argv[1]
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 8
    process = None
    if target == "-":
        stream = sys.stdin
    else:
        if not os.path.isfile(target):
            print(f"找不到 {target}")
            return 2
        if not os.access(target, os.X_OK):
            print(f"{target} 没有可执行权限，DeskPal 不会列出它。运行: chmod +x {target}")
            return 1
        env = dict(os.environ, DESKPAL_PROTOCOL=str(VERSION))
        process = subprocess.Popen(
            [os.path.abspath(target)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
            encoding="utf-8", errors="replace", cwd=os.path.dirname(os.path.abspath(target)), env=env)
        stream = process.stdout
        threading.Thread(target=lambda: [print("  [标准错误]", l.rstrip()) for l in process.stderr], daemon=True).start()
        threading.Timer(seconds, process.terminate).start()

    counts = {"ok": 0, "warn": 0, "error": 0}
    foreground = 0
    started = time.monotonic()
    shown = None
    for raw in stream:
        kind, text = check_line(raw.rstrip("\n"))
        if kind == "skip":
            continue
        counts[kind] += 1
        if text.startswith("foreground"):
            foreground += 1
        if kind != "ok" or text != shown:
            print({"ok": "  正常", "warn": "  注意", "error": "  错误"}[kind], text)
            shown = text

    elapsed = max(time.monotonic() - started, 0.001)
    total = sum(counts.values())
    print()
    print(f"共 {total} 行，{counts['error']} 行错误，{counts['warn']} 行需要注意。")
    failed = counts["error"] > 0
    if process is not None:
        code = process.wait()
        if code not in (0, -15) and elapsed < seconds - 0.5:
            print(f"插件自己退出了（退出码 {code}）。插件应当一直运行；退出后 DeskPal 会隔一会儿再启动它。")
            failed = True
        if total / elapsed > MAX_LINES_PER_SECOND:
            print(f"每秒超过 {MAX_LINES_PER_SECOND} 行，多出的行会被丢掉。窗口没变化时不必重复输出。")
            failed = True
    if foreground == 0:
        print("没有收到 foreground 行。DeskPal 收到第一条 foreground 之前会显示“正在启动”。")
        failed = True
    print("结论：" + ("有问题，见上。" if failed else "可以用。"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
