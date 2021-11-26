#!/usr/bin/env bash

echo ------------
echo dotnet publish
dotnet publish -c Release -o out/cx-auto-sign
echo ------------
echo Pack
cd out || exit 1
tar -zcvf cx-auto-sign.tar.gz cx-auto-sign
