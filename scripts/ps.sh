#!/usr/bin/env bash

ps -o 'pid,tty,start,cmd' -C 'cx-auto-sign,dotnet cx-auto-sign.dll'
