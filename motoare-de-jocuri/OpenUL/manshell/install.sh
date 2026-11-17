#!/bin/bash

mkdir -p "$HOME/.manshell/bin"

SRC="./bin/Release/net10.0/publish"
DST="$HOME/.manshell/bin"

cp -a "$SRC/." "$DST"
