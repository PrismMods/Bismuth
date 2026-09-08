using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    // What a sweep leaves alone: level-editor UI, Bismuth's own panel, other mods' roots.
    internal static partial class GameFontApplier
    {
        /* Level editor form panels are dense, hand-fitted UI that user size/leading
           tweaks wreck (and some labels auto-fit, so a global shrink lands unevenly).
           Editor-scene text keeps metric normalization only: vanilla, just our font. */
        private static bool IsEditorUi(Component c)
        {
            try
            {
                if (c.gameObject.scene.name != "scnEditor") return false;
                // The autoplay controls-tip (pan/stop-autoplay help, new in 3.3.0) is a
                // HUD-style overlay, not a hand-fitted editor form panel, so it takes the
                // normal game-text downscale like gameplay HUD text.
                try { if (ReferenceEquals(c, scnEditor.instance?.controlsTip)) return false; } catch { }
                return true;
            }
            catch { return false; }
        }

        /* Skip() is the sweep's dominant cost: it walks the ancestor chain twice (foreign-mod
           assemblies, then scrDecoration), for every text, on every sweep. A big custom level
           is mostly decoration text, so that work is spent re-reaching the same "not ours"
           verdict thousands of times.

           The verdict depends on where a component SITS in the hierarchy, not on any font or
           setting, so it survives re-sweeps, font changes and Restore(). Pruned with the rest
           when components die. Caveat: a component reparented after its first sweep keeps the
           old verdict — the pooled text that does move (judgement popups) comes through
           ApplyTo(GameObject) with the same prefab shape either way. */
        private static readonly Dictionary<Component, bool> _skipCache = new Dictionary<Component, bool>();

        private static bool Skip(Component c)
        {
            if (_skipCache.TryGetValue(c, out bool cached)) { _sweepSkipHits++; return cached; }
            bool verdict = SkipUncached(c);
            _skipCache[c] = verdict;
            return verdict;
        }

        private static bool SkipUncached(Component c)
        {
            // Our own shadow render child (the TMP we draw under each styled game Text).
            if (c.gameObject.name == GameTextShadow.ChildName) return true;
            /* Bismuth's own canvases manage their fonts themselves, and txtLevelName
               has its dedicated swap/restore in ApplyLevelNameTransform. Check BOTH
               owner references: scrController.instance can be unset during the
               level-select scene sweep, which let txtLevelName slip through and get
               full-size swapped and scene-bolded ("8-X Jungle City" rendered huge). */
            var root = c.transform.root;
            if (root != null && root.name.StartsWith("Bismuth")) return true;
            // Other mods' HUDs (Sapphire's editor chrome, TUFHelper's PP displayer, …) are
            // theirs to style — leave them on their own fonts. Root name first: it's cheap,
            // and some foreign labels are plain TMP under a bare canvas with no mod
            // component anywhere in their chain, invisible to the assembly walk.
            if (IsOtherModRoot(root) || IsForeignModUi(c)) return true;
            // In-level text decorations (scrDecoration) are styled by the mapper — their
            // FontName and size are part of the chart, so leave them untouched.
            try { if (c.GetComponentInParent<scrDecoration>(true) != null) return true; }
            catch { }
            try { if (ReferenceEquals(c, scrController.instance?.txtLevelName)) return true; }
            catch { }
            try { if (ReferenceEquals(c, scrUIController.instance?.txtLevelName)) return true; }
            catch { }
            return false;
        }

        /* Text owned by another mod's UI: a mod's HUD carries its own MonoBehaviours
           (loaded from Mods//UMMMods/), whereas game text only carries Assembly-CSharp /
           engine scripts. If any ancestor is defined in a foreign mod assembly, the
           hierarchy is that mod's and we leave its fonts alone. Per-assembly verdict is
           cached, so after warmup this is a parent walk + dictionary lookups. */
        private static readonly List<MonoBehaviour> _mbBuf = new List<MonoBehaviour>();
        private static readonly Dictionary<System.Reflection.Assembly, bool> _foreignAsm =
            new Dictionary<System.Reflection.Assembly, bool>();
        private static System.Reflection.Assembly _gameAsm, _bismuthAsm;

        /* Mods conventionally name their own canvases after themselves ("SapphireToolbar",
           "SapphireEditorEvents", "BismuthUI" …), so a hierarchy whose ROOT starts with
           another loaded mod's Id is that mod's UI. This is the only signal on foreign
           labels that carry no mod MonoBehaviour in their ancestor chain (a plain TMP
           straight under a bare canvas). Prefixes come from the UMM registry — no
           hardcoded mod list; refreshed each full sweep so late-installed mods count. */
        private static string[] _modRootPrefixes;

        internal static void RefreshModRootPrefixes() => _modRootPrefixes = null;

        private static bool IsOtherModRoot(Transform root)
        {
            if (root == null) return false;
            if (_modRootPrefixes == null) _modRootPrefixes = BuildModRootPrefixes();
            string n = root.name;
            for (int i = 0; i < _modRootPrefixes.Length; i++)
                if (n.StartsWith(_modRootPrefixes[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string[] BuildModRootPrefixes()
        {
            var list = new List<string>();
            try
            {
                foreach (var m in UnityModManagerNet.UnityModManager.modEntries)
                {
                    string id = m?.Info?.Id;
                    // ≥4 chars so a terse Id can't shadow ordinary scene-object names.
                    if (string.IsNullOrEmpty(id) || id.Length < 4 || id == "Bismuth") continue;
                    list.Add(id);
                }
            }
            catch { }
            // Registry unreadable (UMMCompat quirk) → at least protect the sister mod.
            if (list.Count == 0) list.Add("Sapphire");
            return list.ToArray();
        }

        private static bool IsForeignModUi(Component c)
        {
            try
            {
                for (var p = c.transform; p != null; p = p.parent)
                {
                    p.GetComponents(_mbBuf);
                    for (int i = 0; i < _mbBuf.Count; i++)
                    {
                        var b = _mbBuf[i];
                        if (b != null && IsForeignAssembly(b.GetType().Assembly)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool IsForeignAssembly(System.Reflection.Assembly asm)
        {
            if (asm == null) return false;
            bool verdict;
            if (_foreignAsm.TryGetValue(asm, out verdict)) return verdict;
            verdict = ComputeForeign(asm);
            _foreignAsm[asm] = verdict;
            return verdict;
        }

        private static bool ComputeForeign(System.Reflection.Assembly asm)
        {
            if (_bismuthAsm == null) _bismuthAsm = typeof(GameFontApplier).Assembly;
            if (_gameAsm == null) { try { _gameAsm = typeof(scrController).Assembly; } catch { } }
            if (asm == _bismuthAsm || asm == _gameAsm) return false;
            var n = asm.GetName().Name;
            // Engine/runtime/mod-loader assemblies aren't "another mod's HUD".
            if (n == "Assembly-CSharp-firstpass" || n == "UnityModManager" ||
                n == "mscorlib" || n == "netstandard" ||
                n.StartsWith("UnityEngine") || n.StartsWith("Unity.") ||
                n.StartsWith("System") || n.StartsWith("Mono.") ||
                n.StartsWith("Microsoft") || n.StartsWith("0Harmony") || n.StartsWith("MonoMod"))
                return false;
            /* Beyond the whitelist it's a game dependency shipped in Managed/ (DOTween,
               Rewired…) or a mod (loaded from Mods//UMMMods/). A blank Location means an
               in-memory load, which for a non-engine assembly is a mod. */
            string loc = null;
            try { loc = asm.Location; } catch { }
            if (string.IsNullOrEmpty(loc)) return true;
            return loc.Replace('\\', '/').IndexOf("/Managed/", System.StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}
