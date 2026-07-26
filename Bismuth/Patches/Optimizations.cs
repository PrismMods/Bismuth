using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace Bismuth
{
    // Throttles AudioSource.GetSpectrumData (an expensive native FFT) to every other
    // frame, halving the cost left after scrConductor's getSpectrum && !lofiVersion gate.
    [HarmonyPatch(typeof(scrConductor), "Update")]
    internal static class ConductorSpectrumThrottlePatch
    {
        private static int _frame;
        private static bool _suppressed;

        public static void Prefix(scrConductor __instance)
        {
            _suppressed = false;
            if (__instance.getSpectrum && MainClass.Settings.OptimizationsEnabled
                && MainClass.Settings.OptSpectrumThrottle && ++_frame % 2 != 0)
            {
                __instance.getSpectrum = false;
                _suppressed = true;
            }
        }

        public static void Postfix(scrConductor __instance)
        {
            if (_suppressed)
                __instance.getSpectrum = true;
        }
    }

    // After loading a custom-level texture from disk, optionally compresses to DXT and
    // calls Apply(false, true) to release the CPU-side pixel copy once on the GPU.
    [HarmonyPatch(typeof(TextureManager), "LoadTexture")]
    internal static class TextureManagerLoadTexturePatch
    {
        public static void Postfix(ref Texture2D __result)
        {
            if (!MainClass.Settings.OptimizationsEnabled || !MainClass.Settings.OptTextureNonReadable) return;
            if (__result == null || !__result.isReadable) return;
            if (MainClass.Settings.OptTextureDXT && __result.width % 4 == 0 && __result.height % 4 == 0)
                __result.Compress(false);
            __result.Apply(false, true);
        }
    }

    /* ADOFAI 3.3.0 removed TextureManager's per-ImageOptions variant system: CustomTexture
       is now a plain holder and CustomSprite builds its single sprite in its ctor, so
       CustomTexture.GetTexture / CustomSprite.GetSprite / the ImageOptions enum are gone.
       The two variant-copy prefixes that lived here (keeping non-readable base textures
       working through the old Instantiate-based duplication) no longer have a target — and
       aren't needed, since nothing duplicates the texture anymore. The LoadTexture postfix
       above still makes the loaded texture non-readable, which is now safe on its own. */

    // scrPlanet.Update's per-frame Physics2D.OverlapCircleAll allocates a Collider2D[]
    // each time. Use OverlapCircleNonAlloc into a static buffer, returning Array.Empty on
    // the common zero-hit case, to eliminate the per-frame allocation.
    [HarmonyPatch(typeof(scrPlanet), "Update")]
    internal static class PlanetCollisionNonAllocPatch
    {
        private static readonly Collider2D[] _buf = new Collider2D[32];

        private static readonly MethodInfo _overlapAll =
            typeof(Physics2D).GetMethod("OverlapCircleAll", new[] { typeof(Vector2), typeof(float) });
        private static readonly MethodInfo _replacement =
            typeof(PlanetCollisionNonAllocPatch).GetMethod(nameof(OverlapDecorCircle));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                if (instr.opcode == OpCodes.Call && (MethodInfo)instr.operand == _overlapAll)
                    yield return new CodeInstruction(OpCodes.Call, _replacement);
                else
                    yield return instr;
            }
        }

        public static Collider2D[] OverlapDecorCircle(Vector2 pos, float radius)
        {
            if (!MainClass.Settings.OptimizationsEnabled || !MainClass.Settings.OptPhysicsNonAlloc)
                return Physics2D.OverlapCircleAll(pos, radius);
            // Replicates the 2-arg OverlapCircleAll defaults (DefaultRaycastLayers, live
            // queriesHitTriggers, no depth bound) so the non-alloc query returns identical
            // hits. Built per call to track queriesHitTriggers; it's a stack struct, no alloc.
            var filter = new ContactFilter2D
            {
                useTriggers = Physics2D.queriesHitTriggers,
                useLayerMask = true,
                layerMask = Physics2D.DefaultRaycastLayers,
            };
            int count = Physics2D.OverlapCircle(pos, radius, filter, _buf);
            if (count == 0) return Array.Empty<Collider2D>();
            var result = new Collider2D[count];
            Array.Copy(_buf, result, count);
            return result;
        }
    }

    // scrFloor.Update (Volume color type) calls DOTween.Sequence() every frame before
    // checking specialColorPulse, abandoning it when == None (one wasted alloc per tile
    // per frame). The transpiler swaps in a wrapper returning a persistent no-op sequence
    // for the None case.
    [HarmonyPatch(typeof(scrFloor), "Update")]
    internal static class FloorVolumeTrackDOTweenPatch
    {
        private static Sequence _noop;

        private static readonly MethodInfo _seqMethod =
            typeof(DOTween).GetMethod("Sequence", Type.EmptyTypes);
        private static readonly MethodInfo _wrapper =
            typeof(FloorVolumeTrackDOTweenPatch).GetMethod(nameof(SequenceOrNoop));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instr in instructions)
            {
                if (instr.opcode == OpCodes.Call && (MethodInfo)instr.operand == _seqMethod)
                {
                    // Stack before: (empty) — DOTween.Sequence() takes no args.
                    // Stack after our replacement: push this, call SequenceOrNoop(scrFloor) → Sequence.
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, _wrapper);
                }
                else
                {
                    yield return instr;
                }
            }
        }

        public static Sequence SequenceOrNoop(scrFloor floor)
        {
            if (!MainClass.Settings.OptimizationsEnabled || !MainClass.Settings.OptVolumeTrackDOTween)
                return DOTween.Sequence();
            if (floor.specialColorPulse == TrackColorPulse.None)
            {
                // Return a persistent paused sequence; the None branch never uses it.
                if (_noop == null || !_noop.active)
                    _noop = DOTween.Sequence().SetAutoKill(false).Pause();
                return _noop;
            }
            return DOTween.Sequence();
        }
    }
}
