using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    public partial class Overlay
    {
        private void BuildUI()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            ConfigureScaler(canvasGo.AddComponent<CanvasScaler>());

            leftContainer      = MakeContainer(canvasGo, "LeftContainer",  OverlayPosition.Left);
            rightContainer     = MakeContainer(canvasGo, "RightContainer", OverlayPosition.Right);
            timingScaleContainer    = MakeTimingScaleContainer(canvasGo);
            judgementsContainer = MakeJudgementsContainer(canvasGo);
            attemptsContainer  = MakeAttemptsContainer(canvasGo);

            (progressRow,    progressLabel,    progressValue)    = MakeRow("Progress",    "Progress | ");
            (attemptsRow,    attemptsLabel,    attemptsValue)    = MakeRow("Attempts",    "Attempts: ");
            attemptsLabel.fontSize = 18;
            attemptsValue.fontSize = 18;
            attemptsRow.transform.SetParent(attemptsContainer, false);
            (attemptsFullRow, attemptsFullLabel, attemptsFullValue) = MakeRow("AttemptsFull", "Full Attempts: ");
            attemptsFullLabel.fontSize = 18;
            attemptsFullValue.fontSize = 18;
            attemptsFullRow.transform.SetParent(attemptsContainer, false);
            // Best % parents to a side panel or the attempts block per settings —
            // PlaceRows decides, ApplySettings styles it for whichever home it's in.
            (bestRow, bestLabel, bestValue) = MakeRow("Best", "Best | ");
            (accRow,         accLabel,         accValue)         = MakeRow("Acc",         "Accuracy | ");
            (xaccRow,        xaccLabel,        xaccValue)        = MakeRow("XAcc",        "XAccuracy | ");
            (bpmRow,         bpmLabel,         bpmValue)         = MakeRow("Bpm",         "BPM | ");
            (tileBpmRow,     tileBpmLabel,     tileBpmValue)     = MakeRow("TileBpm",     "TBPM | ");
            (kpsRow,         kpsLabel,         kpsValue)         = MakeRow("Kps",         "KPS | ");
            (songDurRow,     songDurLabel,     songDurValue)     = MakeRow("SongDur",     "Song Length | ");
            (levelDurRow,    levelDurLabel,    levelDurValue)    = MakeRow("LevelDur",    "Level Length | ");
            (timingScaleRow, timingScaleLabel, timingScaleValue) = MakeRow("TimingScale", "TimingScale - ");
            timingScaleRow.transform.SetParent(timingScaleContainer, false);

            progressBarGo = MakeProgressBar(canvasGo, out progressBarFill, out progressBarFillImg);
            judgementsRow = MakeJudgementsRow(judgementsContainer.gameObject, out judgementTexts);
            comboDisplayContainer = MakeComboDisplay(canvasGo, out comboDisplayLabel, out comboDisplayValue, out _comboLabelWrapper);
            (fpsContainer, fpsText) = MakeFpsDisplay();

            canvas.gameObject.SetActive(false);
            ShowEmpty();
        }

        private static Transform MakeContainer(GameObject canvasGo, string name, OverlayPosition pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)go.transform;
            if (pos == OverlayPosition.Right)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-10f, -10f);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(10f, -10f);
            }

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = pos == OverlayPosition.Right ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = false;
            vlg.spacing = 4f;
            vlg.padding = pos == OverlayPosition.Right
                ? new RectOffset(0, 8, 8, 8)
                : new RectOffset(8, 0, 8, 8);

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            return go.transform;
        }

        private static RectTransform MakeTimingScaleContainer(GameObject canvasGo)
        {
            var go = new GameObject("TimingScaleContainer", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.12f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 100f);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.LowerCenter;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = false;
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(8, 8, 8, 8);

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            return rect;
        }

        private static RectTransform MakeJudgementsContainer(GameObject canvasGo)
        {
            var go = new GameObject("JudgementsContainer", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 8f);

            // HLG + CSF on the container itself — no child wrapper needed.
            // CSF reads the HLG's preferred width directly, so centering is exact.
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.spacing = 12f;
            hlg.padding = new RectOffset(8, 8, 4, 4);

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return rect;
        }

        private static RectTransform MakeAttemptsContainer(GameObject canvasGo)
        {
            var go = new GameObject("AttemptsContainer", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.75f, 0.1f);
            // Match the settings default (AttemptsAlign = Left): pivot picks the pinned
            // edge, childAlignment aligns the rows. ApplySettings re-derives both, but
            // starting from Center left the block center-rendered in any window where the
            // attempts pass hadn't run yet.
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.MiddleLeft;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = false;
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(8, 8, 4, 4);

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            return rect;
        }

        // Barebones progress bar: full-width strip flush with the top edge; a left-anchored
        // fill whose anchorMax.x tracks percentComplete. Styling knobs to come.
        private static GameObject MakeProgressBar(GameObject canvasGo, out RectTransform fill, out Image fillImg)
        {
            var go = new GameObject("ProgressBar", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, 6f);
            rect.anchoredPosition = Vector2.zero;

            var bg = go.AddComponent<Image>();
            bg.sprite = UI.Theme.White;
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            bg.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(go.transform, false);
            fill = (RectTransform)fillGo.transform;
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            fillImg = fillGo.AddComponent<Image>();
            fillImg.sprite = UI.Theme.White;
            fillImg.color = Color.white;
            fillImg.raycastTarget = false;

            go.SetActive(false);
            return go;
        }

        private static GameObject MakeJudgementsRow(GameObject parent, out TextMeshProUGUI[] texts)
        {
            texts = new TextMeshProUGUI[JudgementColumns.Length];

            for (int i = 0; i < JudgementColumns.Length; i++)
            {
                var go = new GameObject($"J{i}", typeof(RectTransform));
                go.transform.SetParent(parent.transform, false);

                var csf = go.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var t = go.AddComponent<TextMeshProUGUI>();
                t.fontSize = RowBaseFontSize;
                t.color = Color.white;
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Overflow;
                t.text = "0";
                AddShadow(go);
                texts[i] = t;
            }

            return parent;
        }

        private static RectTransform MakeComboDisplay(GameObject canvasGo, out TextMeshProUGUI label, out TextMeshProUGUI value, out RectTransform labelWrapper)
        {
            var go = new GameObject("ComboDisplay", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.82f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 0f);

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Label lives outside the layout — ignoreLayout lets it float freely
            var wrapGo = new GameObject("ComboLabelWrapper", typeof(RectTransform));
            wrapGo.transform.SetParent(go.transform, false);
            var wrapLe = wrapGo.AddComponent<LayoutElement>();
            wrapLe.ignoreLayout = true;
            var wrapRect = (RectTransform)wrapGo.transform;
            wrapRect.anchorMin = wrapRect.anchorMax = new Vector2(0.5f, 0.5f);
            wrapRect.pivot = new Vector2(0.5f, 0.5f);
            wrapRect.anchoredPosition = Vector2.zero;
            labelWrapper = wrapRect;

            label = MakeComboText(wrapGo, "ComboLabel", ComboLabelBaseFontSize);
            label.text = "Perfect Combo";

            value = MakeComboText(go, "ComboValue", ComboValueBaseFontSize);
            value.text = "0";

            if (Instance != null)
            {
                Instance._comboValueShadow = value.GetComponent<TmpShadow>();
                Instance._comboLabelShadow = label.GetComponent<TmpShadow>();
            }

            return rect;
        }

        private static TextMeshProUGUI MakeComboText(GameObject parent, string name, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            AddShadow(go);
            return t;
        }

        private static (GameObject row, TextMeshProUGUI label, TextMeshProUGUI value) MakeRow(string name, string labelText)
        {
            var rowGo = new GameObject(name + "Row", typeof(RectTransform));

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 30f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.spacing = 0f;

            // Placeholder only — ApplySettings recomposes every row label (base text +
            // separator) and converts trailing separator spaces into HLG spacing.
            TextMeshProUGUI labelT = MakeRowText(rowGo, name + "Label");
            labelT.text = labelText;
            TextMeshProUGUI valueT = MakeRowText(rowGo, name + "Value");

            return (rowGo, labelT, valueT);
        }

        private static TextMeshProUGUI MakeRowText(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 60f;

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = RowBaseFontSize;
            t.color = Color.white;
            // Vertical Middle (line metrics), NOT MidlineLeft: Midline is the geometric
            // center of the rendered glyph bounds, so "TimingScale -" (descender on g)
            // and "100%" (none) would sit at different heights.
            t.alignment = TextAlignmentOptions.Left;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            AddShadow(go);
            return t;
        }

        private (GameObject container, TextMeshProUGUI text) MakeFpsDisplay()
        {
            var canvasGo = new GameObject("FpsCanvas");
            canvasGo.transform.SetParent(transform);
            var c = canvasGo.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 1000;
            ConfigureScaler(canvasGo.AddComponent<CanvasScaler>());

            var go = new GameObject("FpsDisplay", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-10f, 10f);

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = 22;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.BottomRight;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.text = "-- FPS";
            AddShadow(go);

            canvasGo.SetActive(false);
            return (canvasGo, t);
        }
    }
}
