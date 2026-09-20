#!/bin/bash
# Build the downloadable font packs and the manifest the mod reads.
#
# Usage: ./tools/build-font-packs.sh <font-dir> [out-dir]
#
#   <font-dir>  a folder of .ttf/.otf files named "<Family>-<Weight>.<ext>" — the file name
#               (minus extension) is the name the mod shows and the name settings save, so it
#               must not change between packs or saved fonts stop resolving.
#   [out-dir]   where the zips and fonts.json land (default: ./font-packs, gitignored).
#
# Then:
#   gh release create fonts font-packs/*.zip --title "Font packs" --notes "..."
#     (or `gh release upload fonts font-packs/*.zip --clobber` to replace them)
#   cp font-packs/fonts.json . && git commit fonts.json
#
# The "fonts" tag is deliberately not a version tag: UpdateChecker only considers releases
# whose tag parses as a version, so this release is invisible to the updater.

set -e

SRC="${1:?usage: $0 <font-dir> [out-dir]}"
OUT="${2:-font-packs}"
BASE_URL="https://github.com/PrismMods/Bismuth/releases/download/fonts"

# Pack id | display name | note | filename glob
PACKS=(
  "paperlogy|Paperlogy|9 weights · Latin + Korean + Japanese|Paperlogy-*"
  "a2z|에이투지체 (A2Z)|9 weights · Latin + Korean|에이투지체-*"
  "maplestory|Maplestory|Bold only · Latin + Korean + kana|Maplestory-*"
)

rm -rf "$OUT"
mkdir -p "$OUT"
entries=()

for spec in "${PACKS[@]}"; do
    IFS='|' read -r id name note glob <<< "$spec"

    staged="$OUT/.stage/$id"
    mkdir -p "$staged"
    found=0
    for f in "$SRC"/$glob; do
        [ -e "$f" ] || continue
        cp "$f" "$staged/"
        found=$((found + 1))
    done
    if [ "$found" -eq 0 ]; then
        echo "skip $id: no files matching '$glob' in $SRC" >&2
        continue
    fi
    # Licence text travels with the fonts it covers, if one is sitting next to them.
    for lic in "$SRC/$id-LICENSE.txt" "$SRC/$id-OFL.txt"; do
        [ -e "$lic" ] && cp "$lic" "$staged/LICENSE.txt"
    done

    # Zipped through Python, not the zip CLI: Info-ZIP writes non-ASCII names as raw UTF-8
    # bytes WITHOUT setting the zip UTF-8 flag (bit 11), so extractors fall back to CP437
    # and 에이투지체-4Regular.ttf installs as mojibake — a name no saved setting resolves to.
    # zipfile sets the flag whenever a name is non-ASCII.
    python3 - "$staged" "$OUT/$id.zip" <<'ZIPPY'
import os, sys, zipfile
src, dst = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for name in sorted(os.listdir(src)):
        z.write(os.path.join(src, name), name)
ZIPPY
    size=$(du -h "$OUT/$id.zip" | cut -f1 | sed 's/M$/ MB/; s/K$/ KB/')
    echo "built $id.zip  ($found files, $size)"

    entries+=("$(jq -n --arg id "$id" --arg name "$name" --arg note "$note" \
                       --arg size "$size" --arg url "$BASE_URL/$id.zip" \
        '{Id:$id, Name:$name, Note:$note, Size:$size, Url:$url}')")
done

rm -rf "$OUT/.stage"
printf '%s\n' "${entries[@]}" | jq -s '{Packs: .}' > "$OUT/fonts.json"

echo
echo "Wrote $OUT/fonts.json:"
cat "$OUT/fonts.json"
