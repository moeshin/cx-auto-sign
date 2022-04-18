if [ -z "$dir" ]; then
  exit 1
fi

_mkdir() {
  if ! [ -d "$1" ]; then
    if [ -e "$1" ]; then
      echo "Path is file: '$1'"
      exit 1
    fi
    mkdir "$1"
  fi
}

help() {
  if [ -z "$2" ]; then
    echo "多账号管理脚本用法：
$3：
  $1 <用户名或文件名>    单个账号
或：
  $1 -a                全部账号"
    exit 1
  fi
}

parseUser() {
  user="$1"
  if [ -e "$user" ]; then
    user="$(basename "$user")"
    user="${user%.*}"
  fi
  echo "$user"
}

waitLock() {
  # 延迟等待锁生效
  # sleep 1 # 小部分不支持浮点
  sleep 0.5
}

pid_dir="$dir/Pid"
log_dir="$dir/Log"
conf_dir="$dir/Configs"

_mkdir "$pid_dir"
_mkdir "$log_dir"
_mkdir "$conf_dir"

start() {
  nohup bash -c 'flock -xn 200; echo "PID: $$"; echo $$ >&200;'"dotnet cx-auto-sign.dll work -u '$1'" <&- 1>"$log_dir/$1.log" 2>&1 200>"$pid_dir/$1.pid" &
  waitLock
}
