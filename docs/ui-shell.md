# Settings Panel Shell (UI/)

The settings panel is a self-owned UGUI canvas (the old IMGUI panel was removed June 2026; UMM's OnGUI now just shows an "Open Settings Panel" button).

## Architecture

Modeled on KorenResourcePack v2's UI structure, stripped to the minimum:

- **No dependencies added** — the panel renders with `TextMeshProUGUI` (`UIBuilder.Tmp`/`Label`), reuses `RoundedRectGraphic`, no tween library. Panel font is a `TMP_FontAsset` from the font scan via `Theme.ApplyFont` (default Paperlogy-4Regular; missing/stale names fall back to `fonts[0]`); a 2×2 white texture generated once at runtime is the only sprite.
- **Static-only** — `UICore` is a static class, not a MonoBehaviour. UMM's `modEntry.OnUpdate` drives `UICore.HandleUpdate()` every frame. `UICore.Initialize(modEntry, settings, onChanged)` builds the canvas; `UICore.Dispose()` tears it down on mod disable.
- **Sharp/minimal aesthetic** — flat rectangles, 1px hairline borders (`UIBuilder.AddBorder`), no rounded panels, no fades. Rounded geometry (radios, cards, chips) renders via `RoundedRectGraphic`.
- **One screen, one topic** (July 2026 revamp) — pages never nest expanders. Anything bigger than a 1–2 control disclosure becomes a drill-in subpage via `PageStack`; independent on/off flags render as toggle-card grids.

## Navigation (`PageStack.cs`)

Every tab owns a `PageStack` (created by `TabRail`, handed to the page's `Build(PageStack)`). The page builds its root into `stack.Root`; subpages swap in-place inside the tab's scroll content:

- `Push(title, build, rebuildOnReveal = false)` — hides the current view, builds a new one with a top bar (`← Back` + breadcrumb of all pushed titles joined with `" / "`). `rebuildOnReveal` re-runs the builder when a child pops back to it (Key Viewer's editor uses this so row/cell deletes show up).
- `Pop()` / `PopToRoot()` — destroys the top view, reveals the previous one, restores the scroll offset saved at push time.
- `RefreshTop()` — clears + re-runs the top view's builder (used after subpage reset buttons so sliders re-read values).
- `RetitleTop(title)` — live-renames the top view's breadcrumb (Key Viewer preset Name field).
- `OnRootRevealed` — fires when the last subpage pops to the root (Key Viewer refreshes preset-row names; Game UI re-tints element cards).

Subpage bodies are built **at push time**, so they always read current runtime state (e.g. font-weight lists) — no refresh hooks needed for subpage content.

## Widget conventions

- Toggle = **classic radio button**: outer ring + filled inner dot when on (not a square checkbox).
- Row height: `UIBuilder.RowHeight = 32f`; sections use `UIBuilder.SectionHeader` (small caps bold, muted color).
- Section spacing: `UIBuilder.Spacer(content)` between groups.
- **NavRow** — drill-in row: optional toggle ring on the LEFT (Key-Viewer-preset style), title, muted `›` chevron right. Row click pushes the subpage; ring click toggles without navigating. Inline `Collapsible` (chevron left, radio right) remains for rows with tiny bodies (≤2 leaf controls).
- **ToggleCard / NavCard + CardGrid** — wrapping `GridLayoutGroup` (Flexible constraint, 156×44 cells) of click-anywhere cards; accent-tinted bg + border while ON (AccentFill/AccentBorder markers keep their alpha through `Theme.ApplyAccent`). `NavCard` adds a corner settings button (a `···` drawn from circles — **not** a `⚙` glyph, which user-supplied panel fonts often lack) that pushes the subpage; a toggle-less NavCard navigates on any click. Hide UI uses ToggleCards; Game UI Elements uses NavCards (tinted = visible).
- **Dropdown** — floating popup: full-screen transparent blocker (click-outside closes; carries `ScrollSwallower`, the one deliberate `IScrollHandler`) + a bordered panel right-aligned under the row, flipping upward near the screen bottom, scrollable past ~8 options, opened pre-scrolled to the selection. Optional `optionFonts` renders each option in its own typeface.
- **Columns** — `UIBuilder.Columns(parent, out left, out right)` pairs related controls side-by-side (panel/overlay font selectors, stat weight rows).
- `UIBuilder.GradientBody(parent, gradient, onChange)` builds the gradient controls flat (Solid toggle / Stops / solid picker / perfect color) — used inside subpage "Color" sections.
- All widgets register a `ClickHandler` (lightweight `IPointerClickHandler`) — no `Selectable`/`Button` state machine.
- Hover tinting uses `HoverHandler` (enter/exit only), **never** `EventTrigger` — EventTrigger implements `IScrollHandler` and silently eats mouse-wheel events, breaking ScrollRect scrolling whenever the cursor is over the widget.
- Destructive actions use `UIBuilder.DangerButton` — red-tinted bg, two-click confirm ("Click again to confirm"), 3s auto-revert timer.
- Section help uses `UIBuilder.SectionHeaderWithHelp(parent, title, helpText)` — a 14px `[?]` icon after the header label; hovering shows a tooltip popup parented to the canvas root (so it renders above the scroll viewport instead of being clipped by its RectMask2D). The header's HLG needs `childControlWidth = true` so the icon's `LayoutElement.preferredWidth` is honored. Section usage explanations live here, not in inline description rows (removed July 2026).
- **Glyph availability:** the panel font is user-selectable, so never rely on exotic Unicode glyphs (`⚙` rendered as nothing under Paperlogy). Draw icons procedurally (`RoundedRectGraphic` dots/shapes) or stick to proven glyphs (`▶`, `←`, `×`, `›`).

## Tab + page layout (`TabRail.cs`)

`TabRail` owns the left rail and the page host. Each page registered via `Tabs.AddTab(name, buildPage)` gets:

1. A page `RectTransform` filling the page host, `SetActive(false)` by default.
2. A `ScrollRect` on the page, with a `Viewport` (RectMask2D + transparent raycast-target Image) and a `Content` child (VerticalLayoutGroup + ContentSizeFitter).
3. A fresh `PageStack(scroll, content)`; the `buildPage(stack)` callback runs once at registration to populate the root view.

**Scrolling gotcha:** `VerticalLayoutGroup.childControlHeight` must be `true` so VLG honors each row's `LayoutElement.preferredHeight`. With it off, VLG reads the bare `RectTransform.sizeDelta.y` (which is 0 for all rows) and the content collapses to zero height — `ContentSizeFitter` then sets content height to 0 and there's nothing to scroll. The viewport also needs a raycast-target Graphic (transparent Image) so the scroll wheel has a handler to land on when the cursor is between rows.

## Hotkey

Toggle: **Ctrl + T** only, in `UICore.HandleUpdate`.

**macOS dead-key gotcha:** the original `Alt + B` binding does not work on macOS. Option (Unity's `LeftAlt`) is a system-level dead-key modifier — Option+B is reserved for typing `∫`, and the B keystroke is swallowed before reaching `Input.GetKeyDown(KeyCode.T)`. Diagnosed via `Input.anyKeyDown` logging: Alt-alone fired, Space fired, but a B keydown was never observed during Option+B chords. Cmd and Ctrl have no such behavior. Function keys (F1–F12) bypass the issue entirely since no modifier is held.

This applies to all future hotkeys: **never bind Option + letter on macOS.** Cmd, Ctrl, Shift, or single function keys only.

## Loader-loop driver

`MainClass.Setup` assigns `modEntry.OnUpdate = (_, __) => UICore.HandleUpdate();` — fires every frame the mod is enabled. Verified under MelonLoader + UMMCompat on Unity 6 (6000.3.10f1); periodic "[UI] HandleUpdate alive frame=N" log entries confirm continuous ticking past startup.

`UICore.Initialize(...)` is called from `MainClass.TryEagerInit`, which is itself gated by `IsEngineReady()` and retried on first scene load when the engine wasn't ready at toggle-on. This shares the same deferred-init mechanism as Overlay / KeyViewer construction — necessary because `koren UMM` / UMMCompat can load mods before game statics are alive (calling asset APIs that early crashes the engine, uncatchable).

## Master switch (rail)

`TabRail.AddMasterSwitch` pins a toggle row to the bottom of the tab rail (wired in `MainClass.BuildUI` → `MainClass.SetMasterEnabled`). It drives `Settings.ModEnabled` (persisted) and flips `MainClass.EnableFeatures()` / `DisableFeatures()` — the same feature set the UMM checkbox controls, split so the **shell survives**: panel, Ctrl+T hotkey, fonts, and update checker stay alive while everything the mod does *to the game* (Harmony patches, Overlay, KeyViewer, `GameUiLayout` wrappers, `GameFontApplier` restyle) is torn down and restored. `MainClass.FeaturesOn` is the runtime gate: `GameFontApplier.Enabled`, `GameUiLayout.Reapply/ApplyOne/ApplyErrorMeter`, and the scene-unload optimization all check it, so editing settings in a master-off panel never touches game objects. A master-off state persists across restarts (a master-off launch never patches at all); the UMM toggle (`StopMod`) additionally disposes the shell.

## Settings UI pages (`UI/Pages/`)

All settings interaction is UGUI (the IMGUI SettingsGui/SettingsInput files were deleted June 2026 after the port). Conventions:

- Pages are built once via `UICore.Tabs.AddTab(name, Build)` in `MainClass.TryEagerInit`; tab switching only toggles `SetActive`. Root-view content whose option set depends on runtime state needs an explicit refresh hook — e.g. `PageOverlay.RefreshFontWeightRows` (multicast `Action`, reset at the top of `PageOverlay.Build`, registered per row by `AddWeightRow`, invoked by PageUI's overlay-font selector after a family change; handlers self-unhook when their host was destroyed with a popped subpage). Subpage content rebuilds at push time and needs none of this.
- `UICore.OnSettingsChanged` fans out to `overlay.ApplySettings` + `keyViewer.ApplySettings` + `KeyLimiter.Apply` (wired in `MainClass.TryEagerInit`). Structural KeyViewer edits additionally fire `UICore.OnKeyViewerRebuild` → `keyViewer.Rebuild`.
- `KeyListener` (PageInput) is the shared key-capture MonoBehaviour for rebind / ghost keys / limiter chips: polls a watched-key list once per frame while `Active`, fires `OnKey(KeyCode)` once, wrapped in `KeyLimiter.RawReadExempt` so capture works while the menu's input block is engaged (capture only ever happens with the menu open).
- The **Misc** page displays read-only stats — "RAM savings (last scene load)", populated from `MainClass.LastUnloadSavingsBytes` (measured around `Resources.UnloadUnusedAssets()` in `OnSceneUnloaded`).
- Hide UI card grid renders only while `HideAllUI` is off; its "All judgements" card deactivates the per-category cards in the same grid.
