using TMPro;
using UnityEngine;

namespace Bismuth
{
    public partial class Overlay
    {
        // Placeholder/inactive value tint (saved-checkpoint accuracy, empty rows).
        private static readonly Color Dim = new Color(0.7f, 0.7f, 0.7f);

        public void OnAttempt()
        {
            bool atCheckpoint = false;
            try { atCheckpoint = GCS.checkpointNum > 0; } catch { }

            if (atCheckpoint)
            {
                SyncFromTracker();
            }
            else
            {
                /* Game only zeroes checkpointsUsed when Start runs with checkpointNum == 0.
                   Without this, a stuck checkpointNum keeps the 0.9875^used XAcc penalty
                   across fresh attempts */
                try { scrController.checkpointsUsed = 0; } catch { }
                ShowEmpty();
            }
        }

        private void SyncFromTracker()
        {
            // Invalidate display cache so per-frame Update re-renders fresh values
            _lastProgressT = -1f;
            _lastBpm = -1f;
            _lastTileBpmVal = -1f;
            _lastKpsVal = -1f;
            _lastTimingScale = -1f;
            _lastComboDisplay = -1;

            var trackers = scrMistakesManager.marginTrackers;
            var tracker = (trackers != null && trackers.Length > 0) ? trackers[0] : null;
            if (tracker == null) return;

            /* Walk saved prefix (hitMargins[0..lastHitMarginsSize]), not live hitMarginsCount:
               scnGame.Play skips RevertToLastCheckpoint outside editor, so on revive tracker
               still contains every pre-death hit */
            for (int i = 0; i < _judgementCounts.Length; i++) _judgementCounts[i] = 0;

            var hm = tracker.hitMargins;
            int depth = tracker.lastHitMarginsSize;
            int cap = hm != null ? System.Math.Min(depth, hm.Count) : 0;
            for (int i = 0; i < cap; i++)
            {
                int mi = (int)hm[i];
                if (mi >= 0 && mi < _judgementCounts.Length) _judgementCounts[mi]++;
            }

            // Rebuild combo by walking saved prefix backwards until streak breaks
            _combo = 0;
            if (hm != null)
            {
                var s = MainClass.Settings;
                for (int i = cap - 1; i >= 0; i--)
                {
                    var m = hm[i];
                    if (m == HitMargin.Perfect) { _combo++; continue; }
                    if (m == HitMargin.Auto)
                    {
                        if (s != null && s.ComboCountAuto) { _combo++; continue; }
                        // ComboCountAuto=false: auto neither breaks nor extends streak
                        continue;
                    }
                    break;
                }
            }

            // Suppress saved-checkpoint accuracy on attempt start. The next AddHit repaints it.
            if (accValue != null)  { accValue.text  = "--.--%"; accValue.color  = Dim; }
            if (xaccValue != null) { xaccValue.text = "--.--%"; xaccValue.color = Dim; }

            RefreshDisplay(includeAccuracy: false);
        }

        // fromCheckpoint == the game's startedFromCheckpoint (Play's seqID > 0): this attempt
        // began partway in, so it doesn't count toward the from-0% "full" total.
        public void OnLevelStart(bool isRestart, bool fromCheckpoint)
        {
            var id = LevelKey.Resolve();
            string key = id.Key;

            /* What counts as a new attempt: a full retry (scrController.Restart → scene
               reload, isRestart), an in-level checkpoint revive (Play seqID>0 with inLevel
               still set), or re-entering the same level from outside. A first sighting of a
               level loads its stored counts instead. Autoplay never counts. */
            if (RDC.auto)
            {
            }
            else if (isRestart || (inLevel && fromCheckpoint))
            {
                if (_currentLevelKey != null) Count(fromCheckpoint);
            }
            else if (!inLevel)
            {
                if (key != null && key == _currentLevelKey) Count(fromCheckpoint);
                else
                {
                    _currentLevelKey = key;
                    // Carry counts over from the old path-based key the first time we
                    // see this custom level under its new content hash.
                    AttemptsStore.Migrate(id.LegacyKey, _currentLevelKey);
                    _attempts = AttemptsStore.Get(_currentLevelKey);
                    _fullAttempts = AttemptsStore.GetFull(_currentLevelKey);
                }
            }
            _currentLevelKey = key ?? _currentLevelKey;
            inLevel = true;
            // Best-% only advances on real from-0% attempts; reload it per level so the
            // row always shows this level's record.
            PersistBest();
            _isFullAttempt = !fromCheckpoint && !RDC.auto;
            _bestPct = AttemptsStore.GetBest(_currentLevelKey);
            UpdateBestText();
            // Reuse OnAttempt so checkpoint sync from Awake_Rewind isn't wiped here
            OnAttempt();
            if (attemptsValue != null) attemptsValue.text = _attempts.ToString();
            if (attemptsFullValue != null) attemptsFullValue.text = _fullAttempts.ToString();
            ShowOrHideElements();
            /* Levels spawn HUD texts after scene loads, so catch them here too. Retries and
               checkpoint revives re-stamp only HUD fonts (rewind re-localization), so the
               full scene scan (a per-attempt hitch on large maps) is first-entry only. */
            if (isRestart || fromCheckpoint) GameFontApplier.ReapplyHud();
            else GameFontApplier.Reapply();
            GameUiLayout.Reapply();
        }

