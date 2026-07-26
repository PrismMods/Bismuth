# Fonts & Text Rendering

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
- **Material restore (TMP):** `TmpState.Mat` caches `fontSharedMaterial` and Restore re-assigns it AFTER the font (set_font resets to the font asset's default material). The game styles TMP per-text via `fontMaterial` instances — `RDString.SetLocalizedFont` explicitly carries `_UnderlayColor`/`_UnderlayDilate` across font stamps — so restoring the font alone gave e.g. the title-screen news sign the default material's dark outline. The re-stamp adoption path updates `Mat` together with `Font`.

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
- **Other mods' UI:** `Skip()` leaves foreign mod hierarchies alone via two signals: `IsForeignModUi` (any ancestor MonoBehaviour defined in a non-game, non-engine assembly — catches component-marked mod HUDs like TUFHelper's), and `IsOtherModRoot` (transform root named after another loaded mod's Id, from `UnityModManager.modEntries`, ≥4 chars, refreshed each full sweep). The root-name check exists because some foreign labels are plain TMP directly under a bare canvas — e.g. Sapphire's toolbar tooltip and timeline clock/BPM carry **no** Sapphire component anywhere in their chain, invisible to the assembly walk; but every Sapphire hierarchy roots at a `Sapphire…`-named object. Falls back to `{"Sapphire"}` if the registry is unreadable.
- **Diagnostics (opt-in):** set `GameFontApplier.DiagEnabled = true` to dump (capped 16 lines/sweep, `GameFontDiag` prefix) each matching text's path/scene/textChanger/title/lsBold/applied font. `DiagFilter` is a substring list (empty = all <40-char texts). Sweeps also log "GameFont: sweep bold-swapped N" (debug) when the count changes.
- **`StopMod`** calls `RestoreAll()`; without it a hot-reloaded assembly (fresh Font instances, empty caches) re-caches scaled text as "original" and compounds across deploys.

### Song title (`txtLevelName`)

Legacy uGUI `Text` owned by the game. When `LevelNameUseOverlayFont` is on, `ApplyLevelNameTransform` attaches an **owned** `GameTextShadow` (`_levelNameFont` is a `TMP_FontAsset`) configured `collapseNewlines` + `noWrap` (the game appends the speed-trial multiplier `\n(1.1배)` to this same Text) and gives it an explicit Bismuth drop shadow (`SetShadow`, offset divided by `LevelNameScale` since `localScale` shrinks the subtree). Off → `Detach()` restores the vanilla original. `_levelNameFont` resolves from the **game** font family (`MainClass.ApplySelectedFont` → `SetLevelNameFont`), defaulting to the title weight; the `levelname` element weight overrides it. **CJK caveat:** the font must contain the title's glyphs — a Latin/Korean-only font (e.g. 에이투지체) lacks kana, so Japanese falls back to a mismatched-scale global font and renders undersized; hence the Paperlogy (CJK) default. Note the game font can resolve a frame *after* the level name is first set, leaving it on the earlier fallback font until a later re-apply.


