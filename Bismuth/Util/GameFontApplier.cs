using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    /* Repaints the game's own text with the overlay font (opt-in via
       Settings.GameTextUseOverlayFont). Covers all three text systems: legacy uGUI
       Text, TextMeshPro, and 3D TextMesh. Originals are cached so toggling off
       restores live objects. Pretendard renders larger per em than the game fonts,
       so every swap also scales size by the line-height/em ratio (fallback 0.85). */
    internal static class GameFontApplier
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

        /* Game titles (world number/name on level select…) are bold in the stock
           fonts, so match them with the family's heaviest weight. scrHUDText.isTitle
           alone misses some; the strongest extra signal is the original font being a
           bold variant. */
        private static bool IsTitle(Component c)
        {
            try
            {
                var hud = c.GetComponent<scrHUDText>();
                if (hud != null && hud.isTitle) return true;
                // The credits block ("by 7th Beat Games") is title-weight display text.
                return c.GetComponentInParent<scrCreditsText>() != null;
            }
            catch { return false; }
        }

        // The title-screen credits ("by 7th Beat Games") — display text that should stay on
        // one line and shrink to fit its rect rather than wrap (Pretendard renders wider).
        private static bool IsCredits(Component c)
        {
            try { return c.GetComponentInParent<scrCreditsText>() != null; }
            catch { return false; }
        }

        // Guest-track credit decorations (GuestTrackWorld…: "level design by", "music by", …)
        // carry a huge native fontSize in a big world-space box, so they wrap + autosize to
        // stay inside that box (FitMode.Box) instead of overflowing when the panel scales up.
        private static bool InGuestTrackCredit(Component c)
        {
            for (var p = c.transform; p != null; p = p.parent)
                if (p.name.StartsWith("GuestTrack")) return true;
            return false;
        }

        // The ">< check out <other game> <" cross-promo under a guest track (object
        // "checkItOut!"). Oversized blue vanilla display text — render it smaller + unbolded.
        private static bool IsCollabTag(Component c)
        {
            return c.gameObject.name.StartsWith("checkItOut");
        }

        // Extra TMP line advance between a guest-track label and the name beneath it (the
        // combined "label\nname" components render cramped at default spacing).
        private const float GuestLineSpacing = 32f;
        // When a name is a SEPARATE component below its label, push it down this fraction of
        // its own font size so it isn't cramped against the label.
        private const float GuestNameGap = 0.2f;

        /* A guest-track artist NAME that sits below a SEPARATE label element (its parent IS
           that label, e.g. guestLevelDesign/Rikri — not the Canvas/artist root and not the
           track root). These render cramped under the label, so nudge them down. Combined
           label+name components (parent Canvas) handle spacing via TMP lineSpacing instead. */
        private static bool IsGuestName(Component c)
        {
            if (!InGuestTrackCredit(c) || IsCollabTag(c) || IsGuestLabelObject(c)) return false;
            var p = c.transform.parent;
            return p != null && GuestLabelNames.Contains(p.name);
        }

        // Shift a transform in its local space (position only; cached in the stats table so
        // Restore resets it). Idempotent — always offsets from the cached original position.
        private static void OffsetLocal(Transform tr, float dx, float dy)
        {
            if (tr == null) return;
            XformState st;
            if (!_statsOrigScale.TryGetValue(tr, out st))
            {
                st = new XformState { Scale = tr.localScale, Pos = tr.localPosition };
                _statsOrigScale[tr] = st;
            }
            tr.localPosition = new Vector3(st.Pos.x + dx, st.Pos.y + dy, st.Pos.z);
        }

        private static void OffsetNameDown(Transform tr, float localDown) => OffsetLocal(tr, 0f, -localDown);

        /* Object-names of guest-track ROLE LABEL elements (the artist NAMES are their
           children). The label TEXT isn't a reliable signal — some labels have no colon
           ("레벨 시각 효과") — so bold is decided by which element holds the text, not its
           wording. A label element holding a combined "label\nname" string can't bold
           wholesale; the shadow bolds its label line per-line instead. */
        private static readonly System.Collections.Generic.HashSet<string> GuestLabelNames =
            new System.Collections.Generic.HashSet<string>
            {
                "guestLevelDesign", "vfxDesign", "guestTrackBy", "tutorialMusicBy",
                "guestArtBy", "guestVFX", "guestTutorialDesign", "specialThanks", "guestTwemoji",
            };

        private static bool IsGuestLabelObject(Component c) => GuestLabelNames.Contains(c.gameObject.name);

        /* Custom-level-select text that should never wrap with the wider font: the rail tiles'
           name/artist (CustomLevelTile), the description's artist line (portalArtist), and the
           "신규!" badges (scrBadgeContainer; badge text is TMP, so this is checked on both the
           shadow and in-place TMP paths). Gated on the CLS scene so it's a no-op elsewhere. */
        private static bool IsClsNoWrap(Component c)
        {
            if (!InCls()) return false;
            try
            {
                if (c.GetComponentInParent<CustomLevelTile>(true) != null) return true;
                if (c.GetComponentInParent<scrBadgeContainer>(true) != null) return true;
                // CLS hub portal/sign names + wave tags (WorldNameCanvas/NameText, waveTag/text):
                // their label/sub lines ("라이브러리\n항목 22개", "추천\n클래식") wrap an extra
                // line with the wider font.
                if (HasAncestor(c, "WorldNameCanvas")) return true;
                var cls = scnCLS.instance;
                if (cls != null && (ReferenceEquals(c, cls.portalArtist) || ReferenceEquals(c, cls.portalName)))
                    return true;
            }
            catch { }
            return false;
        }

        // Title/splash "by 7th Beat Games" (Phase 0/…/7thBeatGames/7th Beat Games Text) —
        // display credit that should stay on one line and fit its box, not wrap.
        private static bool IsSplashCredit(Component c)
        {
            for (var p = c.transform; p != null; p = p.parent)
                if (p.name == "7thBeatGames") return true;
            return false;
        }

        // Portal credits around a world portal in level select (PortalCredit.titleText /
        // peopleText: "객원 레벨 디자인:" + the artist names). Fixed legacy fontSize in a
        // box tuned for the game font, so Pretendard overflows — wrap + autosize to fit.
        private static bool InPortalCredit(Component c)
        {
            try { return c.GetComponentInParent<PortalCredit>(true) != null; }
            catch { return false; }
        }

        private static bool NameLooksBold(string fontName)
        {
            if (string.IsNullOrEmpty(fontName)) return false;
            var n = fontName.ToLowerInvariant();
            return n.Contains("bold") || n.Contains("black") || n.Contains("heavy") ||
                   n.EndsWith("-bd") || n.Contains("_bd") || n.Contains(" bd");
        }

        private static int _sweepBoldCount;

        /* On the level-select / title screen? World content is parented under
           DontDestroyOnLoad, so a component's OWN scene isn't "scnLevelSelect" — gate
           on the ACTIVE scene so they're title-styled only while the menu shows. */
        private static bool InLevelSelect()
        {
            try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "scnLevelSelect"; }
            catch { return false; }
        }

        /* The title screen is all bold-looking display text in vanilla, but no
           per-component signal (isTitle, fontStyle, font name) reliably fires there, so
           while the menu shows this verdict is AUTHORITATIVE (caller ignores
           NameLooksBold/style). Bold everything except news sign + tip cycler (body
           copy), stats panels, and single glyphs (keycaps); credits go through IsTitle. */
        private static bool LevelSelectBold(Component c, string content)
        {
            try
            {
                if (c.GetComponentInParent<NewsSign>() != null) return false;
                /* The "Hit Space" hint cluster holds cycling body-copy tips. NOTE:
                   portal labels and the "by 7th Beat Games" subtitle are ALSO
                   scrTextChanger but sit outside this group, so exclude by cluster, not
                   by component type. */
                if (InHintCluster(c)) return false;
                if (content == null || content.Trim().Length <= 1) return false;
                return !IsStatsText(c);
            }
            catch { return false; }
        }

        private static bool InHintCluster(Component c)
        {
            var p = c.transform;
            for (int i = 0; i < 6 && p != null; i++, p = p.parent)
                if (p.name == "Hit Space") return true;
            return false;
        }

        private static bool InCls()
        {
            try { return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "scnCLS"; }
            catch { return false; }
        }

        /* Custom-level-select chrome to bold. Unlike the title screen, DON'T bold the
           whole scene (full of body copy) — only the screen title, portal/world-name
           labels, and the loading text. */
        private static bool ClsBold(Component c)
        {
            // Selected level title in detail view
            try { var cls = scnCLS.instance; if (cls != null && ReferenceEquals(c, cls.portalName)) return true; }
            catch { }
            // Difficulty name ("엄격"/"느슨"…); its sibling txtDescription stays body copy.
            if (c.gameObject.name == "txtValue" && HasAncestor(c, "Difficulty Container")) return true;
            for (var p = c.transform; p != null; p = p.parent)
            {
                var n = p.name;
                if (n == "WorldNameCanvas" || n == "title" || n == "Loading") return true;
            }
            return false;
        }

        // Transform downscale + line spacing for CLS portal labels
        private const float ClsLabelScale = 0.8f;
        private const float ClsLabelLineSpacing = 1.1f;
        private const float ClsSignScale = 1.6f;   // chain-banner level sign ("레벨"); tunable

        private static bool HasAncestor(Component c, string name)
        {
            for (var p = c.transform; p != null; p = p.parent)
                if (p.name == name) return true;
            return false;
        }

        /* Single bold decision for all three text systems. Authoritative per scene:
           the title screen bolds nearly everything (LevelSelectBold), CLS bolds only
           its chrome (ClsBold), and elsewhere falls back to the per-component
           heuristic. */
        private static bool ShouldBold(Component c, string text, bool styleBold, string origFontName)
        {
            if (ForceRegular(c)) return false;
            // Editor-scene text (tile-direction overlay, form panels) stays vanilla weight
            // — no scene/title rule should bold it.
            if (IsEditorUi(c)) return false;
            /* The settings submenu (child of PauseMenu) keeps its designed weight
               EVERYWHERE — except its tab/section headings ("일반"/general…, the
               "border/title" leaf), which read as titles and get bolded. Checked first
               so no scene rule touches the rest of the submenu. */
            if (HasAncestor(c, "SettingsMenu"))
            {
                if (c.gameObject.name == "title") return true;
                return styleBold || NameLooksBold(origFontName);
            }
            // Guest-track text mixes role LABELS and artist NAMES. A component holding ONLY a
            // label (no name) bolds wholesale at true weight; a component that also holds a
            // name stays regular here and the shadow bolds just the label LINE via <b>.
            if (InGuestTrackCredit(c))
            {
                // Bold the role-label elements; artist-name children stay regular. A combined
                // label+name element (one component holding a newline) can't bold wholesale —
                // the shadow bolds just its label line via <b>.
                if (!IsGuestLabelObject(c)) return false;
                return text == null || text.IndexOf('\n') < 0;
            }
            // Portal credits around a world portal: bold the role label (PortalCredit.titleText,
            // "객원 시각 디자인:") and leave the artist name (peopleText) regular.
            if (InPortalCredit(c))
            {
                try { var pc = c.GetComponentInParent<PortalCredit>(true); return pc != null && ReferenceEquals(c, pc.titleText); }
                catch { return false; }
            }
            if (IsTitle(c)) return true;
            // Official level name/description ("World Description", e.g. "The Wind Up" /
            // "유턴과 구불거리는 길") — bold regardless of which menu scene shows it.
            if (HasAncestor(c, "World Description")) return true;
            // Pause menu: bold all its (non-settings) text. includeInactive: may be
            // inactive during a full sweep.
            try { if (c.GetComponentInParent<PauseMenu>(true) != null) return true; }
            catch { }
            if (InLevelSelect()) return LevelSelectBold(c, text);
            if (InCls()) return ClsBold(c);
            return styleBold || NameLooksBold(origFontName);
        }

        /* Per-element weight table (Game UI tab, Element weights): weight name to the
           font entry of the game-font family. Resolved in MainClass.ApplySelectedFont. */
        private static Dictionary<string, FontLoader.FontEntry> _elementWeights;

        internal static void SetElementWeights(Dictionary<string, FontLoader.FontEntry> table)
        {
            _elementWeights = table;
            _styleGen++;
        }

        // Explicit weight chosen for HUD element this component belongs to, or null
        private static FontLoader.FontEntry ElementWeightEntry(Component c)
        {
            var s = MainClass.Settings;
            if (s == null || s.GameUiTextWeights == null || s.GameUiTextWeights.Count == 0) return null;
            if (_elementWeights == null || _elementWeights.Count == 0) return null;
            string key = GameUiLayout.OwnerKey(c);
            /* Judgement popups (Perfect/EPerfect…) are pooled world-space TMP under
               scrHitTextMesh, not GameUiLayout targets, so they get a synthetic key.
               includeInactive is REQUIRED: popups are pooled and inactive at both sweep
               and Show-prefix time, and the no-arg lookup skips inactive GameObjects, so
               the weight would silently never resolve. */
            if (key == null && IsJudgement(c))
                key = "judgement";
            if (key == null) return null;
            string w = s.GameUiWeightFor(key);
            if (string.IsNullOrEmpty(w)) return null;
            FontLoader.FontEntry e;
            return _elementWeights.TryGetValue(w, out e) ? e : null;
        }

        /* Hit-judgement popups (pooled scrHitTextMesh TMP). includeInactive because
           pooled popups are inactive at sweep / Show-prefix time. */
        private static bool IsJudgement(Component c)
        {
            try { return c is TMP_Text && c.GetComponentInParent<scrHitTextMesh>(true) != null; }
            catch { return false; }
        }

        // Dedicated size multiplier for judgement popups (Game UI tab)
        private static float JudgementScale =>
            MainClass.Settings != null ? Mathf.Clamp(MainClass.Settings.GameJudgementScale, 0.3f, 4f) : 1f;

        /* "Is this a font Bismuth assigned?" Recognizes the game re-stamping a
           localized font onto an already-swapped component. Covers regular, bold, and
           every per-element weight font. */
        private static bool IsOurTmpFont(TMP_FontAsset f)
        {
            if (f == null) return false;
            if (f == _tmpFont || f == _boldTmpFont) return true;
            if (_elementWeights != null)
                foreach (var kv in _elementWeights)
                    if (kv.Value != null && kv.Value.TmpFont == f) return true;
            return false;
        }

        /* Keycap letters (scrLetterPress) sit on small key sprites that the family's
           Black weight overwhelms. Overrides every bold signal. */
        /* Version Text components a scrVersionText drives (PauseMenu.versionText, main menu).
           The Text is *referenced* by the component, not always parented under it, so resolve
           them by reference each full sweep (FindObjectsByType Include catches the inactive
           pause menu, so there's no first-open flash). */
        private static readonly HashSet<Component> _versionTexts = new HashSet<Component>();

        private static void RefreshVersionTexts()
        {
            _versionTexts.Clear();
            try
            {
                foreach (var v in Object.FindObjectsByType<scrVersionText>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (v != null && v.text != null) _versionTexts.Add(v.text);
            }
            catch { }
        }

        private static bool ForceRegular(Component c)
        {
            try
            {
                if (c.GetComponentInParent<scrLetterPress>() != null) return true;
                // Speed-trial best-multiplier badge ("1.5배"): small accent, not title.
                if (c.GetComponentInParent<scrBestMultiplierText>() != null) return true;
                // Version string stays regular weight, incl. the pause-menu version.
                if (_versionTexts.Contains(c) || c.GetComponentInParent<scrVersionText>() != null) return true;
                return false;
            }
            catch { return false; }
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
            if (Enabled && _tmpFont != null) ApplyTmp(t);
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

        /* Level editor form panels are dense, hand-fitted UI that user size/leading
           tweaks wreck (and some labels auto-fit, so a global shrink lands unevenly).
           Editor-scene text keeps metric normalization only: vanilla, just our font. */
        private static bool IsEditorUi(Component c)
        {
            try
            {
                if (c.gameObject.scene.name != "scnEditor") return false;
                // The autoplay controls-tip (pan/stop-autoplay help, new in 3.3.0) is a
                // HUD-style overlay, not a hand-fitted editor form panel, so it takes the
                // normal game-text downscale like gameplay HUD text.
                try { if (ReferenceEquals(c, scnEditor.instance?.controlsTip)) return false; } catch { }
                return true;
            }
            catch { return false; }
        }

        /* Skip() is the sweep's dominant cost: it walks the ancestor chain twice (foreign-mod
           assemblies, then scrDecoration), for every text, on every sweep. A big custom level
           is mostly decoration text, so that work is spent re-reaching the same "not ours"
           verdict thousands of times.

           The verdict depends on where a component SITS in the hierarchy, not on any font or
           setting, so it survives re-sweeps, font changes and Restore(). Pruned with the rest
           when components die. Caveat: a component reparented after its first sweep keeps the
           old verdict — the pooled text that does move (judgement popups) comes through
           ApplyTo(GameObject) with the same prefab shape either way. */
        private static readonly Dictionary<Component, bool> _skipCache = new Dictionary<Component, bool>();

        private static bool Skip(Component c)
        {
            if (_skipCache.TryGetValue(c, out bool cached)) { _sweepSkipHits++; return cached; }
            bool verdict = SkipUncached(c);
            _skipCache[c] = verdict;
            return verdict;
        }

        private static bool SkipUncached(Component c)
        {
            // Our own shadow render child (the TMP we draw under each styled game Text).
            if (c.gameObject.name == GameTextShadow.ChildName) return true;
            /* Bismuth's own canvases manage their fonts themselves, and txtLevelName
               has its dedicated swap/restore in ApplyLevelNameTransform. Check BOTH
               owner references: scrController.instance can be unset during the
               level-select scene sweep, which let txtLevelName slip through and get
               full-size swapped and scene-bolded ("8-X Jungle City" rendered huge). */
            var root = c.transform.root;
            if (root != null && root.name.StartsWith("Bismuth")) return true;
            // Other mods' HUDs (Sapphire's editor chrome, TUFHelper's PP displayer, …) are
            // theirs to style — leave them on their own fonts. Root name first: it's cheap,
            // and some foreign labels are plain TMP under a bare canvas with no mod
            // component anywhere in their chain, invisible to the assembly walk.
            if (IsOtherModRoot(root) || IsForeignModUi(c)) return true;
            // In-level text decorations (scrDecoration) are styled by the mapper — their
            // FontName and size are part of the chart, so leave them untouched.
            try { if (c.GetComponentInParent<scrDecoration>(true) != null) return true; }
            catch { }
            try { if (ReferenceEquals(c, scrController.instance?.txtLevelName)) return true; }
            catch { }
            try { if (ReferenceEquals(c, scrUIController.instance?.txtLevelName)) return true; }
            catch { }
            return false;
        }

        /* Text owned by another mod's UI: a mod's HUD carries its own MonoBehaviours
           (loaded from Mods//UMMMods/), whereas game text only carries Assembly-CSharp /
           engine scripts. If any ancestor is defined in a foreign mod assembly, the
           hierarchy is that mod's and we leave its fonts alone. Per-assembly verdict is
           cached, so after warmup this is a parent walk + dictionary lookups. */
        private static readonly List<MonoBehaviour> _mbBuf = new List<MonoBehaviour>();
        private static readonly Dictionary<System.Reflection.Assembly, bool> _foreignAsm =
            new Dictionary<System.Reflection.Assembly, bool>();
        private static System.Reflection.Assembly _gameAsm, _bismuthAsm;

        /* Mods conventionally name their own canvases after themselves ("SapphireToolbar",
           "SapphireEditorEvents", "BismuthUI" …), so a hierarchy whose ROOT starts with
           another loaded mod's Id is that mod's UI. This is the only signal on foreign
           labels that carry no mod MonoBehaviour in their ancestor chain (a plain TMP
           straight under a bare canvas). Prefixes come from the UMM registry — no
           hardcoded mod list; refreshed each full sweep so late-installed mods count. */
        private static string[] _modRootPrefixes;

        internal static void RefreshModRootPrefixes() => _modRootPrefixes = null;

        private static bool IsOtherModRoot(Transform root)
        {
            if (root == null) return false;
            if (_modRootPrefixes == null) _modRootPrefixes = BuildModRootPrefixes();
            string n = root.name;
            for (int i = 0; i < _modRootPrefixes.Length; i++)
                if (n.StartsWith(_modRootPrefixes[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string[] BuildModRootPrefixes()
        {
            var list = new List<string>();
            try
            {
                foreach (var m in UnityModManagerNet.UnityModManager.modEntries)
                {
                    string id = m?.Info?.Id;
                    // ≥4 chars so a terse Id can't shadow ordinary scene-object names.
                    if (string.IsNullOrEmpty(id) || id.Length < 4 || id == "Bismuth") continue;
                    list.Add(id);
                }
            }
            catch { }
            // Registry unreadable (UMMCompat quirk) → at least protect the sister mod.
            if (list.Count == 0) list.Add("Sapphire");
            return list.ToArray();
        }

        private static bool IsForeignModUi(Component c)
        {
            try
            {
                for (var p = c.transform; p != null; p = p.parent)
                {
                    p.GetComponents(_mbBuf);
                    for (int i = 0; i < _mbBuf.Count; i++)
                    {
                        var b = _mbBuf[i];
                        if (b != null && IsForeignAssembly(b.GetType().Assembly)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool IsForeignAssembly(System.Reflection.Assembly asm)
        {
            if (asm == null) return false;
            bool verdict;
            if (_foreignAsm.TryGetValue(asm, out verdict)) return verdict;
            verdict = ComputeForeign(asm);
            _foreignAsm[asm] = verdict;
            return verdict;
        }

        private static bool ComputeForeign(System.Reflection.Assembly asm)
        {
            if (_bismuthAsm == null) _bismuthAsm = typeof(GameFontApplier).Assembly;
            if (_gameAsm == null) { try { _gameAsm = typeof(scrController).Assembly; } catch { } }
            if (asm == _bismuthAsm || asm == _gameAsm) return false;
            var n = asm.GetName().Name;
            // Engine/runtime/mod-loader assemblies aren't "another mod's HUD".
            if (n == "Assembly-CSharp-firstpass" || n == "UnityModManager" ||
                n == "mscorlib" || n == "netstandard" ||
                n.StartsWith("UnityEngine") || n.StartsWith("Unity.") ||
                n.StartsWith("System") || n.StartsWith("Mono.") ||
                n.StartsWith("Microsoft") || n.StartsWith("0Harmony") || n.StartsWith("MonoMod"))
                return false;
            /* Beyond the whitelist it's a game dependency shipped in Managed/ (DOTween,
               Rewired…) or a mod (loaded from Mods//UMMMods/). A blank Location means an
               in-memory load, which for a non-engine assembly is a mod. */
            string loc = null;
            try { loc = asm.Location; } catch { }
            if (string.IsNullOrEmpty(loc)) return true;
            return loc.Replace('\\', '/').IndexOf("/Managed/", System.StringComparison.OrdinalIgnoreCase) < 0;
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
