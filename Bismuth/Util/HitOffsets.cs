using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bismuth
{
    /* Per-hit timing offsets for the attempt, in milliseconds, captured each hit from a
       prefix on scrController.UpdateHitErrorMeter (see HitTimingPatch — NOT from the meter
       itself, which is skipped entirely when the player hides it). Feeds both the timing
       row and the timing graph, so the conversion and the sign live in one place.

       Cleared per attempt (OnAttempt), not per level — the graph shows the run you are
       playing, the way osu! shows the play you just finished. */
    internal class HitOffsets
    {
        internal struct Hit
        {
            public float Ms;            // raw, before orientation; see Flip
            public HitMargin Margin;
        }

        private readonly List<Hit> _hits = new List<Hit>(512);

        internal int Count { get { return _hits.Count; } }
        internal Hit this[int i] { get { return _hits[i]; } }
        internal bool HasLast { get; private set; }
        internal float LastRawMs { get; private set; }

        internal void Clear()
        {
            _hits.Clear();
            HasLast = false;
            _statsDirty = true;
        }

        internal void Add(float ms, HitMargin margin)
        {
            if (float.IsNaN(ms) || float.IsInfinity(ms)) return;
            _hits.Add(new Hit { Ms = ms, Margin = margin });
            LastRawMs = ms;
            HasLast = true;
            _statsDirty = true;
        }

        // Oriented so negative reads as early and positive as late.
        internal float LastMs { get { return LastRawMs * Flip; } }
        internal float MsAt(int i) { return _hits[i].Ms * Flip; }

        /* The raw angle's sign depends on the floor's direction handling, which is awkward to
           pin down statically — so derive it from the data instead. Early-family margins must
           sit on the negative side; if their mean lands positive the axis is mirrored. Costs
           one pass over the hits and only when they have changed. */
        internal float Flip { get { Rebuild(); return _flip; } }

        private bool _statsDirty = true;
        private float _flip = 1f;
        private readonly List<KeyValuePair<float, Color>> _stops = new List<KeyValuePair<float, Color>>();

        private void Rebuild()
        {
            if (!_statsDirty) return;
            _statsDirty = false;

            // Per-margin running mean, indexed by the margin's own value.
            int slots = Margins.Count;
            var sum = new double[slots];
            var num = new int[slots];
            double earlySum = 0, lateSum = 0;
            int earlyN = 0, lateN = 0;

            for (int i = 0; i < _hits.Count; i++)
            {
                var h = _hits[i];
                int mi = (int)h.Margin;
                if (mi >= 0 && mi < slots) { sum[mi] += h.Ms; num[mi]++; }

                if (h.Margin == Margins.TooEarly || h.Margin == Margins.VeryEarly
                    || h.Margin == Margins.EarlyPerfect) { earlySum += h.Ms; earlyN++; }
                else if (h.Margin == Margins.TooLate || h.Margin == Margins.VeryLate
                    || h.Margin == Margins.LatePerfect) { lateSum += h.Ms; lateN++; }
            }

            // Only re-decide orientation when both sides have evidence; otherwise keep the
            // last verdict so the axis cannot flip back and forth early in a run.
            if (earlyN > 0 && lateN > 0)
                _flip = (earlySum / earlyN) > (lateSum / lateN) ? -1f : 1f;

            /* Gradient stops: each margin's colour placed at the mean offset of the hits that
               earned it. Self-calibrating — the real thresholds move with Difficulty and each
               floor's marginScale, so anything hardcoded would be wrong on half the levels. */
            _stops.Clear();
            for (int mi = 0; mi < slots; mi++)
            {
                if (num[mi] == 0) continue;
                _stops.Add(new KeyValuePair<float, Color>(
                    (float)(sum[mi] / num[mi]) * _flip, Overlay.MarginColorFor((HitMargin)mi)));
            }
            _stops.Sort((a, b) => a.Key.CompareTo(b.Key));
        }

        // Colour for a point on the oriented ms axis: lerp between the two nearest stops,
        // clamped to the ends. Falls back to white until any hit has been seen.
        internal Color ColorAt(float ms)
        {
            Rebuild();
            if (_stops.Count == 0) return Color.white;
            if (_stops.Count == 1 || ms <= _stops[0].Key) return _stops[0].Value;
            if (ms >= _stops[_stops.Count - 1].Key) return _stops[_stops.Count - 1].Value;
            for (int i = 1; i < _stops.Count; i++)
            {
                if (ms > _stops[i].Key) continue;
                float a = _stops[i - 1].Key, b = _stops[i].Key;
                float t = b > a ? (ms - a) / (b - a) : 0f;
                return Color.Lerp(_stops[i - 1].Value, _stops[i].Value, t);
            }
            return _stops[_stops.Count - 1].Value;
        }

        /* The hit's angular offset, computed exactly as scrController.UpdateHitErrorMeter
           does: planet.cachedAngle - planet.targetExitAngle, negated on a CCW floor. Worked
           out here rather than read from the error meter because the meter's own path returns
           early when the player has it switched off — and then there is no data at all. */
        internal static bool TryAngle(scrFloor hitFloor, scrPlanet planet, out float angleDiff)
        {
            angleDiff = 0f;
            if (planet == null || hitFloor == null) return false;
            try
            {
                angleDiff = (float)(planet.cachedAngle - planet.targetExitAngle);
                if (hitFloor.isCCW) angleDiff = -angleDiff;
                return !float.IsNaN(angleDiff) && !float.IsInfinity(angleDiff);
            }
            catch { return false; }
        }

        /* Angle to milliseconds, the same conversion the game does for its own ms readout:
           ms = angle * 60000 / (PI * bpm * speed * pitch). Half a rotation (PI) at bpm B is
           one beat, which is 60000/B ms — that identity is the check that this is right.
           Every input is a public field; the game's own rawMsDiff lives in a compiler
           generated closure and is not safely reachable. */
        internal static bool TryToMs(float angleDiff, scrPlanet planet, out float ms)
        {
            ms = 0f;
            if (planet == null) return false;
            try
            {
                var conductor = planet.conductor;
                var system = planet.planetarySystem;
                if (conductor == null || system == null) return false;

                double pitch = conductor.song != null ? conductor.song.pitch : 1.0;
                double denom = Math.PI * conductor.bpm * system.speed * pitch;
                if (Math.Abs(denom) < 1e-9) return false;

                ms = (float)(angleDiff * 60000.0 / denom);
                return !float.IsNaN(ms) && !float.IsInfinity(ms);
            }
            catch { return false; }
        }
    }
}
