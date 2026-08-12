using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using Bismuth.UI;
using Newtonsoft.Json.Linq;
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
        /* The releases list, not Repository.json: that file only ever names one version, and
           channels need to see every published tag to pick the newest one at or below the
           chosen risk level. */
        private const string ApiUrl =
            "https://api.github.com/repos/PrismMods/Bismuth/releases?per_page=30";
        internal const string ReleasesPage = "https://github.com/PrismMods/Bismuth/releases";

        private static UpdateChecker _inst;

        private string _modPath;
        private SemVer _current;
        private bool _currentKnown;
        private string _downloadUrl;

        // ── Panel-facing state (read on the main thread by PageMisc) ───────
        internal enum State { Idle, Checking, UpToDate, Available, Installing, Installed, Failed }

        internal static State Status { get; private set; }
        internal static string StatusMessage { get; private set; }
        // Newest release at or below the selected channel; may be OLDER than what's installed
        // (picking Stable while running a beta), which the panel offers as a switch.
        internal static string LatestTag { get; private set; }
        internal static bool LatestIsNewer { get; private set; }
        internal static bool Ready { get { return _inst != null; } }
        internal static string InstalledVersion
        {
            get { return _inst != null && _inst._currentKnown ? _inst._current.ToString() : "?"; }
        }

        internal static UpdateChannel Channel
        {
            get
            {
                string set = MainClass.Settings != null ? MainClass.Settings.UpdateChannel : null;
                if (!string.IsNullOrEmpty(set))
                    foreach (UpdateChannel c in Enum.GetValues(typeof(UpdateChannel)))
                        if (string.Equals(set, c.ToString(), StringComparison.OrdinalIgnoreCase)) return c;
                // Unset: follow the build that's installed, so a stable user isn't opted into
                // betas and a beta tester keeps getting them.
                return _inst != null && _inst._currentKnown ? _inst._current.Channel : UpdateChannel.Stable;
            }
        }

        // Manual re-check from the panel, e.g. right after switching channel.
        internal static void CheckNow()
        {
            if (_inst == null || Status == State.Checking || Status == State.Installing) return;
            _inst.StartCheck();
        }

        // Panel action: install whatever the current channel resolved to, newer or older.
        internal static void InstallLatest()
        {
            if (_inst == null || string.IsNullOrEmpty(_inst._downloadUrl)) return;
            if (Status == State.Checking || Status == State.Installing) return;
            Status = State.Installing;
            StatusMessage = "";
            _inst.StartDownload();
        }
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

            var go = new GameObject("BismuthUpdateChecker");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<UpdateChecker>();
            _inst._modPath = modEntry.Path;
            /* SemVer, not System.Version: the suffix on a prerelease ("1.3.4-b1") is what
               decides its channel, and System.Version can't even parse it. An unparsed
               current version leaves _currentKnown false, which suppresses the prompt rather
               than popping it at every launch. */
            _inst._currentKnown = SemVer.TryParse(modEntry.Info.Version, out _inst._current);
            if (!_inst._currentKnown)
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
            BismuthLog.Log($"Update check: v{_current} on the {Channel} channel");
            Status = State.Checking;
            StatusMessage = "";
            _checkStartedAt = Time.realtimeSinceStartup;
            _checkPending = true;
            string url = ApiUrl;
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
                Status = State.Failed;
                StatusMessage = checkErr;
                BismuthLog.Log("Update check failed: " + checkErr);
            }
            if (json != null) _checkPending = false;

            if (json != null)
            {
                try { ParseAndMaybePrompt(json); }
                catch (Exception e)
                {
                    Status = State.Failed;
                    StatusMessage = e.Message;
                    BismuthLog.Log("Update check parse failed: " + e.Message);
                }
            }

            if (dlErr != null)
            {
                Status = State.Failed;
                StatusMessage = dlErr;
                UpdatePopup.SetStatus("Download failed: " + dlErr + " — try Manual update.", allowRetry: true);
            }

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
                {
                    Status = State.Installed;
                    StatusMessage = LatestTag ?? "";
                    UpdatePopup.SetDone("Updated. Restart the game to apply.");
                }
                else
                {
                    Status = State.Failed;
                    StatusMessage = error;
                    UpdatePopup.SetStatus("Install failed: " + error + " — try Manual update.", allowRetry: true);
                }
            }
        }

        // ── Version check ──────────────────────────────────────────────────

        /* Picks the newest release at or below the selected channel. Cumulative by design:
           Alpha accepts beta and stable builds too, so nobody is stranded on an old alpha
           once a newer stable ships. A pick that is OLDER than what's installed is kept —
           that's the channel switch (running a beta, selecting Stable), offered from the
           panel but never auto-prompted. */
        private void ParseAndMaybePrompt(string json)
        {
            UpdateChannel channel = Channel;
            /* A rate-limited or errored API answers with an object ({"message": "API rate
               limit exceeded…"}), not an array. Parsing that as JArray throws a JSON error
               that tells the user nothing — read the message out instead. */
            JToken root = JToken.Parse(json);
            if (!(root is JArray releases))
            {
                string msg = (string)root["message"] ?? "unexpected response";
                Status = State.Failed;
                StatusMessage = msg;
                BismuthLog.Log("Update check: GitHub said '" + msg + "'");
                return;
            }

            SemVer bestVer = new SemVer();
            string bestTag = null, bestUrl = null;
            foreach (JToken rel in releases)
            {
                if ((bool?)rel["draft"] == true) continue;
                string tag = (string)rel["tag_name"];
                if (string.IsNullOrEmpty(tag)) continue;
                if (!SemVer.TryParse(tag, out SemVer v)) continue;
                if (v.Channel > channel) continue;
                if (bestTag != null && v.CompareTo(bestVer) <= 0) continue;

                string url = null;
                if (rel["assets"] is JArray assets)
                    foreach (JToken a in assets)
                    {
                        string name = (string)a["name"];
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!name.StartsWith("Bismuth", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                        url = (string)a["browser_download_url"];
                        break;
                    }
                if (url == null) continue;   // a release with no installable zip is not a release

                bestVer = v; bestTag = tag; bestUrl = url;
            }

            if (bestTag == null)
            {
                LatestTag = null;
                LatestIsNewer = false;
                _downloadUrl = null;
                Status = State.UpToDate;
                BismuthLog.Log($"Update check: no {channel}-channel release with a zip asset");
                return;
            }

            LatestTag = bestTag;
            // An unparseable installed version can't be compared, so nothing counts as newer:
            // the alternative is prompting every launch. The panel still offers the install.
            LatestIsNewer = _currentKnown && bestVer.CompareTo(_current) > 0;
            _downloadUrl = bestUrl;
            Status = LatestIsNewer ? State.Available : State.UpToDate;

            if (!LatestIsNewer)
            {
                BismuthLog.Log($"Update check: up to date (v{_current}, {channel} channel newest is {bestTag})");
                return;
            }

            BismuthLog.Log($"Update available: v{_current} → {bestTag} ({channel} channel)");
            UpdatePopup.Show(_currentKnown ? _current.ToString() : "?", bestTag, ReleasesPage,
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
                    Directory.CreateDirectory(keepDir);
                    // Settings.xml stays locked under Wine even against permissive
                    // FileShare (sharing violation seen in a Proton tester's log) —
                    // serialize the live settings straight into the kept dir instead
                    // of copying the file. Same XmlSerializer format UMM loads with.
                    if (MainClass.Settings != null)
                        using (var w = new StreamWriter(Path.Combine(keepDir, "Settings.xml"), false))
                            new System.Xml.Serialization.XmlSerializer(typeof(Settings)).Serialize(w, MainClass.Settings);
                    foreach (var f in new[] { "keycounts.txt", "BismuthAttempts.txt" })
                    {
                        string src = Path.Combine(deleteDir, f);
                        if (!File.Exists(src)) continue;
                        CopyShared(src, Path.Combine(keepDir, f));
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

        // File.Copy demands exclusive read on the source — under Wine/Proton the active
        // install's Settings.xml keeps an open handle, which failed the duplicate delete
        // every launch. Stream-copy with permissive sharing instead.
        private static void CopyShared(string src, string dst)
        {
            using (var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var d = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
                s.CopyTo(d);
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
