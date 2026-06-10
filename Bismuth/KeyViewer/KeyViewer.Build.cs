using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    internal partial class KeyViewer
    {
        private void BuildLayout()
        {
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Arial", "Helvetica Neue", "Helvetica" }, 14);

            _nextRowIdx = 0;
            if (_settings.ShowHandViewer && _settings.Hand != null)
                _handPanel = BuildPresetPanel(_settings.Hand, 100, "KVPanelHand");
            if (_settings.ShowFootViewer && _settings.Foot != null)
                _footPanel = BuildPresetPanel(_settings.Foot, 1000, "KVPanelFoot");

            _lastKps = -1;
            _lastTotalPerPreset.Clear();

            // Seed Total cells from loaded counts so the displayed total isn't empty until
            // the first keypress. Without this, the rain.cs Update loop only fires the dirty
            // check on keydown, so a freshly built layout sits at empty / 0 until input.
            foreach (var st in _totalCells)
            {
                if (st?.Preset == null || st.Value == null) continue;
                string pn = st.Preset.Name ?? "";
                int total = 0;
                if (_counts.TryGetValue(pn, out var pc))
                    foreach (var v in pc.Values) total += v;
                _lastTotalPerPreset[pn] = total;
                st.Value.text = total.ToString();
            }
        }

        private RectTransform BuildPresetPanel(KeyViewerPreset preset, int sortBase, string panelName)
        {
            var rows = preset.Rows;
            if (rows == null) return null;

            // Collect non-empty rows and register keys/labels/rain
            var rowCells   = new List<List<KeyViewerCell>>();
            var rowHeights = new List<float>();
            var rowCfgs    = new List<KeyViewerRow>();
            foreach (var row in rows)
            {
                if (row == null) continue;
                row.EnsureDefaults();
                if (row.Cells == null || row.Cells.Count == 0) continue;
                rowCells.Add(row.Cells);
                rowHeights.Add(row.Height);
                rowCfgs.Add(row);
                foreach (var cell in row.Cells)
                {
                    string tok = cell.Token;
                    if (tok == "KPS" || tok == "Total") continue;
                    if (TryParseKey(tok, out KeyCode kc))
                    {
                        if (!_keys.Contains(kc)) _keys.Add(kc);
                        if (!string.IsNullOrEmpty(cell.Label)) _customLabels[kc] = cell.Label;
                    }
                }
            }
            if (rowCells.Count == 0) return null;

            // Register rain enablement (and custom colors when set)
            for (int r = 0; r < rowCells.Count; r++)
            {
                var cfg = rowCfgs[r];
                if (!cfg.ShowRain) continue;
                foreach (var cell in rowCells[r])
                {
                    string tok = cell.Token;
                    if (tok == "KPS" || tok == "Total") continue;
                    if (TryParseKey(tok, out KeyCode kc))
                    {
                        _rainEnabled.Add(kc);
                        if (cfg.RainColor != null) _rainColors[kc] = cfg.RainColor;
                    }
                }
            }

            float keyW = preset.KeyWidth;
            float gap = preset.Gap;

            int topN = rowCells[0].Count;
            float panelW = topN * keyW + Mathf.Max(0, topN - 1) * gap;
            float totalH = 0f;
            foreach (var h in rowHeights) totalH += h;

            var panelGo = new GameObject(panelName);
            panelGo.transform.SetParent(_canvas.transform, false);
            var panel = panelGo.AddComponent<RectTransform>();
            panel.sizeDelta = new Vector2(panelW, totalH);
            panel.anchorMin = new Vector2(preset.X, preset.Y);
            panel.anchorMax = new Vector2(preset.X, preset.Y);
            panel.pivot = new Vector2(0f, 0f);
            panel.anchoredPosition = Vector2.zero;
            panel.localScale = Vector3.one * preset.Scale;
            _allPanels.Add(panelGo);

            // Single shadow layer per panel, sortingOrder below all rain layers for this panel
            var shadowLayer = CreateRainLayer(panel, "ShadowLayer_" + panelName, _canvas.sortingOrder + sortBase);

            // Top row defines rain X positions
            int topRowGlobal = _nextRowIdx++;
            _shadowLayers[topRowGlobal] = shadowLayer;
            _rainLayers[topRowGlobal] = CreateRainLayer(panel, "RainLayer_" + topRowGlobal, _canvas.sortingOrder + sortBase + 10);
            var topKeyX = new List<float>();
            {
                var cells = rowCells[0];
                float cellH = rowHeights[0];
                float cy = totalH * 0.5f - cellH * 0.5f;

                // Top row: cells visible widths sum to topN*keyW (constant). Distribute by widthMul.
                float topVisibleTotal = topN * keyW;
                float topSumMul = 0f;
                foreach (var c in cells) topSumMul += c.WidthMul;
                if (topSumMul <= 0f) topSumMul = 1f;

                float xCursor = -(panelW * 0.5f);
                for (int c = 0; c < cells.Count; c++)
                {
                    var cell = cells[c];
                    float visible = topVisibleTotal * cell.WidthMul / topSumMul;
                    float cx = xCursor + visible * 0.5f;
                    float slotW = visible + gap;
                    var center = new Vector2(cx, cy);
                    string tok = cell.Token;
                    if (tok == "KPS")
                        _kpsCells.Add(CreateStatCell(panel, "KPS", center, slotW, cellH, preset));
                    else if (tok == "Total")
                        _totalCells.Add(CreateStatCell(panel, "Total", center, slotW, cellH, preset));
                    else if (TryParseKey(tok, out KeyCode kc))
                    {
                        CreateKeyCell(panel, kc, center, slotW, cellH, preset);
                        _rainX[kc] = cx;
                        _rainRowIndex[kc] = topRowGlobal;
                        topKeyX.Add(cx);
                    }
                    xCursor += visible + gap;
                }
                _rowPanelH[topRowGlobal] = totalH;
                _rowKeyW[topRowGlobal] = keyW;
                _rowRainDepth[topRowGlobal] = 0;
                _rowGap[topRowGlobal] = gap;
                _rowPreset[topRowGlobal] = preset;

                // Ghost keys: indexed against top-row non-stat slots (topKeyX). Spawn rain only.
                if (preset.GhostKeysEnabled && preset.GhostKeys != null)
                {
                    int n = Mathf.Min(topKeyX.Count, preset.GhostKeys.Count);
                    for (int gi = 0; gi < n; gi++)
                    {
                        string tok = preset.GhostKeys[gi];
                        if (string.IsNullOrEmpty(tok) || tok == "None") continue;
                        if (!TryParseKey(tok, out KeyCode gkc)) continue;
                        if (!_keys.Contains(gkc)) _keys.Add(gkc);
                        _ghostKeys.Add(gkc);
                        _rainX[gkc] = topKeyX[gi];
                        _rainRowIndex[gkc] = topRowGlobal;
                        _rainEnabled.Add(gkc);
                        // Default ghost rain color = yellow when GhostRainColor is unset.
                        _rainColors[gkc] = preset.GhostRainColor
                            ?? new KvColor { R = 1f, G = 0.9f, B = 0f, A = 1f };
                    }
                }
            }

            int topM = topKeyX.Count / 2;
            float yTop = totalH * 0.5f - rowHeights[0];
            for (int mi = 1; mi < rowCells.Count; mi++)
            {
                int globalR = _nextRowIdx++;
                _shadowLayers[globalR] = shadowLayer;
                _rainLayers[globalR] = CreateRainLayer(panel, "RainLayer_" + globalR, _canvas.sortingOrder + sortBase + 10 + mi);

                var cells = rowCells[mi];
                float cellH = rowHeights[mi];
                float cy = yTop - cellH * 0.5f;
                yTop -= cellH;

                int n = cells.Count;
                // Lower row: slots sum to panelW. Each slot = panelW * mul/sumMul; visible = slot - gap.
                float lowerSumMul = 0f;
                foreach (var c in cells) lowerSumMul += c.WidthMul;
                if (lowerSumMul <= 0f) lowerSumMul = 1f;

                var leftKeys  = new List<KeyCode>();
                var rightKeys = new List<KeyCode>();
                int halfN = n / 2;
                for (int c = 0; c < n; c++)
                {
                    string tok = cells[c].Token;
                    if (tok != "KPS" && tok != "Total" && TryParseKey(tok, out KeyCode kc))
                    {
                        if (c < halfN) leftKeys.Add(kc);
                        else           rightKeys.Add(kc);
                    }
                }

                float xCursor = -(panelW * 0.5f);
                for (int c = 0; c < n; c++)
                {
                    var cell = cells[c];
                    float slot = panelW * cell.WidthMul / lowerSumMul;
                    float cx = xCursor + slot * 0.5f;
                    var center = new Vector2(cx, cy);
                    string tok = cell.Token;
                    if (tok == "KPS")
                        _kpsCells.Add(CreateStatCell(panel, "KPS", center, slot, cellH, preset));
                    else if (tok == "Total")
                        _totalCells.Add(CreateStatCell(panel, "Total", center, slot, cellH, preset));
                    else if (TryParseKey(tok, out KeyCode kc))
                    {
                        CreateKeyCell(panel, kc, center, slot, cellH, preset);
                        _rainX[kc] = cx;
                        _rainRowIndex[kc] = globalR;
                    }
                    xCursor += slot;
                }
                _rowPanelH[globalR] = totalH;
                _rowKeyW[globalR] = keyW;
                _rowRainDepth[globalR] = mi;
                _rowGap[globalR] = gap;
                _rowPreset[globalR] = preset;

                int rowNonStat = leftKeys.Count + rightKeys.Count;
                if (rowNonStat < topKeyX.Count)
                {
                    for (int i = 0; i < leftKeys.Count; i++)
                    {
                        int slot = topM - leftKeys.Count + i;
                        if (slot >= 0 && slot < topKeyX.Count)
                            _rainX[leftKeys[i]] = topKeyX[slot];
                    }
                    for (int i = 0; i < rightKeys.Count; i++)
                    {
                        int slot = topM + i;
                        if (slot >= 0 && slot < topKeyX.Count)
                            _rainX[rightKeys[i]] = topKeyX[slot];
                    }
                }
            }
            return panel;
        }

        private RectTransform CreateRainLayer(RectTransform parent, string name, int sortingOrder)
        {
            var layerGo = new GameObject(name);
            layerGo.transform.SetParent(parent, false);
            var layerRt = layerGo.AddComponent<RectTransform>();
            layerRt.anchorMin = Vector2.zero;
            layerRt.anchorMax = Vector2.one;
            layerRt.offsetMin = layerRt.offsetMax = Vector2.zero;
            var layerCanvas = layerGo.AddComponent<Canvas>();
            layerCanvas.overrideSorting = true;
            layerCanvas.sortingOrder = sortingOrder;
            return layerRt;
        }

        private void CreateKeyCell(Transform parent, KeyCode key, Vector2 center, float cellW, float cellH,
            KeyViewerPreset preset)
        {
            string label = _customLabels.TryGetValue(key, out string custom) ? custom : GetDisplayName(key);
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
            rt.sizeDelta = new Vector2(cellW - preset.Gap, cellH - preset.Gap);

            var img = go.AddComponent<RoundedRectGraphic>();
            img.Radius      = preset.Radius;
            img.BorderWidth = preset.BorderWidth;
            img.BorderColor = preset.BorderIdle.ToColor();
            img.color       = preset.BgIdle.ToColor();

            if (!_counts.TryGetValue(preset.Name ?? "", out var presetCounts))
                _counts[preset.Name ?? ""] = presetCounts = new Dictionary<KeyCode, int>();
            if (!presetCounts.ContainsKey(key)) presetCounts[key] = 0;

            // Anchor rects: label fills top half by default, count fills bottom half.
            // If one is hidden, the other fills the whole cell so it visually centers.
            Vector2 labelMin = preset.ShowCount ? new Vector2(0f, 0.5f) : new Vector2(0f, 0f);
            Vector2 labelMax = new Vector2(1f, 1f);
            Vector2 countMin = new Vector2(0f, 0f);
            Vector2 countMax = preset.ShowLabel ? new Vector2(1f, 0.5f) : new Vector2(1f, 1f);

            Text nameText  = preset.ShowLabel
                ? MakeLabel(go.transform, label, labelMin, labelMax, preset.LabelSize, false, preset.TxtIdle.ToColor())
                : null;
            Text countText = preset.ShowCount
                ? MakeLabel(go.transform, presetCounts[key].ToString(), countMin, countMax, preset.CountSize, true, preset.CountIdle.ToColor())
                : null;

            if (!_keyCells.TryGetValue(key, out var list))
                _keyCells[key] = list = new List<KeyCellRefs>();
            list.Add(new KeyCellRefs { Bg = img, Name = nameText, Count = countText, Preset = preset });
        }

        private StatCellRefs CreateStatCell(Transform parent, string label, Vector2 center, float cellW, float cellH,
            KeyViewerPreset preset)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
            rt.sizeDelta = new Vector2(cellW - preset.Gap, cellH - preset.Gap);

            var img = go.AddComponent<RoundedRectGraphic>();
            img.Radius      = preset.Radius;
            img.BorderWidth = preset.BorderWidth;
            img.BorderColor = preset.BorderIdle.ToColor();
            img.color       = preset.BgIdle.ToColor();

            var nameText = MakeLabel(go.transform, label,
                new Vector2(0f, 0.5f), new Vector2(1f, 1f), preset.LabelSize, false, preset.TxtIdle.ToColor());
            var valueText = MakeLabel(go.transform, "0",
                new Vector2(0f, 0f), new Vector2(1f, 0.5f), preset.CountSize, true, preset.CountIdle.ToColor());

            return new StatCellRefs { Bg = img, Name = nameText, Value = valueText, Preset = preset };
        }

        private Text MakeLabel(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax,
            int fontSize, bool bold, Color color)
        {
            var go = new GameObject("Txt");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = _font;
            t.fontSize = fontSize;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}
