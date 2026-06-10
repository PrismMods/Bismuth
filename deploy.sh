#!/bin/bash
set -e

MODS_DIR="$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice/UMMMods/Bismuth"

xbuild Bismuth.sln > /dev/null

mkdir -p "$MODS_DIR/Resources"
cp Bismuth/bin/Debug/Bismuth.dll "$MODS_DIR/"
cp Info.json "$MODS_DIR/"
cp Bismuth/Resources/bismuth-fonts "$MODS_DIR/Resources/"

cmp -s Bismuth/bin/Debug/Bismuth.dll "$MODS_DIR/Bismuth.dll" || { echo "ERROR: deployed dll does not match build output" >&2; exit 1; }
cmp -s Info.json "$MODS_DIR/Info.json" || { echo "ERROR: deployed Info.json does not match" >&2; exit 1; }

echo "Deployed $(grep -o '"Version": "[^"]*"' Info.json) to $MODS_DIR"
