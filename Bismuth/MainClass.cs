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
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            KeyLimiter.TryPatchRawInput(harmony);

            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (IsEngineReady() && TryEagerInit())
                return;
            _deferredApplyPending = true;
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
                if (overlay == null) overlay = Overlay.Create();
                overlay.ApplySettings(Settings);
                if (keyViewer == null) keyViewer = KeyViewer.Create(Settings);

                availableFonts = FontLoader.ScanFonts(_modEntry.Path);
                ApplySelectedFont();
                KeyLimiter.Apply(Settings);

                GameUiLayout.Reapply();
                BuildUI();
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
            UICore.Tabs.AddTab("Overlay", PageOverlay.Build);
            UICore.Tabs.AddTab("Key Viewer", PageKeyViewer.Build);
            UICore.Tabs.AddTab("Input", PageInput.Build);
            UICore.Tabs.AddTab("Hide UI", PageHideUi.Build);
            UICore.Tabs.AddTab("UI", PageUI.Build);
            UICore.Tabs.AddTab("Game UI", PageGameUi.Build);
            UICore.Tabs.AddTab("Tweaks", PageTweaks.Build);
            UICore.Tabs.AddTab("Misc", PageMisc.Build);
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
                keyViewer?.ApplySettings(Settings);
                KeyLimiter.Apply(Settings);
                GameUiLayout.Reapply();
                if (wasOpen) UICore.Open();
                // A prior pending set (rapid double reload) is unreferenced by now — drop it.
                if (_oldFonts != null) FontLoader.DestroyTmpAssets(_oldFonts);
                _oldFonts = oldFonts;
                _destroyOldFontsFrame = Time.frameCount + 90; // ~1.5s: covers the frame-spread sweep
                BismuthLog.Log("[Bismuth] Force reload complete (" + availableFonts.Count + " fonts)");
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

            FontLoader.FontEntry target =
                FontLoader.Find(availableFonts, Settings.FontName)
                ?? availableFonts[0];

            // Optional per-part weights for stat rows / combo, drawn from the same family.
            FontLoader.SplitWeight(target.Name, out string family, out _);
            var labelEntry      = FindFamilyWeight(family, Settings.StatLabelWeight);
            var valueEntry      = FindFamilyWeight(family, Settings.StatValueWeight);
            var comboLabelEntry = FindFamilyWeight(family, Settings.ComboLabelWeight);
            var comboValueEntry = FindFamilyWeight(family, Settings.ComboValueWeight);

            overlay.SetFont(target.TmpFont, labelEntry?.TmpFont, valueEntry?.TmpFont,
                comboLabelEntry?.TmpFont, comboValueEntry?.TmpFont);
            var kvLabelEntry   = FindFamilyWeight(family, Settings.KeyViewerLabelWeight);
            var kvCountEntry   = FindFamilyWeight(family, Settings.KeyViewerCountWeight);
            keyViewer?.SetFont((kvLabelEntry ?? target).TmpFont, (kvCountEntry ?? target).TmpFont);
            // Game text has its own font, decoupled from the overlay font (Game UI
            // tab). Titles get the configured weight, defaulting to the heaviest.
            FontLoader.FontEntry gameEntry =
                FontLoader.Find(availableFonts, Settings.GameFontName)
                ?? target;
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
            harmony.UnpatchSelf(); // HarmonyX 2.x: UnpatchAll(id) is obsolete; this unpatches our instance
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
            // Wrappers inserted into the game's UI hierarchy must not outlive the mod
            // (hot reloads would otherwise stack stale wrappers from dead assemblies).
            GameUiEditor.Close();
            GameUiLayout.RestoreAll();
            GameFontApplier.RestoreAll();
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
