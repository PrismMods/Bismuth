using System;
using System.Collections.Generic;
using ADOFAI;
using HarmonyLib;

namespace Bismuth
{
    /* Ported from the standalone BetterCountdown mod (2026-10-06), which did nothing else and
       is retired by this file.

       When a run restarts from a checkpoint the game replays the few tiles before it at the
       level's own tempo. Past ~600 BPM those ticks are too fast to read, let alone use. This
       multiplies the lead-in tiles' speed by a power of two until the countdown tick tempo
       lands inside the chosen range, then pulls the audio seek back by however much that
       shifted the timeline: the song is never re-pitched and the first real hit still falls on
       its real beat.

       Level-start countdowns are untouched — the game already adapts those via
       adjustedCountdownTicks.

       The patches are always installed, like the rest of Bismuth's; the feature is gated on the
       setting instead, so turning it off has to put the tiles back (see Enabled). */

    // Speed the lead-in tiles up by a power of two so the countdown ticks land in a readable
    // tempo range, then pull the audio seek back by however much that shifted the timeline —
    // the song is never re-pitched, and the first hit still falls on its real beat.
    internal static class Countdown
    {

        /* The standalone mod toggled its whole Harmony patch set. Here the patches live with
           every other one, so the setting is the gate — and flipping it off has to restore the
           tiles it stretched, or a level stays stretched until the scene reloads. */
        internal static bool Enabled =>
            MainClass.Settings != null && MainClass.Settings.CountdownSlowdown;

        internal static void OnSettingChanged()
        {
            if (!Enabled) RestoreSpeeds();
        }
        private struct FloorState
        {
            public int Index;
            public float Speed;
            public float ExtraBeats;
            public int HoldLength;
        }

        private static readonly List<FloorState> ModifiedFloors = new List<FloorState>();
        private static List<scrFloor> stretchedFloorsRef;
        private static bool audioShiftApplied;

        internal static double TimelineShift { get; private set; }
        internal static bool SpeedOverrideActive { get; private set; }
        internal static bool AttemptPending { get; set; }
        internal static int PendingCheckpoint { get; set; } = -1;

        internal static void RestoreSpeeds()
        {
            if (ModifiedFloors.Count == 0) return;
            scrLevelMaker maker = scrLevelMaker.instance;
            List<scrFloor> floors = maker == null ? null : maker.listFloors;
            if (maker != null && floors != null && ReferenceEquals(floors, stretchedFloorsRef))
            {
                foreach (FloorState state in ModifiedFloors)
                {
                    if (state.Index >= floors.Count) continue;
                    scrFloor floor = floors[state.Index];
                    floor.speed = state.Speed;
                    floor.extraBeats = state.ExtraBeats;
                    floor.holdLength = state.HoldLength;
                }
                maker.CalculateFloorEntryTimes();
                RebakeFxStartTimes(floors);
            }
            ClearBookkeeping();
        }

        internal static void ClearBookkeeping()
        {
            ModifiedFloors.Clear();
            stretchedFloorsRef = null;
            SpeedOverrideActive = false;
            TimelineShift = 0.0;
            audioShiftApplied = false;
        }

        internal static void ApplyStretch(int checkpoint = -1)
        {
            try
            {
                RestoreSpeeds();
                Stretch(checkpoint);
            }
            catch (Exception e)
            {
                BismuthLog.Log("[countdown] " + "stretch failed: " + e);
                try { RestoreSpeeds(); } catch (Exception inner) { BismuthLog.Log("[countdown] " + "restore failed: " + inner); }
                ClearBookkeeping();
            }
        }

