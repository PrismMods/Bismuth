# Bismuth — Developer Reference

A HarmonyX / UnityModManager overlay mod for **A Dance of Fire and Ice (ADOFAI)**.  
Build: `xbuild Bismuth.sln` (Mono, .NET 4.8). A handful of expected warnings (toolset version, obsolete Unity APIs).

The game is Unity 6 and ships TextMeshPro. **Everything Bismuth draws now renders via TMP** — the HUD (Overlay + KeyViewer), the settings panel, *and* the game's own text. The game keeps its legacy `UnityEngine.UI.Text` / 3D `TextMesh` components (its scripts hold typed references), but Bismuth hides each original (`canvasRenderer` alpha 0) and draws a synced TMP child over it (`GameTextShadow` / `GameTextMeshShadow`), so the legacy `Font` is gone from the render path entirely. Fonts are `TMP_FontAsset`s built at runtime from the bundle and from user-dropped `.ttf`/`.otf` files. New `.cs` files must be added to `Bismuth.csproj`'s explicit `<Compile>` list.

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
│   └── Settings.cs           All serialized mod settings + gradient/preset defaults
├── Patches/
│   ├── Patches.cs            HarmonyX prefix/postfix patches for overlay/judgement/UI hooks
│   └── Optimizations.cs      Performance tweak patches (texture, physics, DOTween, etc.)
├── UI/                       UGUI settings shell (the only settings UI — the old IMGUI panel was deleted June 2026)
│   ├── UICore.cs             Root canvas + panel + titlebar + footer + body layout + hotkey + open/close + UI scale
│   ├── Theme.cs              Color palette + runtime accent system (AccentFill/AccentBorder markers) + panel font + 2×2 white sprite
│   ├── UIBuilder.cs          Static widget factory: Rect/Label/SectionHeader(+WithHelp)/Toggle/Collapsible/Slider/Button/DangerButton/ColorPicker/TextInput/Segmented/CycleSelector/ExpandSection/GradientEditor/AccentSwatches + ClickHandler/HoverHandler
│   ├── DragHandle.cs         Titlebar drag: re-parents pointer events to the panel RectTransform
│   ├── ResizeHandle.cs       8 edge/corner resize handles — BR corner 22px (visible grip), others 12px
│   ├── TabRail.cs            Left-rail tab nav; auto-wraps each page in a ScrollRect/Viewport/Content
│   ├── LocationEditor.cs     Drag-to-position edit mode (own SSO canvas, LocHandle per movable element, axis snapping)
│   ├── GameUiEditor.cs       Same edit mode for the GAME's HUD elements (drag/scroll/right-click-reset, dimmed handles for inactive elements)
│   ├── UpdatePopup.cs        "Update available" popup + DuplicateInstallPopup (Mods/ vs UMMMods/ resolution)
│   ├── LogViewer.cs          In-game BismuthLog viewer (Misc → View log): scroll, Refresh, Open in Finder, [dbg] filter
│   └── Pages/
│       ├── KeyTokens.cs      Shared TokenFromKeyCode / PrettyTokenLabel helpers
│       ├── PageOverlay.cs    Overlay stats (+separator/weight rows), combo display, attempts, song title, FPS
│       ├── PageKeyViewer.cs  Preset lists + full preset editor (row grid, drag-reorder, rebind, submenus)
│       ├── PageInput.cs      Menu input-block toggle + Key Limiter (chip editor + listen) + Chatter Blocker + KeyListener component
│       ├── PageHideUi.cs     Hide UI toggles with conditional sub-container
│       ├── PageUI.cs         Panel scale slider, panel/overlay font pickers (family + weight, each option previewed in its own font), accent color
│       ├── PageGameUi.cs     Game-text repaint: game font, title weight, size/spacing/stats sliders; per-element rows with visibility radio + weight
│       ├── PageTweaks.cs     "Tweaks" tab — editor autoplay-pause enable toggle + key rebind
│       └── PageMisc.cs       Read-only stats (RAM savings), Force reload, View log, Optimizations master-toggle dropdown, Debug (GameProbe dumps + sweep trace)
└── Util/
    ├── AttemptsStore.cs      Persists per-level attempt counts to `BismuthAttempts.txt`
    ├── BismuthLog.cs         File-based session logger → `BismuthLog.txt`
    ├── FontLoader.cs         Font scan (bundle + loose .ttf/.otf in Fonts/) → FontEntry list; lazy TMP_FontAsset (dynamic SDF, weight table); family/weight linking; name matching
    ├── GameFontApplier.cs    Repaints ALL game text with the chosen font: legacy Text + 3D TextMesh via shadows, game TMP swapped in place; bold/scale/fit decisions; per-element weights; sweep scoping
    ├── GameTextShadow.cs     Per-legacy-Text shadow renderer: hides the original, mirrors it into a TMP child (FitMode, strip-size, bold-label lines, collapse-newlines, no-wrap, explicit drop shadow)
    ├── GameTextMeshShadow.cs Same idea for 3D `TextMesh` (world-space) — TMP child matched to the mesh's world size
    ├── TmpShadow.cs          TMP drop-shadow component — drives the SDF underlay (legacy Shadow doesn't affect TMP)
    ├── GameUiLayout.cs       Game-HUD layout overrides: wrapper transforms over scrUIController elements + per-element visibility + error meter UpdateLayout override
    ├── GameProbe.cs          Runtime inspector (Misc → Debug): dump texts/images/assets/components filtered by substring
    ├── Tweaks.cs             "Tweaks" tab support — AutoPauseKeyCode() injected into scnEditor.Update by a transpiler
    └── UpdateChecker.cs      Self-updater: Repository.json check, zip download/install, duplicate-install resolution
```

Pages register via `UICore.Tabs.AddTab(name, BuildPage)` in `MainClass.TryEagerInit`. Pages are built **once** at registration and only shown/hidden on tab switch — anything whose option set depends on runtime state (e.g. the font-weight rows) needs an explicit refresh hook (`PageOverlay.RefreshFontWeightRows`).

---

## UI shell (`UI/`)

The settings panel is a self-owned UGUI canvas (the old IMGUI panel was removed June 2026; UMM's OnGUI now just shows an "Open Settings Panel" button).

### Architecture

Modeled on KorenResourcePack v2's UI structure, stripped to the minimum:

- **No dependencies added** — the panel renders with `TextMeshProUGUI` (`UIBuilder.Tmp`/`Label`), reuses `RoundedRectGraphic`, no tween library. Panel font is a `TMP_FontAsset` from the font scan via `Theme.ApplyFont` (default Paperlogy-4Regular; missing/stale names fall back to `fonts[0]`); a 2×2 white texture generated once at runtime is the only sprite.
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
| `FontName` | `"Paperlogy-4Regular"` | Font used for all overlay text (also the level name's fallback source; Paperlogy carries CJK. `FontLoader.Find` matches ignoring spaces/hyphens/case) |
| `StatSeparator` | `" \| "` | Text between a stat row's label and value; empty falls back to `" \| "`. Trailing spaces become HLG spacing (TMP never measures trailing whitespace — see Fonts section) |
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
| `LevelNameUseOverlayFont` | `true` | Repaint the song title with the Bismuth font via an owned `GameTextShadow` (TMP) + explicit drop shadow; off → the vanilla original shows. `_levelNameFont` resolves from the **game** font family (see Song title in Fonts) |
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

---

## Fonts & text rendering

Everything Bismuth draws is **TextMeshPro** (`TextMeshProUGUI` / world-space `TextMeshPro`) — HUD, settings panel, and the game's own text (via the shadow renderers below). The legacy `Font` is gone from the render path; the only fonts used are runtime `TMP_FontAsset`s. The game ships `Unity.TextMeshPro.dll` + TextCore modules (referenced in the csproj).

### FontLoader (`Util/FontLoader.cs`)

- `ScanFonts(modPath)` gathers fonts from two sources into `FontEntry` objects: the bundled `Resources/bismuth-fonts` AssetBundle, and **loose `.ttf`/`.otf` files** the user drops in `<mod>/Fonts/` (or `Resources/`). Then `LinkFamilies` groups by family and wires each entry's bold sibling (exact `Bold`, else the heaviest ≥ Bold).
- `FontEntry.TmpFont` lazily builds a dynamic-SDF `TMP_FontAsset`, naming it `"<name> (TMP)"` and wiring the family's real Bold into `fontWeightTable[7]` so `<b>`/`FontStyles.Bold` render true bold glyphs. Bundled entries use `CreateFontAsset(Font)`; loose files use Unity 6's `CreateFontAsset(filePath, …)`, which stores the path and reloads the face on demand (`AtlasPopulationMode.Dynamic`) — so Korean/CJK glyphs rasterize from the file as needed and persist across scenes.
- `SplitWeight` strips a leading digit from the weight token (`4Regular` → `Regular`, `8ExtraBold` → `ExtraBold`) so numbered-weight families (`Paperlogy-4Regular`) sort correctly; `WeightRank` orders against the canonical Thin → … → Black scale. Shared by the font pickers and weight-table wiring.
- `Find(fonts, name)` matches names ignoring spaces/hyphens/case. Missing names fall back (game font → overlay font `target`; overlay/UI font → `fonts[0]`) in `MainClass.ApplySelectedFont` / `UICore.ResolveSavedFont`.
- `WeightHeaviest` (`"Heaviest"`) is a sentinel: `MainClass.FindFamilyWeight` resolves it to the family's max-rank weight at apply time.
- `DestroyTmpAssets` runs in `StopMod` (and on Force reload) so runtime SDF atlases/materials don't pile up across hot reloads.

**Defaults (Settings.cs):** overlay font `FontName` and panel/game fonts (`UiFontName`/`GameFontName`) default to **`Paperlogy-4Regular`** — Paperlogy carries CJK glyphs, so Japanese/Korean level titles render without falling back. (`FontName` was 에이투지체 until 1.2.x; 에이투지체 lacks kana, which drew Japanese titles undersized via the global fallback.)

### Font pickers (font preview)

`PageUI.BuildFontSelector` builds a family dropdown + a weight sub-dropdown. `UIBuilder.Dropdown` takes an optional `IList<TMP_FontAsset> optionFonts` so **each option renders in its own typeface** (family option = its Regular/lightest weight; weight option = that weight). The "Game default" sentinel option keeps the panel font.

### Per-part weight overrides

`MainClass.ApplySelectedFont` resolves the selected entry, then `Settings.{StatLabel,StatValue,ComboLabel,ComboValue}Weight` against the same family, and calls `Overlay.SetFont(base, label, value, comboLabel, comboValue)` (nulls = base). Judgements/FPS always use the base font; the KeyViewer gets the base font only. The UI rows live in PageOverlay (`AddWeightRow` — self-rebuilding hosts registered on `RefreshFontWeightRows`, invoked by PageUI after an overlay font change; rows only exist when the family has >1 weight).

### TmpShadow (`Util/TmpShadow.cs`)

Legacy `Shadow`/`Outline` are mesh modifiers TMP ignores. `TmpShadow` drives the SDF shader's **underlay** on the text's per-instance `fontMaterial`:

- `OffsetPx` keeps legacy `effectDistance` pixel semantics; converted to the shader's padding-relative units via `samplingPointSize / (atlasPadding × fontSize)` and clamped to [-1, 1] (max ≈ fontSize/10 px).
- **`Apply()` must end with `UpdateMeshPadding()` + `SetVerticesDirty()` + `SetMaterialDirty()`** — TMP measures mesh quad padding from the material _before_ a freshly assigned font asset's material has the underlay enabled, which clips the shadow to the glyph bounds.
- Apply is **idempotent** (tracks last-applied enabled/color/offset/font/fontSize and no-ops when unchanged) because the regeneration above is expensive — never remove the guard.
- Re-Apply is required after font or fontSize changes (the material instance is replaced); `Overlay.SetFont` re-applies every shadow via `GetComponentsInChildren<TmpShadow>(true)`.
- The master pass at the end of `ApplySettings` pushes `OverlayShadowEnabled`/`OverlayShadowColor` to all shadows (combo label/count keep their own colors).

### TMP gotchas (hard-won)

- **Trailing whitespace is never measured** — plain spaces _and_ U+00A0 NBSPs are excluded from preferred width (an NBSP fix was tried and failed in-game). Stat row separators put only the visible part in the label and realize the trailing-space run as `HorizontalLayoutGroup.spacing`, sized via `SpaceWidth()` = `GetPreferredValues("| |") − ("||")`.
- **`MidlineLeft` ≠ vertical centering** — Midline is the geometric center of the rendered glyph bounds, so strings with/without descenders sit at different heights. Row texts use `TextAlignmentOptions.Left` (line-metric Middle).
- Rich text (`<color>`, `<b>`) is on by default; coop acc/xacc rows use inline color tags.
- `enableWordWrapping` is obsolete in this TMP — use `textWrappingMode = TextWrappingModes.NoWrap`.

### Game text repaint (`Util/GameFontApplier.cs`)

`GameTextUseOverlayFont` sweeps ALL game text onto the **game font** (`GameFontName`, decoupled from the overlay font) — but the render path differs by component type, because the legacy `Font` no longer draws anything:

- **Legacy `Text`** → the original is left alive and layout-contributing but hidden (`canvasRenderer` alpha 0); a `GameTextShadow` child `TextMeshProUGUI` mirrors its text/color/alignment/size live in `LateUpdate` with the Bismuth font. Bold/scale/fit/no-wrap decisions are pushed via `Configure`.
- **3D `TextMesh`** → same, via `GameTextMeshShadow` (world-space TMP sized to the mesh).
- **Game `TMP_Text`** → swapped in place (`t.font = target`), with the original asset appended to `fallbackFontAssetTable` so glyphs the Bismuth font lacks still render. Its original font/size/lineSpacing/style/best-fit bounds are cached for toggle-off restore.

#### Sizing

Every swap scales by the line-height/em ratio of original vs ours (clamped; `TextMesh` gets ×0.8 extra), times a **baked base ×0.6** and the `GameTextScale` slider, and widens leading by a **baked base ×1.5** and the `GameTextLineSpacing` slider. The bases were hand-tuned for Pretendard (it fills more of the em box, so equal-metric leading reads cramped); the sliders center at 1.0. Legacy `lineSpacing` is a multiplier, TMP's is additive in font units (~em%). Separately: `GameStatsScale` sizes `*StatsText*` panels, `GameJudgementScale` sizes the hit-judgement popups (scrHitTextMesh), and `GameTextTitleWeight` (default heaviest) sets the bold weight — all independent of the global scale.

#### Enabling

No on/off toggle. The "Game font" selector has a prepended "Game default" entry (BuildFontSelector's `defaultOption`/`onDefault`): picking it sets `GameTextUseOverlayFont=false` and hides the options block, picking any family enables the swap. Defaults ON (Paperlogy-4Regular). The sliders call `RequestResize()`, a debounced (+15 frame) `Restore()`+`Apply()`, needed because `Apply` skips text already on our font.

#### Idempotency (three rules, each fixed a compounding bug)

- **Skip guard:** the Apply* methods compute the bold decision and target font/style _before_ the early-out, and skip only when the component matches BOTH the target font and style. Skipping on "font is ours" alone froze bold-ness, because legacy bold is faux via `fontStyle` (the font stays `_font`), so keycaps stuck bold and world names stuck regular.
- **Re-stamp adoption:** when a sweep finds a cached component whose font isn't ours (`IsOurLegacyFont`/`IsOurTmpFont`, covering regular, bold, and every element-weight font), the game re-stamped it (Start()-time localization runs after the scene-entry sweep), so the cached restore font and TextMesh material are updated to the re-stamped one. Otherwise "Game default" restored pre-localization Latin-only fonts and Korean rendered as tofu.
- **Cached-original sizing:** all sizes derive from the cached ORIGINAL state, never current values. The game re-assigns localized fonts on rewind, defeating the skip, and computing from current values compounded the scale once per attempt.

#### Bold decision (`ShouldBold(c, text, styleBold, origFontName)`)

- `ForceRegular` → never. Overrides every bold signal for `scrLetterPress` keycaps and `scrBestMultiplierText` speed-trial badges ("1.5배") globally (their source font is bold and rendered Black).
- `IsTitle` → always: `scrHUDText.isTitle`, or `scrCreditsText` for the "by 7th Beat Games" block.
- else by **active** scene: `scnLevelSelect` → `LevelSelectBold` (whole scene, authoritative), `scnCLS` → `ClsBold` (chrome only), else the per-component heuristic (cached style bold, or original font name looks bold: bold/black/heavy/-bd).

Gate on the _active_ scene (`InLevelSelect()`), not the component's own scene: the title/menu world content (floors, world names, keycaps, credits) is parented under DontDestroyOnLoad. In level select the verdict is authoritative, so the caller ignores `NameLooksBold`/style there (they mis-bold single-letter keycaps).

`LevelSelectBold` bolds everything except the `NewsSign` hierarchy, the press-to-start hint cluster (`Hit Space` ancestor, holding the cycling `numberKeys`/`GameComplete*` tips), `*StatsText*` panels, and single-glyph content. The portal labels (계속/보정/…) and the visible subtitle are _also_ `scrTextChanger`, so the hints can't be excluded by component type. The `Hit Space` ancestor check isolates them (verified via desktopText keys: hints `levelSelect.numberKeys`/`GameCompleteFullPure`, labels `levelSelect.continue`/`calibration`/…, subtitle `levelSelect.subtitle`).

`ClsBold` bolds only the chrome: the screen `title`, `WorldNameCanvas` portal labels, `scnCLS.instance.portalName`, the difficulty name (`Difficulty Container/.../txtValue`, "엄격"), and the `Loading` text. Body copy, level descriptions, help text, and `txtDescription` stay regular.

#### Auto-fit text ignores `fontSize`

- Auto-sizing TMP (scnCLS level descriptions, `enableAutoSizing`) fits text between `fontSizeMin/Max`, so short text balloons to Max. `ApplyTmp` scales the bounds (cached/restored).
- Legacy `Text` with `resizeTextForBestFit` ignores `fontSize`; `ApplyText` scales `resizeTextMaxSize` (cached/restored). This is also how the `Continue/LastLevel` ("8-X Jungle City") ×0.6 special case takes effect.

#### How bold renders (differs by system)

Since every path is now TMP, bold is a **real Bold `TMP_FontAsset`**, not faux dilation — the shadow/swap gets the family's bold weight (or the regular weight with `fontWeightTable[7]` pointing at the Bold asset, so a `<b>` line renders true bold). The faux-`FontStyles.Bold` flag is only used as a fallback when no bold asset exists for the family. The heaviest weight is resolved in `ApplySelectedFont` (`WeightHeaviest` sentinel). Individual label lines inside a mixed component (guest-track "label\nname") are bolded with `<b>` tags in the shadow's mirrored text.

#### Per-element weights (Game UI tab → Element weights)

`Settings.GameUiTextWeights` (`Key`/`Weight`, `""`=Auto) overrides the heuristic for the seven text-bearing GameUiLayout targets, the synthetic `judgement` key (pooled scrHitTextMesh popups, world-space `TMP_Text`; lookup uses `GetComponentInParent<scrHitTextMesh>(true)` because the pooled popups are inactive at sweep/Show time, and the no-arg overload would skip them), and `levelname` (consumed by MainClass, not here). `GameUiLayout.OwnerKey(c)` maps a component to its target via frame-cached `IsChildOf` checks; MainClass hands GameFontApplier a weight→FontEntry table (`SetElementWeights`); the skip-guards compare against the per-element font so weight changes re-apply. Explicit weights render with faux bold stripped.

#### Sweep triggers and scoping (fixed lag spikes at start/death/retry)

Full `FindObjectsByType`×3 scans (expensive on large maps) are reserved for MENU scene loads, first level entry, and toggle-resize, frame-deduped via `_lastSweepFrame` (`SetFonts` bypasses, since font identity changed). A retry reloads the **gameplay scene (`scnGame`)** on every attempt, so `OnActiveSceneChanged` scopes it: `scnGame` → `ReapplyHud()` (+ delayed scoped re-sweep); menu scenes → full `Reapply()` + `RequestFullSweepSoon()`. `ReapplyHud()` is a `GetComponentsInChildren` sweep of just `scrUIController.canvas` **plus the world-space autoplay label** (`GameUiLayout.AutoplayTextObject()`), where all mid-scene text spawns and re-stamps (death %, results, congrats, rewind re-localization). First entry still full-sweeps via `OnLevelStart`, so nothing is missed. Exceptions and additions:

- **scnLevelSelect:** state-change ticks sweep _fully_ (`StateSweep()`). Portal labels and world names activate late on approach and live outside any canvas.
- **Scene entry** needs delayed FULL sweeps (`RequestFullSweepSoon`, +2/+30, from `OnActiveSceneChanged` and `SetFonts`): localization assigns fonts in `Start()`, one frame after `sceneLoaded`, stomping the immediate sweep (cold launch left the title screen vanilla until the toggle was cycled).
- **CLS navigation:** re-sweep after each `scnCLS.DisplayLevel` (it re-stamps the default font on the portal info and fires no other sweep).
- **Pause/settings:** re-sweep on `PauseMenu.Show` / `ShowSettingsMenu` (shown over gameplay with no scene change).
- *Known gap:* world-space win-screen extras outside the canvas (event endscreen lanterns) aren't caught by state-change sweeps.

#### Other hooks

- **Pause-menu bolding:** `ShouldBold` bolds everything under a `PauseMenu` ancestor except the `SettingsMenu` subtree (keeps its designed weight). The `SettingsMenu` check runs first, so it holds even when Settings is opened from the main menu where `LevelSelectBold` would otherwise catch it.
- **Localized re-stamp:** `RDString.SetLocalizedFont` (Text/TMP/TextMesh) is postfixed → `OnLocalizedFontSet` re-applies our font immediately. The language-selector previews stamp each language's own font over ours; a script the chosen font lacks falls back to the game's original asset (kept as a TMP fallback) or tofus.
- **News sign:** `NewsSign.ShowNews` is postfixed with `ApplyTo` (it fills its text only when the async fetch lands, after the scene sweep).
- **Editor exemption:** `Text`/`TMP_Text` in the `scnEditor` scene get metric normalization only (no `GameTextScale`, no leading change), because the hand-fitted form panels break under resizing. `TextMesh` is resized everywhere.
- **Diagnostics (opt-in):** set `GameFontApplier.DiagEnabled = true` to dump (capped 16 lines/sweep, `GameFontDiag` prefix) each matching text's path/scene/textChanger/title/lsBold/applied font. `DiagFilter` is a substring list (empty = all <40-char texts). Sweeps also log "GameFont: sweep bold-swapped N" (debug) when the count changes.
- **`StopMod`** calls `RestoreAll()`; without it a hot-reloaded assembly (fresh Font instances, empty caches) re-caches scaled text as "original" and compounds across deploys.

### Song title (`txtLevelName`)

Legacy uGUI `Text` owned by the game. When `LevelNameUseOverlayFont` is on, `ApplyLevelNameTransform` attaches an **owned** `GameTextShadow` (`_levelNameFont` is a `TMP_FontAsset`) configured `collapseNewlines` + `noWrap` (the game appends the speed-trial multiplier `\n(1.1배)` to this same Text) and gives it an explicit Bismuth drop shadow (`SetShadow`, offset divided by `LevelNameScale` since `localScale` shrinks the subtree). Off → `Detach()` restores the vanilla original. `_levelNameFont` resolves from the **game** font family (`MainClass.ApplySelectedFont` → `SetLevelNameFont`), defaulting to the title weight; the `levelname` element weight overrides it. **CJK caveat:** the font must contain the title's glyphs — a Latin/Korean-only font (e.g. 에이투지체) lacks kana, so Japanese falls back to a mismatched-scale global font and renders undersized; hence the Paperlogy (CJK) default. Note the game font can resolve a frame *after* the level name is first set, leaving it on the earlier fallback font until a later re-apply.

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
    │   └── ComboLabel  (TextMeshProUGUI — the "Perfect Combo" line)
    └── ComboValue      (TextMeshProUGUI — the integer counter)

FpsCanvas (separate ScreenSpaceOverlay canvas, sortingOrder 1000)
└── FpsDisplay (TextMeshProUGUI, bottom-right)
```

All HUD text is `TextMeshProUGUI`; every text carries a `TmpShadow` component (see Fonts section).

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
| `ApplyLevelNameTransform()` | `ShowOrHideElements()` + `LevelNameTextRestorePatch` | Applies `LevelNameScale` and additive `LevelNameY` to `txtLevelName.rectTransform`; swaps font + Bismuth shadow when `LevelNameUseOverlayFont` (see Fonts section) |

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
| `SetFont(font)` | `MainClass.ApplySelectedFont` | Pushes the new `TMP_FontAsset` to every live cell's Name/Count text and stat cell text |
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

The MonoBehaviour `Update` runs every frame regardless of canvas active state (Unity calls Update on the GameObject; the canvas being inactive only suppresses rendering, not script execution). The key polling lives in `PollKeys`, wrapped in `KeyLimiter.RawReadExempt = true` (try/finally) so the viewer keeps observing keys while the menu's input block is active. The flow:

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
| `StateBehaviour.ChangeState(States.None)` | Postfix | `OnLevelEnd()`; every state change also requests delayed `GameFontApplier` sweeps + `GameUiLayout` re-applies (death/results text spawns late) |
| `scnEditor.ResetScene` | Postfix | `OnLevelEnd()` |
| `scnEditor.SwitchToEditMode` | Postfix | `ShowOrHideElements()` |
| `scrController.LevelNameTextRestore` | Postfix | `ApplyLevelNameTransform()` — re-applies our scale/offset after the game restores canonical position |
| `scrHitErrorMeter.UpdateLayout` | Postfix | `GameUiLayout.ApplyErrorMeter` — re-applies the meter position/scale override after the game's own layout pass |
| `scrShowIfDebug.Update` | Pre+Post | Temporarily sets `RDC.auto = false` (if `HideAutoplayText \|\| HideAllUI`) to suppress the autoplay text |
| `scrHitTextMesh.Show` | Prefix | Moves judgement popup off-screen (`HideAllUI`) or suppresses it (`HidePerfectJudgements`) |
| `scrHitText.Show(Vector3, float)` | Prefix | Moves legacy judgement text off-screen (`HideAllUI`) |
| `scrMissIndicator.Awake` | Postfix | Moves miss indicator off-screen (`HideAllUI`) |
| `scrPlanet.MoveToNextFloor` | Postfix | Hides error meter (`HideAllUI` or `HideHitmeter`) |
| `scrController.paused` (setter) | Postfix | Hides error meter (`HideAllUI` or `HideHitmeter`) |
| `OttoButtonController.Update` | Postfix | Hides Otto debug button (`HideAllUI`) |
| `RDInput.GetMain(ButtonState)` | Postfix | Key Limiter — clamps press count to allowed-key count when state=WentDown; zeroes it entirely while the Bismuth menu is open |
| `RDInput.WentDown(KeyCode)` / `RDInput.IsDown(KeyCode)` | Postfix | Menu input block — raw shortcut-key reads return false while the menu is open |
| `RDInput.GetState(InputAction, ButtonState)` | Postfix | Menu input block — Rewired action reads return false while the menu is open |
| `UnityEngine.Input.GetKeyDown(KeyCode)` | Postfix | Menu input block — direct polls (menu number-nav) return false while open, except KeyCode.B and `RawReadExempt` reads. Applied separately via `TryPatchRawInput` |
| `scrMistakesManager.AddHit(HitMargin)` | Prefix | Key Limiter — suppresses hit if no allowed key currently held, or unconditionally while the menu is open |
| `scnEditor.Update` | **Transpiler** | Tweaks tab — swaps the hardcoded `Ldc_I4 32` (KeyCode.Space) feeding the autoplay-pause `Input.GetKeyDown` for a `call Tweaks.AutoPauseKeyCode()`, so the pause key is rebindable/disableable. Fails safe: no pattern match → vanilla Space |
| `scnEditor.LateUpdate` | Postfix | `EditorLateUpdateShowHudPatch` — re-enables the HUD canvas while `GameUiEditor.IsActive` (the editor force-disables it outside play mode) |

### Optimizations (`Optimizations.cs`)

Independent file with Harmony patches gated on `Opt*` settings: `scrConductor.Update` (spectrum throttle), `TextureManager.LoadTexture` / `CustomTexture.GetTexture` / `CustomSprite.GetSprite` (non-readable / DXT), `scrPlanet.Update` / `scrFloor.Update` (physics non-alloc / DOTween fix).

---

## Settings UI pages (`UI/Pages/`)

All settings interaction is UGUI (the IMGUI SettingsGui/SettingsInput files were deleted June 2026 after the port). Conventions:

- Pages are built once via `UICore.Tabs.AddTab(name, Build)` in `MainClass.TryEagerInit`; tab switching only toggles `SetActive`. Anything whose option set depends on runtime state needs an explicit refresh hook — e.g. `PageOverlay.RefreshFontWeightRows` (multicast `Action`, reset at the top of `PageOverlay.Build`, registered per row by `AddWeightRow`, invoked by PageUI's overlay-font selector after a family change).
- `UICore.OnSettingsChanged` fans out to `overlay.ApplySettings` + `keyViewer.ApplySettings` + `KeyLimiter.Apply` (wired in `MainClass.TryEagerInit`). Structural KeyViewer edits additionally fire `UICore.OnKeyViewerRebuild` → `keyViewer.Rebuild`.
- `KeyListener` (PageInput) is the shared key-capture MonoBehaviour for rebind / ghost keys / limiter chips: polls a watched-key list once per frame while `Active`, fires `OnKey(KeyCode)` once, wrapped in `KeyLimiter.RawReadExempt` so capture works while the menu's input block is engaged (capture only ever happens with the menu open).
- The **Misc** page displays read-only stats — "RAM savings (last scene load)", populated from `MainClass.LastUnloadSavingsBytes` (measured around `Resources.UnloadUnusedAssets()` in `OnSceneUnloaded`).
- Hide UI sub-options render only while `HideAllUI` is off.

---

## Game UI layout overrides (`Util/GameUiLayout.cs` + `UI/GameUiEditor.cs`)

Moves/scales the **game's own** HUD elements (Game UI tab → "Edit game UI on screen"; the old Locations tab was dissolved June 2026 — overlay-location entries moved to the UI tab, game-UI entries to the Game UI tab). Two mechanisms:

- **Wrapper transforms.** Each `scrUIController` element gets a full-stretch `BismuthGameUiWrap_<key>` RectTransform inserted above it. Targets (keyed by field name, all public): `txtPercent`, `txtCongrats`, `txtAllStrictClear`, `txtResults`, `txtPressToStart`, `txtCountdown`, `difficultyContainer`, `modifiersContainer`, `pauseButton`, plus the autoplay label (a `scrShowIfDebug` with no static accessor, found by type via a frame-throttled `FindObjectsByType` and cached).
  - The wrapper's rect equals the parent's, so the element's anchors/anchoredPosition keep their exact meaning, and everything the game does to the element (difficulty show/minimize `DOAnchorPosX` tweens, text rewrites) happens _inside_ the wrapper and never fights our offset/scale. Scale pivots on the element's measured center (recomputed each apply), so scaling doesn't slide elements toward screen center.
  - Overrides live in `Settings.GameUiOverrides` (`List<GameUiOverride>`: `Key`/`OffX`/`OffY`/`Scale` in parent-canvas units, plus `Hidden` and `Align`). **Visibility:** `ApplyVisibility` puts a `CanvasGroup` on the wrapper (alpha 0 / `blocksRaycasts` false when `Hidden`), so the element stays hidden regardless of the game re-activating it, and the alpha also covers the TMP shadow child. PageGameUi's element rows carry a visibility **radio**. **Text alignment** (`Align`, text-bearing elements like the autoplay label): `ApplyAlign` sets the label's pivot.x (Left/Center/Right) once at a settled width and freezes the anchored position, so content changes grow symmetrically about a fixed point instead of drifting; autoplay's effective default is Center.
  - Fresh installs get a curated default layout (`MakeGameUiDefaults`, re-baked June 12 2026: presstostart/congrats/countdown/percent/results/strictclear/autoplay repositioned and scaled) and per-element text weights (`MakeGameUiWeightDefaults`: presstostart Thin, congrats Black, countdown/strictclear Bold, results/autoplay Light, judgement Black, levelname Medium). Both are seeded ONCE in `EnsureDefaults` behind the `GameUiDefaultsSeeded` flag, and `GameTextUseOverlayFont` defaults TRUE (swap on out of the box, Paperlogy-4Regular).
  - Reset: right-click a handle → `ResetToDefault` (Bismuth default when the key has one, else vanilla). Game UI tab buttons "Reset layout to Bismuth defaults" (`ResetAllToDefaults`) and "Reset layout to game defaults" (`ResetAllToGame`, removes every override plus meter restore). An empty list is a legitimate "all vanilla" state, so emptiness alone must not re-seed (unlike the KV presets).
- **Error meter** — `scrHitErrorMeter` already models position as a normalized anchor (`pos`, applied as anchorMin=anchorMax=pivot in `UpdateLayout`) and scale as `meterScale` → `localScale`. A postfix on `UpdateLayout` re-applies `Settings.GameErrorMeter{X,Y,Scale}` (absolute pos + multiplier on the in-game size setting, `GameErrorMeterOverride` gate) — covers `scrController.Awake` and the game's own settings-menu size changes, so no sweeps are needed. The game's per-size pixel offset (`anchoredPosition` 0,−30…0,−94) is zeroed when overriding. Original wrapper state is captured before the first override for restore.

Re-apply triggers (fresh objects per scene): `Overlay.OnActiveSceneChanged`, `Overlay.OnLevelStart`, and delayed ticks (+2/+30 frames, `GameUiLayout.Tick` from `Overlay.Update`) requested by the `StateBehaviour.ChangeState` postfix. Wrappers are unwrapped in `StopMod` (`RestoreAll`) so hot reloads don't stack stale wrappers; `EnsureWrapper` also re-adopts an existing wrapper by name. Parents with a `LayoutGroup` are refused (would fight the stretch anchors).

`GameUiEditor` mirrors `LocationEditor` (own SSO canvas at 31000, mutual exclusion between the two editors) and reuses `LocHandle`, which gained optional `GetScale`/`SetScale` (corner `ScaleGrip`s for uniform scale from the pointer-to-center distance ratio, plus scroll wheel), `ResetTarget` (right-click), `ShowInactive` (dimmed handle at the element's last layout position), and `TightBounds` (size to the union of visible child Graphics rather than the target rect, since the error meter wrapper extends well below its drawn content).

- A `HandleSorter` re-orders the handle sibling block by area (largest first) each frame, so smaller handles render and raycast above the screen-blanketing congrats/results boxes.
- The autoplay `scrShowIfDebug` lookup prefers an **active** instance (several carry a `Text`, and caching an inactive one pinned the handle to a dead object) and debug-logs each search.
- While open it force-shows the game targets: the FULL ancestor chain up to the canvas is activated (own-GO-only left invisible handles, since win/fail containers are inactive), CanvasGroup/text alphas < 0.05 are lifted, and empty `Text`s are filled with Korean sample strings per key. All changes are recorded (four lists) and restored in reverse on Close.
- The `results` sample is special: the real screen is a multi-line colored breakdown, so `ResultsSample` invokes the game's private `DetailedResults.GenerateResults(players[0].marginTracker)` via reflection (localized labels plus authentic `<color>` values, falling back to the static placeholder if anything is null or throws).
- Bismuth's own overlay is NOT shown (`Overlay.EditMode` untouched, user preference). `txtLevelName` is deliberately not a target: Bismuth already owns its transform (`LevelNameScale`/`LevelNameY`).

The autoplay label needs its own per-frame help: `scrShowIfDebug.Update` disables the (private) Text component every frame when autoplay/debug is off — `Behaviour.enabled`, which force-show doesn't cover. The existing `ShowIfDebugUpdatePatch` postfix re-enables it (and fills `RDString.Get("status.autoplay")` if empty) while `GameUiEditor.IsActive`; self-corrects after Close since the game rewrites both every frame.

Level-editor caveat: `scnEditor.LateUpdate` force-writes `uiController.canvas.enabled = playMode` **every frame**, so outside play mode the whole HUD canvas is disabled and force-show alone leaves handles over nothing. `EditorLateUpdateShowHudPatch` (postfix on that LateUpdate) re-enables the canvas while `GameUiEditor.IsActive`; no restore needed — the game's per-frame write takes back over after Close.

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
| `Log(message)` | Appends `[HH:mm:ss] message\n`; no-op if `Init` failed; swallows IO exceptions. Plain file IO — safe from background threads |
| `Debug(message)` | `Log` with a `"[dbg] "` prefix, for high-frequency diagnostics (hook traces, per-attempt dumps). The in-game viewer hides `[dbg]` lines unless its Debug toggle is on |
| `ReadTail(maxChars)` | Tail of the current log for the in-game viewer, capped ~12k chars (uGUI Text 65k-vertex limit) |

Use `BismuthLog.Log(...)` for any Bismuth-specific diagnostic output, `Debug(...)` for anything per-hook/per-attempt. The UMM log (`modEntry.Logger`) is still used by `FontLoader` for font load results.

### LogViewer (`UI/LogViewer.cs`)

Misc → "View log" opens a standalone window (own canvas) showing `ReadTail()`, auto-scrolled to the newest lines. Buttons: Refresh, **Clear** (`BismuthLog.Clear`), Open in File Manager (`OsShell.OpenFolder(ModPath)`), Close. Each line is **numbered** — the number is a clickable TMP `<link>` (`LogLineCopier` → `FindIntersectingLink` → copies that raw line to the clipboard); the line's own `<color>`/`<size>` tags are wrapped in `<noparse>` so they show literally. `[dbg]` lines are hidden unless Debug mode is on.

---

## Self-updater (`Util/UpdateChecker.cs` + `UI/UpdatePopup.cs`)

UMMCompat doesn't run UMM's own updater, so the mod updates itself. `UpdateChecker.Begin(modEntry)` (from `TryEagerInit`) fetches `Repository.json` (the URL comes from Info.json's `Repository` field, kept current by `release.sh`). A newer `Releases[0].Version` shows `UpdatePopup` with three buttons: **Update now (requires restart)**, **Manual update** (opens the releases page derived from `DownloadUrl`), and **Later**.

"Update now" downloads the release zip and extracts the `Bismuth/` payload over every existing install dir, running copy last, dll last within each (UMM watches it for hot reload). Persistent data survives by construction: the zip carries only the payload and nothing is deleted, and `MainClass.PersistNow()` flushes settings/counts first. On success the primary button becomes **Close**.

**Dual-install handling.** Native UMM loads from `<game>/Mods/Bismuth`, MelonLoader+UMMCompat from `<game>/UMMMods/Bismuth`. When both exist (and `Settings.IgnoreDuplicateInstall` is unset), `DuplicateInstallPopup` asks which loader is in use and offers to delete the unused copy. Deleting the _running_ copy first flushes and carries `Settings.xml`/`keycounts.txt`/`BismuthAttempts.txt` to the kept dir. The version check runs after the prompt resolves.

### MelonLoader/UMMCompat landmines (all hit while building this)

1. **`modEntry.Info.Repository` is not populated** from Info.json — the checker has a hardcoded fallback URL.
2. **UnityWebRequest coroutines silently never resume** — no timeout, no error, no completion. Networking runs on the ThreadPool, results drained by `Update()` under a lock.
3. **Child processes inherit `DYLD_INSERT_LIBRARIES`** (MelonLoader's arm64 bootstrap dylib), which dyld refuses to load into arm64e system binaries — curl dies with exit 134. `CurlFetch` strips `DYLD_*`/`LD_PRELOAD` from the child env. Applies to ANY `Process.Start` from the mod.
4. **`JsonUtility.FromJson` silently returned nothing** for Repository.json — the two fields are parsed with regexes instead.

Transport order: curl subprocess (macOS / Win10+ / Linux) → .NET `WebClient` fallback (TLS 1.2 forced via `ServicePointManager`). Every stage logs to BismuthLog (`Update check: …`, `Update worker: …`).

### Popup canvases: sortingOrder is signed 16-bit

`Canvas.sortingOrder` wraps beyond 32767 (35000 became −30536 and rendered _under_ the settings panel). Layering: LocationEditor 31000 < settings panel 32000 < LogViewer 32500 < UpdatePopup 32600 < DuplicateInstallPopup 32601. Never exceed 32767.
