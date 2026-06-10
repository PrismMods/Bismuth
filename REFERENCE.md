# Bismuth — Developer Reference

A HarmonyX / UnityModManager overlay mod for **A Dance of Fire and Ice (ADOFAI)**.  
Build: `xbuild Bismuth.sln` (Mono, .NET 4.8). Three expected warnings (toolset version, TextCoreModule ref).

Project philosophy: Minimal and lightweight, but highly customizable.

---

## Log locations

Bismuth can run under two loader stacks. The log paths differ — check the right one for the current install.

### Native UMM (UnityModManager directly)

| Log | Path |
| --- | ---- |
| UMM log (`modEntry.Logger.Log`) | `…/ADanceOfFireAndIce.app/Contents/Resources/Data/Managed/UnityModManager/Log.txt` |
| Unity player log (`Debug.Log`) | `~/Library/Logs/7th Beat Games/A Dance of Fire and Ice/Player.log` |
| Bismuth log (`BismuthLog.Log`) | `<ModPath>/BismuthLog.txt` → `…/Mods/Bismuth/BismuthLog.txt` |

Full UMM path: `/Users/preluminance/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice/ADanceOfFireAndIce.app/Contents/Resources/Data/Managed/UnityModManager/Log.txt`

### MelonLoader + UMMCompat plugin

UMMCompat loads UMM-style mods from `UMMMods/` (not `Mods/`), and `modEntry.Path` resolves there — so `BismuthLog.txt` follows the dll into `UMMMods/Bismuth/`.

| Log | Path |
| --- | ---- |
| MelonLoader log | `…/A Dance of Fire and Ice/MelonLoader/Logs/<YY-M-D_H-M-S>.log` (one file per launch; latest = most recently modified) |
| UMMCompat / per-mod console output | Mixed into the MelonLoader log, prefixed `[UMMCompat]` / `[<ModName>]` |
| Unity player log (`Debug.Log`) | `~/Library/Logs/7th Beat Games/A Dance of Fire and Ice/Player.log` |
| Bismuth log (`BismuthLog.Log`) | `<ModPath>/BismuthLog.txt` → `…/UMMMods/Bismuth/BismuthLog.txt` |

Full MelonLoader log dir: `/Users/preluminance/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice/MelonLoader/Logs/`  
Full UMMMods Bismuth log: `/Users/preluminance/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice/UMMMods/Bismuth/BismuthLog.txt`

`deploy.sh` writes to `Mods/Bismuth/`, but UMMCompat appears to sweep mods into `UMMMods/` on startup — so the live dll, settings, and log all end up there. If `Mods/Bismuth/BismuthLog.txt` is stale or missing, check `UMMMods/Bismuth/BismuthLog.txt` instead.

### Log lifecycle

`BismuthLog.txt` is cleared and re-created each session (on `StartMod`). Call `BismuthLog.Log("message")` from anywhere — swallows IO errors silently. If `StartMod` never runs (mod disabled in the manager UI, or load failure), the log keeps its previous-session contents and won't reflect the current launch — so a stale-looking log usually means "mod didn't start," not "logger broke."

---

## Files

Sources live in subfolders under `Bismuth/`. Several classes are split into `partial`s; each part is a separate file. Folder map:

```text
Bismuth/
├── MainClass.cs              UMM entry point — loads settings, creates Overlay + KeyViewer, wires GUI callbacks
├── Startup.cs                Early-init hooks (font preload, etc.)
├── KeyViewer/
│   ├── KeyViewer.cs          partial: class shell, state fields, internal cell/column types, lifecycle
│   ├── KeyViewer.Build.cs    partial: BuildLayout / BuildPresetPanel / cell + layer construction
│   ├── KeyViewer.Rain.cs     partial: per-frame Update, StartRainColumn / StopRainColumn
│   ├── KeyViewer.Sprites.cs  partial: rain shadow body sprites + shadow tip texture + gradient texture
│   ├── KeyViewer.Keys.cs     partial: TryParseKey + GetDisplayName
│   ├── RoundedRectGraphic.cs Procedural rounded-rect MaskableGraphic — fill, border ring, AA fringe
│   └── KeyLimiter.cs         Harmony patches limiting input to active preset keys (sync + async/SkyHook paths)
├── Overlay/
│   ├── Overlay.cs            partial: class shell, state, lifecycle (Awake/OnDestroy/scene hooks), helpers
│   ├── Overlay.Build.cs      partial: UGUI tree construction (canvas, containers, rows, combo, FPS, judgements)
│   ├── Overlay.Update.cs     partial: per-frame Update + UpdateDisplay
│   ├── Overlay.Game.cs       partial: OnAttempt, OnLevelStart, OnLevelEnd, ShowEmpty, SetFont, ResetAttempts
│   ├── Overlay.Apply.cs      partial: ApplySettings, PlaceRows, Attach, ShowOrHideElements, ApplyLevelNameTransform
│   └── ColorGradient.cs      `ColorGradient` / `ColorStop` types + `Evaluate(t)`
├── Settings/
│   ├── Settings.cs           All serialized mod settings + gradient/preset defaults
│   ├── SettingsInput.cs      Shared input widgets (deferred-commit text, Slider with undo button)
│   ├── SettingsGui.cs        partial: entry point + shared state fields + nav-state reset
│   ├── SettingsGui.Overlay.cs    partial: overlay stat rows
│   ├── SettingsGui.Combo.cs      partial: combo display section
│   ├── SettingsGui.KeyViewer.cs  partial: key viewer category + preset edit page + KV color editor
│   ├── SettingsGui.KeyLimiter.cs partial: key limiter section
│   ├── SettingsGui.HideUi.cs     partial: hide UI toggles
│   ├── SettingsGui.Tweaks.cs     partial: tweaks + optimizations + font + misc
│   ├── SettingsGui.Gradient.cs   partial: gradient + color-stop editors
│   └── SettingsGui.Helpers.cs    partial: Indent / W / WMax / DrawSwatch / hex / position buttons / SliderRow shim
├── Patches/
│   ├── Patches.cs            HarmonyX prefix/postfix patches for overlay/judgement/UI hooks
│   └── Optimizations.cs      Performance tweak patches (texture, physics, DOTween, etc.)
├── UI/                       UGUI settings shell — in-progress replacement for IMGUI SettingsGui
│   ├── UICore.cs             Root canvas + panel + titlebar + footer + body layout + hotkey + open/close + UI scale
│   ├── Theme.cs              Color palette + runtime accent system (AccentFill/AccentBorder markers) + lazy OS-font + 2×2 white sprite
│   ├── UIBuilder.cs          Static widget factory: Rect/Label/SectionHeader(+WithHelp)/Toggle/Collapsible/Slider/Button/DangerButton/ColorPicker/TextInput/Segmented/CycleSelector/ExpandSection/GradientEditor/AccentSwatches + ClickHandler/HoverHandler
│   ├── DragHandle.cs         Titlebar drag: re-parents pointer events to the panel RectTransform
│   ├── ResizeHandle.cs       8 edge/corner resize handles — BR corner 22px (visible grip), others 12px
│   ├── TabRail.cs            Left-rail tab nav; auto-wraps each page in a ScrollRect/Viewport/Content
│   └── Pages/
│       ├── KeyTokens.cs      Shared TokenFromKeyCode / PrettyTokenLabel helpers (mirrors SettingsGui table)
│       ├── PageOverlay.cs    Overlay stats, combo display, attempts (+ full attempts), song title
│       ├── PageKeyViewer.cs  Preset lists + full preset editor (row grid, drag-reorder, rebind, submenus)
│       ├── PageInput.cs      Menu input-block toggle + Key Limiter (chip editor + listen) + Chatter Blocker
│       ├── PageHideUi.cs     Hide UI toggles with conditional sub-container
│       └── PageUI.cs         Panel scale slider, font cycle, accent color (swatches or custom picker)
└── Util/
    ├── AttemptsStore.cs      Persists per-level attempt counts to `BismuthAttempts.txt`
    ├── BismuthLog.cs         File-based session logger → `BismuthLog.txt`
    └── FontLoader.cs         Scans for fonts and exposes them to the GUI
```

