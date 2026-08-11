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
            // tracks scene changes (apply-time updates alone would miss them). Computed once
            // and reused for the key-count gate below.
            bool hidden = HiddenForScene(_settings);
            UpdateCanvasVisibility(hidden);
            if (_handPanel == null && _footPanel == null) return;
            // Don't count editor/menu key presses while scene-hidden (e.g. typing in the
            // chart editor would otherwise inflate persisted counts).
            if (hidden) return;

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

        // Async press lists queried once per frame (legacy Input polling is blind on
        // Proton/X11 while SkyHook keeps reporting). _heldKeys dedupes the two sources:
        // async events can lead legacy by a frame, and without it one press would fire
        // twice on platforms where both work.
        private static readonly HashSet<KeyCode> _asyncDown   = new HashSet<KeyCode>();
        private static readonly HashSet<KeyCode> _asyncUp     = new HashSet<KeyCode>();
        private static readonly HashSet<KeyCode> _asyncHeld   = new HashSet<KeyCode>();
        private readonly HashSet<KeyCode> _heldKeys = new HashSet<KeyCode>();
        private static readonly List<KeyCode> _stuckScratch = new List<KeyCode>();

        private void PollKeys(float now)
        {
            _asyncDown.Clear();
            _asyncUp.Clear();
            KeyLimiter.CollectStateKeys(KeyLimiter.StateWentDown, _asyncDown);
            KeyLimiter.CollectStateKeys(KeyLimiter.StateWentUp, _asyncUp);

            foreach (var key in _keys)
            {
                bool held = _heldKeys.Contains(key);
                bool down = (Input.GetKeyDown(key) || _asyncDown.Contains(key)) && !held;
                bool up   = (Input.GetKeyUp(key)   || _asyncUp.Contains(key))   && held;
                if (down) _heldKeys.Add(key);
                if (up)   _heldKeys.Remove(key);
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
                        // Resolved at spawn so live accent edits color the next drop
                        // without a rebuild.
                        Color rc = _rainColors.TryGetValue(key, out var kvc) ? kvc.ToColor() : Color.white;
                        StartRainColumn(key, ThemeRain(key, rc));
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

            // Stuck-release guard: a key we consider held but that BOTH sources report
            // released (missed WentUp) gets the release path forced, else its future
            // presses would be swallowed by the held check forever.
            if (_heldKeys.Count > 0)
            {
                _asyncHeld.Clear();
                KeyLimiter.CollectStateKeys(KeyLimiter.StateIsDown, _asyncHeld);
                _stuckScratch.Clear();
                foreach (var key in _heldKeys)
                    if (!Input.GetKey(key) && !_asyncHeld.Contains(key) && !_asyncDown.Contains(key))
                        _stuckScratch.Add(key);
                foreach (var key in _stuckScratch)
                {
                    _heldKeys.Remove(key);
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

        // Theme mode dictates rain: top row plain white, lower rows accent. It only overrides
        // colors the user never chose — the factory presets ship a stock blue RainColor, so
        // "only recolor defaults" left everything blue, but silently ignoring a color picked
        // in row settings looked like a broken picker. Ghost rain keeps its own color too.
        private Color ThemeRain(KeyCode key, Color c)
        {
            var s = MainClass.Settings;
            if (s == null || !s.AccentAsTheme) return c;
            if (_ghostKeys.Contains(key)) return c;
            if (_rainRowCfg.TryGetValue(key, out var row) && row.RainColorCustom) return c;
            return _lowerRowRainKeys.Contains(key)
                ? new Color(s.UiAccentR, s.UiAccentG, s.UiAccentB, c.a)
                : new Color(1f, 1f, 1f, c.a);
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

            // Per-row override wins; otherwise narrow by depth (top row = depth 0 = full width).
            float rowW = _rowRainWidth.TryGetValue(rowIdx, out var rw) ? rw : 0f;
            float w = rowW > 0f ? rowW : Mathf.Max(4f, rowKeyW - rainDepth * widthStep);
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