        private static void Stretch(int checkpoint)
        {
            int cp = checkpoint >= 0
                ? checkpoint
                : (PendingCheckpoint >= 0 ? PendingCheckpoint : GCS.checkpointNum);
            if (!Countdown.Enabled || cp == 0) return;

            scrConductor conductor = scrConductor.instance;
            scrLevelMaker maker = scrLevelMaker.instance;
            List<scrFloor> floors = maker == null ? null : maker.listFloors;
            if (conductor == null || conductor.song == null || maker == null || floors == null) return;
            if (cp <= 0 || cp >= floors.Count - 1) return;

            scrFloor cpFloor = floors[cp];
            double tickBpm = (double)conductor.bpm
                * cpFloor.speed
                * (cpFloor.numPlanets * 0.5f)
                * conductor.song.pitch
                * conductor.countdownSpeedMultiplier;
            if (tickBpm <= 0.0 || double.IsNaN(tickBpm) || double.IsInfinity(tickBpm)) return;

            double factor = Pow2.Factor(tickBpm, MainClass.Settings.CountdownMinBpm, Math.Max(MainClass.Settings.CountdownMinBpm, MainClass.Settings.CountdownMaxBpm));
            if (factor == 1.0) return;

            int firstStretched = Math.Max(1, cp - 5);
            maker.CalculateFloorEntryTimes();
            double firstHitEntryOld = floors[cp + 1].entryTime;

            for (int i = firstStretched; i <= cp; i++)
            {
                scrFloor floor = floors[i];
                ModifiedFloors.Add(new FloorState
                {
                    Index = i,
                    Speed = floor.speed,
                    ExtraBeats = floor.extraBeats,
                    HoldLength = floor.holdLength,
                });
                floor.speed *= (float)factor;
                // Pauses, free-roam and holds are counted in beats, so they have to scale with
                // the tiles or they'd play back at the stretched tempo instead of their own.
                if (floor.extraBeats > 0f) floor.extraBeats *= (float)factor;
                if (floor.holdLength <= 0) continue;
                floor.holdLength = Math.Max(0, (int)Math.Round(floor.holdLength * factor, MidpointRounding.AwayFromZero));
            }

            maker.CalculateFloorEntryTimes();
            RebakeFxStartTimes(floors);

            TimelineShift = floors[cp + 1].entryTime - firstHitEntryOld;
            stretchedFloorsRef = floors;
            SpeedOverrideActive = true;
            audioShiftApplied = false;

            BismuthLog.Log("[countdown] " + string.Format("clamp: {0:0.#} BPM -> {1:0.#} BPM (speed x{2} on tiles {3}-{4}, timeline +{5:0.###}s)",
                tickBpm, tickBpm * factor, factor, firstStretched, cp, TimelineShift));
        }

        // Consumed once per scrub: how far back the audio seek has to move to cancel the stretch.
        internal static double ClaimAudioShift(scrConductor conductor, double scrubTime)
        {
            if (audioShiftApplied || !SpeedOverrideActive || TimelineShift == 0.0) return 0.0;
            if (conductor == null || conductor.song == null || conductor.song.clip == null) return 0.0;
            float pitch = conductor.song.pitch;
            if (pitch <= 0f || float.IsNaN(pitch) || float.IsInfinity(pitch)) return 0.0;
            audioShiftApplied = true;

            double countdownLead =
                conductor.separateCountdownTime ? conductor.crotchetAtStart * conductor.countdownTicks : 0.0;
            double baseSongTime = scrubTime + conductor.addoffset - countdownLead;
            double limit = Math.Max(0.0, conductor.song.clip.length - 0.01);
            double target = Math.Min(limit, Math.Max(0.0, baseSongTime - TimelineShift));
            double shift = baseSongTime - target;

            // Near the very start of the clip there may not be enough song left to pull back into.
            double residual = TimelineShift - shift;
            if (Math.Abs(residual) > 0.0005)
                BismuthLog.Log("[countdown] " + string.Format("audio pull-back clamped inside the clip: moved {0:0.###}s of {1:0.###}s ({2:0.###}s residual desync)",
                    shift, TimelineShift, residual));
            return shift;
        }

        // The scrub moved the audio only; walk the logical clock back by the same amount.
        internal static void RestoreLogicalClock(scrConductor conductor, double shift)
        {
            if (conductor == null || conductor.song == null) return;
            float pitch = conductor.song.pitch;
            if (pitch <= 0f || float.IsNaN(pitch) || float.IsInfinity(pitch)) return;
            conductor.dspTimeSong -= shift / pitch;
            if (ADOBase.playerManager == null) return;
            foreach (scrPlayer player in ADOBase.playerManager)
                if (player != null) player.lastHit += shift;
        }

