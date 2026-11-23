#!/usr/bin/env bash
# 例子：X11 会话，用 xdotool 读取当前窗口。已在 Ubuntu 24.04 + openbox 上验证。
# DeskPal 内置识别已经支持 X11，这个例子只是用来演示一个完整可用的插件。
# 需要：xdotool、xprop、jq。
set -u
echo '{"v":1,"type":"hello","name":"x11-xdotool","version":"1.0"}'

last=""
while :; do
  if id=$(xdotool getactivewindow 2>/dev/null); then
    title=$(xdotool getwindowname "$id" 2>/dev/null || true)
    pid=$(xdotool getwindowpid "$id" 2>/dev/null || true)
    class=$(xprop -id "$id" WM_CLASS 2>/dev/null | sed -n 's/.*, "\(.*\)"$/\1/p')
    state=$(xprop -id "$id" _NET_WM_STATE 2>/dev/null)
    fullscreen=false; case "$state" in *_NET_WM_STATE_FULLSCREEN*) fullscreen=true ;; esac

    path=""; process=""
    if [ -n "$pid" ] && path=$(readlink "/proc/$pid/exe" 2>/dev/null); then process=$(basename "$path"); fi
    [ -z "$process" ] && process="$class"

    if [ -n "$process" ]; then
      line=$(jq -cn --arg process "$process" --arg name "$class" --arg title "$title" --arg path "$path" --argjson fullscreen "$fullscreen" \
        '{v:1,type:"foreground",process:$process,name:$name,title:$title,path:$path,fullscreen:$fullscreen}')
      if [ "$line" != "$last" ]; then echo "$line"; last="$line"; fi
    fi
  fi
  sleep 1
done
