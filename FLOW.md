# ADOFAI Game Flow — IL Inspection Notes

Source: `ikdasm Assembly-CSharp.dll > /tmp/acs.il` (monodis also works)  
Game version: v2.9.8 (Mac), Unity 6 — ships TextMeshPro, but `scrController.txtLevelName` is still legacy `UnityEngine.UI.Text`

---

## Key Classes

| Class | Role |
| ----- | ---- |
| `scrController` | Central game controller — level state, planet management, input |
| `scrConductor` | Audio timing — song position, beat/bar events, spectrum |
| `scrFloor` | Individual tile — icon, opacity, glow, dummy planets |
| `scrPlanet` | The player planet — movement, collision, hittable state |
| `scrPlayer` | Per-player input reader — `ValidInputWasTriggered`, `CountValidKeysPressed`, key stats |
| `RDInput` | Static input hub wrapping Rewired + SkyHook async input — see Input Architecture below |
| `scrMistakesManager` | Tracks hit margins, accuracy, xacc |
| `scrUIController` | In-game UI — noFail image, difficulty, level name |
| `scrHitText` | Legacy floating judgement text (UI) |
| `scrHitTextMesh` | TextMesh judgement popup (3D world) |
| `scrShowIfDebug` | Shows/hides `Text` reading "status.autoplay" when `RDC.auto = true` |
| `scrPressToStart` | "Press any key" prompt for official levels |
| `scnGame` | Custom level scene — owns `levelPath`, calls `Play(seqID, isRestart)` |
| `scnEditor` | Level editor scene |
| `RDC` | Static flags: `auto`, `noHud`, `debug` |
| `GCS` | Global config statics: `internalLevelName`, `lofiVersion`, `typingMode`, etc. |
| `ADOBase` | Base MonoBehaviour — exposes `controller`, `isLevelEditor`, `isMobile` |
| `RDCheatCode` | Per-instance cheat sequence checker — `CheckCheatCode()` reads input history |

---

## Input Architecture (RDInput)

