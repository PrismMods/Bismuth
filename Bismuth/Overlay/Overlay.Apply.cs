using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    public partial class Overlay
    {
        public void ApplySettings(Settings settings)
        {
            PlaceRows(settings);
            ShowOrHideElements();

            bool ovr = settings.ShowOverlay;
            if (progressRow != null)     progressRow.SetActive(ovr && settings.ShowProgress);
            if (attemptsRow != null)     attemptsRow.SetActive(ovr && settings.ShowAttempts);
            if (attemptsFullRow != null) attemptsFullRow.SetActive(ovr && settings.ShowFullAttempts);
            if (accRow != null)          accRow.SetActive(ovr && settings.ShowAcc);
            if (xaccRow != null)         xaccRow.SetActive(ovr && settings.ShowXAcc);
            if (bpmRow != null)          bpmRow.SetActive(ovr && settings.ShowBpm);
            if (tileBpmRow != null)      tileBpmRow.SetActive(ovr && settings.ShowTileBpm);
            if (timingScaleRow != null)  timingScaleRow.SetActive(ovr && settings.ShowTimingScale);
            if (judgementsRow != null)   judgementsRow.SetActive(ovr && settings.ShowJudgements);
            if (comboDisplayContainer != null)
            {
                comboDisplayContainer.gameObject.SetActive(settings.ShowComboDisplay);
                comboDisplayContainer.anchorMin = comboDisplayContainer.anchorMax =
                    new Vector2(settings.ComboDisplayX, settings.ComboDisplayAnchorY);
                comboDisplayContainer.anchoredPosition = new Vector2(0f, settings.ComboDisplayY);
                /* ComboDisplaySize fans out into text fontSize fields below instead of
                   localScale so texts re-rasterize at chosen size; scaling the container
                   would stretch the rasterized texture and blur the glyphs */
                comboDisplayContainer.localScale = Vector3.one;
                if (_comboLabelWrapper != null)
                    _comboLabelWrapper.anchoredPosition = new Vector2(0f, settings.ComboLabelY * settings.ComboDisplaySize);
            }
            if (comboDisplayLabel != null)
            {
                comboDisplayLabel.text = settings.ComboDisplayText;
                comboDisplayLabel.fontSize = Mathf.RoundToInt(ComboLabelBaseFontSize * settings.ComboLabelSize * settings.ComboDisplaySize);
            }
            if (comboDisplayValue != null)
            {
                comboDisplayValue.fontSize = Mathf.RoundToInt(ComboValueBaseFontSize * settings.ComboDisplaySize * settings.ComboCountSize);
                // Defend against leftover transient pulse scale from old localScale-based path
                comboDisplayValue.rectTransform.localScale = Vector3.one;
            }
            /* Combo shadows: count and label each have their own offset + color. Both scale
               with ComboDisplaySize so drop offset tracks fontSize-driven text size; label
               additionally scales by ComboLabelSize since its fontSize does too */
            var defaultShColor = new Color(0f, 0f, 0f, 0.5f);
            if (_comboValueShadow != null)
            {
                _comboValueShadow.Color    = settings.ComboShadowColor?.ToColor() ?? defaultShColor;
                float countShadowScale = settings.ComboDisplaySize * settings.ComboCountSize;
                _comboValueShadow.OffsetPx = new Vector2(
                    settings.ComboShadowOffsetX * countShadowScale,
                    settings.ComboShadowOffsetY * countShadowScale);
                _comboValueShadow.Apply();
            }
            if (_comboLabelShadow != null)
            {
                _comboLabelShadow.Color    = settings.ComboLabelShadowColor?.ToColor() ?? defaultShColor;
                float labelShadowScale = settings.ComboDisplaySize * settings.ComboLabelSize;
                _comboLabelShadow.OffsetPx = new Vector2(
                    settings.ComboLabelShadowOffsetX * labelShadowScale,
                    settings.ComboLabelShadowOffsetY * labelShadowScale);
                _comboLabelShadow.Apply();
            }

            /* Container scales below stay at 1. Each scale slider fans out into child
               Text fontSizes (and row LayoutElement.preferredHeight, so VLG row spacing
               tracks the new text height) so glyphs re-rasterize at the chosen size
               rather than being stretched. */
            if (timingScaleContainer != null)
            {
                timingScaleContainer.anchorMin = timingScaleContainer.anchorMax =
                    new Vector2(settings.TimingScaleX, settings.TimingScaleAnchorY);
                timingScaleContainer.anchoredPosition = new Vector2(0f, settings.TimingScaleY);
                timingScaleContainer.localScale = Vector3.one;
            }
            SetRow(timingScaleRow, timingScaleLabel, timingScaleValue, settings.TimingScaleSize);

            int judgementFs = Mathf.RoundToInt(RowBaseFontSize * settings.JudgementsSize);
            if (judgementsContainer != null)
            {
                judgementsContainer.anchorMin = judgementsContainer.anchorMax =
                    new Vector2(settings.JudgementsX, settings.JudgementsAnchorY);
                judgementsContainer.anchoredPosition = new Vector2(0f, settings.JudgementsY);
                judgementsContainer.localScale = Vector3.one;
                var hlg = judgementsContainer.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                if (hlg != null) hlg.spacing = settings.JudgementsGap;
            }
            // Judgement texts have CSF for both fits, so layout auto-adjusts from fontSize alone
            var judgementShadow = new Vector2(ShadowBaseOffset, -ShadowBaseOffset) * settings.JudgementsSize;
            if (judgementTexts != null)
                foreach (var t in judgementTexts)
                {
                    if (t == null) continue;
                    t.fontSize = judgementFs;
                    var sh = t.GetComponent<TmpShadow>();
                    if (sh != null) { sh.OffsetPx = judgementShadow; sh.Apply(); }
                }

            if (attemptsContainer != null)
            {
                var anchor = new Vector2(settings.AttemptsX, settings.AttemptsY);
                attemptsContainer.anchorMin = anchor;
                attemptsContainer.anchorMax = anchor;
                // Pivot.x picks which edge stays pinned to the anchor (so the block grows
                // inward); the VLG child alignment aligns the differently-sized rows.
                float px; TextAnchor childAlign;
                switch (settings.AttemptsAlign)
                {
                    case TextAlign.Left:  px = 0f;   childAlign = TextAnchor.MiddleLeft;   break;
                    case TextAlign.Right: px = 1f;   childAlign = TextAnchor.MiddleRight;  break;
                    default:              px = 0.5f; childAlign = TextAnchor.MiddleCenter; break;
                }
                attemptsContainer.pivot = new Vector2(px, attemptsContainer.pivot.y);
                var avlg = attemptsContainer.GetComponent<VerticalLayoutGroup>();
                if (avlg != null) avlg.childAlignment = childAlign;
            }

            if (fpsContainer != null) fpsContainer.SetActive(settings.ShowFps);

            if (leftContainer != null)
            {
                leftContainer.localScale = Vector3.one;
                var lr = (RectTransform)leftContainer;
                lr.anchorMin = lr.anchorMax = new Vector2(settings.StatusLeftX, settings.StatusLeftY);
                lr.anchoredPosition = Vector2.zero;
            }
            if (rightContainer != null)
            {
                rightContainer.localScale = Vector3.one;
                var rr = (RectTransform)rightContainer;
                rr.anchorMin = rr.anchorMax = new Vector2(settings.StatusRightX, settings.StatusRightY);
                rr.anchoredPosition = Vector2.zero;
            }
            SetRow(progressRow, progressLabel, progressValue, settings.Scale);
            SetRow(accRow,      accLabel,      accValue,      settings.Scale);
            SetRow(xaccRow,     xaccLabel,     xaccValue,     settings.Scale);
            SetRow(bpmRow,      bpmLabel,      bpmValue,      settings.Scale);
            SetRow(tileBpmRow,  tileBpmLabel,  tileBpmValue,  settings.Scale);

            /* Master shadow pass: runs after the per-part blocks above so it has the
               final offsets. Combo label/count keep their dedicated colors. */
            bool shadowOn = settings.OverlayShadowEnabled;
            var shadowColor = settings.OverlayShadowColor?.ToColor() ?? new Color(0f, 0f, 0f, 0.5f);
            foreach (var sh in GetComponentsInChildren<TmpShadow>(true))
            {
                sh.Enabled = shadowOn;
                if (sh != _comboValueShadow && sh != _comboLabelShadow) sh.Color = shadowColor;
                sh.Apply();
            }

            ApplySeparators(settings);

            /* Text/spacing changes only dirty layout for the next frame, so force a
               rebuild now so a separator edit reflows the rows while the menu is open. */
            if (leftContainer != null)         LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)leftContainer);
            if (rightContainer != null)        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)rightContainer);
            if (attemptsContainer != null)     LayoutRebuilder.ForceRebuildLayoutImmediate(attemptsContainer);
            if (timingScaleContainer != null)  LayoutRebuilder.ForceRebuildLayoutImmediate(timingScaleContainer);
        }

        /* TMP never includes trailing whitespace in text preferred width (plain or
           non-breaking), so a separator like " | " would render flush against the value.
           Visible part of separator lives in label text; trailing-space run is converted
           into HorizontalLayoutGroup.spacing sized from actual space-glyph advance at label
           current font and size */
        private void ApplySeparators(Settings settings)
        {
            string sep = StatSeparator(settings);
            int trail = 0;
            while (trail < sep.Length && sep[sep.Length - 1 - trail] == ' ') trail++;
            string visible = sep.Substring(0, sep.Length - trail);

            SetRowSeparator(progressRow, progressLabel, "Progress",  visible, trail);
            SetRowSeparator(accRow,      accLabel,      "Accuracy",  visible, trail);
            SetRowSeparator(xaccRow,     xaccLabel,     "XAccuracy", visible, trail);
            SetRowSeparator(bpmRow,      bpmLabel,      "BPM",       visible, trail);
            SetRowSeparator(tileBpmRow,  tileBpmLabel,  "TBPM",      visible, trail);

            // Fixed-format rows keep their own separators but need same trailing-space treatment
            SetRowSeparator(attemptsRow,     attemptsLabel,     "Attempts",      ":", 1);
            SetRowSeparator(attemptsFullRow, attemptsFullLabel, "Full Attempts", ":", 1);
            SetRowSeparator(timingScaleRow,  timingScaleLabel,  "TimingScale",   " -", 1);
        }

        private static void SetRowSeparator(GameObject row, TextMeshProUGUI label, string baseText,
            string visibleSep, int trailingSpaces)
        {
            if (label == null) return;
            label.text = baseText + visibleSep;
            var hlg = row != null ? row.GetComponent<HorizontalLayoutGroup>() : null;
            if (hlg != null)
                hlg.spacing = trailingSpaces > 0 ? trailingSpaces * SpaceWidth(label) : 0f;
        }

        private static float SpaceWidth(TMP_Text t)
        {
            // Interior spaces measure fine, so diff a pair of glyphs with and without one
            float w = t.GetPreferredValues("| |").x - t.GetPreferredValues("||").x;
            return w > 0f ? w : t.fontSize * 0.25f;
        }

        private const float RowBaseLayoutHeight = 27f;

        private static void SetRow(GameObject row, TextMeshProUGUI label, TextMeshProUGUI value, float scale)
        {
            int fs = Mathf.RoundToInt(RowBaseFontSize * scale);
            var shadowOffset = new Vector2(ShadowBaseOffset, -ShadowBaseOffset) * scale;
            ApplyTextScale(label, fs, shadowOffset);
            ApplyTextScale(value, fs, shadowOffset);
            if (row != null)
            {
                var le = row.GetComponent<LayoutElement>();
                if (le != null) le.preferredHeight = RowBaseLayoutHeight * scale;
            }
        }

        private static void ApplyTextScale(TextMeshProUGUI t, int fs, Vector2 shadowOffset)
        {
            if (t == null) return;
            t.fontSize = fs;
            var sh = t.GetComponent<TmpShadow>();
            if (sh != null) { sh.OffsetPx = shadowOffset; sh.Apply(); }
        }

        private void PlaceRows(Settings settings)
        {
            if (progressRow != null)  progressRow.transform.SetParent(null, false);
            if (accRow != null)       accRow.transform.SetParent(null, false);
            if (xaccRow != null)      xaccRow.transform.SetParent(null, false);
            if (bpmRow != null)       bpmRow.transform.SetParent(null, false);
            if (tileBpmRow != null)   tileBpmRow.transform.SetParent(null, false);

            Attach(progressRow,  settings.ProgressPosition);
            Attach(accRow,       settings.AccPosition);
            Attach(xaccRow,      settings.XAccPosition);
            Attach(bpmRow,       settings.BpmPosition);
            Attach(tileBpmRow,   settings.TileBpmPosition);
        }

        private void Attach(GameObject row, OverlayPosition pos)
        {
            if (row == null) return;
            row.transform.SetParent(pos == OverlayPosition.Right ? rightContainer : leftContainer, false);
        }

        public void ShowOrHideElements()
        {
            ApplyLevelNameTransform();
            var settings = MainClass.Settings;
            bool hideAll        = settings.ActiveHideAllUI;
            bool hideAutoIcon   = hideAll || settings.ActiveHideAutoplayIcon;
            bool hideNoFail     = hideAll || settings.ActiveHideNoFail;
            bool hideDifficulty = hideAll || settings.ActiveHideDifficulty;

            // RDC.noHud dereferences RDC.data; both can NRE during startup.
            try { if (RDConstants.data == null) return; }
            catch { return; }
            RDC.noHud = hideAll;

            var ctrl = scrController.instance;
            if (ctrl?.errorMeter != null && ctrl.gameworld && scnEditor.instance == null)
                ctrl.errorMeter.gameObject.SetActive(!hideAll && !settings.ActiveHideHitmeter);

            var editor = scnEditor.instance;
            if (editor != null)
            {
                if (editor.buttonNoFail != null)
                    editor.buttonNoFail.gameObject.SetActive(!hideNoFail);
                if (editor.editorDifficultySelector != null)
                    editor.editorDifficultySelector.gameObject.SetActive(!hideDifficulty);
                if (editor.autoImage != null)
                    editor.autoImage.enabled = !hideAutoIcon;
                if (editor.buttonAuto != null)
                    editor.buttonAuto.enabled = !hideAutoIcon;
            }

            var uic = scrUIController.instance;
            if (uic != null)
            {
                if (uic.noFailImage != null)      uic.noFailImage.enabled = !hideNoFail;
                if (uic.difficultyImage != null)   uic.difficultyImage.enabled = !hideDifficulty;
                /* Gameplay HUD difficulty widget leaks into editor when a level is loaded via
                   the custom-levels-menu editor button; force-hide there */
                bool inEditor = scnEditor.instance != null;
                if (uic.difficultyContainer != null)
                {
                    bool show = !hideDifficulty && !inEditor && uic.difficultyUIMode != DifficultyUIMode.DontShow;
                    if (uic.difficultyContainer.gameObject.activeSelf != show)
                        uic.difficultyContainer.gameObject.SetActive(show);
                }
                if (uic.difficultyFadeContainer != null)
                {
                    bool show = !hideDifficulty && !inEditor && uic.difficultyUIMode != DifficultyUIMode.DontShow;
                    if (uic.difficultyFadeContainer.gameObject.activeSelf != show)
                        uic.difficultyFadeContainer.gameObject.SetActive(show);
                }

                if (hideDifficulty)
                {
                    var indicators = Object.FindObjectsByType<DifficultyIndicator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var ind in indicators)
                        if (ind != null && ind.gameObject.activeSelf)
                            ind.gameObject.SetActive(false);
                }

                if (hideAll || settings.ActiveHideBetaBuild)
                {
                    var betas = Object.FindObjectsByType<scrEnableIfBeta>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var b in betas)
                        if (b != null && b.gameObject.activeSelf)
                            b.gameObject.SetActive(false);
                }
                if (uic.txtLevelName != null)
                    uic.txtLevelName.gameObject.SetActive(!hideAll && !settings.ActiveHideLevelName);
            }
        }

        public void ApplyLevelNameTransform()
        {
            var ctrl = scrController.instance;
            if (ctrl?.txtLevelName == null) return;
            var settings = MainClass.Settings;
            var rt = ctrl.txtLevelName.rectTransform;
            if (!_levelNameOrigPos.HasValue)
                _levelNameOrigPos = rt.anchoredPosition;
            /* Restore any stale fontSize left over from when this used fontSize-based path:
               its Shadow/Outline effectDistance is in raw pixels and doesn't track fontSize, so
               a shrunken fontSize misalignment-ghosted glyphs against the unchanged shadow */
            if (_levelNameOrigFontSize.HasValue && ctrl.txtLevelName.fontSize != _levelNameOrigFontSize.Value)
                ctrl.txtLevelName.fontSize = _levelNameOrigFontSize.Value;
            /* localScale scales the whole subtree (glyphs + Shadow + Outline) uniformly, so
               drop-shadow offset stays correct. Default use is shrinking (0.5×), where
               downsampled glyphs from the original raster look cleaner than re-rasterising the
               dynamic font at a smaller size anyway */
            rt.localScale = Vector3.one * settings.LevelNameScale;
            rt.anchoredPosition = _levelNameOrigPos.Value + new Vector2(settings.LevelNameX, settings.LevelNameY);
            /* Keep level name on one line. Game appends speed-trial multiplier ("… (1.1배)")
               to this same Text; Pretendard is wider than stock font, so it wrapped where
               vanilla didn't and the wrapped line overlapped the first. Label is meant to be
               single-line */
            ctrl.txtLevelName.horizontalOverflow = HorizontalWrapMode.Overflow;
            ctrl.txtLevelName.verticalOverflow = VerticalWrapMode.Overflow;

            /* Render the level name in our TMP font via a GameTextShadow (hides the legacy
               original, draws a TMP child). The shadow's alpha-0 hide also suppresses the
               game's own Shadow/Outline, so no effect juggling is needed — we hand the TMP an
               explicit Bismuth drop shadow. owned:true keeps GameFontApplier's sweep/Restore
               from detaching it. */
            bool styled = settings.LevelNameUseOverlayFont && _levelNameFont != null;
            if (styled)
            {
                var sh = GameTextShadow.Attach(ctrl.txtLevelName, owned: true);
                // collapseNewlines keeps the speed-trial multiplier ("…\n(1.1배)") inline.
                sh.Configure(_levelNameFont, FontStyles.Normal, 1f, collapseNewlines: true);
                // Offset divided by LevelNameScale: localScale shrinks the whole subtree, so
                // this lands at the same ~2px screen offset as the overlay rows.
                float inv = settings.LevelNameScale > 0.01f ? 1f / settings.LevelNameScale : 1f;
                var col = settings.OverlayShadowColor?.ToColor() ?? new Color(0f, 0f, 0f, 0.5f);
                sh.SetShadow(settings.OverlayShadowEnabled, col,
                    new Vector2(ShadowBaseOffset, -ShadowBaseOffset) * inv);
            }
            else
            {
                // Drop the shadow so the original shows with the game's own font + effects.
                var sh = ctrl.txtLevelName.GetComponent<GameTextShadow>();
                if (sh != null) sh.Detach();
            }

            ctrl.txtLevelName.gameObject.SetActive(!settings.ActiveHideAllUI && !settings.ActiveHideLevelName);
        }

        internal void SetLevelNameFont(TMP_FontAsset font)
        {
            _levelNameFont = font;
            ApplyLevelNameTransform();
        }
    }
}
