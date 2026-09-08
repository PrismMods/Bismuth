using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bismuth.UI
{
    // Pointer/animation MonoBehaviours the UIBuilder widgets attach to their GameObjects.

    // Eats wheel events. Lives on the dropdown blocker so scrolling outside the floating
    // list doesn't scroll the page underneath it. (Everywhere else we deliberately AVOID
    // IScrollHandler so events bubble to the page ScrollRect — see HoverHandler.)
    internal class ScrollSwallower : MonoBehaviour, IScrollHandler
    {
        public void OnScroll(PointerEventData e) { }
    }

    // Gradient-strip stop handle: press selects, drag maps the pointer to a normalized
    // position across the strip. Implementing IDragHandler also keeps the ScrollRect
    // from stealing the gesture.
    internal class GradientStopHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public RectTransform Strip;
        public Action OnSelect;
        public Action<float> OnMove;

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            OnSelect?.Invoke();
        }

        public void OnDrag(PointerEventData e)
        {
            if (Strip == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Strip, e.position, e.pressEventCamera, out Vector2 local))
                return;
            float w = Strip.rect.width;
            if (w <= 0f) return;
            OnMove?.Invoke(Mathf.Clamp01(local.x / w + Strip.pivot.x));
        }
    }

    // Frees a runtime-baked Texture2D when its host object is destroyed (gradient strips
    // live on subpages that are torn down on every pop).
    internal class TextureReleaser : MonoBehaviour
    {
        public Texture2D Tex;
        private void OnDestroy() { if (Tex != null) UnityEngine.Object.Destroy(Tex); }
    }

    // Hover state notifier. Implements only the pointer enter/exit interfaces — does NOT
    // implement IScrollHandler, so mouse-wheel events bubble through to a parent ScrollRect.
    // (EventTrigger absorbs scroll events even when no scroll trigger is wired.)
    internal class HoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action OnEnter;
        public Action OnExit;
        public void OnPointerEnter(PointerEventData e) { OnEnter?.Invoke(); }
        public void OnPointerExit(PointerEventData e) { OnExit?.Invoke(); }
    }

    // Lightweight click receiver — no Selectable state machine, no graphic transitions.
    // Distinguishes left vs right click via PointerEventData.button.
    internal class ClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public Action OnClick;       // left click
        public Action OnRightClick;  // right click (mouse2)

        public void OnPointerClick(PointerEventData e)
        {
            // A pending key bind owns the click — otherwise binding LMB would also press
            // whatever the cursor happened to be over. KeyListener handles it instead.
            if (Pages.KeyListener.ClicksSwallowed) return;
            if (e.button == PointerEventData.InputButton.Right) OnRightClick?.Invoke();
            else if (e.button == PointerEventData.InputButton.Left) OnClick?.Invoke();
        }

        public static ClickHandler Attach(GameObject go, Action onClick)
        {
            var c = go.GetComponent<ClickHandler>() ?? go.AddComponent<ClickHandler>();
            c.OnClick = onClick;
            return c;
        }
    }

    // Marker components for accent-tinted graphics. Theme.ApplyAccent only repaints graphics
    // that carry these — eliminates the false-positive matching that corrupted swatch presets.
    internal class AccentFill : MonoBehaviour { public bool Active = true; }
    internal class AccentBorder : MonoBehaviour { public bool Active = true; }

    // Animates a Collapsible's body open/closed: body height (via LayoutElement.preferredHeight
    // override of the natural VLG height), alpha (CanvasGroup), and chevron rotation 0→90°.
    // RectMask2D on the body clips children that overflow while height < natural.
    internal class ExpandAnimator : MonoBehaviour
    {
        public RectTransform Body;
        public LayoutElement BodyLe;
        public CanvasGroup BodyCg;
        public RectTransform Chevron;
        public float Duration = 0.18f;

        private float _t;
        private bool _expanding;
        private bool _running;
        private float _naturalH;

        public void Set(bool expanded)
        {
            if (!Body.gameObject.activeSelf) Body.gameObject.SetActive(true);
            // Clear the height override and force a layout pass so we can read the natural,
            // VLG-derived preferred height from the children. Then re-apply current _t.
            if (BodyLe != null) BodyLe.preferredHeight = -1f;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(Body);
            _naturalH = UnityEngine.UI.LayoutUtility.GetPreferredHeight(Body);
            _expanding = expanded;
            if (BodyLe != null) BodyLe.preferredHeight = _t * _naturalH;
            if (BodyCg != null) BodyCg.alpha = _t;
            _running = true;
            enabled = true;
        }

        private void Update()
        {
            if (!_running) return;
            float dir = _expanding ? 1f : -1f;
            _t = Mathf.Clamp01(_t + dir * Time.unscaledDeltaTime / Duration);
            float eased = EaseOutCubic(_t);

            if (BodyLe != null) BodyLe.preferredHeight = eased * _naturalH;
            if (BodyCg != null) BodyCg.alpha = eased;
            if (Chevron != null) Chevron.localRotation = Quaternion.Euler(0f, 0f, -90f * eased);

            if (_expanding && _t >= 1f)
            {
                _running = false;
                // Release the height override so future child changes can grow the body naturally.
                if (BodyLe != null) BodyLe.preferredHeight = -1f;
            }
            else if (!_expanding && _t <= 0f)
            {
                _running = false;
                Body.gameObject.SetActive(false);
            }
        }

        private static float EaseOutCubic(float t) { return 1f - Mathf.Pow(1f - t, 3f); }
    }

    // Auto-revert timer for DangerButton. Counts unscaled seconds; fires OnTimeout when
    // armed long enough without confirmation. CancelTimer disarms it on successful confirm.
    internal class DangerButtonState : MonoBehaviour
    {
        public Action OnTimeout;
        private float _expireAt;
        private bool _running;

        public void StartTimer(float seconds)
        {
            _expireAt = Time.unscaledTime + seconds;
            _running = true;
        }

        public void CancelTimer() { _running = false; }

        private void Update()
        {
            if (!_running) return;
            if (Time.unscaledTime >= _expireAt)
            {
                _running = false;
                OnTimeout?.Invoke();
            }
        }
    }

    // Click-and-drag handler living on the slider track. Pointer position → normalized t → value.
    /* Normalized 0..1 drag over a rect, reported as (x, y) with y up. One control serves the
       picker's saturation/value square (both axes) and its hue/alpha strips (y only) —
       SliderControl can't, it's single-axis and owns a fill/handle/input triple. */
    internal class ColorPadControl : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public RectTransform Area;
        public Action<Vector2> OnChange;

        public void OnPointerDown(PointerEventData e) => Report(e);
        public void OnDrag(PointerEventData e) => Report(e);

        private void Report(PointerEventData e)
        {
            if (Area == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Area, e.position, e.pressEventCamera, out Vector2 local))
                return;
            var r = Area.rect;
            if (r.width <= 0f || r.height <= 0f) return;
            OnChange?.Invoke(new Vector2(
                Mathf.Clamp01((local.x - r.xMin) / r.width),
                Mathf.Clamp01((local.y - r.yMin) / r.height)));
        }
    }

    internal class SliderControl : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public float Min, Max, Value;
        public RectTransform Track;
        public RectTransform Handle;
        public RectTransform Fill;
        public TMP_InputField ValueInput;
        public string Format = "0.00";
        public float Step = 0f;
        public Action<float> OnChange;
        public Action OnEditBegin;    // gesture start, before the first value change (undo baseline)
        public Action OnAfterChange;  // after Value changed + OnChange invoked (refresh undo button)

        public void OnPointerDown(PointerEventData e) { OnEditBegin?.Invoke(); UpdateFromPointer(e); }
        public void OnDrag(PointerEventData e) { UpdateFromPointer(e); }

        private void UpdateFromPointer(PointerEventData e)
        {
            if (Track == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Track, e.position, e.pressEventCamera, out Vector2 local))
                return;
            float w = Track.rect.width;
            if (w <= 0f) return;
            // Track pivot is centered (0.5, 0.5), so local.x ∈ [-w/2, w/2]
            float t = Mathf.Clamp01((local.x + w * 0.5f) / w);
            float newValue = Mathf.Lerp(Min, Max, t);
            if (Step > 0f) newValue = Mathf.Round(newValue / Step) * Step;
            if (Mathf.Approximately(newValue, Value)) return;
            Value = newValue;
            ApplyVisuals();
            OnChange?.Invoke(Value);
            OnAfterChange?.Invoke();
        }

        public void ApplyVisuals()
        {
            float t = (Max > Min) ? Mathf.InverseLerp(Min, Max, Value) : 0f;
            if (Handle != null)
            {
                Handle.anchorMin = new Vector2(t, 0.5f);
                Handle.anchorMax = new Vector2(t, 0.5f);
                Handle.anchoredPosition = Vector2.zero;
            }
            if (Fill != null)
            {
                Fill.anchorMax = new Vector2(t, 1f);
            }
            // Don't overwrite while the user is mid-typing — the onEndEdit handler will
            // re-format on commit. Otherwise dragging the slider while focused would clobber
            // the typed text mid-character.
            if (ValueInput != null && !ValueInput.isFocused)
            {
                string formatted = Value.ToString(Format);
                if (ValueInput.text != formatted) ValueInput.text = formatted;
            }
        }
    }
}
