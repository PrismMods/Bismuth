using System.Collections.Generic;
using System.IO;

namespace Topaz
{
    internal static class AttemptsStore
    {
        private static Dictionary<string, int> _data;
        private static string FilePath => Path.Combine(MainClass.ModPath, "TopazAttempts.txt");

        /* Full-attempt entries share same file via non-colliding key prefix. Level
           names/paths never start with "F::" on any real ADOFAI install */
        private const string FullPrefix = "F::";

        private static void EnsureLoaded()
        {
            if (_data != null) return;
            _data = new Dictionary<string, int>();
            string path = FilePath;
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                int sep = line.IndexOf('=');
                if (sep < 0) continue;
                string key = line.Substring(0, sep);
                if (int.TryParse(line.Substring(sep + 1), out int val))
                    _data[key] = val;
            }
        }

        public static int Get(string key)
        {
            if (key == null) return 0;
            EnsureLoaded();
            return _data.TryGetValue(key, out int v) ? v : 0;
        }

        public static void Set(string key, int value)
        {
            if (key == null) return;
            EnsureLoaded();
            _data[key] = value;
            Save();
        }

        public static int GetFull(string key)
        {
            return key == null ? 0 : Get(FullPrefix + key);
        }

        public static void SetFull(string key, int value)
        {
            if (key == null) return;
            Set(FullPrefix + key, value);
        }

        /* Best progress (0..1) shares the int store as basis points (percent × 100). */
        private const string BestPrefix = "B::";

        public static float GetBest(string key)
        {
            return key == null ? 0f : Get(BestPrefix + key) / 10000f;
        }

        public static void SetBest(string key, float t)
        {
            if (key == null) return;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            Set(BestPrefix + key, (int)(t * 10000f + 0.5f));
        }

        /* One-time carry-over when a level's key scheme changes (e.g. path-based ->
           content hash). Moves both the regular and full-attempt entries from oldKey
           to newKey, but never clobbers an existing newKey. No-op if oldKey is null,
           equal to newKey, or absent. */
        public static void Migrate(string oldKey, string newKey)
        {
            if (oldKey == null || newKey == null || oldKey == newKey) return;
            EnsureLoaded();
            bool changed = false;
            changed |= Move(oldKey, newKey);
            changed |= Move(FullPrefix + oldKey, FullPrefix + newKey);
            changed |= Move(BestPrefix + oldKey, BestPrefix + newKey);
            if (changed) Save();
        }

        private static bool Move(string from, string to)
        {
            if (_data.ContainsKey(to) || !_data.TryGetValue(from, out int v)) return false;
            _data[to] = v;
            _data.Remove(from);
            return true;
        }

        public static void ClearAll()
        {
            EnsureLoaded();
            _data.Clear();
            Save();
        }

        private static void Save()
        {
            var lines = new List<string>();
            foreach (var kv in _data)
                lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(FilePath, lines.ToArray());
        }
    }
}
