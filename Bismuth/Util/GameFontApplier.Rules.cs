using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    // Per-element styling decisions: which game text is bold, which weight/fit it gets.
    internal static partial class GameFontApplier
    {
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
    }
}
