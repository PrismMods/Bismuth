using System;

namespace Bismuth
{
    /* The fields Bismuth's custom results screen can draw, mirroring what the game's own
       DetailedResults readout contains. Each is an independently placeable element.

       Values come from Bismuth's own per-attempt counters and the game's margin tracker —
       nothing parses the game's generated string, which changes shape with the perfect-text
       preset and the classic-experience setting.

       Default positions lay the fields out in three columns, roughly the game's grouping.
       They are only defaults: a field the player has moved stores its own X/Y. */
    internal static class ResultsFields
    {
        internal sealed class FieldDef
        {
            public string Key;
            public string Label;
            public float X, Y;                      // default normalized anchor
            public Func<Overlay, string> Value;     // null-safe; returns "" when unavailable
            // Judgement fields default to the game's own colour for that margin; anything
            // else defaults to white. An explicit per-field colour always wins.
            public Func<UnityEngine.Color?> DefaultColor;
        }

        private const float ColL = 0.40f, ColM = 0.50f, ColR = 0.60f;

        internal static readonly FieldDef[] All =
        {
            // Perfect band, early → late, the order the enum itself uses.
            Margin("perfectM",      "-Perfect",      ColL, 0.62f, () => Margins.PerfectMinus),
            Margin("xPerfect",      "XPerfect",      ColM, 0.62f, () => Margins.XPerfect),
            Margin("perfectP",      "+Perfect",      ColR, 0.62f, () => Margins.PerfectPlus),

            Margin("ePerfect",      "Early Perfect", ColL, 0.56f, () => Margins.EarlyPerfect),
            Margin("lPerfect",      "Late Perfect",  ColR, 0.56f, () => Margins.LatePerfect),

            Margin("early",         "Early",         ColL, 0.50f, () => Margins.VeryEarly),
            Margin("late",          "Late",          ColR, 0.50f, () => Margins.VeryLate),

            Margin("tooEarly",      "Too Early",     ColL, 0.44f, () => Margins.TooEarly),
            Margin("tooLate",       "Too Late",      ColR, 0.44f, () => Margins.TooLate),

            Margin("missFails",     "Miss Fails",     ColL, 0.38f, () => Margins.FailMiss),
            Margin("overloadFails", "Overload Fails", ColM, 0.38f, () => Margins.FailOverload),
            new FieldDef { Key = "deaths", Label = "Deaths", X = ColR, Y = 0.38f,
                Value = o => Tracker(t => t.deaths.ToString()) },

            new FieldDef { Key = "accuracy", Label = "Accuracy", X = ColL, Y = 0.32f,
                Value = o => Tracker(t => (t.percentAcc * 100f).ToString("0.00") + "%") },
            new FieldDef { Key = "xAccuracy", Label = "X-Accuracy", X = ColM, Y = 0.32f,
                Value = o => Tracker(t => (t.percentXAcc * 100f).ToString("0.00") + "%") },
            new FieldDef { Key = "xScore", Label = "X-Score", X = ColR, Y = 0.32f,
                Value = o => Tracker(t => t.xScore.ToString()) },

            new FieldDef { Key = "checkpoints", Label = "Checkpoints", X = ColL, Y = 0.26f,
                Value = o => Safe(() => scrController.checkpointsUsed.ToString()) },
            new FieldDef { Key = "maximumUsedKeys", Label = "Max Used Keys", X = ColM, Y = 0.26f,
                Value = o => Safe(() => scrController.instance != null
                    ? scrController.instance.maximumUsedKeys.ToString() : "") },
            // The game only shows this in practice mode; Bismuth's own counter is the source,
            // since it already tracks attempts per level and the game's is not exposed.
            new FieldDef { Key = "practiceAttempts", Label = "Attempts", X = ColR, Y = 0.26f,
                Value = o => o != null ? o.AttemptCount.ToString() : "" },
        };

        internal static FieldDef Find(string key)
        {
            foreach (var f in All)
                if (string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        /* A judgement count. The margin is resolved through a lambda rather than captured by
           value because Margins resolves at startup and this array is a static initializer —
           the field values are not necessarily ready when this runs. */
        private static FieldDef Margin(string key, string label, float x, float y, Func<HitMargin> margin)
            => new FieldDef
            {
                Key = key, Label = label, X = x, Y = y,
                Value = o => o != null ? o.JudgementCount(margin()).ToString() : "",
                DefaultColor = () =>
                {
                    try { return Overlay.MarginColorFor(margin()); }
                    catch { return null; }
                },
            };

        private static string Tracker(Func<scrMarginTracker, string> read) => Safe(() =>
        {
            var trackers = scrMistakesManager.marginTrackers;
            var t = trackers != null && trackers.Length > 0 ? trackers[0] : null;
            return t != null ? read(t) : "";
        });

        // The results screen is a place where half the game is mid-teardown; a field that
        // cannot read its source shows nothing rather than taking the whole screen down.
        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; }
            catch { return ""; }
        }
    }
}
