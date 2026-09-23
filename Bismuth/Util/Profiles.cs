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
        // Built-ins are generated, not files: Default = the Settings class defaults (soft-red
        // theme), Azure = the classic periwinkle accent with theme off.
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

                // Stats this profile turns on beyond the classic set.
                s.ShowXScore = true;
                s.ShowHitError = true;
                s.ShowTimingGraph = true;
                s.CustomResults = true;

                // Timing graph: vertical, pinned to the right edge, sliding inward on results.
                s.TimingGraphX = 0.9796875f;
                s.TimingGraphY = 0.5f;
                s.TimingGraphHeight = 50f;
                s.TimingGraphScale = 1.36894929f;
                s.TimingGraphRotation = 90f;
                s.TimingGraphResultsX = 0.928906262f;
                s.TimingGraphResultsY = 0.5f;
                s.TimingGraphResultsScale = 1.33563578f;
                s.TimingGraphResultsRotation = 90f;
                s.TimingGraphRangeMs = 50f;
                s.TimingScaleAnchorY = 0.113749996f;
                s.TimingGraphBuckets = 80;
                s.TimingGraphBackground = false;
                s.TimingGraphResultsPos = true;

                /* Curated layouts. Replaced wholesale rather than merged: a profile is a
                   snapshot, and a half-applied layout is worse than either version. */
                s.GameUiDefaultsSeeded = true;
                s.GameUiOverrides = new List<GameUiOverride>
                {
                    new GameUiOverride { Key = "presstostart", OffX = 0f, OffY = -400f, Scale = 0.4f, Rotation = 0f, Align = -1 },
                    new GameUiOverride { Key = "congrats", OffX = 0.41015625f, OffY = 5.00000048f, Scale = 0.85f, Rotation = 0f, Align = -1 },
                    new GameUiOverride { Key = "countdown", OffX = 0f, OffY = 70f, Scale = 0.75f, Rotation = 0f, Align = -1 },
                    new GameUiOverride { Key = "percent", OffX = 0f, OffY = -35f, Scale = 0.3f, Rotation = 0f, Align = -1 },
                    new GameUiOverride { Key = "results", OffX = -1.27156554E-05f, OffY = 31.4583359f, Scale = 0.8f, Rotation = 0f, Align = -1 },
                    new GameUiOverride { Key = "strictclear", OffX = -2.842171E-13f, OffY = 265f, Scale = 0.4f, Rotation = 0f, Align = -1 },
                    new GameUiOverride { Key = "autoplay", OffX = 653.060547f, OffY = -137.5f, Scale = 0.7254728f, Rotation = 0f, Align = 1 },
                };
                s.ResultsFields = new List<ResultsField>
                {
                    new ResultsField { Key = "perfectM", X = 0.924119234f, Y = 0.460000038f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "xPerfect", X = 0.9241193f, Y = 0.5f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "perfectP", X = 0.9241193f, Y = 0.54f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "ePerfect", X = 0.9241193f, Y = 0.420000017f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "lPerfect", X = 0.924119234f, Y = 0.57875f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "early", X = 0.924119234f, Y = 0.380625f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "late", X = 0.924119234f, Y = 0.618125f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "tooLate", X = 0.924119234f, Y = 0.657499969f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "tooEarly", X = 0.924119234f, Y = 0.340625f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "overloadFails", X = 0.924119234f, Y = 0.301875f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "missFails", X = 0.924119234f, Y = 0.696875f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "deaths", X = 0.9241193f, Y = 0.105f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "xScore", X = 0.924119234f, Y = 0.775625f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "xAccuracy", X = 0.9241193f, Y = 0.815625f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "accuracy", X = 0.924119234f, Y = 0.85375f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "practiceAttempts", X = 0.9241193f, Y = 0.145625f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "checkpoints", X = 0.9241193f, Y = 0.185624987f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                    new ResultsField { Key = "maximumUsedKeys", X = 0.9241193f, Y = 0.225625f, Scale = 1f, Rotation = 0f, LabelAlign = 0, ValueAlign = 2 },
                };
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
