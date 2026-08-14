using System;
using System.Collections.Generic;
using System.Reflection;
using Bismuth.UI;
using Bismuth.UI.Pages;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace Bismuth
{
    public static class MainClass
    {
        public static bool IsEnabled { get; private set; }
        public static Settings Settings { get; private set; }
        public static UnityModManager.ModEntry.ModLogger Logger { get; private set; }
        public static string ModPath { get; private set; }
        // Bytes reclaimed by the last Resources.UnloadUnusedAssets() on scene unload. -1 = no measurement yet.
        public static long LastUnloadSavingsBytes { get; private set; } = -1;

        private static Harmony harmony;
        private static Overlay overlay;
        private static KeyViewer keyViewer;
        private static List<FontLoader.FontEntry> availableFonts = new List<FontLoader.FontEntry>();
        // Retry init on first scene load when koren UMM loaded us before game statics were ready.
        private static bool _deferredApplyPending;
        // Force-reload request (Misc → Debug). Deferred to OnUpdate so the panel isn't rebuilt
        // inside its own button's click handler.
        private static bool _forceReloadPending;
        // Old font assets pending destruction after a force reload (dropped a few frames later,
        // once re-apply has settled — see OnUpdate). Time.frameCount to destroy at, or -1.
        private static List<FontLoader.FontEntry> _oldFonts;
        private static int _destroyOldFontsFrame = -1;

        internal static void Setup(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            ModPath = modEntry.Path;
            Settings = Settings.Load<Settings>(modEntry);
            Settings.EnsureDefaults();
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = (_, __) =>
            {
                UICore.HandleUpdate();
                if (_forceReloadPending) { _forceReloadPending = false; DoForceReload(); }
                // Drop the previous font assets a beat after a force reload — only once every
                // re-apply (incl. GameFontApplier's frame-spread sweep) has re-pointed text off
                // them, so nothing renders against a just-destroyed material.
                if (_destroyOldFontsFrame > 0 && Time.frameCount >= _destroyOldFontsFrame)
                {
                    _destroyOldFontsFrame = -1;
                    FontLoader.DestroyTmpAssets(_oldFonts);
                    _oldFonts = null;
                    overlay?.DumpDebug("post-font-destroy");
                }
            };
            // Opting into OnUnload makes the mod hot-reloadable: UMM watches the dll and
            // reloads in-place when it changes, instead of requiring a game restart.
            modEntry.OnUnload = OnUnload;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            IsEnabled = value;
            if (value) StartMod(modEntry);
            else StopMod(modEntry);
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Settings live in the in-game panel (Ctrl+B).");
            if (GUILayout.Button("Open Settings Panel", GUILayout.ExpandWidth(false)))
                UICore.Open();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            keyViewer?.SaveCounts();
            Settings.Save(modEntry);
        }

        // Flush all persistent data now. Used by UpdateChecker before replacing files.
        internal static void PersistNow()
        {
            if (_modEntry == null) return;
            OnSaveGUI(_modEntry);
        }

        // Tear everything down so the freshly loaded assembly starts from a clean slate.
        // The old assembly stays in memory (Mono can't unload it), but nothing of ours
        // may survive in the scene: DDOL GameObjects, Harmony patches, scene callbacks.
        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            if (LocationEditor.IsActive) LocationEditor.Close();
            if (GameUiEditor.IsActive) GameUiEditor.Close();
            OnSaveGUI(modEntry);
            if (IsEnabled) StopMod(modEntry);
            return true;
        }

        private static UnityModManager.ModEntry _modEntry;

        private static void StartMod(UnityModManager.ModEntry modEntry)
        {
            _modEntry = modEntry;
            BismuthLog.Init();
            harmony = new Harmony(modEntry.Info.Id);
            // Patch at the same early point as always when the master switch is on; a
            // master-off launch stays fully hands-off until the rail switch flips.
            if (Settings.ModEnabled) PatchAllOnce();

            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (IsEngineReady() && TryEagerInit())
                return;
            _deferredApplyPending = true;
        }

        private static bool _patched;

        private static void PatchAllOnce()
        {
            if (_patched) return;
            _patched = true;
            PatchAllResilient(harmony, Assembly.GetExecutingAssembly());
            // Isolated so a Harmony-version issue in this optional layer can never abort the
            // whole mod load (a MissingMethodException from an absent Patch overload on older
            // Harmony surfaces at THIS call site, not inside the method's own try/catch).
            try { KeyLimiter.TryPatchRawInput(harmony); }
            catch (Exception e) { BismuthLog.Log("TryPatchRawInput skipped: " + e.Message); }
        }

        /* Harmony's own PatchAll aborts the WHOLE batch the moment one patch class can't
           resolve its target — so a single hook whose game method was renamed/removed by an
           ADOFAI update bricks the entire mod (dead overlay, dead panel, no way back), which
           is exactly what a 3.3.0 API removal did. Patch each class in isolation instead: a
           broken hook drops only its own feature and logs which one, and every other patch
           still applies. CreateClassProcessor/PatchClassProcessor are long-standing HarmonyX
           APIs (verified present in the loaded 0Harmony). */
        private static void PatchAllResilient(Harmony h, Assembly asm)
        {
            int applied = 0, skipped = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(asm))
            {
                try
                {
                    // No-op (empty list) for types without [HarmonyPatch]; throws only when a
                    // real patch class fails to bind its target — precisely what we isolate.
                    var patched = h.CreateClassProcessor(type).Patch();
                    if (patched != null && patched.Count > 0) applied++;
                }
                catch (Exception e)
                {
                    skipped++;
                    BismuthLog.Log("[patch] SKIPPED " + type.FullName + " — " + e.Message
                        + " (likely a game API change; other patches unaffected)");
                }
            }
            BismuthLog.Log("[patch] applied " + applied + " class(es)"
                + (skipped > 0 ? ", SKIPPED " + skipped + " (see above)" : ""));
        }

        // Time.frameCount == 0 during koren UMM's static-ctor injection window. Calling
        // asset APIs (AssetBundle.LoadAllAssets, Font.CreateDynamicFontFromOSFont) at that
        // point crashes the engine — a managed try/catch can't recover it.
        private static bool IsEngineReady()
        {
            try { return Time.frameCount > 0; }
            catch { return false; }
        }

        private static bool TryEagerInit()
        {
            try
            {
                availableFonts = FontLoader.ScanFonts(_modEntry.Path);
                BuildUI();
                if (Settings.ModEnabled) EnableFeatures();
                UpdateChecker.Begin(_modEntry);
                return true;
            }
            catch (Exception ex)
            {
                // Overlay.Awake leaves a half-built component on a DDOL GameObject when it
                // throws; destroy it so the deferred retry doesn't render a stale UI on top
                // of the working one.
                BismuthLog.Log("Eager init deferred (game/engine not ready): " + ex.Message);
                if (Overlay.Instance != null && overlay == null)
                    UnityEngine.Object.Destroy(Overlay.Instance.gameObject);
                if (KeyViewer.Instance != null && keyViewer == null)
                    UnityEngine.Object.Destroy(KeyViewer.Instance.gameObject);
                return false;
            }
        }

        /* Everything the mod does TO THE GAME: Harmony patches, overlay, key viewer, input
           limiter, game font/layout rewrites. The panel, hotkey, fonts, and update checker
           live outside this pair, so the rail's master switch (and a master-off launch)
           keeps a way back on screen. _featuresOn flips only after a full enable, so a
           mid-enable exception during deferred init retries cleanly on the next scene load. */
        private static bool _featuresOn;
        internal static bool FeaturesOn => _featuresOn;

        private static void EnableFeatures()
        {
            if (_featuresOn) return;
            try
            {
                PatchAllOnce();
                if (overlay == null) overlay = Overlay.Create();
                overlay.ApplySettings(Settings);
                if (keyViewer == null) keyViewer = KeyViewer.Create(Settings);
                // On before the re-applies below — GameFontApplier/GameUiLayout gate on it.
                _featuresOn = true;
                ApplySelectedFont();
                KeyLimiter.Apply(Settings);
                GameUiLayout.Reapply();
            }
            catch { _featuresOn = false; throw; }
        }

        private static void DisableFeatures()
        {
            if (!_featuresOn) return;
            _featuresOn = false;
            // The game's key limiter is a persisted GAME setting we borrow — hand the
            // player's own list back before the features go away, or it outlives the mod.
            KeyLimiter.ReleaseGameLimiter();
            if (LocationEditor.IsActive) LocationEditor.Close();
            GameUiEditor.Close();
            harmony.UnpatchSelf(); // HarmonyX 2.x: UnpatchAll(id) is obsolete; this unpatches our instance
            _patched = false;
            if (overlay != null)
            {
                UnityEngine.Object.Destroy(overlay.gameObject);
                overlay = null;
            }
            if (keyViewer != null)
            {
                keyViewer.SaveCounts();
                UnityEngine.Object.Destroy(keyViewer.gameObject);
                keyViewer = null;
            }
            // Wrappers inserted into the game's UI hierarchy must not outlive the features
            // (hot reloads would otherwise stack stale wrappers from dead assemblies).
            GameUiLayout.RestoreAll();
            GameFontApplier.RestoreAll();
        }

        // Rail master switch: same effect as the UMM checkbox, but reachable in-game and
        // persisted, so a master-off state survives a restart.
        internal static void SetMasterEnabled(bool on)
        {
            Settings.ModEnabled = on;
            try
            {
                if (on) EnableFeatures();
                else DisableFeatures();
            }
            catch (Exception ex) { BismuthLog.Log("[Bismuth] Master toggle failed: " + ex); }
            Settings.Save(_modEntry);
            BismuthLog.Log("[Bismuth] Master switch → " + (on ? "enabled" : "disabled"));
        }

        // Builds the settings panel + tabs over the current font list. Reused by force-reload.
        private static void BuildUI()
        {
            UICore.Initialize(_modEntry, Settings, () =>
            {
                overlay?.ApplySettings(Settings);
                keyViewer?.ApplySettings(Settings);
                KeyLimiter.Apply(Settings);
                GameUiLayout.Reapply();
            }, availableFonts);
            UICore.OnKeyViewerRebuild = () => keyViewer?.Rebuild(Settings);
            /* Tabs are grouped by WHOSE pixels they change, not by widget type. The old rail had
               three tabs with "UI" in the name (Hide UI / UI / Game UI) that meant three
               different things, and the position editor sat under panel styling instead of
               with the overlay and key viewer it actually moves.
                 Overlay    — Bismuth's own on-screen stuff
                 Game UI    — the game's own UI
                 Appearance — how Bismuth itself is drawn
               Key Viewer keeps its own tab despite also being Bismuth's own UI — it's far too
               big to sit as a section under Overlay. Composition order matters in two places:
               PageKeyViewer's font-weight rows need PageOverlay to have run first (it resets
               RefreshFontWeightRows, hence the tab order below), and pages composed into one
               tab append to a shared root, so composition order is also section order. */
            UICore.Tabs.AddTab("Overlay", stack =>
            {
                PageOverlay.Build(stack);
                UIBuilder.Spacer(stack.Root);
                PageUI.BuildLocations(stack);
            });
            UICore.Tabs.AddTab("Key Viewer", PageKeyViewer.Build);
            UICore.Tabs.AddTab("Input", PageInput.Build);
            UICore.Tabs.AddTab("Game UI", stack =>
            {
                PageHideUi.Build(stack);
                UIBuilder.Spacer(stack.Root);
                PageGameUi.Build(stack);
            });
            UICore.Tabs.AddTab("Appearance", PageUI.Build);
            UICore.Tabs.AddTab("Misc", stack =>
            {
                PageTweaks.Build(stack);
                UIBuilder.Spacer(stack.Root);
                PageMisc.Build(stack);
            });
            // Master kill-switch pinned under the tabs — flips the whole mod on/off
            // without a trip to UMM's own settings window.
            UICore.Tabs.AddMasterSwitch("Mod enabled", Settings.ModEnabled, SetMasterEnabled);
        }

        // Misc → Debug "Force reload": re-scan fonts (pick up newly dropped files), rebuild the
        // panel so the pickers show them, and reapply everything — without a UMM reload. Set via
        // a flag and run from OnUpdate so the panel isn't torn down inside its button's handler.
        internal static void RequestForceReload() => _forceReloadPending = true;

        private static void DoForceReload()
        {
            try
            {
                bool wasOpen = UICore.IsOpen;
                // Keep the OLD assets alive; re-scan into fresh entries, re-apply everything onto
                // the new fonts, THEN drop the old ones (deferred in OnUpdate) so no text — panel,
                // overlay, or the incrementally-swept game text — is left on a destroyed material.
                var oldFonts = availableFonts;
                availableFonts = FontLoader.ScanFonts(_modEntry.Path);
                UICore.Dispose();
                BuildUI();
                ApplySelectedFont();
                overlay?.ApplySettings(Settings);
                // Structural rebuild, not just ApplySettings — force reload is also the
                // profile-load path, and profiles can swap the whole preset list.
                keyViewer?.Rebuild(Settings);
                if (_featuresOn)
                {
                    KeyLimiter.Apply(Settings);
                    GameUiLayout.Reapply();
                }
                if (wasOpen) UICore.Open();
                // A prior pending set (rapid double reload) is unreferenced by now — drop it.
                if (_oldFonts != null) FontLoader.DestroyTmpAssets(_oldFonts);
                _oldFonts = oldFonts;
                _destroyOldFontsFrame = Time.frameCount + 90; // ~1.5s: covers the frame-spread sweep
                BismuthLog.Log("[Bismuth] Force reload complete (" + availableFonts.Count + " fonts)");
                overlay?.DumpDebug("post-forcereload");
            }
            catch (Exception ex)
            {
                BismuthLog.Log("[Bismuth] Force reload failed: " + ex);
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_deferredApplyPending) return;
            if (!IsEngineReady()) return;
            // RDConstants.data's lazy getter itself can NRE if Resources.Load isn't safe yet.
            try { if (RDConstants.data == null) return; }
            catch { return; }
            if (TryEagerInit())
            {
                _deferredApplyPending = false;
                BismuthLog.Log("Deferred init succeeded on scene '" + scene.name + "'");
            }
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            if (!_featuresOn) return;
            /* Before the unload pass, not after: pruning the filter dictionaries drops the last
               managed reference to those entries, so UnloadUnusedAssets can then actually
               reclaim them. Gated on its own setting (Leak Guard), not on OptUnloadAssets. */
            LeakGuard.SweepStaticCaches();
            if (!Settings.OptimizationsEnabled || !Settings.OptUnloadAssets) return;
            // Measure synchronously: op.completed fires after the next scene starts allocating,
            // which makes before-after read as negative noise.
            long before = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Resources.UnloadUnusedAssets();
            long after = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            LastUnloadSavingsBytes = before - after;
            PageMisc.RefreshSavings();
        }

        internal static void ApplySelectedFont()
        {
            if (overlay == null || availableFonts.Count == 0) return;

            FontLoader.FontEntry master =
                FontLoader.Find(availableFonts, Settings.FontName)
                ?? availableFonts[0];

            // Per-part overlay fonts (master forces FontName onto all three via the
            // Effective* accessors). Weight overrides resolve inside each part's family.
            var statsEntry = FontLoader.Find(availableFonts, Settings.EffectiveStatsFont) ?? master;
            var comboEntry = FontLoader.Find(availableFonts, Settings.EffectiveComboFont) ?? master;
            var kvEntry    = FontLoader.Find(availableFonts, Settings.EffectiveKeyViewerFont) ?? master;
            FontLoader.SplitWeight(statsEntry.Name, out string statsFamily, out _);
            FontLoader.SplitWeight(comboEntry.Name, out string comboFamily, out _);
            FontLoader.SplitWeight(kvEntry.Name, out string kvFamily, out _);

            var labelEntry      = FindFamilyWeight(statsFamily, Settings.StatLabelWeight);
            var valueEntry      = FindFamilyWeight(statsFamily, Settings.StatValueWeight);
            var comboLabelEntry = FindFamilyWeight(comboFamily, Settings.ComboLabelWeight) ?? comboEntry;
            var comboValueEntry = FindFamilyWeight(comboFamily, Settings.ComboValueWeight) ?? comboEntry;

            overlay.SetFont(statsEntry.TmpFont, labelEntry?.TmpFont, valueEntry?.TmpFont,
                comboLabelEntry.TmpFont, comboValueEntry.TmpFont);
            var kvLabelEntry   = FindFamilyWeight(kvFamily, Settings.KeyViewerLabelWeight);
            var kvCountEntry   = FindFamilyWeight(kvFamily, Settings.KeyViewerCountWeight);
            keyViewer?.SetFont((kvLabelEntry ?? kvEntry).TmpFont, (kvCountEntry ?? kvEntry).TmpFont);
            // Game text has its own font, decoupled from the overlay font (Game UI
            // tab). Titles get the configured weight, defaulting to the heaviest.
            FontLoader.FontEntry gameEntry =
                FontLoader.Find(availableFonts, Settings.GameFontName)
                ?? master;
            FontLoader.SplitWeight(gameEntry.Name, out string gameFamily, out _);
            var titleEntry = FindFamilyWeight(gameFamily, Settings.GameTextTitleWeight)
                ?? FindFamilyWeight(gameFamily, FontLoader.WeightHeaviest);
            // The level name is a game HUD element: its weight lives in the Game UI
            // Element-weights list ("levelname"), drawn from the game font family, and
            // defaults to the title weight (bold) when unset.
            string lnWeight = Settings.GameUiWeightFor("levelname");
            var levelNameEntry = (!string.IsNullOrEmpty(lnWeight) ? FindFamilyWeight(gameFamily, lnWeight) : null)
                ?? titleEntry ?? gameEntry;
            overlay.SetLevelNameFont(levelNameEntry.TmpFont);
            // Weight table for the per-element overrides (Game UI tab → Element weights).
            var gameWeights = new Dictionary<string, FontLoader.FontEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in availableFonts)
            {
                FontLoader.SplitWeight(e.Name, out string fam, out string w);
                if (fam == gameFamily && !string.IsNullOrEmpty(w)) gameWeights[w] = e;
            }
            GameFontApplier.SetElementWeights(gameWeights);
            GameFontApplier.SetFonts(gameEntry.TmpFont, titleEntry?.TmpFont);
        }

        private static FontLoader.FontEntry FindFamilyWeight(string family, string weight)
        {
            if (string.IsNullOrEmpty(weight)) return null;
            bool heaviest = string.Equals(weight, FontLoader.WeightHeaviest, StringComparison.OrdinalIgnoreCase);
            FontLoader.FontEntry best = null;
            int bestRank = -1;
            foreach (var e in availableFonts)
            {
                FontLoader.SplitWeight(e.Name, out string fam, out string w);
                if (fam != family) continue;
                if (!heaviest)
                {
                    if (string.Equals(w, weight, StringComparison.OrdinalIgnoreCase)) return e;
                    continue;
                }
                int rank = FontLoader.WeightRank(w);
                if (rank > bestRank) { bestRank = rank; best = e; }
            }
            return best;
        }

        private static void StopMod(UnityModManager.ModEntry modEntry)
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _deferredApplyPending = false;
            DisableFeatures();
            // Runtime-created TMP assets (SDF atlases + materials) would otherwise pile up
            // across hot reloads — Mono keeps the old assembly alive.
            FontLoader.DestroyTmpAssets(availableFonts);
            UpdateChecker.Dispose();
            UpdatePopup.Close();
            DuplicateInstallPopup.Close();
            LogViewer.Close();
            UICore.Dispose();
        }
    }
}