When adding a new section to the settings GUI, follow the partial-file pattern: put state-fields/Draw entry calls in the section partial, expose a `Draw<Name>Section(settings, ref changed)` helper, and call it from `Draw()` in `SettingsGui.cs`. The class shell in each partial uses `internal static partial class SettingsGui` (or `internal/public partial class …` for `KeyViewer` / `Overlay`).

---

## UI shell (`UI/`)

Bismuth is mid-migration from UMM's IMGUI settings panel to a self-owned UGUI canvas — same loader (UMM-style entry point, UMMCompat-compatible), different render path. Both run in parallel during the port; the IMGUI panel will be removed once feature parity is reached.

### Architecture

Modeled on KorenResourcePack v2's UI structure, stripped to the minimum:

- **No dependencies added** — uses `UnityEngine.UI.Text` (legacy), reuses `RoundedRectGraphic`, no TMP, no tween library, no asset bundles. Arial via `Font.CreateDynamicFontFromOSFont`; a 2×2 white texture generated once at runtime is the only sprite.
- **Static-only** — `UICore` is a static class, not a MonoBehaviour. UMM's `modEntry.OnUpdate` drives `UICore.HandleUpdate()` every frame. `UICore.Initialize(modEntry, settings, onChanged)` builds the canvas; `UICore.Dispose()` tears it down on mod disable.
- **Sharp/minimal aesthetic** — flat rectangles, 1px hairline borders (`UIBuilder.AddBorder`), no rounded panels, no fades. The only rounded geometry is the radio-button widget via `RoundedRectGraphic`.

### Widget conventions

- Toggle = **classic radio button**: outer ring + filled inner dot when on (not a square checkbox).
- Row height: `UIBuilder.RowHeight = 32f`; sections use `UIBuilder.SectionHeader` (small caps bold, muted color).
- Section spacing: `UIBuilder.Spacer(content)` between groups.
- All widgets register a `ClickHandler` (lightweight `IPointerClickHandler`) — no `Selectable`/`Button` state machine.
- Hover tinting uses `HoverHandler` (enter/exit only), **never** `EventTrigger` — EventTrigger implements `IScrollHandler` and silently eats mouse-wheel events, breaking ScrollRect scrolling whenever the cursor is over the widget.
- Destructive actions use `UIBuilder.DangerButton` — red-tinted bg, two-click confirm ("Click again to confirm"), 3s auto-revert timer.
- Section help uses `UIBuilder.SectionHeaderWithHelp(parent, title, helpText)` — a 14px `[?]` icon after the header label; hovering shows a tooltip popup parented to the canvas root (so it renders above the scroll viewport instead of being clipped by its RectMask2D). The header's HLG needs `childControlWidth = true` so the icon's `LayoutElement.preferredWidth` is honored.

### Tab + page layout (`TabRail.cs`)

`TabRail` owns the left rail and the page host. Each page registered via `Tabs.AddTab(name, buildPage)` gets:

1. A page `RectTransform` filling the page host, `SetActive(false)` by default.
2. A `ScrollRect` on the page, with a `Viewport` (RectMask2D + transparent raycast-target Image) and a `Content` child (VerticalLayoutGroup + ContentSizeFitter).
3. The `buildPage(Content)` callback runs once at registration to populate widgets.

