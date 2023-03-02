ver=2.2.1
ver_file=./cx-auto-sign/cx-auto-sign.csproj

function set-ver() {
  sed -i 's#<\(Version\)>.*</\1>#<\1>'"$1"'</\1>#' "$ver_file"
}
