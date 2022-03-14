#!/usr/bin/env bash

ver=2.1.3
file=./cx-auto-sign/cx-auto-sign.csproj

sed -ie 's#<\(\(Assembly\|File\|\)Version\)>.*</\1>#'"<\1>$ver</\1>"'#' "$file"
git commit -o "$file" -m "Release $ver" -S || exit 1
git tag "v$ver" || exit 1
