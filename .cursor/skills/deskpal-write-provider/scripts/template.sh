#!/usr/bin/env bash
# DeskPal 识别插件模板（bash）。把 read_window 换成你的桌面环境的查询方式即可。
# 用法：复制到 ~/.config/DeskPal/providers/，chmod +x，然后在 DeskPal「设置 → 应用识别」里选它。
# 需要 jq（用来生成正确的 JSON；手拼 JSON 遇到带引号的标题会出错）。
set -u

echo '{"v":1,"type":"hello","name":"my-provider","version":"0.1"}'

# 设置这四个变量：PID（没有就留空）、APP（应用标识或窗口类名）、TITLE、FULLSCREEN（true/false）。
read_window() {
  PID=""; APP=""; TITLE=""; FULLSCREEN=false
  # 例：从你的桌面环境读取
  # PID=$(some-command --active-window-pid)
  # TITLE=$(some-command --active-window-title)
}

last=""
while :; do
  read_window

  # 规则按“程序文件名”匹配（code、firefox、steam…），优先用进程的可执行文件名。
  process=""; path=""
  if [ -n "$PID" ] && path=$(readlink "/proc/$PID/exe" 2>/dev/null); then
    process=$(basename "$path")
  fi
  [ -z "$process" ] && process="$APP"

  if [ -n "$process" ]; then
    line=$(jq -cn --arg process "$process" --arg name "$APP" --arg title "$TITLE" --arg path "$path" --argjson fullscreen "$FULLSCREEN" \
      '{v:1,type:"foreground",process:$process,name:$name,title:$title,path:$path,fullscreen:$fullscreen}')
    # 没变化就不输出。
    if [ "$line" != "$last" ]; then
      echo "$line"
      last="$line"
    fi
  fi
  sleep 1
done
