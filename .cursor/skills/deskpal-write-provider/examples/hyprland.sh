#!/usr/bin/env bash
# 例子：Hyprland。按 hyprctl 的文档编写，【未在真机上验证】，用之前请先用 check_provider.py 检查。
# 需要：hyprctl（Hyprland 自带）、jq。
set -u
echo '{"v":1,"type":"hello","name":"hyprland","version":"0.1","provides":["foreground"]}'

last=""
while :; do
  # hyprctl activewindow -j 给出 class、title、pid、fullscreen（不同版本是 true/false 或 0/1/2）。
  if info=$(hyprctl activewindow -j 2>/dev/null) && [ -n "$info" ] && [ "$info" != "{}" ]; then
    pid=$(jq -r '.pid // empty' <<<"$info")
    path=""; process=""
    if [ -n "$pid" ] && path=$(readlink "/proc/$pid/exe" 2>/dev/null); then process=$(basename "$path"); fi

    line=$(jq -c --arg exe "$process" --arg path "$path" '{
        v: 1, type: "foreground",
        process: (if $exe != "" then $exe else (.class // "") end),
        name: (.class // ""),
        title: (.title // ""),
        path: $path,
        fullscreen: ((.fullscreen // false) | if type == "boolean" then . else . != 0 end)
      } | select(.process != "")' <<<"$info")
    if [ -n "$line" ] && [ "$line" != "$last" ]; then echo "$line"; last="$line"; fi
  fi
  sleep 1
done
