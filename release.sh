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
REPO="sbrothers7/Bismuth"
ZIP_NAME="Bismuth.zip"
DOWNLOAD_URL="https://github.com/$REPO/releases/download/v$VERSION/$ZIP_NAME"

# Bump version fields in-place.
echo "$VERSION" > VERSION.txt
jq --arg v "$VERSION" '.Version = $v' Info.json > Info.json.tmp && mv Info.json.tmp Info.json
jq --arg v "$VERSION" --arg url "$DOWNLOAD_URL" \
    '.Releases[0].Version = $v | .Releases[0].DownloadUrl = $url' \
    Repository.json > Repository.json.tmp && mv Repository.json.tmp Repository.json

# Build.
xbuild Bismuth.sln > /dev/null

# Stage the UMM payload (single Bismuth/ folder at the zip root).
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/Bismuth/Resources"
cp Bismuth/bin/Debug/Bismuth.dll "$STAGE/Bismuth/"
cp Info.json "$STAGE/Bismuth/"
cp Bismuth/Resources/bismuth-fonts "$STAGE/Bismuth/Resources/"

# Zip it.
rm -f "$ZIP_NAME"
(cd "$STAGE" && zip -qr "$ZIP_NAME" Bismuth)
mv "$STAGE/$ZIP_NAME" .

echo "Built $ZIP_NAME"
echo "Next steps:"
echo "  git commit -am 'v$VERSION' && git push"
echo "  git tag v$VERSION && git push --tags"
echo "  gh release create v$VERSION $ZIP_NAME --title 'v$VERSION' --notes-file -"
