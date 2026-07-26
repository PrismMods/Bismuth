# HarmonyX Patches

## HarmonyX patches (`Patches.cs`)

| Patch target | Timing | Action |
| ------------ | ------ | ------ |
| `scrMarginTracker.Reset` | Postfix | `OnAttempt()` |
| `scrController.Start` / `scrController.Awake_Rewind` / `scrMistakesManager.LoadCheckpointProgress` | Postfix | `OnAttempt()` — extra attempt entry points (Start doesn't always fire; checkpoint load repopulates the tracker after Start) |
| `scrMarginTracker.AddHit(HitMargin)` | Postfix | `UpdateDisplay(percentAcc, percentXAcc, hit)` |
| `scrController.Restart` | Prefix | Sets `ScnGamePlayPatch.PendingRestart` — an in-game retry reloads the scene, so flag it here and consume it in the `Play` postfix (v3.2 dropped Play's old `isRestart` param) |
| `scnGame.Play(seqID)` | Postfix | `OnLevelStart(isRestart = PendingRestart, fromCheckpoint = seqID > 0)` — custom levels |
| `scrPressToStart.ShowText` | Postfix | `OnLevelStart(false, false)` — official levels (always a fresh entry from this hook) |
| `scrController.StartLoadingScene` | Postfix | `OnLevelEnd()`; clears `KeyViewer.PauseMenuOpen` (quitting while paused never calls Hide) |
| `scrUIController.WipeToBlack` | Postfix | `OnLevelEnd()` |
| `StateBehaviour.ChangeState(Enum)` | Postfix | `OnLevelEnd()` on transition to `States.None`; every state change also requests delayed `GameFontApplier` sweeps + `GameUiLayout` re-applies (death/results text spawns late) |
| `NewsSign.ShowNews` | Postfix | `GameFontApplier.ApplyTo` — the title-screen news text fills only when the async fetch lands, after the scene sweep |
| `scnCLS.DisplayLevel` | Postfix | `GameFontApplier.RequestFullSweepSoon` — CLS navigation re-stamps the default font on portal info but fires no sweep |
| `RDString.SetLocalizedFont(Text / TMP_Text / TextMesh)` | Postfix | `GameFontApplier.OnLocalizedFontSet` — re-applies our font right after the game stamps a language's own over it |
| `PauseMenu.Show` / `PauseMenu.ShowSettingsMenu` | Postfix | `GameFontApplier.ApplyTo` + `RequestFullSweepSoon` (menu shows with no scene change); `Show` also sets `KeyViewer.PauseMenuOpen = true` |
| `PauseMenu.Hide` | Postfix | Clears `KeyViewer.PauseMenuOpen` |
| `scrController.LevelNameTextRestore` | Postfix | `ApplyLevelNameTransform()` — re-applies our scale/offset after the game restores canonical position |
| `scrHitErrorMeter.UpdateLayout` | Postfix | `GameUiLayout.ApplyErrorMeter` — re-applies the meter position/scale override after the game's own layout pass |
| `scrShowIfDebug.Update` | Pre+Post | Temporarily sets `RDC.auto = false` (if `HideAutoplayText \|\| HideAllUI`) to suppress the autoplay text; re-enables the real label while `GameUiEditor.IsActive` |
| `scrHitTextMesh.Show` | Prefix | Repaints the pooled popup (`ApplyTo`), then moves it off-screen (`HideAllUI`) or suppresses it (`ShouldHideJudgement`) |
| `scrMissIndicator.Awake` | Postfix | Moves miss indicator off-screen (`HideAllUI`) |
| `scrPlanet.MoveToNextFloor` | Postfix | Hides error meter (`HideAllUI` or `HideHitmeter`) |
| `scrController.paused` (setter) | Postfix | Hides error meter (`HideAllUI` or `HideHitmeter`) |
| `scnEditor.ResetScene` | Postfix | `OnLevelEnd()` (editor stop) |
| `scnEditor.SwitchToEditMode` | Postfix | `ShowOrHideElements()` |
| `scnEditor.LateUpdate` | Postfix | `EditorLateUpdateShowHudPatch` — re-enables the HUD canvas while `GameUiEditor.IsActive` (the editor force-disables it outside play mode) |
| `OttoButtonController.Update` | Postfix | Hides Otto debug button (`HideAllUI`) |
| `PreviewSongPlayer.<FadePreview>b__13_1` | Postfix | CLS preview-volume — rescales the fade envelope's volume writes toward `ClsPreviewVolume` (resolved via `TargetMethod`) |
| `RDInput.GetMain(ButtonState)` | Postfix | Key Limiter — clamps press count to allowed-key count when state=WentDown; zeroes it entirely while the Bismuth menu is open |
| `RDInput.WentDown(KeyCode)` / `RDInput.IsDown(KeyCode)` | Postfix | Menu input block — raw shortcut-key reads return false while the menu is open |
| `RDInput.GetState(InputAction, ButtonState)` | Postfix | Menu input block — Rewired action reads return false while the menu is open |
| `UnityEngine.Input.GetKeyDown(KeyCode)` | Postfix | Menu input block — direct polls (menu number-nav) return false while open, except KeyCode.B and `RawReadExempt` reads. Applied separately via `TryPatchRawInput` |
| `scrMarginTracker.AddHit(HitMargin)` | Prefix | Key Limiter — suppresses hit if no allowed key currently held, or unconditionally while the menu is open |

### Optimizations (`Optimizations.cs`)

Independent file with Harmony patches gated on `Opt*` settings: `scrConductor.Update` (spectrum throttle), `TextureManager.LoadTexture` (non-readable / DXT postfix), `scrPlanet.Update` / `scrFloor.Update` (physics non-alloc / DOTween fix). (The old `CustomTexture.GetTexture` / `CustomSprite.GetSprite` variant-copy prefixes were removed for ADOFAI 3.3.0, which deleted the per-`ImageOptions` texture-variant system those hooked; nothing duplicates the texture anymore, so the `LoadTexture` non-readable postfix is safe standalone.)

### Resilient patching (`MainClass.PatchAllResilient`)

Patches are applied per-class via `harmony.CreateClassProcessor(type).Patch()` in a try/catch, **not** `harmony.PatchAll(assembly)`. Harmony's own `PatchAll` aborts the entire batch the moment one patch class can't bind its target, so a single game-API change (an ADOFAI update renaming/removing one hooked method) would brick the whole mod — dead overlay, dead panel, no in-game recovery. With the resilient path a broken hook logs `[patch] SKIPPED <class> — <reason>` and drops only its own feature; everything else still applies. Startup logs `[patch] applied N class(es)[, SKIPPED M]`. When diagnosing a post-update break, that skip line names the class to fix (see the memory note `game-update-breaks`).


