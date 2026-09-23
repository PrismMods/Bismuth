using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Bismuth
{
    internal static class FontLoader
    {
        internal class FontEntry
        {
            public readonly string Name;
            private readonly string _filePath; // .ttf/.otf on disk
            // Same family Bold weight, wired by LinkFamilies after scan
            internal FontEntry BoldSibling;
            private TMP_FontAsset _tmp;

            /* Every font is a file on disk now — shipped none, installed as font packs, or
               dropped in by hand. Unity 6 TMP's CreateFontAsset(filePath) keeps the path and
               reloads the face on demand, so glyphs (incl. CJK) populate dynamically. */
            public FontEntry(string name, string filePath) { Name = name; _filePath = filePath; }

            /* Created on first use: dynamic SDF atlas, with family real Bold in weight
               table so <b>/FontStyles.Bold doesn't fall back to synthetic bold */
            public TMP_FontAsset TmpFont
            {
                get
                {
                    if (_tmp == null)
                    {
                        if (!string.IsNullOrEmpty(_filePath))
                            // 90pt, 9 padding, SDFAA, 1024² — the defaults TMP builds an asset with.
                            _tmp = TMP_FontAsset.CreateFontAsset(_filePath, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                        if (_tmp != null)
                        {
                            _tmp.name = Name + " (TMP)";
                            EnsureSymbolFallback(_tmp);
                            if (BoldSibling != null && BoldSibling != this)
                                _tmp.fontWeightTable[7].regularTypeface = BoldSibling.TmpFont;
                        }
                    }
                    return _tmp;
                }
            }

            internal void DestroyTmp()
            {
                if (_tmp == null) return;
                var atlases = _tmp.atlasTextures;
                if (atlases != null)
                    foreach (var tex in atlases)
                        if (tex != null) UnityEngine.Object.Destroy(tex);
                if (_tmp.material != null) UnityEngine.Object.Destroy(_tmp.material);
                UnityEngine.Object.Destroy(_tmp);
                _tmp = null;
            }
        }

        /* Keycap symbols (⇥ ␣ ⏎ ⇧ …) are missing from nearly every display font, so TMP
           borrows them from whatever fallback has them — the game's CJK asset normalizes
           its metrics differently and draws them tiny and off-baseline. This bundled subset
           carries them at matching metrics. It is a fallback, never a pickable font, so it
           stays out of the scanned list. */
        internal const string SymbolFontName = "BismuthSymbols";
        private static string _symbolFontPath;
        private static TMP_FontAsset _symbolFont;

        private static bool _symbolFontLogged;

        internal static TMP_FontAsset SymbolFont
        {
            get
            {
                if (_symbolFont == null && !string.IsNullOrEmpty(_symbolFontPath))
                {
                    _symbolFont = TMP_FontAsset.CreateFontAsset(
                        _symbolFontPath, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                    if (_symbolFont != null) _symbolFont.name = SymbolFontName + " (TMP)";
                }
                /* Logged once, because every way this can fail looks identical in game — a
                   keycap symbol drawn small and low — and the cause is on disk, not in the
                   code: an install that copied only the dll has no Resources/BismuthSymbols.ttf,
                   so the symbols go back to being borrowed from the game's font. */
                if (!_symbolFontLogged)
                {
                    _symbolFontLogged = true;
                    if (_symbolFont != null)
                        MainClass.Logger.Log("[Bismuth] Keycap symbol font loaded from " + _symbolFontPath);
                    else if (string.IsNullOrEmpty(_symbolFontPath))
                        MainClass.Logger.Warning("[Bismuth] " + SymbolFontName +
                            ".ttf missing from Resources — keycap symbols (⇥ ⎵ ⏎) fall back to the game font");
                    else
                        MainClass.Logger.Warning("[Bismuth] Could not build a font asset from " + _symbolFontPath);
                }
                return _symbolFont;
            }
        }

        // First in the table: GameFontApplier appends the game's own asset here, and that
        // one has the symbols too — at the metrics that started this.
        internal static void EnsureSymbolFallback(TMP_FontAsset target)
        {
            var sym = SymbolFont;
            if (target == null || sym == null || target == sym) return;
            var fb = target.fallbackFontAssetTable;
            if (fb == null) target.fallbackFontAssetTable = fb = new List<TMP_FontAsset>();
            if (fb.Count > 0 && fb[0] == sym) return;
            fb.Remove(sym);
            fb.Insert(0, sym);
        }

        /* Canonical ordering for weight cycle. Names not in this list sort last, in
           scan order */
        internal static readonly string[] WeightOrder =
        {
            "Thin", "ExtraLight", "UltraLight", "Light", "Regular", "Medium",
            "SemiBold", "DemiBold", "Bold", "ExtraBold", "UltraBold", "Heavy", "Black",
        };

        /* "Pretendard SemiBold" / "Pretendard-SemiBold" maps to ("Pretendard",
           "SemiBold"). Some families prefix the weight with an ordinal to force file
           ordering ("Paperlogy-7Bold", "Paperlogy-4Regular") — the leading digits are
           stripped before matching. A last token that still isn't a known weight is a
           single-weight family, shown under its full name. */
        internal static void SplitWeight(string name, out string family, out string weight)
        {
            family = name;
            weight = "Regular";
            if (string.IsNullOrEmpty(name)) return;
            int sp = name.LastIndexOfAny(new[] { ' ', '-' });
            if (sp <= 0) return;
            string last = name.Substring(sp + 1).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            foreach (var w in WeightOrder)
            {
                if (string.Equals(last, w, StringComparison.OrdinalIgnoreCase))
                {
                    family = name.Substring(0, sp);
                    weight = w;
                    return;
                }
            }
        }

        internal static int WeightRank(string weight)
        {
            for (int i = 0; i < WeightOrder.Length; i++)
                if (string.Equals(WeightOrder[i], weight, StringComparison.OrdinalIgnoreCase)) return i;
            return WeightOrder.Length;
        }

        /* Weight-override sentinel: resolves to family heaviest weight at apply time, so
           it tracks family switches instead of pinning specific name */
        internal const string WeightHeaviest = "Heaviest";

        /* Saved settings may spell font with spaces ("Maplestory Bold") while bundle
           asset uses hyphens ("Maplestory-Bold"), match ignoring both */
        private static string NormalizeName(string s) =>
            s == null ? "" : s.Replace(" ", "").Replace("-", "").ToLowerInvariant();

        internal static FontEntry Find(IList<FontEntry> fonts, string name)
        {
            if (fonts == null || string.IsNullOrEmpty(name)) return null;
            string norm = NormalizeName(name);
            foreach (var e in fonts)
                if (NormalizeName(e.Name) == norm) return e;
            return null;
        }

        /* The build ships no fonts. Fonts arrive as installed font packs (FontPacks, one
           subfolder per pack) or dropped in by hand, so both roots are scanned recursively.
           An empty result is normal and handled: the panel and overlay fall back to the
           game's own font (GameFontApplier.GameFont). */
        public static List<FontEntry> ScanFonts(string modPath)
        {
            DropStaleBundle(modPath);
            var result = new List<FontEntry>();
            ScanLooseFonts(Path.Combine(modPath, "Fonts"), result);
            ScanLooseFonts(Path.Combine(modPath, "Resources"), result);
            LinkFamilies(result);
            return result;
        }

        /* Up to 1.3.x the fonts shipped as a 5.6 MB AssetBundle in Resources/. Updates extract
           over the install and never delete, so without this the orphan sits there forever on
           every existing install, doing nothing. Our own file, by exact name. */
        private static void DropStaleBundle(string modPath)
        {
            try
            {
                string old = Path.Combine(Path.Combine(modPath, "Resources"), "bismuth-fonts");
                if (!File.Exists(old)) return;
                File.Delete(old);
                MainClass.Logger.Log("[Bismuth] Removed the old font bundle — fonts are packs now");
            }
            catch { /* read-only install, locked file: harmless, it just stays */ }
        }

        // Register .ttf/.otf files. The file name (minus extension) is the entry name, so
        // "Foo-Bold.ttf" splits into family Foo / weight Bold and bold-links to its siblings.
        // Recursive: font packs install into Fonts/<PackId>/, and it lets a hand-managed
        // Fonts/ folder be organised. First name seen wins.
        private static void ScanLooseFonts(string dir, List<FontEntry> result)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string filePath in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf") continue;
                string name = Path.GetFileNameWithoutExtension(filePath);
                if (string.Equals(name, SymbolFontName, StringComparison.OrdinalIgnoreCase))
                {
                    _symbolFontPath = filePath;   // fallback only, never offered as a choice
                    continue;
                }
                if (Find(result, name) != null) continue;
                result.Add(new FontEntry(name, filePath));
                MainClass.Logger.Log($"[Bismuth] Found custom font '{name}' ({Path.GetFileName(filePath)})");
            }
        }

        /* Pick each family's "bold" weight for <b>/FontStyles.Bold (TMP otherwise faux-bolds).
           Prefer an exact "Bold"; if the family has none, fall back to its heaviest weight that
           is still ≥ Bold (ExtraBold/Black), so a family shipped without a plain Bold still gets
           a REAL bold rather than synthetic. A regular gets the family's bold; the bold weight
           itself gets no sibling. */
        private static void LinkFamilies(List<FontEntry> entries)
        {
            var byFamily = new Dictionary<string, List<FontEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                SplitWeight(e.Name, out string fam, out _);
                if (!byFamily.TryGetValue(fam, out var list)) byFamily[fam] = list = new List<FontEntry>();
                list.Add(e);
            }
            int boldRank = WeightRank("Bold");
            foreach (var e in entries)
            {
                SplitWeight(e.Name, out string fam, out string w);
                if (!byFamily.TryGetValue(fam, out var family)) continue;

                FontEntry exact = null, heaviest = null;
                int heaviestRank = -1;
                foreach (var c in family)
                {
                    SplitWeight(c.Name, out _, out string cw);
                    if (string.Equals(cw, "Bold", StringComparison.OrdinalIgnoreCase)) exact = c;
                    int r = WeightRank(cw);
                    if (r > heaviestRank) { heaviestRank = r; heaviest = c; }
                }

                FontEntry bold = exact;
                // No plain Bold → use the heaviest weight if it's at least Bold and heavier than us.
                if (bold == null && heaviest != null && heaviestRank >= boldRank && heaviestRank > WeightRank(w))
                    bold = heaviest;
                if (bold != null && bold != e) e.BoldSibling = bold;
            }
        }

        internal static void DestroyTmpAssets(List<FontEntry> entries)
        {
            if (entries != null)
                foreach (var e in entries) e.DestroyTmp();
            if (_symbolFont == null) return;
            var atlases = _symbolFont.atlasTextures;
            if (atlases != null)
                foreach (var tex in atlases)
                    if (tex != null) UnityEngine.Object.Destroy(tex);
            if (_symbolFont.material != null) UnityEngine.Object.Destroy(_symbolFont.material);
            UnityEngine.Object.Destroy(_symbolFont);
            _symbolFont = null;
        }

    }
}