**Scrolling gotcha:** `VerticalLayoutGroup.childControlHeight` must be `true` so VLG honors each row's `LayoutElement.preferredHeight`. With it off, VLG reads the bare `RectTransform.sizeDelta.y` (which is 0 for all rows) and the content collapses to zero height — `ContentSizeFitter` then sets content height to 0 and there's nothing to scroll. The viewport also needs a raycast-target Graphic (transparent Image) so the scroll wheel has a handler to land on when the cursor is between rows.

### Hotkey

Toggle: **Ctrl + B** only, in `UICore.HandleUpdate`.

**macOS dead-key gotcha:** the original `Alt + B` binding does not work on macOS. Option (Unity's `LeftAlt`) is a system-level dead-key modifier — Option+B is reserved for typing `∫`, and the B keystroke is swallowed before reaching `Input.GetKeyDown(KeyCode.B)`. Diagnosed via `Input.anyKeyDown` logging: Alt-alone fired, Space fired, but a B keydown was never observed during Option+B chords. Cmd and Ctrl have no such behavior. Function keys (F1–F12) bypass the issue entirely since no modifier is held.

This applies to all future hotkeys: **never bind Option + letter on macOS.** Cmd, Ctrl, Shift, or single function keys only.

### Loader-loop driver

`MainClass.Setup` assigns `modEntry.OnUpdate = (_, __) => UICore.HandleUpdate();` — fires every frame the mod is enabled. Verified under MelonLoader + UMMCompat on Unity 6 (6000.3.10f1); periodic "[UI] HandleUpdate alive frame=N" log entries confirm continuous ticking past startup.

`UICore.Initialize(...)` is called from `MainClass.TryEagerInit`, which is itself gated by `IsEngineReady()` and retried on first scene load when the engine wasn't ready at toggle-on. This shares the same deferred-init mechanism as Overlay / KeyViewer construction — necessary because `koren UMM` / UMMCompat can load mods before game statics are alive (calling asset APIs that early crashes the engine, uncatchable).

---

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
| `ShowTimingScale` | `true` | Show timing scale row |
| `ShowJudgements` | `true` | Show hit-margin count row |
| `ShowAttempts` | `false` | Show attempts counter |
| `ShowFullAttempts` | `false` | Show full-attempts counter (attempts started at 0% — checkpoint restarts excluded). Renders as a second row under Attempts in the same container |
| `Scale` | `1.0` | Scale applied to left/right overlay columns |
| `FontName` | `"Maplestory Bold"` | Font used for all overlay text |

### Row positions (`OverlayPosition` enum: `Left` / `Right`)

| Field | Default |
| ----- | ------- |
| `ProgressPosition` | Left |
| `AccPosition` | Left |
| `XAccPosition` | Left |
| `BpmPosition` | Right |
| `TileBpmPosition` | Right |

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
| `ComboShadowOffsetX` / `ComboShadowOffsetY` | `4` / `-4` | Count (`comboDisplayValue`) drop-shadow offset in px; multiplied by `ComboDisplaySize × ComboCountSize` |
| `ComboShadowColor` | `(0, 0, 0, 0.5)` | Count drop-shadow `Shadow.effectColor` |
| `ComboLabelShadowOffsetX` / `ComboLabelShadowOffsetY` | `2.5` / `-2.5` | Label drop-shadow offset in px; multiplied by `ComboDisplaySize × ComboLabelSize` |
| `ComboLabelShadowColor` | `(0, 0, 0, 0.5)` | Label drop-shadow color |
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
| `BlockInputsWhileMenuOpen` | `true` | While the Bismuth UGUI menu is open, no gameplay inputs register at all (both `GetMain` and `AddHit` paths short-circuit on `UICore.IsOpen`). Toggle lives at the top of the Input page |
| `KeyLimiterEnabled` | `true` | Master toggle for the allowed-key filter |
| `KeyLimiterUseKvKeys` | `true` | If true, allowed set = union of active hand + foot preset keys; else parse `KeyLimiterCustomKeys` |
| `KeyLimiterCustomKeys` | `""` | Space/comma-separated key tokens (same parser as KeyViewer) |
| `ChatterBlockerEnabled` | `false` | Master toggle for chatter suppression |
| `ChatterThresholdMs` | `50` | If a press fires within this many milliseconds of the same key's previous accepted press, it is silently dropped |

Implementation: `KeyLimiter.Apply(settings)` populates `_allowed: HashSet<KeyCode>` and `_allowedLabels: HashSet<ushort>` (SkyHook `KeyLabel` enum values, obtained via reflection on `SkyHook.AsyncKeyMapper.UnityKeyToAsyncKey`).

The KeyLimiter, ChatterBlocker, and Ghost-key suppression all share a single press-list iteration in `CountAllowedInPressedKeys` and a single `RDInput.GetMain` postfix.

**Ghost-key suppression** is collected at `Apply` time from the active hand preset's `GhostKeys` into `_ghosts: HashSet<KeyCode>`. It always applies — independent of the limiter / chatter toggles — so pressing a ghost key never registers as a tile hit. The postfix gate fires when any of `_active`, `_chatterActive`, or `_ghosts.Count > 0` is true.

**Menu input block**: when `BlockInputsWhileMenuOpen` is set and `UICore.IsOpen`, the `GetMain` postfix zeroes `__result` (main port only, `__0 == 0`) and the `AddHit` prefix returns `false` — before any limiter/chatter logic runs. The flag is cached as `_blockWhileOpen` in `Apply`.

1. **`RDInput.GetMain(int state)` postfix** — fires when either filter is enabled (`_active || _chatterActive`), state = `ButtonState.Down`, and we're not re-entering. Clamps `__result` to `CountAllowedInPressedKeys()`, which iterates `RDInput.GetStateKeys(Down)` (via reflection), resolves each press to a Unity `KeyCode` (direct, async label via `AsyncKeyToUnityKey`, or HID fallback), then applies: **limiter** (drop if `_active && !allowed`) and **chatter** (drop if `_chatterActive` and the key's previous accepted-press time is within `_chatterThresholdSec`). On accept, the key's `_lastPressTime` is updated. P/Space pass when `scrController.state != PlayerControl` (death screen, pause menu, between tiles).
2. **`scrMistakesManager.AddHit(HitMargin)` prefix** — fallback that returns `false` (suppressing the hit) if no allowed key is currently held (tolerant to 1-frame async delay using `Input.GetKey`).