        internal static void OnScrubCompleted()
        {
            if (SpeedOverrideActive) AttemptPending = false;
        }

        private static void RebakeFxStartTimes(List<scrFloor> floors)
        {
            scrConductor conductor = scrConductor.instance;
            if (conductor == null) return;
            foreach (scrFloor floor in floors)
                foreach (ffxPlusBase fx in floor.GetComponents<ffxPlusBase>())
                    try { fx.SetStartTime(conductor.bpm, fx.degreeOffset); } catch (Exception) { }
        }

    }

    internal static class CountdownPatches
    {
        [HarmonyPatch(typeof(scrController), nameof(scrController.Start_Rewind))]
        internal static class StartRewindPatch
        {
            private static void Postfix()
            {
                Countdown.PendingCheckpoint = -1;
                if (GCS.checkpointNum == 0) return;
                Countdown.AttemptPending = Countdown.Enabled;
                Countdown.ApplyStretch(GCS.checkpointNum);
            }
        }

        [HarmonyPatch(typeof(scrController), nameof(scrController.Scrub))]
        internal static class ScrubPatch
        {
            private static void Postfix() { Countdown.OnScrubCompleted(); }
        }

        [HarmonyPatch(typeof(scrConductor), nameof(scrConductor.ScrubMusicToTime))]
        internal static class ScrubMusicPatch
        {
            private static void Prefix(scrConductor __instance, ref double newTime, out double __state)
            {
                __state = Countdown.ClaimAudioShift(__instance, newTime);
                newTime -= __state;
            }

            private static void Postfix(scrConductor __instance, double __state)
            {
                if (__state != 0.0) Countdown.RestoreLogicalClock(__instance, __state);
            }
        }

        [HarmonyPatch(typeof(scrController), "WaitForStartCo")]
        internal static class WaitForStartPatch
        {
            private static void Prefix()
            {
                if (GCS.checkpointNum == 0) return;
                Countdown.AttemptPending = Countdown.Enabled;
                Countdown.ApplyStretch();
            }
        }

        [HarmonyPatch(typeof(scnGame), "Play")]
        internal static class PlayPatch
        {
            private static void Prefix(int seqID)
            {
                Countdown.PendingCheckpoint = seqID;
                Countdown.AttemptPending = Countdown.Enabled && seqID != 0;
            }
        }

        // Rebuilding the floors throws away the stretch; re-apply it once the fresh list is up.
        [HarmonyPatch(typeof(scnGame), "ApplyEventsToFloors",
            new[] { typeof(List<scrFloor>), typeof(LevelData), typeof(scrLevelMaker), typeof(List<LevelEvent>) })]
        internal static class ApplyEventsPatch
        {
            private static void Postfix()
            {
                Countdown.RestoreSpeeds();
                Countdown.ClearBookkeeping();
                if (Countdown.AttemptPending) Countdown.ApplyStretch();
            }
        }

        [HarmonyPatch(typeof(scrConductor), "SetupConductorWithLevelData")]
        internal static class SetupConductorPatch
        {
            private static void Postfix()
            {
                Countdown.RestoreSpeeds();
                Countdown.AttemptPending = false;
                Countdown.PendingCheckpoint = -1;
            }
        }
    }

    internal static class Pow2
    {
        // Nearest power of two that lands bpm inside [min, max]; ties go to fewer doublings.
        // Nothing fits (range narrower than an octave) -> the factor that gets closest.
        internal static double Factor(double bpm, double min, double max)
        {
            double bestFactor = 1.0;
            double bestDistance = double.MaxValue;
            int bestSteps = int.MaxValue;
            for (int k = -12; k <= 12; k++)
            {
                double factor = Math.Pow(2.0, k);
                double result = bpm * factor;
                double distance = result < min ? min - result : (result > max ? result - max : 0.0);
                int steps = Math.Abs(k);
                if (distance < bestDistance - 0.0001
                    || (Math.Abs(distance - bestDistance) <= 0.0001 && steps < bestSteps))
                {
                    bestDistance = distance;
                    bestSteps = steps;
                    bestFactor = factor;
                }
            }
            return bestFactor;
        }
    }
}
