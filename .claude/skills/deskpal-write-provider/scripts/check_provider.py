#!/usr/bin/env python3
"""检查一个 DeskPal 插件的输出是否符合协议 v1。

用法:
    python3 check_provider.py <插件文件> [秒数，默认 8]
    <某个命令> | python3 check_provider.py -          # 检查标准输入里的行

运行插件若干秒，按 DeskPal 的规则逐行检查。有错误时退出码为 1。
插件在 hello 里声明 "reads": ["room"] 时，往它的标准输入写一行示例 room（和 DeskPal 的格式相同，内容是假的）；
没声明时关闭它的标准输入，和 DeskPal 一样。
Windows 上 .cmd/.bat、.ps1、.py 会像 DeskPal 一样交给 cmd、PowerShell、Python 运行。
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
CAPABILITIES = ("foreground", "domain", "idle", "lock", "media", "detail")
DEFAULT_PROVIDES = ("foreground", "domain")
READS = ("room",)
# 和 RoomFeed.Build 输出的格式一致：成员只有这些字段，没有标题、应用名和网站。
SAMPLE_ROOM = {
    "v": 1, "type": "room", "room": {"id": "00000000-0000-0000-0000-000000000001", "name": "示例房间"},
    "members": [
        {"id": "00000000-0000-0000-0000-000000000002", "name": "张三", "owner": True, "me": False, "blocked": False,
         "state": "Slacking", "category": "Game", "since": "2026-11-27T08:30:00Z", "client": "windows"},
        {"id": "00000000-0000-0000-0000-000000000003", "name": "我", "owner": False, "me": True, "blocked": False,
         "state": "Working", "category": "Work", "since": "2026-11-27T08:10:00Z", "client": "linux"},
        {"id": "00000000-0000-0000-0000-000000000004", "name": "李四", "owner": False, "me": False, "blocked": False,
         "state": "Offline", "category": "None", "since": None, "client": None},
        {"id": "00000000-0000-0000-0000-000000000005", "name": "王五", "owner": False, "me": False, "blocked": True},
    ],
}
WINDOWS = os.name == "nt"
WINDOWS_EXTENSIONS = (".exe", ".cmd", ".bat", ".ps1", ".py")


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


def is_bool(obj, key):
    return key in obj and isinstance(obj[key], bool)


class Checker:
    def __init__(self):
        self.provides = None  # None: hello 没说，按默认
        self.apps = None  # hello 里的 apps，规范化后的进程名
        self.reads = None  # hello 里的 reads，只留认识的；None: 第一行 hello 还没到
        self.seen = set()

    def declared(self):
        return set(self.provides if self.provides is not None else DEFAULT_PROVIDES)

    def check_line(self, line):
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

        if kind == "hello":
            return self.hello(obj)
        if kind not in CAPABILITIES:
            return "warn", f'DeskPal 不认识 type="{kind}"，这一行会被忽略'

        kind_result = getattr(self, kind)(obj)
        if kind_result[0] != "error":
            self.seen.add(kind)
            if kind not in self.declared():
                return "warn", kind_result[1] + f'    ← hello 的 provides 里没有 "{kind}"，DeskPal 不会采用这一行'
        return kind_result

    def hello(self, obj):
        if not isinstance(obj.get("name"), str) or not obj["name"].strip():
            return "error", 'hello 必须有 "name"'
        if not is_text(obj, "version"):
            return "error", '"version" 必须是字符串'
        provides = obj.get("provides")
        note = ""
        if provides is not None:
            if not isinstance(provides, list):
                return "error", '"provides" 必须是列表，例如 ["foreground", "domain"]'
            unknown = [p for p in provides if p not in CAPABILITIES]
            self.provides = [p for p in provides if p in CAPABILITIES]
            if unknown:
                note = f"    ← 不认识 {unknown}，会被忽略"
        apps = obj.get("apps")
        if apps is not None:
            if not isinstance(apps, list):
                return "error", '"apps" 必须是进程名的列表，例如 ["QQ"]'
            self.apps = [normalize_process(a) for a in apps if isinstance(a, str) and normalize_process(a)]
        if "detail" in self.declared() and not self.apps:
            note += '    ← 声明了 detail 却没有 "apps"，DeskPal 不会采用补充的标题'
        reads = obj.get("reads")
        if reads is not None:
            if not isinstance(reads, list):
                return "error", '"reads" 必须是列表，例如 ["room"]'
            unknown = [r for r in reads if r not in READS]
            if unknown:
                note += f"    ← reads 里不认识 {unknown}，会被忽略"
        if self.reads is None:
            self.reads = [r for r in (reads or []) if r in READS]
        shown = self.provides if self.provides is not None else f"{list(DEFAULT_PROVIDES)}（没写 provides，按默认）"
        apps_text = f"  apps={self.apps}" if self.apps is not None else ""
        reads_text = f"  reads={self.reads}" if self.reads else ""
        return ("warn" if note else "ok"), f'hello  {obj["name"]} {obj.get("version") or ""}  provides={shown}{apps_text}{reads_text}{note}'

    def detail(self, obj):
        if not isinstance(obj.get("process"), str):
            return "error", 'detail 必须有 "process"（字符串）'
        process = normalize_process(obj["process"])
        if not process:
            return "error", '"process" 是空的'
        for key in ("window", "title"):
            if not is_text(obj, key):
                return "error", f'"{key}" 必须是字符串或 null'
        notes = []
        if self.apps is not None and process.lower() not in (a.lower() for a in self.apps):
            notes.append(f'"{process}" 不在 hello 的 apps 里，这一行不会生效')
        if not obj.get("window"):
            notes.append('没写 "window"：同一程序的其他窗口也会用这个标题，建议写上看到的窗口标题')
        if len(obj.get("title") or "") > MAX_TITLE:
            notes.append(f"title 超过 {MAX_TITLE} 个字符，会被截断")
        text = f'detail  process={process}  window={obj.get("window")!r}  title={obj.get("title")!r}'
        return ("warn" if notes else "ok"), text + ("    ← " + "；".join(notes) if notes else "")

    def foreground(self, obj):
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
        if process.lower() in ("python", "python3", "java", "node", "electron", "wine", "sh", "bash", "powershell", "pwsh"):
            notes.append("process 是解释器或运行时的名字，规则认不出具体应用；请改用应用自己的名字")
        text = f'foreground  process={process}  name={obj.get("name")!r}  title={obj.get("title")!r}'
        return ("warn" if notes else "ok"), text + ("    ← " + "；".join(notes) if notes else "")

    def domain(self, obj):
        if not is_text(obj, "domain"):
            return "error", '"domain" 必须是字符串或 null'
        value = obj.get("domain")
        if value and ("/" in value or " " in value):
            return "warn", f"domain={value!r}    ← 只需要域名（例如 github.com），不要带路径"
        return "ok", f"domain  {value!r}"

    def idle(self, obj):
        seconds = obj.get("seconds")
        if isinstance(seconds, bool) or not isinstance(seconds, (int, float)) or seconds < 0:
            return "error", 'idle 需要 "seconds"：距上次键盘鼠标输入的秒数（0 或更大的数字）'
        return "ok", f"idle  {seconds} 秒"

    def lock(self, obj):
        if not is_bool(obj, "locked"):
            return "error", 'lock 需要 "locked"：true 或 false'
        return "ok", f'lock  {obj["locked"]}'

    def media(self, obj):
        if not is_bool(obj, "playing"):
            return "error", 'media 需要 "playing"：true 或 false'
        for key in ("title", "artist", "source"):
            if not is_text(obj, key):
                return "error", f'"{key}" 必须是字符串'
        return "ok", f'media  playing={obj["playing"]}  title={obj.get("title")!r}  source={obj.get("source")!r}'


def command_for(path):
    if not WINDOWS:
        return [path]
    ext = os.path.splitext(path)[1].lower()
    if ext in (".cmd", ".bat"):
        return ["cmd.exe", "/d", "/c", path]
    if ext == ".ps1":
        return ["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path]
    if ext == ".py":
        return ["python.exe", "-u", path]
    return [path]


def answer_input(process, reads):
    try:
        if "room" in reads:
            print("  提示 这个插件会读取房间成员和状态（不含标题）。已往它的标准输入写一行示例 room：")
            line = json.dumps(SAMPLE_ROOM, ensure_ascii=False)
            print("       " + line)
            process.stdin.write(line + "\n")
            process.stdin.flush()
        else:
            process.stdin.close()
    except (BrokenPipeError, OSError, ValueError):
        pass


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    target = sys.argv[1]
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 8
    process = None
    if target == "-":
        stream = sys.stdin
    else:
        if not os.path.isfile(target):
            print(f"找不到 {target}")
            return 2
        target = os.path.abspath(target)
        if WINDOWS and os.path.splitext(target)[1].lower() not in WINDOWS_EXTENSIONS:
            print(f"Windows 上 DeskPal 只运行这些类型的插件：{' '.join(WINDOWS_EXTENSIONS)}")
            return 1
        if not WINDOWS and not os.access(target, os.X_OK):
            print(f"{target} 没有可执行权限，DeskPal 不会列出它。运行: chmod +x {target}")
            return 1
        env = dict(os.environ, DESKPAL_PROTOCOL=str(VERSION), PYTHONIOENCODING="utf-8")
        process = subprocess.Popen(
            command_for(target), stdout=subprocess.PIPE, stderr=subprocess.PIPE, stdin=subprocess.PIPE, text=True,
            encoding="utf-8", errors="replace", cwd=os.path.dirname(target), env=env)
        stream = process.stdout
        threading.Thread(target=lambda: [print("  [标准错误]", l.rstrip()) for l in process.stderr], daemon=True).start()
        threading.Timer(seconds, process.terminate).start()

    checker = Checker()
    counts = {"ok": 0, "warn": 0, "error": 0}
    started = time.monotonic()
    shown = None
    answered = False
    for raw in stream:
        kind, text = checker.check_line(raw.rstrip("\r\n"))
        if process is not None and not answered and (checker.reads is not None or kind not in ("skip",)):
            # 第一行有效输出之后决定标准输入：声明了 reads 就写一行示例 room，否则关闭。
            answered = True
            answer_input(process, checker.reads or [])
        if kind == "skip":
            continue
        counts[kind] += 1
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
        if elapsed < seconds - 0.5:
            print(f"插件自己退出了（退出码 {code}）。插件应当一直运行；退出后 DeskPal 会隔一会儿再启动它。")
            failed = True
        if total / elapsed > MAX_LINES_PER_SECOND:
            print(f"每秒超过 {MAX_LINES_PER_SECOND} 行，多出的行会被丢掉。没有变化时不必重复输出。")
            failed = True
    missing = sorted(checker.declared() - checker.seen - {"domain", "lock", "media", "detail"})
    if missing:
        # 网站、锁屏、媒体、补充标题可能只是这几秒里没有变化或对应的应用不在前台；当前窗口和暂离应当很快出现。
        print(f"声明了 {missing} 却没有收到对应的行。DeskPal 收到之前，这几项没有数据。")
        failed = True
    print("结论：" + ("有问题，见上。" if failed else "可以用。"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
