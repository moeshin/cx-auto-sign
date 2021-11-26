#!/usr/bin/env bash

dir="$(cd "$(dirname "$0")" || exit 1; pwd)"

. "$dir/.base.sh"

echo '状态  账号  进程'

for file in "$pid_dir"/*; do
  if [ -f "$file" ]; then
    user="$(parseUser "$file")"
    {
      if flock -n 200; then
        status='停止'
      else
        status="运行"
        pid="$(cat<&200)"
      fi
      echo "$status $user $pid"
    } 200<"$file"
  fi
done
