using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bismuth.UI
{
    // Full-screen edit overlay for dragging overlay / key-viewer elements (Locations tab).
    // Each visible element gets a handle tracking its screen rect; dragging rewrites the
    // element's normalized anchor in Settings (live) and snaps to screen edges / centers.
    // The floating "Done" pill fires the full OnSettingsChanged apply chain once.
    internal static class LocationEditor
    {
        /* The shell moved into GameUiEditor, which now hosts this as its "Overlay" layer —
           the two editors were the same canvas, dim, handle set, Done button and undo poller,
           and keeping both meant closing one to adjust the other. What stays here is what is
           genuinely this editor's: which overlay elements are draggable and what a drag writes.

           These entry points are kept so every existing caller and the Appearance-tab button
           still work; they open the merged editor on this layer. */
        public static bool IsActive => GameUiEditor.IsActive && GameUiEditor.CurrentLayer == GameUiEditor.Layer.Overlay;
        public static void Open() => GameUiEditor.OpenOverlay();
        public static void Close() => GameUiEditor.Close();
        public static void Toggle() { if (IsActive) Close(); else Open(); }

        // Wire every overlay target onto a handle from the merged editor's factory.
        internal static void AttachHandles(Func<string, Func<RectTransform>, LocHandle> factory)
        {
            foreach (var t in MakeTargets(UICore.Settings))
            {
                var h = factory(t.Name, t.Get);
                if (h == null) continue;
                h.BeginDragCapture = t.BeginDrag;
                h.DragBy = t.DragBy;
                h.CaptureUndo = t.CaptureUndo;
                h.LockX = t.LockX;
            }
        }

        // ── Targets ────────────────────────────────────────────────────────

        private class Target
        {
            public string Name;
            public Func<RectTransform> Get;
            public bool LockX;              // vertical-only elements (offset-driven, e.g. combo label)
            public Action BeginDrag;        // capture the start value
            public Action<Vector2> DragBy;  // apply a screen-pixel delta from drag start
            public Func<Action> CaptureUndo; // snapshot for undo, returns a restore closure
        }

        private static void SetRectAnchor(RectTransform rt, Vector2 a)
        {
            if (rt == null) return;
            rt.anchorMin = rt.anchorMax = a;
        }

        // Standard target: position is a normalized screen anchor stored in Settings.
        private static Target AnchorTarget(
            string name, Func<RectTransform> get,
            Func<Vector2> getAnchor, Action<Vector2> setAnchor)
        {
            Vector2 start = Vector2.zero;
            return new Target
            {
                Name = name,
                Get = get,
                BeginDrag = () => start = getAnchor(),
                DragBy = d =>
                {
                    var a = start + new Vector2(d.x / Screen.width, d.y / Screen.height);
                    a.x = Mathf.Clamp01(a.x);
                    a.y = Mathf.Clamp01(a.y);
                    setAnchor(a);
                },
                CaptureUndo = () => { var saved = getAnchor(); return () => setAnchor(saved); },
            };
        }

        private static List<Target> MakeTargets(Settings s)
        {
            float comboLabelStart = 0f;
            var list = new List<Target>
            {
                AnchorTarget("Left Panel",
                    () => Overlay.Instance?.LeftPanelRect,
                    () => new Vector2(s.StatusLeftX, s.StatusLeftY),
                    v => { s.StatusLeftX = v.x; s.StatusLeftY = v.y; SetRectAnchor(Overlay.Instance?.LeftPanelRect, v); }),
                AnchorTarget("Right Panel",
                    () => Overlay.Instance?.RightPanelRect,
                    () => new Vector2(s.StatusRightX, s.StatusRightY),
                    v => { s.StatusRightX = v.x; s.StatusRightY = v.y; SetRectAnchor(Overlay.Instance?.RightPanelRect, v); }),
                AnchorTarget("Combo",
                    () => Overlay.Instance?.ComboRect,
                    () => new Vector2(s.ComboDisplayX, s.ComboDisplayAnchorY),
                    v => { s.ComboDisplayX = v.x; s.ComboDisplayAnchorY = v.y; SetRectAnchor(Overlay.Instance?.ComboRect, v); }),

                // Combo label floats above the count via ComboLabelY (a px offset scaled by
                // ComboDisplaySize), not an anchor — drag maps to that offset, X locked.
                new Target
                {
                    Name = "Combo Label",
                    Get = () => Overlay.Instance?.ComboLabelRect,
                    LockX = true,
                    BeginDrag = () => comboLabelStart = s.ComboLabelY,
                    DragBy = d =>
                    {
                        float scale = Mathf.Max(0.01f, s.ComboDisplaySize);
                        float canvasDeltaY = d.y / GameUiEditor.EditorCanvasScale;
                        s.ComboLabelY = Mathf.Clamp(comboLabelStart + canvasDeltaY / scale, -100f, 200f);
                        var wrap = Overlay.Instance?.ComboLabelRect;
                        if (wrap != null) wrap.anchoredPosition = new Vector2(0f, s.ComboLabelY * scale);
                    },
                    CaptureUndo = () =>
                    {
                        float saved = s.ComboLabelY;
                        return () =>
                        {
                            s.ComboLabelY = saved;
                            float scale = Mathf.Max(0.01f, s.ComboDisplaySize);
                            var wrap = Overlay.Instance?.ComboLabelRect;
                            if (wrap != null) wrap.anchoredPosition = new Vector2(0f, s.ComboLabelY * scale);
                        };
                    },
                },

                AnchorTarget("Judgements",
                    () => Overlay.Instance?.JudgementsRect,
                    () => new Vector2(s.JudgementsX, s.JudgementsAnchorY),
                    v => { s.JudgementsX = v.x; s.JudgementsAnchorY = v.y; SetRectAnchor(Overlay.Instance?.JudgementsRect, v); }),
                AnchorTarget("Timing Scale",
                    () => Overlay.Instance?.TimingScaleRect,
                    () => new Vector2(s.TimingScaleX, s.TimingScaleAnchorY),
                    v => { s.TimingScaleX = v.x; s.TimingScaleAnchorY = v.y; SetRectAnchor(Overlay.Instance?.TimingScaleRect, v); }),
                AnchorTarget("Attempts",
                    () => Overlay.Instance?.AttemptsRect,
                    () => new Vector2(s.AttemptsX, s.AttemptsY),
                    v => { s.AttemptsX = v.x; s.AttemptsY = v.y; SetRectAnchor(Overlay.Instance?.AttemptsRect, v); }),
                AnchorTarget("Key Viewer (Hand)",
                    () => KeyViewer.Instance?.HandPanel,
                    () => s.Hand != null ? new Vector2(s.Hand.X, s.Hand.Y) : Vector2.zero,
                    v => { if (s.Hand == null) return; s.Hand.X = v.x; s.Hand.Y = v.y; SetRectAnchor(KeyViewer.Instance?.HandPanel, v); }),
                AnchorTarget("Key Viewer (Foot)",
                    () => KeyViewer.Instance?.FootPanel,
                    () => s.Foot != null ? new Vector2(s.Foot.X, s.Foot.Y) : Vector2.zero,
                    v => { if (s.Foot == null) return; s.Foot.X = v.x; s.Foot.Y = v.y; SetRectAnchor(KeyViewer.Instance?.FootPanel, v); }),
            };
            return list;
        }

    }

    // One draggable handle. Tracks its target's screen rect each frame (expanded to a
    // grabbable minimum), hides while the target is hidden/empty, and converts drags into
    // normalized-anchor writes with edge/center snapping. Optional extras (GameUiEditor):
    // corner-grip / scroll-wheel scaling, right-click reset, dimmed inactive-target handles.
    internal class LocHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IScrollHandler, IPointerClickHandler
    {
        public Func<RectTransform> GetTarget;
        public Action BeginDragCapture;
        public Action<Vector2> DragBy;   // screen-pixel delta from drag start
        public Func<float> GetScale;     // current scale, with SetScale enables scaling
        public Func<float> GetRotation;  // current rotation in degrees, with SetRotation enables it
        public Action<float> SetRotation; // absolute write (callee clamps/wraps)
        public Action<float> SetScale;   // absolute scale write (callee clamps)
        public Action ResetTarget;       // right-click, null = no reset
        public Func<Action> CaptureUndo; // snapshot current state, returns a restore closure (null = not undoable)
        public bool ShowInactive;        // keep a dimmed handle when the target is inactive
        public bool TightBounds;         // size to visible child Graphics, not the target rect
        public bool LockX;
        public Canvas EditorCanvas;
        public CanvasGroup Group;

        private RectTransform _rt;
        private bool _dragging;
        private Vector2 _screenStart;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector3[] _cornersStart = new Vector3[4];

        private const float SnapPx = 14f;       // canvas units; scaled to screen px below
        private const float MarginFrac = 0.01f; // inset snap line per axis, matches default 0.01 anchors
        private const float MinW = 56f;      // grabbable minimum, canvas units
        private const float MinH = 30f;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _rt.anchorMin = _rt.anchorMax = Vector2.zero; // bottom-left of editor canvas
            _rt.pivot = Vector2.zero;
        }

        private void LateUpdate()
        {
            var target = GetTarget?.Invoke();
            bool active = target != null && target.gameObject.activeInHierarchy;
            bool show = target != null && (active ? HasContent(target) : ShowInactive);
            // Visibility via CanvasGroup, not SetActive — a disabled GameObject would stop
            // receiving LateUpdate and never come back.
            Group.alpha = show ? (active ? 1f : 0.45f) : 0f;
            Group.blocksRaycasts = show;
            Group.interactable = show;
            if (!show) return;

            // SSO canvases share the screen-pixel world space; both canvases use the same
            // scaler config so a single scaleFactor converts to editor-canvas units.
            if (!(TightBounds && active && TryTightCorners(target)))
                target.GetWorldCorners(_corners);
            float sf = EditorCanvas.scaleFactor;
            Vector2 min = _corners[0] / sf;
            Vector2 max = _corners[2] / sf;
            Vector2 size = max - min;
            if (size.x < MinW) { float d = (MinW - size.x) * 0.5f; min.x -= d; size.x = MinW; }
            if (size.y < MinH) { float d = (MinH - size.y) * 0.5f; min.y -= d; size.y = MinH; }
            _rt.anchoredPosition = min;
            _rt.sizeDelta = size;

            if ((SetScale != null || SetRotation != null) && !_gripsMade) MakeGrips();
        }

        // Handle center in screen pixels (SSO canvas world units are screen px).
        internal Vector2 ScreenCenter()
        {
            float sf = EditorCanvas != null ? EditorCanvas.scaleFactor : 1f;
            return (Vector2)_rt.position + _rt.sizeDelta * (0.5f * sf);
        }

        // Photoshop-style corner grips: drag toward/away from the handle center to
        // scale. Children with their own drag handlers, so grip drags don't bubble
        // into the move-drag on this handle.
        private bool _gripsMade;

        private void MakeGrips()
        {
            _gripsMade = true;

            /* Rotation grip: a round knob floating above the top edge, the convention every
               drawing tool uses, so it can never be confused with the square corner scale
               grips. Only built when the target can actually rotate. */
            if (SetRotation != null && GetRotation != null)
            {
                var rgo = new GameObject("RotateGrip", typeof(RectTransform));
                var rrt = (RectTransform)rgo.transform;
                rrt.SetParent(transform, false);
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 1f);
                rrt.pivot = new Vector2(0.5f, 0.5f);
                rrt.anchoredPosition = new Vector2(0f, 22f);
                rrt.sizeDelta = new Vector2(14f, 14f);

                var rbg = rgo.AddComponent<RoundedRectGraphic>();
                rbg.Radius = 7f;                       // round, vs the square scale grips
                rbg.AAFringe = 0.5f;
                rbg.BorderWidth = 1f;
                rbg.BorderColor = new Color(0f, 0f, 0f, 0.6f);
                rbg.color = Theme.Accent;
                rbg.raycastTarget = true;

                // Stem, so the knob reads as attached to the handle rather than floating.
                var stem = new GameObject("Stem", typeof(RectTransform));
                var srt = (RectTransform)stem.transform;
                srt.SetParent(transform, false);
                srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f);
                srt.pivot = new Vector2(0.5f, 0f);
                srt.anchoredPosition = Vector2.zero;
                srt.sizeDelta = new Vector2(1.5f, 16f);
                var simg = stem.AddComponent<RoundedRectGraphic>();
                simg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.6f);
                simg.raycastTarget = false;

                rgo.AddComponent<RotateGrip>().Owner = this;
            }

            if (SetScale == null) return;
            var corners = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            foreach (var c in corners)
            {
                var go = new GameObject("Grip", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = rt.anchorMax = c;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(12f, 12f);

                var bg = go.AddComponent<RoundedRectGraphic>();
                bg.Radius = 2f;
                bg.AAFringe = 0.5f;
                bg.BorderWidth = 1f;
                bg.BorderColor = new Color(0f, 0f, 0f, 0.6f);
                bg.color = Theme.Accent;
                bg.raycastTarget = true;

                go.AddComponent<ScaleGrip>().Owner = this;
            }
        }

        // Union of the visible child Graphics' rects, for targets whose own rect has dead
        // space (the error meter wrapper extends below its content). Writes _corners[0]/[2]
        // and reports whether it found anything to measure.
        private static readonly Vector3[] _tightTmp = new Vector3[4];

        private bool TryTightCorners(RectTransform target)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (var g in target.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.isActiveAndEnabled || g.color.a < 0.05f) continue;
                g.rectTransform.GetWorldCorners(_tightTmp);
                min = Vector2.Min(min, _tightTmp[0]);
                max = Vector2.Max(max, _tightTmp[2]);
                any = true;
            }
            if (!any) return false;
            _corners[0] = min;
            _corners[2] = max;
            return true;
        }

        // An empty container still has padding-driven size, so only offer a handle when
        // something inside is visible (a target drawing its own Graphic counts). The death
        // % text carries inactive aux labels that hid its handle when the message showed.
        private static bool HasContent(RectTransform target)
        {
            var g = target.GetComponent<Graphic>();
            if (g != null && g.enabled) return true;
            if (target.childCount == 0) return true;
            for (int i = 0; i < target.childCount; i++)
                if (target.GetChild(i).gameObject.activeSelf) return true;
            return false;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            var target = GetTarget?.Invoke();
            if (target == null || e.button != PointerEventData.InputButton.Left) return;
            _dragging = true;
            _screenStart = e.position;
            target.GetWorldCorners(_cornersStart);
            EditorUndo.Capture(this);
            BeginDragCapture?.Invoke();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging) return;
            Vector2 delta = e.position - _screenStart; // screen px

            // Hold Shift to lock the drag to its dominant axis (1-D move). LockX is a
            // permanent vertical-only constraint for certain targets (e.g. combo label).
            bool lockX = LockX;
            bool lockY = false;
            if (!LockX && ShiftHeld())
            {
                if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)) lockY = true;
                else lockX = true;
            }
            if (lockX) delta.x = 0f;
            if (lockY) delta.y = 0f;

            float sf = EditorCanvas.scaleFactor;
            float snap = SnapPx * sf;

            // Snap the element's would-be screen rect per axis: edge flush to the screen
            // edge, edge to the 1%-inset margin line (the default anchor positions), or
            // center to the screen center.
            if (!lockX)
                delta.x += AxisSnap(_cornersStart[0].x + delta.x, _cornersStart[2].x + delta.x,
                    Screen.width, snap, Screen.width * MarginFrac);
            if (!lockY)
                delta.y += AxisSnap(_cornersStart[0].y + delta.y, _cornersStart[2].y + delta.y,
                    Screen.height, snap, Screen.height * MarginFrac);

            DragBy?.Invoke(delta);
        }

        private static bool ShiftHeld() =>
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Returns the adjustment that aligns the rect [lo..hi] with its nearest snap line
        // on one screen axis, or 0 when none is within `snap`.
        private static float AxisSnap(float lo, float hi, float size, float snap, float margin)
        {
            float best = float.MaxValue, adj = 0f;
            Consider(-lo, ref best, ref adj);                          // lo edge → 0
            Consider(margin - lo, ref best, ref adj);                  // lo edge → inset line
            Consider(size - hi, ref best, ref adj);                    // hi edge → size
            Consider(size - margin - hi, ref best, ref adj);           // hi edge → inset line
            Consider(size * 0.5f - (lo + hi) * 0.5f, ref best, ref adj); // center → center
            return best <= snap ? adj : 0f;
        }

        private static void Consider(float candidate, ref float best, ref float adj)
        {
            float d = Mathf.Abs(candidate);
            if (d < best) { best = d; adj = candidate; }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            // Push through the full apply chain once per drop (per-frame would also run
            // KeyLimiter etc. needlessly).
            UICore.OnSettingsChanged?.Invoke();
        }

        public void OnScroll(PointerEventData e)
        {
            if (Mathf.Approximately(e.scrollDelta.y, 0f)) return;
            if (SetScale == null || GetScale == null) return;
            EditorUndo.Capture(this);
            SetScale(GetScale() * (1f + 0.1f * Mathf.Sign(e.scrollDelta.y)));
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (ResetTarget == null || e.button != PointerEventData.InputButton.Right) return;
            EditorUndo.Capture(this);
            ResetTarget();
            UICore.OnSettingsChanged?.Invoke();
        }
    }

    // Corner grip on a LocHandle: dragging scales the target around the handle center
    // (uniform, the ratio of the pointer's current to initial distance from center).
    internal class ScaleGrip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public LocHandle Owner;

        private bool _scaling;
        private Vector2 _center;
        private float _startDist;
        private float _startScale;

        public void OnBeginDrag(PointerEventData e)
        {
            if (Owner == null || Owner.GetScale == null || Owner.SetScale == null ||
                e.button != PointerEventData.InputButton.Left) return;
            _center = Owner.ScreenCenter();
            _startDist = (e.position - _center).magnitude;
            if (_startDist < 2f) return;
            EditorUndo.Capture(Owner);
            _startScale = Owner.GetScale();
            _scaling = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_scaling) return;
            float f = (e.position - _center).magnitude / _startDist;
            Owner.SetScale(_startScale * f);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_scaling) return;
            _scaling = false;
            UICore.OnSettingsChanged?.Invoke();
        }
    }

    /* Rotation knob on a LocHandle: dragging swings the target around the handle centre by
       the angle the pointer sweeps. Absolute from the gesture's start angle rather than
       incremental per frame, so a fast drag cannot accumulate drift. Shift snaps to 15°. */
    internal class RotateGrip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public LocHandle Owner;

        private bool _rotating;
        private Vector2 _center;
        private float _startPointerAngle;
        private float _startRotation;

        public void OnBeginDrag(PointerEventData e)
        {
            if (Owner == null || Owner.GetRotation == null || Owner.SetRotation == null ||
                e.button != PointerEventData.InputButton.Left) return;
            _center = Owner.ScreenCenter();
            var v = e.position - _center;
            if (v.sqrMagnitude < 4f) return;      // too close to the centre to read an angle
            _startPointerAngle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            EditorUndo.Capture(Owner);
            _startRotation = Owner.GetRotation();
            _rotating = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_rotating) return;
            var v = e.position - _center;
            if (v.sqrMagnitude < 4f) return;
            float now = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            // Screen Y is up and rotation reads clockwise, hence the negated sweep.
            float value = _startRotation - (now - _startPointerAngle);
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                value = Mathf.Round(value / 15f) * 15f;
            Owner.SetRotation(value);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_rotating) return;
            _rotating = false;
            UICore.OnSettingsChanged?.Invoke();
        }
    }

    // Per-editor-session undo stack. Each handle gesture (drag/scale/reset) pushes a
    // restore closure captured just before it mutates settings; Ctrl/Cmd+Z pops one.
    internal static class EditorUndo
    {
        private static readonly Stack<Action> _stack = new Stack<Action>();

        public static void Reset() => _stack.Clear();

        public static void Capture(LocHandle h)
        {
            var restore = h?.CaptureUndo?.Invoke();
            if (restore != null) _stack.Push(restore);
        }

        public static bool Undo()
        {
            if (_stack.Count == 0) return false;
            _stack.Pop().Invoke();
            UICore.OnSettingsChanged?.Invoke();
            return true;
        }
    }

    // Polls Ctrl/Cmd+Z to undo the last edit. Uses GetKey edge detection because
    // KeyLimiter blocks Input.GetKeyDown (but not GetKey) while the panel is open.
    internal class UndoPoller : MonoBehaviour
    {
        private bool _zPrev;

        private void Update()
        {
            bool z = Input.GetKey(KeyCode.Z);
            bool mod = Input.GetKey(KeyCode.LeftControl)  || Input.GetKey(KeyCode.RightControl)
                    || Input.GetKey(KeyCode.LeftCommand)  || Input.GetKey(KeyCode.RightCommand);
            if (z && !_zPrev && mod) EditorUndo.Undo();
            _zPrev = z;
        }
    }
}
