using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Bismuth
{
    /* Downloadable font packs. The build ships no fonts — they install here, into
       <mod>/Fonts/<PackId>/, which FontLoader.ScanFonts already picks up recursively.
       Install and remove are therefore just "unzip a folder" and "delete a folder";
       there is no per-file bookkeeping and nothing to keep in sync.

       Network work runs on the thread pool and lands in fields drained by Tick() on the
       main thread — same shape as UpdateChecker, and for the same reason (UnityWebRequest
       coroutines never resume under MelonLoader). Fetching is UpdateChecker's curl-first
       helper, so the one place that knows how to get bytes out of this game stays one. */
    internal static class FontPacks
    {
        /* A plain file in the repo, not a releases API call: the API is rate limited per IP
           and this list changes about once a year. The pack zips sit next to it in the repo
           and are fetched raw the same way, which also keeps the releases list free of
           anything that isn't a mod build. */
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/PrismMods/Bismuth/main/fonts.json";

        internal class Pack
        {
            public string Id;       // folder name under Fonts/, and the manifest key
            public string Name;     // display name
            public string Note;     // "9 weights · Korean + Latin"
            public string Size;     // "5.3 MB", as written in the manifest — no client-side math
            public string Url;
        }

        internal enum State { Idle, Loading, Ready, Failed }

        internal static State Status { get; private set; }
        internal static string StatusMessage { get; private set; }
        internal static List<Pack> Available = new List<Pack>();

        // Pack id currently downloading/installing, or null. One at a time: two unzips into
        // Fonts/ racing each other would both trigger a rescan mid-write.
        internal static string Busy { get; private set; }
        // Per-pack failure text, shown on that pack's row.
        internal static readonly Dictionary<string, string> Errors =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Panel redraws itself when this fires (rows show state, and install adds fonts).
        internal static Action OnChanged;

        private static readonly object _gate = new object();
        private static string _manifestJson;
        private static string _manifestError;
        private static byte[] _packZip;
        private static string _packZipId;
        private static string _packError;

        internal static string FontsDir =>
            Path.Combine(MainClass.ModPath ?? ".", "Fonts");

        internal static string InstallDir(string id) => Path.Combine(FontsDir, id);

        internal static bool IsInstalled(string id)
        {
            try { return Directory.Exists(InstallDir(id)); }
            catch { return false; }
        }

        // ── Manifest ───────────────────────────────────────────────────────

        internal static void Refresh()
        {
            if (Status == State.Loading) return;
            Status = State.Loading;
            StatusMessage = "";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] data = UpdateChecker.FetchBytes(ManifestUrl);
                    lock (_gate) _manifestJson = System.Text.Encoding.UTF8.GetString(data);
                }
                catch (Exception e) { lock (_gate) _manifestError = e.Message; }
            });
        }

        private static void ParseManifest(string json)
        {
            var packs = new List<Pack>();
            JToken root = JToken.Parse(json);
            if (!(root["Packs"] is JArray arr))
                throw new Exception("no Packs array");
            foreach (JToken p in arr)
            {
                string id = (string)p["Id"];
                string url = (string)p["Url"];
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url)) continue;
                packs.Add(new Pack
                {
                    Id = id,
                    Name = (string)p["Name"] ?? id,
                    Note = (string)p["Note"] ?? "",
                    Size = (string)p["Size"] ?? "",
                    Url = url,
                });
            }
            Available = packs;
            Status = State.Ready;
            BismuthLog.Log("Font packs: manifest listed " + packs.Count + " packs");
        }

        // ── Install / remove ───────────────────────────────────────────────

        internal static void Install(Pack pack)
        {
            if (pack == null || Busy != null) return;
            Busy = pack.Id;
            Errors.Remove(pack.Id);
            OnChanged?.Invoke();
            string url = pack.Url, id = pack.Id;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] data = UpdateChecker.FetchBytes(url);
                    BismuthLog.Log("Font pack '" + id + "': downloaded " + data.Length + " bytes");
                    lock (_gate) { _packZip = data; _packZipId = id; }
                }
                catch (Exception e) { lock (_gate) { _packError = e.Message; _packZipId = id; } }
            });
        }

        internal static bool Remove(string id, out string error)
        {
            error = null;
            if (Busy != null) { error = "busy"; return false; }
            try
            {
                string dir = InstallDir(id);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                BismuthLog.Log("Font pack '" + id + "': removed");
                /* A removed pack may be the font in use. The reload rescans, and every
                   resolver falls back (to fonts[0], or the game's font when none are left),
                   so the saved name can stay — reinstalling restores the choice. */
                MainClass.RequestForceReload();
                OnChanged?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Errors[id] = e.Message;
                BismuthLog.Log("Font pack '" + id + "' remove failed: " + e);
                return false;
            }
        }

        /* Extract into Fonts/<id>/ with the zip's own folders flattened away: the pack is a
           flat set of font files whatever shape it was zipped in, and a traversal entry
           ("../…") must not escape the install dir. */
        private static void Extract(byte[] zipBytes, string id)
        {
            string dir = InstallDir(id);
            string tmp = Path.Combine(Path.GetTempPath(), "bismuth-fontpack-" + id + ".zip");
            File.WriteAllBytes(tmp, zipBytes);
            try
            {
                Directory.CreateDirectory(dir);
                using (var archive = ZipFile.OpenRead(tmp))
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;   // directory
                        string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                        if (ext != ".ttf" && ext != ".otf" && ext != ".txt") continue;
                        entry.ExtractToFile(Path.Combine(dir, entry.Name), overwrite: true);
                    }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        // ── Main-thread drain (MainClass.OnUpdate) ─────────────────────────

        internal static void Tick()
        {
            string json, manifestErr, packErr, packId;
            byte[] zip;
            lock (_gate)
            {
                json = _manifestJson; _manifestJson = null;
                manifestErr = _manifestError; _manifestError = null;
                zip = _packZip; _packZip = null;
                packErr = _packError; _packError = null;
                packId = _packZipId;
                if (zip != null || packErr != null) _packZipId = null;
            }

            if (manifestErr != null)
            {
                Status = State.Failed;
                StatusMessage = manifestErr;
                BismuthLog.Log("Font packs: manifest fetch failed: " + manifestErr);
                OnChanged?.Invoke();
            }
            else if (json != null)
            {
                try { ParseManifest(json); }
                catch (Exception e)
                {
                    Status = State.Failed;
                    StatusMessage = e.Message;
                    BismuthLog.Log("Font packs: manifest parse failed: " + e.Message);
                }
                OnChanged?.Invoke();
            }

            if (packErr != null)
            {
                Errors[packId] = packErr;
                Busy = null;
                BismuthLog.Log("Font pack '" + packId + "' download failed: " + packErr);
                OnChanged?.Invoke();
            }
            else if (zip != null)
            {
                try
                {
                    Extract(zip, packId);
                    Busy = null;
                    BismuthLog.Log("Font pack '" + packId + "': installed");
                    /* Rescans fonts, rebuilds the panel so the pickers list them, and
                       re-applies — the same path as Misc → Debug → Force reload. It tears
                       the panel down, so it is deferred to the next frame, not run here. */
                    MainClass.RequestForceReload();
                }
                catch (Exception e)
                {
                    Busy = null;
                    Errors[packId] = e.Message;
                    BismuthLog.Log("Font pack '" + packId + "' install failed: " + e);
                }
                OnChanged?.Invoke();
            }
        }
    }
}
