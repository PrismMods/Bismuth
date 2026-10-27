# Overlay (HUD)

## Overlay UGUI hierarchy (`Overlay.cs`)

``` filetree
Canvas (ScreenSpaceOverlay, sortingOrder 999)
├── LeftContainer      — VLG, top-left,  holds Left-position stat rows
├── RightContainer     — VLG, top-right, holds Right-position stat rows
├── TimingScaleContainer — VLG, anchor (0.5, 0.12) + Y offset, holds TimingScale row
├── JudgementsContainer— HLG, anchor (0.5, 0.0) + Y offset, holds 9 margin count texts
├── AttemptsContainer  — VLG, anchor (AttemptsX, AttemptsY), holds attempts row + full-attempts row
└── ComboDisplay       — anchor (0.5, 0.87) + Y offset from settings
    ├── ComboLabelWrapper  (ignoreLayout=true, positioned by _comboLabelWrapper.anchoredPosition)
    │   └── ComboLabel  (TextMeshProUGUI — the "Perfect Combo" line)
    └── ComboValue      (TextMeshProUGUI — the integer counter)

FpsCanvas (separate ScreenSpaceOverlay canvas, sortingOrder 1000)
└── FpsDisplay (TextMeshProUGUI, bottom-right)
```

All HUD text is `TextMeshProUGUI`; every text carries a `TmpShadow` component (see [fonts.md](fonts.md)).

### Key private fields

| Field | Type | Purpose |
| ----- | ---- | ------- |
| `_attempts` | `int` | Attempt count for current level; loaded from `AttemptsStore` on level start |
| `_fullAttempts` | `int` | Full-attempt count (starts from 0% only — incremented only when `GCS.checkpointNum == 0` at level start); loaded via `AttemptsStore.GetFull` |
| `_currentLevelKey` | `string` | Level key (file path for custom, level code for official); null between scenes |
| `_combo` | `int` | Current perfect-combo streak |
| `_comboPulseT` | `float` | 1 → 0 over `ComboPulseDuration` seconds; drives label Y-offset animation |
| `_comboLabelWrapper` | `RectTransform` | `ignoreLayout=true` wrapper; `anchoredPosition.y = ComboLabelY` |
| `_levelNameOrigPos` | `Vector2?` | First-seen `anchoredPosition` of `txtLevelName.rectTransform`; reset on scene unload |
| `_levelNameOrigFontSize` | `int?` | First-seen `fontSize` of `txtLevelName`; restored when the previous fontSize-based scale path is detected on disk |
| `_levelNameFont` | `TMP_FontAsset` | Song-title font (game family; set by `SetLevelNameFont`). Rendered via an owned `GameTextShadow` — the old legacy-`Font` swap + `_levelNameOrigFont`/`_levelNameShadow`/`_levelNameGameEffects` fields are gone (the shadow's alpha-0 hide suppresses the game's own Shadow/Outline, and it carries its own drop shadow via `SetShadow`) |
| `_comboLabelShadow` / `_comboValueShadow` | `TmpShadow` | Cached refs to the combo label / count shadow components; written every `ApplySettings` |
| `ShadowBaseOffset` | `const float = 2f` | Per-text drop-shadow base offset px; stat rows / timing scale / judgement texts each scale this by their own size slider |
| `RowBaseFontSize` | `const int = 27` | Base font size of every stat row / timing scale / judgement text; multiplied by the relevant size slider (attempts rows are fixed 18, FPS 22) |
| `ComboLabelBaseFontSize` | `const int = 24` | Base size multiplied by `ComboLabelSize × ComboDisplaySize` |
| `ComboValueBaseFontSize` | `const int = 80` | Base size multiplied by `ComboCountSize × ComboDisplaySize` |

---

## Overlay lifecycle

