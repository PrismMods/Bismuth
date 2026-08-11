using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;

namespace Bismuth
{
    /* Settings profiles = full Settings snapshots serialized to <mod>/Profiles/<name>.xml
       (the same XmlSerializer format UMM saves with), which doubles as import/export:
       share the file, drop it in the folder, rescan, load. Loading copies every public
       field onto the LIVE Settings instance (UI closures and statics hold references to
       it), then the caller force-reloads to rebuild the panel and re-apply everything. */
    internal static class Profiles
    {
        // Built-ins are generated, not files. Default IS the out-of-the-box preset (= the
        // Settings class defaults: soft-red theme accent — final spec TBD by the user);
        // Azure = the same but with the classic periwinkle accent, theme off.
        internal static readonly string[] BuiltIn = { "Default", "Azure" };

        private static string Dir => Path.Combine(MainClass.ModPath, "Profiles");

        internal static bool IsBuiltIn(string name)
        {
            foreach (var b in BuiltIn)
                if (string.Equals(b, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static List<string> ListSaved()
        {
            var result = new List<string>();
            try
            {
                if (Directory.Exists(Dir))
                    foreach (var f in Directory.GetFiles(Dir, "*.xml"))
                        result.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch (Exception e) { BismuthLog.Log("Profiles: list failed: " + e.Message); }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        internal static string ProfilesDir()
        {
            try { Directory.CreateDirectory(Dir); } catch { }
            return Dir;
        }

        // Strip path-hostile characters; empty or built-in names are rejected by Save.
        internal static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "");
            return name.Trim();
        }

        internal static bool SaveCurrent(string name, out string error)
        {
            error = null;
            name = SanitizeName(name);
            if (name.Length == 0) { error = "Enter a profile name first."; return false; }
            if (IsBuiltIn(name)) { error = "\"" + name + "\" is a built-in profile."; return false; }
            try
            {
                Directory.CreateDirectory(Dir);
                using (var w = new StreamWriter(Path.Combine(Dir, name + ".xml"), false))
                    new XmlSerializer(typeof(Settings)).Serialize(w, MainClass.Settings);
                // Saving makes it the live profile — the settings on screen ARE its contents.
                MainClass.Settings.ActiveProfile = name;
                BismuthLog.Log("Profiles: saved '" + name + "'");
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                BismuthLog.Log("Profiles: save failed: " + e);
                return false;
            }
        }

        internal static bool Delete(string name, out string error)
        {
            error = null;
            try
            {
                string path = Path.Combine(Dir, SanitizeName(name) + ".xml");
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        internal static bool Load(string name, out string error)
        {
            error = null;
            try
            {
                Settings src;
                if (IsBuiltIn(name))
                {
                    src = MakeBuiltIn(name);
                }
                else
                {
                    string path = Path.Combine(Dir, SanitizeName(name) + ".xml");
                    if (!File.Exists(path)) { error = "Profile file not found."; return false; }
                    using (var r = new StreamReader(path))
                        src = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(r);
                }
                if (src == null) { error = "Profile could not be read."; return false; }
                src.EnsureDefaults();
                CopyInto(src, MainClass.Settings);
                MainClass.Settings.EnsureDefaults();
                // After CopyInto — it copies every field, including the file's own ActiveProfile.
                MainClass.Settings.ActiveProfile = name;
                BismuthLog.Log("Profiles: loaded '" + name + "'");
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                BismuthLog.Log("Profiles: load failed: " + e);
                return false;
            }
        }

        private static Settings MakeBuiltIn(string name)
        {
            var s = new Settings();
            s.EnsureDefaults();
            if (string.Equals(name, "Azure", StringComparison.OrdinalIgnoreCase))
            {
                // The classic look: periwinkle accent, theme mode off, pre-1.3 stat set.
                s.UiAccentR = 0.604f;
                s.UiAccentG = 0.706f;
                s.UiAccentB = 1.0f;
                s.AccentAsTheme = false;
                s.ShowKps = false;
                s.ShowBestProgress = false;
                s.ShowProgressBar = false;
            }
            return s;
        }

        // The live Settings instance is captured all over (page closures, UMM) — mutate
        // it in place instead of swapping references. List/object fields take the fresh
        // instances from the deserialized snapshot, which shares nothing.
        private static void CopyInto(Settings from, Settings into)
        {
            foreach (var f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                f.SetValue(into, f.GetValue(from));
        }
    }
}
