using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Bismuth
{
    /* Bismuth's side of PrismLib (github.com/PrismMods/PrismLib).

       Two things here were costing us silently. Bismuth blocks the keyboard game-wide while its
       panel is open, which kills every Sapphire hotkey for as long as the panel is up; and Sapphire
       hides the gameplay HUD in Editor Mode, which it used to tell us through a reflection poke at
       Settings.ExternalEditorSuppress. Both are now claims, so either mod can see the other.

       EVERY PrismLib type stays inside this file, in METHOD BODIES only, and every method here that
       touches one is called solely after Available has been checked. Two separate rules, both
       load-bearing on a machine where the library never arrived — which is exactly the machine that
       must keep working:

         - No PrismLib type may appear in a FIELD, because field types are part of the class layout
           and are resolved when the type itself loads, before any method runs. A `ModHandle _me`
           field made this whole class unloadable and took the mod down with it:
           "TypeLoadException - Could not load type of field 'Bismuth.PrismBridge:_me'". Hence the
           object fields and the casts.
         - No caller outside this file may mention a PrismLib type, because the CLR resolves a
           method's types when that method is JITted. */
    internal static class PrismBridge
    {
        internal static bool Available { get; private set; }

        internal static void Init(string version)
        {
            try
            {
                if (!PrismLib.Bootstrap.PrismBootstrap.Ensure(BismuthLog.Log, new Version(0, 2, 0))) return;
                Wire(version);
                Available = true;
            }
            catch (Exception e) { BismuthLog.Log("PrismLib unavailable: " + e.Message); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Wire(string version)
        {
            Version v;
            Version.TryParse((version ?? "0.0.0").Split('-')[0], out v);
            var me = PrismLib.Prism.Register("Bismuth", v ?? new Version(0, 0, 0));
            _me = me;
            PrismLib.Prism.Log = BismuthLog.Log;
            PrismLib.Keys.KeyName = i => ((KeyCode)i).ToString();
            me.BindKey("SettingsPanel", (int)KeyCode.B, PrismLib.KeyMods.Ctrl, "Open the settings panel");
        }

        // Opaque on purpose — see the class comment. Cast at use, never in the field type.
        private static object _me;
        private static object _input;

        /* Held while KeyLimiter is swallowing keys game-wide. Driven from OnUpdate rather than from
           the BlockInputs property, which is read several times a frame from Harmony postfixes. */
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SyncInputClaim(bool blocking)
        {
            try
            {
                var me = _me as PrismLib.ModHandle;
                if (blocking) { if (_input == null && me != null) _input = me.ClaimState(PrismLib.StateKey.InputCapture, "panel open"); }
                else if (_input != null) { ((PrismLib.Claim)_input).Release(); _input = null; }
            }
            catch { }
        }

        // Cached per frame: the HUD readers below are properties consulted many times a frame.
        private static int _hudFrame = -1;
        private static bool _hudHeld;

        /// True while another mod owns the gameplay HUD (Sapphire's Editor Mode does), so Bismuth's
        /// overlays stand down instead of drawing over it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool HudHeldElsewhere()
        {
            try
            {
                if (Time.frameCount == _hudFrame) return _hudHeld;
                _hudFrame = Time.frameCount;
                var owner = PrismLib.Claims.OwnerOf(PrismLib.StateKey.GameHud);
                return _hudHeld = owner != null && owner != "Bismuth";
            }
            catch { return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Shutdown()
        {
            if (!Available) return;
            Available = false;
            try { _input = null; PrismLib.Claims.ReleaseAll("Bismuth"); PrismLib.Keys.Unregister("Bismuth"); }
            catch { }
        }
    }
}
