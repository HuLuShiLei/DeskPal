#!/bin/sh
# 例子：macOS，用系统自带的 osascript（JavaScript for Automation）读最前面的应用和它的窗口标题。
# 【未在真机上验证】，用之前请先用 check_provider.py 检查。macOS 自带的官方插件已经能读窗口，这个例子只用来演示。
# 读窗口标题需要“辅助功能”权限：插件作为 DeskPal 的子进程运行，在「系统设置 → 隐私与安全性 → 辅助功能」里允许 DeskPal。
echo '{"v":1,"type":"hello","name":"macos-frontmost","version":"0.1","provides":["foreground"]}'

last=""
while :; do
  line=$(osascript -l JavaScript -e '
    const se = Application("System Events");
    const p = se.applicationProcesses.whose({ frontmost: true })[0];
    let title = "";
    try { title = p.windows[0].name() || ""; } catch (e) {}
    let path = "";
    try { path = p.applicationFile().posixPath(); } catch (e) {}
    JSON.stringify({ v: 1, type: "foreground", process: p.name(), name: p.displayedName(), title: title, path: path });
  ' 2>/dev/null)
  if [ -n "$line" ] && [ "$line" != "$last" ]; then
    echo "$line"
    last="$line"
  fi
  sleep 1
done
