# Topaz — Developer Documentation

A HarmonyX / UnityModManager overlay mod for **A Dance of Fire and Ice (ADOFAI)**.
Build: `xbuild Topaz.sln` (Mono, .NET 4.8) — never `dotnet build`. A handful of expected warnings (toolset version, obsolete Unity APIs).

The game is Unity 6 and ships TextMeshPro. **Everything Topaz draws renders via TMP** — the HUD (Overlay + KeyViewer), the settings panel, *and* the game's own text. The game keeps its legacy `UnityEngine.UI.Text` / 3D `TextMesh` components (its scripts hold typed references), but Topaz hides each original (`canvasRenderer` alpha 0) and draws a synced TMP child over it (`GameTextShadow` / `GameTextMeshShadow`), so the legacy `Font` is gone from the render path entirely. Fonts are `TMP_FontAsset`s built at runtime from the bundle and from user-dropped `.ttf`/`.otf` files. New `.cs` files must be added to `Topaz.csproj`'s explicit `<Compile>` list.

Project philosophy: **minimal and lightweight, but highly customizable.**

## Getting oriented

- **[Project Layout](project-layout.md)** — the folder/file map: what lives where, how pages register.
- **[Logging](logging.md)** — where the logs are under each loader stack, `TopazLog`, and the in-game LogViewer.

## The settings panel

- **[Settings Panel Shell](ui-shell.md)** — panel architecture, PageStack drill-in navigation, widget conventions (NavRow, cards, floating dropdown), hotkey, page conventions.
- **[Settings Fields](settings.md)** — every serialized field in `Settings.cs` with defaults and purpose, plus `ColorGradient`/`ColorStop`.

## The HUD

- **[Overlay](overlay.md)** — the stat/combo HUD: UGUI hierarchy, lifecycle, canvas show conditions, combo logic.
- **[Key Viewer](keyviewer.md)** — hand/foot panels: lifecycle, per-frame flow, cell rendering (`RoundedRectGraphic`), rain columns and shadows.
- **[Game UI Layout Overrides](game-ui-layout.md)** — moving/scaling/hiding the *game's* HUD elements: wrapper transforms, the on-screen editor, the error-meter override.

## Text rendering

- **[Fonts & Text Rendering](fonts.md)** — FontLoader, TMP shadow underlay, TMP gotchas, and the full game-text repaint system (sizing, bold decisions, sweep scoping, per-element weights).

## Game integration

- **[HarmonyX Patches](patches.md)** — every patch target, timing, and what it drives; the Optimizations patch set.
- **[Attempts Persistence](attempts.md)** — per-level attempt counting, level keying, the in-game retry flow.
- **[Self-Updater](updater.md)** — Repository.json check, install flow, dual-install handling, MelonLoader/UMMCompat landmines.
