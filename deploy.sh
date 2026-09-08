#!/bin/bash
set -e

MODS_DIR="$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice/UMMMods/Bismuth"

xbuild Bismuth.sln > /dev/null

mkdir -p "$MODS_DIR/Resources"
cp Bismuth/bin/Debug/Bismuth.dll "$MODS_DIR/"
# UMM's ParseVersion strips non-digits from each dotted piece and Int32.Parses it, so
# "1.3.5-b1" is fine (-> 1.3.51) but a piece with NO digit ("dev") throws and the mod is
# silently skipped at startup. Deploy the last release tag instead; the repo file stays as-is.
VERSION=$(grep -o '"Version": "[^"]*"' Info.json | cut -d'"' -f4)
if ! [[ "$VERSION" =~ ^[^.]*[0-9][^.]*(\.[^.]*[0-9][^.]*)*$ ]]; then
  VERSION=$(git describe --tags --abbrev=0 --match 'v[0-9]*' | sed 's/^v//')
  echo "Info.json Version is non-numeric; deploying as $VERSION"
fi
jq --arg v "$VERSION" '.Version = $v' Info.json > "$MODS_DIR/Info.json"
cp Bismuth/Resources/bismuth-fonts "$MODS_DIR/Resources/"
cp Bismuth/Resources/BismuthSymbols.ttf Bismuth/Resources/BismuthSymbols-LICENSE.txt "$MODS_DIR/Resources/"

cmp -s Bismuth/bin/Debug/Bismuth.dll "$MODS_DIR/Bismuth.dll" || { echo "ERROR: deployed dll does not match build output" >&2; exit 1; }
grep -q "\"Version\": \"$VERSION\"" "$MODS_DIR/Info.json" || { echo "ERROR: deployed Info.json does not carry Version $VERSION" >&2; exit 1; }

echo "Deployed $(grep -o '"Version": "[^"]*"' "$MODS_DIR/Info.json") to $MODS_DIR"
