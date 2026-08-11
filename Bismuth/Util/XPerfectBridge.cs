using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Bismuth
{
    /* Soft read-only bridge to the XPerfect mod, which splits HitMargin.Perfect into
       XPerfect / +Perfect / -Perfect. We read its counters rather than re-deriving the
       split: it owns the timing thresholds, and mirroring them would drift the moment it
       retunes. Absent mod → Available is false and nothing in the overlay changes.

       Resolved once — the loaded mod set can't change mid-session, and a failed lookup
       must not re-reflect every frame. */
    internal static class XPerfectBridge
    {
        private static bool _probed;
        private static MethodInfo _x, _plus, _minus;

        // XPerfect's own counter colors, so the columns read as that mod's feature.
        internal static readonly Color XColor    = new Color32(0x4D, 0xCC, 0xFF, 0xFF);
        internal static readonly Color PlusColor = new Color32(0x60, 0xFF, 0x4E, 0xFF);

        internal static bool Available
        {
            get { Probe(); return _x != null && _plus != null && _minus != null; }
        }

        /* A NEGATIVE probe must not latch: UMM can enable XPerfect after Bismuth has already
           built its judgement row, and the row is built once. Callers re-arm on settings
           apply; a positive result sticks (the mod can't unload). */
        internal static void Rescan()
        {
            if (_x == null) _probed = false;
        }

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            var t = AccessTools.TypeByName("XPerfect.AccuracyState");
            if (t == null) return;
            _x     = AccessTools.PropertyGetter(t, "XPerfectCount");
            _plus  = AccessTools.PropertyGetter(t, "PlusPerfectCount");
            _minus = AccessTools.PropertyGetter(t, "MinusPerfectCount");
            BismuthLog.Log(Available
                ? "XPerfect detected — Perfect judgement column splits into X / + / -"
                : "XPerfect found but its counters didn't resolve — Perfect column stays merged");
        }

        internal static int XPerfect     => Read(_x);
        internal static int PlusPerfect  => Read(_plus);
        internal static int MinusPerfect => Read(_minus);

        private static int Read(MethodInfo getter)
        {
            if (getter == null) return 0;
            try { return (int)getter.Invoke(null, null); }
            catch { return 0; }
        }
    }
}