For the limiter's allowed-set check we always compare in the label direction (`_allowedLabels`) — `AsyncKeyToUnityKey` is ambiguous (multiple `KeyCode`s collapse to one `KeyLabel` slot). The chatter blocker still needs a Unity `KeyCode` as its state-tracking identity, so it calls `AsyncKeyToUnityKey` (best-effort) — collisions just mean a few physically distinct keys share one chatter timer, which is harmless.

**Per-frame idempotency**: `CountAllowedInPressedKeys` may be called multiple times per frame (the game's own `GetMain` invocations + our `GetStateKeys` re-entry). Chatter decisions are cached in `_chatterDecisionThisFrame` (cleared when `Time.frameCount` changes) so the second call doesn't see `now - _lastPressTime[key] ≈ 0` and incorrectly reclassify an already-accepted press as chatter.

**Modifier fallback (`_hidToKeyCode`)**: SkyHook's native bundle shipped with the game reports modifier-key presses with `label = KeyLabel.Unknown(119)` instead of the correct label — its internal `KeyLabel` table is older than the C# binding. To recover them, `CountAllowedInPressedKeys` consults the raw `AsyncKeyCode.key` field (USB HID Usage IDs, page 0x07 — _not_ macOS HIToolbox scancodes) when the label is `Unknown` and looks up `_hidToKeyCode`. Mapping: `0x39→CapsLock, 0xE0→LCtrl, 0xE1→LShift, 0xE2→LAlt, 0xE3→LCmd, 0xE4→RCtrl, 0xE5→RShift, 0xE6→RAlt, 0xE7→RCmd`.

The `0xE1`/`0xE5` codes were confirmed via diagnostic logging — earlier guesses based on the macOS HIToolbox virtual-key table (`0x38` LShift, `0x3C` RShift, …) did not match the bundle's actual output.

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
| `LevelNameScale` | `0.5` | `localScale` applied to `txtLevelName.rectTransform` |
| `LevelNameY` | `40` | Additive Y offset from `_levelNameOrigPos` (px) |

### UGUI panel preferences (UI shell)

| Field | Default | Purpose |
| ----- | ------- | ------- |
| `UiScale` | `1.0` | Panel UI scale (0.5–2). Implemented by shrinking the CanvasScaler reference resolution; panel sizeDelta is counter-scaled so on-screen size stays constant |
| `UiFontName` | `""` | Selected panel font (from `FontLoader` scan); empty = OS Arial fallback |
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

---

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
    │   └── ComboLabel  (Text — the "Perfect Combo" line)
    └── ComboValue      (Text — the integer counter)
```

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
| `_comboLabelShadow` / `_comboValueShadow` | `Shadow` | Cached refs to the combo label / count drop-shadow components; written every `ApplySettings` |
| `ShadowBaseOffset` | `const float = 2f` | Per-text drop-shadow base offset (matches `AddShadow`'s `effectDistance = (2, -2)`); stat rows / timing scale / judgement texts each scale this by their own size slider |
| `RowBaseFontSize` | `const int = 30` | Base font size of every stat row / timing scale / judgement text; multiplied by the relevant size slider |
| `ComboLabelBaseFontSize` | `const int = 30` | Base size multiplied by `ComboLabelSize × ComboDisplaySize` |
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
| `ApplySettings(settings)` | Settings change callback | Re-applies all positions, scales, active states |
| `ShowOrHideElements()` | Scene change / settings change | Toggles game-native UI visibility (noFail, difficulty, autoplay, song title, error meter) |
| `ApplyLevelNameTransform()` | `ShowOrHideElements()` + `LevelNameTextRestorePatch` | Applies `LevelNameScale` and additive `LevelNameY` offset to `txtLevelName.rectTransform` |

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

---

## KeyViewer lifecycle

The KeyViewer is created once per session as a `DontDestroyOnLoad` `GameObject` named `BismuthKeyViewer` and holds its own `ScreenSpaceOverlay` canvas (`sortingOrder = 100`). It owns no scene-level hooks of its own — all lifecycle calls fan out from `MainClass`.

| Method | Called by | Action |
| ------ | --------- | ------ |
| `Create(settings)` (static) | `MainClass.TryEagerInit` | Creates the GameObject, calls `BuildCanvas`, `LoadCounts` (if any active preset has `PersistCounts`), and `BuildLayout` (if `AnyViewerOn`). Stashes `Instance` for cross-reload guard |
| `BuildCanvas()` | `Create` only | Adds `Canvas` + `CanvasScaler` (1080p ref, ScaleWithScreenSize, matchWidthOrHeight 0.5) + `GraphicRaycaster`; sets initial active state via `AnyViewerOn` |
| `BuildLayout()` | `Create`, `Rebuild`, lazy on first `AnyViewerOn = true` | Calls `BuildPresetPanel` for hand (sortBase 100) and foot (sortBase 1000); resets `_lastKps`/`_lastTotalPerPreset` |
| `ApplySettings(settings)` | `MainClass.OnGUI` → `onChanged` callback | Toggles canvas active, lazy-builds layout if first true, re-positions/re-scales hand+foot panels from preset, calls `ApplyColors` (pushes `BgIdle`/`BorderIdle` + text colors to every live cell) |
| `Rebuild(settings)` | `MainClass.OnGUI` → `onKeyViewerRebuild` callback (fires when `_needsKvRebuild`) | `ClearLayout` + `BuildLayout` + active toggle. Used for structural edits (key width, border radius, border width, gap, row keys, etc.) |
| `ResetCounts()` | `MainClass.OnGUI` → `onKeyViewerReset` callback ("Reset Counters" button) | Zeros every per-preset `_counts` dict, empties `_hitTimes`, zeros every visible Count/Value text, resets `_lastKps`/`_lastTotalPerPreset` |
| `SetFont(font)` | `MainClass.ApplySelectedFont` | Pushes the new font to every live cell's Name/Count text and stat cell text |
| `SaveCounts()` | `MainClass.OnSaveGUI` (UMM "Save") and `MainClass.StopMod` | Writes `keycounts.txt` (one tab-separated `presetName\tkeycode\tcount` per line). No-op if no active preset has `PersistCounts` |
| `LoadCounts()` | `Create` only (when `NeedsPersist`) | Parses `keycounts.txt` into `_counts`. Counts re-populate visible cells when those cells get built/rebuilt |
| `OnDestroy()` | Unity, when `MainClass.StopMod` calls `Destroy(gameObject)` | Clears `Instance` so a later mod-enable cycle doesn't see the orphaned reference |

`MainClass.StopMod` always calls `SaveCounts()` before `Destroy(gameObject)`, so per-key counts survive a mod-disable / re-enable cycle even without an explicit UMM save.

### Canvas show condition

`AnyViewerOn(settings)` gates both panel construction and canvas activation:

``` txt
canvas.active = !HideAllUI
                && ShowKeyViewer
                && ((ShowHandViewer && Hand != null) || (ShowFootViewer && Foot != null))
```

`ApplySettings` calls `_canvas.gameObject.SetActive(AnyViewerOn(...))` and builds the layout lazily on the first frame where the condition flips on. Settings UI changes route through `MainClass.OnGUI`'s `onChanged` → `keyViewer.ApplySettings`; structural changes additionally fire `onKeyViewerRebuild` (see `_needsKvRebuild` below).

### Per-frame Update flow (`KeyViewer.Rain.cs`)

The MonoBehaviour `Update` runs every frame regardless of canvas active state (Unity calls Update on the GameObject; the canvas being inactive only suppresses rendering, not script execution). The flow:

1. For each registered key in `_keys`: query `Input.GetKeyDown` / `Input.GetKeyUp`.
2. **Down** — enqueue `realtimeSinceStartup` into `_hitTimes` (unless ghost), bump `_counts[preset.Name][key]` for each cell rendering this key, swap each cell's `Bg.color` → `BgHeld`, `Bg.BorderColor` → `BorderHeld`, `Name.color` → `TxtHeld`, `Count.color/text` → `CountHeld` / new count; update each preset's `Total` if any cells own one; spawn rain if the row's rain is enabled.
3. **Up** — swap each cell back to the `*Idle` colours; stop the rain column (transition to dying state).
4. Drain `_hitTimes` of entries older than 1s; if `_hitTimes.Count` changed, push the new KPS into every KPS cell.
5. Iterate `_rainColumns` and advance each (growing → grow Height; dying → grow BotY; destroy when BotY ≥ `RainTrackLength`).

Idle/Held colour swaps are done per-cell-per-event (not per-frame), so a held key just keeps its `*Held` colours until the up event swaps them back.

## KeyViewer rendering (`KeyViewer/*`)

Two independent UGUI panels parented to a single screen-space-overlay canvas (`sortingOrder = 100`). `BuildLayout` calls `BuildPresetPanel` for the hand and foot presets with distinct `sortBase` values (`100` and `1000`) so their sub-canvases never collide.

### Cell rendering (`RoundedRectGraphic.cs`)

Key cells and stat cells (KPS / Total) use a `RoundedRectGraphic` background instead of a baked rounded sprite. It's a `MaskableGraphic` subclass that procedurally tessellates the rounded rect in `OnPopulateMesh` at the cell's actual size — so corners stay smooth at any resolution / scale, and `Radius` / `BorderWidth` are continuous parameters rather than fixed source-texture pixel counts.

| Layer | Geometry | Color |
| ----- | -------- | ----- |
| Fill | Triangle fan from `rect.center` to the **inner** outline (4 arcs × `segs+1` verts, CCW) | `Graphic.color` (inherited; what `Bg.color = ...` writes to) |
| Border ring | Quad strip between outer and inner outlines (only when `BorderWidth > 0` and `BorderColor.a > 0`) | `BorderColor` |
| AA fringe | Quad strip extending `AAFringe` units (default `1.25`) outside the outer outline | `BorderColor` (if border present) or fill color, alpha fading 1 → 0 |

**Outline construction.** For each rounded rect we sample 4 quarter-arcs centred at the four offset corners of the (bw-inset for inner) rect. The fringe outline shares the **outer** corner centres with radius `r + AAFringe`, so straight-edge fringe segments line up with corner-arc fringe segments without per-vertex normal computation.

**Segment count** scales with radius to keep chord length ≤ ~1 unit:

``` cs
segs = Mathf.Clamp(Mathf.CeilToInt((r + fringe) * Mathf.PI * 0.5f), 4, MaxCornerSegments)   // MaxCornerSegments default 48
```

A coarser previous formula (`r / 2`, cap 16) produced visible polyline facets inside the smooth fringe at typical small radii — `r * π/2` keeps each chord sub-pixel up to r ≈ 30.

**Live colour updates.** `KeyViewer.Rain.cs` swaps both `Bg.color` and `Bg.BorderColor` on key down/up using `BgIdle`/`BgHeld` and `BorderIdle`/`BorderHeld` from the cell's preset. `KeyViewer.ApplyColors` re-pushes both on every `ApplySettings` so settings-panel colour edits propagate to live cells.

Cell refs (`KeyCellRefs.Bg`, `StatCellRefs.Bg`) are typed as `RoundedRectGraphic` rather than `Image`. Color assignments still work because `Graphic.color` is the inherited base property; mesh re-tessellation happens automatically through `SetVerticesDirty`.

### Per-row layers

Each panel owns:

- **One shared shadow layer** (`Canvas, sortingOrder = sortBase`) — every rain column's shadow Graphics for that panel render in here. Lower than any rain layer in that panel, so column B's rain always draws on top of column A's shadow.
- **One rain layer per row** (`Canvas, sortingOrder = sortBase + 10 + rowIndex`) — the row's per-key rain body + tip Graphics.

`_rainLayers` / `_shadowLayers` map global row index → layer `RectTransform`. The top row defines rain X positions; lower rows remap their rain X into the top row's column slots (left-aligned to midpoint, right-aligned to midpoint, see `BuildPresetPanel`).

### Rain column lifecycle

On key down, `StartRainColumn` creates body + tip Graphics (and shadow body + shadow tip Graphics if `RainShadowSize > 0`), pushes a `RainColumn` into `_rainColumns`. `Update` advances each column:

- **Growing** (`Growing = true`, set until key up) — `Height` grows at `RainSpeed * dt`; `BotY` stays at 0.
- **Dying** — `Growing = false`; `BotY` grows at `RainSpeed * dt`. When `BotY >= RainTrackLength`, all Graphics are destroyed and the column removed.

Per frame, the rain body covers `[BotY, min(BotY+Height, fadeStart)]` (sharp opaque rectangle). The rain tip covers `[max(BotY, fadeStart), min(BotY+Height, fadeEnd)]` with `uvRect.y = (tipBot - fadeStart) / fadeZoneH` sampling the 2-row gradient texture (`GetGradientTex`).

### Shadow sprites + textures

| Cache | Builder | Purpose |
| ----- | ------- | ------- |
| `_shadowBodySprites[shadowSize]` | `GetShadowBodySprite` (`softTop: false`) | 9-slice body sprite, fade on L/B/R, sharp top. Used while a rain tip exists above the body so the body meets the tip seamlessly at `fadeStart` |
| `_shadowBodySpritesSoftTop[shadowSize]` | `GetShadowBodySpriteSoftTop` (`softTop: true`) | All-sides fade variant. Used while the body's top _is_ the rain top (growing, `BotY+Height ≤ fadeStart`) so the shadow gets a soft halo above the rain |
| `_shadowTipTextures[(shadowSize << 16) \| rainWidth]` | `GetShadowTipTex(shadowSize, rainWidth)` | 2-row texture, baked at the actual rect width so the side blur stays exactly `shadowSize` px (RawImage has no 9-slice; stretching would distort blur fraction). Bottom row = horizontal-blur opaque, top row = transparent. `Rain.Update` sets `uvRect` to match the rain tip's fade |
| `_gradTex` | `GetGradientTex` | 1×2 RGBA texture: white opaque → white transparent. Used by the rain tip (not shadow tip — that has horizontal blur baked in) |
| `_allSprites` / `_allTextures` | — | Tracked for cleanup on `ClearLayout` |

The shadow body's rect height is `bodyH + ShadowSize` (with sharp-top) or `bodyH + 2*ShadowSize` (with soft-top, extending fade above the rain). Its bottom Y is `panelTop + BotY - ShadowSize`. When `BotY > fadeStart`, an alpha multiplier `Mathf.Clamp01(1 - (BotY - fadeStart) / fadeZoneH)` is applied to the shadow body color so the trailing extension fades out with the rain instead of hanging in mid-air.

### Cell + count state

| Field | Purpose |
| ----- | ------- |
| `_keyCells[KeyCode]` | `List<KeyCellRefs>` — every cell rendering this key (hand and foot presets can each own one). All entries get updated on key down/up |
| `_kpsCells` / `_totalCells` | `List<StatCellRefs>` — stat cells track preset for color/font resolution. Each `Total` cell sums only its own preset's counts |
| `_counts[presetName][KeyCode]` | Per-preset, per-key total. Persisted to `keycounts.txt` if any active preset has `PersistCounts = true`. File format: tab-separated `presetName\tKeyCode\tcount` per line |
| `_lastTotalPerPreset[presetName]` | Cached last-written `Total` value per preset, drives the dirty check that avoids re-writing the text every keydown |
| `_hitTimes` | `Queue<float>` — `realtimeSinceStartup` of recent hits; KPS = count where `now - peek <= 1s` |
| `_rainEnabled` | `HashSet<KeyCode>` — keys whose row has `ShowRain = true` (plus any ghost keys). Populated during `BuildPresetPanel` |
| `_rainColors` | Per-key custom rain color. Missing → falls back to `Color.white` |
| `_ghostKeys` | `HashSet<KeyCode>` — keys flagged as ghost. Rain still spawns; `_hitTimes` / `_counts` / `_totalCells` are skipped on press |

---

## HarmonyX patches (`Patches.cs`)

| Patch target | Timing | Action |
| ------------ | ------ | ------ |
| `scrMistakesManager.Reset` | Postfix | `OnAttempt()` |
| `scrMistakesManager.AddHit(HitMargin)` | Postfix | `UpdateDisplay(percentAcc, percentXAcc, hit)` |
| `scnGame.Play(seqID, isRestart)` | Postfix | `OnLevelStart(isRestart)` — custom levels; `isRestart` param distinguishes retry from first load |
| `scrPressToStart.ShowText` | Postfix | `OnLevelStart(false)` — official levels (always a fresh entry from this hook) |
| `scrController.StartLoadingScene` | Postfix | `OnLevelEnd()` |
| `scrUIController.WipeToBlack` | Postfix | `OnLevelEnd()` |
| `StateBehaviour.ChangeState(States.None)` | Postfix | `OnLevelEnd()` |
| `scnEditor.ResetScene` | Postfix | `OnLevelEnd()` |
| `scnEditor.SwitchToEditMode` | Postfix | `ShowOrHideElements()` |
| `scrController.LevelNameTextRestore` | Postfix | `ApplyLevelNameTransform()` — re-applies our scale/offset after the game restores canonical position |
| `scrShowIfDebug.Update` | Pre+Post | Temporarily sets `RDC.auto = false` (if `HideAutoplayText \|\| HideAllUI`) to suppress the autoplay text |
| `scrHitTextMesh.Show` | Prefix | Moves judgement popup off-screen (`HideAllUI`) or suppresses it (`HidePerfectJudgements`) |
| `scrHitText.Show(Vector3, float)` | Prefix | Moves legacy judgement text off-screen (`HideAllUI`) |
| `scrMissIndicator.Awake` | Postfix | Moves miss indicator off-screen (`HideAllUI`) |
| `scrPlanet.MoveToNextFloor` | Postfix | Hides error meter (`HideAllUI` or `HideHitmeter`) |
| `scrController.paused` (setter) | Postfix | Hides error meter (`HideAllUI` or `HideHitmeter`) |
| `OttoButtonController.Update` | Postfix | Hides Otto debug button (`HideAllUI`) |
| `RDInput.GetMain(int)` | Postfix | Key Limiter — clamps press count to allowed-key count when state=Down; zeroes it entirely while the Bismuth menu is open (`BlockInputsWhileMenuOpen`) |
| `scrMistakesManager.AddHit(HitMargin)` | Prefix | Key Limiter — suppresses hit if no allowed key currently held, or unconditionally while the menu is open |

### Optimizations (`Optimizations.cs`)

Independent file with Harmony patches gated on `Opt*` settings: `scrConductor.Update` (spectrum throttle), `TextureManager.LoadTexture` / `CustomTexture.GetTexture` / `CustomSprite.GetSprite` (non-readable / DXT), `scrPlanet.Update` / `scrFloor.Update` (physics non-alloc / DOTween fix).

---

## SettingsGui patterns

| Helper | Signature | Purpose |
| ------ | --------- | ------- |
| `W(px)` | `GUILayoutOption` | `GUILayout.Width(px × _uiScale)` |
| `WMax(px)` | `GUILayoutOption` | `GUILayout.MaxWidth(px × _uiScale)` |
| `Indent(action, space=20f)` | `void` | `BeginHorizontal → Space(space) → action() → EndHorizontal`. Captured locals can't be `ref` — set a local bool inside and copy out afterwards |
| `SliderRow(label, out result, value, min, max, indent=20f, fmt="F2", suffix="")` | `void` | Thin shim that delegates to `SettingsInput.Slider`. Use the new API in fresh code |
| `DeferredText(canonical, width, out committed)` | `bool` | Thin shim that delegates to `SettingsInput.DeferredText` |
| `DrawGradientEditor(key, gradient, ref changed)` | `void` | Full gradient editor (solid toggle, perfect color, per-stop RGB) |
| `DrawPositionButtons(current, out result)` | `bool` | L/R buttons for `OverlayPosition` (left button disabled when already Left, vice versa) |

Sections nested inside the Overlay section use `indent=40f` in `SliderRow` to appear visually indented below their section headers (which are already at the overlay's 20px indent).

`_uiScale = GUI.matrix.m00` is read at the start of every `Draw()` call.

`_noWrapLabel` is a cached `GUIStyle` with `wordWrap = false`; initialized lazily and passed to every `GUILayout.Label` call to prevent wrapping at any scale.

### SettingsInput (`Settings/SettingsInput.cs`)

Shared input widgets owning per-frame counter + deferred-edit buffers + per-control undo baselines. Driven by the SettingsGui Draw loop:

- `BeginFrame(uiScale, noWrapLabel)` — reset counter, refresh cached scale/style. Called at the start of every `SettingsGui.Draw`.
- `ResetState()` — wipe edit buffers and undo baselines. Called whenever `_editingPreset` / `_editingIsFoot` changes (navigation between main menu / hand presets / foot presets) so stale state doesn't carry across views.
- `Slider(label, ref float value, min, max, indent, fmt, suffix, step=0)` — renders label + editable text + slider + inline undo button (`↶`). Returns `true` if `value` changed.
- `Slider(label, ref int value, …)` — int overload that snaps to `step=1`.
- `DeferredText(canonical, width, out committed)` — text field whose edits stay buffered (and visible) while focused; commits only on focus loss or Enter. Used by every hex color input.

Control IDs are `"ctrl" + N` where `N` is a per-frame counter. As long as the UI tree doesn't reorder mid-edit, IDs stay stable across frames and undo/buffer state survives. Slider step is applied both to slider drags and text commits.

### Auto-rebuild of the key viewer

`_needsKvRebuild` is a static flag set anywhere a _structural_ edit in the preset edit page happens — key width, border radius, border width, gap, row keys text, row height, special-key insert buttons, show-rain toggle, rain-color custom/reset, add/remove row. After `DrawKvPreset` returns, the Draw loop fires `onKeyViewerRebuild` once if the flag is set, then clears it. Non-structural edits (X/Y/Scale, colors, rain animation params) route through `onChanged → ApplySettings` only, so dragging RGB sliders doesn't trigger a full layout rebuild.

Border radius and border width _don't actually_ require a structural rebuild — `RoundedRectGraphic` would re-tessellate on property assignment — but they're routed through the rebuild path for parity with the existing key-width pattern. If perf becomes a concern this could be downgraded to a per-cell push.

### SettingsGui section order

Overlay (Scale → Progress → Accuracy / X-Accuracy → BPM / Tile BPM → Attempts → Timing Scale → Judgements) → Combo Display → Hide UI → Tweaks → Key Limiter → Chatter Blocker → Key Viewer (header has top-level "Enabled" toggle binding `ShowKeyViewer`; then Hand list + Foot list, each with their own Enabled toggle and Edit/Delete/Add) → Optimizations → Font → Misc.

The **Misc** section displays read-only stats — currently just "RAM savings (last scene load): ±X.XX MB", populated from `MainClass.LastUnloadSavingsBytes` (set asynchronously when `Resources.UnloadUnusedAssets()` completes in `OnSceneUnloaded`, measured via `Profiler.GetTotalAllocatedMemoryLong()`). Shows `----MB` until the first unload completes.

### Preset edit view

`_editingPreset >= 0` short-circuits the normal `Draw` and renders only the preset editor for `(_editingIsFoot ? KvFootPresets : KvHandPresets)[_editingPreset]`. Sections inside, in order: Name, Reset Counters button, Main Settings (Key Width, Gap, X, Y, Scale, Persist Counts), Edit Rows (per-row collapsible block: cell grid + Listen toggle, Height, Show Rain, Rain Color editor, `× Delete Row` button at the bottom), Key Rain (Track Length, Fade Start, Speed, Width Step, Shadow Size, Shadow Color), **Ghost Keys** (hand only — toggle + per-slot button grid + Ghost Rain Color editor), Background (Released / Pressed colors), **Border** (Radius slider, Width slider, Released / Pressed border color editors), Label Text (`Visible` toggle, colors, font size), Count Text (same shape) color editors. No Apply button — rebuild is automatic via `_needsKvRebuild`.

**Cell grid widget** (`DrawCellGrid`): each cell renders as a clickable button (`▼` prefix when expanded). Selecting a cell opens an inline panel with reorder (`◀ ▶`), Delete, **Change Key** (rebinds this cell to a different key via the shared `ListenForKey()` capture; shows "Listening…" while active; hidden for KPS / Total tokens), Label override (text), and a Width slider that mirrors the symmetric cell. A `● Listen` / `■ Stop` toggle button leading the row puts the row into key-capture mode — pressing a not-yet-registered key adds it as a new cell, pressing an already-registered key removes its cell. `+ KPS` / `+ Total` insert the corresponding special tokens. Captured by `ListenForKey()` which combines `Event.KeyDown` with an explicit `Input.GetKeyDown` poll for modifier keys (macOS doesn't surface modifier-only presses as IMGUI key events).

**Per-row collapse**: `_kvRowOpen[idx]` (parallel to `_kvRowRainOpen[idx]`) gates everything below the row header. Collapsed rows show only the `► Row N` toggle. New rows default to open.

The Key Limiter section reuses the same grid + Listen widget against `Settings.KeyLimiterCustomKeys` (serialized as a space-joined token string; no per-cell label/width).

### Hide UI conditional rendering

All Hide UI sub-options (everything except Hide All) are only rendered when `HideAllUI` is `false`. When `HideAllUI` is on there is no point toggling them individually.

---

## AttemptsStore (`AttemptsStore.cs`)

Static class that persists per-level attempt counts across sessions.

**File:** `<ModPath>/BismuthAttempts.txt` — one `key=value` line per level. Sits alongside the other Bismuth-owned files in the mod folder.

**Key format:** Computed by `GetLevelKey()` in `Overlay.cs`:

- **Official levels** — `scrController.instance.levelName` (= `GCS.internalLevelName`, e.g. `"1-1"`)
- **Custom levels** — `scnGame.instance.levelPath` (the `.adofai` file path, set by `LoadAndPlayLevel` before `scnGame.Play` fires)

`scrController.levelName` is NOT used for custom levels — it falls back to the Unity scene name `"scnGame"` when `GCS.internalLevelName` is null, making all custom levels share one key. `GCS.customLevelPaths[0]` is null at the time `scnGame.Play` fires.

### In-game retry flow (custom levels)

Discovered via IL inspection of `scrController.ResetCustomLevel`:

```txt
scrController.ResetCustomLevel(isRestart)  [coroutine]
  → scrUIController.WipeToBlack()          → our patch: OnLevelEnd() → inLevel = false
  → (yield until wipe completes)
  → scnGame.ResetScene()                   — resets scene in-place, no Unity scene unload
  → scnGame.Play(checkpointNum, isRestart=true)  → our patch: OnLevelStart(isRestart=true)
```

`scrMistakesManager.Reset` fires only in `scrController.Awake` and `scnEditor.SwitchToEditMode` — **not** during in-game retries when the scene doesn't reload. So `OnAttempt()` cannot be used to detect retries; the `isRestart` parameter on `scnGame.Play` is the reliable signal.

| Method | Returns | Purpose |
| ------ | ------- | ------- |
| `Get(key)` | `int` | Returns stored count for key, or 0 if not found |
| `Set(key, value)` | `void` | Stores count and immediately writes the file |
| `GetFull(key)` / `SetFull(key, value)` | `int` / `void` | Full-attempt counterpart — same file, key prefixed `F::` (level names/paths never start with that) |
| `ClearAll()` | `void` | Empties all stored counts (regular + full) and overwrites the file |

Loads lazily on first call. `Get`/`Set` are no-ops when `key` is null (handles the between-scenes window).

---

## BismuthLog (`BismuthLog.cs`)

Static session logger writing to `<ModPath>/BismuthLog.txt`.

Lives in the mod folder (`MainClass.ModPath`), so all Bismuth-owned persistent data sits in one directory. On macOS that is typically `…/Steam/steamapps/common/A Dance of Fire and Ice/Mods/Bismuth/BismuthLog.txt`.

| Method | Purpose |
| ------ | ------- |
| `Init()` | Called by `MainClass.StartMod` — clears the file and writes a timestamped session header |
| `Log(message)` | Appends `[HH:mm:ss] message\n`; no-op if `Init` failed; swallows IO exceptions |

Use `BismuthLog.Log(...)` for any Bismuth-specific diagnostic output. The UMM log (`modEntry.Logger`) is still used by `FontLoader` for font load results.
