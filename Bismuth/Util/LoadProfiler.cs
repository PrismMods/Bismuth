using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Bismuth
{
    /* Where a level load's time actually goes, measured inside the GAME's own load path.
       Bismuth's share is 4% (129ms of 3190ms), so anything worth doing about load speed is
       about the other 96% — and guessing which part that is would be the same mistake as
       assuming the font sweep was the ancestor walks.

       Debug-mode only, and patched lazily: these are hot game methods, and a stopwatch pair
       on them is not something to ship running for everyone.

       Reading the numbers: only SYNCHRONOUS time inside each method is captured. The ones
       that kick off coroutines (ReloadSong, ReloadCustomSounds) will read as near-zero here
       while their real work happens across later frames — that gap between the sum and the
       3190ms total is itself the finding, since it points at async asset work rather than
       at scene construction. */
    internal static class LoadProfiler
    {
        /* Synchronous entry points on the load path, coarse to fine. UpdateDecorationObjects
           measured 4497ms on a heavy chart — the question it raises is how much of that is
           asset IO (a thread could overlap it) versus GameObject creation (it could not), so
           TextureManager.LoadTexture and the sprite loader are timed separately underneath. */
        private static readonly (string Type, string Method)[] Targets =
        {
            ("scnGame", "LoadLevel"),
            ("scnGame", "FinishCustomLevelLoading"),
            ("scnGame", "ApplyEventsToFloors"),
            ("scnGame", "UpdateDecorationObjects"),
            ("scnGame", "RemakePath"),
            ("scnGame", "ReloadAssets"),
            ("scnGame", "UpdateFloorSprites"),
            ("scnGame", "UpdateBackgroundSprites"),
            ("scnGame", "UpdateTrackColors"),
            ("scnGame", "SetBackground"),
            ("TextureManager", "LoadTexture"),
            ("scrDecoration", "SetSprite"),
            ("scrDecoration", "LoadSprite"),
        };

        private class Entry { public double Ms; public int Calls; public Stopwatch Watch = new Stopwatch(); }

        private static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private static bool _patched;

        internal static bool Active => MainClass.Settings != null && MainClass.Settings.DebugMode;

        internal static void TryPatch(Harmony harmony)
        {
            if (_patched || harmony == null || !Active) return;
            _patched = true;

            foreach (var (typeName, name) in Targets)
            {
                try
                {
                    var t = AccessTools.TypeByName(typeName);
                    if (t == null) continue;
                    // First overload by name: unambiguous enough here, and a wrong pick only
                    // mis-attributes a line in a debug readout.
                    MethodInfo m = null;
                    foreach (var cand in t.GetMethods(AccessTools.all))
                        if (cand.Name == name && !cand.IsAbstract && cand.GetMethodBody() != null) { m = cand; break; }
                    if (m == null) continue;

                    _entries[name] = new Entry();
                    var proc = harmony.CreateProcessor(m);
                    proc.AddPrefix(new HarmonyMethod(typeof(LoadProfiler), nameof(Pre)));
                    proc.AddPostfix(new HarmonyMethod(typeof(LoadProfiler), nameof(Post)));
                    proc.Patch();
                }
                catch (Exception e)
                {
                    BismuthLog.Log($"LoadProfiler: {name} not patched ({e.Message})");
                }
            }
            BismuthLog.Log($"LoadProfiler: watching {_entries.Count} load method(s)");
        }

        /* Re-entrant by nature — LoadLevel calls RemakePath calls ApplyEventsToFloors — so each
           entry keeps its own stopwatch and nested time is counted against BOTH, outer and
           inner. Subtracting would hide the nesting; the caller reads the outer as a total and
           the inner as its breakdown. */
        /* Distinct texture paths vs calls. 380 loads for one level is only irreducible work if
           they are 380 different files — if the same sprite is loaded repeatedly, a path-keyed
           cache removes the decode entirely, no threads needed. */
        private static readonly HashSet<string> _texturePaths = new HashSet<string>();

        private static void Pre(MethodBase __originalMethod, object[] __args)
        {
            if (__originalMethod.Name == "LoadTexture" && __args != null && __args.Length > 0
                && __args[0] is string path)
                _texturePaths.Add(path);

            /* Timed from LoadLevel, not from sceneLoaded: loading a level out of the custom
               level list reuses the existing scnGame scene, so no scene-load event fires and
               the previous version silently reported nothing for exactly the case we care
               about. LoadLevel is the load, whichever way it was entered. */
            if (__originalMethod.Name == "LoadLevel")
            {
                Reset();
                _loadStart = Time.realtimeSinceStartup;
                GameFontApplier.ResetSceneAccounting();
            }
            if (_entries.TryGetValue(__originalMethod.Name, out var e)) e.Watch.Start();
        }

        private static float _loadStart = -1f;

        private static void Post(MethodBase __originalMethod)
        {
            if (!_entries.TryGetValue(__originalMethod.Name, out var e)) return;
            e.Watch.Stop();
            e.Ms = e.Watch.Elapsed.TotalMilliseconds;
            e.Calls++;
            _lastActivityFrame = Time.frameCount;
        }

        internal static void Reset()
        {
            foreach (var e in _entries.Values) { e.Watch.Reset(); e.Ms = 0; e.Calls = 0; }
            _texturePaths.Clear();
        }

        /* Ending the measurement on FinishCustomLevelLoading only worked for CLS levels — two
           consecutive test runs loaded levels that never call it and silently reported nothing.
           Instead: the load is over once no profiled method has run for a while. Frame-based,
           so a long stall inside the load can't end it early. */
        private static int _lastActivityFrame;
        private const int QuietFrames = 90;

        internal static void Tick()
        {
            if (_loadStart < 0f || !Active) return;
            if (Time.frameCount - _lastActivityFrame < QuietFrames) return;
            Report();
        }

        private static void Report()
        {
            if (_loadStart < 0f) return;
            // Minus the quiet window we waited to be sure the load had finished.
            float totalMs = (Time.realtimeSinceStartup - _loadStart) * 1000f
                            - QuietFrames * Time.unscaledDeltaTime * 1000f;
            _loadStart = -1f;
            string breakdown = Summary();
            BismuthLog.Debug($"[dbg] Level load: ~{Mathf.Max(0f, totalMs):0}ms of load work, " +
                $"Bismuth sweeps {GameFontApplier.SweepMsThisScene:0}ms");
            if (breakdown != null) BismuthLog.Debug("[dbg] Load breakdown: " + breakdown);
            if (_entries.TryGetValue("LoadTexture", out var tex) && tex.Calls > 0)
                BismuthLog.Debug($"[dbg] Textures: {tex.Calls} load(s), {_texturePaths.Count} distinct file(s), " +
                    $"{tex.Ms / tex.Calls:0.0}ms each, DXT compression " +
                    $"{(MainClass.Settings != null && MainClass.Settings.OptTextureDXT ? "ON" : "off")}");
        }

        // Called once the level is playable, beside the overall load timing.
        internal static string Summary()
        {
            if (_entries.Count == 0) return null;
            var parts = new List<string>();
            foreach (var kv in _entries)
                if (kv.Value.Ms >= 1d)
                    parts.Add($"{kv.Key} {kv.Value.Ms:0}ms x{kv.Value.Calls}");
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }
    }
}
