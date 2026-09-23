using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Bismuth
{
    /* Every HitMargin value Bismuth names, resolved BY NAME at startup instead of referenced
       as a compile-time constant.

       The reason: an enum member compiles to its integer, and this enum gets renumbered. The
       alpha that absorbed XPerfect split HitMargin.Perfect into PerfectMinus / XPerfect /
       PerfectPlus, which shifted everything after it by +2 — a build against 3.3.1 counts
       FailMiss (8) as the alpha's TooLate, silently, with no patch error and no crash. Going
       through here means one dll counts correctly on both, and the next renumber costs
       nothing.

       So: no `HitMargin.X` member reference anywhere else in Bismuth. Naming the *type*
       (patch signatures, parameters) is fine — only members carry baked values. */
    internal static class Margins
    {
        internal static readonly HitMargin TooEarly     = Get("TooEarly");
        internal static readonly HitMargin VeryEarly    = Get("VeryEarly");
        internal static readonly HitMargin EarlyPerfect = Get("EarlyPerfect");
        internal static readonly HitMargin LatePerfect  = Get("LatePerfect");
        internal static readonly HitMargin VeryLate     = Get("VeryLate");
        internal static readonly HitMargin TooLate      = Get("TooLate");
        internal static readonly HitMargin Multipress   = Get("Multipress");
        internal static readonly HitMargin OverPress    = Get("OverPress");
        internal static readonly HitMargin FailMiss     = Get("FailMiss");
        internal static readonly HitMargin FailOverload = Get("FailOverload");
        internal static readonly HitMargin Auto         = Get("Auto");
        // Auto-resolved tiles: the player never presses for these, so they neither extend a
        // combo nor break one. Midspin breaking it was the bug — it killed a 273 chain.
        internal static readonly HitMargin Midspin      = Get("Midspin");

        /* The perfect band. On a build that splits it these are three distinct values; on one
           that doesn't, all three are the single Perfect and the band has one entry. Callers
           that just ask "was this a perfect?" should use IsPerfect and not care. */
        internal static readonly bool SplitPerfect;
        internal static readonly HitMargin PerfectMinus, XPerfect, PerfectPlus;
        // Early → late, matching the enum's own layout and the hit error meter above the row.
        internal static readonly HitMargin[] PerfectBand;

        // Slots needed for a per-margin count array (highest value + 1, not the member count —
        // the enum is dense today but nothing guarantees that).
        internal static readonly int Count;

        static Margins()
        {
            var merged = Get("Perfect");                 // absent once the split lands
            SplitPerfect = TryGet("XPerfect", out XPerfect);
            if (SplitPerfect)
            {
                PerfectMinus = Get("PerfectMinus", XPerfect);
                PerfectPlus  = Get("PerfectPlus", XPerfect);
                PerfectBand  = new[] { PerfectMinus, XPerfect, PerfectPlus };
            }
            else
            {
                PerfectMinus = XPerfect = PerfectPlus = merged;
                PerfectBand  = new[] { merged };
            }

            int max = 0;
            foreach (HitMargin v in Enum.GetValues(typeof(HitMargin)))
                if ((int)v > max) max = (int)v;
            Count = max + 1;

            BismuthLog.Log(SplitPerfect
                ? $"HitMargin: game splits Perfect (X={(int)XPerfect}, -={(int)PerfectMinus}, +={(int)PerfectPlus}), {Count} slots"
                : $"HitMargin: merged Perfect={(int)merged}, {Count} slots");
        }

        /* Does this margin extend a combo? Strict mode wants an exact XPerfect and treats
           the signed perfects as breaks; it can only mean anything where the split exists.
           Both the live counter and the checkpoint rebuild go through here — they disagreed
           once and a revive silently reported a different combo than the play did. */
        internal static bool ExtendsCombo(HitMargin m)
        {
            if (SplitPerfect && MainClass.Settings != null && MainClass.Settings.ComboXPerfectOnly)
                return m == XPerfect;
            return IsPerfect(m);
        }

        /* Neutral to a combo: neither extends nor breaks. Only the auto-resolved tiles, and
           deliberately NOT the game's IsPlayerHitMargin — that also excludes Multipress and
           OverPress, which are real mistakes and must break the chain. */
        internal static bool IsComboNeutral(HitMargin m)
            => (m == Auto && Auto != None) || (m == Midspin && Midspin != None);

        /* Margins that come from an actual timed press, early to late. Excludes fails, auto,
           midspin, multipress and overpress: none of those carry a meaningful offset, and a
           miss at an extreme angle would stretch a timing graph's axis over nothing. */
        internal static bool IsTimed(HitMargin m)
            => IsPerfect(m)
            || m == TooEarly || m == VeryEarly || m == EarlyPerfect
            || m == LatePerfect || m == VeryLate || m == TooLate;

        internal static bool IsPerfect(HitMargin m)
        {
            for (int i = 0; i < PerfectBand.Length; i++)
                if (PerfectBand[i] == m) return true;
            return false;
        }

        /* An unresolved name falls back to -1, NOT default(HitMargin) — default is 0, which
           is TooEarly, so a name this game build doesn't have would silently alias a real
           margin and every `m == Margins.Whatever` test against it would fire on genuine
           TooEarly hits. -1 can never equal a real value. */
        internal const HitMargin None = (HitMargin)(-1);

        private static HitMargin Get(string name) => Get(name, None);

        private static HitMargin Get(string name, HitMargin fallback)
            => TryGet(name, out HitMargin v) ? v : fallback;

        private static bool TryGet(string name, out HitMargin value)
        {
            // Enum.TryParse also accepts numeric strings; these are all names, so that can't bite.
            try { return Enum.TryParse(name, out value); }
            catch { value = default(HitMargin); return false; }
        }

        /* colourXPerfect only exists on builds that have the split, so it's read by reflection
           rather than referenced — the same dll has to load against a game without the field.
           Falls back to the XPerfect mod's own blue, which is where the colour came from. */
        private static FieldInfo _xColourField;
        private static bool _xColourProbed;

        internal static Color XPerfectColour(ColourSchemeHitMargin scheme)
        {
            if (!_xColourProbed)
            {
                _xColourProbed = true;
                _xColourField = AccessTools.Field(typeof(ColourSchemeHitMargin), "colourXPerfect");
            }
            if (_xColourField != null && scheme != null)
            {
                try { return (Color)_xColourField.GetValue(scheme); }
                catch { }
            }
            return XPerfectBridge.XColor;
        }
    }
}
