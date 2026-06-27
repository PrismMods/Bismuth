using System;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    /* On-demand runtime inspector for finding game-object references while developing
       (which component holds a label, what images/sprites/assets are loaded, …). Driven
       from Misc → Debug when debug mode is on; output goes to BismuthLog, readable via
       Misc → View log. Generalizes GameFontApplier's sweep Diag.

       Extend it by adding a Dump* method here and a button in PageMisc — each method
       filters by the shared Filter substring (matched against text/name/path/asset name)
       and caps its output so a dump can't flood the log. */
    internal static class GameProbe
    {
        // Substring filter (case-insensitive), set from the Debug section's Filter field.
        internal static string Filter = "";
        private const int MaxRows = 300;

        private static bool Match(params string[] candidates)
        {
            if (string.IsNullOrEmpty(Filter)) return true;
            foreach (var s in candidates)
                if (!string.IsNullOrEmpty(s) && s.IndexOf(Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        internal static string Path(Transform t)
        {
            var sb = new StringBuilder();
            for (var p = t; p != null; p = p.parent)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, p.name);
            }
            return sb.ToString();
        }

        private static string Clip(string s, int max = 60)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", "\\n").Trim();
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }

        // ── Dumps ──────────────────────────────────────────────────────────

        internal static void DumpTexts()
        {
            int n = 0;
            BismuthLog.Log($"[probe] ── Texts (filter '{Filter}') ──");
            foreach (var t in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || !Match(t.text, t.name, Path(t.transform)) || ++n > MaxRows) continue;
                var rt = t.transform as RectTransform;
                string box = rt != null ? $" rect={rt.rect.size} scale={t.transform.lossyScale.x:0.###}" : "";
                BismuthLog.Log($"[probe] uGUI \"{Clip(t.text)}\"  {Path(t.transform)}  (fs={t.fontSize} bestFit={t.resizeTextForBestFit} style={t.fontStyle}{box})");
            }
            foreach (var t in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && Match(t.text, t.name, Path(t.transform)) && ++n <= MaxRows)
                    BismuthLog.Log($"[probe] TMP  \"{Clip(t.text)}\"  {Path(t.transform)}  ({t.GetType().Name})");
            foreach (var t in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && Match(t.text, t.name, Path(t.transform)) && ++n <= MaxRows)
                    BismuthLog.Log($"[probe] mesh \"{Clip(t.text)}\"  {Path(t.transform)}");
            BismuthLog.Log($"[probe] texts: {n} matched" + (n > MaxRows ? $" (showed {MaxRows})" : ""));
        }

        internal static void DumpImages()
        {
            int n = 0;
            BismuthLog.Log($"[probe] ── Images (filter '{Filter}') ──");
            foreach (var img in UnityEngine.Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (img == null) continue;
                string spr = img.sprite != null ? img.sprite.name : "<none>";
                if (Match(img.name, spr, Path(img.transform)) && ++n <= MaxRows)
                    BismuthLog.Log($"[probe] Image  sprite='{spr}'  {Path(img.transform)}");
            }
            foreach (var sr in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (sr == null) continue;
                string spr = sr.sprite != null ? sr.sprite.name : "<none>";
                if (Match(sr.name, spr, Path(sr.transform)) && ++n <= MaxRows)
                    BismuthLog.Log($"[probe] SpriteRend sprite='{spr}'  {Path(sr.transform)}");
            }
            BismuthLog.Log($"[probe] images: {n} matched" + (n > MaxRows ? $" (showed {MaxRows})" : ""));
        }

        // Loaded sprite/texture ASSETS (incl. ones not on any GameObject) — found by name.
        internal static void DumpAssets()
        {
            int n = 0;
            BismuthLog.Log($"[probe] ── Assets: Sprites + Textures (filter '{Filter}') ──");
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && Match(s.name) && ++n <= MaxRows)
                    BismuthLog.Log($"[probe] Sprite    '{s.name}'  {s.rect.width}x{s.rect.height}");
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (tex != null && Match(tex.name) && ++n <= MaxRows)
                    BismuthLog.Log($"[probe] Texture2D '{tex.name}'  {tex.width}x{tex.height}");
            BismuthLog.Log($"[probe] assets: {n} matched" + (n > MaxRows ? $" (showed {MaxRows})" : ""));
        }

        // Any component type by name (e.g. "PauseMenu", "scrCreditsText") — dump instances' paths.
        internal static void DumpComponents(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) { BismuthLog.Log("[probe] enter a component type name"); return; }
            var type = AccessTools.TypeByName(typeName.Trim());
            if (type == null) { BismuthLog.Log($"[probe] type '{typeName}' not found"); return; }
            int n = 0;
            BismuthLog.Log($"[probe] ── {type.Name} (filter '{Filter}') ──");
            foreach (var o in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var comp = o as Component;
                if (comp == null) continue;
                if (Match(comp.name, Path(comp.transform)) && ++n <= MaxRows)
                    BismuthLog.Log($"[probe] {type.Name}  {Path(comp.transform)}");
            }
            BismuthLog.Log($"[probe] {type.Name}: {n} matched" + (n > MaxRows ? $" (showed {MaxRows})" : ""));
        }
    }
}
