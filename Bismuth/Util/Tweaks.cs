namespace Bismuth
{
    // Support hooks for the "Tweaks" tab.
    internal static class Tweaks
    {
        /* KeyCode (as int) the editor's autoplay-pause check should read. A transpiler in
           Patches.cs swaps the hardcoded KeyCode.Space (32) in scnEditor.Update for a call
           to this, so the pause key is rebindable. Falls back to Space so behaviour is
           unchanged when settings aren't loaded yet. */
        internal static int AutoPauseKeyCode()
        {
            try
            {
                var s = MainClass.Settings;
                if (s == null) return (int)UnityEngine.KeyCode.Space;
                // Disabled → return None, which Input.GetKeyDown never reports, so the pause
                // never fires (checked live each frame, so the toggle takes effect at once).
                if (!s.AutoplayPauseEnabled) return (int)UnityEngine.KeyCode.None;
                return (int)s.AutoplayPauseKey;
            }
            catch { return (int)UnityEngine.KeyCode.Space; }
        }
    }
}
