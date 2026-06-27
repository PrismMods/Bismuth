using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using Bismuth.UI;
using UnityEngine;
using UnityModManagerNet;

namespace Bismuth
{
    /* In-mod updater for loaders that skip UMM's own (UMMCompat). On startup it
       checks Repository.json. When a newer version exists, UpdatePopup offers
       "Update now" (download the release zip and overwrite the mod payload in
       place) or a link to the releases page. User data (settings, keycounts,
       attempts, log) is never touched. The zip only holds the payload (dll,
       Info.json, Resources) and nothing is deleted.

       Networking is plain .NET on the thread pool, drained by Update() on the
       main thread. UnityWebRequest coroutines silently never resume under
       MelonLoader + UMMCompat: no timeout, no error, no completion. */
    internal class UpdateChecker : MonoBehaviour
    {
        /* UMMCompat doesn't populate modEntry.Info.Repository from Info.json, so
           the repo URL falls back to this hardcoded value. */
        private const string FallbackRepoUrl =
            "https://raw.githubusercontent.com/sbrothers7/Bismuth/main/Repository.json";

        private static UpdateChecker _inst;

        private string _repoUrl;
        private string _modPath;
        private Version _current;
        private string _downloadUrl;
        /* Existing install dirs, null when absent. Native UMM loads from
           <game>/Mods/, MelonLoader+UMMCompat from <game>/UMMMods/. Both can
           exist at once. */
        private string _modsDir;
        private string _ummModsDir;

        // Background-thread results, drained on main thread in Update()
        private readonly object _gate = new object();
        private string _repoJson;
        private string _checkError;
        private byte[] _zipBytes;
        private string _downloadError;

        /* Diagnostics. Both UnityWebRequest and the first WebClient attempt have
           died with no error, no timeout, no completion under MelonLoader+
           UMMCompat. Every stage logs so the log shows how far a check got. */
        private float _checkStartedAt;
        private bool _checkPending;
        private bool _tickLogged;
        private bool _watchdogLogged;

        internal static void Begin(UnityModManager.ModEntry modEntry)
        {
            if (_inst != null) return;
            if (modEntry == null) { BismuthLog.Log("Update check skipped: no mod entry"); return; }

            string repoUrl = modEntry.Info.Repository;
            if (string.IsNullOrEmpty(repoUrl))
            {
                BismuthLog.Log("Update check: Info.Repository empty (UMMCompat) — using fallback URL");
                repoUrl = FallbackRepoUrl;
            }

            var go = new GameObject("BismuthUpdateChecker");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<UpdateChecker>();
            _inst._repoUrl = repoUrl;
            _inst._modPath = modEntry.Path;
            if (!Version.TryParse(modEntry.Info.Version, out _inst._current))
                BismuthLog.Log("Update check: unparseable current version '" + modEntry.Info.Version + "'");

            _inst.DetectInstalls();
            bool duplicate = _inst._modsDir != null && _inst._ummModsDir != null;
            if (duplicate && MainClass.Settings != null && !MainClass.Settings.IgnoreDuplicateInstall)
                // Resolve the duplicate first. The check runs once the prompt closes.
                DuplicateInstallPopup.Show(_inst._modsDir, _inst._ummModsDir, _inst._modPath,
                    () => _inst?.StartCheck());
            else
                _inst.StartCheck();
        }

        internal static void Dispose()
        {
            if (_inst != null) Destroy(_inst.gameObject);
            _inst = null;
        }

        // ── Async fetches: thread pool to fields to Update drain ───────────

