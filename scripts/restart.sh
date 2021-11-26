#!/usr/bin/env bash

dir="$(cd "$(dirname "$0")" || exit 1; pwd)"

. "$dir/.base.sh"

testUser "$1" "重新启动"

restart() {
  user="$1"
  {
    if ! flock -n 200; then
      echo "停止：$user"
      kill "$(cat<&200)"
      waitLock
    fi
  } 200<"$2"
  echo "启动：$user"
  start "$1"
}

if [ "$1" == "-a" ]; then
  for file in "$pid_dir"/*; do
    if [ -f "$file" ]; then
      restart "$(parseUser "$file")" "$file"
    fi
  done
else
  user="$(parseUser "$1")"
  pid_file="$pid_dir/$user.pid"
  if [ -e "$pid_file" ]; then
    restart "$user" "$pid_file"
  fi
fi
