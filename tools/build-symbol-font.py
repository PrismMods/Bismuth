#!/usr/bin/env python3
"""Build Bismuth/Resources/BismuthSymbols.ttf — the keycap-symbol fallback font.

Key viewer labels use symbols (⇥ ⎵ ⏎ ⇧ ↵ …) that display fonts almost never carry.
Without a fallback we control, TMP borrows them from the game's own CJK asset, whose
normalized metrics draw them tiny and off-baseline. This subsets DejaVu Sans down to
the symbol blocks, renames it (the license requires derivatives not carry the original
name), and applies two glyph fixes.

Usage:  python3 tools/build-symbol-font.py [path/to/DejaVuSans.ttf]
        pip install fonttools; DejaVu: https://unpkg.com/dejavu-fonts-ttf@2.37/ttf/DejaVuSans.ttf
"""
import sys, os
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.boundsPen import BoundsPen

SRC = sys.argv[1] if len(sys.argv) > 1 else "DejaVuSans.ttf"
DST = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "..", "Bismuth", "Resources", "BismuthSymbols.ttf")

# Whole blocks, not a hand-picked list: users retype key labels with whatever symbol they
# like (a tester picked ↵ U+21B5 over our default ⏎), and every one we miss looks like a
# bug. DejaVu covers all 112 arrows, the keyboard-ish parts of Misc Technical, and the
# control pictures; absent codepoints are simply skipped by the subsetter.
RANGES = list(range(0x2190, 0x2200)) + list(range(0x2300, 0x2400)) + list(range(0x2400, 0x2440))

NAMES = [(1, "Bismuth Symbols"), (2, "Regular"),
         (3, "Bismuth Symbols; derived from DejaVu Sans"), (4, "Bismuth Symbols"),
         (6, "BismuthSymbols-Regular"), (16, "Bismuth Symbols"), (17, "Regular")]


def main():
    opts = subset.Options()
    opts.name_IDs = ['*']          # keep the license/name table
    opts.hinting = False
    opts.desubroutinize = True
    font = subset.load_font(SRC, opts)
    sub = subset.Subsetter(options=opts)
    sub.populate(unicodes=RANGES)
    sub.subset(font)
    subset.save_font(font, DST, opts)

    font = TTFont(DST)
    upem, glyf, cmap = font['head'].unitsPerEm, font['glyf'], font.getBestCmap()
    cap = 0.73 * upem          # DejaVu cap height; the band the letter keys occupy

    # 1. U+2423 OPEN BOX hangs BELOW the baseline in DejaVu, which reads as "too low" on a
    #    keycap. Centre its ink on the cap band instead.
    g = glyf[cmap[0x2423]]
    g.expand(glyf)
    ys = [p[1] for p in g.coordinates]
    g.coordinates.translate((0, round(cap / 2 - (min(ys) + max(ys)) / 2)))
    g.recalcBounds(glyf)

    # 2. U+23B5 BOTTOM SQUARE BRACKET is absent from DejaVu, and it is the symbol a space
    #    key wants: wide and shallow, where the open box is narrow and deep. Drawn here at
    #    DejaVu's stroke weight so it sits beside the borrowed glyphs without looking alien.
    t, W, H = 170, 1700, 560
    x0, x1 = 100, 100 + W
    y0 = round(cap / 2) - H // 2
    y1 = y0 + H
    pen = TTGlyphPen(None)
    for p in [(x0, y1), (x0, y0), (x1, y0), (x1, y1),
              (x1 - t, y1), (x1 - t, y0 + t), (x0 + t, y0 + t), (x0 + t, y1)]:
        (pen.moveTo if p == (x0, y1) else pen.lineTo)(p)
    pen.closePath()
    name = "spacebracket"
    if name not in font.getGlyphOrder():
        font.setGlyphOrder(list(font.getGlyphOrder()) + [name])
    glyf.glyphs[name] = pen.glyph()
    glyf.glyphOrder = font.getGlyphOrder()
    glyf[name].recalcBounds(glyf)
    font['hmtx'].metrics[name] = (W + 2 * x0, x0)
    font['maxp'].numGlyphs = len(font.getGlyphOrder())
    for table in font['cmap'].tables:
        if table.isUnicode():
            table.cmap[0x23B5] = name

    for nid, val in NAMES:
        font['name'].setName(val, nid, 3, 1, 0x409)
        font['name'].setName(val, nid, 1, 0, 0)
    font.save(DST)

    font = TTFont(DST, lazy=True)
    gs, cmap = font.getGlyphSet(), font.getBestCmap()
    print(f"{os.path.getsize(DST) / 1024:.1f} KB, {len(cmap)} codepoints")
    for label, cp in [('⇥', 0x21E5), ('⎵', 0x23B5), ('␣', 0x2423),
                      ('⏎', 0x23CE), ('↵', 0x21B5), ('⇧', 0x21E7)]:
        if cp not in cmap:
            print(f"  {label} MISSING")
            continue
        bp = BoundsPen(gs)
        gs[cmap[cp]].draw(bp)
        x0, y0, x1, y1 = bp.bounds
        print(f"  {label} y {y0/upem:.2f}..{y1/upem:.2f}  h {(y1-y0)/upem:.2f}")


main()
