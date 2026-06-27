using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    // Game-HUD layout + game-text repaint settings. The on-screen drag editor and the
    // per-element list below it both drive the same GameUiLayout overrides; global
    // game-text (font/sizes/title weight) lives in its own section.
    internal static class PageGameUi
    {
        // text=true → the element is a single Text, so it gets weight + alignment controls.
        private static readonly (string Key, string Label, bool Text)[] LayoutElements =
        {
            ("percent",      "Death %",        true),
            ("congrats",     "Congrats",       true),
            ("strictclear",  "Strict Clear",   true),
            ("results",      "Results",        true),
            ("presstostart", "Press To Start", true),
            ("countdown",    "Countdown",      true),
            ("difficulty",   "Difficulty",     false),
            ("modifiers",    "Modifiers",      false),
            ("pause",        "Pause Button",   false),
            ("autoplay",     "Autoplay Text",  true),
        };

        private static readonly string[] AlignLabels = { "Left", "Center", "Right" };

        // Offsets are in parent-canvas units; the baked defaults reach ±750, so this
        // range covers a 1920×1080 canvas with headroom (the drag editor is unbounded).
        private const float OffsetRange = 1500f;
        // Level name sits near the top center; a tighter range than the layout elements.
        private const float LevelNameRange = 500f;

        private static GameObject _elementsHost;
        private static GameObject _titleWeightHost;

        public static void Build(RectTransform content)
        {
            var s = UICore.Settings;

            // ── Layout ─────────────────────────────────────────────────────
            UIBuilder.SectionHeader(content, "Layout");
            UIBuilder.Description(content,
                "Drag and resize elements directly on screen. Precise controls can be done using menus below.");
            UIBuilder.Button(content, "Edit game UI on screen", GameUiEditor.Open);
            UIBuilder.DangerButton(content, "Reset layout to Bismuth defaults", () =>
            {
                GameUiLayout.ResetAllToDefaults();
                RebuildElements();
                UICore.OnSettingsChanged?.Invoke();
            });
            UIBuilder.DangerButton(content, "Reset layout to game defaults", () =>
            {
                GameUiLayout.ResetAllToGame();
                RebuildElements();
                UICore.OnSettingsChanged?.Invoke();
            });

            // ── Game text ──────────────────────────────────────────────────
            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Game text");

            // One font selector instead of a toggle. "Game default" = the game's own
            // fonts (swap off, weight controls below hidden), any family = swap on.
            GameObject optionsHost = null;
            PageUI.BuildFontSelector(content, "Game font", UICore.AvailableFonts, s.GameFontName,
                entry =>
                {
                    s.GameFontName = entry.Name;
                    s.GameTextUseOverlayFont = true;
                    MainClass.ApplySelectedFont();
                    UICore.OnSettingsChanged?.Invoke();
                    RebuildTitleWeightRow();
                    RebuildElements();
                    if (optionsHost != null) optionsHost.SetActive(true);
                },
                defaultOption: "Game default",
                defaultSelected: !s.GameTextUseOverlayFont,
                onDefault: () =>
                {
                    s.GameTextUseOverlayFont = false;
                    GameFontApplier.Reapply(); // restores originals
                    UICore.OnSettingsChanged?.Invoke();
                    RebuildElements();
                    if (optionsHost != null) optionsHost.SetActive(false);
                });

            optionsHost = UIBuilder.Rect("GameTextOptions", content);
            var ovlg = optionsHost.AddComponent<VerticalLayoutGroup>();
            ovlg.childControlWidth = true;
            ovlg.childControlHeight = true;
            ovlg.childForceExpandWidth = true;
            ovlg.childForceExpandHeight = false;
            ovlg.spacing = 2f;
            var optBody = optionsHost.transform;

            _titleWeightHost = UIBuilder.Rect("TitleWeightHost", optBody);
            var twvlg = _titleWeightHost.AddComponent<VerticalLayoutGroup>();
            twvlg.childControlWidth = true;
            twvlg.childControlHeight = true;
            twvlg.childForceExpandWidth = true;
            twvlg.childForceExpandHeight = false;
            RebuildTitleWeightRow();

            UIBuilder.Slider(optBody, "Game text size", s.GameTextScale, 0.4f, 1.5f, v =>
            {
                s.GameTextScale = v;
                GameFontApplier.RequestResize();
            });
            UIBuilder.Slider(optBody, "Line spacing", s.GameTextLineSpacing, 0.8f, 2f, v =>
            {
                s.GameTextLineSpacing = v;
                GameFontApplier.RequestResize();
            });
            UIBuilder.Slider(optBody, "Level stats size", s.GameStatsScale, 0.4f, 1.5f, v =>
            {
                s.GameStatsScale = v;
                GameFontApplier.RequestResize();
            });

            optionsHost.SetActive(s.GameTextUseOverlayFont);

            // ── Elements ───────────────────────────────────────────────────
            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Elements");
            _elementsHost = UIBuilder.Rect("ElementsHost", content);
            var evlg = _elementsHost.AddComponent<VerticalLayoutGroup>();
            evlg.childControlWidth = true;
            evlg.childControlHeight = true;
            evlg.childForceExpandWidth = true;
            evlg.childForceExpandHeight = false;
            evlg.spacing = 2f;
            RebuildElements();
        }

        // One expandable row per HUD element. Rebuilt when the game font changes so the
        // weight dropdowns appear/disappear with the family's available weights.
        private static void RebuildElements()
        {
            if (_elementsHost == null) return;
            ClearChildren(_elementsHost);

            var s = UICore.Settings;
            List<string> weights = s.GameTextUseOverlayFont ? GameFamilyWeights(s) : null;
            if (weights != null && weights.Count <= 1) weights = null;

            foreach (var (key, label, text) in LayoutElements)
            {
                string k = key; bool t = text; var w = weights;
                // Header radio = visible. Off hides the element (CanvasGroup on its wrapper);
                // the title still expands the position/scale/weight/align controls below.
                bool visible = !(GameUiLayout.GetOverride(k, false)?.Hidden ?? false);
                UIBuilder.Collapsible(_elementsHost.transform, label, visible,
                    v =>
                    {
                        GameUiLayout.GetOverride(k, true).Hidden = !v;
                        GameUiLayout.ApplyOne(k);
                        UICore.OnSettingsChanged?.Invoke();
                    },
                    body => BuildLayoutBody(body, k, t, w));
            }
            // Judgements (size + weight) and Level Name (Bismuth-owned X/Y/Scale + weight)
            // have no GameUiOverride transform, so they get their own bodies.
            var jw = weights;
            UIBuilder.ExpandSection(_elementsHost.transform, "Judgements", body => BuildJudgementsBody(body, jw));
            UIBuilder.ExpandSection(_elementsHost.transform, "Level Name", body => BuildLevelNameBody(body, jw));
            UIBuilder.ExpandSection(_elementsHost.transform, "Error Meter", BuildMeterBody);
        }

        // Pooled hit-judgement popups: size (GameJudgementScale) + family weight.
        private static void BuildJudgementsBody(Transform body, List<string> weights)
        {
            var s = UICore.Settings;
            UIBuilder.Slider(body, "Size", s.GameJudgementScale, 0.5f, 3f,
                v => { s.GameJudgementScale = v; GameFontApplier.RequestResize(); }, "0.00");
            WeightDropdown(body, "judgement", weights);
        }

        // Level name's transform is owned by Bismuth (ApplyLevelNameTransform), not a
        // GameUiOverride, so X/Y/Scale write the LevelName* settings directly.
        private static void BuildLevelNameBody(Transform body, List<string> weights)
        {
            var s = UICore.Settings;
            UIBuilder.Slider(body, "Position X", s.LevelNameX, -LevelNameRange, LevelNameRange,
                v => { s.LevelNameX = v; Overlay.Instance?.ApplyLevelNameTransform(); }, "0", 1f);
            UIBuilder.Slider(body, "Position Y", s.LevelNameY, -LevelNameRange, LevelNameRange,
                v => { s.LevelNameY = v; Overlay.Instance?.ApplyLevelNameTransform(); }, "0", 1f);
            UIBuilder.Slider(body, "Scale", s.LevelNameScale, 0.1f, 3f,
                v => { s.LevelNameScale = v; Overlay.Instance?.ApplyLevelNameTransform(); }, "0.00");
            WeightDropdown(body, "levelname", weights);
        }

        // Shared "Auto + family weights" dropdown for a GameUiTextWeight key. No-op when
        // the family has ≤1 weight (weights == null).
        private static void WeightDropdown(Transform parent, string key, List<string> weights)
        {
            if (weights == null) return;
            var s = UICore.Settings;
            var options = new List<string> { "Auto" };
            options.AddRange(weights);
            string current = s.GameUiWeightFor(key);
            int idx = 0;
            for (int i = 0; i < weights.Count; i++)
                if (string.Equals(weights[i], current, StringComparison.OrdinalIgnoreCase)) { idx = i + 1; break; }
            UIBuilder.Dropdown(parent, "Weight", options, idx, i =>
            {
                s.SetGameUiWeight(key, i == 0 ? "" : weights[i - 1]);
                MainClass.ApplySelectedFont();
            });
        }

        private static void BuildLayoutBody(Transform body, string key, bool text, List<string> weights)
        {
            var s = UICore.Settings;
            var o = GameUiLayout.GetOverride(key, create: false);
            float offX = o != null ? o.OffX : 0f;
            float offY = o != null ? o.OffY : 0f;
            float scale = o != null ? o.Scale : 1f;

            UIBuilder.Slider(body, "Position X", offX, -OffsetRange, OffsetRange,
                v => { GameUiLayout.GetOverride(key, true).OffX = v; GameUiLayout.ApplyOne(key); }, "0");
            UIBuilder.Slider(body, "Position Y", offY, -OffsetRange, OffsetRange,
                v => { GameUiLayout.GetOverride(key, true).OffY = v; GameUiLayout.ApplyOne(key); }, "0");
            UIBuilder.Slider(body, "Scale", scale, 0.1f, 5f,
                v => { GameUiLayout.GetOverride(key, true).Scale = v; GameUiLayout.ApplyOne(key); }, "0.00");

            if (text) WeightDropdown(body, key, weights);

            if (text)
            {
                int align = o != null && o.Align >= 0 ? o.Align : (int)TextAlign.Center;
                UIBuilder.Segmented(body, "Align", align, AlignLabels, i =>
                {
                    GameUiLayout.GetOverride(key, true).Align = i;
                    GameUiLayout.ApplyOne(key);
                    UICore.OnSettingsChanged?.Invoke();
                });
            }

            UIBuilder.DangerButton(body, "Reset to default", () =>
            {
                GameUiLayout.ResetToDefault(key);
                RebuildElements();
                UICore.OnSettingsChanged?.Invoke();
            });
        }

        private static void BuildMeterBody(Transform body)
        {
            var s = UICore.Settings;
            // X/Y/Scale only take effect while the override is on.
            UIBuilder.Collapsible(body, "Override position", s.GameErrorMeterOverride,
                v => { s.GameErrorMeterOverride = v; UICore.OnSettingsChanged?.Invoke(); }, null);
            // Range extends past 0–1 so the meter can be pushed off-screen (matches the
            // clamp in GameUiLayout.ApplyErrorMeter).
            UIBuilder.Slider(body, "Position X", s.GameErrorMeterX, -0.5f, 1.5f,
                v => { s.GameErrorMeterX = v; UICore.OnSettingsChanged?.Invoke(); }, "0.00");
            UIBuilder.Slider(body, "Position Y", s.GameErrorMeterY, -0.5f, 1.5f,
                v => { s.GameErrorMeterY = v; UICore.OnSettingsChanged?.Invoke(); }, "0.00");
            UIBuilder.Slider(body, "Scale", s.GameErrorMeterScale, 0.1f, 5f,
                v => { s.GameErrorMeterScale = v; UICore.OnSettingsChanged?.Invoke(); }, "0.00");
            UIBuilder.DangerButton(body, "Reset to default", () =>
            {
                GameUiLayout.ResetMeter();
                RebuildElements();
                UICore.OnSettingsChanged?.Invoke();
            });
        }

        // Weights available in the game-font family, canonically sorted.
        private static List<string> GameFamilyWeights(Settings s)
        {
            var result = new List<string>();
            var fonts = UICore.AvailableFonts;
            if (fonts == null || fonts.Count == 0) return result;
            FontLoader.SplitWeight(
                string.IsNullOrEmpty(s.GameFontName) ? fonts[0].Name : s.GameFontName,
                out string family, out _);
            foreach (var e in fonts)
            {
                FontLoader.SplitWeight(e.Name, out string fam, out string w);
                if (fam == family && !string.IsNullOrEmpty(w) && !result.Contains(w))
                    result.Add(w);
            }
            result.Sort((a, b) => FontLoader.WeightRank(a).CompareTo(FontLoader.WeightRank(b)));
            return result;
        }

        // Weight used for title/bold game text. Options: the game-font family's weights
        // plus the "Heaviest" sentinel. Rebuilt when the family changes.
        private static void RebuildTitleWeightRow()
        {
            if (_titleWeightHost == null) return;
            ClearChildren(_titleWeightHost);

            var s = UICore.Settings;
            var fonts = UICore.AvailableFonts;
            if (fonts == null || fonts.Count == 0) return;

            FontLoader.SplitWeight(
                string.IsNullOrEmpty(s.GameFontName) ? fonts[0].Name : s.GameFontName,
                out string family, out _);

            var weights = new List<string> { FontLoader.WeightHeaviest };
            foreach (var e in fonts)
            {
                FontLoader.SplitWeight(e.Name, out string fam, out string w);
                if (fam == family && !string.IsNullOrEmpty(w) && !weights.Contains(w))
                    weights.Add(w);
            }
            weights.Sort((a, b) =>
            {
                // Heaviest pinned first, then canonical light→heavy order.
                if (a == FontLoader.WeightHeaviest) return -1;
                if (b == FontLoader.WeightHeaviest) return 1;
                return FontLoader.WeightRank(a).CompareTo(FontLoader.WeightRank(b));
            });

            int idx = 0;
            for (int i = 0; i < weights.Count; i++)
                if (string.Equals(weights[i], s.GameTextTitleWeight, StringComparison.OrdinalIgnoreCase))
                    idx = i;

            UIBuilder.Dropdown(_titleWeightHost.transform, "Title weight", weights, idx, i =>
            {
                s.GameTextTitleWeight = weights[i];
                MainClass.ApplySelectedFont();
            });
        }

        private static void ClearChildren(GameObject host)
        {
            for (int i = host.transform.childCount - 1; i >= 0; i--)
            {
                var c = host.transform.GetChild(i);
                c.SetParent(null);
                UnityEngine.Object.Destroy(c.gameObject);
            }
        }
    }
}