The game reads the keyboard through **four independent layers** — blocking one does not block the others (this bit Topaz's block-inputs-while-menu-open feature repeatedly):

```txt
ButtonState enum: WentDown = 0, WentUp = 1, IsDown = 2, IsUp = 3

1. RDInput.GetMain(ButtonState)         — aggregates RDInputType.Main() over active input types
   ├── get_mainPressCount  = GetMain(WentDown)
   ├── get_mainHeldCount   = GetMain(IsDown)
   └── gameplay hit chain:
       scrPlayer.ValidInputWasTriggered()
         gate: Input.anyKeyDown || GetMain(IsDown) > 0   (desktop branch)
         → return CountValidKeysPressed() > 0
       scrPlayer.CountValidKeysPressed()
         desktop non-coop: count = mainPressCount        ← the ONLY count source
         coop: per-player RDInput.playerInputs hashset count (bypasses GetMain)
         (then GetMainPressKeys-driven bookkeeping: downKeysDuration, keyFrequency,
          keyTotal — stats only, does not affect the returned count)

2. RDInput.WentDown/IsDown/WentUp(KeyCode) — literal Input.GetKeyDown/GetKey/GetKeyUp
   passthroughs; used for raw shortcut keys (R-restart, arrows, …)

3. RDInput.GetState(InputAction, ButtonState) — Rewired actions; all the get_*Press
   properties (restartPress, backPress, confirmPress, cancelPress, quitPress, …) are
   one-line wrappers over GetState

4. UnityEngine.Input.GetKeyDown direct — menu scenes poll below RDInput entirely:
   scnLevelSelect.Update / scnLevelSelectTaro.Update read Alpha1… for number-key nav
```

- `GetMainPressKeys()` = `GetStateKeys(WentDown)` — does **not** route through `GetMain`.
- `GetStateKeys(ButtonState)` returns `List<AnyKeyCode>`; `AnyKeyCode.value` is either a
  `KeyCode` (sync path) or an `AsyncKeyCode` (SkyHook async path — `label` is a SkyHook
  `KeyLabel`, `key` is a raw USB HID usage ID; modifiers report `label = Unknown(119)`).
- `scrController.LevelNameTextRestore` is called only from `scrController.OnLandOnPortal`
  (level completion) — not per frame.
- The game has its own key-limiter counter (`scrPlayer.keyLimiterOverCounter`), zeroed at
  the top of `CountValidKeysPressed`.

---

## scrConductor::Update

```txt
1. Compute song position from AudioSource.time + offsets + calibration
2. Compute deltaSongPos
3. Beat detection: if songPosition >= nextBeatTime → OnBeat(); advance nextBeatTime
4. Bar detection:  if songPosition >= nextBarTime  → advance nextBarTime
5. Editor-only: Ctrl+G key debug dump (gated by Application.isEditor)
6. Spectrum: if getSpectrum && !GCS.lofiVersion → AudioSource.GetSpectrumData(spectrum, 0, FFTWindow.Blackman)
```

**Optimization note:** Step 6 is the only expensive native call. `getSpectrum` is a public bool field.  
`GetSpectrumData` performs an FFT on the audio buffer — cost scales with spectrum array size.

---

## scrFloor::Update

```txt
1. Determine sprite array: rabbitSpritesArr (rabbit planet) or snailSpritesArr (snail planet)
2. Gate: !isLevelEditor || !paused; and gameworld; and currentSeqID >= seqID and !isFake
3. Compute BPM-based animation frame: 60 / (conductor.bpm * speed) → beat duration
4. Determine frame index from song position mod beat duration
5. SetIconSprite / SetIconOutlineSprite
```

Runs on every floor tile every frame during gameplay. Heavy when tile count is large.  
The BPM/speed division and modular frame calculation repeat every frame per tile.

---

## scrFloor::LateUpdate

```txt
1. Early exit: !Application.isPlaying → return
2. Early exit: !gameworld && !(currFloor.freeroamGenerated) → return
3. isTweening = (position changed) || (floorRenderer scale changed)
4. isFading = (opacity != opacityLastFrame)
5. isChangingGlowMult = (glowMultiplier != glowMultiplierLastFrame)

if isFading:
  → Material.SetFloat(ShaderProperty_Alpha, opacity)      ← only on actual opacity change
  → SetIconColor(opacity)
  → topGlow.color = (1,1,1, opacity * glowMultiplier * 0.8)
  → outlineSprite.color
  → foreach dummyPlanets (if multiplanetLine == null): SetPlanetColor with opacity
  → foreach dummyPlanets (if multiplanetLine != null && isFading && seqID > currentSeqID): SetPlanetColor + SetTailColor

if isChangingGlowMult (and not already handled above):
  → topGlow.color

if isTweening && multiplanetLine != null:
  → move multiplanetLine to floor position

6. Cache lastPos, lastScale, opacityLastFrame, glowMultiplierLastFrame
```

**Note:** Material.SetFloat and enumerator loops are already gated by `isFading`/`isChangingGlowMult`.  
These only fire during active transitions, not every frame. No patch needed.

---

## scrHitText::Update (and scrHitTextMesh::Update — identical structure)

```txt
1. if dead → return                                    ← early exit
2. if forceOnScreen:
   → compute orthographic size, aspect ratio
   → clamp textPos.x and textPos.y to screen bounds
   → set canvas.localPosition                          ← only when forceOnScreen = true
3. timer += deltaTime
4. if timer > 1.25s:
   → dead = true
   → DOKill transform, DOKill text
   → SetActive(false) on parent
```

**Note:** Screen-clamping is already gated by `forceOnScreen`. The game already does this optimization.  
`forceOnScreen` is set to true in multiplayer/splitscreen contexts to keep text on screen.

---

## scrController::Update (partial — cheat code section)

```txt
if !isMobile:
  if debugModeCheatCode.CheckCheatCode() || (Ctrl+Home key): RDC.debug = !RDC.debug
  if typingModeCheatCode.CheckCheatCode(): GCS.typingMode = !typingMode; Flash(white)
  if hideHudCheatCode.CheckCheatCode():    RDC.noHud = !RDC.noHud

if RDInput.cancelPress && !paused && !lofiVersion:
  → pause logic
```

**Conflict note:** `hideHudCheatCode` toggles `RDC.noHud` — this can conflict with Topaz's  
`HideAllUI` setting which also controls `RDC.noHud` via `ShowOrHideElements`.  
Since `ShowOrHideElements` only runs on settings change (not every frame), the state can  
drift if the player triggers this cheat while `HideAllUI` is on.

---

## scrPlanet::Update (collision)

```txt
Physics2D.OverlapCircleAll(position, radius, layerMask)
→ allocates Collider2D[] every frame when hittable = true
→ checks each collider for hit registration
```

**Risk:** Core hit detection — patching this can break gameplay. Avoid.

---

## Level Lifecycle

```txt
Custom level:
  LoadAndPlayLevel() sets scnGame.instance.levelPath
  → scnGame.Play(seqID=0, isRestart=false)         ← Topaz: OnLevelStart(false)

In-game retry (custom):
  scrController.ResetCustomLevel() coroutine:
    → scrUIController.WipeToBlack()                  ← Topaz: OnLevelEnd()
    → (yield: wipe animation)
    → scnGame.ResetScene()                           ← no Unity scene unload
    → scnGame.Play(checkpointNum, isRestart=true)   ← Topaz: OnLevelStart(true)

Official level:
  → scrPressToStart.ShowText()                       ← Topaz: OnLevelStart(false)

Level end (any):
  → scrController.StartLoadingScene()               ← Topaz: OnLevelEnd()
  → scrUIController.WipeToBlack()                   ← Topaz: OnLevelEnd()
  → StateBehaviour.ChangeState(States.None)         ← Topaz: OnLevelEnd()
  → scnEditor.ResetScene()                          ← Topaz: OnLevelEnd() (editor)
  → SceneManager.sceneUnloaded                      ← Topaz: OnSceneUnloaded()
```

`scrMistakesManager.Reset` fires only in `scrController.Awake` and `scnEditor.SwitchToEditMode`  
— NOT during in-game retries (no scene reload). The `isRestart` param on `scnGame.Play`  
is the only reliable retry signal.

---

## Spectrum Data

`scrConductor.spectrum` — `float[]` array, populated by `AudioSource.GetSpectrumData`.  
`scrConductor.getSpectrum` — bool, set by level events when a level uses audio visualization.  
`GCS.lofiVersion` — bool, suppresses spectrum (performance/battery mode).

`scnCLS` (Community Level Showcase) — when active and its `previewSongPlayer` is playing,  
`GetSpectrumData` targets that `AudioSource` instead of the main song.

---

## RDConstants

`RDConstants.data` — singleton; holds:

- `hitMarginColoursUI` — colors per `HitMargin` value (used by Topaz's `MarginColor`)
- `rabbitSpritesArr` / `snailSpritesArr` — 10-sprite arrays for animated planet icons  
  (index = `1 + 2*isFirstBeat + 5*isRabbit + beatPhase`)

---

## Performance Summary

| Area | Hot path? | Notes |
| ---- | --------- | ----- |
| `scrConductor::Update` — spectrum | Yes (when `getSpectrum`) | FFT native call; throttleable |
| `scrFloor::Update` — icon sprite | Yes (per tile per frame) | BPM math every frame; complex to patch safely |
| `scrFloor::LateUpdate` — opacity | Only during fades | Already gated by `isFading` |
| `scrPlanet::Update` — collision | Yes | Array alloc per frame; risky to patch |
| `scrController::Update` — cheat codes | Minor | 3 input-sequence checks; already mobile-gated |
| `scrHitText::Update` — screen clamp | Only when `forceOnScreen` | Already gated by game |
