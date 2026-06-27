using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    internal partial class KeyViewer
    {
        private void Update()
        {
            // Re-evaluate scene-based visibility every frame so editor/main-menu hiding
            // tracks scene changes (apply-time updates alone would miss them).
            UpdateCanvasVisibility();
            if (_handPanel == null && _footPanel == null) return;
            // Don't count editor/menu key presses while scene-hidden (e.g. typing in the
            // chart editor would otherwise inflate persisted counts).
            if (HiddenForScene(_settings)) return;

            float now = Time.realtimeSinceStartup;
            while (_hitTimes.Count > 0 && now - _hitTimes.Peek() > 1f)
                _hitTimes.Dequeue();

            // The viewer keeps observing keys while the settings menu blocks the game.
            KeyLimiter.RawReadExempt = true;
            try
            {
                PollKeys(now);
            }
            finally
            {
                KeyLimiter.RawReadExempt = false;
            }
        }

        private void PollKeys(float now)
        {
            foreach (var key in _keys)
            {
                bool down = Input.GetKeyDown(key);
                bool up   = Input.GetKeyUp(key);
                if (!down && !up) continue;

                if (down)
                {
                    bool isGhost = _ghostKeys.Contains(key);
                    if (!isGhost) _hitTimes.Enqueue(now);
                    if (!isGhost && _keyCells.TryGetValue(key, out var cells))
                    {
                        foreach (var c in cells)
                        {
                            if (c?.Preset == null) continue;
                            string pn = c.Preset.Name ?? "";
                            if (!_counts.TryGetValue(pn, out var pc)) _counts[pn] = pc = new Dictionary<KeyCode, int>();
                            pc.TryGetValue(key, out int prev);
                            pc[key] = prev + 1;
                            if (c.Bg    != null) { c.Bg.color = c.Preset.BgHeld.ToColor(); c.Bg.BorderColor = c.Preset.BorderHeld.ToColor(); }
                            if (c.Name  != null) c.Name.color  = c.Preset.TxtHeld.ToColor();
                            if (c.Count != null) { c.Count.text = pc[key].ToString(); c.Count.color = c.Preset.CountHeld.ToColor(); }
                        }
                    }

                    // Update per-preset Total cells (each Total sums only its own preset's counts).
                    // Skip for ghost keys — they don't contribute to counts.
                    if (!isGhost) foreach (var s in _totalCells)
                    {
                        if (s?.Preset == null || s.Value == null) continue;
                        string pn = s.Preset.Name ?? "";
                        int total = 0;
                        if (_counts.TryGetValue(pn, out var pc))
                            foreach (var v in pc.Values) total += v;
                        _lastTotalPerPreset.TryGetValue(pn, out int last);
                        if (total != last)
                        {
                            _lastTotalPerPreset[pn] = total;
                            s.Value.text = total.ToString();
                        }
                    }

                    if (_rainEnabled.Contains(key))
                    {
                        Color rc = _rainColors.TryGetValue(key, out var kvc) ? kvc.ToColor() : Color.white;
                        StartRainColumn(key, rc);
                    }
                }

                if (up)
                {
                    if (_keyCells.TryGetValue(key, out var cells))
                        foreach (var c in cells)
                        {
                            if (c?.Preset == null) continue;
                            if (c.Bg    != null) { c.Bg.color = c.Preset.BgIdle.ToColor(); c.Bg.BorderColor = c.Preset.BorderIdle.ToColor(); }
                            if (c.Name  != null) c.Name.color  = c.Preset.TxtIdle.ToColor();
                            if (c.Count != null) c.Count.color = c.Preset.CountIdle.ToColor();
                        }
                    if (_rainEnabled.Contains(key)) StopRainColumn(key);
                }
            }

            int kps = _hitTimes.Count;
            if (kps != _lastKps)
            {
                _lastKps = kps;
                string ks = kps.ToString();
                foreach (var s in _kpsCells) if (s?.Value != null) s.Value.text = ks;
            }

            if (_rainColumns.Count > 0)
            {
                float dt = Time.unscaledDeltaTime;

                for (int i = _rainColumns.Count - 1; i >= 0; i--)
                {
                    var col = _rainColumns[i];
                    if (col.BodyRt == null) { _rainColumns.RemoveAt(i); continue; }

                    float speed     = col.Preset != null ? col.Preset.RainSpeed : 500f;
                    float trackLen  = Mathf.Max(col.Preset != null ? col.Preset.RainTrackLength : 390f, 2f);
                    float fadeStart = Mathf.Clamp(col.Preset != null ? col.Preset.RainDistance : 300f, 0f, trackLen - 1f);
                    float fadeEnd   = trackLen;
                    float fadeZoneH = Mathf.Max(fadeEnd - fadeStart, 1f);

                    if (col.Growing)
                        col.Height += speed * dt;
                    else
                    {
                        col.BotY += speed * dt;
                        if (col.BotY >= fadeEnd)
                        {
                            Destroy(col.BodyRt.gameObject);
                            if (col.TipRt != null) Destroy(col.TipRt.gameObject);
                            if (col.ShadowBodyRt != null) Destroy(col.ShadowBodyRt.gameObject);
                            if (col.ShadowTipRt  != null) Destroy(col.ShadowTipRt.gameObject);
                            _rainColumns.RemoveAt(i);
                            continue;
                        }
                    }

                    float panelTop = col.PanelHeight * 0.5f + col.Gap * 0.5f;
                    float bodyTop = Mathf.Min(col.BotY + col.Height, fadeStart);
                    float bodyH   = Mathf.Max(0f, bodyTop - col.BotY);
                    col.BodyRt.anchoredPosition = new Vector2(col.BodyRt.anchoredPosition.x, panelTop + col.BotY);
                    col.BodyRt.sizeDelta        = new Vector2(col.Width, bodyH);
                    col.BodyImg.color           = col.BaseColor;

                    float tipBot  = Mathf.Max(col.BotY, fadeStart);
                    float tipTopY = Mathf.Min(col.BotY + col.Height, fadeEnd);
                    float tipH    = Mathf.Max(0f, tipTopY - tipBot);
                    if (col.TipRt != null)
                    {
                        col.TipRt.anchoredPosition = new Vector2(col.TipRt.anchoredPosition.x, panelTop + tipBot);
                        col.TipRt.sizeDelta        = new Vector2(col.Width, tipH);
                        if (tipH > 0f)
                        {
                            float uvY = (tipBot - fadeStart) / fadeZoneH;
                            float uvH = tipH / fadeZoneH;
                            col.TipImg.uvRect = new Rect(0f, uvY, 1f, uvH);
                            col.TipImg.color  = col.BaseColor;
                        }
                        else col.TipImg.color = new Color(col.BaseColor.r, col.BaseColor.g, col.BaseColor.b, 0f);
                    }

                    if (col.ShadowBodyRt != null)
                    {
                        // The shadow body extension below BotY can sit in mid-air after the rain
                        // body has crossed into the fade zone — fade its alpha with the same curve
                        // the rain tip uses so it disappears with the rain.
                        float bodyFadeMul = col.BotY <= fadeStart
                            ? 1f
                            : Mathf.Clamp01(1f - (col.BotY - fadeStart) / fadeZoneH);
                        Color shadowBodyColor = col.ShadowColor;
                        shadowBodyColor.a *= bodyFadeMul;

                        // No rain tip yet (top hasn't reached fadeStart): use the soft-top
                        // sprite, body extended up by ShadowSize so the fade renders above the
                        // rain. Else use the sharp-top sprite so bodyTop meets the tip at
                        // fadeStart without overlap brightening.
                        bool hasTip = (col.BotY + col.Height) > fadeStart;
                        int ss = Mathf.RoundToInt(col.ShadowSize);
                        Sprite wantSprite = hasTip
                            ? GetShadowBodySprite(ss)
                            : GetShadowBodySpriteSoftTop(ss);
                        if (col.ShadowBodyImg.sprite != wantSprite)
                            col.ShadowBodyImg.sprite = wantSprite;

                        float sw = col.Width + col.ShadowSize * 2f;
                        float topExt = hasTip ? 0f : col.ShadowSize;
                        col.ShadowBodyRt.anchoredPosition = new Vector2(col.ShadowBodyRt.anchoredPosition.x, panelTop + col.BotY - col.ShadowSize);
                        col.ShadowBodyRt.sizeDelta        = new Vector2(sw, bodyH + col.ShadowSize + topExt);
                        col.ShadowBodyImg.color           = shadowBodyColor;
                        if (col.ShadowTipRt != null)
                        {
                            col.ShadowTipRt.anchoredPosition = new Vector2(col.ShadowTipRt.anchoredPosition.x, panelTop + tipBot);
                            col.ShadowTipRt.sizeDelta        = new Vector2(sw, tipH);
                            if (tipH > 0f)
                            {
                                float uvY = (tipBot - fadeStart) / fadeZoneH;
                                float uvH = tipH / fadeZoneH;
                                col.ShadowTipImg.uvRect = new Rect(0f, uvY, 1f, uvH);
                                col.ShadowTipImg.color  = col.ShadowColor;
                            }
                            else col.ShadowTipImg.color = new Color(col.ShadowColor.r, col.ShadowColor.g, col.ShadowColor.b, 0f);
                        }
                    }
                }
            }
        }

        private void StartRainColumn(KeyCode key, Color color)
        {
            if (!_rainX.TryGetValue(key, out float x)) return;
            if (!_rainRowIndex.TryGetValue(key, out int rowIdx)) return;
            if (!_rainLayers.TryGetValue(rowIdx, out var layerRt)) return;

            var preset = _rowPreset.TryGetValue(rowIdx, out var rp) ? rp : null;
            float rowKeyW = _rowKeyW.TryGetValue(rowIdx, out var kw) ? kw : 60f;
            int rainDepth = _rowRainDepth.TryGetValue(rowIdx, out var rd) ? rd : 0;
            float rowPanelH = _rowPanelH.TryGetValue(rowIdx, out var ph) ? ph : 0f;
            float rowGap = _rowGap.TryGetValue(rowIdx, out var gp) ? gp : 4f;
            float widthStep = preset != null ? preset.RainWidthStep : 14f;

            float w = Mathf.Max(4f, rowKeyW - rainDepth * widthStep);
            Transform layer = layerRt;
            float startY = rowPanelH * 0.5f + rowGap * 0.5f;

            int shadowSizeInt = preset != null ? Mathf.Max(0, Mathf.RoundToInt(preset.RainShadowSize)) : 0;
            float shadowSize = shadowSizeInt;
            Color shadowColor = preset?.RainShadowColor != null ? preset.RainShadowColor.ToColor() : new Color(0f, 0f, 0f, 0.5f);

            RectTransform shadowBodyRt = null;
            Image shadowBodyImg = null;
            RectTransform shadowTipRt = null;
            RawImage shadowTipImg = null;
            if (shadowSize > 0f && _shadowLayers.TryGetValue(rowIdx, out var shadowLayer) && shadowLayer != null)
            {
                var sBodyGo = new GameObject("RainBodyShadow");
                sBodyGo.transform.SetParent(shadowLayer, false);
                shadowBodyRt = sBodyGo.AddComponent<RectTransform>();
                shadowBodyRt.anchorMin = shadowBodyRt.anchorMax = new Vector2(0.5f, 0.5f);
                shadowBodyRt.pivot     = new Vector2(0.5f, 0f);
                shadowBodyRt.anchoredPosition = new Vector2(x, startY);
                shadowBodyRt.sizeDelta        = new Vector2(w + shadowSize * 2f, 0f);
                shadowBodyImg = sBodyGo.AddComponent<Image>();
                shadowBodyImg.sprite = GetShadowBodySprite(shadowSizeInt);
                shadowBodyImg.type   = Image.Type.Sliced;
                shadowBodyImg.color  = shadowColor;

                var sTipGo = new GameObject("RainTipShadow");
                sTipGo.transform.SetParent(shadowLayer, false);
                shadowTipRt = sTipGo.AddComponent<RectTransform>();
                shadowTipRt.anchorMin = shadowTipRt.anchorMax = new Vector2(0.5f, 0.5f);
                shadowTipRt.pivot     = new Vector2(0.5f, 0f);
                shadowTipRt.anchoredPosition = new Vector2(x, startY);
                shadowTipRt.sizeDelta        = new Vector2(w + shadowSize * 2f, 0f);
                shadowTipImg = sTipGo.AddComponent<RawImage>();
                shadowTipImg.texture = GetShadowTipTex(shadowSizeInt, Mathf.RoundToInt(w));
                shadowTipImg.color   = shadowColor;
            }

            var bodyGo = new GameObject("RainBody");
            bodyGo.transform.SetParent(layer, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            bodyRt.anchorMin = bodyRt.anchorMax = new Vector2(0.5f, 0.5f);
            bodyRt.pivot     = new Vector2(0.5f, 0f);
            bodyRt.anchoredPosition = new Vector2(x, startY);
            bodyRt.sizeDelta        = new Vector2(w, 0f);
            var bodyImg = bodyGo.AddComponent<Image>();
            bodyImg.color = color;

            var tipGo = new GameObject("RainTip");
            tipGo.transform.SetParent(layer, false);
            var tipRt = tipGo.AddComponent<RectTransform>();
            tipRt.anchorMin = tipRt.anchorMax = new Vector2(0.5f, 0.5f);
            tipRt.pivot     = new Vector2(0.5f, 0f);
            tipRt.anchoredPosition = new Vector2(x, startY);
            tipRt.sizeDelta        = new Vector2(w, 0f);
            var tipImg = tipGo.AddComponent<RawImage>();
            tipImg.texture = GetGradientTex();
            tipImg.color   = color;

            _rainColumns.Add(new RainColumn
            {
                Key = key,
                BodyRt = bodyRt, BodyImg = bodyImg,
                TipRt  = tipRt,  TipImg  = tipImg,
                ShadowBodyRt = shadowBodyRt, ShadowBodyImg = shadowBodyImg,
                ShadowTipRt  = shadowTipRt,  ShadowTipImg  = shadowTipImg,
                BaseColor = color, ShadowColor = shadowColor,
                Width = w, ShadowSize = shadowSize,
                Height = 0f, BotY = 0f, Growing = true,
                PanelHeight = rowPanelH,
                Gap = rowGap,
                Preset = preset
            });
        }

        private void StopRainColumn(KeyCode key)
        {
            foreach (var col in _rainColumns)
                if (col.Key == key) col.Growing = false;
        }
    }
}
