using UnityEngine;

namespace Bismuth
{
    public partial class Overlay
    {
        private void Update()
        {
            GameFontApplier.Tick();
            GameUiLayout.Tick();
            // Queues/schedules pre-mixed hit-sound segments; no-ops unless the setting is on.
            HitSoundRenderer.Pump();
            if (inLevel && scrController.instance == null)
                inLevel = false;

            var settings = MainClass.Settings;
            bool showOverlayStats = settings.ShowOverlay &&
                (settings.ShowProgress || settings.ShowAttempts || settings.ShowFullAttempts ||
                 settings.ShowBestProgress || settings.ShowAcc || settings.ShowXAcc ||
                 settings.ShowBpm || settings.ShowTileBpm || settings.ShowKps ||
                 settings.ShowSongDuration || settings.ShowLevelDuration ||
                 settings.ShowProgressBar || settings.ShowTimingScale || settings.ShowJudgements);
            bool paused = scrController.instance?.paused ?? false;
            // Combo rides the same master Enable as the stats — it's an overlay part too.
            bool showCombo = settings.ShowOverlay && settings.ShowComboDisplay;
            bool show = _editMode ||
                (inLevel && !paused && !settings.ActiveHideAllUI && !Settings.ExternalEditorSuppress
                 && (showOverlayStats || showCombo));
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

            if (judgementTexts != null)
            {
                bool nf = scrController.instance?.noFail ?? false;
                if (nf != _lastNoFail)
                {
                    _lastNoFail = nf;
                    // Locate the fail columns by their column code — they were indices 0 and 8
                    // until XPerfect's split started shifting everything after Perfect.
                    var cols = JudgementColumns;
                    for (int i = 0; i < cols.Length && i < judgementTexts.Length; i++)
                        if (cols[i] == (int)HitMargin.FailOverload || cols[i] == (int)HitMargin.FailMiss)
                            judgementTexts[i]?.gameObject.SetActive(nf);
                }
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
                    comboDisplayValue.color = settings.ActiveComboGradient?.Evaluate(ct) ?? Color.white;
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
                _fmt = "F" + settings.Precision;
                _lastProgressT = -1f;
                _lastBpm = -1f;
                _lastTileBpmVal = -1f;
                _lastKpsVal = -1f;
                _lastTimingScale = -1f;
            }

            if (!inLevel || scrController.instance == null) return;

            // Best % advances on full attempts even while the overlay is hidden, so track it
            // before the visibility gate below.
            if (_isFullAttempt)
            {
                float bp = Mathf.Floor(Mathf.Clamp01(scrController.instance.percentComplete) * 10000f) / 10000f;
                if (bp > _bestPct)
                {
                    _bestPct = bp;
                    _bestDirty = true;
                    UpdateBestText();
                }
            }

            // Nothing below is visible when the overlay/combo canvas is hidden (off, paused,
            // hide-all) — skip the whole per-frame stat pipeline. `show` already includes
            // _editMode, so the layout editor still renders.
            if (!show) return;

            string fmt = _fmt;

            if (settings.ShowProgress && progressValue != null)
            {
                float t = Mathf.Clamp01(scrController.instance.percentComplete);
                float tQ = Mathf.Floor(t * 10000f) / 10000f;
                if (tQ != _lastProgressT)
                {
                    _lastProgressT = tQ;
                    progressValue.text = tQ >= 1f ? "100%" : (tQ * 100f).ToString(fmt) + "%";
                    progressValue.color = settings.ActiveProgressGradient?.Evaluate(tQ) ?? Color.white;
                }
            }

            if (settings.ShowProgressBar && progressBarFill != null)
            {
                float t = Mathf.Clamp01(scrController.instance.percentComplete);
                if (t != _lastBarT)
                {
                    _lastBarT = t;
                    progressBarFill.anchorMax = new Vector2(t, 1f);
                    if (progressBarFillImg != null)
                        progressBarFillImg.color = ProgressBarFillColor(settings, t);
                }
            }

            if ((settings.ShowBpm || settings.ShowTileBpm || settings.ShowKps) && scrConductor.instance != null)
            {
                float pitch = scrConductor.instance.song != null ? scrConductor.instance.song.pitch : 1f;
                float bpm = scrConductor.instance.bpm * (float)scrController.instance.playerOne.planetarySystem.speed * pitch;

                if (bpmValue != null && settings.ShowBpm)
                {
                    if (bpm != _lastBpm)
                    {
                        _lastBpm = bpm;
                        bpmValue.text = TrimZeros(bpm.ToString(fmt));
                        bpmValue.color = settings.ActiveBpmGradient?.Evaluate(bpm / 10000f) ?? Color.white;
                    }
                }

                if (settings.ShowTileBpm || settings.ShowKps)
                {
                    if (Time.time - _lastTileBpmTime >= 0.01666f)
                    {
                        var floor = scrController.instance?.currFloor;
                        _lastTileBpm = floor != null && floor.angleLength > 0
                            ? bpm * Mathf.PI / (float)floor.angleLength
                            : bpm;
                        _lastTileBpmTime = Time.time;
                    }
                    if (tileBpmValue != null && settings.ShowTileBpm && _lastTileBpm != _lastTileBpmVal)
                    {
                        _lastTileBpmVal = _lastTileBpm;
                        tileBpmValue.text = TrimZeros(_lastTileBpm.ToString(fmt));
                        tileBpmValue.color = settings.ActiveTileBpmGradient?.Evaluate(_lastTileBpm / 10000f) ?? Color.white;
                    }
                    // KPS = tile hits per second = TBPM / 60. The gradient is evaluated in
                    // the same t-domain as Tile BPM (tbpm/10000) so "Use colors from
                    // Tile BPM" matches that row's color exactly.
                    if (kpsValue != null && settings.ShowKps && _lastTileBpm != _lastKpsVal)
                    {
                        _lastKpsVal = _lastTileBpm;
                        kpsValue.text = TrimZeros((_lastTileBpm / 60f).ToString(fmt));
                        kpsValue.color = settings.ActiveKpsGradient?.Evaluate(_lastTileBpm / 10000f) ?? Color.white;
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

            // Duration rows: "elapsed/total" (e.g. 0:31/3:34). Totals computed lazily once
            // the clip/floors exist; everything scales by pitch so it reads as real
            // playback time. Both rows share ONE clock — the chart clock the game measures
            // entryTimes against — so their current time ticks in step (audio position vs
            // chart time are offset by the song intro and flip seconds at different
            // moments, which read as "out of sync" side by side).
            if ((settings.ShowSongDuration || settings.ShowLevelDuration) && scrConductor.instance != null)
            {
                var cond = scrConductor.instance;
                float pitch = cond.song != null ? cond.song.pitch : 1f;
                if (pitch <= 0f) pitch = 1f;
                float tReal = Mathf.Max(0f, (float)cond.songposition_minusi / pitch);

                // Once the clock passes a total, show exactly the total's rounded text —
                // flooring against a rounded total left the last second unreachable
                // (7.6s total displayed 0:08 but elapsed capped at floor(7.6) = 0:07).
                int ElapsedFor(float total) =>
                    tReal >= total ? Mathf.RoundToInt(total) : Mathf.FloorToInt(tReal);

                if (settings.ShowSongDuration && songDurValue != null)
                {
                    var clip = cond.song != null ? cond.song.clip : null;
                    // Songless levels: hide the row entirely instead of a stuck "-:--"
                    // (ApplySettings re-activates it on settings edits; this re-hides).
                    bool wantRow = clip != null && settings.ShowOverlay;
                    if (songDurRow != null && songDurRow.activeSelf != wantRow)
                        songDurRow.SetActive(wantRow);
                    if (clip != null && _songDurTotal < 0f)
                    {
                        _songDurTotal = clip.length / pitch;
                        _songDurTotalText = FormatDuration(_songDurTotal);
                        _lastSongElapsed = -1;
                    }
                    if (_songDurTotal >= 0f)
                    {
                        int e = ElapsedFor(_songDurTotal);
                        if (e != _lastSongElapsed)
                        {
                            _lastSongElapsed = e;
                            songDurValue.text = FormatDuration(e) + "/" + _songDurTotalText;
                            songDurValue.color = Color.white;
                        }
                    }
                }

                if (settings.ShowLevelDuration && levelDurValue != null)
                {
                    if (_levelDurTotal < 0f)
                    {
                        // Chart length = last floor's entry time (seconds of song time).
                        var floors = ADOBase.lm != null ? ADOBase.lm.listFloors : null;
                        var last = floors != null && floors.Count > 0 ? floors[floors.Count - 1] : null;
                        if (last != null)
                        {
                            _levelDurTotal = (float)last.entryTime / pitch;
                            _levelDurTotalText = FormatDuration(_levelDurTotal);
                            _lastLevelElapsed = -1;
                        }
                    }
                    if (_levelDurTotal >= 0f)
                    {
                        int e = ElapsedFor(_levelDurTotal);
                        if (e != _lastLevelElapsed)
                        {
                            _lastLevelElapsed = e;
                            levelDurValue.text = FormatDuration(e) + "/" + _levelDurTotalText;
                            levelDurValue.color = Color.white;
                        }
                    }
                }
            }
        }

        private void UpdateBestText()
        {
            if (bestValue == null) return;
            var s = MainClass.Settings;
            bestValue.text = _bestPct >= 1f ? "100%" : (_bestPct * 100f).ToString("F" + s.Precision) + "%";
            bestValue.color = Color.white;
        }

        // Per-style fill color. Style 1 (default): white fill; the progress gradient's
        // perfect color still fires at 100%. Theme mode overrides: solid fill in the
        // theme's 100% color throughout (user call — no ramp). Styles 2/3 reserved.
        private static Color ProgressBarFillColor(Settings s, float t)
        {
            switch (s.ProgressBarStyle)
            {
                default:
                    var g = s.ActiveProgressGradient;
                    if (s.AccentAsTheme && g != null && g.HasPerfectColor)
                        return new Color(g.PR, g.PG, g.PB, g.PA);
                    if (t >= 1f && g != null && g.HasPerfectColor)
                        return new Color(g.PR, g.PG, g.PB, g.PA);
                    return Color.white;
            }
        }

        private static string FormatDuration(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return "-:--";
            int total = Mathf.RoundToInt(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            return h > 0
                ? h + ":" + m.ToString("00") + ":" + s.ToString("00")
                : m + ":" + s.ToString("00");
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
            string fmt = _fmt;
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
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < playerCount; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        float a = trackers[i]?.percentAcc ?? 0f;
                        bool alive = players != null && i < players.Length && players[i] != null && players[i].alive;
                        bool dead = mixedState && !alive;
                        Color c = dead ? deadColor : (s.ActiveAccGradient?.Evaluate(a) ?? Color.white);
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
                    accValue.color = s.ActiveAccGradient?.Evaluate(a) ?? Color.white;
                }
            }
            if (includeAccuracy && xaccValue != null && playerCount > 0)
            {
                if (playerCount > 1)
                {
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
                var cols = JudgementColumns;
                for (int i = 0; i < cols.Length && i < judgementTexts.Length; i++)
                {
                    var t = judgementTexts[i];
                    if (t == null) continue;
                    t.text = ColumnCount(cols[i]).ToString();
                    t.color = ColumnColor(cols[i]);
                }
            }
        }
    }
}
