#!/bin/bash
# Build Topaz on Linux against the Steam (native Linux) copy of the game.
# Usage: ./release-linux.sh             build only (Topaz/bin/Debug/Topaz.dll)
#        ./release-linux.sh <version>   full release via release.sh, e.g. 1.3.6-b2
#
# The csproj points at the macOS install with a MelonLoader layout. On Linux:
#   - game assemblies come from <game>/ADanceOfFireAndIce_Data/Managed;
#   - Topaz compiles against HarmonyX (not the Harmony 2.3.6 UMM ships), so its 0Harmony.dll is
#     fetched from NuGet once into lib/linux/ and laid out where the csproj expects it, together
#     with a link to UMM's UnityModManager.dll.
# Set ADOFAI_ROOT to use a game install other than the default Steam library.

set -e
cd "$(dirname "$0")"

ROOT="${ADOFAI_ROOT:-$HOME/.local/share/Steam/steamapps/common/A Dance of Fire and Ice}"
MANAGED="$ROOT/ADanceOfFireAndIce_Data/Managed"
HARMONYX_VERSION=2.10.2
FAKE="lib/linux/root"

[ -f "$MANAGED/Assembly-CSharp.dll" ] || { echo "Game not found at $ROOT (set ADOFAI_ROOT)" >&2; exit 1; }
[ -f "$MANAGED/UnityModManager/UnityModManager.dll" ] || { echo "UnityModManager is not installed in $MANAGED" >&2; exit 1; }

HARMONY="lib/linux/HarmonyX-$HARMONYX_VERSION/0Harmony.dll"
if [ ! -f "$HARMONY" ]; then
    TMP=$(mktemp -d)
    trap 'rm -rf "$TMP"' EXIT
    curl -sSLo "$TMP/pkg.zip" "https://www.nuget.org/api/v2/package/HarmonyX/$HARMONYX_VERSION"
    unzip -q "$TMP/pkg.zip" 'lib/net45/0Harmony.dll' -d "$TMP"
    mkdir -p "$(dirname "$HARMONY")"
    mv "$TMP/lib/net45/0Harmony.dll" "$HARMONY"
fi

mkdir -p "$FAKE/MelonLoader/net472" "$FAKE/UserLibs"
ln -sfn "$PWD/$HARMONY" "$FAKE/MelonLoader/net472/0Harmony.dll"
ln -sfn "$MANAGED/UnityModManager/UnityModManager.dll" "$FAKE/UserLibs/UnityModManager.dll"

[ -f lib/PrismLib.dll ] && [ -f lib/PrismLib.UI.dll ] || lib/update-prismlib.sh

export ADOFAI_MANAGED="$MANAGED"
export GAME_ROOT="$PWD/$FAKE"

if [ -n "$1" ]; then
    exec ./release.sh "$1"
fi

BUILD_LOG=$(xbuild Topaz.sln "/p:AdofaiManaged=$ADOFAI_MANAGED" "/p:GameRoot=$GAME_ROOT" 2>&1) || true
if ! grep -q " 0 Error(s)" <<<"$BUILD_LOG"; then
    grep -E "error|Error" <<<"$BUILD_LOG" >&2
    echo "Build failed." >&2
    exit 1
fi
echo "Built Topaz/bin/Debug/Topaz.dll"
