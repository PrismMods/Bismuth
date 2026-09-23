using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    /* Timing graph — a histogram of this attempt's hit offsets, osu!-style: x is the offset in
       ms (early left, late right, zero at the centre line), y is how many hits landed there.

       Its own canvas, like the FPS display, because the stats canvas is switched off
       wholesale whenever no stat row is enabled or the game pauses, and a deactivated parent
       takes every child with it. The graph has to outlive both: it is meant to stay up over
       the results screen. It hides on OnLevelEnd, whose triggers are StartLoadingScene, the
       WipeToBlack out of the win screen, a state change to None, and the editor's
       ResetScene — none of them fire on REACHING the results, so inLevel holds through them.

       Redrawn on each hit rather than per frame — the data only changes on a hit. Every
       column is always drawn, empty ones as a thin stub, so the axis and the colour ramp
       read even before a bucket has a hit in it. */
    public partial class Overlay
    {
        private const float GraphMinRangeMs = 20f;
        private const float GraphStubHeight = 2f;   // an empty column still shows its colour
        private const int GraphMinBuckets = 4, GraphMaxBuckets = 240;

        // The results editor needs the graph on screen while positioning it, with no level
        // running. Same role _editMode plays for the LocationEditor.
        internal bool GraphPreview { get; set; }
        // Which placement the editor is currently editing: the results one, or the play one.
        internal bool GraphPreviewResults { get; set; }

        /* 0 = play placement, 1 = results placement. Eased toward its target each frame so
           the graph slides across on level complete instead of jumping. */
        private float _graphBlend;
        internal RectTransform TimingGraphRect => _graphRect;

        private GameObject _graphCanvas;
        private RectTransform _graphRect;
        private Image[] _graphBars;
        private Image _graphCentre;
        private Image _graphBg;

        private void MakeTimingGraph()
        {
            _graphCanvas = new GameObject("TimingGraphCanvas");
            _graphCanvas.transform.SetParent(transform);
            var c = _graphCanvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 999;
            ConfigureScaler(_graphCanvas.AddComponent<CanvasScaler>());

            var root = new GameObject("TimingGraph", typeof(RectTransform));
            root.transform.SetParent(_graphCanvas.transform, false);
            _graphRect = (RectTransform)root.transform;
            _graphRect.pivot = new Vector2(0.5f, 0.5f);

            _graphBg = root.AddComponent<Image>();
            _graphBg.color = new Color(0f, 0f, 0f, 0.35f);
            _graphBg.raycastTarget = false;

            // Zero line, drawn over the bars so it stays visible under a tall centre bucket.
            var z = new GameObject("Zero", typeof(RectTransform));
            z.transform.SetParent(root.transform, false);
            var zr = (RectTransform)z.transform;
            zr.anchorMin = new Vector2(0.5f, 0f);
            zr.anchorMax = new Vector2(0.5f, 1f);
            zr.pivot = new Vector2(0.5f, 0.5f);
            zr.sizeDelta = new Vector2(1.5f, 0f);
            _graphCentre = z.AddComponent<Image>();
            _graphCentre.color = new Color(1f, 1f, 1f, 0.6f);
            _graphCentre.raycastTarget = false;

            _graphCanvas.SetActive(false);
            BuildGraphBars(MainClass.Settings != null ? MainClass.Settings.TimingGraphBuckets : 60);
        }

        /* The column pool, rebuilt whenever the bucket count changes. Bars are anchored to the
           bottom edge with height set per redraw; the zero line is re-raised afterwards so it
           always draws over them. */
        private void BuildGraphBars(int count)
        {
            if (_graphRect == null) return;
            count = Mathf.Clamp(count, GraphMinBuckets, GraphMaxBuckets);

            if (_graphBars != null)
                foreach (var old in _graphBars)
                    if (old != null) Destroy(old.gameObject);

            _graphBars = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var b = new GameObject("Bar" + i, typeof(RectTransform));
                b.transform.SetParent(_graphRect, false);
                var r = (RectTransform)b.transform;
                r.anchorMin = new Vector2((float)i / count, 0f);
                r.anchorMax = new Vector2((float)(i + 1) / count, 0f);
                r.pivot = new Vector2(0.5f, 0f);
                r.offsetMin = new Vector2(0.5f, 0f);   // hairline gap between columns
                r.offsetMax = new Vector2(-0.5f, 0f);
                var img = b.AddComponent<Image>();
                img.raycastTarget = false;
                _graphBars[i] = img;
            }
            if (_graphCentre != null) _graphCentre.transform.SetAsLastSibling();
            RedrawTimingGraph();
        }

        internal void ApplyTimingGraph(Settings s)
        {
            if (_graphRect == null) return;
            _graphRect.sizeDelta = new Vector2(s.TimingGraphWidth, s.TimingGraphHeight);
            // Placement is per-frame now (it can be mid-slide); snap it here so a settings
            // change shows immediately.
            ApplyGraphPlacement(s, snap: true);
            // Disabling the Image rather than zeroing its alpha: an off background then costs
            // no draw call at all, and the bars and zero line are unaffected.
            if (_graphBg != null) _graphBg.enabled = s.TimingGraphBackground;
            int want = Mathf.Clamp(s.TimingGraphBuckets, GraphMinBuckets, GraphMaxBuckets);
            if (_graphBars == null || _graphBars.Length != want) BuildGraphBars(want);
            else RedrawTimingGraph();
        }

        /* Where the graph sits, blended between its play and results placements. The editor
           pins the blend to whichever placement it is editing so dragging never fights a
           slide that is still running. */
        private void ApplyGraphPlacement(Settings s, bool snap = false)
        {
            if (_graphRect == null) return;

            float want = GraphPreview ? (GraphPreviewResults ? 1f : 0f)
                : (s.TimingGraphResultsPos && _resultsShowing ? 1f : 0f);

            if (snap || GraphPreview) _graphBlend = want;
            else
            {
                float dur = Mathf.Max(0.01f, s.TimingGraphMoveTime);
                _graphBlend = Mathf.MoveTowards(_graphBlend, want, Time.unscaledDeltaTime / dur);
            }

            float b = Mathf.SmoothStep(0f, 1f, _graphBlend);
            var pos = Vector2.Lerp(new Vector2(s.TimingGraphX, s.TimingGraphY),
                                   new Vector2(s.TimingGraphResultsX, s.TimingGraphResultsY), b);
            float sc = Mathf.Lerp(Mathf.Clamp(s.TimingGraphScale, 0.25f, 4f),
                                  Mathf.Clamp(s.TimingGraphResultsScale, 0.25f, 4f), b);
            // LerpAngle so a 350°→10° move takes the short way round rather than spinning back.
            float rot = Mathf.LerpAngle(s.TimingGraphRotation, s.TimingGraphResultsRotation, b);

            _graphRect.anchorMin = _graphRect.anchorMax = pos;
            _graphRect.anchoredPosition = Vector2.zero;
            _graphRect.localScale = Vector3.one * sc;
            _graphRect.localRotation = Quaternion.Euler(0f, 0f, rot);
        }

        /* Per frame, but only flips active state. Deliberately NOT gated on pause or on any
           stat row being enabled — see the class comment: the results screen is the point. */
        private void UpdateTimingGraphVisibility(Settings s)
        {
            if (_graphCanvas == null) return;
            if (s.TimingGraphResultsPos || GraphPreview || _graphBlend > 0f)
                ApplyGraphPlacement(s);
            bool show = _editMode || GraphPreview
                || (s.ShowTimingGraph && s.ShowOverlay && inLevel
                    && !s.ActiveHideAllUI && !Settings.EditorSuppressed);
            if (_graphCanvas.activeSelf != show) _graphCanvas.SetActive(show);
        }

        // ponytail: full rebucket every hit, O(hits) each — ~15k float ops at 5k tiles, well
        // under a millisecond. If a 50k-tile level ever stutters, keep running bucket counts
        // and only rebucket when the auto range actually changes.
        private void RedrawTimingGraph()
        {
            if (_graphBars == null) return;
            var s = MainClass.Settings;
            int n = Offsets.Count;

            // Symmetric axis. Auto fits the widest hit so nothing falls off the edge; a fixed
            // range keeps graphs comparable from one play to the next.
            float range = s != null && s.TimingGraphRangeMs > 0f ? s.TimingGraphRangeMs : AutoRange(n);
            int buckets = _graphBars.Length;
            float width = 2f * range / buckets;

            var counts = new int[buckets];
            int max = 0;
            for (int i = 0; i < n; i++)
            {
                int b = Mathf.FloorToInt((Offsets.MsAt(i) + range) / width);
                if (b < 0 || b >= buckets) continue;        // off-axis under a fixed range
                if (++counts[b] > max) max = counts[b];
            }

            float h = _graphRect != null ? _graphRect.rect.height : 0f;
            for (int b = 0; b < buckets; b++)
            {
                var img = _graphBars[b];
                float centre = -range + (b + 0.5f) * width;
                var col = Offsets.ColorAt(centre);
                // Empty columns stay visible as a dim stub in their own colour, so the axis
                // and ramp are always legible; filled ones scale to the tallest column.
                bool empty = counts[b] == 0;
                col.a *= empty ? 0.35f : 1f;
                img.color = col;
                float barH = empty || max == 0 ? GraphStubHeight
                    : Mathf.Max(GraphStubHeight, h * counts[b] / max);
                var r = img.rectTransform;
                r.sizeDelta = new Vector2(r.sizeDelta.x, barH);
            }
        }

        private float AutoRange(int n)
        {
            float widest = 0f;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Abs(Offsets.MsAt(i));
                if (a > widest) widest = a;
            }
            // Round up to a tidy 10ms so the axis doesn't twitch by a millisecond every hit.
            return Mathf.Max(GraphMinRangeMs, Mathf.Ceil(widest / 10f) * 10f);
        }
    }
}
