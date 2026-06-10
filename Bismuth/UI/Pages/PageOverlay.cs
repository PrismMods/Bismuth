using System;
using UnityEngine;

namespace Bismuth.UI.Pages
{
    internal static class PageOverlay
    {
        private static readonly string[] PositionLabels = new[] { "Left", "Right" };

        public static void Build(RectTransform content)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeader(content, "Overlay");
            UIBuilder.Collapsible(content, "Enable", s.ShowOverlay,
                v => { s.ShowOverlay = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Stats");

            UIBuilder.Collapsible(content, "Progress", s.ShowProgress,
                v => { s.ShowProgress = v; notify?.Invoke(); },
                body =>
                {
                    UIBuilder.Segmented(body, "Position", (int)s.ProgressPosition, PositionLabels,
                        i => { s.ProgressPosition = (OverlayPosition)i; notify?.Invoke(); });
                    UIBuilder.GradientEditor(body, "Color", s.ProgressGradient, () => notify?.Invoke());
                });

            UIBuilder.Collapsible(content, "Accuracy", s.ShowAcc,
                v => { s.ShowAcc = v; notify?.Invoke(); },
                body =>
                {
                    UIBuilder.Segmented(body, "Position", (int)s.AccPosition, PositionLabels,
                        i => { s.AccPosition = (OverlayPosition)i; notify?.Invoke(); });
                    UIBuilder.GradientEditor(body, "Color", s.AccGradient, () => notify?.Invoke());
                });

            UIBuilder.Collapsible(content, "X-Accuracy", s.ShowXAcc,
                v => { s.ShowXAcc = v; notify?.Invoke(); },
                body =>
                {
                    UIBuilder.Segmented(body, "Position", (int)s.XAccPosition, PositionLabels,
                        i => { s.XAccPosition = (OverlayPosition)i; notify?.Invoke(); });

                    // Forward-declared so the toggle handler can flip its visibility.
                    GameObject xaccGradGo = null;
                    UIBuilder.Collapsible(body, "Use colors from Accuracy", s.XAccUseAccGradient,
                        v =>
                        {
                            s.XAccUseAccGradient = v;
                            if (xaccGradGo != null) xaccGradGo.SetActive(!v);
                            notify?.Invoke();
                        }, null);
                    xaccGradGo = UIBuilder.GradientEditor(body, "Color", s.XAccGradient, () => notify?.Invoke());
                    xaccGradGo.SetActive(!s.XAccUseAccGradient);
                });

            UIBuilder.Collapsible(content, "BPM", s.ShowBpm,
                v => { s.ShowBpm = v; notify?.Invoke(); },
                body =>
                {
                    UIBuilder.Segmented(body, "Position", (int)s.BpmPosition, PositionLabels,
                        i => { s.BpmPosition = (OverlayPosition)i; notify?.Invoke(); });
                    UIBuilder.GradientEditor(body, "Color", s.BpmGradient, () => notify?.Invoke());
                });

            UIBuilder.Collapsible(content, "Tile BPM", s.ShowTileBpm,
                v => { s.ShowTileBpm = v; notify?.Invoke(); },
                body =>
                {
                    UIBuilder.Segmented(body, "Position", (int)s.TileBpmPosition, PositionLabels,
                        i => { s.TileBpmPosition = (OverlayPosition)i; notify?.Invoke(); });

                    GameObject tbpmGradGo = null;
                    UIBuilder.Collapsible(body, "Use colors from BPM", s.TileBpmUseBpmGradient,
                        v =>
                        {
                            s.TileBpmUseBpmGradient = v;
                            if (tbpmGradGo != null) tbpmGradGo.SetActive(!v);
                            notify?.Invoke();
                        }, null);
                    tbpmGradGo = UIBuilder.GradientEditor(body, "Color", s.TileBpmGradient, () => notify?.Invoke());
                    tbpmGradGo.SetActive(!s.TileBpmUseBpmGradient);
                });

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Timing");

            UIBuilder.Collapsible(content, "Timing Scale", s.ShowTimingScale,
                v => { s.ShowTimingScale = v; notify?.Invoke(); },
                body =>
                {
                    UIBuilder.Slider(body, "Y offset", s.TimingScaleY, -300f, 300f,
                        v => { s.TimingScaleY = v; notify?.Invoke(); }, "0", 1f);
                    UIBuilder.Slider(body, "Size", s.TimingScaleSize, 0.25f, 2.0f,
                        v => { s.TimingScaleSize = v; notify?.Invoke(); }, "0.00");
                });

            UIBuilder.Collapsible(content, "Judgements", s.ShowJudgements,
                v => { s.ShowJudgements = v; notify?.Invoke(); },
                body =>
                {
                    UIBuilder.Slider(body, "Y offset", s.JudgementsY, 0f, 400f,
                        v => { s.JudgementsY = v; notify?.Invoke(); }, "0", 1f);
                    UIBuilder.Slider(body, "Size", s.JudgementsSize, 0.25f, 2.0f,
                        v => { s.JudgementsSize = v; notify?.Invoke(); }, "0.00");
                    UIBuilder.Slider(body, "Gap", s.JudgementsGap, 0f, 60f,
                        v => { s.JudgementsGap = v; notify?.Invoke(); }, "0", 1f);
                });

            UIBuilder.Collapsible(content, "Combo Display", s.ShowComboDisplay,
                v => { s.ShowComboDisplay = v; notify?.Invoke(); },
                body => BuildComboBody(body, s, notify));

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Level Info");

            UIBuilder.ExpandSection(content, "Attempts", body =>
            {
                UIBuilder.Collapsible(body, "Show attempts", s.ShowAttempts,
                    v => { s.ShowAttempts = v; notify?.Invoke(); }, null);
                UIBuilder.Collapsible(body, "Show full attempts", s.ShowFullAttempts,
                    v => { s.ShowFullAttempts = v; notify?.Invoke(); }, null);
                UIBuilder.Slider(body, "X", s.AttemptsX, 0f, 1f,
                    v => { s.AttemptsX = v; notify?.Invoke(); }, "0.00");
                UIBuilder.Slider(body, "Y", s.AttemptsY, 0f, 1f,
                    v => { s.AttemptsY = v; notify?.Invoke(); }, "0.00");
                UIBuilder.DangerButton(body, "Reset current level", () => Overlay.Instance?.ResetAttempts());
                UIBuilder.DangerButton(body, "Reset all levels", () => AttemptsStore.ClearAll());
            });

            // Scale/offset always apply; visibility (HideLevelName) lives in Hide UI.
            UIBuilder.ExpandSection(content, "Song Title/Artist", body =>
            {
                UIBuilder.Slider(body, "Scale", s.LevelNameScale, 0.1f, 3f,
                    v => { s.LevelNameScale = v; notify?.Invoke(); }, "0.00");
                UIBuilder.Slider(body, "Y offset", s.LevelNameY, -500f, 500f,
                    v => { s.LevelNameY = v; notify?.Invoke(); }, "0", 1f);
            });

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Display");
            UIBuilder.Slider(content, "Overlay scale", s.Scale, 0.5f, 3.0f,
                v => { s.Scale = v; notify?.Invoke(); }, "0.00");
            UIBuilder.IntSlider(content, "Decimal places", s.Precision, 0, 4,
                v => { s.Precision = v; notify?.Invoke(); });

            UIBuilder.Spacer(content);
            UIBuilder.Collapsible(content, "FPS", s.ShowFps,
                v => { s.ShowFps = v; notify?.Invoke(); }, null);
        }

        // Combo display has a lot of settings; group them into sub-ExpandSections so the body
        // stays scannable. Defensive null-checks on the KvColor shadow fields in case Settings
        // was deserialized without them (older save files).
        private static void BuildComboBody(Transform body, Settings s, Action notify)
        {
            UIBuilder.Collapsible(body, "Count autoplay tiles", s.ComboCountAuto,
                v => { s.ComboCountAuto = v; notify?.Invoke(); }, null);

            UIBuilder.Slider(body, "Gradient max", s.ComboGradientMax, 100f, 5000f,
                v => { s.ComboGradientMax = v; notify?.Invoke(); }, "0", 50f);

            UIBuilder.ExpandSection(body, "Position & size", sub =>
            {
                UIBuilder.Slider(sub, "Y offset", s.ComboDisplayY, -200f, 200f,
                    v => { s.ComboDisplayY = v; notify?.Invoke(); }, "0", 1f);
                UIBuilder.Slider(sub, "Size", s.ComboDisplaySize, 0.25f, 3f,
                    v => { s.ComboDisplaySize = v; notify?.Invoke(); }, "0.00");
            });

            UIBuilder.ExpandSection(body, "Label", sub =>
            {
                UIBuilder.TextInput(sub, "Text", s.ComboDisplayText,
                    v => { s.ComboDisplayText = v; notify?.Invoke(); });
                UIBuilder.Slider(sub, "Y offset", s.ComboLabelY, -100f, 200f,
                    v => { s.ComboLabelY = v; notify?.Invoke(); }, "0", 1f);
                UIBuilder.Slider(sub, "Size", s.ComboLabelSize, 0.25f, 3f,
                    v => { s.ComboLabelSize = v; notify?.Invoke(); }, "0.00");

                UIBuilder.ExpandSection(sub, "Shadow", shadow =>
                {
                    UIBuilder.Slider(shadow, "X offset", s.ComboLabelShadowOffsetX, -20f, 20f,
                        v => { s.ComboLabelShadowOffsetX = v; notify?.Invoke(); }, "0.0");
                    UIBuilder.Slider(shadow, "Y offset", s.ComboLabelShadowOffsetY, -20f, 20f,
                        v => { s.ComboLabelShadowOffsetY = v; notify?.Invoke(); }, "0.0");

                    if (s.ComboLabelShadowColor == null)
                        s.ComboLabelShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };
                    var lc = s.ComboLabelShadowColor;
                    UIBuilder.ColorPicker(shadow, "Color",
                        new Color(lc.R, lc.G, lc.B, lc.A), true,
                        c => { lc.R = c.r; lc.G = c.g; lc.B = c.b; lc.A = c.a; notify?.Invoke(); });
                });
            });

            UIBuilder.ExpandSection(body, "Count", sub =>
            {
                UIBuilder.Slider(sub, "Size", s.ComboCountSize, 0.25f, 3f,
                    v => { s.ComboCountSize = v; notify?.Invoke(); }, "0.00");

                UIBuilder.ExpandSection(sub, "Shadow", shadow =>
                {
                    UIBuilder.Slider(shadow, "X offset", s.ComboShadowOffsetX, -20f, 20f,
                        v => { s.ComboShadowOffsetX = v; notify?.Invoke(); }, "0.0");
                    UIBuilder.Slider(shadow, "Y offset", s.ComboShadowOffsetY, -20f, 20f,
                        v => { s.ComboShadowOffsetY = v; notify?.Invoke(); }, "0.0");

                    if (s.ComboShadowColor == null)
                        s.ComboShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };
                    var cc = s.ComboShadowColor;
                    UIBuilder.ColorPicker(shadow, "Color",
                        new Color(cc.R, cc.G, cc.B, cc.A), true,
                        c => { cc.R = c.r; cc.G = c.g; cc.B = c.b; cc.A = c.a; notify?.Invoke(); });
                });
            });

            UIBuilder.ExpandSection(body, "Animations", sub =>
            {
                UIBuilder.Slider(sub, "Y offset", s.ComboPulseOffsetY, -20f, 50f,
                    v => { s.ComboPulseOffsetY = v; notify?.Invoke(); }, "0.0");
                UIBuilder.Slider(sub, "Scale", s.ComboPulseScale, 0f, 1f,
                    v => { s.ComboPulseScale = v; notify?.Invoke(); }, "0.00");
                UIBuilder.Slider(sub, "Duration", s.ComboPulseDuration, 0.05f, 1f,
                    v => { s.ComboPulseDuration = v; notify?.Invoke(); }, "0.00");
            });

            UIBuilder.GradientEditor(body, "Color", s.ComboGradient, () => notify?.Invoke());
        }
    }
}
