# Settings Fields

## Settings fields (`Settings.cs`)

### Overlay stat rows

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `ShowOverlay` | `true` | Master toggle for all stat rows (does **not** affect combo display) |
| `ShowProgress` | `true` | Show progress % row |
| `ShowAcc` | `false` | Show accuracy % row |
| `ShowXAcc` | `true` | Show X-accuracy % row |
| `ShowBpm` | `true` | Show BPM row |
| `ShowTileBpm` | `true` | Show tile BPM row |
| `ShowKps` | `false` | Show KPS row (keys per second = tile BPM / 60, computed from the same throttled tile-BPM sample) |
| `ProgressLabel` … `KpsLabel` | `""` | Per-stat label overrides (`""` = built-in Progress/Accuracy/XAccuracy/BPM/TBPM/KPS). Edited via the "Label" input on each stat's subpage |
| `ShowTimingScale` | `true` | Show timing scale row |
| `ShowJudgements` | `true` | Show hit-margin count row |
| `ShowAttempts` | `false` | Show attempts counter |
| `ShowFullAttempts` | `false` | Show full-attempts counter (attempts started at 0% — checkpoint restarts excluded). Renders as a second row under Attempts in the same container |
| `Scale` | `1.0` | Scale applied to left/right overlay columns |
| `FontName` | `"Paperlogy-4Regular"` | Font used for all overlay text (also the level name's fallback source; Paperlogy carries CJK. `FontLoader.Find` matches ignoring spaces/hyphens/case) |
| `StatSeparator` | `" \| "` | Text between a stat row's label and value; empty falls back to `" \| "`. Trailing spaces become HLG spacing (TMP never measures trailing whitespace — see [fonts.md](fonts.md)) |
| `StatLabelWeight` | `"Medium"` | Weight override for stat row labels (`""` = match the overlay font). Honored only when the family has that weight |
| `StatValueWeight` | `""` | Weight override for stat row values |
| `GameUiTextWeights["levelname"]` | (unset → title weight) | Weight for the song-title / level-name text. Lives in the Game UI → Element weights list ("Level Name"), drawn from the GAME font family; consumed by `MainClass.ApplySelectedFont` (NOT GameFontApplier — txtLevelName is Skip'd/Bismuth-managed), defaulting to `titleEntry` (heaviest) when unset. The old Overlay-tab `LevelNameWeight` row was removed June 14 2026. `ApplyLevelNameTransform` also forces the label single-line (horizontal+vertical overflow) — the game appends the speed-trial multiplier to this same Text and Pretendard's width wrapped+overlapped it |
| `KeyViewerLabelWeight` / `KeyViewerCountWeight` | `""` | Independent weight overrides for key viewer labels vs counts (Key Viewer tab rows; only render when the overlay family has >1 weight; PageKeyViewer reuses `PageOverlay.AddWeightRow` — depends on the Overlay tab building first). An explicit count weight drops the counts' legacy faux-Bold style (`KeyViewer.SetFont(label, count, countExplicit)`) |
| `OverlayShadowEnabled` | `true` | Master switch for every overlay text shadow (rows, judgements, attempts, FPS, combo, song title) |
| `OverlayShadowColor` | `(0, 0, 0, 0.5)` | Master shadow color — applies to everything except combo label/count, which keep their dedicated colors but obey the switch |

### Row positions (`OverlayPosition` enum: `Left` / `Right`)

| Field | Default |
| ----- | ------- |
| `ProgressPosition` | Left |
| `AccPosition` | Left |
| `XAccPosition` | Left |
| `BpmPosition` | Right |
| `TileBpmPosition` | Right |
| `KpsPosition` | Right |

### Attempts position

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `AttemptsX` | `0.77` | Normalized screen X anchor of the attempts container (0 = left, 1 = right) |
| `AttemptsY` | `0.05` | Normalized screen Y anchor of the attempts container (0 = bottom, 1 = top) |

The attempts container is a VLG holding both the Attempts row and the Full Attempts row — `AttemptsX`/`AttemptsY` move both together; each row's visibility is gated independently (`ShowAttempts` / `ShowFullAttempts`).

### Timing Scale sub-settings

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `TimingScaleY` | `0` | Y offset of the timing scale container (px, from anchor) |
| `TimingScaleSize` | `0.75` | Scale of the timing scale container |

### Judgements sub-settings

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `JudgementsY` | `0` | Y offset of the judgements container (px) |
| `JudgementsSize` | `0.9` | Scale of the judgements container |
| `JudgementsGap` | `12` | HLG spacing between judgement count texts (px) |

### Combo display

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `ShowComboDisplay` | `true` | Toggle for combo display only — independent of `ShowOverlay` |
| `ComboDisplayY` | `0` | Y offset of the entire combo display container (px) |
| `ComboDisplaySize` | `1.0` | Overall scale multiplier — fans into the label/count fontSize and both shadow offsets so glyphs re-rasterize instead of stretching |
| `ComboDisplayText` | `"Perfect Combo"` | Text shown in the combo label above the counter |
| `ComboLabelY` | `65` | Y offset of the label wrapper relative to the counter (px) — multiplied by `ComboDisplaySize` at apply time |
| `ComboLabelSize` | `1.0` | Additional fontSize multiplier for the label only (`ComboLabelBaseFontSize × ComboLabelSize × ComboDisplaySize`); also scales the label shadow |
| `ComboCountSize` | `1.0` | Additional fontSize multiplier for the count only (`ComboValueBaseFontSize × ComboCountSize × ComboDisplaySize`); also scales the count shadow |
| `ComboCountAuto` | `false` | Whether autoplay tiles increment (but never break) the combo |
| `ComboShadowOffsetX` / `ComboShadowOffsetY` | `4` / `-4` | Count (`comboDisplayValue`) drop-shadow offset in px (TmpShadow underlay); multiplied by `ComboDisplaySize × ComboCountSize` |
| `ComboShadowColor` | `(0, 0, 0, 0.5)` | Count drop-shadow color |
| `ComboLabelShadowOffsetX` / `ComboLabelShadowOffsetY` | `2.5` / `-2.5` | Label drop-shadow offset in px; multiplied by `ComboDisplaySize × ComboLabelSize` |
| `ComboLabelShadowColor` | `(0, 0, 0, 0.5)` | Label drop-shadow color |
| `ComboLabelWeight` | `""` | Weight override for the combo label (`""` = match overlay font) |
| `ComboValueWeight` | `"Heaviest"` | Weight override for the count. `FontLoader.WeightHeaviest` is a sentinel resolving to the family's heaviest weight at apply time; its dropdown option only exists on the Count row |
| `ComboPulseOffsetY` | `8` | Extra Y offset (px) added to `_comboLabelWrapper` at pulse peak — label rises then settles back to `ComboLabelY` |
| `ComboPulseScale` | `0.2` | Extra fontSize multiplier applied to the count at pulse peak (`+0.2` = 20% bigger). Animated via `fontSize`, not `localScale`, so the rasterized text stays crisp |
| `ComboPulseDuration` | `0.2` | Seconds for the pulse to decay from peak to normal |
| `ComboGradientMax` | `1000` | Combo count that maps to `t = 1.0` in `ComboGradient.Evaluate` |

### Gradients

| Field | Evaluated at | Default |
| ----- | ------------ | ------- |
| `ProgressGradient` | `t = percentComplete` | Grey → blue; gold at 100% |
| `AccGradient` | `t = percentAcc` (and xacc) | Red → orange → green → blue; gold at 100% |
| `BpmGradient` | `t = bpm / 10000` | White → blue |
| `ComboGradient` | `t = combo / ComboGradientMax` | White → blue |
| `KpsGradient` | `t = tileBpm / 10000` (same domain as Tile BPM, so `KpsUseTileBpmGradient` matches that row's color exactly) | White → blue |

### Key Viewer

Two independent panels — **Hand** and **Foot** — each driven by its own active preset from independent lists.

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `KvHandPresets` | 10k / 12k / 16k | Hand preset list |
| `KvFootPresets` | 2k / 4k / 8k / 16k | Foot preset list |
| `KvActiveHand` | `1` (12k) | Index into `KvHandPresets` |
| `KvActiveFoot` | `0` | Index into `KvFootPresets` |
| `ShowKeyViewer` | `true` | Top-level master toggle — when off, the KV canvas deactivates regardless of hand/foot flags. Toggle lives next to the "Key Viewer" section header (mirrors Key Limiter / Chatter Blocker pattern) |
| `ShowHandViewer` | `true` | Hand panel toggle (subordinate to `ShowKeyViewer`) |
| `ShowFootViewer` | `false` | Foot panel toggle (subordinate to `ShowKeyViewer`) |

`Settings.Hand` / `Settings.Foot` (XmlIgnore properties) resolve the active preset for each category; both can be `null` if the list is empty.

#### KeyViewerPreset

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `Name` | `"Preset"` | Display name in selector |
| `PersistCounts` | `true` | Save/load per-key totals to `keycounts.txt`; honoured if **either** active preset opts in |
| `Rows` | _set by Make…Preset_ | Ordered `KeyViewerRow` list (top row defines panel width) |
| `KeyWidth` | `60` | Top-row key width in px; lower rows stretch to match top width |
| `Radius` | `8` | Cell corner radius in canvas units. Vector-rendered by `RoundedRectGraphic` so it scales cleanly with cell size at any resolution. GUI slider range 0–64 |
| `BorderWidth` | `0` | Cell border ring thickness in canvas units (0 = no border). GUI slider range 0–16, snapped to 0.5 px |
| `BorderIdle` / `BorderHeld` | white / white | Border ring color when idle/pressed; swapped on press/release like `BgIdle`/`BgHeld` |
| `Gap` | `4` | Inner gap subtracted from each cell's size; also offsets rain start |
| `X`, `Y` | `0.01`, `0.01` | Canvas anchor (0–1) of bottom-left of the panel |
| `Scale` | `1.0` | `localScale` of the panel |
| `RainDistance` | `300` | "Fade Start" — distance from panel top where the rain tip's gradient begins |
| `RainTrackLength` | `390` | Total track length (px). Rain destroyed past this. Fade zone = `TrackLength − Distance` |
| `RainSpeed` | `500` | px/sec |
| `RainWidthStep` | `14` | px narrower per row depth (row 0 = full keyWidth) |
| `RainShadowSize` | `0` | Soft-blur radius for the shadow halo on each side of the rain (`0` = off) |
| `RainShadowColor` | `(0,0,0,0.05)` | Shadow color when `RainShadowSize > 0` |
| `BgIdle` / `BgHeld` | black 0.7α / white | Cell background colors |
| `TxtIdle` / `TxtHeld` | white / black | Cell label text colors |
| `LabelSize` | `16` | Cell label font size |
| `CountIdle` / `CountHeld` | gray / black | Cell count text colors |
| `CountSize` | `13` | Cell count font size |
| `ShowLabel` / `ShowCount` | `true` / `true` | When one is off the other fills the whole cell (visually centers). When off, the corresponding `Text` is not created at all |
| `GhostKeysEnabled` | `false` | When true, ghost keys spawn rain only. No key cell, not counted as input, never trigger tile hits |
| `GhostKeys` | `null` | Token list indexed against the top row's non-stat cells. `"None"` / empty = unassigned. Auto-resized by the GUI to match top-row cell count |
| `GhostRainColor` | `null` | Per-preset rain tint for ghost-key presses. `null` = built-in default `(1.0, 0.9, 0.0, 1.0)` (yellow); applied in `BuildPresetPanel` |

#### KeyViewerRow

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `Cells` | _empty_ | Ordered `List<KeyViewerCell>`. One cell per visible key slot. Edited via the grid widget in the preset edit page |
| `Height` | `60` | Row height in px |
| `RainColor` | `null` | Override per-row rain tint. `null` = white default |
| `ShowRain` | `true` | Per-row rain enable; falls back to white if `RainColor` is null |

#### KeyViewerCell

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `Token` | _required_ | KeyViewer token (e.g. `"A"`, `"LShift"`, `"KPS"`, `"Total"`) — parsed via `KeyViewer.TryParseKey` |
| `Label` | `null` | Optional override for the cell's display text. `null` = use the token's default label |
| `WidthMul` | `1.0` | Per-cell width multiplier. Top-row visible widths distribute by `WidthMul / Σ WidthMul`; lower-row slot widths use the same. Editing one cell's width auto-syncs its mirror (`Cells[N-1-i]`) for symmetry |

Token parsing: `KeyViewer.TryParseKey` maps friendly names (`Tab`, `LShift`, `LCmd` / `RCmd`, `[`, `,`, etc.) and bare letters/digits to `KeyCode`s. Other tokens fall through to `Enum.Parse(KeyCode, …, true)`.

### Key Limiter / Chatter Blocker (`Settings.cs` + `KeyLimiter.cs`)

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `BlockInputsWhileMenuOpen` | `true` | While the Bismuth menu is open, the game sees no keyboard input at all — see "Menu input block" below for the four patched layers. Toggle lives at the top of the Input page |
| `KeyLimiterEnabled` | `true` | Master toggle for the allowed-key filter |
| `KeyLimiterUseKvKeys` | `true` | If true, allowed set = union of active hand + foot preset keys; else parse `KeyLimiterCustomKeys` |
| `KeyLimiterCustomKeys` | `""` | Space/comma-separated key tokens (same parser as KeyViewer) |
| `ChatterBlockerEnabled` | `false` | Master toggle for chatter suppression |
| `ChatterThresholdMs` | `50` | If a press fires within this many milliseconds of the same key's previous accepted press, it is silently dropped |

Implementation: `KeyLimiter.Apply(settings)` populates `_allowed: HashSet<KeyCode>` and `_allowedLabels: HashSet<ushort>` (SkyHook `KeyLabel` enum values, obtained via reflection on `SkyHook.AsyncKeyMapper.UnityKeyToAsyncKey`).

The KeyLimiter, ChatterBlocker, and Ghost-key suppression all share a single press-list iteration in `CountAllowedInPressedKeys` and a single `RDInput.GetMain` postfix.

**Ghost-key suppression** is collected at `Apply` time from the active hand preset's `GhostKeys` into `_ghosts: HashSet<KeyCode>`. It always applies, independent of the limiter and chatter toggles, so pressing a ghost key never registers as a tile hit. The postfix gate fires when any of `_active`, `_chatterActive`, or `_ghosts.Count > 0` is true.

**Menu input block**: the game reads the keyboard through **four independent layers**, all gated on `BlockInputs` (`_blockWhileOpen && UICore.IsOpen && !RDC.auto`). **Autoplay is exempt** (`!RDC.auto`): the game drives an autoplay run through the same input pipeline (`PlayerControl_Update` → planet hits), so blocking it while the panel is open starved the hit tracker — the results showed 0 counts / `NaN%` accuracy. `HitInputEvent` force-hits on `auto`, so a stray keystroke can't double-count. The four layers:

| Layer | Used by | Patch |
| ----- | ------- | ----- |
| `RDInput.GetMain(ButtonState)` | press counting → `mainPressCount` → `scrPlayer.CountValidKeysPressed` → planet hits | postfix → 0 when state = `WentDown` |
| `RDInput.WentDown/IsDown(KeyCode)` | raw shortcut keys (R-restart, arrows, …) — straight `Input.GetKeyDown/GetKey` passthroughs | postfix → false |
| `RDInput.GetState(InputAction, ButtonState)` | Rewired actions behind `restartPress`/`backPress`/`confirmPress`/… properties | postfix → false |
| `UnityEngine.Input.GetKeyDown(KeyCode)` | menu scenes (scnLevelSelect & co.) poll number-key navigation directly, below RDInput | postfix → false, **except `KeyCode.B`** (Ctrl+B must still close the panel). Extern icall — patched via `KeyLimiter.TryPatchRawInput(harmony)` after `PatchAll`, try/caught so a failed native detour only loses this layer |

`ButtonState`: `WentDown=0, WentUp=1, IsDown=2, IsUp=3`. The `AddHit` prefix additionally returns `false` while blocked.

**`RawReadExempt`**: Bismuth's own pollers must keep seeing keys while the menu is open. KeyViewer's `PollKeys` (rain/counting) and PageInput's `KeyListener` (rebind + limiter Listen chips) set `KeyLimiter.RawReadExempt` in try/finally around their reads (main-thread-only plain bool). **Any new mod-side `Input` polling needs the same wrap.** The panel itself reads `UnityEngine.Input` in `UICore.HandleUpdate` (Ctrl+B), covered by the B exemption.

1. **`RDInput.GetMain(ButtonState)` postfix** (limiter/chatter part). Fires when either filter is enabled (`_active || _chatterActive`), state = `WentDown`, and we're not re-entering. Clamps `__result` to `CountAllowedInPressedKeys()`, which iterates `RDInput.GetStateKeys(Down)` (via reflection), resolves each press to a Unity `KeyCode` (direct, async label via `AsyncKeyToUnityKey`, or HID fallback), then applies: **limiter** (drop if `_active && !allowed`) and **chatter** (drop if `_chatterActive` and the key's previous accepted-press time is within `_chatterThresholdSec`). On accept, the key's `_lastPressTime` is updated. P/Space pass when `scrController.state != PlayerControl` (death screen, pause menu, between tiles).
2. **`scrMistakesManager.AddHit(HitMargin)` prefix**. Fallback that returns `false` (suppressing the hit) if no allowed key is currently held (tolerant to 1-frame async delay using `Input.GetKey`).

For the limiter's allowed-set check we always compare in the label direction (`_allowedLabels`), because `AsyncKeyToUnityKey` is ambiguous (multiple `KeyCode`s collapse to one `KeyLabel` slot). The chatter blocker still needs a Unity `KeyCode` as its state-tracking identity, so it calls `AsyncKeyToUnityKey` (best-effort). Collisions just mean a few physically distinct keys share one chatter timer, which is harmless.

**Per-frame idempotency**: `CountAllowedInPressedKeys` may be called multiple times per frame (the game's own `GetMain` invocations + our `GetStateKeys` re-entry). Chatter decisions are cached in `_chatterDecisionThisFrame` (cleared when `Time.frameCount` changes) so the second call doesn't see `now - _lastPressTime[key] ≈ 0` and incorrectly reclassify an already-accepted press as chatter.

**Modifier fallback (`_hidToKeyCode`)**: SkyHook's native bundle shipped with the game reports modifier-key presses with `label = KeyLabel.Unknown(119)` instead of the correct label, because its internal `KeyLabel` table is older than the C# binding. To recover them, `CountAllowedInPressedKeys` consults the raw `AsyncKeyCode.key` field (USB HID Usage IDs, page 0x07, _not_ macOS HIToolbox scancodes) when the label is `Unknown` and looks up `_hidToKeyCode`. Mapping: `0x39→CapsLock, 0xE0→LCtrl, 0xE1→LShift, 0xE2→LAlt, 0xE3→LCmd, 0xE4→RCtrl, 0xE5→RShift, 0xE6→RAlt, 0xE7→RCmd`.

The `0xE1`/`0xE5` codes were confirmed via diagnostic logging. Earlier guesses based on the macOS HIToolbox virtual-key table (`0x38` LShift, `0x3C` RShift, …) did not match the bundle's actual output.

### Hide UI

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `HideAllUI` | `false` | Hides nearly all in-game UI (`RDC.noHud = true`); sub-options hidden from GUI when on |
| `HideHitmeter` | `false` | Hides `scrController.errorMeter` (the hit-error bar). Re-applied on floor change / pause via `HideErrorMeter` patch |
| `HideAutoplayText` | `false` | Hides the "status.autoplay" debug text (`scrShowIfDebug`) via temporary `RDC.auto = false` |
| `HideAutoplayIcon` | `false` | Hides `editor.autoImage` and `editor.buttonAuto` in the editor |
| `HideNoFail` | `false` | Hides no-fail icon (`editor.buttonNoFail`, `uic.noFailImage`) |
| `HideDifficulty` | `false` | Hides difficulty UI (`editor.editorDifficultySelector`, `uic.difficultyImage/Container`) |
| `HidePerfectJudgements` | `false` | Suppresses the "Perfect" floating text |
| `HideLevelName` | `false` | Hides `txtLevelName` (song title) |

### Tweaks

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `LevelNameScale` | `0.3` | `localScale` applied to `txtLevelName.rectTransform` |
| `LevelNameY` | `30` | Additive Y offset from `_levelNameOrigPos` (px) |
| `LevelNameUseOverlayFont` | `true` | Repaint the song title with the Bismuth font via an owned `GameTextShadow` (TMP) + explicit drop shadow; off → the vanilla original shows. `_levelNameFont` resolves from the **game** font family (see Song title in [fonts.md](fonts.md)) |
| `HidePlanetRings` | `false` | **Tweaks tab.** Hides the dotted circle `scrRing` draws around each planet (everywhere rings render: gameplay, editor play-test, level-select previews) |
| `AutoplayPauseEnabled` | `false` | **Tweaks tab.** Whether the editor's play-mode autoplay pause fires at all. When off, `Tweaks.AutoPauseKeyCode()` returns `KeyCode.None` (never matches) |
| `AutoplayPauseKey` | `Space` | **Tweaks tab.** Key that pauses/resumes autoplay while play-testing in the editor. Injected into `scnEditor.Update` by a transpiler (see below), so it's rebindable — the game otherwise hardcodes Space |

### UGUI panel preferences (UI shell)

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `UiScale` | `1.0` | Panel UI scale (0.5–2). Implemented by shrinking the CanvasScaler reference resolution; panel sizeDelta is counter-scaled so on-screen size stays constant |
| `UiFontName` | `"Paperlogy-4Regular"` | Selected panel font (from `FontLoader` scan); missing/stale names fall back to `fonts[0]` |
| `UiAccentCustom` | `false` | If true, the accent color picker is shown instead of preset swatches |
| `UiAccentR/G/B` | periwinkle | Saved accent color, applied via `Theme.ApplyAccent` on init |
| `UiPanelWidth` / `UiPanelHeight` | `840` / `540` | Panel dimensions in canonical scale-1.0 units, saved on Close. Position is **not** saved — the panel always re-centers on Open |

---

## ColorGradient / ColorStop (`ColorGradient.cs`)

``` docs
ColorGradient
  bool  IsSolid          — if true, Evaluate always returns first stop color
  bool  HasPerfectColor  — if true AND t >= 1.0, returns (PR, PG, PB, PA)
  float PR, PG, PB, PA   — "perfect" RGBA (used when HasPerfectColor && t >= 1)
  List<ColorStop> Stops  — sorted by Progress

ColorStop
  float Progress  — t position in [0, 1]
  float R, G, B, A
```

`Evaluate(t)` linearly interpolates between stops; clamps beyond first/last stop.


