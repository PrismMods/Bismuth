using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace Bismuth
{
    /* Game members whose shape differs between ADOFAI builds, reached by NAME so one dll runs on all
       of them (2.9 on Unity 2022, 3.x on Unity 6). A direct reference binds to one build's signature
       and throws MissingMember on the others — Sapphire's tools/refcheck lists them.

       The big split is co-op: 3.x keeps a scrPlayer per player, each with a scrMarginTracker; 2.9
       has neither — one scrMistakesManager (hit lists static) and the planets on scrController.
       Callers hold trackers and players as `object` and read them through here. */
    internal static class GameCompat
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        private static readonly Dictionary<string, MemberInfo> _mem = new Dictionary<string, MemberInfo>();

        // Plain reflection, not AccessTools: MelonLoader logs a warning for every AccessTools miss,
        // and these run every frame on the build that lacks the member.
        private static MemberInfo Mem(Type t, string name)
        {
            string k = t.FullName + "." + name;
            MemberInfo m;
            if (_mem.TryGetValue(k, out m)) return m;
            try { m = t.GetProperty(name, Any); } catch (AmbiguousMatchException) { m = null; }
            if (m == null) m = t.GetField(name, Any);
            _mem[k] = m;
            return m;
        }

        // Field or property, instance or static; null when this build has no such member.
        internal static object Get(object o, string name, Type t = null)
        {
            t = t ?? (o != null ? o.GetType() : null);
            if (t == null) return null;
            var m = Mem(t, name);
            var p = m as PropertyInfo;
            if (p != null) return p.GetValue(p.GetGetMethod(true).IsStatic ? null : o, null);
            var f = m as FieldInfo;
            return f != null ? f.GetValue(f.IsStatic ? null : o) : null;
        }

        internal static bool Set(object o, string name, object value, Type t = null)
        {
            t = t ?? (o != null ? o.GetType() : null);
            if (t == null) return false;
            var m = Mem(t, name);
            var p = m as PropertyInfo;
            if (p != null && p.CanWrite) { p.SetValue(p.GetSetMethod(true).IsStatic ? null : o, value, null); return true; }
            var f = m as FieldInfo;
            if (f == null) return false;
            f.SetValue(f.IsStatic ? null : o, value);
            return true;
        }

        private static float F(object v) => v == null ? 0f : Convert.ToSingle(v);

        // ── players and trackers ─────────────────────────────────────────────

        // Assembly.GetType, not AccessTools.TypeByName: that logs a WARNING on the build without them.
        private static readonly Type _trackerT = typeof(scrController).Assembly.GetType("scrMarginTracker");
        private static readonly Type _playerT = typeof(scrController).Assembly.GetType("scrPlayer");
        private static readonly Type _playerMgrT = typeof(scrController).Assembly.GetType("scrPlayerManager");
        private static readonly object[] None = new object[0];
        private static readonly object[] _one = new object[1];

        internal static bool CoopEra => _trackerT != null;

        // What records hits: both have AddHit(HitMargin) and Reset().
        internal static Type TrackerType => _trackerT ?? typeof(scrMistakesManager);

        // What hits: scrPlayer.Hit on 3.x, scrController.Hit on 2.9.
        internal static Type HitterType => _playerT ?? typeof(scrController);

        // One per player on 3.x; the controller's single mistakes manager on 2.9. Never null.
        internal static object[] Trackers()
        {
            if (_trackerT != null)
                return Get(null, "marginTrackers", typeof(scrMistakesManager)) as object[] ?? None;
            var ctrl = scrController.instance;
            object mm = ctrl != null ? Get(ctrl, "mistakesManager") : null;
            if (mm == null) return None;
            _one[0] = mm;
            return _one;
        }

        internal static object Tracker(int i)
        {
            var t = Trackers();
            return i >= 0 && i < t.Length ? t[i] : null;
        }

        // hitMargins/hitMarginsCount/lastHitMarginsSize are instance on 3.x, static on 2.9; Get takes either.
        internal static List<HitMargin> HitMargins(object t) => t == null ? null : Get(t, "hitMargins") as List<HitMargin>;
        internal static int LastHitMarginsSize(object t) => t == null ? 0 : Convert.ToInt32(Get(t, "lastHitMarginsSize") ?? 0);
        internal static float PercentAcc(object t) => t == null ? 0f : F(Get(t, "percentAcc"));
        internal static float PercentXAcc(object t) => t == null ? 0f : F(Get(t, "percentXAcc"));

        // Per tracker on 3.x; one static count on 2.9's controller.
        internal static int Deaths(object t)
        {
            object v = t != null ? Get(t, "deaths") : null;
            if (v == null) v = Get(null, "deaths", typeof(scrController));
            return v == null ? 0 : Convert.ToInt32(v);
        }

        // 3.x: the player's own tracker; 2.9: the "player" is the controller.
        internal static object TrackerOf(object player)
        {
            if (player == null) return null;
            return Get(player, "marginTracker") ?? Get(player, "mistakesManager");
        }

        internal static object[] Players()
        {
            if (_playerMgrT != null)
            {
                var mgr = Get(null, "instance", _playerMgrT);
                return (mgr != null ? Get(mgr, "players") as object[] : null) ?? None;
            }
            var ctrl = scrController.instance;
            if (ctrl == null) return None;
            _one[0] = ctrl;
            return _one;
        }

        // 2.9 has one player and no per-player death.
        internal static bool Alive(int i)
        {
            if (_playerT == null) return true;
            var p = Players();
            return i >= 0 && i < p.Length && p[i] != null && Get(p[i], "alive") is bool a && a;
        }

        internal static scrFloor CurrFloor(object hitter) => hitter == null ? null : Get(hitter, "currFloor") as scrFloor;

        // scrPlayer's getter is private; scrController's is not.
        internal static scrPlanet ChosenPlanet(object hitter) => hitter == null ? null : Get(hitter, "chosenPlanet") as scrPlanet;

        // Planet speed lives on PlanetarySystem on 3.x and on scrController on 2.9.
        internal static double Speed(scrPlanet planet)
        {
            object sys = planet != null ? Get(planet, "planetarySystem") : null;
            object v = sys != null ? Get(sys, "speed") : null;
            if (v == null && scrController.instance != null) v = Get(scrController.instance, "speed");
            return v == null ? 1.0 : Convert.ToDouble(v);
        }

        internal static double PlayerOneSpeed()
        {
            var ctrl = scrController.instance;
            if (ctrl == null) return 1.0;
            object p1 = Get(ctrl, "playerOne");
            object sys = p1 != null ? Get(p1, "planetarySystem") : null;
            object v = (sys != null ? Get(sys, "speed") : null) ?? Get(ctrl, "speed");
            return v == null ? 1.0 : Convert.ToDouble(v);
        }

        // A property on 3.x, a field on 2.9.
        internal static double TargetExitAngle(scrPlanet p) => Convert.ToDouble(Get(p, "targetExitAngle"));

        // Shift every player's last-hit time (the countdown's logical-clock rewind).
        internal static void ShiftLastHit(double shift)
        {
            foreach (var p in Players())
            {
                if (p == null) continue;
                object v = Get(p, "lastHit");
                if (v != null) Set(p, "lastHit", Convert.ToDouble(v) + shift);
            }
        }

        // ── 3.x-only members (absent → no-op / empty) ────────────────────────

        internal static string MaxUsedKeys()
        {
            var ctrl = scrController.instance;
            object v = ctrl != null ? Get(ctrl, "maximumUsedKeys") : null;
            return v != null ? v.ToString() : "";
        }

        internal static void SetNoAutoHud(bool v) => Set(null, "noAutoHud", v, typeof(RDC));

        internal static LineRenderer RingLine(scrRing ring) => ring == null ? null : Get(ring, "line") as LineRenderer;

        internal static UnityEngine.UI.Text ControlsTip(scnEditor ed) => ed == null ? null : Get(ed, "controlsTip") as UnityEngine.UI.Text;

        // The results readout: a DetailedResults wrapping a Text on 3.x, the Text itself on 2.9.
        internal static Component ResultsComponent(scrUIController uic) => uic == null ? null : Get(uic, "txtResults") as Component;

        internal static UnityEngine.UI.Text ResultsText(scrUIController uic)
        {
            var r = ResultsComponent(uic);
            return r as UnityEngine.UI.Text ?? (r != null ? Get(r, "textComponent") as UnityEngine.UI.Text : null);
        }

        // ── TMP: Unity 2022 (2.9) vs Unity 6 (3.x) ───────────────────────────

        // TMP_FontAsset's on 2022, TMP_Asset's on Unity 6.
        internal static FaceInfo FaceInfoOf(TMP_FontAsset fa) => fa == null ? default(FaceInfo) : (FaceInfo)Get(fa, "faceInfo");

        // int on 2022, float on Unity 6.
        internal static float PointSize(FaceInfo fi) => F(Get(fi, "pointSize"));

        // A field on 2022's TMP, a property on Unity 6's.
        internal static Material MaterialOf(TMP_Asset a) => a == null ? null : Get(a, "material") as Material;

        // ── RDString: 2.9 adds a trailing LangSection parameter ─────────────

        private static readonly MethodInfo _rdGet = Lead(typeof(RDString), "Get", typeof(string));

        internal static string Str(string key) => (string)_rdGet.Invoke(null, Args(_rdGet, key));

        private static MethodInfo Lead(Type t, string name, params Type[] lead)
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length < lead.Length) continue;
                bool ok = true;
                for (int i = 0; i < lead.Length && ok; i++) ok = ps[i].ParameterType == lead[i];
                for (int i = lead.Length; i < ps.Length && ok; i++) ok = ps[i].IsOptional;
                if (ok) return m;
            }
            return null;
        }

        private static object[] Args(MethodInfo m, params object[] lead)
        {
            var ps = m.GetParameters();
            var a = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < lead.Length) { a[i] = lead[i]; continue; }
                var p = ps[i];
                object d = p.DefaultValue;
                if (d == null || d is DBNull || d == Type.Missing)
                    a[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                else
                    a[i] = p.ParameterType.IsEnum ? Enum.ToObject(p.ParameterType, d) : d;
            }
            return a;
        }
    }
}
