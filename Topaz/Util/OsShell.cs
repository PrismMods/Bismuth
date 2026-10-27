using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Topaz
{
    internal static class OsShell
    {
        /* ProcessStartInfo with the loader-injection env stripped: MelonLoader's
           injected arm64 dylib (DYLD_INSERT_LIBRARIES) kills arm64e system
           binaries with dyld exit 134. Use for every child process we start. */
        internal static ProcessStartInfo CleanPsi(string file, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var injected = new List<string>();
            foreach (string k in psi.EnvironmentVariables.Keys)
                if (k.StartsWith("DYLD_", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("LD_PRELOAD", StringComparison.OrdinalIgnoreCase))
                    injected.Add(k);
            foreach (var k in injected)
                psi.EnvironmentVariables.Remove(k);
            return psi;
        }

        /* Open a folder in the OS file manager. OpenURL is only a fallback.
           It chokes on paths with spaces. */
        internal static void OpenFolder(string path)
        {
            try
            {
                string file;
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.WindowsEditor:
                        file = "explorer.exe";
                        break;
                    case RuntimePlatform.LinuxPlayer:
                        file = "xdg-open";
                        break;
                    default: // macOS
                        file = "open";
                        break;
                }
                Process.Start(CleanPsi(file, "\"" + path + "\""));
            }
            catch (Exception e)
            {
                TopazLog.Log("OpenFolder failed (" + e.Message + ") — falling back to OpenURL");
                try { Application.OpenURL(new Uri(path).AbsoluteUri); } catch { }
            }
        }
    }
}
