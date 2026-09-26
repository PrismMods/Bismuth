using System;
using System.IO;

namespace Bismuth
{
    internal static class BismuthLog
    {
        private static string _path;

        internal static void Init()
        {
            _path = Path.Combine(MainClass.ModPath, "BismuthLog.txt");
            try
            {
                File.WriteAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Bismuth session start\n");
            }
            catch { _path = null; }
        }

        internal static void Log(string message)
        {
            if (_path == null) return;
            try { File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss}] {message}\n"); }
            catch { }
        }

        // Truncate the log file (Clear button in the log viewer).
        internal static void Clear()
        {
            if (_path == null) return;
            try { File.WriteAllText(_path, $"[{DateTime.Now:HH:mm:ss}] log cleared\n"); }
            catch { }
        }

        /* High-frequency diagnostics: hook traces, per-attempt dumps. Written to file
           like everything else, but in-game viewer hides [dbg] lines unless Debug
           toggle on */
        internal static void Debug(string message) => Log("[dbg] " + message);

        /// Where the file lives, for the debug window's "open folder" button. Null if init failed.
        internal static string LogPath => _path;

        /* Tail of the current log for an in-game viewer. The default cap is a uGUI vertex budget
           (~4 verts per glyph, 65k limit) from the old uGUI viewer; PrismLib.UI's list virtualises,
           so the shared debug window asks for far more. */
        internal static string ReadTail(int maxChars = 12000)
        {
            if (_path == null) return "(log not initialized)";
            try
            {
                string s = File.ReadAllText(_path);
                if (s.Length <= maxChars) return s;
                s = s.Substring(s.Length - maxChars);
                int nl = s.IndexOf('\n');
                return "…\n" + (nl >= 0 ? s.Substring(nl + 1) : s);
            }
            catch (Exception e)
            {
                return "(log unavailable: " + e.Message + ")";
            }
        }
    }
}
