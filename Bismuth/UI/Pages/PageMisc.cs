using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static class PageMisc
    {
        // Updated from MainClass.OnSceneUnloaded so the readout stays live while the
        // panel is open (IMGUI re-read it every draw; uGUI text is built once).
        private static TextMeshProUGUI _savingsText;

        public static void Build(RectTransform content)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeader(content, "Misc");

            var savingsRow = UIBuilder.Row(content);
            _savingsText = UIBuilder.Label(savingsRow.transform, SavingsLabel(), (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.TextMuted);
            _savingsText.rectTransform.offsetMin = new Vector2(8f, 0f);

            UIBuilder.Button(content, "View log", LogViewer.Show);

            // Debug sits directly under View log — its dumps/sweep traces write to that log.
            BuildDebug(content, s, notify);

            UIBuilder.Spacer(content);
            BuildOptimizations(content, s, notify);
        }

        // Optimizations collapse under one dropdown whose header radio is the master switch
        // (Settings.OptimizationsEnabled gates every flag below it). Collapsed by default.
        private static void BuildOptimizations(RectTransform content, Settings s, System.Action notify)
        {
            UIBuilder.Collapsible(content, "Optimizations", s.OptimizationsEnabled,
                v => { s.OptimizationsEnabled = v; notify?.Invoke(); },
                body =>
                {
                    UIBuilder.Collapsible(body, "Spectrum Throttle (every 2nd frame)", s.OptSpectrumThrottle,
                        v => { s.OptSpectrumThrottle = v; notify?.Invoke(); }, null);
                    Desc(body, "Halves AudioSource.GetSpectrumData FFT cost on levels that use audio visualization.");

                    UIBuilder.Collapsible(body, "Texture Non-Readable", s.OptTextureNonReadable,
                        v => { s.OptTextureNonReadable = v; notify?.Invoke(); }, null);
                    Desc(body, "Frees CPU-side pixel data after GPU upload. Halves RAM per custom level texture.");

                    UIBuilder.Collapsible(body, "DXT Compression (lossy)", s.OptTextureDXT,
                        v => { s.OptTextureDXT = v; notify?.Invoke(); }, null);
                    Desc(body, "Compresses textures to DXT before upload. 4-6x VRAM savings, slight quality loss. Requires Non-Readable.");

                    UIBuilder.Collapsible(body, "Physics NonAlloc", s.OptPhysicsNonAlloc,
                        v => { s.OptPhysicsNonAlloc = v; notify?.Invoke(); }, null);
                    Desc(body, "Eliminates per-frame Collider2D[] allocation from decoration hitbox checks.");

                    UIBuilder.Collapsible(body, "Unload Assets on Scene Change", s.OptUnloadAssets,
                        v => { s.OptUnloadAssets = v; notify?.Invoke(); }, null);
                    Desc(body, "Forces GC and unloads unused textures/audio between levels to reclaim memory.");

                    UIBuilder.Collapsible(body, "Volume Track DOTween Fix", s.OptVolumeTrackDOTween,
                        v => { s.OptVolumeTrackDOTween = v; notify?.Invoke(); }, null);
                    Desc(body, "Prevents abandoned DOTween sequences from being created every frame on Volume-type track colors.");
                });
        }

        // Developer tools, revealed by the Debug mode toggle. Polls live game objects /
        // assets and dumps their references to the log (Misc → View log) — see GameProbe.
        private static void BuildDebug(RectTransform content, Settings s, System.Action notify)
        {
            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Debug");

            GameObject tools = null;
            UIBuilder.Toggle(content, "Debug mode", s.DebugMode, v =>
            {
                s.DebugMode = v;
                if (tools != null) tools.SetActive(v);
                notify?.Invoke();
            });

            tools = UIBuilder.Rect("DebugTools", content);
            var vlg = tools.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            var t = tools.transform;

            // Re-scan fonts (pick up newly dropped Fonts/ files), rebuild the panel, and
            // reapply everything — a soft reload without the UMM Ctrl+F10.
            UIBuilder.Button(t, "Force reload", MainClass.RequestForceReload);

            UIBuilder.TextInput(t, "Filter", GameProbe.Filter, v =>
            {
                GameProbe.Filter = v ?? "";
                if (GameFontApplier.DiagEnabled)
                    GameFontApplier.DiagFilter = string.IsNullOrEmpty(GameProbe.Filter) ? null : new[] { GameProbe.Filter };
            });
            UIBuilder.Button(t, "Dump texts", GameProbe.DumpTexts);
            UIBuilder.Button(t, "Dump images", GameProbe.DumpImages);
            UIBuilder.Button(t, "Dump assets (sprites/textures)", GameProbe.DumpAssets);

            string compType = "";
            UIBuilder.TextInput(t, "Component type", compType, v => compType = v);
            UIBuilder.Button(t, "Dump components", () => GameProbe.DumpComponents(compType));

            UIBuilder.Toggle(t, "Trace font sweep", GameFontApplier.DiagEnabled, v =>
            {
                GameFontApplier.DiagEnabled = v;
                GameFontApplier.DiagFilter = string.IsNullOrEmpty(GameProbe.Filter) ? null : new[] { GameProbe.Filter };
            });

            tools.SetActive(s.DebugMode);
        }

        public static void RefreshSavings()
        {
            if (_savingsText != null) _savingsText.text = SavingsLabel();
        }

        private static string SavingsLabel()
        {
            string savings;
            long bytes = MainClass.LastUnloadSavingsBytes;
            if (bytes < 0) savings = "----MB";
            else
            {
                float mb = bytes / (1024f * 1024f);
                savings = (mb >= 0f ? "+" : "") + mb.ToString("F2") + " MB";
            }
            return "RAM savings (last scene load): " + savings;
        }

        // Wrapping muted caption under a toggle. The page VLG controls child rects, so the
        // indent comes from a padded wrapper group rather than offsetMin; Wrap + the inner
        // group's childControlHeight lets the Text's preferred height drive the row height.
        private static void Desc(Transform parent, string text)
        {
            var wrap = UIBuilder.Rect("Desc", parent);
            var vlg = wrap.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(10, 4, 0, 6);

            var t = UIBuilder.Label(wrap.transform, text, (int)UIBuilder.LabelFontSize - 2, TextAnchor.UpperLeft, Theme.TextMuted);
            t.textWrappingMode = TextWrappingModes.Normal;
        }
    }
}
