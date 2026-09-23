using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    /* Repaints the game's text in the Bismuth font (Settings.GameTextUseOverlayFont): legacy
       Text and 3D TextMesh via shadows, game TMP swapped in place (originals cached for
       restore). Size is scaled by the line-height/em ratio (fallback 0.85). Partials: Rules
       (bold/weight/fit decisions), Scope (what to skip). */
    internal static partial class GameFontApplier
    {
        private const float DefaultScale = 0.85f;

        private static TMP_FontAsset _tmpFont;
        /* Family bold for title text (scrHUDText.isTitle: world number/name on level
           select, etc). Falls back to the regular weight when absent. */
        private static TMP_FontAsset _boldTmpFont;

        /* Mat: the text's material at cache time. Restoring `.font` alone resets TMP to the
           font asset's DEFAULT material, but the game configures text via per-instance
           materials — RDString.SetLocalizedFont carries _UnderlayColor/_UnderlayDilate across
           font stamps through `fontMaterial` — so e.g. the news sign got the default's dark
           outline after a disable instead of its own clean instance. */
        private struct TmpState
        {
            public TMP_FontAsset Font; public Material Mat; public float Size; public float LineSpacing;
            public FontStyles Style; public bool AutoSize; public float SizeMin, SizeMax;
            /* What we last stamped on, and under which styling generation. A re-sweep that
               finds all three unchanged skips the classification above the old early-out
               (ElementWeightEntry / ShouldBold / IsClsNoWrap) — that work, not the ancestor
               walks, is what a warm sweep was spending its time on. */
            public TMP_FontAsset Applied; public FontStyles AppliedStyle; public int Gen;
        }

        /* Bumped whenever an input to the styling decision changes: the fonts themselves, the
           per-element weight table, or a size/spacing slider. Any settled text then re-derives
           its style on the next sweep. Generation rather than a cache clear, so a change during
           a partially-drained sweep can't leave half the scene stamped as current. */
        private static int _styleGen;

        // Legacy game Text and 3D TextMesh are rendered via shadows (TMP). Only game TMP
        // is still swapped in place; its original state is cached here for Restore.
        private static readonly Dictionary<TMP_Text, TmpState> _origTmp = new Dictionary<TMP_Text, TmpState>();

        // FeaturesOn: the master switch. A master-off panel can still flip this page's
        // settings; Reapply() must then restore rather than sweep.
        private static bool Enabled =>
            MainClass.FeaturesOn && MainClass.Settings != null && MainClass.Settings.GameTextUseOverlayFont;

        /* Hand-tuned bases for Pretendard over the game fonts: metric normalization
           alone leaves text ~1.4× too large and leading ~1.5× too tight. Sliders
           apply ON TOP of these, centered at 1.0. */
        private const float BaseGameTextScale = 0.6f;
        private const float BaseGameLineSpacing = 1.5f;
        private const float BaseStatsScale = 0.8f;

        // User-tunable multiplier on top of metric normalization (Game UI tab)
        private static float UserScale =>
            (MainClass.Settings != null ? Mathf.Clamp(MainClass.Settings.GameTextScale, 0.4f, 1.5f) : 1f)
            * BaseGameTextScale;

        /* Line-advance multiplier (Game UI tab). Pretendard fills more of the em box,
           so swapped multi-line text reads cramped at a metrically "equal" size. */
        private static float UserLineSpacing =>
            (MainClass.Settings != null ? Mathf.Clamp(MainClass.Settings.GameTextLineSpacing, 0.8f, 2f) : 1f)
            * BaseGameLineSpacing;

        // Separate multiplier for level-select per-level stats panels
        private static float UserStatsScale =>
            (MainClass.Settings != null ? Mathf.Clamp(MainClass.Settings.GameStatsScale, 0.4f, 1.5f) : 1f)
            * BaseStatsScale;

        // Per-level stats (attempts, max x-acc, …) sit under "StatsText Container"
        private static bool IsStatsText(Component c)
        {
            var p = c.transform;
            for (int i = 0; i < 5 && p != null; i++, p = p.parent)
                if (p.name.Contains("StatsText")) return true;
            return false;
        }

        /* Stats size applied to CONTAINER localScale, not font size: these labels
           best-fit their rects, so font-size changes feel stepped/dead. Cached per
           container for restore; idempotent (always orig × multiplier). */
        private struct XformState { public Vector3 Scale; public Vector3 Pos; }
        private static readonly Dictionary<Transform, XformState> _statsOrigScale =
            new Dictionary<Transform, XformState>();

        private static void ApplyStatsScale(Component c)
        {
            Transform container = null;
            var p = c.transform;
            for (int i = 0; i < 5 && p != null; i++, p = p.parent)
                if (p.name.Contains("StatsText")) container = p; // topmost match wins
            ScaleTransform(container, UserStatsScale);
        }

        private static void ScaleTransform(Transform tr, float m, bool keepCenter = true)
        {
            if (tr == null) return;
            XformState st;
            if (!_statsOrigScale.TryGetValue(tr, out st))
            {
                st = new XformState { Scale = tr.localScale, Pos = tr.localPosition };
                _statsOrigScale[tr] = st;
            }
            var ns = new Vector3(st.Scale.x * m, st.Scale.y * m, st.Scale.z);
            tr.localScale = ns;
            /* Scaling is about the pivot, which sits off-center on stats containers
               (the block drifted down as it shrank), so shift localPosition to keep the
               rect center put. keepCenter=false scales about the pivot instead, for
               right-anchored labels (Continue/LastLevel) that must stay flush to the
               margin. */
            var rt = tr as RectTransform;
            if (keepCenter && rt != null)
            {
                Vector2 c = rt.rect.center;
                tr.localPosition = st.Pos + new Vector3(
                    c.x * (st.Scale.x - ns.x),
                    c.y * (st.Scale.y - ns.y), 0f);
            }
            else tr.localPosition = st.Pos;
        }

        // Called from MainClass.ApplySelectedFont whenever overlay font resolves
        internal static void SetFonts(TMP_FontAsset tmpFont, TMP_FontAsset boldTmpFont)
        {
            _tmpFont = tmpFont;
            _boldTmpFont = boldTmpFont != null ? boldTmpFont : tmpFont;
            _styleGen++;
            WireBoldWeight();
            PrewarmGameGlyphs();
            _lastSweepFrame = -1; // font identity changed, never dedupe this sweep
            Reapply();
            RequestFullSweepSoon(); // catch Start()-time localized-font re-stamps
        }

        // Rasterize common Latin glyphs into the game atlases now, so the first scene sweep
        // doesn't cold-generate them. Korean (the bulk of localized UI) can't be cheaply
        // pre-warmed, so it still rasterizes on first use, then persists across scenes.
        private const string PrewarmAscii =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        /* Point the regular game-font asset's bold (700) slot at the bundled Bold asset, so a
           <b> tag renders TRUE bold instead of TMP's faux-bold. The guest-track shadow uses
           <b> to bold individual label LINES inside a component that also holds a
           regular-weight name ("객원 레벨 디자인:\nRikri"). */
        private static void WireBoldWeight()
        {
            try
            {
                if (_tmpFont == null || _boldTmpFont == null || _boldTmpFont == _tmpFont) return;
                var table = _tmpFont.fontWeightTable;
                if (table == null || table.Length <= 7) return;
                var pair = table[7]; // index 7 = weight 700 (bold)
                pair.regularTypeface = _boldTmpFont;
                table[7] = pair;
            }
            catch { }
        }

        private static void PrewarmGameGlyphs()
        {
            try
            {
                _tmpFont?.TryAddCharacters(PrewarmAscii);
                if (_boldTmpFont != null && _boldTmpFont != _tmpFont) _boldTmpFont.TryAddCharacters(PrewarmAscii);
            }
            catch { }
        }

        /* Called on scene change / level start / toggle flip. Frame-deduped because
           scene change and level start can land on the same frame and the
           FindObjectsByType scan is the expensive part. */
        private static int _lastSweepFrame = -1;

        internal static void Reapply()
        {
            if (Enabled)
            {
                if (Time.frameCount == _lastSweepFrame) return;
                _lastSweepFrame = Time.frameCount;
                Apply();
            }
            else Restore();
        }

        /* The cheap sweep: only text that is currently active. For triggers that mean "something
           just appeared" (a delayed re-check after scene entry, a level-select state change)
           rather than "a whole scene arrived" — hidden text was already styled by the full sweep
           that brought the scene in, and hasn't changed since. */
        internal static void ReapplyActive()
        {
            if (!Enabled) { Restore(); return; }
            if (Time.frameCount == _lastSweepFrame) return;
            _lastSweepFrame = Time.frameCount;
            ApplyActive();
        }

        /* Scoped sweep: the game HUD canvas + the world-space autoplay/status label.
           Everything that (re)spawns or gets re-stamped mid-LEVEL (death %, results,
           congrats, rewind, press-to-start, countdown) sits under scrUIController.canvas;
           the only styled game text NOT under it is the autoplay label. A full scene scan
           here visibly hitched at start/death/retry on large maps (thousands of tile and
           decoration texts), so a retry — which reloads scnGame — uses this scope instead.
           Full sweeps stay reserved for menu scene loads. */
        internal static void ReapplyHud()
        {
            if (!Enabled || _tmpFont == null) return;
            try
            {
                var uic = scrUIController.instance;
                if (uic != null && uic.canvas != null) ApplyTo(uic.canvas.gameObject);
                var auto = GameUiLayout.AutoplayTextObject();
                if (auto != null) ApplyTo(auto);
            }
            catch { }
        }

        /* Death/results text spawns on controller state changes, after the level-start
           sweep. The state-change patch requests two delayed sweeps (Overlay.Update
           ticks them): one soon for instant texts, one later for animated screens. */
        private static int _sweepFrameA = -1;
        private static int _sweepFrameB = -1;

        internal static void RequestSweepSoon()
        {
            if (!Enabled) return;
            _sweepFrameA = Time.frameCount + 2;
            _sweepFrameB = Time.frameCount + 30;
        }

        /* Scene-entry texts get localized fonts in their Start(), one frame AFTER
           sceneLoaded, so an immediate sweep runs too early and gets stomped (cold
           launch showed the vanilla title screen until the toggle was cycled). Delayed
           FULL sweeps after scene entry / font resolution catch the re-stamp. */
        private static int _fullSweepFrameA = -1;
        private static int _fullSweepFrameB = -1;

        internal static void RequestFullSweepSoon()
        {
            if (!Enabled) return;
            _fullSweepFrameA = Time.frameCount + 2;
            _fullSweepFrameB = Time.frameCount + 30;
        }

        /* For UI that OPENS over a live scene (pause menu, its settings submenu). The menu's own
           subtree — inactive children included — is already styled by the ApplyTo at the call
           site; the scene behind it hasn't changed just because the player paused. A full sweep
           there rescanned the whole level: 11693 texts, 44ms scan + 134ms styling, to restyle a
           menu that was already done. This catches anything that activated, and nothing else. */
        private static int _activeSweepFrame = -1;

        internal static void RequestActiveSweepSoon()
        {
            if (!Enabled) return;
            _activeSweepFrame = Time.frameCount + 2;
        }

        /* Re-style only text we have already seen — no FindObjectsByType. Anything the game
           re-stamped since fails the settled check inside ApplyTmp and gets re-derived; the
           rest costs one dictionary lookup. */
        private static readonly List<TMP_Text> _recheckBuf = new List<TMP_Text>();

        private static void RecheckKnown()
        {
            if (!Enabled || _tmpFont == null) return;
            _recheckBuf.Clear();
            foreach (var kv in _origTmp)
                if (kv.Key != null && !ReferenceEquals(kv.Key.font, kv.Value.Applied))
                    _recheckBuf.Add(kv.Key);   // buffered: ApplyTmp writes back into _origTmp
            for (int i = 0; i < _recheckBuf.Count; i++) ApplyTmp(_recheckBuf[i]);
            if (_recheckBuf.Count > 0)
                BismuthLog.Debug($"[dbg] GameFont recheck: re-styled {_recheckBuf.Count} re-stamped text(s)");
            _recheckBuf.Clear();
        }

        internal static void Tick()
        {
            // Finish styling a spread-out scene sweep before the deferred work below.
            if (_pending.Count > 0) DrainPending(SweepBudget);
            /* State-change sweeps are HUD-scoped in gameplay (the texts they catch spawn
               under the HUD canvas; full sweeps caused death-screen lag). Level select
               is swept fully: its world text activates late, outside any canvas, and the
               scene is small. */
            if (_sweepFrameA > 0 && Time.frameCount >= _sweepFrameA) { _sweepFrameA = -1; StateSweep(); }
            if (_sweepFrameB > 0 && Time.frameCount >= _sweepFrameB) { _sweepFrameB = -1; StateSweep(); }
            if (_activeSweepFrame > 0 && Time.frameCount >= _activeSweepFrame) { _activeSweepFrame = -1; ReapplyActive(); }
            if (_fullSweepFrameA > 0 && Time.frameCount >= _fullSweepFrameA) { _fullSweepFrameA = -1; Reapply(); }
            /* The late pass exists to catch the game re-stamping a localized font in Start(),
               which only ever happens to text sweep A already found. Re-checking what we know
               costs a dictionary walk; a second full sweep costs another 20-40ms scan for the
               same answer. Genuinely new objects come from the next real trigger. */
            if (_fullSweepFrameB > 0 && Time.frameCount >= _fullSweepFrameB) { _fullSweepFrameB = -1; RecheckKnown(); }
            /* Size-multiplier changes need a full restore+apply (Apply skips text
               already on the Bismuth font). Debounce so slider drags don't sweep the
               scene every tick. */
            if (_resizeFrame > 0 && Time.frameCount >= _resizeFrame)
            {
                _resizeFrame = -1;
                if (Enabled) { Restore(); Apply(); _lastSweepFrame = Time.frameCount; }
            }
        }

        private static void StateSweep()
        {
            bool levelSelect = false;
            try
            {
                levelSelect = UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().name == "scnLevelSelect";
            }
            catch { }
            // Level select's late text activates outside any canvas, so this can't be HUD-scoped
            // — but it IS activation, which is exactly what the active-only scan is for.
            if (levelSelect) ReapplyActive();
            else ReapplyHud();
        }

        private static int _resizeFrame = -1;

        // Called when a size slider moves. Coalesces into one re-sweep shortly after.
        internal static void RequestResize()
        {
            if (!Enabled) return;
            _styleGen++;   // sizes feed the stamp, so settled text has to re-derive
            _resizeFrame = Time.frameCount + 15;
        }

        private static int _lastBoldLogged = -1;

        // ── Sweep diagnostics (opt-in) ─────────────────────────────────────
        /* Flip DiagEnabled to dump, once per sweep, the bold/font decision for every text
           matching DiagFilter — invaluable for tracing which component a stray label
           belongs to and why it did/didn't bold. */
        // Runtime-toggled from Misc → Debug → "Trace font sweep" (DiagFilter = the Debug filter).
        internal static bool DiagEnabled = false;
        // Substrings to match. null/empty matches every non-empty text under DiagMaxLen.
        internal static string[] DiagFilter = null;
        private static int _diagBudget;

        // Restrict dump to one scene (active scene name) when set. null = any.
        internal static string DiagScene = null;
        internal static int DiagMaxLen = 40;

        private static bool DiagMatch(Component c, string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length >= DiagMaxLen) return false;
            // Skip Bismuth's own panel UI (DontDestroyOnLoad noise)
            var root = c.transform.root;
            if (root != null && root.name.StartsWith("Bismuth")) return false;
            if (DiagScene != null)
            {
                try { if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != DiagScene) return false; }
                catch { }
            }
            if (DiagFilter == null || DiagFilter.Length == 0) return true;
            foreach (var f in DiagFilter)
                if (!string.IsNullOrEmpty(f) && text.Contains(f)) return true;
            return false;
        }

        private static float DiagLineSpacing(Component c)
        {
            if (c is Text t) return t.lineSpacing;
            if (c is TMP_Text m) return m.lineSpacing;
            return 0f;
        }

        // Post-apply dump for one component. font/style are the applied result.
        private static void Diag(Component c, string text, string type, string font, object style)
        {
            if (!DiagEnabled || _diagBudget <= 0 || !DiagMatch(c, text)) return;
            _diagBudget--;
            string ns = "-", tc = "-", desktop = "-";
            try { var n = c.GetComponentInParent<NewsSign>(); if (n != null) ns = n.name; } catch { }
            try
            {
                var x = c.GetComponentInParent<scrTextChanger>();
                if (x != null)
                {
                    tc = x.name;
                    var dt = typeof(scrTextChanger).GetField("desktopText");
                    if (dt != null) desktop = (dt.GetValue(x) as string) ?? "null";
                }
            }
            catch { }
            string extra = "";
            try
            {
                var rt = c.transform as RectTransform;
                if (rt != null) extra = " pos=" + rt.anchoredPosition + " size=" + rt.rect.size + " scale=" + rt.localScale.x;
                if (c is Text tt) extra += " hOver=" + tt.horizontalOverflow + " vOver=" + tt.verticalOverflow + " fs=" + tt.fontSize + " bestFit=" + tt.resizeTextForBestFit + " raw=[" + tt.text.Trim() + "]";
                if (c is TMP_Text mm) extra += " over=" + mm.overflowMode + " wrap=" + mm.textWrappingMode + " fs=" + mm.fontSize + " autoSize=" + mm.enableAutoSizing;
            }
            catch { }
            BismuthLog.Debug("GameFontDiag '" + text.Trim() + "' " + type + " path=" + DiagPath(c.transform) +
                " textChanger=" + tc + " title=" + IsTitle(c) + " lsBold=" + LevelSelectBold(c, text) +
                " lineSpacing=" + DiagLineSpacing(c) + extra + " -> font=" + font + " style=" + style);
        }

        private static string DiagPath(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            for (var p = t; p != null; p = p.parent)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, p.name);
            }
            return sb.ToString();
        }

        /* A full scene sweep gathers every text, then styles them a budget at a time across
           frames (Tick drains the rest), so a large scene load — where each legacy text now
           spawns a TMP shadow GameObject — doesn't stall in one frame. Originals stay visible
           until their shadow attaches, so it reads as a brief styling cascade. */
        private static readonly Queue<Component> _pending = new Queue<Component>();
        private const int SweepBudget = 256;

        /* Sweep cost readout ([dbg], Misc → Debug mode). Prints where a sweep's time actually
           went — scan vs styling — because "the font sweep hitches on big levels" has more
           than one plausible cause and the profiler can't be attached to a shipped build. */
        private static readonly System.Diagnostics.Stopwatch _sweepScanWatch = new System.Diagnostics.Stopwatch();
        private static readonly System.Diagnostics.Stopwatch _sweepDrainWatch = new System.Diagnostics.Stopwatch();
        private static int _sweepFound, _sweepStyled, _sweepSkipHits, _sweepSettled;
        private static bool _lastScanWasFull;

        /* Bismuth's share of a level load. Accumulated across every sweep since the scene
           arrived, so it can be weighed against the load's total wall time — optimising our
           178ms is worth doing at 500ms of load and pointless at 4 seconds. */
        internal static double SweepMsThisScene { get; private set; }
        internal static void ResetSceneAccounting() => SweepMsThisScene = 0d;

        /* Scene entry needs the inactive text too — menus and popups are built hidden and must
           already be styled when they open. Every OTHER sweep is looking for text that just
           appeared, which by definition is active, and Include costs 4-20x more because it
           walks the scene graph instead of a type index (measured: 8886 found / 86 active,
           22.1ms vs 5.3ms). So: full scan when a scene or font arrives, active-only after. */
        private static void ApplyActive() => Apply(activeOnly: true);

        private static void Apply(bool activeOnly = false)
        {
            if (_tmpFont == null) return;
            var inactive = activeOnly ? FindObjectsInactive.Exclude : FindObjectsInactive.Include;
            _lastScanWasFull = !activeOnly;
            _sweepScanWatch.Reset(); _sweepDrainWatch.Reset();
            _sweepFound = _sweepStyled = _sweepSkipHits = _sweepSettled = 0;
            _sweepScanWatch.Start();
            PruneThrottled();
            RefreshModRootPrefixes(); // pick up mods installed after our load
            RefreshVersionTexts();
            _sweepBoldCount = 0;
            if (DiagEnabled) _diagBudget = 64; // per-sweep cap so it can't flood log
            _pending.Clear();
            foreach (var t in Object.FindObjectsByType<Text>(inactive, FindObjectsSortMode.None))
                _pending.Enqueue(t);
            foreach (var t in Object.FindObjectsByType<TMP_Text>(inactive, FindObjectsSortMode.None))
                _pending.Enqueue(t);
            foreach (var t in Object.FindObjectsByType<TextMesh>(inactive, FindObjectsSortMode.None))
                _pending.Enqueue(t);
            _sweepFound = _pending.Count;

            _sweepScanWatch.Stop();

            DrainPending(SweepBudget);
        }

        // Style up to `budget` of the gathered texts; Tick drains the remainder next frames.
        private static void DrainPending(int budget)
        {
            _sweepDrainWatch.Start();
            int n = 0;
            while (_pending.Count > 0 && n < budget)
            {
                var c = _pending.Dequeue();
                n++;
                if (c == null) continue;
                if (c is Text txt)
                {
                    ApplyText(txt);
                    if (DiagEnabled) Diag(txt, txt.text, "Text", txt.font != null ? txt.font.name : "null", txt.fontStyle);
                }
                else if (c is TMP_Text tmp)
                {
                    ApplyTmp(tmp);
                    if (DiagEnabled) Diag(tmp, tmp.text, tmp.GetType().Name, tmp.font != null ? tmp.font.name : "null", tmp.fontStyle);
                }
                else if (c is TextMesh mesh)
                {
                    ApplyTextMesh(mesh);
                    if (DiagEnabled) Diag(mesh, mesh.text, "TextMesh", mesh.font != null ? mesh.font.name : "null", mesh.fontStyle);
                }
            }
            _sweepStyled += n;
            _sweepDrainWatch.Stop();
            if (_pending.Count == 0 && _sweepBoldCount != _lastBoldLogged)
            {
                _lastBoldLogged = _sweepBoldCount;
                BismuthLog.Debug("GameFont: sweep bold-swapped " + _sweepBoldCount +
                                 " texts (bold font: " + (_boldTmpFont != null ? _boldTmpFont.name : "none") + ")");
            }
            if (_pending.Count == 0 && _sweepFound > 0)
            {
                // Once per finished sweep — the watches are cumulative, so this is the only
                // point where adding them doesn't multiply-count the drains.
                SweepMsThisScene += _sweepScanWatch.Elapsed.TotalMilliseconds
                                  + _sweepDrainWatch.Elapsed.TotalMilliseconds;
                BismuthLog.Debug($"[dbg] GameFont sweep: {_sweepFound} texts, " +
                    $"scan {_sweepScanWatch.Elapsed.TotalMilliseconds:0.0}ms, " +
                    $"style {_sweepDrainWatch.Elapsed.TotalMilliseconds:0.0}ms, " +
                    $"settled {_sweepSettled}, skip-cache {_skipCache.Count} entries ({_sweepSkipHits} hits), " +
                    $"{(_lastScanWasFull ? "full" : "active-only")} scan");
                _sweepFound = 0;
            }
        }

        /* Re-apply the Bismuth font right after the game stamps a localized one
           (RDString.SetLocalizedFont, patched), fixing language-selector previews that
           revert to each language's own font over the swap. A script Pretendard lacks
           (e.g. Thai) falls back to tofu, acceptable per the "keep our font" request. */
        internal static void OnLocalizedFontSet(Text t)
        {
            if (Enabled && _tmpFont != null) ApplyText(t);
        }

        internal static void OnLocalizedFontSet(TMP_Text t)
        {
            if (t != null) NoteGameFont(t.font);
            if (Enabled && _tmpFont != null) ApplyTmp(t);
        }

        /* The game's own localized TMP asset, kept so a build with no fonts installed still
           draws readable text: TMP_Settings.defaultFontAsset is Latin-only, which tofus a
           Korean panel. The game stamps the language's real font through SetLocalizedFont,
           so the first one that isn't ours is exactly what the game itself renders with. */
        private static TMP_FontAsset _gameFont;

        internal static TMP_FontAsset GameFont
        {
            get { return _gameFont != null ? _gameFont : TMP_Settings.defaultFontAsset; }
        }

        private static void NoteGameFont(TMP_FontAsset f)
        {
            if (f == null || IsOurTmpFont(f)) return;
            _gameFont = f;
        }

        internal static void OnLocalizedFontSet(TextMesh t)
        {
            if (Enabled && _tmpFont != null) ApplyTextMesh(t);
        }

        // Per-spawn hook for pooled/instantiated objects (judgement popups)
        internal static void ApplyTo(GameObject go)
        {
            if (!Enabled || _tmpFont == null || go == null) return;
            if (DiagEnabled) _diagBudget = 16; // HUD sweeps get their own budget (filter is specific)
            foreach (var t in go.GetComponentsInChildren<Text>(true))
            {
                ApplyText(t);
                if (DiagEnabled && t != null) Diag(t, t.text, "Text", t.font != null ? t.font.name : "null", t.fontStyle);
            }
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
            {
                ApplyTmp(t);
                if (DiagEnabled && t != null) Diag(t, t.text, t.GetType().Name, t.font != null ? t.font.name : "null", t.fontStyle);
            }
            foreach (var t in go.GetComponentsInChildren<TextMesh>(true)) ApplyTextMesh(t);
        }

        /* Visual-size normalization: ratio of (line height / em) between the original
           GAME font (legacy, still legacy — it's the game's own text) and our Bismuth TMP
           font. > 1 means the original is airier, so swapped text must shrink. Clamped so
           metric outliers don't halve a label. Our side comes from TMP faceInfo (the legacy
           Bismuth Font is gone). */
        private static float LegacyScale(Font orig)
        {
            try
            {
                if (orig != null && orig.fontSize > 0 && orig.lineHeight > 0 && _tmpFont != null)
                {
                    float o = (float)orig.lineHeight / orig.fontSize;
                    float u = _tmpFont.faceInfo.lineHeight / _tmpFont.faceInfo.pointSize;
                    if (o > 0f && u > 0f) return Mathf.Clamp(o / u, 0.6f, 1.1f);
                }
            }
            catch { }
            return DefaultScale;
        }

        private static float TmpScale(TMP_FontAsset orig)
        {
            try
            {
                if (orig != null && _tmpFont != null)
                {
                    float o = orig.faceInfo.lineHeight / orig.faceInfo.pointSize;
                    float u = _tmpFont.faceInfo.lineHeight / _tmpFont.faceInfo.pointSize;
                    if (o > 0f && u > 0f) return Mathf.Clamp(o / u, 0.6f, 1.1f);
                }
            }
            catch { }
            return DefaultScale;
        }

        /* IMPORTANT: all three Apply* methods derive sizes from the CACHED ORIGINAL
           state, never current values. The game re-assigns localized fonts on rewind,
           defeating the "font == ours" skip; recomputing from current values then
           compounds the scale once per attempt (text grew/shrank every death). */

        private static void ApplyText(Text t)
        {
            if (t == null || Skip(t)) return;
            var elem = ElementWeightEntry(t);
            /* Decisions come from the LIVE original. We no longer swap its font, so its
               fontStyle / font.name still reflect the game's own — the basis for bold
               detection. The original stays legacy and is hidden by its shadow, which
               renders the visible glyphs in TMP. */
            bool italic = t.fontStyle == FontStyle.Italic || t.fontStyle == FontStyle.BoldAndItalic;
            bool bold = ShouldBold(t, t.text,
                t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic,
                t.font != null ? t.font.name : null);
            if (bold) _sweepBoldCount++;
            bool editorUi = IsEditorUi(t);
            bool guest = InGuestTrackCredit(t);
            // Guest-track credits mix Korean labels with Latin names; per-font metric scaling
            // inflates the airier Latin names past the labels. Scale uniformly so every line
            // follows the game's authored fontSize (label > name) rather than font metrics.
            float scale = (guest ? DefaultScale : LegacyScale(t.font)) * (editorUi ? 1f : UserScale);
            // The collab cross-promo is oversized vanilla display text — render it noticeably
            // smaller than the rest of the panel.
            if (guest && IsCollabTag(t)) scale *= 0.7f;

            /* Transform-level fixes apply to the original; the shadow child inherits them.
               CLS portal/world-name and Continue/LastLevel are fit-container text that
               ignore fontSize, so they're shrunk via the transform instead. */
            // Wave tags fit via FitMode.Width (below); the transform downscale would double-shrink them.
            if (InCls() && HasAncestor(t, "WorldNameCanvas") && !HasAncestor(t, "waveTag"))
            {
                // The chain-banner level sign ("레벨", under SignContainer) reads too small at
                // the portal-label downscale — give it its own larger, center-kept scale.
                bool sign = HasAncestor(t, "SignContainer");
                ScaleTransform(t.transform, sign ? ClsSignScale : ClsLabelScale, keepCenter: sign);
            }
            if (IsStatsText(t)) ApplyStatsScale(t);
            if (t.name == "LastLevel" && t.transform.parent != null && t.transform.parent.name == "Continue")
                ScaleTransform(t.transform, 0.6f, keepCenter: false);
            // Separate name component below its label — nudge it down off the cramped label.
            if (guest && !string.IsNullOrEmpty(t.text) && IsGuestName(t))
                OffsetNameDown(t.transform, GuestNameGap * t.fontSize);

            /* Real Bold/Black asset for bold (TMP renders the bundled weights cleanly,
               unlike the legacy Black asset); fall back to a faux-bold style flag only when
               no bold asset is loaded. An element weight pins a specific asset outright. */
            TMP_FontAsset tmpFont = elem != null && elem.TmpFont != null ? elem.TmpFont
                                  : (bold && _boldTmpFont != null ? _boldTmpFont : _tmpFont);
            bool fauxBold = bold && (elem == null || elem.TmpFont == null) && _boldTmpFont == null;
            FontStyles style = (fauxBold ? FontStyles.Bold : FontStyles.Normal)
                             | (italic ? FontStyles.Italic : FontStyles.Normal);
            /* Fit mode for overflow-prone display text (Pretendard renders wider than the
               game fonts). Portal + guest-track credits wrap and autosize down to STAY
               INSIDE their box (Box) — so a long phrase ("Guest level design by") never
               spills out and blow up to screen width when the world-enter zoom scales the
               whole XtraInfo panel up. The title/splash credit only needs to not wrap, so
               it collapses to one line capped near natural size (Width). */
            bool splash = IsSplashCredit(t);
            GameTextShadow.FitMode fit;
            float fitShrink;
            if (InPortalCredit(t) || guest) { fit = GameTextShadow.FitMode.Box; fitShrink = 1f; }
            else if (IsCredits(t) || splash)
            {
                fit = GameTextShadow.FitMode.Width;
                fitShrink = splash ? 0.95f : GameTextShadow.DefaultFitShrink;
            }
            // CLS fixed-size (non-bestFit) one-line text overflows its box with the wider font
            // and crowds neighbours: wave tags ("웨이브 4"), the rail tile artist (LevelArtist,
            // overflows into the "신규!" badge), and the detail artist (ArtistText, overflows
            // into its media icons). Autosize them down to fit instead of plain no-wrap.
            else if (InCls() && (HasAncestor(t, "waveTag")
                     || t.gameObject.name == "LevelArtist" || t.gameObject.name == "ArtistText"))
            { fit = GameTextShadow.FitMode.Width; fitShrink = 0.95f; }
            else { fit = GameTextShadow.FitMode.None; fitShrink = GameTextShadow.DefaultFitShrink; }
            // Guest-track labels bake an absolute <size=…> tag into their text that overrides
            // our scaling — strip it so the whole panel sizes consistently off our scale. For
            // mixed label+name components (not bolded wholesale) bold just the label line, and
            // add line spacing so the label and the name beneath it aren't cramped.
            GameTextShadow.Attach(t).Configure(tmpFont, style, scale, fit, fitShrink,
                stripSize: guest, boldLabelLines: guest && IsGuestLabelObject(t) && !bold,
                lineSpacing: guest ? GuestLineSpacing : 0f, noWrap: IsClsNoWrap(t));
        }

        private static void ApplyTmp(TMP_Text t)
        {
            if (t == null || _tmpFont == null || Skip(t)) return;
            /* Settled: same styling generation, and the text still carries exactly what we
               stamped. Everything below re-derives a decision that cannot have changed, so a
               warm re-sweep gets out here for two reference compares. */
            if (_origTmp.TryGetValue(t, out TmpState settled)
                && settled.Gen == _styleGen
                && ReferenceEquals(t.font, settled.Applied)
                && t.fontStyle == settled.AppliedStyle)
            {
                _sweepSettled++;
                return;
            }
            var elem = ElementWeightEntry(t);
            TmpState st;
            if (!_origTmp.TryGetValue(t, out st))
            {
                st = new TmpState
                {
                    Font = t.font, Mat = t.fontSharedMaterial,
                    Size = t.fontSize, LineSpacing = t.lineSpacing, Style = t.fontStyle,
                    AutoSize = t.enableAutoSizing, SizeMin = t.fontSizeMin, SizeMax = t.fontSizeMax,
                };
                _origTmp[t] = st;
            }
            else if (!IsOurTmpFont(t.font))
            {
                st.Font = t.font; // re-stamped since cached, see ApplyText
                st.Mat = t.fontSharedMaterial;
                _origTmp[t] = st;
            }
            // CLS "신규!" badges (TMP) must not wrap — set before the early-out below so it
            // applies even when the font is already swapped.
            if (IsClsNoWrap(t) && t.textWrappingMode != TextWrappingModes.NoWrap)
                t.textWrappingMode = TextWrappingModes.NoWrap;
            bool bold = ShouldBold(t, t.text, (st.Style & FontStyles.Bold) != 0,
                st.Font != null ? st.Font.name : null);
            bool explicitWeight = elem != null && elem.TmpFont != null;
            // TMP bold = a different font asset, but also compare style (faux-Bold flag)
            // so a flip re-applies.
            var target = explicitWeight ? elem.TmpFont : (bold && _boldTmpFont != null ? _boldTmpFont : _tmpFont);
            FontStyles desiredStyle = bold || explicitWeight ? (st.Style & ~FontStyles.Bold) : st.Style;
            if (t.font == target && t.fontStyle == desiredStyle) { Settle(t, ref st, target, desiredStyle); return; }
            if (bold) _sweepBoldCount++;
            bool editorUi = IsEditorUi(t);
            float scale = TmpScale(st.Font) * (editorUi ? 1f : UserScale);
            // Judgement popups take their own size multiplier, independent of the
            // global game-text scale.
            if (IsJudgement(t)) scale *= JudgementScale;
            if (IsStatsText(t)) ApplyStatsScale(t);
            // Original asset becomes a fallback of ours so glyphs Pretendard lacks
            // (kana, symbols) keep rendering instead of boxing.
            if (st.Font != null)
            {
                var fb = target.fallbackFontAssetTable;
                if (fb == null) target.fallbackFontAssetTable = fb = new List<TMP_FontAsset>();
                if (!fb.Contains(st.Font)) fb.Add(st.Font);
            }
            t.font = target;
            // Real Black asset replaces the faux-bold style; leaving the Bold flag set
            // would stack simulated bold on top and smudge the glyphs.
            t.fontStyle = desiredStyle;
            /* Auto-sizing TMP IGNORES fontSize and fits between fontSizeMin/Max, so short
               text balloons to Max. Scale the BOUNDS instead (TMP's best-fit analog). */
            if (st.AutoSize)
            {
                t.enableAutoSizing = true;
                t.fontSizeMin = st.SizeMin * scale;
                t.fontSizeMax = st.SizeMax * scale;
            }
            else
                t.fontSize = st.Size * scale;
            /* TMP lineSpacing is additive, in font units where ~100 = one em. Convert
               the multiplier into the extra advance it implies for the Bismuth font. */
            float emLine = 100f;
            try
            {
                if (_tmpFont.faceInfo.pointSize > 0)
                    emLine = _tmpFont.faceInfo.lineHeight / _tmpFont.faceInfo.pointSize * 100f;
            }
            catch { }
            t.lineSpacing = editorUi ? st.LineSpacing : st.LineSpacing + (UserLineSpacing - 1f) * emLine;
            Settle(t, ref st, target, desiredStyle);
        }

        // Record what this text now carries, so the next sweep can recognise it as done.
        // TmpState is a struct in a dictionary — the write-back is the point.
        private static void Settle(TMP_Text t, ref TmpState st, TMP_FontAsset applied, FontStyles style)
        {
            st.Applied = applied;
            st.AppliedStyle = style;
            st.Gen = _styleGen;
            _origTmp[t] = st;
        }

        private static void ApplyTextMesh(TextMesh t)
        {
            if (t == null || Skip(t)) return;
            var elem = ElementWeightEntry(t);
            // Decisions from the live original (its font/style stay the game's own — we
            // hide it and render a TMP shadow that auto-matches its world size).
            bool meshItalic = t.fontStyle == FontStyle.Italic || t.fontStyle == FontStyle.BoldAndItalic;
            bool bold = ShouldBold(t, t.text,
                t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic,
                t.font != null ? t.font.name : null);
            if (bold) _sweepBoldCount++;
            if (IsStatsText(t)) ApplyStatsScale(t);
            TMP_FontAsset tmpFont = elem != null && elem.TmpFont != null ? elem.TmpFont
                                  : (bold && _boldTmpFont != null ? _boldTmpFont : _tmpFont);
            bool fauxBold = bold && (elem == null || elem.TmpFont == null) && _boldTmpFont == null;
            FontStyles style = (fauxBold ? FontStyles.Bold : FontStyles.Normal)
                             | (meshItalic ? FontStyles.Italic : FontStyles.Normal);
            // Shadow matches the original's height, so only the user's scale slider applies.
            var sh = GameTextMeshShadow.Attach(t);
            if (sh != null) sh.Configure(tmpFont, style, UserScale);
        }

        /* StopMod must restore: after a hot reload the caches are gone and fresh Font
           instances make swapped text look unswapped, so it would re-cache the scaled
           state as "original" and compound across deploys. */
        internal static void RestoreAll() => Restore();

        private static void Restore()
        {
            _pending.Clear(); // abandon any in-flight incremental sweep
            // Legacy game Text and 3D TextMesh are styled via shadows — un-hide each
            // original and drop its TMP child. (Game TMP below is still swapped in place.)
            foreach (var sh in Object.FindObjectsByType<GameTextShadow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (sh != null && !sh.Owned) sh.Detach(); // leave overlay-owned (level name) alone
            foreach (var sh in Object.FindObjectsByType<GameTextMeshShadow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (sh != null) sh.Detach();
            foreach (var kv in _origTmp)
                if (kv.Key != null)
                {
                    kv.Key.font = kv.Value.Font;
                    // After the font, or set_font would stomp it back to the default again.
                    if (kv.Value.Mat != null) kv.Key.fontSharedMaterial = kv.Value.Mat;
                    kv.Key.fontSize = kv.Value.Size;
                    kv.Key.lineSpacing = kv.Value.LineSpacing;
                    kv.Key.fontStyle = kv.Value.Style;
                    if (kv.Value.AutoSize)
                    {
                        kv.Key.fontSizeMin = kv.Value.SizeMin;
                        kv.Key.fontSizeMax = kv.Value.SizeMax;
                    }
                }
            foreach (var kv in _statsOrigScale)
                if (kv.Key != null)
                {
                    kv.Key.localScale = kv.Value.Scale;
                    kv.Key.localPosition = kv.Value.Pos;
                }
            _origTmp.Clear();
            _statsOrigScale.Clear();
        }

        /* Pruning walks every cached entry and asks Unity whether the object is dead — an
           overloaded == that crosses into native per entry. With the caches now holding one
           entry per text in the scene (14k on a big level), doing that on every sweep cost
           more than the scan it precedes. Dead entries are harmless in the meantime: lookups
           miss, and DrainPending already null-checks. So prune on scene change, where the
           corpses actually appear, and occasionally otherwise as a backstop. */
        private static int _sweepsSincePrune;

        private static void PruneThrottled()
        {
            if (_sweepsSincePrune++ < 20) return;
            _sweepsSincePrune = 0;
            Prune();
        }

        internal static void PruneNow()
        {
            _sweepsSincePrune = 0;
            Prune();
        }

        // Drop entries whose components died with their scene
        private static void Prune()
        {
            PruneDict(_origTmp);
            PruneDict(_statsOrigScale);
            PruneDict(_skipCache);
        }

        private static void PruneDict<TKey, TVal>(Dictionary<TKey, TVal> dict) where TKey : Object
        {
            List<TKey> dead = null;
            foreach (var k in dict.Keys)
                if (k == null) (dead = dead ?? new List<TKey>()).Add(k);
            if (dead != null)
                foreach (var k in dead) dict.Remove(k);
        }
    }
}
