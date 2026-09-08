# Project Layout

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
│   ├── UIBuilder.cs          Static widget factory: Rect/VGroup/Columns/Label/SectionHeader(+WithHelp)/Toggle/Collapsible/NavRow/ToggleCard/NavCard/CardGrid/Slider/Button/DangerButton/ColorPicker/TextInput/Segmented/CycleSelector/ExpandSection/GradientBody/Dropdown(floating)/AccentSwatches
│   ├── UIBuilder.Handlers.cs Pointer/animation MonoBehaviours behind the widgets: ClickHandler/HoverHandler/ScrollSwallower/SliderControl/ColorPadControl/ExpandAnimator/DangerButtonState/…
│   ├── PageStack.cs          Per-tab drill-in navigator: Push/Pop subpages with ← Back + breadcrumb top bar (see ui-shell.md)
│   ├── DragHandle.cs         Titlebar drag: re-parents pointer events to the panel RectTransform
│   ├── ResizeHandle.cs       8 edge/corner resize handles — BR corner 22px (visible grip), others 12px
│   ├── TabRail.cs            Left-rail tab nav; auto-wraps each page in a ScrollRect/Viewport/Content + a PageStack
│   ├── LocationEditor.cs     Drag-to-position edit mode (own SSO canvas, LocHandle per movable element, axis snapping)
│   ├── GameUiEditor.cs       Same edit mode for the GAME's HUD elements (drag/scroll/right-click-reset, dimmed handles for inactive elements)
│   ├── UpdatePopup.cs        "Update available" popup + DuplicateInstallPopup (Mods/ vs UMMMods/ resolution)
│   ├── LogViewer.cs          In-game BismuthLog viewer (Misc → View log): scroll, Refresh, Open in Finder, [dbg] filter
│   └── Pages/
│       ├── KeyTokens.cs      Shared TokenFromKeyCode / PrettyTokenLabel helpers
│       ├── PageOverlay.cs    Overlay root list (NavRows) + per-stat/combo/attempts subpages, separator/weight rows, FPS
│       ├── PageKeyViewer.cs  Preset lists + preset editor (style cards, DM Note import/export page)
│       ├── PageKeyViewer.Rows.cs  partial: row grid, cell buttons, drag-reorder, rebind mode, ghost keys, row/cell subpages
│       ├── PageInput.cs      Menu input-block toggle + Key Limiter (chip editor + listen) + Chatter Blocker + KeyListener component
│       ├── PageHideUi.cs     Hide UI toggle-card grid with conditional sub-container
│       ├── PageUI.cs         Panel scale slider, panel/overlay font pickers side-by-side (family + weight, each option previewed in its own font), accent color
│       ├── PageGameUi.cs     Game-text repaint: game font, title weight, size/spacing/stats sliders; per-element NavCards (tinted = visible) → layout subpages
│       ├── PageTweaks.cs     "Tweaks" tab — editor autoplay-pause enable toggle + key rebind
│       └── PageMisc.cs       Read-only stats (RAM savings), Force reload, View log, Optimizations NavRow → subpage, Debug (GameProbe dumps + sweep trace)
└── Util/
    ├── AttemptsStore.cs      Persists per-level attempt counts to `BismuthAttempts.txt`
    ├── BismuthLog.cs         File-based session logger → `BismuthLog.txt`
    ├── FontLoader.cs         Font scan (bundle + loose .ttf/.otf in Fonts/) → FontEntry list; lazy TMP_FontAsset (dynamic SDF, weight table); family/weight linking; name matching
    ├── GameFontApplier.cs    Repaints ALL game text with the chosen font: legacy Text + 3D TextMesh via shadows, game TMP swapped in place; sweeps, apply, restore
    ├── GameFontApplier.Rules.cs  partial: bold/weight/fit decisions per element (titles, credits, CLS, per-element weights)
    ├── GameFontApplier.Scope.cs  partial: what a sweep skips — editor UI, Bismuth's own panel, other mods' roots
    ├── GameTextShadow.cs     Per-legacy-Text shadow renderer: hides the original, mirrors it into a TMP child (FitMode, strip-size, bold-label lines, collapse-newlines, no-wrap, explicit drop shadow)
    ├── GameTextMeshShadow.cs Same idea for 3D `TextMesh` (world-space) — TMP child matched to the mesh's world size
    ├── TmpShadow.cs          TMP drop-shadow component — drives the SDF underlay (legacy Shadow doesn't affect TMP)
    ├── GameUiLayout.cs       Game-HUD layout overrides: wrapper transforms over scrUIController elements + per-element visibility + error meter UpdateLayout override
    ├── GameProbe.cs          Runtime inspector (Misc → Debug): dump texts/images/assets/components filtered by substring
    ├── Tweaks.cs             "Tweaks" tab support — AutoPauseKeyCode() injected into scnEditor.Update by a transpiler
    ├── DmNotePreset.cs       DM Note preset.json ↔ KeyViewerPreset (import clusters dx/dy into rows, export replays the row math); files in <mod>/DmNote/
    └── UpdateChecker.cs      Self-updater: Repository.json check, zip download/install, duplicate-install resolution
```

Pages register via `UICore.Tabs.AddTab(name, BuildPage)` in `MainClass.TryEagerInit` — `BuildPage` takes the tab's `PageStack` and fills `stack.Root`. Root views are built **once** at registration and only shown/hidden on tab switch — root content whose option set depends on runtime state (e.g. the font-weight rows) needs an explicit refresh hook (`PageOverlay.RefreshFontWeightRows`); subpage bodies rebuild at push time, so they don't.


