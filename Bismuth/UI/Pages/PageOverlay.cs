using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bismuth.UI.Pages
{
    // Root page is a compact list: plain toggles for leaf settings, NavRows that drill
    // into a flat subpage per feature (no nested expanders — one screen, one topic).
    internal static class PageOverlay
    {
        private static readonly string[] PositionLabels = new[] { "Left", "Right" };
        private static readonly string[] AlignLabels = new[] { "Left", "Center", "Right" };

        public static void Build(PageStack stack)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            var content = stack.Root;
            // Weight rows registered by AddWeightRow below; reset so panel rebuilds
            // don't accumulate handlers for destroyed hosts.
            RefreshFontWeightRows = null;

            UIBuilder.SectionHeader(content, "Overlay");
            UIBuilder.Collapsible(content, "Enable", s.ShowOverlay,
                v => { s.ShowOverlay = v; notify?.Invoke(); }, null);

            UIBuilder.Collapsible(content, "Text shadow", s.OverlayShadowEnabled,
                v => { s.OverlayShadowEnabled = v; notify?.Invoke(); },
                body =>
                {
                    if (s.OverlayShadowColor == null)
                        s.OverlayShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };
                    var sc = s.OverlayShadowColor;
                    UIBuilder.ColorPicker(body, "Color",
                        new Color(sc.R, sc.G, sc.B, sc.A), true,
                        c => { sc.R = c.r; sc.G = c.g; sc.B = c.b; sc.A = c.a; notify?.Invoke(); });
                });

            PageUI.BuildFontSelector(content, "Master font", UICore.AvailableFonts, s.FontName,
                entry =>
                {
                    s.FontName = entry.Name;
                    MainClass.ApplySelectedFont();
                    notify?.Invoke();
                    // Weight rows track the master family while the toggle below is on.
                    RefreshFontWeightRows?.Invoke();
                }, showWeightRow: false);
            // Off (default): each overlay part uses its own font (stats below, combo on
            // its subpage, key viewer on its tab). On: master rules all.
            UIBuilder.Collapsible(content, "Apply master font to all overlays", s.OverlayMasterFontEnabled,
                v =>
                {
                    s.OverlayMasterFontEnabled = v;
                    MainClass.ApplySelectedFont();
                    notify?.Invoke();
                    RefreshFontWeightRows?.Invoke();
                }, null);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeaderWithHelp(content, "Stats",
                "Click a card to show or hide that stat in game\n(highlighted = shown).\nClick the ··· button on a card for its settings:\nlabel, position, and colors.");

            UIBuilder.TextInput(content, "Separator text", s.StatSeparator,
                v => { s.StatSeparator = v; notify?.Invoke(); });

            PageUI.BuildFontSelector(content, "Stats font", UICore.AvailableFonts, s.StatsFontName,
                entry =>
                {
                    s.StatsFontName = entry.Name;
                    MainClass.ApplySelectedFont();
                    notify?.Invoke();
                    RefreshFontWeightRows?.Invoke();
                }, showWeightRow: false);
            AddWeightRow(content, "Label weight", () => s.StatLabelWeight, v => s.StatLabelWeight = v);
            AddWeightRow(content, "Value weight", () => s.StatValueWeight, v => s.StatValueWeight = v);

            var statGrid = UIBuilder.CardGrid(content).transform;

            UIBuilder.NavCard(statGrid, "Progress", s.ShowProgress,
                v => { s.ShowProgress = v; notify?.Invoke(); },
                () => stack.Push("Progress", body =>
                {
                    LabelInput(body, "Progress", () => s.ProgressLabel, v => s.ProgressLabel = v, notify);
                    UIBuilder.Segmented(body, "Position", (int)s.ProgressPosition, PositionLabels,
                        i => { s.ProgressPosition = (OverlayPosition)i; notify?.Invoke(); });
                    ColorSection(body, s.ProgressGradient, notify);
                }), "label, position, color, gradient");

            UIBuilder.NavCard(statGrid, "Accuracy", s.ShowAcc,
                v => { s.ShowAcc = v; notify?.Invoke(); },
                () => stack.Push("Accuracy", body =>
                {
                    LabelInput(body, "Accuracy", () => s.AccLabel, v => s.AccLabel = v, notify);
                    UIBuilder.Segmented(body, "Position", (int)s.AccPosition, PositionLabels,
                        i => { s.AccPosition = (OverlayPosition)i; notify?.Invoke(); });
                    ColorSection(body, s.AccGradient, notify);
                }), "label, position, color, gradient");

            UIBuilder.NavCard(statGrid, "X-Accuracy", s.ShowXAcc,
                v => { s.ShowXAcc = v; notify?.Invoke(); },
                () => stack.Push("X-Accuracy", body =>
                {
                    LabelInput(body, "XAccuracy", () => s.XAccLabel, v => s.XAccLabel = v, notify);
                    UIBuilder.Segmented(body, "Position", (int)s.XAccPosition, PositionLabels,
                        i => { s.XAccPosition = (OverlayPosition)i; notify?.Invoke(); });

                    // Forward-declared so the toggle handler can flip its visibility.
                    GameObject colorHost = null;
                    UIBuilder.Collapsible(body, "Use colors from Accuracy", s.XAccUseAccGradient,
                        v =>
                        {
                            s.XAccUseAccGradient = v;
                            if (colorHost != null) colorHost.SetActive(!v);
                            notify?.Invoke();
                        }, null);
                    colorHost = UIBuilder.VGroup(body, "ColorHost");
                    ColorSection(colorHost.transform, s.XAccGradient, notify);
                    colorHost.SetActive(!s.XAccUseAccGradient);
                }), "label, position, use colors from accuracy, color, gradient, xacc");

            UIBuilder.NavCard(statGrid, "BPM", s.ShowBpm,
                v => { s.ShowBpm = v; notify?.Invoke(); },
                () => stack.Push("BPM", body =>
                {
                    LabelInput(body, "BPM", () => s.BpmLabel, v => s.BpmLabel = v, notify);
                    UIBuilder.Segmented(body, "Position", (int)s.BpmPosition, PositionLabels,
                        i => { s.BpmPosition = (OverlayPosition)i; notify?.Invoke(); });
                    ColorSection(body, s.BpmGradient, notify);
                }), "label, position, color, gradient");

            UIBuilder.NavCard(statGrid, "Tile BPM", s.ShowTileBpm,
                v => { s.ShowTileBpm = v; notify?.Invoke(); },
                () => stack.Push("Tile BPM", body =>
                {
                    LabelInput(body, "TBPM", () => s.TileBpmLabel, v => s.TileBpmLabel = v, notify);
                    UIBuilder.Segmented(body, "Position", (int)s.TileBpmPosition, PositionLabels,
                        i => { s.TileBpmPosition = (OverlayPosition)i; notify?.Invoke(); });

                    GameObject colorHost = null;
                    UIBuilder.Collapsible(body, "Use colors from BPM", s.TileBpmUseBpmGradient,
                        v =>
                        {
                            s.TileBpmUseBpmGradient = v;
                            if (colorHost != null) colorHost.SetActive(!v);
                            notify?.Invoke();
                        }, null);
                    colorHost = UIBuilder.VGroup(body, "ColorHost");
                    ColorSection(colorHost.transform, s.TileBpmGradient, notify);
                    colorHost.SetActive(!s.TileBpmUseBpmGradient);
                }), "label, position, use colors from bpm, color, gradient, tbpm");

            UIBuilder.NavCard(statGrid, "KPS", s.ShowKps,
                v => { s.ShowKps = v; notify?.Invoke(); },
                () => stack.Push("KPS", body =>
                {
                    LabelInput(body, "KPS", () => s.KpsLabel, v => s.KpsLabel = v, notify);
                    UIBuilder.Segmented(body, "Position", (int)s.KpsPosition, PositionLabels,
                        i => { s.KpsPosition = (OverlayPosition)i; notify?.Invoke(); });

                    GameObject colorHost = null;
                    UIBuilder.Collapsible(body, "Use colors from Tile BPM", s.KpsUseTileBpmGradient,
                        v =>
                        {
                            s.KpsUseTileBpmGradient = v;
                            if (colorHost != null) colorHost.SetActive(!v);
                            notify?.Invoke();
                        }, null);
                    colorHost = UIBuilder.VGroup(body, "ColorHost");
                    ColorSection(colorHost.transform, s.KpsGradient, notify);
                    colorHost.SetActive(!s.KpsUseTileBpmGradient);
                }), "label, position, use colors from tile bpm, color, gradient, keys per second");

            UIBuilder.NavCard(statGrid, "Song Duration", s.ShowSongDuration,
                v => { s.ShowSongDuration = v; notify?.Invoke(); },
                () => stack.Push("Song Duration", body =>
                {
                    LabelInput(body, "Song Length", () => s.SongDurationLabel, v => s.SongDurationLabel = v, notify);
                    UIBuilder.Segmented(body, "Position", (int)s.SongDurationPosition, PositionLabels,
                        i => { s.SongDurationPosition = (OverlayPosition)i; notify?.Invoke(); });
                }), "label, position, elapsed time, length");

            UIBuilder.NavCard(statGrid, "Level Duration", s.ShowLevelDuration,
                v => { s.ShowLevelDuration = v; notify?.Invoke(); },
                () => stack.Push("Level Duration", body =>
                {
                    LabelInput(body, "Level Length", () => s.LevelDurationLabel, v => s.LevelDurationLabel = v, notify);
                    UIBuilder.Segmented(body, "Position", (int)s.LevelDurationPosition, PositionLabels,
                        i => { s.LevelDurationPosition = (OverlayPosition)i; notify?.Invoke(); });
                }), "label, position, elapsed time, length");

            UIBuilder.NavCard(statGrid, "Progress Bar", s.ShowProgressBar,
                v => { s.ShowProgressBar = v; notify?.Invoke(); },
                () => stack.Push("Progress Bar", body =>
                {
                    UIBuilder.Slider(body, "Height", s.ProgressBarHeight, 1f, 24f,
                        v => { s.ProgressBarHeight = v; notify?.Invoke(); }, "0", 1f);
                    // Style selector appears once styles 2/3 exist; 1 = white fill along
                    // the top edge, Progress perfect color at 100%.
                    var hintRow = UIBuilder.Row(body, 24f);
                    var hint = UIBuilder.Label(hintRow.transform,
                        Loc.T("Style 1: white fill along the top edge; flashes the Progress perfect color at 100%."),
                        (int)UIBuilder.LabelFontSize - 2, TextAnchor.MiddleLeft, Theme.TextMuted);
                    hint.rectTransform.offsetMin = new Vector2(8f, 0);
                }), "height, style");

            UIBuilder.NavCard(statGrid, "Best %", s.ShowBestProgress,
                v => { s.ShowBestProgress = v; notify?.Invoke(); },
                () => stack.Push("Best %", body =>
                {
                    LabelInput(body, "Best", () => s.BestLabel, v => s.BestLabel = v, notify);

                    // Park it in the attempts block instead of a side panel; the position
                    // choice only applies while it's a panel row.
                    GameObject posHost = null;
                    UIBuilder.Collapsible(body, "Show in attempts block", s.BestInAttempts,
                        v =>
                        {
                            s.BestInAttempts = v;
                            if (posHost != null) posHost.SetActive(!v);
                            notify?.Invoke();
                        }, null);
                    posHost = UIBuilder.VGroup(body, "PosHost");
                    UIBuilder.Segmented(posHost.transform, "Position", (int)s.BestPosition, PositionLabels,
                        i => { s.BestPosition = (OverlayPosition)i; notify?.Invoke(); });
                    posHost.SetActive(!s.BestInAttempts);
                }), "label, position, show in attempts block, best progress, record");

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeaderWithHelp(content, "Timing",
                "Click a card to show or hide that element in game\n(highlighted = shown).\nClick the ··· button on a card for its settings.");
            var timingGrid = UIBuilder.CardGrid(content).transform;

            UIBuilder.NavCard(timingGrid, "Timing Scale", s.ShowTimingScale,
                v => { s.ShowTimingScale = v; notify?.Invoke(); },
                () => stack.Push("Timing Scale", body =>
                {
                    UIBuilder.Slider(body, "Y offset", s.TimingScaleY, -300f, 300f,
                        v => { s.TimingScaleY = v; notify?.Invoke(); }, "0", 1f);
                    UIBuilder.Slider(body, "Size", s.TimingScaleSize, 0.25f, 2.0f,
                        v => { s.TimingScaleSize = v; notify?.Invoke(); }, "0.00");
                }), "offset, size");

            UIBuilder.NavCard(timingGrid, "Judgements", s.ShowJudgements,
                v => { s.ShowJudgements = v; notify?.Invoke(); },
                () => stack.Push("Judgements", body =>
                {
                    UIBuilder.Slider(body, "Y offset", s.JudgementsY, 0f, 400f,
                        v => { s.JudgementsY = v; notify?.Invoke(); }, "0", 1f);
                    UIBuilder.Slider(body, "Size", s.JudgementsSize, 0.25f, 2.0f,
                        v => { s.JudgementsSize = v; notify?.Invoke(); }, "0.00");
                    UIBuilder.Slider(body, "Gap", s.JudgementsGap, 0f, 60f,
                        v => { s.JudgementsGap = v; notify?.Invoke(); }, "0", 1f);
                }), "offset, size, gap");

            UIBuilder.NavCard(timingGrid, "Combo Display", s.ShowComboDisplay,
                v => { s.ShowComboDisplay = v; notify?.Invoke(); },
                () => stack.Push("Combo Display", body => BuildComboBody(body, s, notify)),
                "font, count autoplay tiles, gradient max, offset, size, text, weight, shadow, pulse animation, duration, color, label, count");

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Level Info");

            // Song-title controls live elsewhere: visibility in Hide UI, position/scale/
            // weight in Game UI → Elements ("Level Name").
            UIBuilder.NavRow(content, "Attempts",
                () => stack.Push("Attempts", body =>
                {
                    UIBuilder.Collapsible(body, "Show attempts", s.ShowAttempts,
                        v => { s.ShowAttempts = v; notify?.Invoke(); }, null);
                    UIBuilder.Collapsible(body, "Show full attempts", s.ShowFullAttempts,
                        v => { s.ShowFullAttempts = v; notify?.Invoke(); }, null);
                    UIBuilder.Slider(body, "X", s.AttemptsX, 0f, 1f,
                        v => { s.AttemptsX = v; notify?.Invoke(); }, "0.00");
                    UIBuilder.Slider(body, "Y", s.AttemptsY, 0f, 1f,
                        v => { s.AttemptsY = v; notify?.Invoke(); }, "0.00");
                    UIBuilder.Segmented(body, "Align", (int)s.AttemptsAlign, AlignLabels,
                        idx => { s.AttemptsAlign = (TextAlign)idx; notify?.Invoke(); });
                    UIBuilder.DangerButton(body, "Reset current level", () => Overlay.Instance?.ResetAttempts());
                    UIBuilder.DangerButton(body, "Reset all levels", () => AttemptsStore.ClearAll());
                }), "show attempts, full attempts, position, align, reset current level, reset all levels");

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Display");
            UIBuilder.Slider(content, "Overlay scale", s.Scale, 0.5f, 3.0f,
                v => { s.Scale = v; notify?.Invoke(); }, "0.00");
            UIBuilder.Slider(content, "Row spacing", s.StatRowSpacing, -10f, 30f,
                v => { s.StatRowSpacing = v; notify?.Invoke(); }, "0.0", 0.5f);
            UIBuilder.IntSlider(content, "Decimal places", s.Precision, 0, 4,
                v => { s.Precision = v; notify?.Invoke(); });
            UIBuilder.Collapsible(content, "FPS", s.ShowFps,
                v => { s.ShowFps = v; notify?.Invoke(); }, null);
        }

        // Stat-label override input. Shows the effective label; committing empty text or
        // the built-in default stores "" (= use the default).
        private static void LabelInput(Transform body, string def, Func<string> get, Action<string> set, Action notify)
        {
            UIBuilder.TextInput(body, "Label", string.IsNullOrEmpty(get()) ? def : get(),
                v =>
                {
                    var trimmed = (v ?? "").Trim();
                    set(trimmed.Length == 0 || trimmed == def ? "" : v);
                    notify?.Invoke();
                });
        }

        // "Color" section header + flat gradient controls — the standard tail of a stat subpage.
        private static void ColorSection(Transform body, ColorGradient grad, Action notify)
        {
            UIBuilder.Spacer(body);
            UIBuilder.SectionHeader(body, "Color");
            UIBuilder.GradientBody(body, grad, () => notify?.Invoke());
        }

        // Combo Display subpage — everything flat under section headers. Defensive
        // null-checks on the KvColor shadow fields in case Settings was deserialized
        // without them (older save files).
        private static void BuildComboBody(Transform body, Settings s, Action notify)
        {
            UIBuilder.SectionHeader(body, "Main");
            PageUI.BuildFontSelector(body, "Font", UICore.AvailableFonts, s.ComboFontName,
                entry =>
                {
                    s.ComboFontName = entry.Name;
                    MainClass.ApplySelectedFont();
                    notify?.Invoke();
                    RefreshFontWeightRows?.Invoke();
                }, showWeightRow: false);
            UIBuilder.Collapsible(body, "Count autoplay tiles", s.ComboCountAuto,
                v => { s.ComboCountAuto = v; notify?.Invoke(); }, null);
            UIBuilder.Slider(body, "Gradient max", s.ComboGradientMax, 100f, 5000f,
                v => { s.ComboGradientMax = v; notify?.Invoke(); }, "0", 50f);
            UIBuilder.Slider(body, "Y offset", s.ComboDisplayY, -200f, 200f,
                v => { s.ComboDisplayY = v; notify?.Invoke(); }, "0", 1f);
            UIBuilder.Slider(body, "Size", s.ComboDisplaySize, 0.25f, 3f,
                v => { s.ComboDisplaySize = v; notify?.Invoke(); }, "0.00");

            UIBuilder.Spacer(body);
            UIBuilder.SectionHeader(body, "Label");
            UIBuilder.TextInput(body, "Text", s.ComboDisplayText,
                v => { s.ComboDisplayText = v; notify?.Invoke(); });
            UIBuilder.Slider(body, "Y offset", s.ComboLabelY, -100f, 200f,
                v => { s.ComboLabelY = v; notify?.Invoke(); }, "0", 1f);
            UIBuilder.Slider(body, "Size", s.ComboLabelSize, 0.25f, 3f,
                v => { s.ComboLabelSize = v; notify?.Invoke(); }, "0.00");
            AddWeightRow(body, "Weight", () => s.ComboLabelWeight, v => s.ComboLabelWeight = v,
                fontName: () => s.EffectiveComboFont);
            UIBuilder.Slider(body, "Shadow X", s.ComboLabelShadowOffsetX, -20f, 20f,
                v => { s.ComboLabelShadowOffsetX = v; notify?.Invoke(); }, "0.0");
            UIBuilder.Slider(body, "Shadow Y", s.ComboLabelShadowOffsetY, -20f, 20f,
                v => { s.ComboLabelShadowOffsetY = v; notify?.Invoke(); }, "0.0");
            if (s.ComboLabelShadowColor == null)
                s.ComboLabelShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };
            var lc = s.ComboLabelShadowColor;
            UIBuilder.ColorPicker(body, "Shadow color",
                new Color(lc.R, lc.G, lc.B, lc.A), true,
                c => { lc.R = c.r; lc.G = c.g; lc.B = c.b; lc.A = c.a; notify?.Invoke(); });

            UIBuilder.Spacer(body);
            UIBuilder.SectionHeader(body, "Count");
            UIBuilder.Slider(body, "Size", s.ComboCountSize, 0.25f, 3f,
                v => { s.ComboCountSize = v; notify?.Invoke(); }, "0.00");
            AddWeightRow(body, "Weight", () => s.ComboValueWeight, v => s.ComboValueWeight = v,
                includeHeaviest: true, fontName: () => s.EffectiveComboFont);
            UIBuilder.Slider(body, "Shadow X", s.ComboShadowOffsetX, -20f, 20f,
                v => { s.ComboShadowOffsetX = v; notify?.Invoke(); }, "0.0");
            UIBuilder.Slider(body, "Shadow Y", s.ComboShadowOffsetY, -20f, 20f,
                v => { s.ComboShadowOffsetY = v; notify?.Invoke(); }, "0.0");
            if (s.ComboShadowColor == null)
                s.ComboShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };
            var cc = s.ComboShadowColor;
            UIBuilder.ColorPicker(body, "Shadow color",
                new Color(cc.R, cc.G, cc.B, cc.A), true,
                c => { cc.R = c.r; cc.G = c.g; cc.B = c.b; cc.A = c.a; notify?.Invoke(); });

            UIBuilder.Spacer(body);
            UIBuilder.SectionHeader(body, "Pulse animation");
            UIBuilder.Slider(body, "Y offset", s.ComboPulseOffsetY, -20f, 50f,
                v => { s.ComboPulseOffsetY = v; notify?.Invoke(); }, "0.0");
            UIBuilder.Slider(body, "Scale", s.ComboPulseScale, 0f, 1f,
                v => { s.ComboPulseScale = v; notify?.Invoke(); }, "0.00");
            UIBuilder.Slider(body, "Duration", s.ComboPulseDuration, 0.05f, 1f,
                v => { s.ComboPulseDuration = v; notify?.Invoke(); }, "0.00");

            ColorSection(body, s.ComboGradient, notify);
        }

        // ── Per-part font weight overrides ─────────────────────────────────
        // Each AddWeightRow plants a self-rebuilding row whose options track the overlay
        // font family (PageUI calls RefreshFontWeightRows after a font change). The row
        // only exists while the family has multiple weights. Rows on subpages die with
        // their view; their handler unhooks itself on the next refresh.

        internal static Action RefreshFontWeightRows;

        // fontName picks which part's family the weight options come from
        // (default: the stats font).
        internal static void AddWeightRow(Transform parent, string label,
            Func<string> get, Action<string> set, bool includeHeaviest = false,
            Func<string> fontName = null)
        {
            // Host object keeps the ENGLISH label in its name (it's an id, not display text).
            var host = UIBuilder.VGroup(parent, "Weight_" + label);
            label = Loc.T(label);
            var nameSource = fontName ?? (() => UICore.Settings.EffectiveStatsFont);

            Action rebuild = null;
            rebuild = () =>
            {
                if (host == null) { RefreshFontWeightRows -= rebuild; return; }
                for (int i = host.transform.childCount - 1; i >= 0; i--)
                {
                    var c = host.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                var weights = FamilyWeights(nameSource());
                if (weights.Count <= 1) return;
                WeightDropdown(host.transform, label, weights, get(), set, includeHeaviest);
            };
            RefreshFontWeightRows += rebuild;
            rebuild();
        }

        // Weights available in the named font's family, canonically sorted.
        private static List<string> FamilyWeights(string fontName)
        {
            var result = new List<string>();
            var fonts = UICore.AvailableFonts;
            if (fonts == null || fonts.Count == 0) return result;

            var current = FontLoader.Find(fonts, fontName) ?? fonts[0];
            FontLoader.SplitWeight(current.Name, out string family, out _);
            foreach (var e in fonts)
            {
                FontLoader.SplitWeight(e.Name, out string fam, out string w);
                if (fam == family && !result.Contains(w)) result.Add(w);
            }
            result.Sort((a, b) => FontLoader.WeightRank(a).CompareTo(FontLoader.WeightRank(b)));
            return result;
        }

        private static void WeightDropdown(Transform host, string label,
            List<string> weights, string current, Action<string> set, bool includeHeaviest)
        {
            int fixedCount = includeHeaviest ? 2 : 1;
            var options = new List<string>(weights.Count + fixedCount) { "Use Default" };
            if (includeHeaviest) options.Add("Heaviest");
            options.AddRange(weights);

            int idx = 0;
            if (includeHeaviest && string.Equals(current, FontLoader.WeightHeaviest, StringComparison.OrdinalIgnoreCase))
                idx = 1;
            else
                for (int i = 0; i < weights.Count; i++)
                    if (string.Equals(weights[i], current, StringComparison.OrdinalIgnoreCase))
                    { idx = i + fixedCount; break; }

            UIBuilder.Dropdown(host, label, options, idx, i =>
            {
                set(i == 0 ? "" : includeHeaviest && i == 1 ? FontLoader.WeightHeaviest : weights[i - fixedCount]);
                MainClass.ApplySelectedFont();
                UICore.OnSettingsChanged?.Invoke();
            });
        }
    }
}
