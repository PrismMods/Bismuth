using UnityEngine;

namespace Bismuth
{
    // Support hooks for the Misc tab's custom-level section. (The editor helpers — autoplay pause key, tile
    // angle, Editor Mode — moved to the Sapphire mod.)
    internal static class Tweaks
    {
        // Live-apply for the CLS preview-volume slider: the fade postfix only rescales
        // future volume writes, so retune any preview that's already playing steady.
        internal static void ApplyClsPreviewVolume()
        {
            try
            {
                var s = MainClass.Settings;
                if (s == null) return;
                var p = Object.FindAnyObjectByType<PreviewSongPlayer>();
                if (p != null && p.playing && p.audioSource != null)
                    p.audioSource.volume = Mathf.Clamp01(s.ClsPreviewVolume);
            }
            catch { }
        }
    }
}
