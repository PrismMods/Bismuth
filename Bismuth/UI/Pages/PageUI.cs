using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    // "Appearance" tab — how Bismuth itself looks (panel scale, font, accent). The on-screen
    // POSITION editor moved to the Overlay tab: it moves the overlay and key viewer, so it
    // belongs with the things it moves, not with panel styling.
    internal static class PageUI
    {
        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;

            UIBuilder.SectionHeader(content, "Language");
            // Rebuild on change: every panel string is resolved at build time.
            UIBuilder.Dropdown(content, "Language",
                new[] { Loc.T("Follow game"), Loc.T("English"), Loc.T("Korean") },
                Mathf.Clamp(s.PanelLanguage, 0, 2),
                i => { s.PanelLanguage = i; UICore.OnSettingsChanged?.Invoke(); MainClass.RequestForceReload(); });

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Scale");
            UIBuilder.Slider(content, "UI scale", s.UiScale, 0.5f, 2.0f, v => UICore.ApplyScale(v), "0.00");

            UIBuilder.Spacer(content);
            BuildFontAndAccent(stack);

            UIBuilder.Spacer(content);
            BuildProfiles(content);
        }

        // Built by the Overlay tab — positions Bismuth's on-screen elements.
        public static void BuildLocations(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;

            UIBuilder.SectionHeaderWithHelp(content, "Positions",
                "Drag elements directly on screen to adjust positions.");
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
        }

        private static void BuildFontAndAccent(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;

            UIBuilder.SectionHeader(content, "Font");
            // Overlay fonts live on the Overlay tab (master + apply-to-all) and next to
            // their part's weight rows (stats / combo / key viewer).
            BuildFontSelector(content, "Panel font", UICore.AvailableFonts, s.UiFontName,
                entry => UICore.ApplyFont(entry));

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

            // Theme mode: the accent generates one gradient that replaces every stat/combo
            // gradient and the key viewer's default rain color (evaluate-time override —
            // the saved gradients survive toggling).
            UIBuilder.Collapsible(content, "Apply accent as theme color", s.AccentAsTheme, v => {
                s.AccentAsTheme = v;
                UICore.OnSettingsChanged?.Invoke();
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
        // showWeightRow=false drops the base-weight sub-row (used where dedicated per-part
        // weight rows already exist) — family changes then land on Regular/lightest.
        internal static void BuildFontSelector(
            Transform parent, string label,
            IList<FontLoader.FontEntry> fonts, string currentName,
            Action<FontLoader.FontEntry> apply,
            string defaultOption = null, bool defaultSelected = false, Action onDefault = null,
            bool showWeightRow = true)
        {
            if (fonts == null || fonts.Count == 0)
            {
                UIBuilder.Dropdown(parent, label, new[] { Loc.T("(none loaded)") }, 0, null);
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

            // Preview each family name in its own font (Regular weight, else lightest).
            var familyFonts = new List<TMPro.TMP_FontAsset>(familyOptions.Count);
            if (offset == 1) familyFonts.Add(null); // default option keeps the panel font
            foreach (var fam in familyNames)
            {
                var list = byFamily[fam];
                var rep = list[0];
                foreach (var e in list)
                {
                    SplitWeight(e.Name, out _, out string w);
                    if (string.Equals(w, "Regular", StringComparison.OrdinalIgnoreCase)) { rep = e; break; }
                }
                familyFonts.Add(rep.TmpFont);
            }

            // Weight row container — sized by its own layout group so the page VLG picks
            // up the row when present and collapses it when empty.
            GameObject weightHost = null;

            Action<int, string> rebuildWeights = null;
            rebuildWeights = (famIdx, preferredWeight) =>
            {
                if (!showWeightRow) return;
                for (int i = weightHost.transform.childCount - 1; i >= 0; i--)
                {
                    var c = weightHost.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }

                var entries = byFamily[familyNames[famIdx]];
                if (entries.Count <= 1) return;

                var weightNames = new List<string>(entries.Count);
                var weightFonts = new List<TMPro.TMP_FontAsset>(entries.Count);
                int weightIdx = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    SplitWeight(entries[i].Name, out _, out string w);
                    weightNames.Add(w);
                    weightFonts.Add(entries[i].TmpFont); // each weight shown in that weight
                    if (string.Equals(w, preferredWeight, StringComparison.OrdinalIgnoreCase)) weightIdx = i;
                }

                UIBuilder.Dropdown(weightHost.transform, "  Weight", weightNames, weightIdx,
                    idx =>
                    {
                        SplitWeight(entries[idx].Name, out _, out curWeight);
                        apply(entries[idx]);
                    }, weightFonts);
            };

            UIBuilder.Dropdown(parent, label, familyOptions, familyIdx, idx =>
            {
                if (offset == 1 && idx == 0)
                {
                    if (weightHost != null)
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
            }, familyFonts);

            if (showWeightRow)
            {
                weightHost = UIBuilder.VGroup(parent, "Weights_" + label);
                if (!(defaultSelected && offset == 1))
                    rebuildWeights(familyIdx - offset, curWeight);
            }
        }
        // ── Profiles ───────────────────────────────────────────────────────
        // Full-settings snapshots; the .xml files in the Profiles folder ARE the
        // import/export format. Loading copies into the live Settings then rides the
        // force-reload path (deferred, so the panel isn't torn down inside its own
        // button handler).
        private static void BuildProfiles(Transform content)
        {
            UIBuilder.SectionHeaderWithHelp(content, "Profiles",
                "A profile snapshots ALL Bismuth settings.\n" +
                "Load applies it and rebuilds the panel.\n" +
                "Profiles are .xml files in the Profiles folder —\n" +
                "share them, or drop one in and Rescan to import.");

            var listHost = UIBuilder.VGroup(content, "ProfileList");
            string pendingName = "";

            System.Action rebuildList = null;
            rebuildList = () =>
            {
                for (int i = listHost.transform.childCount - 1; i >= 0; i--)
                {
                    var c = listHost.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                foreach (var name in Profiles.BuiltIn)
                    BuildProfileRow(listHost.transform, name, builtIn: true, rebuildList);
                foreach (var name in Profiles.ListSaved())
                    BuildProfileRow(listHost.transform, name, builtIn: false, rebuildList);
            };
            rebuildList();

            UIBuilder.TextInput(content, "New profile name", "", v => pendingName = v);
            UIBuilder.Button(content, "Save current settings as profile", () =>
            {
                if (Profiles.SaveCurrent(pendingName, out string err))
                    rebuildList();
                else
                    BismuthLog.Log("Profiles: " + err);
            });
            UIBuilder.Button(content, "Rescan profiles folder", rebuildList);
            UIBuilder.Button(content, "Open profiles folder", () => OsShell.OpenFolder(Profiles.ProfilesDir()));
        }

        private static void BuildProfileRow(Transform parent, string name, bool builtIn, System.Action rebuildList)
        {
            var row = UIBuilder.Row(parent);
            var bg = UIBuilder.SolidImage(row, Theme.RowBg);
            bg.raycastTarget = true;

            bool active = name == UICore.Settings.ActiveProfile;

            // Accent dot marks the loaded profile, matching the Key Viewer preset rows.
            if (active)
            {
                var dotGo = UIBuilder.Rect("ActiveDot", row.transform);
                var dr = (RectTransform)dotGo.transform;
                dr.anchorMin = dr.anchorMax = new Vector2(0, 0.5f);
                dr.pivot = new Vector2(0, 0.5f);
                dr.anchoredPosition = new Vector2(8f, 0);
                dr.sizeDelta = new Vector2(6f, 6f);
                var dot = dotGo.AddComponent<RoundedRectGraphic>();
                dot.Radius = 3f;
                dot.color = Theme.ToggleOn;
                dot.raycastTarget = false;
                dotGo.AddComponent<AccentFill>();
            }

            var label = UIBuilder.Label(row.transform, builtIn ? name + "  (built-in)" : name,
                (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft,
                active ? Theme.Text : Theme.TextMuted);
            label.rectTransform.offsetMin = new Vector2(active ? 20f : 8f, 0);
            label.rectTransform.offsetMax = new Vector2(-140f, 0);

            // Loading what's already loaded does nothing useful — say so instead.
            if (active)
            {
                var act = UIBuilder.Label(row.transform, Loc.T("Active"),
                    (int)UIBuilder.LabelFontSize - 1, TextAnchor.MiddleRight, Theme.TextMuted);
                act.rectTransform.offsetMin = new Vector2(0f, 0);
                act.rectTransform.offsetMax = new Vector2(builtIn ? -12f : -48f, 0);
            }
            else MiniButton(row.transform, "Load", 56f, builtIn ? -8f : -44f, () =>
            {
                if (Profiles.Load(name, out string err))
                    MainClass.RequestForceReload();
                else
                    BismuthLog.Log("Profiles: " + err);
            });
            if (!builtIn)
                MiniButton(row.transform, "×", 28f, -8f, () =>
                {
                    if (Profiles.Delete(name, out string err)) rebuildList();
                    else BismuthLog.Log("Profiles: " + err);
                });
        }

        private static void MiniButton(Transform parent, string label, float width, float anchoredX, System.Action onClick)
        {
            var btn = UIBuilder.Rect(label, parent);
            var rect = (RectTransform)btn.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(anchoredX, 0);
            rect.sizeDelta = new Vector2(width, 22f);
            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            bg.color = Theme.ButtonBg;
            bg.raycastTarget = true;
            var t = UIBuilder.Label(btn.transform, label, (int)UIBuilder.LabelFontSize - 1, TextAnchor.MiddleCenter, Theme.Text);
            ClickHandler.Attach(btn, () => onClick());
        }
    }
}
