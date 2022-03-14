#!/usr/bin/env bash

. ./ver.sh

set-ver "$ver"
git commit -o "$ver_file" -m "Release $ver" -S || exit 1
git tag "v$ver" || exit 1
