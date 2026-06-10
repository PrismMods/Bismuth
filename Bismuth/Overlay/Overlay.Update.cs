using UnityEngine;

namespace Bismuth
{
    public partial class Overlay
    {
        private void Update()
        {
            if (inLevel && scrController.instance == null)
                inLevel = false;

            var settings = MainClass.Settings;
            bool showOverlayStats = settings.ShowOverlay &&
                (settings.ShowProgress || settings.ShowAttempts || settings.ShowFullAttempts ||
                 settings.ShowAcc || settings.ShowXAcc || settings.ShowBpm || settings.ShowTileBpm ||
                 settings.ShowTimingScale || settings.ShowJudgements);
            bool paused = scrController.instance?.paused ?? false;
            bool show = _editMode ||
                (inLevel && !paused && !settings.ActiveHideAllUI && (showOverlayStats || settings.ShowComboDisplay));
            if (canvas.gameObject.activeSelf != show)
                canvas.gameObject.SetActive(show);

            if (settings.ShowFps && fpsText != null)
            {
                _fpsAccum  += Time.unscaledDeltaTime;
                _fpsFrames += 1;
                if (_fpsAccum >= FpsInterval)
                {
                    fpsText.text = Mathf.RoundToInt(_fpsFrames / _fpsAccum) + " FPS";
                    _fpsAccum  = 0f;
                    _fpsFrames = 0;
                }
            }

            if (judgementTexts != null && judgementTexts.Length > 8)
            {
                bool nf = scrController.instance?.noFail ?? false;
                if (judgementTexts[0] != null) judgementTexts[0].gameObject.SetActive(nf);
                if (judgementTexts[8] != null) judgementTexts[8].gameObject.SetActive(nf);
            }

            if (settings.ShowComboDisplay && comboDisplayValue != null)
            {
                if (_combo != _lastComboDisplay)
                {
                    _lastComboDisplay = _combo;
                    comboDisplayValue.text = _combo.ToString();
                    float ct = settings.ComboGradientMax > 0f
                        ? Mathf.Clamp01(_combo / settings.ComboGradientMax)
                        : 0f;
                    comboDisplayValue.color = settings.ComboGradient?.Evaluate(ct) ?? Color.white;
                }
            }

            if (_comboPulseT > 0f)
            {
                _comboPulseT = Mathf.Max(0f, _comboPulseT - Time.deltaTime / settings.ComboPulseDuration);
                // Drive both the label offset and the count's pulse-bumped size off ComboDisplaySize
                // so the pulse stays proportional and the count re-rasterizes at the larger size
                // instead of stretching the texture.
                if (_comboLabelWrapper != null)
                    _comboLabelWrapper.anchoredPosition = new Vector2(0f,
                        (settings.ComboLabelY + settings.ComboPulseOffsetY * _comboPulseT) * settings.ComboDisplaySize);
                if (comboDisplayValue != null)
                    comboDisplayValue.fontSize = Mathf.RoundToInt(
                        ComboValueBaseFontSize * settings.ComboDisplaySize * settings.ComboCountSize
                        * (1f + settings.ComboPulseScale * _comboPulseT));
            }

            if (settings.Precision != _lastPrecision)
            {
                _lastPrecision = settings.Precision;
                _lastProgressT = -1f;
                _lastBpm = -1f;
                _lastTileBpmVal = -1f;
                _lastTimingScale = -1f;
            }

            if (!inLevel || scrController.instance == null) return;

            string fmt = "F" + settings.Precision;

            if (settings.ShowProgress && progressValue != null)
            {
                float t = Mathf.Clamp01(scrController.instance.percentComplete);
                float tQ = Mathf.Floor(t * 10000f) / 10000f;
                if (tQ != _lastProgressT)
                {
                    _lastProgressT = tQ;
                    progressValue.text = tQ >= 1f ? "100%" : (tQ * 100f).ToString(fmt) + "%";
                    progressValue.color = settings.ProgressGradient?.Evaluate(tQ) ?? Color.white;
                }
            }

            if ((settings.ShowBpm || settings.ShowTileBpm) && scrConductor.instance != null)
            {
                float pitch = scrConductor.instance.song != null ? scrConductor.instance.song.pitch : 1f;
                float bpm = scrConductor.instance.bpm * (float)scrController.instance.playerOne.planetarySystem.speed * pitch;

                if (bpmValue != null && settings.ShowBpm)
                {
                    if (bpm != _lastBpm)
                    {
                        _lastBpm = bpm;
                        bpmValue.text = TrimZeros(bpm.ToString(fmt));
                        bpmValue.color = settings.BpmGradient?.Evaluate(bpm / 10000f) ?? Color.white;
                    }
                }

                if (tileBpmValue != null && settings.ShowTileBpm)
                {
                    if (Time.time - _lastTileBpmTime >= 0.01666f)
                    {
                        var floor = scrController.instance?.currFloor;
                        _lastTileBpm = floor != null && floor.angleLength > 0
                            ? bpm * Mathf.PI / (float)floor.angleLength
                            : bpm;
                        _lastTileBpmTime = Time.time;
                    }
                    if (_lastTileBpm != _lastTileBpmVal)
                    {
                        _lastTileBpmVal = _lastTileBpm;
                        tileBpmValue.text = TrimZeros(_lastTileBpm.ToString(fmt));
                        tileBpmValue.color = settings.ActiveTileBpmGradient?.Evaluate(_lastTileBpm / 10000f) ?? Color.white;
                    }
                }
            }

            if (settings.ShowTimingScale && timingScaleValue != null)
            {
                var nextFloor = scrController.instance.currFloor?.nextfloor;
                if (nextFloor != null)
                {
                    float scale = (float)nextFloor.marginScale;
                    if (scale != _lastTimingScale)
                    {
                        _lastTimingScale = scale;
                        timingScaleValue.text = $"{scale * 100f:F0}%";
                        timingScaleValue.color = Color.white;
                    }
                }
            }
        }

