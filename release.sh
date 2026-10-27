#!/bin/bash
# Build a GitHub release zip and bump every version field that needs to stay in sync.
# Usage: ./release.sh <version>     e.g. ./release.sh 1.0.1
#
# After this finishes:
#   1. Commit + push the version bumps so the raw Repository.json updates.
#   2. git tag v<version> && git push --tags
#   3. Create the GitHub release at tag v<version> and upload the produced zip.
#      UMM's in-game updater will then see the new version.

set -e

if [ -z "$1" ]; then
    echo "Usage: $0 <version>   (e.g. $0 1.0.1)" >&2
    exit 1
fi

VERSION="$1"
REPO="PrismMods/Topaz"
ZIP_NAME="Topaz.zip"
DOWNLOAD_URL="https://github.com/$REPO/releases/download/v$VERSION/$ZIP_NAME"

# Bump version fields in-place.
echo "$VERSION" > VERSION.txt
jq --arg v "$VERSION" '.Version = $v' Info.json > Info.json.tmp && mv Info.json.tmp Info.json
jq --arg v "$VERSION" --arg url "$DOWNLOAD_URL" \
    '.Releases[0].Version = $v | .Releases[0].DownloadUrl = $url' \
    Repository.json > Repository.json.tmp && mv Repository.json.tmp Repository.json

# Build. ADOFAI_MANAGED / GAME_ROOT override the csproj's macOS paths (release-linux.sh sets them).
PROPS=()
[ -n "$ADOFAI_MANAGED" ] && PROPS+=("/p:AdofaiManaged=$ADOFAI_MANAGED")
[ -n "$GAME_ROOT" ] && PROPS+=("/p:GameRoot=$GAME_ROOT")
BUILD_LOG=$(xbuild Topaz.sln "${PROPS[@]}" 2>&1) || true
# xbuild's exit status is not reliable on every Mono; never zip a stale dll.
if ! grep -q " 0 Error(s)" <<<"$BUILD_LOG"; then
    grep -E "error|Error" <<<"$BUILD_LOG" >&2
    echo "Build failed; nothing was packaged." >&2
    exit 1
fi

# Stage the UMM payload (single Topaz/ folder at the zip root).
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/Topaz/Resources"
cp Topaz/bin/Debug/Topaz.dll "$STAGE/Topaz/"
# Ships with the mod: PrismLib.UI is an ordinary dependency (the UI half needs no shared
# instance). Leaving it out makes the released build fail to load its update toast.
cp lib/PrismLib.UI.dll "$STAGE/Topaz/"
cp Info.json "$STAGE/Topaz/"
cp Topaz/Resources/TopazSymbols.ttf Topaz/Resources/TopazSymbols-LICENSE.txt "$STAGE/Topaz/Resources/"

# Zip it (<name>.zip with the payload in a <name>/ root folder).
pack() {
    rm -f "$1.zip"
    if command -v zip > /dev/null; then
        (cd "$STAGE" && zip -qr "$1.zip" "$1")
    else
        (cd "$STAGE" && python3 -m zipfile -c "$1.zip" "$1")
    fi
    mv "$STAGE/$1.zip" .
}
pack Topaz
# Topaz was Bismuth up to 1.3.x, and those builds' updater only takes a release zip named
# Bismuth* with a Bismuth/ root. The same payload goes up that way too, so they update into
# Topaz, which carries their data over on its first launch (FromBismuth.cs).
mv "$STAGE/Topaz" "$STAGE/Bismuth"
pack Bismuth

echo "Built $ZIP_NAME and the legacy Bismuth.zip"
echo "Next steps:"
echo "  git commit -am 'v$VERSION' && git push"
echo "  git tag v$VERSION && git push --tags"
echo "  gh release create v$VERSION $ZIP_NAME Bismuth.zip --title 'v$VERSION' --notes-file -"
