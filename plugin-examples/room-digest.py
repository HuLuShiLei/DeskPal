#!/usr/bin/env python3
"""DeskPal 插件（三个平台）：读取当前房间的成员和状态，把一行摘要写到标准错误（进 DeskPal 的日志）。

只读不报：hello 里 provides 为空，所以不替换任何内置插件的信息；reads 里写了 room，DeskPal 就会在
它开启后往标准输入写 room 行：hello 之后一行，之后房间、成员或任何人的状态变化时再写，每秒最多一行。
每行都是完整的快照，直接用最新一行即可。成员只有 id、name、owner、me、blocked、state、category、since、client，
没有窗口标题、媒体标题、应用名和网站；被我屏蔽的人只有前五项。

用法：Windows 放进 %APPDATA%\\DeskPal\\plugins\\（需要 python 在 PATH 上）；Linux 放进 ~/.config/DeskPal/plugins/，
macOS 放进 ~/Library/Application Support/DeskPal/plugins/，并 chmod +x。在「设置 → 插件」里点「刷新」并开启。
摘要在「设置 → 日志文件」里能看到（DeskPal 每次启动插件只记它标准错误的前 20 行）。
检查：python3 .claude/skills/deskpal-write-provider/scripts/check_provider.py plugin-examples/room-digest.py
"""
import json
import sys
import time


def summary(message):
    room = message.get("room")
    if room is None:
        return "没有进入房间"
    parts = []
    for member in message.get("members", []):
        name = member["name"] + ("（我）" if member.get("me") else "")
        if member.get("blocked"):
            parts.append(f"{name}: 已屏蔽")
        elif member.get("state") == "Offline":
            parts.append(f"{name}: 离线")
        else:
            parts.append(f"{name}: {member.get('state')}/{member.get('category')}")
    return f"{room['name']}  " + "，".join(parts)


def main():
    print(json.dumps({"v": 1, "type": "hello", "name": "房间摘要", "version": "0.1", "provides": [], "reads": ["room"]},
                     ensure_ascii=False), flush=True)
    for line in sys.stdin:
        try:
            message = json.loads(line)
        except ValueError:
            continue
        if message.get("type") == "room":
            print(summary(message), file=sys.stderr, flush=True)
    # 标准输入关闭了（DeskPal 正要重新启动它）：保持运行，等 DeskPal 结束它。
    while True:
        time.sleep(3600)


if __name__ == "__main__":
    main()
