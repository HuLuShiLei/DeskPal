#!/usr/bin/env bash
# 例子：Sway（以及兼容 swaymsg 的合成器）。按 sway-ipc 的文档编写，【未在真机上验证】，
# 用之前请先用 check_provider.py 检查。
# 需要：swaymsg（Sway 自带）、jq。
set -u
echo '{"v":1,"type":"hello","name":"sway","version":"0.1"}'

last=""
while :; do
  # 窗口树里 focused 为 true 的那个节点：Wayland 窗口有 app_id，XWayland 窗口有 window_properties.class。
  if info=$(swaymsg -t get_tree 2>/dev/null | jq -c 'first(.. | objects | select(.focused == true and (.pid != null))) // empty') && [ -n "$info" ]; then
    pid=$(jq -r '.pid // empty' <<<"$info")
    path=""; process=""
    if [ -n "$pid" ] && path=$(readlink "/proc/$pid/exe" 2>/dev/null); then process=$(basename "$path"); fi

    line=$(jq -c --arg exe "$process" --arg path "$path" '
      (.app_id // .window_properties.class // "") as $app
      | { v: 1, type: "foreground",
          process: (if $exe != "" then $exe else $app end),
          name: $app,
          title: (.name // ""),
          path: $path,
          fullscreen: ((.fullscreen_mode // 0) != 0) }
      | select(.process != "")' <<<"$info")
    if [ -n "$line" ] && [ "$line" != "$last" ]; then echo "$line"; last="$line"; fi
  fi
  sleep 1
done
