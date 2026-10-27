using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Topaz
{
    /* Topaz was called Bismuth up to 1.3.x. Runs at every launch, before settings load, and
       finds an install still carrying that name one of two ways:
       - Updated in place: a 1.x updater took the release's legacy Bismuth.zip (this payload
         under the old root folder) into its own <loader>/Bismuth, so Topaz now runs from there.
         Settings and key counts already sit beside it; the attempts file is renamed, and the old
         dll, log and symbol font are dropped.
       - Installed beside: Topaz went into <loader>/Topaz while Bismuth still sits in
         <loader>/Bismuth. Its data comes over when this install has no settings yet, and the
         old folder is retired: without an Info.json UMM stops loading it from the next launch,
         and the folder itself goes on the launch after, when Bismuth is no longer running. */
    internal static class FromBismuth
    {
        private const string Retired = "Info.json.retired";

        // Left in the folder by an in-place update: nothing reads them any more.
        private static readonly string[] Stale =
        {
            "Bismuth.dll", "BismuthLog.txt",
            Path.Combine("Resources", "BismuthSymbols.ttf"), Path.Combine("Resources", "BismuthSymbols-LICENSE.txt"),
        };

        internal static void Run(string modPath)
        {
            try { Adopt(modPath); }
            catch (Exception e) { MainClass.Logger.Warning("[Topaz] Carrying over from Bismuth failed: " + e.Message); }
            try { Tidy(modPath); }
            catch (Exception e) { MainClass.Logger.Warning("[Topaz] Tidying Bismuth's files failed: " + e.Message); }
        }

        // The Id an install's Info.json declares, null when there is none.
        internal static string InfoId(string dir)
        {
            string info = Path.Combine(dir, "Info.json");
            if (!File.Exists(info)) return null;
            Match m = Regex.Match(File.ReadAllText(info), "\"Id\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static void Adopt(string modPath)
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(modPath).TrimEnd('/', '\\')));
            foreach (string loader in new[] { "Mods", "UMMMods" })
            {
                string old = Path.Combine(Path.Combine(root, loader), "Bismuth");
                if (!Directory.Exists(old) || UpdateChecker.SamePath(old, modPath)) continue;
                if (File.Exists(Path.Combine(old, Retired)))
                {
                    TryDelete(old);
                    continue;
                }
                if (InfoId(old) != "Bismuth") continue;

                if (!File.Exists(Path.Combine(modPath, "Settings.xml")))
                {
                    CopyData(old, modPath);
                    MainClass.Logger.Log("[Topaz] Carried settings and data over from " + old);
                }
                File.Move(Path.Combine(old, "Info.json"), Path.Combine(old, Retired));
                MainClass.Logger.Log("[Topaz] Retired the old Bismuth install at " + old + "; restart once so it stops running");
            }
        }

        // The user's files only: settings, counts, attempts, profiles, fonts, presets. The
        // payload (dlls, Info.json, Resources) and the log stay behind.
        private static void CopyData(string from, string to)
        {
            foreach (string src in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = src.Substring(from.Length).TrimStart('/', '\\');
                string name = Path.GetFileName(rel);
                if (rel.StartsWith("Resources", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".old") || name.EndsWith(".new")) continue;   // an updater's swap files
                if (name == "Info.json" || name == "BismuthLog.txt") continue;
                string dst = Path.Combine(to, rel);
                if (File.Exists(dst)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst);
            }
        }

        private static void Tidy(string modPath)
        {
            string attempts = Path.Combine(modPath, "BismuthAttempts.txt");
            if (File.Exists(attempts) && !File.Exists(Path.Combine(modPath, "TopazAttempts.txt")))
                File.Move(attempts, Path.Combine(modPath, "TopazAttempts.txt"));
            foreach (string f in Stale)
            {
                string path = Path.Combine(modPath, f);
                if (!File.Exists(path)) continue;
                try { File.Delete(path); }
                catch { /* locked or read-only: harmless, it just stays */ }
            }
        }

        private static void TryDelete(string dir)
        {
            try
            {
                Directory.Delete(dir, true);
                MainClass.Logger.Log("[Topaz] Removed the retired Bismuth folder at " + dir);
            }
            catch { /* still in use: the next launch tries again */ }
        }
    }
}
