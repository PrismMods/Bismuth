# Key Viewer

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



## DM Note presets (`Util/DmNotePreset.cs`)

Two-way mapping between a `KeyViewerPreset` and a [DM Note](https://github.com/DmNote-App/DmNote) `preset.json`. Files live in `<mod>/DmNote/` (same drop-in-folder flow as Profiles); the Key Viewer root's **DM Note presets** row lists them and imports one as a new hand or foot preset (made active), and every preset editor has **Export to DM Note**.

| Direction | What happens |
| --------- | ------------ |
| Import (`FromJson`) | Picks the tab (`selectedKeyType`, else the first with both `keys` and `keyPositions`), drops hidden entries, resolves each DM Note key name to a `KeyCode` (`FromDmName`), then clusters items by vertical center (within half a key height) into rows, left to right. `KeyWidth` = median key width, `Gap` = median horizontal gap, row `Height` = median item height + gap, `WidthMul` from each item's width. Preset colors/border/sizes come from the first key's fields (DM Note defaults where absent); each row's rain color from its first key's `noteColor` + `noteOpacity`. `statPositions` `kps`/`total` become KPS/Total cells (`kpsAvg`/`kpsMax` skipped). A `ghostKey` on a top-row key fills that ghost slot |
| Export (`ToJson`) | Replays `BuildPresetPanel`'s slot math (top row: visible width = `topN*KeyWidth*mul/sumMul`; lower rows: slot = `panelW*mul/sumMul` minus the gap) into `dx/dy/width/height` at a 30px margin, one entry per cell. Lower-row rain remapped into top-row columns is expressed as `noteOffsetX`. Colors serialize as `rgba(r, g, b, a)`, rain as `#RRGGBB` + `noteOpacity`. Stats go to `statPositions` with `statType`. Always writes tab `4key` (DM Note doesn't enforce the count) |

Key names are DM Note's `globalKey` strings (`A`, `LEFT SHIFT`, `NUMPAD 5`, `SQUARE BRACKET OPEN`); a few are bare Windows VK codes (`21` Hangul/RAlt, `25` Hanja/RCtrl, `91`/`92` Cmd, `19` Pause). `FromDmName` also accepts `KeyA`/`Digit1` codes, Unity enum names and the common aliases other exporters use.

Lossy by design: DM Note per-key styling collapses to the preset's single style, and its free placement becomes rows. `DmNotePreset.SelfCheck()` (debug mode, at init) round-trips the active hand preset and logs a mismatch in row/token shape.