        // Bump + persist the attempt counters. A from-0% attempt also bumps "full"; a
        // checkpoint start bumps only the regular count.
        private void Count(bool fromCheckpoint)
        {
            _attempts++;
            AttemptsStore.Set(_currentLevelKey, _attempts);
            if (!fromCheckpoint)
            {
                _fullAttempts++;
                AttemptsStore.SetFull(_currentLevelKey, _fullAttempts);
            }
        }

        public void OnLevelEnd()
        {
            inLevel = false;
            PersistBest();
        }

        // Best-% writes are deferred to attempt boundaries — AttemptsStore rewrites its
        // file on every Set, so per-frame persistence would thrash it.
        private void PersistBest()
        {
            if (!_bestDirty || _currentLevelKey == null) return;
            _bestDirty = false;
            AttemptsStore.SetBest(_currentLevelKey, _bestPct);
        }

        public void ShowEmpty()
        {
            _lastProgressT = -1f;
            _lastBpm = -1f;
            _lastTileBpmVal = -1f;
            _lastKpsVal = -1f;
            _lastTimingScale = -1f;
            _lastComboDisplay = -1;

            if (progressValue != null)  { progressValue.text  = "--.--%"; progressValue.color  = Dim; }
            if (attemptsValue != null)  { attemptsValue.color = Color.white; }
            if (attemptsFullValue != null) { attemptsFullValue.color = Color.white; }
            _combo = 0;
            if (comboDisplayValue != null)  { comboDisplayValue.text = "0"; }
            if (comboDisplayLabel != null)  { comboDisplayLabel.color = Color.white; }
            if (accValue != null)      { accValue.text      = "--.--%"; accValue.color      = Dim; }
            if (xaccValue != null)     { xaccValue.text     = "--.--%"; xaccValue.color     = Dim; }
            if (bpmValue != null)      { bpmValue.text      = "---";    bpmValue.color      = Dim; }
            if (tileBpmValue != null)      { tileBpmValue.text      = "---";    tileBpmValue.color      = Dim; }
            if (kpsValue != null)          { kpsValue.text          = "---";    kpsValue.color          = Dim; }
            // Durations recompute lazily each attempt — cheap, and self-corrects when the
            // level or pitch changed since the last attempt.
            _songDurTotal = -1f;
            _levelDurTotal = -1f;
            _lastSongElapsed = -1;
            _lastLevelElapsed = -1;
            if (songDurValue != null)      { songDurValue.text      = "-:--";   songDurValue.color      = Dim; }
            if (levelDurValue != null)     { levelDurValue.text     = "-:--";   levelDurValue.color     = Dim; }
            _lastBarT = -1f;
            if (progressBarFill != null) progressBarFill.anchorMax = new Vector2(0f, 1f);
            if (timingScaleValue != null)  { timingScaleValue.text  = "---%";   timingScaleValue.color  = Dim; }
            for (int i = 0; i < _judgementCounts.Length; i++) _judgementCounts[i] = 0;
            if (judgementTexts != null)
            {
                var cols = JudgementColumns;
                for (int i = 0; i < judgementTexts.Length && i < cols.Length; i++)
                    if (judgementTexts[i] != null) { judgementTexts[i].text = "0"; judgementTexts[i].color = ColumnColor(cols[i]); }
            }
        }