        private void StartCheck()
        {
            BismuthLog.Log($"Update check: v{_current} against {_repoUrl}");
            _checkStartedAt = Time.realtimeSinceStartup;
            _checkPending = true;
            string url = _repoUrl;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // BismuthLog is plain file IO, safe off main thread
                BismuthLog.Log("Update worker: thread started");
                try
                {
                    byte[] data = FetchBytes(url);
                    BismuthLog.Log("Update worker: fetched " + data.Length + " bytes");
                    lock (_gate) _repoJson = System.Text.Encoding.UTF8.GetString(data);
                }
                catch (Exception e) { lock (_gate) _checkError = e.Message; }
            });
        }

        private void StartDownload()
        {
            UpdatePopup.SetStatus("Downloading…", allowRetry: false);
            string url = _downloadUrl;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] data = FetchBytes(url);
                    BismuthLog.Log("Update worker: downloaded " + data.Length + " bytes");
                    lock (_gate) _zipBytes = data;
                }
                catch (Exception e) { lock (_gate) _downloadError = e.Message; }
            });
        }

        /* Shells out to curl (present on macOS, Windows 10+, and almost every
           Linux). Both UnityWebRequest and Mono WebClient have hung silently
           here. WebClient stays as a fallback when curl is missing or fails. */
        private static byte[] FetchBytes(string url)
        {
            try { return CurlFetch(url); }
            catch (Exception e)
            {
                BismuthLog.Log("Update worker: curl failed (" + e.Message + "), trying WebClient");
            }
            return WebClientFetch(url);
        }

        private static byte[] CurlFetch(string url)
        {
            var psi = OsShell.CleanPsi("curl", "-sL --max-time 30 \"" + url + "\"");
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (var p = System.Diagnostics.Process.Start(psi))
            using (var ms = new MemoryStream())
            {
                p.StandardOutput.BaseStream.CopyTo(ms);
                p.WaitForExit();
                if (p.ExitCode != 0)
                    throw new Exception("curl exit " + p.ExitCode + ": " + p.StandardError.ReadToEnd().Trim());
                return ms.ToArray();
            }
        }

        private static byte[] WebClientFetch(string url)
        {
            // GitHub requires TLS 1.2. Old Mono profiles don't enable it by default.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Bismuth-updater";
                return wc.DownloadData(url);
            }
        }

        private void Update()
        {
            if (!_tickLogged)
            {
                _tickLogged = true;
                BismuthLog.Log("Update checker: main-thread ticking confirmed");
            }

            string json, checkErr, dlErr;
            byte[] zip;
            lock (_gate)
            {
                json = _repoJson; _repoJson = null;
                checkErr = _checkError; _checkError = null;
                zip = _zipBytes; _zipBytes = null;
                dlErr = _downloadError; _downloadError = null;
            }

            if (_checkPending && !_watchdogLogged
                && Time.realtimeSinceStartup - _checkStartedAt > 20f)
            {
                _watchdogLogged = true;
                BismuthLog.Log("Update check still pending after 20s — network hang?");
            }

            if (checkErr != null)
            {
                _checkPending = false;
                BismuthLog.Log("Update check failed: " + checkErr);
            }
            if (json != null) _checkPending = false;

            if (json != null)
            {
                try { ParseAndMaybePrompt(json); }
                catch (Exception e) { BismuthLog.Log("Update check parse failed: " + e.Message); }
            }

            if (dlErr != null)
                UpdatePopup.SetStatus("Download failed: " + dlErr + " — try Manual update.", allowRetry: true);

            if (zip != null)
            {
                string error = null;
                try { Install(zip); }
                catch (Exception e)
                {
                    error = e.Message;
                    BismuthLog.Log("Update install failed: " + e);
                }
                if (error == null)
                    UpdatePopup.SetDone("Updated. Restart the game to apply.");
                else
                    UpdatePopup.SetStatus("Install failed: " + error + " — try Manual update.", allowRetry: true);
            }
        }

        // ── Version check ──────────────────────────────────────────────────

        private void ParseAndMaybePrompt(string json)
        {
            /* Repository.json has a trivial fixed shape, and JsonUtility silently
               produced nothing for it here, so parse the two fields directly. */
            var vm = System.Text.RegularExpressions.Regex.Match(json, "\"Version\"\\s*:\\s*\"([^\"]+)\"");
            var dm = System.Text.RegularExpressions.Regex.Match(json, "\"DownloadUrl\"\\s*:\\s*\"([^\"]+)\"");
            if (!vm.Success)
            {
                BismuthLog.Log("Update check: no Version field in repository json: " +
                    (json.Length > 200 ? json.Substring(0, 200) + "…" : json));
                return;
            }
            if (!Version.TryParse(vm.Groups[1].Value, out Version latest))
            {
                BismuthLog.Log("Update check: unparseable release version '" + vm.Groups[1].Value + "'");
                return;
            }
            if (_current != null && latest <= _current)
            {
                BismuthLog.Log($"Update check: up to date (v{_current})");
                return;
            }
            if (!dm.Success)
            {
                BismuthLog.Log("Update check: newer version but no DownloadUrl in repository json");
                return;
            }
            string releaseUrl = dm.Groups[1].Value;

            _downloadUrl = releaseUrl;
            // ".../releases/download/v1.2.3/Bismuth.zip" → ".../releases"
            int cut = _downloadUrl?.IndexOf("/download/", StringComparison.Ordinal) ?? -1;
            string releasesPage = cut > 0 ? _downloadUrl.Substring(0, cut)
                                          : "https://github.com/sbrothers7/Bismuth/releases";

            BismuthLog.Log($"Update available: v{_current} → v{latest}");
            UpdatePopup.Show(_current?.ToString() ?? "?", latest.ToString(), releasesPage,
                () => StartDownload());
        }

        // ── Install ────────────────────────────────────────────────────────

        private void Install(byte[] zipBytes)
        {
            /* Flush user data before touching anything, so the current session
               state is on disk no matter what happens next. */
            MainClass.PersistNow();

            /* Update every existing install so a deliberately kept duplicate
               (Mods/ + UMMMods/) can't drift to a stale version. Running copy
               goes last. */
            var targets = new List<string>();
            if (_modsDir != null && !SamePath(_modsDir, _modPath)) targets.Add(_modsDir);
            if (_ummModsDir != null && !SamePath(_ummModsDir, _modPath)) targets.Add(_ummModsDir);
            if (Directory.Exists(_modPath)) targets.Add(_modPath);
            if (targets.Count == 0) targets.Add(_modPath);

            string tmp = Path.Combine(Path.GetTempPath(), "bismuth-update.zip");
            File.WriteAllBytes(tmp, zipBytes);
            try
            {
                using (var archive = ZipFile.OpenRead(tmp))
                {
                    foreach (string target in targets)
                    {
                        /* dll written LAST. UMM watches it for hot reload, so the
                           reload must not fire while Info.json/Resources are still old. */
                        var deferred = new List<ZipArchiveEntry>();
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue; // directory
                            if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                deferred.Add(entry);
                            else
                                ExtractEntry(entry, target);
                        }
                        foreach (var entry in deferred)
                            ExtractEntry(entry, target);
                    }
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private static void ExtractEntry(ZipArchiveEntry entry, string targetDir)
        {
            // Zip layout is "Bismuth/<payload>", strip root folder
            string rel = entry.FullName.Replace('\\', '/');
            if (rel.StartsWith("Bismuth/", StringComparison.OrdinalIgnoreCase))
                rel = rel.Substring("Bismuth/".Length);
            if (rel.Length == 0 || rel.Contains("..")) return;

            string dest = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            entry.ExtractToFile(dest, overwrite: true);
            BismuthLog.Log("Updated file: " + dest);
        }

        // ── Duplicate install handling ─────────────────────────────────────

        private void DetectInstalls()
        {
            try
            {
                // _modPath = <game root>/<loader folder>/Bismuth
                string active = Norm(_modPath);
                string root = Path.GetDirectoryName(Path.GetDirectoryName(active));
                string mods = Path.Combine(root, "Mods", "Bismuth");
                string umm = Path.Combine(root, "UMMMods", "Bismuth");
                if (File.Exists(Path.Combine(mods, "Info.json"))) _modsDir = mods;
                if (File.Exists(Path.Combine(umm, "Info.json"))) _ummModsDir = umm;
            }
            catch (Exception e)
            {
                BismuthLog.Log("Install detection failed: " + e.Message);
            }
        }

        /* Deletes the unused copy. When it's the running one, the freshest user
           data lives there, so flush it and carry it over to the kept copy. */
        internal static bool DeleteInstall(string keepDir, string deleteDir, out string error, out bool deletedActive)
        {
            error = null;
            deletedActive = false;
            try
            {
                deletedActive = SamePath(deleteDir, _inst._modPath);
                if (deletedActive)
                {
                    MainClass.PersistNow();
                    foreach (var f in new[] { "Settings.xml", "keycounts.txt", "BismuthAttempts.txt" })
                    {
                        string src = Path.Combine(deleteDir, f);
                        if (!File.Exists(src)) continue;
                        Directory.CreateDirectory(keepDir);
                        File.Copy(src, Path.Combine(keepDir, f), true);
                    }
                }
                Directory.Delete(deleteDir, true);
                if (SamePath(deleteDir, _inst._modsDir)) _inst._modsDir = null;
                if (SamePath(deleteDir, _inst._ummModsDir)) _inst._ummModsDir = null;
                BismuthLog.Log("Deleted duplicate install: " + deleteDir);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                BismuthLog.Log("Duplicate delete failed: " + e);
                return false;
            }
        }

        internal static void MarkKeepBoth()
        {
            if (MainClass.Settings != null) MainClass.Settings.IgnoreDuplicateInstall = true;
            MainClass.PersistNow();
        }

        private static bool SamePath(string a, string b)
        {
            if (a == null || b == null) return false;
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }

        private static string Norm(string p) =>
            Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, '/');
    }
}
