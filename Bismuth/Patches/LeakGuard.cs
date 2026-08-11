using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    /* Ported from Quartz's optimizer module (optimizer.qmod, LeakGuardPatches).

       These reclaim GPU memory the game orphans: RenderTextures and Texture2Ds that are
       replaced without being destroyed, so they live until the process exits. Unity does not
       garbage-collect native texture memory — an unreferenced Texture2D leaks its VRAM.

       Every one of these DESTROYS a texture, so the ownership tests are the whole safety
       story and are kept exactly as Quartz has them:
         - camera RT is freed only when it is neither the live material texture nor camRT
         - workshop thumbnails only when this patch saw that sprite installed (OwnedThumbnails)
         - practice waveforms only when the texture is actually named "Waveform"
       Loosening any of those destroys something still in use, which shows as pink or missing
       visuals. Each body is individually try/caught for the same reason: a throw here would
       otherwise escape into a game method mid-frame.

       Fields are reached with AccessTools because several are private; the public ones are
       touched directly. All 14 were verified present in ADOFAI 3.3.0. */
    internal static class LeakGuard
    {
        private static bool Active =>
            MainClass.Settings.OptimizationsEnabled && MainClass.Settings.OptLeakGuard;

        private static readonly AccessTools.FieldRef<scrVisualDecoration, Material> _decoMat =
            AccessTools.FieldRefAccess<scrVisualDecoration, Material>("meshRendererMat");
        private static readonly AccessTools.FieldRef<scrCamera, MeshRenderer> _camQuadMesh =
            AccessTools.FieldRefAccess<scrCamera, MeshRenderer>("camQuadMesh");
        private static readonly AccessTools.FieldRef<scrCamera, RenderTexture> _camRT =
            AccessTools.FieldRefAccess<scrCamera, RenderTexture>("camRT");

        // Sprites this patch watched get installed as a workshop thumbnail. Only these are
        // ever destroyed — a thumbnail the game shares elsewhere is never in the set.
        private static readonly HashSet<Sprite> _ownedThumbnails = new HashSet<Sprite>();

        // scrVisualDecoration owns a material whose mainTexture can be a RenderTexture, plus
        // two spare RTs. None are released when the decoration is destroyed.
        [HarmonyPatch(typeof(scrVisualDecoration), "OnDestroy")]
        internal static class VisualDecoMaterialPatch
        {
            public static void Postfix(scrVisualDecoration __instance)
            {
                if (!Active) return;
                try
                {
                    var mat = _decoMat(__instance);
                    if (mat != null)
                    {
                        if (mat.mainTexture is RenderTexture rt) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                        UnityEngine.Object.Destroy(mat);
                        _decoMat(__instance) = null;
                    }
                    if (__instance.spareRT1 != null) { UnityEngine.Object.Destroy(__instance.spareRT1); __instance.spareRT1 = null; }
                    if (__instance.spareRT2 != null) { UnityEngine.Object.Destroy(__instance.spareRT2); __instance.spareRT2 = null; }
                }
                catch { }
            }
        }

        // Changing the custom frame rate swaps the camera quad's RenderTexture and drops the
        // old one. Freed only if the new state no longer references it.
        [HarmonyPatch(typeof(scrCamera), "SetCustomFrameRate")]
        internal static class FrameRateRTPatch
        {
            public static void Prefix(scrCamera __instance, out RenderTexture __state)
            {
                __state = null;
                if (!Active) return;
                try
                {
                    var mesh = _camQuadMesh(__instance);
                    if (mesh != null && mesh.sharedMaterial != null)
                        __state = mesh.sharedMaterial.mainTexture as RenderTexture;
                }
                catch { }
            }

            public static void Postfix(scrCamera __instance, RenderTexture __state)
            {
                if (__state == null) return;
                try
                {
                    var mesh = _camQuadMesh(__instance);
                    Texture live = (mesh != null && mesh.sharedMaterial != null) ? mesh.sharedMaterial.mainTexture : null;
                    // Still in use as either the quad's texture or the camera's own RT — leave it.
                    if (__state != live && __state != _camRT(__instance))
                    {
                        __state.Release();
                        UnityEngine.Object.Destroy(__state);
                    }
                }
                catch { }
            }
        }

        // Browsing the workshop list replaces the thumbnail sprite per selection; the old
        // sprite and its texture are dropped.
        [HarmonyPatch(typeof(WorkshopLevelList), "SelectLevel")]
        internal static class WorkshopThumbnailPatch
        {
            public static void Prefix(WorkshopLevelList __instance, out Sprite __state)
            {
                __state = __instance.thumbnail != null ? __instance.thumbnail.sprite : null;
            }

            public static void Postfix(WorkshopLevelList __instance, Sprite __state)
            {
                if (!Active) return;
                try
                {
                    Sprite live = __instance.thumbnail != null ? __instance.thumbnail.sprite : null;
                    if (live != null && live != __state) _ownedThumbnails.Add(live);
                    if (__state != null && __state != live && _ownedThumbnails.Remove(__state))
                    {
                        var tex = __state.texture;
                        UnityEngine.Object.Destroy(__state);
                        if (tex != null) UnityEngine.Object.Destroy(tex);
                    }
                }
                catch { }
            }
        }

        // The practice timeline bakes a waveform texture on every Init and drops the old one.
        [HarmonyPatch(typeof(PracticeTimeline), "Init")]
        internal static class PracticeWaveformPatch
        {
            public static void Prefix(PracticeTimeline __instance, out Texture __state)
            {
                __state = __instance.waveRaw != null ? __instance.waveRaw.texture : null;
            }

            public static void Postfix(PracticeTimeline __instance, Texture __state)
            {
                if (!Active) return;
                try
                {
                    Texture live = __instance.waveRaw != null ? __instance.waveRaw.texture : null;
                    // Name check is the ownership test — only the baked waveform is ours.
                    if (__state != live && __state is Texture2D old && old.name == "Waveform")
                        UnityEngine.Object.Destroy(old);
                }
                catch { }
            }
        }

        /* Static caches that grow for the whole session. Called on scene change alongside the
           existing unload-assets pass. The filter dictionaries are keyed by UnityEngine.Object,
           so destroyed keys compare == null while still holding their entry (and its values)
           alive — that's what makes them leak rather than just grow. */
        private static readonly string[] FilterDictFields =
            { "addedFilters", "usedFilters", "modifiedFilters", "filterOriginalValues", "filterFieldTweens", "initializedFilters" };
        private static readonly List<object> _deadKeys = new List<object>();

        internal static void SweepStaticCaches()
        {
            if (!Active) return;
            try { FloorMesh.cache.Clear(); } catch { }
            PruneFilterDictionaries();
            _ownedThumbnails.Clear();
        }

        private static void PruneFilterDictionaries()
        {
            try
            {
                foreach (var name in FilterDictFields)
                {
                    var f = AccessTools.Field(typeof(ffxSetFilterAdvancedPlus), name);
                    if (!(f?.GetValue(null) is IDictionary dict)) continue;

                    _deadKeys.Clear();
                    foreach (var key in dict.Keys)
                        // Unity's overloaded == is the point: a destroyed Object is "null"
                        // while the reference is still a live dictionary key.
                        if (key is UnityEngine.Object uo && uo == null) _deadKeys.Add(key);
                    foreach (var key in _deadKeys) dict.Remove(key);
                }
            }
            catch { }
            _deadKeys.Clear();
        }
    }
}
