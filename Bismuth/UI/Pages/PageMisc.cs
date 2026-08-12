using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static class PageMisc
    {
        // Updated from MainClass.OnSceneUnloaded so the readout stays live while the
        // panel is open (IMGUI re-read it every draw; uGUI text is built once).
        private static TextMeshProUGUI _savingsText;

        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeader(content, "Misc");

            var savingsRow = UIBuilder.Row(content);
            _savingsText = UIBuilder.Label(savingsRow.transform, SavingsLabel(), (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.TextMuted);
            _savingsText.rectTransform.offsetMin = new Vector2(8f, 0f);

            UIBuilder.Button(content, "View log", LogViewer.Show);
            BuildUpdates(stack, content, s, notify);

            UIBuilder.Spacer(content);

            // Debug sits under Profiles — its dumps/sweep traces write to the log above.
            BuildDebug(content, s, notify);

            UIBuilder.Spacer(content);
            BuildOptimizations(stack, content, s, notify);
        }

        // Developer tools, revealed by the Debug mode toggle. Polls live game objects /
        // assets and dumps their references to the log (Misc → View log) — see GameProbe.
        private static void BuildDebug(Transform content, Settings s, System.Action notify)
        {
            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Debug");

            GameObject tools = null;
            UIBuilder.Toggle(content, "Debug mode", s.DebugMode, v =>
            {
                s.DebugMode = v;
                if (tools != null) tools.SetActive(v);
                notify?.Invoke();
            });

            tools = UIBuilder.Rect("DebugTools", content);
            var vlg = tools.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            var t = tools.transform;

            // Re-scan fonts (pick up newly dropped Fonts/ files), rebuild the panel, and
            // reapply everything — a soft reload without the UMM Ctrl+F10.
            UIBuilder.Button(t, "Force reload", MainClass.RequestForceReload);

            UIBuilder.TextInput(t, "Filter", GameProbe.Filter, v =>
            {
                GameProbe.Filter = v ?? "";
                if (GameFontApplier.DiagEnabled)
                    GameFontApplier.DiagFilter = string.IsNullOrEmpty(GameProbe.Filter) ? null : new[] { GameProbe.Filter };
            });
            UIBuilder.Button(t, "Dump texts", GameProbe.DumpTexts);
            UIBuilder.Button(t, "Dump images", GameProbe.DumpImages);
            UIBuilder.Button(t, "Dump assets (sprites/textures)", GameProbe.DumpAssets);

            string compType = "";
            UIBuilder.TextInput(t, "Component type", compType, v => compType = v);
            UIBuilder.Button(t, "Dump components", () => GameProbe.DumpComponents(compType));

            UIBuilder.Toggle(t, "Trace font sweep", GameFontApplier.DiagEnabled, v =>
            {
                GameFontApplier.DiagEnabled = v;
                GameFontApplier.DiagFilter = string.IsNullOrEmpty(GameProbe.Filter) ? null : new[] { GameProbe.Filter };
            });

            tools.SetActive(s.DebugMode);
        }

        public static void RefreshSavings()
        {
            if (_savingsText != null) _savingsText.text = SavingsLabel();
        }

        private static string SavingsLabel()
        {
            string savings;
            long bytes = MainClass.LastUnloadSavingsBytes;
            if (bytes < 0) savings = "----MB";
            else
            {
                float mb = bytes / (1024f * 1024f);
                savings = (mb >= 0f ? "+" : "") + mb.ToString("F2") + " MB";
            }
            return "RAM savings (last scene load): " + savings;
        }

        // Wrapping muted caption under a toggle. The page VLG controls child rects, so the
        // indent comes from a padded wrapper group rather than offsetMin; Wrap + the inner
        // group's childControlHeight lets the Text's preferred height drive the row height.
        private static void Desc(Transform parent, string text)
        {
            text = Loc.T(text);
            var wrap = UIBuilder.Rect("Desc", parent);
            var vlg = wrap.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(10, 4, 0, 6);

            var t = UIBuilder.Label(wrap.transform, text, (int)UIBuilder.LabelFontSize - 2, TextAnchor.UpperLeft, Theme.TextMuted);
            t.textWrappingMode = TextWrappingModes.Normal;
        }
        // Stored in Settings.UpdateChannel, so these stay English; the selector shows Loc.T of
        // each (UpdateChecker.Channel matches the enum name, never the displayed text).
        private static readonly string[] ChannelNames = { "Stable", "Beta", "Alpha" };

        /* Updates subpage: what's installed, which channel it follows, and the actions.
           Channels are a risk CEILING, not a filter — Alpha takes beta and stable builds too
           (UpdateChecker.ParseAndMaybePrompt) — so the newest build the channel allows is what
           the button installs, even when that means moving BACK to an older stable. */
        private static void BuildUpdates(PageStack stack, Transform content, Settings s, System.Action notify)
        {
            UIBuilder.NavRow(content, "Updates", () => stack.Push("Updates", body =>
            {
                UIBuilder.Label(body, Loc.T("Installed") + ": " + UpdateChecker.InstalledVersion);
                var status = UIBuilder.Label(body, StatusLine(), (int)UIBuilder.LabelFontSize,
                    TextAnchor.MiddleLeft, Theme.TextMuted);

                int idx = (int)UpdateChecker.Channel;
                var shown = new string[ChannelNames.Length];
                for (int i = 0; i < ChannelNames.Length; i++) shown[i] = Loc.T(ChannelNames[i]);
                UIBuilder.CycleSelector(body, "Build channel", shown, idx, i =>
                {
                    s.UpdateChannel = ChannelNames[i];
                    notify?.Invoke();
                    // The channel decides which releases count, so the answer on screen is
                    // stale the moment it changes.
                    UpdateChecker.CheckNow();
                });
                Desc(body, "Alpha and Beta include everything below them. Switching to a lower channel offers the newest build there, even if that is older than what you have.");

                UIBuilder.Button(body, "Check now", UpdateChecker.CheckNow);
                // Built with a placeholder and hidden until a check resolves — the poller owns
                // both the label and whether there is anything to install at all.
                string act = ActionLabel();
                var action = UIBuilder.Button(body, act ?? Loc.T("Install update"), UpdateChecker.InstallLatest);
                action.SetActive(act != null);
                UIBuilder.Button(body, "Open releases page",
                    () => Application.OpenURL(UpdateChecker.ReleasesPage));

                var poll = UIBuilder.Rect("UpdateStatusPoll", body).AddComponent<StatusPoller>();
                poll.Label = status;
                poll.Action = action;
                poll.SeedAction(act);
            }), "update version channel beta alpha stable release");
        }

        /* Already localized on return: the version tags interpolated in here mean the finished
           sentence can never be a table key, so each translatable piece is looked up on its own
           and the tag is appended around it. */
        private static string StatusLine()
        {
            if (!UpdateChecker.Ready) return Loc.T("Update checks are not running");
            switch (UpdateChecker.Status)
            {
                case UpdateChecker.State.Checking:   return Loc.T("Checking…");
                case UpdateChecker.State.UpToDate:
                    return UpdateChecker.LatestTag == null
                        ? Loc.T("No builds published on this channel")
                        : Loc.T("Up to date") + " — " + UpdateChecker.LatestTag;
                case UpdateChecker.State.Available:  return Loc.T("Available") + ": " + UpdateChecker.LatestTag;
                case UpdateChecker.State.Installing: return Loc.T("Downloading…");
                case UpdateChecker.State.Installed:  return Loc.T("Installed — restart the game to apply");
                case UpdateChecker.State.Failed:     return Loc.T("Failed") + ": " + UpdateChecker.StatusMessage;
                default:                             return Loc.T("Not checked yet");
            }
        }

        // Null when there is nothing to install, so the poller can hide the button. The tag
        // is a placeholder rather than a prefix — Korean puts the version before the verb.
        private static string ActionLabel()
        {
            if (!UpdateChecker.Ready || UpdateChecker.LatestTag == null) return null;
            if (UpdateChecker.Status == UpdateChecker.State.Installing) return null;
            if (UpdateChecker.Status == UpdateChecker.State.Installed) return null;
            return Loc.T(UpdateChecker.LatestIsNewer ? "Update to {0}" : "Switch to {0}")
                .Replace("{0}", UpdateChecker.LatestTag);
        }

        /* The checker runs its network work on the thread pool and reports through plain
           static fields, so the page polls instead of subscribing — same shape the savings
           readout uses, and nothing can call back into Unity off-thread. */
        private class StatusPoller : MonoBehaviour
        {
            public TextMeshProUGUI Label;
            public GameObject Action;
            private string _last, _lastAction;
            private int _cd;

            // What the page already rendered, so the first tick only reacts to real changes.
            public void SeedAction(string label) { _lastAction = label; }

            private void Update()
            {
                if (--_cd > 0) return;
                _cd = 10;   // ~6/s, far below a per-frame cost
                // Both come back localized already.
                string now = StatusLine();
                if (Label != null && now != _last) { _last = now; Label.text = now; }

                string act = ActionLabel();
                if (Action == null || act == _lastAction) return;
                _lastAction = act;
                Action.SetActive(act != null);
                if (act != null)
                {
                    var t = Action.GetComponentInChildren<TextMeshProUGUI>();
                    if (t != null) t.text = act;
                }
            }
        }

        // Optimizations drill into a subpage; the NavRow's ring is the master switch
        // (Settings.OptimizationsEnabled gates every flag below it).
        private static void BuildOptimizations(PageStack stack, Transform content, Settings s, System.Action notify)
        {
            UIBuilder.NavRow(content, "Optimizations", s.OptimizationsEnabled,
                v => { s.OptimizationsEnabled = v; notify?.Invoke(); },
                () => stack.Push("Optimizations", body =>
                {
                    UIBuilder.Collapsible(body, "Spectrum Throttle (every 2nd frame)", s.OptSpectrumThrottle,
                        v => { s.OptSpectrumThrottle = v; notify?.Invoke(); }, null);
                    Desc(body, "Halves AudioSource.GetSpectrumData FFT cost on levels that use audio visualization.");

                    UIBuilder.Collapsible(body, "Texture Non-Readable", s.OptTextureNonReadable,
                        v => { s.OptTextureNonReadable = v; notify?.Invoke(); }, null);
                    Desc(body, "Frees CPU-side pixel data after GPU upload. Halves RAM per custom level texture.");

                    UIBuilder.Collapsible(body, "DXT Compression (lossy)", s.OptTextureDXT,
                        v => { s.OptTextureDXT = v; notify?.Invoke(); }, null);
                    Desc(body, "Compresses textures to DXT before upload. 4-6x VRAM savings, slight quality loss. Requires Non-Readable.");

                    UIBuilder.Collapsible(body, "Physics NonAlloc", s.OptPhysicsNonAlloc,
                        v => { s.OptPhysicsNonAlloc = v; notify?.Invoke(); }, null);
                    Desc(body, "Eliminates per-frame Collider2D[] allocation from decoration hitbox checks.");

                    UIBuilder.Collapsible(body, "Unload Assets on Scene Change", s.OptUnloadAssets,
                        v => { s.OptUnloadAssets = v; notify?.Invoke(); }, null);
                    Desc(body, "Forces GC and unloads unused textures/audio between levels to reclaim memory.");

                    UIBuilder.Collapsible(body, "Volume Track DOTween Fix", s.OptVolumeTrackDOTween,
                        v => { s.OptVolumeTrackDOTween = v; notify?.Invoke(); }, null);
                    Desc(body, "Prevents abandoned DOTween sequences from being created every frame on Volume-type track colors.");

                    UIBuilder.Collapsible(body, "Skip No-Op Screen Filters", s.OptSkipNoOpScreenFilters,
                        v => { s.OptSkipNoOpScreenFilters = v; notify?.Invoke(); }, null);
                    Desc(body, "Skips the screen tile/scroll shader passes when they are set to do nothing.");

                    UIBuilder.Collapsible(body, "Leak Guard", s.OptLeakGuard,
                        v => { s.OptLeakGuard = v; notify?.Invoke(); }, null);
                    Desc(body, "Frees textures the game replaces without destroying (decorations, camera, thumbnails, waveforms).");

                    UIBuilder.Collapsible(body, "Fast Bloom", s.OptFastBloom,
                        v => { s.OptFastBloom = v; notify?.Invoke(); }, null);
                    Desc(body, "Drops bloom to its cheaper path. Visibly softer glow — off by default.");

                    UIBuilder.Collapsible(body, "Render All Hit Sounds", s.OptRenderAllHitSounds,
                        v => { s.OptRenderAllHitSounds = v; if (!v) HitSoundRenderer.StopAll("setting off"); notify?.Invoke(); }, null);
                    Desc(body, "Pre-mixes hit sounds on a background thread instead of scheduling one voice each. Experimental — check timing by ear.");
                }),
                "spectrum throttle, texture non-readable, dxt compression, physics nonalloc, unload assets, dotween, volume track, screen filters, tile, scroll, leak guard, vram, textures, bloom, hit sounds, audio, performance, ram");
        }
    }
}
