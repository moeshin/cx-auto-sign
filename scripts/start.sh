#!/usr/bin/env bash

dir="$(cd "$(dirname "$0")" || exit 1; pwd)"

. "$dir/.base.sh"

testUser "$1" "启动"

if [ "$1" == "-a" ]; then
  for file in "$conf_dir"/*; do
    if [ -f "$file" ]; then
      user="$(parseUser "$file")"
      {
        if flock -n 200; then
          echo "启动：$user"
        else
          echo "重新启动：$user"
          kill "$(cat<&200)"
          waitLock
        fi
      } 200<"$pid_dir/$user.pid"
      start "$user"
    fi
  done
else
  user="$(parseUser "$1")"
  pid_file="$pid_dir/$user.pid"
  if [ -e "$pid_file" ]; then
    {
      if flock -n 200; then
        echo "启动：$user"
      else
        echo '程序正在运行，是否结束？ [Y/n]'
        read -r text
        case "$text" in
          N | n)
          exit 1
          ;;
        esac
        echo "重新启动：$user"
        kill "$(cat<&200)"
        waitLock
      fi
    } 200<"$pid_file"
  fi
  start "$user"
fi
