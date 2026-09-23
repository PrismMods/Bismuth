using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    /* Custom detailed results: one placeable element per field, instead of the game's single
       text block.

       Visibility mirrors the game's own txtResults rather than inventing a "results are up"
       signal. The original is kept alive but render-hidden (canvasRenderer alpha 0 — the same
       trick GameTextShadow uses for game text), so:
         - it never shows while the custom screen is on, whatever the game's setting says;
         - the game only activates it when Persistence.showDetailedResults is on, so with that
           off our fields stay hidden too, with no extra condition to keep in sync.
       Killing the GameObject instead would have thrown that signal away. */
    public partial class Overlay
    {
        private GameObject _resultsCanvas;
        private readonly Dictionary<string, (RectTransform Rect, TextMeshProUGUI Label, TextMeshProUGUI Value)>
            _resultsRows = new Dictionary<string, (RectTransform, TextMeshProUGUI, TextMeshProUGUI)>();

        // Set by the results editor so fields can be positioned with no level running.
        internal bool ResultsPreview { get; set; }

        /* True between DetailedResults.Show() and leaving the results. The game never
           deactivates that object, so this flag is the only honest signal. */
        private bool _resultsShowing;
        internal void OnResultsShown() => _resultsShowing = true;
        internal void ClearResultsShown() => _resultsShowing = false;
        internal int AttemptCount => _attempts;
        internal int JudgementCount(HitMargin m)
        {
            int i = (int)m;
            return i >= 0 && i < _judgementCounts.Length ? _judgementCounts[i] : 0;
        }

        // Fonts ride the overlay's own font pass (SetFont), same as every other row.
        internal void SetResultsFont(TMP_FontAsset labelFont, TMP_FontAsset valueFont)
        {
            foreach (var kv in _resultsRows)
            {
                if (kv.Value.Label != null && labelFont != null) kv.Value.Label.font = labelFont;
                if (kv.Value.Value != null && valueFont != null) kv.Value.Value.font = valueFont;
            }
        }

        internal RectTransform ResultsFieldRect(string key)
            => _resultsRows.TryGetValue(key, out var r) ? r.Rect : null;

        private void MakeResultsScreen()
        {
            _resultsCanvas = new GameObject("BismuthResultsCanvas");
            _resultsCanvas.transform.SetParent(transform);
            var c = _resultsCanvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 998;   // under the timing graph, over the game
            ConfigureScaler(_resultsCanvas.AddComponent<CanvasScaler>());

            foreach (var def in ResultsFields.All)
            {
                var go = new GameObject("Result_" + def.Key, typeof(RectTransform));
                go.transform.SetParent(_resultsCanvas.transform, false);
                var rect = (RectTransform)go.transform;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(240f, 34f);   // replaced by ApplyResultsScreen

                var label = MakeResultsText(go, "L", isLabel: true);
                var value = MakeResultsText(go, "V", isLabel: false);
                _resultsRows[def.Key] = (rect, label, value);
            }

            _resultsCanvas.SetActive(false);
        }

        /* Label owns the left half of the row, value the right half, with a small gap at the
           seam. Which way each sits inside its half is a setting — the alignment itself is
           written in ApplyResultsScreen. */
        private static TextMeshProUGUI MakeResultsText(GameObject parent, string name, bool isLabel)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(isLabel ? 0f : 0.5f, 0f);
            r.anchorMax = new Vector2(isLabel ? 0.5f : 1f, 1f);
            r.offsetMin = new Vector2(isLabel ? 0f : 6f, 0f);
            r.offsetMax = new Vector2(isLabel ? -6f : 0f, 0f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = 22;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        // 0 Left, 1 Center, 2 Right. Midline keeps rows on a shared baseline regardless of
        // whether the string has descenders (see the TMP notes in docs/fonts.md).
        private static TextAlignmentOptions AlignOf(int a)
        {
            if (a == 1) return TextAlignmentOptions.Center;
            return a == 2 ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        }

        internal void ApplyResultsScreen(Settings s)
        {
            if (_resultsCanvas == null) return;
            foreach (var def in ResultsFields.All)
            {
                if (!_resultsRows.TryGetValue(def.Key, out var row)) continue;
                var o = s.ResultsFieldFor(def.Key);

                float x = o != null && !float.IsNaN(o.X) ? o.X : def.X;
                float y = o != null && !float.IsNaN(o.Y) ? o.Y : def.Y;
                row.Rect.anchorMin = row.Rect.anchorMax = new Vector2(x, y);
                row.Rect.anchoredPosition = Vector2.zero;
                float w = o != null && o.Width > 0f ? o.Width : s.ResultsFieldWidth;
                row.Rect.sizeDelta = new Vector2(Mathf.Max(40f, w), 34f);
                row.Rect.localScale = Vector3.one * Mathf.Clamp(o != null ? o.Scale : 1f, 0.25f, 4f);
                row.Rect.localRotation = Quaternion.Euler(0f, 0f, o != null ? o.Rotation : 0f);

                row.Label.alignment = AlignOf(o?.LabelAlign ?? 0);
                row.Value.alignment = AlignOf(o?.ValueAlign ?? 2);
                row.Label.text = !string.IsNullOrEmpty(o?.Label) ? o.Label : Loc.T(def.Label);
                row.Label.color = o?.LabelColor?.ToColor() ?? Color.white;
                row.Value.color = o?.ValueColor?.ToColor()
                    ?? def.DefaultColor?.Invoke() ?? Color.white;
                row.Rect.gameObject.SetActive(!(o?.Hidden ?? false));
            }
            RefreshResultsValues();
        }

        private void RefreshResultsValues()
        {
            foreach (var def in ResultsFields.All)
            {
                if (!_resultsRows.TryGetValue(def.Key, out var row) || row.Value == null) continue;
                string v = def.Value != null ? def.Value(this) : "";
                /* In the editor there is no level, so most fields read empty — and an empty row
                   has no bounds, which leaves a handle with nothing to grab. A placeholder
                   keeps every field draggable and shows roughly its real width. */
                if (ResultsPreview && string.IsNullOrEmpty(v)) v = "0";
                row.Value.text = v;
            }
        }

        /* Per frame: follow the game's own results text. Cheap — one activeInHierarchy read
           plus, only on the frame it appears, one pass over the fields. */
        private void UpdateResultsScreen(Settings s)
        {
            if (_resultsCanvas == null) return;

            bool on = s.CustomResults && s.ShowOverlay && !s.ActiveHideAllUI;
            // The game only calls Show() when its own detailed results are enabled, so gating
            // on that flag inherits the setting without reading it.
            bool show = ResultsPreview || (on && _resultsShowing);
            if (_resultsCanvas.activeSelf != show)
            {
                _resultsCanvas.SetActive(show);
                if (show) RefreshResultsValues();
            }
            else if (show) RefreshResultsValues();
        }
    }
}