        public void UpdateDisplay(float percentAcc, float percentXAcc, HitMargin margin)
        {
            var s = MainClass.Settings;

            if (margin == HitMargin.Perfect || (margin == HitMargin.Auto && s.ComboCountAuto))
            {
                _combo++;
                _comboPulseT = 1f;
            }
            else if (margin != HitMargin.Auto)
                _combo = 0;

            int mi = (int)margin;
            if (mi >= 0 && mi < _judgementCounts.Length) _judgementCounts[mi]++;

            RefreshDisplay();
        }

        // includeAccuracy=false skips the acc/xacc repaint (SyncFromTracker holds the
        // "--.--%" placeholder until the player's first hit).
        private void RefreshDisplay(bool includeAccuracy = true)
        {
            var s = MainClass.Settings;
            string fmt = "F" + s.Precision;
            var trackers = scrMistakesManager.marginTrackers;
            int playerCount = trackers?.Length ?? 0;

            // Mixed alive/dead state in coop: paint dead players gray + tag them.
            var players = scrPlayerManager.instance?.players;
            bool anyAlive = false, anyDead = false;
            for (int i = 0; i < playerCount; i++)
            {
                bool alive = players != null && i < players.Length && players[i] != null && players[i].alive;
                if (alive) anyAlive = true; else anyDead = true;
            }
            bool mixedState = playerCount > 1 && anyAlive && anyDead;
            Color deadColor = Color.gray;

            if (includeAccuracy && accValue != null && playerCount > 0)
            {
                if (playerCount > 1)
                {
                    accValue.supportRichText = true;
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < playerCount; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        float a = trackers[i]?.percentAcc ?? 0f;
                        bool alive = players != null && i < players.Length && players[i] != null && players[i].alive;
                        bool dead = mixedState && !alive;
                        Color c = dead ? deadColor : (s.AccGradient?.Evaluate(a) ?? Color.white);
                        sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(c)).Append('>');
                        sb.Append((a * 100f).ToString(fmt)).Append('%');
                        if (dead) sb.Append(" (dead)");
                        sb.Append("</color>");
                    }
                    accValue.text = sb.ToString();
                    accValue.color = Color.white;
                }
                else
                {
                    float a = trackers[0]?.percentAcc ?? 0f;
                    accValue.text = (a * 100f).ToString(fmt) + "%";
                    accValue.color = s.AccGradient?.Evaluate(a) ?? Color.white;
                }
            }
            if (includeAccuracy && xaccValue != null && playerCount > 0)
            {
                if (playerCount > 1)
                {
                    xaccValue.supportRichText = true;
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < playerCount; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        float x = trackers[i]?.percentXAcc ?? 0f;
                        bool alive = players != null && i < players.Length && players[i] != null && players[i].alive;
                        bool dead = mixedState && !alive;
                        Color c = dead ? deadColor : (s.ActiveXAccGradient?.Evaluate(x) ?? Color.white);
                        sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(c)).Append('>');
                        sb.Append(x >= 1f ? "100" : (x * 100f).ToString(fmt)).Append('%');
                        if (dead) sb.Append(" (dead)");
                        sb.Append("</color>");
                    }
                    xaccValue.text = sb.ToString();
                    xaccValue.color = Color.white;
                }
                else
                {
                    float x = trackers[0]?.percentXAcc ?? 0f;
                    xaccValue.text = x >= 1f ? "100%" : (x * 100f).ToString(fmt) + "%";
                    xaccValue.color = s.ActiveXAccGradient?.Evaluate(x) ?? Color.white;
                }
            }

            if (s.ShowJudgements && judgementTexts != null)
            {
                for (int i = 0; i < DisplayedMargins.Length; i++)
                {
                    var t = judgementTexts[i];
                    if (t == null) continue;
                    int count = _judgementCounts[(int)DisplayedMargins[i]];
                    t.text = count.ToString();
                    t.color = MarginColor(DisplayedMargins[i]);
                }
            }
        }
    }
}
