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
                // ComboDisplaySize fans out into the text fontSize fields below instead of
                // localScale so the texts re-rasterize at the chosen size — scaling the
                // container would stretch the rasterized texture and blur the glyphs.
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
                // Defend against leftover transient pulse scale from the old localScale-based path.
                comboDisplayValue.rectTransform.localScale = Vector3.one;
            }
            // Combo shadows — count and label each have their own offset + color. Both scale
            // with ComboDisplaySize so the drop offset tracks the fontSize-driven text size;
            // the label additionally scales by ComboLabelSize since its fontSize does too.
            var defaultShColor = new Color(0f, 0f, 0f, 0.5f);
            if (_comboValueShadow != null)
            {
                _comboValueShadow.effectColor    = settings.ComboShadowColor?.ToColor() ?? defaultShColor;
                float countShadowScale = settings.ComboDisplaySize * settings.ComboCountSize;
                _comboValueShadow.effectDistance = new Vector2(
                    settings.ComboShadowOffsetX * countShadowScale,
                    settings.ComboShadowOffsetY * countShadowScale);
            }
            if (_comboLabelShadow != null)
            {
                _comboLabelShadow.effectColor    = settings.ComboLabelShadowColor?.ToColor() ?? defaultShColor;
                float labelShadowScale = settings.ComboDisplaySize * settings.ComboLabelSize;
                _comboLabelShadow.effectDistance = new Vector2(
                    settings.ComboLabelShadowOffsetX * labelShadowScale,
                    settings.ComboLabelShadowOffsetY * labelShadowScale);
            }

            // Container scales below stay at 1; each scale slider fans out into the child Text
            // fontSizes (and the row's LayoutElement.preferredHeight, so VLG row spacing tracks
            // the new text height) so glyphs re-rasterize at the chosen size rather than being
            // stretched.
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
            // Judgement texts have CSF for both fits, so layout auto-adjusts from fontSize alone.
            var judgementShadow = new Vector2(ShadowBaseOffset, -ShadowBaseOffset) * settings.JudgementsSize;
            if (judgementTexts != null)
                foreach (var t in judgementTexts)
                {
                    if (t == null) continue;
                    t.fontSize = judgementFs;
                    var sh = t.GetComponent<Shadow>();
                    if (sh != null) sh.effectDistance = judgementShadow;
                }

            if (attemptsContainer != null)
            {
                var anchor = new Vector2(settings.AttemptsX, settings.AttemptsY);
                attemptsContainer.anchorMin = anchor;
                attemptsContainer.anchorMax = anchor;
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
        }

        private const float RowBaseLayoutHeight = 30f;

        private static void SetRow(GameObject row, Text label, Text value, float scale)
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

        private static void ApplyTextScale(Text t, int fs, Vector2 shadowOffset)
        {
            if (t == null) return;
            t.fontSize = fs;
            var sh = t.GetComponent<Shadow>();
            if (sh != null) sh.effectDistance = shadowOffset;
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
                // The gameplay HUD's difficulty widget leaks into the editor when a level is
                // loaded via the custom-levels-menu editor button; force-hide there.
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
            // Restore any stale fontSize left over from when this used the fontSize-based path —
            // its Shadow/Outline effectDistance is in raw pixels and doesn't track fontSize, so
            // a shrunken fontSize misalignment-ghosted the glyphs against the unchanged shadow.
            if (_levelNameOrigFontSize.HasValue && ctrl.txtLevelName.fontSize != _levelNameOrigFontSize.Value)
                ctrl.txtLevelName.fontSize = _levelNameOrigFontSize.Value;
            // localScale scales the whole subtree (glyphs + Shadow + Outline) uniformly, so
            // the drop-shadow offset stays correct. The default use is shrinking (0.5×), where
            // downsampled glyphs from the original raster look cleaner than re-rasterising the
            // dynamic font at a smaller size anyway.
            rt.localScale = Vector3.one * settings.LevelNameScale;
            rt.anchoredPosition = _levelNameOrigPos.Value + new Vector2(0f, settings.LevelNameY);
            ctrl.txtLevelName.gameObject.SetActive(!settings.ActiveHideAllUI && !settings.ActiveHideLevelName);
        }
    }
}
