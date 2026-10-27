# Attempts Persistence

## AttemptsStore (`AttemptsStore.cs`)

Static class that persists per-level attempt counts across sessions.

**File:** `<ModPath>/TopazAttempts.txt` — one `key=value` line per level. Sits alongside the other Topaz-owned files in the mod folder.

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


