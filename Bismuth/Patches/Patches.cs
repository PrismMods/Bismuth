using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using MonsterLove.StateMachine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    internal static class Patches
    {
        // Reset display text when a level (re)starts.
        [HarmonyPatch(typeof(scrMarginTracker), "Reset")]
        private static class MistakesResetPatch
        {
            public static void Postfix() { BismuthLog.Debug("[hook] scrMarginTracker.Reset"); Overlay.Instance?.OnAttempt(); }
        }

        // Multiple entry points because Start doesn't always fire and LoadCheckpointProgress
        // populates the tracker after Start would have run.
        [HarmonyPatch(typeof(scrController), "Start")]
        private static class ControllerStartPatch
        {
            public static void Postfix() { BismuthLog.Debug("[hook] scrController.Start"); Overlay.Instance?.OnAttempt(); }
        }

        [HarmonyPatch(typeof(scrController), "Awake_Rewind")]
        private static class ControllerAwakeRewindPatch
        {
            public static void Postfix() { BismuthLog.Debug("[hook] scrController.Awake_Rewind"); Overlay.Instance?.OnAttempt(); }
        }

        [HarmonyPatch(typeof(scrMistakesManager), "LoadCheckpointProgress")]
        private static class LoadCheckpointProgressPatch
        {
            public static void Postfix() { BismuthLog.Debug("[hook] scrMistakesManager.LoadCheckpointProgress"); Overlay.Instance?.OnAttempt(); }
        }

        // Update display after every hit.
        [HarmonyPatch(typeof(scrMarginTracker), "AddHit", new[] { typeof(HitMargin) })]
        private static class AddHitPatch
        {
            public static void Postfix(scrMarginTracker __instance, HitMargin hit)
            {
                Overlay.Instance?.UpdateDisplay(__instance.percentAcc, __instance.percentXAcc, hit);
            }
        }

        // An in-game retry reloads the scene via scrController.Restart, which re-enters
        // scnGame.Play fresh — so flag the restart here and consume it in the Play postfix.
        // (ADOFAI v3.2 dropped Play's old isRestart parameter, breaking the by-name bind.)
        [HarmonyPatch(typeof(scrController), "Restart")]
        private static class ControllerRestartPatch
        {
            public static void Prefix() => ScnGamePlayPatch.PendingRestart = true;
        }

        // Custom level starts playing. PendingRestart distinguishes an in-game retry (set
        // above, survives the scene reload) from a first/outside entry (left false).
        [HarmonyPatch(typeof(scnGame), "Play")]
        private static class ScnGamePlayPatch
        {
            internal static bool PendingRestart;

            public static void Postfix(int seqID)
            {
                bool isRestart = PendingRestart;
                PendingRestart = false;
                // seqID > 0 ⇒ started from a checkpoint tile (the game's own startedFromCheckpoint).
                Overlay.Instance?.OnLevelStart(isRestart, seqID > 0);
            }
        }

        // Official level "press any key" prompt appears (always a fresh entry from this hook).
        [HarmonyPatch(typeof(scrPressToStart), "ShowText")]
        private static class PressToStartShowTextPatch
        {
            public static void Postfix()
            {
                Overlay.Instance?.OnLevelStart(false, false);
            }
        }

        // Scene is being replaced — hide until the next level starts.
        [HarmonyPatch(typeof(scrController), "StartLoadingScene")]
        private static class StartLoadingScenePatch
        {
            public static void Postfix()
            {
                Overlay.Instance?.OnLevelEnd();
                KeyViewer.PauseMenuOpen = false; // quitting a level while paused never calls Hide
            }
        }

        // Screen wipes to black (win screen exit, scene transitions).
        [HarmonyPatch(typeof(scrUIController), "WipeToBlack")]
        private static class WipeToBlackPatch
        {
            public static void Postfix()
            {
                Overlay.Instance?.OnLevelEnd();
            }
        }

        // State machine transitions to None (ESC / direct menu exit).
        [HarmonyPatch(typeof(StateBehaviour), "ChangeState", new[] { typeof(Enum) })]
        private static class StateChangePatch
        {
            public static void Postfix(Enum newState)
            {
                try
                {
                    if ((States)newState == States.None)
                        Overlay.Instance?.OnLevelEnd();
                    // Fail/Won spawn fresh text (death "% completed", results screens)
                    // that the level-start sweep can't see.
                    GameFontApplier.RequestSweepSoon();
                    GameUiLayout.RequestApplySoon();
                }
                catch { }
            }
        }

        // The title-screen news sign fills its TMP text only when the async fetch of
        // adofai-news.json lands, after the scene-change sweep. Repaint on arrival.
        [HarmonyPatch(typeof(NewsSign), "ShowNews")]
        private static class NewsSignShowPatch
        {
            public static void Postfix(NewsSign __instance)
            {
                try { GameFontApplier.ApplyTo(__instance.gameObject); }
                catch { }
            }
        }

        // CLS navigation re-stamps the default font on the portal info but fires no sweep,
        // so only the level selected at the last sweep kept the overlay font. Re-sweep
        // after each DisplayLevel.
        [HarmonyPatch(typeof(scnCLS), "DisplayLevel")]
        private static class CLSDisplayLevelPatch
        {
            public static void Postfix()
            {
                try { GameFontApplier.RequestFullSweepSoon(); }
                catch { }
            }
        }

        // RDString.SetLocalizedFont stamps each language's OWN font over our swap (the
        // language-selector previews reverted while cycling). Re-apply ours right after.
        // Panel strings are resolved at build time, so a mid-session language switch needs a
        // rebuild to take effect. Force reload already re-runs BuildUI.
        [HarmonyPatch(typeof(RDString), "ChangeLanguage")]
        private static class LanguageChangePatch
        {
            public static void Postfix()
            {
                if (MainClass.IsEnabled) MainClass.RequestForceReload();
            }
        }

        [HarmonyPatch(typeof(RDString), "SetLocalizedFont", new[] { typeof(Text) })]
        private static class LocalizedFontTextPatch
        {
            public static void Postfix(Text text)
            {
                try { GameFontApplier.OnLocalizedFontSet(text); } catch { }
            }
        }

        [HarmonyPatch(typeof(RDString), "SetLocalizedFont", new[] { typeof(TMP_Text) })]
        private static class LocalizedFontTmpPatch
        {
            public static void Postfix(TMP_Text text)
            {
                try { GameFontApplier.OnLocalizedFontSet(text); } catch { }
            }
        }

        [HarmonyPatch(typeof(RDString), "SetLocalizedFont", new[] { typeof(TextMesh) })]
        private static class LocalizedFontMeshPatch
        {
            public static void Postfix(TextMesh text)
            {
                try { GameFontApplier.OnLocalizedFontSet(text); } catch { }
            }
        }

        // The pause/settings menu shows over gameplay with no scene change, so no font
        // sweep ran for it. Re-sweep when it opens — both the pause menu (Show) and the
        // settings submenu opened directly (ShowSettingsMenu).
        [HarmonyPatch(typeof(PauseMenu), "Show")]
        private static class PauseMenuShowPatch
        {
            public static void Postfix(PauseMenu __instance)
            {
                KeyViewer.PauseMenuOpen = true;
                try { GameFontApplier.ApplyTo(__instance.gameObject); GameFontApplier.RequestFullSweepSoon(); }
                catch { }
            }
        }

        // Resume / close the pause menu — re-show the key viewer.
        [HarmonyPatch(typeof(PauseMenu), "Hide")]
        private static class PauseMenuHidePatch
        {
            public static void Postfix() => KeyViewer.PauseMenuOpen = false;
        }

        [HarmonyPatch(typeof(PauseMenu), "ShowSettingsMenu")]
        private static class PauseMenuSettingsPatch
        {
            public static void Postfix(PauseMenu __instance)
            {
                try { GameFontApplier.ApplyTo(__instance.gameObject); GameFontApplier.RequestFullSweepSoon(); }
                catch { }
            }
        }

        // The game's layout pass for the hit error meter (anchors from `pos`, scale from
        // `meterScale`), on Awake and on meter size/shape changes. Re-apply our override.
        [HarmonyPatch(typeof(scrHitErrorMeter), "UpdateLayout")]
        private static class ErrorMeterLayoutPatch
        {
            public static void Postfix(scrHitErrorMeter __instance)
            {
                try { GameUiLayout.ApplyErrorMeter(__instance); }
                catch { }
            }
        }

        // Re-apply level name transform after the game restores it to its default position.
        [HarmonyPatch(typeof(scrController), "LevelNameTextRestore")]
        private static class LevelNameTextRestorePatch
        {
            public static void Postfix()
            {
                Overlay.Instance?.ApplyLevelNameTransform();
            }
        }

        // Editor stopped playback — reset scene fires when the user presses stop.
        [HarmonyPatch(typeof(scnEditor), "ResetScene")]
        private static class EditorResetScenePatch
        {
            public static void Postfix()
            {
                Overlay.Instance?.OnLevelEnd();
            }
        }

        // Switching back to edit mode — re-apply editor element visibility.
        [HarmonyPatch(typeof(scnEditor), "SwitchToEditMode")]
        private static class SwitchToEditModePatch
        {
            public static void Postfix()
            {
                Overlay.Instance?.ShowOrHideElements();
            }
        }

        /* The autoplay label is a scrShowIfDebug. 3.3.0 hides it through the game's own
           flags — RDC.noHud (all HUD) and RDC.noAutoHud (autoplay HUD only) — both set from
           Overlay.ShowOrHideElements. We no longer flip RDC.auto to hide it: the game's
           autoplay toggle (Space / the auto button) reads RDC.auto, so flipping it mid-frame
           made every play/toggle turn autoplay OFF (and made autoplay unusable with the
           hide-autoplay-text option on). This postfix now only force-SHOWS the label while
           the game-UI layout editor is open, so it can be positioned. */
        [HarmonyPatch(typeof(scrShowIfDebug), "Update")]
        private static class ShowIfDebugUpdatePatch
        {
            public static void Postfix(scrShowIfDebug __instance)
            {
                if (!Bismuth.UI.GameUiEditor.IsActive) return;
                var txt = __instance.GetComponent<UnityEngine.UI.Text>();
                if (txt == null) return;
                txt.enabled = true;
                if (string.IsNullOrEmpty(txt.text))
                    txt.text = RDString.Get("status.autoplay");
            }
        }

        // Move judgement text off-screen in hide-all mode. Suppress Perfect when the setting is enabled.
        [HarmonyPatch(typeof(scrHitTextMesh), "Show")]
        private static class HitTextShowPatch
        {
            public static bool Prefix(scrHitTextMesh __instance, ref Vector3 position)
            {
                // Pooled popups dodge the scene-change sweep, so repaint on spawn.
                GameFontApplier.ApplyTo(__instance.gameObject);
                if (MainClass.Settings.ActiveHideAllUI)
                {
                    position = new Vector3(100000f, 100000f, 100000f);
                    return true;
                }
                if (MainClass.Settings.ShouldHideJudgement(__instance.hitMargin))
                    return false;
                return true;
            }
        }

        // Move the miss indicator off-screen on spawn in hide-all mode.
        [HarmonyPatch(typeof(scrMissIndicator), "Awake")]
        private static class MissIndicatorAwakePatch
        {
            public static void Postfix(scrMissIndicator __instance)
            {
                if (MainClass.Settings.ActiveHideAllUI)
                    __instance.transform.position = new Vector3(100000f, 100000f, 100000f);
            }
        }

        // Hide the hit error meter whenever a new floor is entered or the game pauses.
        private static void HideErrorMeter()
        {
            var controller = scrController.instance;
            if (controller == null) return;
            if (!MainClass.Settings.ActiveHideAllUI && !MainClass.Settings.ActiveHideHitmeter) return;
            var errorMeter = controller.errorMeter;
            if (errorMeter != null && controller.gameworld && errorMeter.gameObject.activeSelf)
                errorMeter.gameObject.SetActive(false);
        }

        [HarmonyPatch(typeof(scrPlanet), "MoveToNextFloor")]
        private static class MoveToNextFloorHideErrorMeterPatch
        {
            public static void Postfix() => HideErrorMeter();
        }

        [HarmonyPatch(typeof(scrController), "paused", MethodType.Setter)]
        private static class PausedSetterHideErrorMeterPatch
        {
            public static void Postfix() => HideErrorMeter();
        }

        // 3.3.0 routes ALL error-meter showing through scrController.UpdateErrorMeterVisibility
        // (it re-activates the meter root on pause/unpause, level setup, editor toggle), and
        // it's called from sites the floor/pause hooks above don't cover — so the meter came
        // back. Re-hide right after the game's own visibility pass.
        [HarmonyPatch(typeof(scrController), "UpdateErrorMeterVisibility")]
        private static class ErrorMeterVisibilityPatch
        {
            public static void Postfix() => HideErrorMeter();
        }

        // The level editor force-disables the game's HUD canvas every frame outside play
        // mode (LateUpdate: canvas.enabled = playMode), so the game-UI editor showed handles
        // over nothing. Re-enable after the game's write while an edit session is open;
        // scnEditor takes back over on the first frame after Close.
        [HarmonyPatch(typeof(scnEditor), "LateUpdate")]
        private static class EditorLateUpdateShowHudPatch
        {
            public static void Postfix()
            {
                if (!Bismuth.UI.GameUiEditor.IsActive) return;
                var uic = scrUIController.instance;
                if (uic != null && uic.canvas != null && !uic.canvas.enabled)
                    uic.canvas.enabled = true;
            }
        }

        /* The beta-build label (scrEnableIfBeta) decides its own visibility in Awake and,
           on a non-stable Steam branch, force-enables itself there — which runs after (or
           independently of) our one-shot sweep, so a hidden label came back and stayed. The
           component has no Update, so Awake is the only re-show path: re-hide right after it
           whenever the setting is on. This makes "Hide beta build text" stick; it never
           enables the label. */
        [HarmonyPatch(typeof(scrEnableIfBeta), "Awake")]
        private static class BetaBuildTextHidePatch
        {
            public static void Postfix(scrEnableIfBeta __instance)
            {
                var s = MainClass.Settings;
                if (s != null && (s.ActiveHideBetaBuild || s.ActiveHideAllUI)
                    && __instance != null && __instance.gameObject.activeSelf)
                    __instance.gameObject.SetActive(false);
            }
        }

        /* The editor play-test autoplay controls tip (scnEditor.controlsTip on ottoCanvas:
           pan / stop-autoplay key help, new in 3.3.0) is re-shown every frame by
           scnEditor.Update. Hide it via Text.enabled — GameTextShadow mirrors the original's
           enabled state, so its Bismuth-font shadow hides with it. Stateful restore: we only
           re-enable a tip WE disabled, so we never fight the game's own enable/disable. */
        [HarmonyPatch(typeof(scnEditor), "Update")]
        private static class EditorControlsTipHidePatch
        {
            private static bool _forcedOff;

            public static void Postfix(scnEditor __instance)
            {
                var tip = __instance.controlsTip;
                if (tip == null) return;
                bool hide = MainClass.Settings.ActiveHideControlsTip || MainClass.Settings.ActiveHideAllUI;
                if (hide)
                {
                    if (tip.enabled) { tip.enabled = false; _forcedOff = true; }
                }
                else if (_forcedOff)
                {
                    tip.enabled = true;
                    _forcedOff = false;
                }
            }
        }

        // Hide the Otto debug button in hide-all mode.
        [HarmonyPatch(typeof(OttoButtonController), "Update")]
        private static class OttoButtonUpdatePatch
        {
            public static void Postfix(OttoButtonController __instance)
            {
                if (MainClass.Settings.ActiveHideAllUI && Overlay.Instance != null && Overlay.Instance.InLevel
                    && __instance.button != null)
                    __instance.button.gameObject.SetActive(false);
            }
        }

        // CLS song-preview volume (Tweaks tab). The preview fades via a DOTween whose
        // compiler-generated setter lambda writes audioSource.volume directly toward a
        // hardcoded 1.0 — rescaling every write caps the whole fade envelope, so the
        // steady preview plays at the configured volume.
        [HarmonyPatch]
        private static class ClsPreviewVolumePatch
        {
            static System.Reflection.MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("PreviewSongPlayer");
                return t != null ? AccessTools.Method(t, "<FadePreview>b__13_1") : null;
            }

            public static void Postfix(PreviewSongPlayer __instance, float x)
            {
                var s = MainClass.Settings;
                if (s == null || __instance.audioSource == null) return;
                float v = Mathf.Clamp01(s.ClsPreviewVolume);
                if (v >= 0.999f) return;
                __instance.audioSource.volume = x * v;
            }
        }

    }
}
