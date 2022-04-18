#!/usr/bin/env bash

dir="$(cd "$(dirname "$0")" || exit 1; pwd)"

. "$dir/.base.sh"

help "$0" "$1" "停止"

stop() {
  user="$1"
  {
    if flock -n 200; then
      echo "未启动：$user"
    else
      echo "停止：$user"
      kill "$(cat<&200)"
      waitLock
    fi
  } 200<"$2"
}

if [ "$1" == "-a" ]; then
  for file in "$pid_dir"/*; do
    if [ -f "$file" ]; then
      stop "$(parseUser "$file")" "$file"
    fi
  done
else
  user="$(parseUser "$1")"
  pid_file="$pid_dir/$user.pid"
  if [ -e "$pid_file" ]; then
    stop "$user" "$pid_file"
  fi
fi
