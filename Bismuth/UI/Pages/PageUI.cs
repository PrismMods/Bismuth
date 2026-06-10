using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static class PageUI
    {
        public static void Build(RectTransform content)
        {
            var s = UICore.Settings;

            UIBuilder.SectionHeader(content, "Scale");
            UIBuilder.Slider(content, "UI scale", s.UiScale, 0.5f, 2.0f, v => UICore.ApplyScale(v), "0.00");

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Font");
            var fonts = UICore.AvailableFonts;

            BuildFontSelector(content, "Panel font", fonts, s.UiFontName,
                entry => UICore.ApplyFont(entry));
            BuildFontSelector(content, "Overlay font", fonts, s.FontName,
                entry =>
                {
                    s.FontName = entry.Name;
                    MainClass.ApplySelectedFont();
                    UICore.OnSettingsChanged?.Invoke();
                });

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Accent");
            var current = new Color(s.UiAccentR, s.UiAccentG, s.UiAccentB, 1f);

            // Build both controls; visibility is swapped by the toggle below.
            var swatchRow = UIBuilder.AccentSwatches(content, "Accent color", Theme.AccentPresets, current, c => UICore.ApplyAccent(c));
            var pickerRow = UIBuilder.ColorPicker(content, "Custom color", current, false, c => UICore.ApplyAccent(c));

            UIBuilder.Collapsible(content, "Use custom color", s.UiAccentCustom, v => {
                s.UiAccentCustom = v;
                swatchRow.SetActive(!v);
                pickerRow.SetActive(v);
            }, null);

            swatchRow.SetActive(!s.UiAccentCustom);
            pickerRow.SetActive(s.UiAccentCustom);
        }

        // ── Family + weight font selector ────────────────────────────────────

        // Canonical ordering for the weight cycle. Names not in this list sort last,
        // in scan order.
        private static readonly string[] WeightOrder =
        {
            "Thin", "ExtraLight", "UltraLight", "Light", "Regular", "Medium",
            "SemiBold", "DemiBold", "Bold", "ExtraBold", "UltraBold", "Heavy", "Black",
        };

        // "Pretendard SemiBold" / "Pretendard-SemiBold" → ("Pretendard", "SemiBold"); a name
        // whose last token isn't a known weight is a single-weight family shown under its
        // full name.
        private static void SplitWeight(string name, out string family, out string weight)
        {
            family = name;
            weight = "Regular";
            if (string.IsNullOrEmpty(name)) return;
            int sp = name.LastIndexOfAny(new[] { ' ', '-' });
            if (sp <= 0) return;
            string last = name.Substring(sp + 1);
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

        private static int WeightRank(string weight)
        {
            for (int i = 0; i < WeightOrder.Length; i++)
                if (string.Equals(WeightOrder[i], weight, StringComparison.OrdinalIgnoreCase)) return i;
            return WeightOrder.Length;
        }

        // One font selector = a family cycle row plus a weight cycle row underneath that
        // only exists while the chosen family has more than one weight. The weight row is
        // rebuilt on every family change since CycleSelector's option list is fixed.
        private static void BuildFontSelector(
            Transform parent, string label,
            IList<FontLoader.FontEntry> fonts, string currentName,
            Action<FontLoader.FontEntry> apply)
        {
            if (fonts == null || fonts.Count == 0)
            {
                UIBuilder.Dropdown(parent, label, new[] { "(none loaded)" }, 0, null);
                return;
            }

            // Group by family, preserving scan order of families; weights sorted canonically.
            var familyNames = new List<string>();
            var byFamily = new Dictionary<string, List<FontLoader.FontEntry>>();
            foreach (var e in fonts)
            {
                SplitWeight(e.Name, out string fam, out _);
                if (!byFamily.TryGetValue(fam, out var list))
                {
                    list = new List<FontLoader.FontEntry>();
                    byFamily[fam] = list;
                    familyNames.Add(fam);
                }
                list.Add(e);
            }
            foreach (var list in byFamily.Values)
                list.Sort((a, b) =>
                {
                    SplitWeight(a.Name, out _, out string wa);
                    SplitWeight(b.Name, out _, out string wb);
                    return WeightRank(wa).CompareTo(WeightRank(wb));
                });

            SplitWeight(string.IsNullOrEmpty(currentName) ? fonts[0].Name : currentName,
                out string curFamily, out string curWeight);
            int familyIdx = Mathf.Max(0, familyNames.IndexOf(curFamily));

            // Weight row container — sized by its own layout group so the page VLG picks
            // up the row when present and collapses it when empty.
            GameObject weightHost = null;

            Action<int, string> rebuildWeights = null;
            rebuildWeights = (famIdx, preferredWeight) =>
            {
                for (int i = weightHost.transform.childCount - 1; i >= 0; i--)
                {
                    var c = weightHost.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }

                var entries = byFamily[familyNames[famIdx]];
                if (entries.Count <= 1) return;

                var weightNames = new List<string>(entries.Count);
                int weightIdx = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    SplitWeight(entries[i].Name, out _, out string w);
                    weightNames.Add(w);
                    if (string.Equals(w, preferredWeight, StringComparison.OrdinalIgnoreCase)) weightIdx = i;
                }

                UIBuilder.Dropdown(weightHost.transform, "    Weight", weightNames, weightIdx,
                    idx =>
                    {
                        SplitWeight(entries[idx].Name, out _, out curWeight);
                        apply(entries[idx]);
                    });
            };

            UIBuilder.Dropdown(parent, label, familyNames, familyIdx, idx =>
            {
                var entries = byFamily[familyNames[idx]];
                // Keep the previous weight when the new family has it; else prefer Regular,
                // which entries[0]-after-sort approximates closely enough.
                int pick = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    SplitWeight(entries[i].Name, out _, out string w);
                    if (string.Equals(w, curWeight, StringComparison.OrdinalIgnoreCase)) { pick = i; break; }
                }
                SplitWeight(entries[pick].Name, out _, out curWeight);
                apply(entries[pick]);
                rebuildWeights(idx, curWeight);
            });

            weightHost = UIBuilder.Rect("Weights_" + label, parent);
            var vlg = weightHost.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;

            rebuildWeights(familyIdx, curWeight);
        }
    }
}
