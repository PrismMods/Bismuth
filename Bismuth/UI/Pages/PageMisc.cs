using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static class PageMisc
    {
        // Updated from MainClass.OnSceneUnloaded so the readout stays live while the
        // panel is open (IMGUI re-read it every draw; uGUI text is built once).
        private static Text _savingsText;

        public static void Build(RectTransform content)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeader(content, "Misc");

            var savingsRow = UIBuilder.Row(content);
            _savingsText = UIBuilder.Label(savingsRow.transform, SavingsLabel(), (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.TextMuted);
            _savingsText.rectTransform.offsetMin = new Vector2(8f, 0f);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Optimizations");

            UIBuilder.Collapsible(content, "Spectrum Throttle (every 2nd frame)", s.OptSpectrumThrottle,
                v => { s.OptSpectrumThrottle = v; notify?.Invoke(); }, null);
            Desc(content, "Halves AudioSource.GetSpectrumData FFT cost on levels that use audio visualization.");

            UIBuilder.Collapsible(content, "Texture Non-Readable", s.OptTextureNonReadable,
                v => { s.OptTextureNonReadable = v; notify?.Invoke(); }, null);
            Desc(content, "Frees CPU-side pixel data after GPU upload. Halves RAM per custom level texture.");

            UIBuilder.Collapsible(content, "DXT Compression (lossy)", s.OptTextureDXT,
                v => { s.OptTextureDXT = v; notify?.Invoke(); }, null);
            Desc(content, "Compresses textures to DXT before upload. 4-6x VRAM savings, slight quality loss. Requires Non-Readable.");

            UIBuilder.Collapsible(content, "Physics NonAlloc", s.OptPhysicsNonAlloc,
                v => { s.OptPhysicsNonAlloc = v; notify?.Invoke(); }, null);
            Desc(content, "Eliminates per-frame Collider2D[] allocation from decoration hitbox checks.");

            UIBuilder.Collapsible(content, "Unload Assets on Scene Change", s.OptUnloadAssets,
                v => { s.OptUnloadAssets = v; notify?.Invoke(); }, null);
            Desc(content, "Forces GC and unloads unused textures/audio between levels to reclaim memory.");

            UIBuilder.Collapsible(content, "Volume Track DOTween Fix", s.OptVolumeTrackDOTween,
                v => { s.OptVolumeTrackDOTween = v; notify?.Invoke(); }, null);
            Desc(content, "Prevents abandoned DOTween sequences from being created every frame on Volume-type track colors.");
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
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
