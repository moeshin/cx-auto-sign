#!/usr/bin/env bash

echo ------------
echo dotnet publish
dotnet publish cx-auto-sign/cx-auto-sign.csproj -c Release -o out/cx-auto-sign
echo ------------
echo Pack
cd out || exit 1
tar -zcvf cx-auto-sign.tar.gz cx-auto-sign
