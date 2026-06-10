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

        internal static void Setup(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            ModPath = modEntry.Path;
            Settings = Settings.Load<Settings>(modEntry);
            Settings.EnsureDefaults();
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = (_, __) => UICore.HandleUpdate();
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

        // Tear everything down so the freshly loaded assembly starts from a clean slate.
        // The old assembly stays in memory (Mono can't unload it), but nothing of ours
        // may survive in the scene: DDOL GameObjects, Harmony patches, scene callbacks.
        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            if (LocationEditor.IsActive) LocationEditor.Close();
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

                UICore.Initialize(_modEntry, Settings, () =>
                {
                    overlay?.ApplySettings(Settings);
                    keyViewer?.ApplySettings(Settings);
                    KeyLimiter.Apply(Settings);
                }, availableFonts);
                UICore.OnKeyViewerRebuild = () => keyViewer?.Rebuild(Settings);
                UICore.Tabs.AddTab("Overlay", PageOverlay.Build);
                UICore.Tabs.AddTab("Key Viewer", PageKeyViewer.Build);
                UICore.Tabs.AddTab("Input", PageInput.Build);
                UICore.Tabs.AddTab("Hide UI", PageHideUi.Build);
                UICore.Tabs.AddTab("Locations", PageLocations.Build);
                UICore.Tabs.AddTab("UI", PageUI.Build);
                UICore.Tabs.AddTab("Misc", PageMisc.Build);
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
            if (!Settings.OptUnloadAssets) return;
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

            string targetName = string.IsNullOrEmpty(Settings.FontName)
                ? availableFonts[0].Name
                : Settings.FontName;

            FontLoader.FontEntry target = null;
            foreach (FontLoader.FontEntry e in availableFonts)
                if (e.Name == targetName) { target = e; break; }
            if (target == null) target = availableFonts[0];

            overlay.SetFont(target.Font);
            keyViewer?.SetFont(target.Font);
        }

        private static void StopMod(UnityModManager.ModEntry modEntry)
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _deferredApplyPending = false;
            harmony.UnpatchAll(modEntry.Info.Id);
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
            UICore.Dispose();
        }
    }
}
