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

            // Bismuth-element on-screen position editor (moved from the old Locations tab).
            UIBuilder.SectionHeader(content, "Locations");
            UIBuilder.Description(content,
                "Drag elements directly on screen to adjust positions");
            UIBuilder.Button(content, "Edit positions on screen", LocationEditor.Open);
            UIBuilder.DangerButton(content, "Reset all positions", () =>
            {
                s.StatusLeftX  = 0.005f; s.StatusLeftY  = 0.99f;
                s.StatusRightX = 0.995f; s.StatusRightY = 0.99f;
                s.ComboDisplayX = 0.5f;   s.ComboDisplayAnchorY = 0.85f;
                s.ComboDisplayY = 0f;
                s.JudgementsX = 0.5f;     s.JudgementsAnchorY = 0f;
                s.JudgementsY = 0f;
                s.TimingScaleX = 0.5f;    s.TimingScaleAnchorY = 0.12f;
                s.TimingScaleY = 0f;
                s.AttemptsX = 0.77f;      s.AttemptsY = 0.05f;
                if (s.Hand != null) { s.Hand.X = 0.01f; s.Hand.Y = 0.01f; }
                if (s.Foot != null) { s.Foot.X = 0.01f; s.Foot.Y = 0.01f; }
                UICore.OnSettingsChanged?.Invoke();
            });

            UIBuilder.Spacer(content);
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
                    // Stat weight rows on the Overlay tab depend on this family.
                    PageOverlay.RefreshFontWeightRows?.Invoke();
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

        // ── Family + weight font selector ──────────────────────────────────
        // Family/weight name parsing lives in FontLoader (shared with the TMP
        // weight-table wiring).
        private static void SplitWeight(string name, out string family, out string weight)
            => FontLoader.SplitWeight(name, out family, out weight);

        private static int WeightRank(string weight) => FontLoader.WeightRank(weight);

        private static int FindWeight(IList<FontLoader.FontEntry> entries, string weight)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                SplitWeight(entries[i].Name, out _, out string w);
                if (string.Equals(w, weight, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        // One font selector = a family cycle row + a weight row (only when the family has
        // multiple weights, rebuilt on each family change). Internal: PageGameUi reuses it.
        // defaultOption prepends a sentinel family (e.g. "Game default") that fires onDefault
        // instead of apply and clears the weight row; defaultSelected starts on it.
        internal static void BuildFontSelector(
            Transform parent, string label,
            IList<FontLoader.FontEntry> fonts, string currentName,
            Action<FontLoader.FontEntry> apply,
            string defaultOption = null, bool defaultSelected = false, Action onDefault = null)
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
            int offset = defaultOption != null ? 1 : 0;
            var familyOptions = new List<string>(familyNames.Count + offset);
            if (offset == 1) familyOptions.Add(defaultOption);
            familyOptions.AddRange(familyNames);
            int familyIdx = defaultSelected && offset == 1
                ? 0
                : offset + Mathf.Max(0, familyNames.IndexOf(curFamily));

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

            UIBuilder.Dropdown(parent, label, familyOptions, familyIdx, idx =>
            {
                if (offset == 1 && idx == 0)
                {
                    for (int i = weightHost.transform.childCount - 1; i >= 0; i--)
                    {
                        var c = weightHost.transform.GetChild(i);
                        c.SetParent(null);
                        UnityEngine.Object.Destroy(c.gameObject);
                    }
                    onDefault?.Invoke();
                    return;
                }
                var entries = byFamily[familyNames[idx - offset]];
                // Family change always lands on Regular when the family has it (carrying
                // the previous weight over surprises — e.g. Maplestory-Bold → Pretendard
                // landed on Bold); fall back to the previous weight, then the lightest.
                int pick = FindWeight(entries, "Regular");
                if (pick < 0) pick = FindWeight(entries, curWeight);
                if (pick < 0) pick = 0;
                SplitWeight(entries[pick].Name, out _, out curWeight);
                apply(entries[pick]);
                rebuildWeights(idx - offset, curWeight);
            });

            weightHost = UIBuilder.Rect("Weights_" + label, parent);
            var vlg = weightHost.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;

            if (!(defaultSelected && offset == 1))
                rebuildWeights(familyIdx - offset, curWeight);
        }
    }
}