        public void ResetAttempts()
        {
            _attempts = 0;
            _fullAttempts = 0;
            _bestPct = 0f;
            _bestDirty = false;
            AttemptsStore.Set(_currentLevelKey, 0);
            AttemptsStore.SetFull(_currentLevelKey, 0);
            AttemptsStore.SetBest(_currentLevelKey, 0f);
            if (attemptsValue != null) attemptsValue.text = "0";
            if (attemptsFullValue != null) attemptsFullValue.text = "0";
            UpdateBestText();
        }

        // Force-reload triage: dump the canvas show-condition terms + font health of a
        // few representative texts (destroyed assets compare == null via Unity's operator).
        internal void DumpDebug(string tag)
        {
            try
            {
                var s = MainClass.Settings;
                bool paused = false;
                try { paused = scrController.instance != null && scrController.instance.paused; } catch { }
                string F(TextMeshProUGUI t) =>
                    t == null ? "text-null" : t.font == null ? "FONT-DESTROYED" : t.font.name;
                BismuthLog.Debug(
                    $"[overlay] {tag}: inLevel={inLevel} canvasActive={(canvas != null ? canvas.gameObject.activeSelf.ToString() : "canvas-null")}" +
                    $" hideAll={s?.ActiveHideAllUI} paused={paused} ctrl={(scrController.instance != null)}" +
                    $" progress={F(progressValue)} combo={F(comboDisplayValue)} attempts={F(attemptsValue)}");
            }
            catch (System.Exception e) { BismuthLog.Debug("[overlay] dump failed: " + e.Message); }
        }

        /* labelFont/valueFont override stat rows label/value texts, and
           comboLabelFont/comboValueFont the combo display (null = font); judgements and FPS
           always use base font */
        public void SetFont(TMP_FontAsset font, TMP_FontAsset labelFont = null, TMP_FontAsset valueFont = null,
            TMP_FontAsset comboLabelFont = null, TMP_FontAsset comboValueFont = null)
        {
            if (font == null) return;
            var lf = labelFont ?? font;
            var vf = valueFont ?? font;
            if (progressLabel != null)  progressLabel.font  = lf;
            if (progressValue != null)  progressValue.font  = vf;
            if (attemptsLabel != null)  attemptsLabel.font  = lf;
            if (attemptsValue != null)  attemptsValue.font  = vf;
            if (attemptsFullLabel != null) attemptsFullLabel.font = lf;
            if (attemptsFullValue != null) attemptsFullValue.font = vf;
            if (accLabel != null)       accLabel.font       = lf;
            if (accValue != null)       accValue.font       = vf;
            if (xaccLabel != null)      xaccLabel.font      = lf;
            if (xaccValue != null)      xaccValue.font      = vf;
            if (bpmLabel != null)       bpmLabel.font       = lf;
            if (bpmValue != null)       bpmValue.font       = vf;
            if (tileBpmLabel != null)       tileBpmLabel.font       = lf;
            if (tileBpmValue != null)       tileBpmValue.font       = vf;
            if (kpsLabel != null)           kpsLabel.font           = lf;
            if (kpsValue != null)           kpsValue.font           = vf;
            if (songDurLabel != null)       songDurLabel.font       = lf;
            if (songDurValue != null)       songDurValue.font       = vf;
            if (levelDurLabel != null)      levelDurLabel.font      = lf;
            if (levelDurValue != null)      levelDurValue.font      = vf;
            if (bestLabel != null)          bestLabel.font          = lf;
            if (bestValue != null)          bestValue.font          = vf;
            if (timingScaleLabel != null)    timingScaleLabel.font    = lf;
            if (timingScaleValue != null)    timingScaleValue.font    = vf;
            if (comboDisplayLabel != null)   comboDisplayLabel.font   = comboLabelFont ?? font;
            if (comboDisplayValue != null)   comboDisplayValue.font   = comboValueFont ?? font;
            if (fpsText != null)             fpsText.font             = font;
            if (judgementTexts != null)
                foreach (var t in judgementTexts)
                    if (t != null) t.font = font;
            /* Changing the font asset swaps the material, dropping the underlay setup,
               so re-apply every shadow against the new per-text material instance. */
            foreach (var sh in GetComponentsInChildren<TmpShadow>(true))
                sh.Apply();
        }
    }
}