| Method | Called by | Action |
| ------ | --------- | ------ |
| `OnAttempt()` | `MistakesResetPatch` | `ShowEmpty()` only — `scrMistakesManager.Reset` fires during `scrController.Awake` (init) and `scnEditor.SwitchToEditMode`, not during in-game retries |
| `OnLevelStart(isRestart)` | `ScnGamePlayPatch` / `PressToStartPatch` | `isRestart=true` → in-game retry → `_attempts++`; `isRestart=false && !inLevel && same key` → exit+re-enter → `_attempts++`; `isRestart=false && new key` → load from store. `_fullAttempts++` alongside `_attempts++` only when `GCS.checkpointNum == 0` (start from 0%). Then `inLevel = true`; `ShowEmpty()`; sets both attempts texts; `ShowOrHideElements()` |
| `OnLevelEnd()` | Various patches (wipe, load, ESC) | `inLevel = false` — does **not** reset `_attempts` or `_currentLevelKey` |
| `OnSceneUnloaded()` | `SceneManager.sceneUnloaded` | `inLevel = false`; `RDC.noHud = false`; `_levelNameOrigPos = null` |
| `ShowEmpty()` | After each attempt | Resets displayed values to `--` / `0`; attempts color stays white |
| `UpdateDisplay(acc, xacc, margin)` | `AddHitPatch` (every tile hit) | Updates acc/xacc colors; combo logic; judgement counts |
| `ApplySettings(settings)` | Settings change callback | Re-applies all positions, scales, active states; recomposes stat labels (`ApplySeparators`); master shadow pass; ends with `LayoutRebuilder.ForceRebuildLayoutImmediate` on all four containers so edits reflow instantly |
| `SetFont(font, label, value, comboLabel, comboValue)` | `MainClass.ApplySelectedFont` | Routes TMP font assets to stat labels/values and combo label/count (nulls = base); judgements/FPS use base; re-applies every `TmpShadow` (font change replaces materials) |
| `SetLevelNameFont(font)` | `MainClass.ApplySelectedFont` | Stores the `TMP_FontAsset` for the song title and re-runs `ApplyLevelNameTransform` |
| `ShowOrHideElements()` | Scene change / settings change | Toggles game-native UI visibility (noFail, difficulty, autoplay, song title, error meter) |
| `ApplyLevelNameTransform()` | `ShowOrHideElements()` + `LevelNameTextRestorePatch` | Applies `LevelNameScale` and additive `LevelNameY` to `txtLevelName.rectTransform`; swaps font + Topaz shadow when `LevelNameUseOverlayFont` (see [fonts.md](fonts.md)) |

### Canvas show condition (checked every `Update`)

``` txt
paused           = scrController.instance?.paused ?? false
showOverlayStats = ShowOverlay && (any stat row flag is true)
canvas.active    = inLevel && !paused && !HideAllUI && (showOverlayStats || ShowComboDisplay)
```

### `ApplySettings` row visibility

``` docs
overlayRow.SetActive(ShowOverlay && ShowXxx)   — all stat rows gated on both flags
attemptsRow.SetActive(ShowOverlay && ShowAttempts)
attemptsFullRow.SetActive(ShowOverlay && ShowFullAttempts)
comboDisplayContainer.SetActive(ShowComboDisplay)   — no ShowOverlay gate
```

---

## Combo logic (`UpdateDisplay`)

``` pseudo
if margin == Perfect  OR  (margin == Auto AND ComboCountAuto):
    _combo++
    _comboPulseT = 1.0   → triggers pulse animation
else if margin != Auto:
    _combo = 0           → non-auto non-perfect breaks combo
// Auto + ComboCountAuto=false: no change (neither increments nor breaks)
```

**Pulse animation** (`Update`): each frame while `_comboPulseT > 0`:

``` cs
_comboPulseT -= deltaTime / ComboPulseDuration
_comboLabelWrapper.anchoredPosition.y = (ComboLabelY + ComboPulseOffsetY × _comboPulseT) × ComboDisplaySize
comboDisplayValue.fontSize = round(ComboValueBaseFontSize × ComboDisplaySize × ComboCountSize × (1 + ComboPulseScale × _comboPulseT))
```

Label wrapper drifts up and back; count text re-rasterizes at the pulse-bumped fontSize so the pulse stays crisp (the previous `localScale`-based pulse blurred the text texture at peak). Container `localScale` stays fixed at 1 — all scaling fans into fontSize.

**Combo gradient**: `t = Clamp01(combo / ComboGradientMax)` → applied to `comboDisplayValue.color`.


