using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bismuth.UI
{
    internal static class UIBuilder
    {
        public const float RowHeight = 32f;
        public const float SectionGap = 12f;
        public const float LabelFontSize = 15;
        public const float HeaderFontSize = 16;
        public const float SmallCapsFontSize = 12;

        public static GameObject Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static Image SolidImage(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.sprite = Theme.White;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size = (int)LabelFontSize, TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            var go = Rect("Label", parent);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Theme.Font;
            t.fontSize = size;
            t.color = color ?? Theme.Text;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static GameObject SectionHeader(Transform parent, string text)
        {
            var go = Rect(text + "Header", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 22f;
            le.minHeight = 22f;

            var label = Label(go.transform, text.ToUpperInvariant(), (int)SmallCapsFontSize, TextAnchor.MiddleLeft, Theme.TextMuted);
            label.fontStyle = FontStyle.Bold;
            label.rectTransform.offsetMin = new Vector2(2f, 0f);
            return go;
        }

        // SectionHeader with a [?] icon next to the label. Hover the icon to show a popup
        // with helpText. Popup parents to the canvas root so it can render over the
        // scroll viewport instead of being clipped by RectMask2D.
        public static GameObject SectionHeaderWithHelp(Transform parent, string text, string helpText)
        {
            var go = Rect(text + "Header", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 22f;
            le.minHeight = 22f;

            // childControlWidth=true so HLG honors preferred widths (Text's ILayoutElement
            // for the label, LayoutElement for the [?] icon). With it false, the qmark
            // expanded to the default RectTransform size and rendered as a wide pill.
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(2, 0, 0, 0);

            var labelGo = Rect("L", go.transform);
            var labelT = labelGo.AddComponent<Text>();
            labelT.text = text.ToUpperInvariant();
            labelT.font = Theme.Font;
            labelT.fontSize = (int)SmallCapsFontSize;
            labelT.color = Theme.TextMuted;
            labelT.fontStyle = FontStyle.Bold;
            labelT.alignment = TextAnchor.MiddleLeft;
            labelT.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelT.verticalOverflow = VerticalWrapMode.Overflow;
            labelT.raycastTarget = false;

            var qGo = Rect("Q", go.transform);
            var qLe = qGo.AddComponent<LayoutElement>();
            qLe.preferredWidth = 14f;
            qLe.preferredHeight = 14f;
            qLe.minWidth = 14f;
            qLe.minHeight = 14f;
            var qBg = qGo.AddComponent<RoundedRectGraphic>();
            qBg.Radius = 7f;
            qBg.AAFringe = 0.5f;
            qBg.color = new Color(1f, 1f, 1f, 0.08f);
            qBg.raycastTarget = true;
            var qLbl = Label(qGo.transform, "?", (int)SmallCapsFontSize - 1, TextAnchor.MiddleCenter, Theme.TextMuted);
            qLbl.fontStyle = FontStyle.Bold;

            var popup = BuildHelpTooltip(helpText);
            if (popup != null) popup.SetActive(false);

            var qRect = (RectTransform)qGo.transform;
            var hover = qGo.AddComponent<HoverHandler>();
            hover.OnEnter = () =>
            {
                if (popup == null) return;
                popup.SetActive(true);
                popup.transform.SetAsLastSibling();
                Vector3[] corners = new Vector3[4];
                qRect.GetWorldCorners(corners);
                // corners[0] = qmark bottom-left in world coords. Popup pivot is top-left,
                // so this anchors the popup directly under the icon with a small gap.
                popup.transform.position = corners[0] + new Vector3(-4f, -4f, 0f);
            };
            hover.OnExit = () =>
            {
                if (popup != null) popup.SetActive(false);
            };

            return go;
        }

        // Floating help tooltip parented to the canvas root. Auto-sizes both axes to fit
        // the wrapped text (ContentSizeFitter + VLG padding). Hidden by default; the caller
        // toggles visibility on hover.
        private static GameObject BuildHelpTooltip(string text)
        {
            var root = UICore.CanvasRoot;
            if (root == null) return null;
            var go = Rect("HelpTooltip", root.transform);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 1f);

            SolidImage(go, Theme.Panel);
            AddBorder(go, Theme.PanelBorder, 1f);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = Rect("T", go.transform);
            var t = textGo.AddComponent<Text>();
            t.text = text;
            t.font = Theme.Font;
            t.fontSize = (int)SmallCapsFontSize;
            t.color = Theme.Text;
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;

            return go;
        }

        public static GameObject Row(Transform parent, float height = RowHeight)
        {
            var go = Rect("Row", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return go;
        }

        public static GameObject Toggle(Transform parent, string label, bool initial, Action<bool> onChange)
        {
            var row = Row(parent);
            bool value = initial;

            var bg = SolidImage(row, new Color(0, 0, 0, 0));
            bg.raycastTarget = true;

            var labelGo = Rect("Text", row.transform);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-40f, 0f);
            var lab = labelGo.AddComponent<Text>();
            lab.text = label;
            lab.font = Theme.Font;
            lab.fontSize = (int)LabelFontSize;
            lab.color = Theme.Text;
            lab.alignment = TextAnchor.MiddleLeft;
            lab.horizontalOverflow = HorizontalWrapMode.Wrap;
            lab.verticalOverflow = VerticalWrapMode.Overflow;
            lab.raycastTarget = false;

            // Classic radio button: outer ring + filled inner dot when on.
            const float ringSize = 16f;
            const float dotSize = 7f;
            var ringGo = Rect("Ring", row.transform);
            var ringRect = (RectTransform)ringGo.transform;
            ringRect.anchorMin = new Vector2(1f, 0.5f);
            ringRect.anchorMax = new Vector2(1f, 0.5f);
            ringRect.pivot = new Vector2(1f, 0.5f);
            ringRect.anchoredPosition = new Vector2(-8f, 0f);
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            var ring = ringGo.AddComponent<RoundedRectGraphic>();
            ring.Radius = ringSize * 0.5f;
            ring.BorderWidth = 1.25f;
            ring.BorderColor = value ? Theme.ToggleOn : Theme.ToggleOff;
            ring.color = new Color(0, 0, 0, 0);
            ring.raycastTarget = false;

            var dotGo = Rect("Dot", ringGo.transform);
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(dotSize, dotSize);
            var dot = dotGo.AddComponent<RoundedRectGraphic>();
            dot.Radius = dotSize * 0.5f;
            dot.color = Theme.ToggleOn;
            dot.raycastTarget = false;
            dotGo.SetActive(value);

            void Apply(bool v)
            {
                value = v;
                ring.BorderColor = v ? Theme.ToggleOn : Theme.ToggleOff;
                dotGo.SetActive(v);
            }

            HoverFill(row, bg, Theme.RowBgHover, new Color(0, 0, 0, 0));
            ClickHandler.Attach(row, () => { Apply(!value); onChange?.Invoke(value); });

            return row;
        }

        // Header row: optional ▶ arrow (when buildBody is non-null) + clickable title + radio button.
        // Arrow/title click → expand/collapse the body. Radio click → toggle the bool independently.
        // The container's preferredHeight is computed by its VLG, so the parent scrollable VLG
        // sees the full (header + body when expanded) height and reflows naturally.
        public static GameObject Collapsible(
            Transform parent,
            string title,
            bool initial,
            Action<bool> onToggle,
            Action<Transform> buildBody = null)
        {
            var container = Rect("Coll", parent);
            var clVlg = container.AddComponent<VerticalLayoutGroup>();
            clVlg.childControlWidth = true;
            clVlg.childControlHeight = true;
            clVlg.childForceExpandWidth = true;
            clVlg.childForceExpandHeight = false;
            clVlg.spacing = 0f;
            clVlg.padding = new RectOffset(0, 0, 0, 0);

            // Header row
            var header = Rect("Header", container.transform);
            var headerLe = header.AddComponent<LayoutElement>();
            headerLe.preferredHeight = RowHeight;
            headerLe.minHeight = RowHeight;
            var headerBg = SolidImage(header, new Color(0, 0, 0, 0));
            headerBg.raycastTarget = true;

            bool hasBody = buildBody != null;
            bool expanded = false;
            bool value = initial;

            // ▶ chevron (only when there's a body). Animator rotates it 90° on expand.
            Text chevron = null;
            if (hasBody)
            {
                var arrowGo = Rect("Arrow", header.transform);
                var arrowRect = (RectTransform)arrowGo.transform;
                arrowRect.anchorMin = new Vector2(0, 0);
                arrowRect.anchorMax = new Vector2(0, 1);
                arrowRect.pivot = new Vector2(0, 0.5f);
                arrowRect.sizeDelta = new Vector2(24f, 0);
                arrowRect.anchoredPosition = new Vector2(2f, 0);
                chevron = labelChild(arrowGo.transform, "▶", 15, TextAnchor.MiddleCenter, Theme.TextMuted);
            }

            // Title click zone (separate hit region — does NOT toggle the radio)
            var titleGo = Rect("Title", header.transform);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(hasBody ? 32f : 8f, 0);
            titleRect.offsetMax = new Vector2(-36f, 0);
            var titleBg = SolidImage(titleGo, new Color(0, 0, 0, 0));
            titleBg.raycastTarget = true;
            labelChild(titleGo.transform, title, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            // Radio (right side) — separate click zone, doesn't bubble to title/header
            const float ringSize = 14f;
            const float dotSize = 6f;
            var ringGo = Rect("Ring", header.transform);
            var ringRect = (RectTransform)ringGo.transform;
            ringRect.anchorMin = new Vector2(1f, 0.5f);
            ringRect.anchorMax = new Vector2(1f, 0.5f);
            ringRect.pivot = new Vector2(1f, 0.5f);
            ringRect.anchoredPosition = new Vector2(-8f, 0f);
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            var ring = ringGo.AddComponent<RoundedRectGraphic>();
            ring.Radius = ringSize * 0.5f;
            ring.BorderWidth = 1.25f;
            ring.BorderColor = value ? Theme.ToggleOn : Theme.ToggleOff;
            ring.color = new Color(0, 0, 0, 0);
            ring.raycastTarget = true;
            var ringAccent = ringGo.AddComponent<AccentBorder>();
            ringAccent.Active = value;

            var dotGo = Rect("Dot", ringGo.transform);
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(dotSize, dotSize);
            var dot = dotGo.AddComponent<RoundedRectGraphic>();
            dot.Radius = dotSize * 0.5f;
            dot.color = Theme.ToggleOn;
            dot.raycastTarget = false;
            dotGo.AddComponent<AccentFill>();
            dotGo.SetActive(value);

            // Body — initially hidden, only created when callback provided. Wired with
            // CanvasGroup (alpha fade) + LayoutElement (height interpolation override) +
            // RectMask2D (clip children that overflow during unroll) for animation.
            GameObject bodyGo = null;
            ExpandAnimator animator = null;
            if (hasBody)
            {
                bodyGo = Rect("Body", container.transform);
                var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
                bodyVlg.childControlWidth = true;
                bodyVlg.childControlHeight = true;
                bodyVlg.childForceExpandWidth = true;
                bodyVlg.childForceExpandHeight = false;
                bodyVlg.spacing = 2f;
                bodyVlg.padding = new RectOffset(24, 0, 2, 6);
                var bodyLe = bodyGo.AddComponent<LayoutElement>();
                bodyLe.preferredHeight = -1f;
                var bodyCg = bodyGo.AddComponent<CanvasGroup>();
                bodyCg.alpha = 0f;
                bodyGo.AddComponent<RectMask2D>();
                buildBody(bodyGo.transform);
                bodyGo.SetActive(false);

                animator = bodyGo.AddComponent<ExpandAnimator>();
                animator.Body = (RectTransform)bodyGo.transform;
                animator.BodyLe = bodyLe;
                animator.BodyCg = bodyCg;
                animator.Chevron = chevron != null ? chevron.rectTransform : null;
            }

            Action toggleValue = () => {
                value = !value;
                ring.BorderColor = value ? Theme.ToggleOn : Theme.ToggleOff;
                ringAccent.Active = value;
                dotGo.SetActive(value);
                onToggle?.Invoke(value);
            };

            ClickHandler.Attach(ringGo, toggleValue);

            if (hasBody)
            {
                Action toggleExpand = () => {
                    expanded = !expanded;
                    animator.Set(expanded);
                };
                ClickHandler.Attach(titleGo, toggleExpand);
                // Empty header space (the gap between title and ring) also bubbles to here
                // via the header's raycast bg — clicking it expands too.
                ClickHandler.Attach(header, toggleExpand);
            }
            else
            {
                // No body → no arrow → no expand action. Clicking anywhere on the row
                // (including the title area) toggles the radio.
                ClickHandler.Attach(titleGo, toggleValue);
                ClickHandler.Attach(header, toggleValue);
            }

            HoverFill(header, headerBg, Theme.RowBgHover, new Color(0, 0, 0, 0));
            return container;
        }

        // Internal: create a child Text inside a parent rect (no its own LayoutElement).
        private static Text labelChild(Transform parent, string text, int size, TextAnchor anchor, Color color)
        {
            var go = Rect("L", parent);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Theme.Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static GameObject Button(Transform parent, string label, Action onClick)
        {
            var row = Row(parent);
            var bg = SolidImage(row, Theme.ButtonBg);
            bg.raycastTarget = true;

            var t = Label(row.transform, label, (int)LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);
            t.rectTransform.offsetMin = new Vector2(8f, 0f);
            t.rectTransform.offsetMax = new Vector2(-8f, 0f);

            HoverFill(row, bg, Theme.ButtonHover, Theme.ButtonBg);
            ClickHandler.Attach(row, onClick);
            return row;
        }

        public static GameObject IntSlider(
            Transform parent,
            string label,
            int value, int min, int max,
            Action<int> onChange)
        {
            return Slider(parent, label, value, min, max,
                v => onChange?.Invoke(Mathf.RoundToInt(v)),
                "0", 1f);
        }

        // Horizontal slider — label left, draggable track + handle middle, numeric value right.
        // `step` > 0 snaps the value to multiples of step (used by IntSlider with step=1).
        public static GameObject Slider(
            Transform parent,
            string label,
            float value, float min, float max,
            Action<float> onChange,
            string format = "0.00",
            float step = 0f)
        {
            var row = Row(parent);
            const float labelW = 140f;
            const float valueW = 56f;

            // Label
            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = labGo.AddComponent<Text>();
            lab.text = label;
            lab.font = Theme.Font;
            lab.fontSize = (int)LabelFontSize;
            lab.color = Theme.Text;
            lab.alignment = TextAnchor.MiddleLeft;
            lab.raycastTarget = false;

            // Value display (right) — wrapped in an InputField so the user can click and
            // type a new value directly. The InputField lives on `valGo`; its caret is built
            // by Unity on first activation. The visible Text is a child so it doesn't collide
            // with the InputField on the same GameObject.
            var valGo = Rect("Value", row.transform);
            var valRect = (RectTransform)valGo.transform;
            valRect.anchorMin = new Vector2(1, 0);
            valRect.anchorMax = new Vector2(1, 1);
            valRect.pivot = new Vector2(1, 0.5f);
            valRect.sizeDelta = new Vector2(valueW, 0);
            valRect.anchoredPosition = new Vector2(-8f, 0);
            // Transparent bg gives the InputField a raycast target for click-to-focus.
            var valBg = SolidImage(valGo, new Color(0, 0, 0, 0));
            valBg.raycastTarget = true;

            var valTextGo = Rect("Text", valGo.transform);
            var valTextRect = (RectTransform)valTextGo.transform;
            valTextRect.anchorMin = Vector2.zero;
            valTextRect.anchorMax = Vector2.one;
            valTextRect.offsetMin = Vector2.zero;
            valTextRect.offsetMax = Vector2.zero;
            var valT = valTextGo.AddComponent<Text>();
            valT.font = Theme.Font;
            valT.fontSize = (int)LabelFontSize;
            valT.color = Theme.TextMuted;
            valT.alignment = TextAnchor.MiddleRight;
            valT.supportRichText = false;
            valT.raycastTarget = false;

            var input = valGo.AddComponent<InputField>();
            input.textComponent = valT;
            input.contentType = InputField.ContentType.DecimalNumber;
            input.lineType = InputField.LineType.SingleLine;
            input.caretWidth = 1;
            input.caretBlinkRate = 0.6f;
            input.customCaretColor = true;
            input.caretColor = Theme.Text;
            input.selectionColor = new Color(Theme.ToggleOn.r, Theme.ToggleOn.g, Theme.ToggleOn.b, 0.45f);
            input.text = Mathf.Clamp(value, min, max).ToString(format);

            // Track — stretches between label and value
            var trackGo = Rect("Track", row.transform);
            var trackRect = (RectTransform)trackGo.transform;
            trackRect.anchorMin = new Vector2(0, 0.5f);
            trackRect.anchorMax = new Vector2(1, 0.5f);
            trackRect.pivot = new Vector2(0.5f, 0.5f);
            trackRect.sizeDelta = new Vector2(-(labelW + valueW + 24f), 5f);
            trackRect.anchoredPosition = new Vector2((labelW - valueW) * 0.5f + 4f, 0);

            // Sharp flat Image instead of RoundedRectGraphic — the procedural geometry's AA
            // fringe blurred visibly at UI scale > 1. Flat rect with no antialiasing stays crisp.
            var trackBg = trackGo.AddComponent<Image>();
            trackBg.sprite = Theme.White;
            trackBg.color = Theme.ToggleOff;
            trackBg.raycastTarget = true;

            // Fill (left portion)
            var fillGo = Rect("Fill", trackGo.transform);
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = new Vector2(0, 0);
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.pivot = new Vector2(0, 0.5f);
            fillRect.sizeDelta = new Vector2(0, 0);
            fillRect.anchoredPosition = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = Theme.White;
            fill.color = Theme.ToggleOn;
            fill.raycastTarget = false;
            fillGo.AddComponent<AccentFill>();

            // Handle — anchored at a normalized X position (0..1)
            var handleGo = Rect("Handle", trackGo.transform);
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(14f, 14f);
            var handle = handleGo.AddComponent<RoundedRectGraphic>();
            handle.Radius = 7f;
            // Tighter AA fringe — the default 1.25 looks like a soft halo when the canvas
            // is scaled up. 0.5 keeps the circle anti-aliased without the perceived blur.
            handle.AAFringe = 0.5f;
            handle.color = Theme.ToggleOn;
            handle.raycastTarget = false;
            handleGo.AddComponent<AccentFill>();

            var ctrl = trackGo.AddComponent<SliderControl>();
            ctrl.Min = min;
            ctrl.Max = max;
            ctrl.Value = Mathf.Clamp(value, min, max);
            ctrl.Track = trackRect;
            ctrl.Handle = handleRect;
            ctrl.Fill = fillRect;
            ctrl.ValueInput = input;
            ctrl.Format = format;
            ctrl.Step = step;
            ctrl.OnChange = onChange;
            ctrl.ApplyVisuals();

            // Keyboard commit: parse, clamp, snap, push back through ApplyVisuals + onChange.
            float captureMin = min, captureMax = max, captureStep = step;
            string captureFormat = format;
            input.onEndEdit.AddListener(committed => {
                if (float.TryParse(committed, out float v))
                {
                    v = Mathf.Clamp(v, captureMin, captureMax);
                    if (captureStep > 0f) v = Mathf.Round(v / captureStep) * captureStep;
                    if (!Mathf.Approximately(v, ctrl.Value))
                    {
                        ctrl.Value = v;
                        onChange?.Invoke(v);
                    }
                }
                // Always reformat the displayed text — reverts garbage input or normalizes precision.
                input.text = ctrl.Value.ToString(captureFormat);
                ctrl.ApplyVisuals();
            });

            return row;
        }

        // ◀ / ▶ cycle selector — for picking from a finite ordered list (font name, etc.)
        public static GameObject CycleSelector(
            Transform parent,
            string label,
            IList<string> options,
            int currentIndex,
            Action<int> onChange)
        {
            var row = Row(parent);
            const float labelW = 140f;
            const float btnW = 22f;

            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = labGo.AddComponent<Text>();
            lab.text = label;
            lab.font = Theme.Font;
            lab.fontSize = (int)LabelFontSize;
            lab.color = Theme.Text;
            lab.alignment = TextAnchor.MiddleLeft;
            lab.raycastTarget = false;

            // Right cluster: ◀ value ▶
            var rightGo = Rect("Right", row.transform);
            var rightRect = (RectTransform)rightGo.transform;
            rightRect.anchorMin = new Vector2(1, 0);
            rightRect.anchorMax = new Vector2(1, 1);
            rightRect.pivot = new Vector2(1, 0.5f);
            rightRect.sizeDelta = new Vector2(220f, 0);
            rightRect.anchoredPosition = new Vector2(-8f, 0);

            int idx = (options == null || options.Count == 0) ? -1 : Mathf.Clamp(currentIndex, 0, options.Count - 1);
            string currentText = (idx >= 0) ? options[idx] : "(none)";

            Text valueText = null;

            void MakeArrow(string glyph, float anchorX, Vector2 pivot, Action click)
            {
                var go = Rect(glyph, rightGo.transform);
                var r = (RectTransform)go.transform;
                r.anchorMin = new Vector2(anchorX, 0);
                r.anchorMax = new Vector2(anchorX, 1);
                r.pivot = pivot;
                r.sizeDelta = new Vector2(btnW, 0);
                r.anchoredPosition = Vector2.zero;
                var bg = SolidImage(go, new Color(0, 0, 0, 0));
                bg.raycastTarget = true;
                var t = labelChild(go.transform, glyph, (int)LabelFontSize, TextAnchor.MiddleCenter, Theme.TextMuted);
                HoverFill(go, bg, Theme.RowBgHover, new Color(0, 0, 0, 0));
                ClickHandler.Attach(go, click);
            }

            var valGo = Rect("Value", rightGo.transform);
            var valRect = (RectTransform)valGo.transform;
            valRect.anchorMin = new Vector2(0, 0);
            valRect.anchorMax = new Vector2(1, 1);
            valRect.offsetMin = new Vector2(btnW + 4, 0);
            valRect.offsetMax = new Vector2(-(btnW + 4), 0);
            valueText = valGo.AddComponent<Text>();
            valueText.text = currentText;
            valueText.font = Theme.Font;
            valueText.fontSize = (int)LabelFontSize;
            valueText.color = Theme.Text;
            valueText.alignment = TextAnchor.MiddleCenter;
            valueText.raycastTarget = false;
            valueText.horizontalOverflow = HorizontalWrapMode.Overflow;

            MakeArrow("◂", 0f, new Vector2(0, 0.5f), () => {
                if (options == null || options.Count == 0) return;
                idx = (idx - 1 + options.Count) % options.Count;
                valueText.text = options[idx];
                onChange?.Invoke(idx);
            });
            MakeArrow("▸", 1f, new Vector2(1, 0.5f), () => {
                if (options == null || options.Count == 0) return;
                idx = (idx + 1) % options.Count;
                valueText.text = options[idx];
                onChange?.Invoke(idx);
            });

            return row;
        }

        // Inline dropdown — label on left, current value + chevron on right. Clicking the
        // row expands an option list beneath it (inside the scroll content, so nothing to
        // clip against RectMask2D or float over the panel). Selecting an option collapses
        // the list and fires onChange. Better than CycleSelector for long option lists.
        public static GameObject Dropdown(
            Transform parent,
            string label,
            IList<string> options,
            int currentIndex,
            Action<int> onChange)
        {
            var container = Rect("Dropdown_" + label, parent);
            var clVlg = container.AddComponent<VerticalLayoutGroup>();
            clVlg.childControlWidth = true;
            clVlg.childControlHeight = true;
            clVlg.childForceExpandWidth = true;
            clVlg.childForceExpandHeight = false;
            clVlg.spacing = 0f;

            int idx = Mathf.Clamp(currentIndex, 0, options.Count - 1);

            var header = Row(container.transform);
            var headerBg = SolidImage(header, new Color(0, 0, 0, 0));
            headerBg.raycastTarget = true;
            HoverFill(header, headerBg, Theme.RowBgHover, new Color(0, 0, 0, 0));

            var labGo = Rect("Label", header.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(140f, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = labGo.AddComponent<Text>();
            lab.text = label;
            lab.font = Theme.Font;
            lab.fontSize = (int)LabelFontSize;
            lab.color = Theme.Text;
            lab.alignment = TextAnchor.MiddleLeft;
            lab.raycastTarget = false;

            var valGo = Rect("Value", header.transform);
            var valRect = (RectTransform)valGo.transform;
            valRect.anchorMin = new Vector2(0, 0);
            valRect.anchorMax = new Vector2(1, 1);
            valRect.offsetMin = new Vector2(150f, 0);
            valRect.offsetMax = new Vector2(-8f, 0);
            var val = valGo.AddComponent<Text>();
            val.text = (options.Count > 0 ? options[idx] : "") + "  ▾";
            val.font = Theme.Font;
            val.fontSize = (int)LabelFontSize;
            val.color = Theme.Text;
            val.alignment = TextAnchor.MiddleRight;
            val.raycastTarget = false;
            val.horizontalOverflow = HorizontalWrapMode.Overflow;

            var listGo = Rect("Options", container.transform);
            var lVlg = listGo.AddComponent<VerticalLayoutGroup>();
            lVlg.childControlWidth = true;
            lVlg.childControlHeight = true;
            lVlg.childForceExpandWidth = true;
            lVlg.childForceExpandHeight = false;
            lVlg.spacing = 1f;
            lVlg.padding = new RectOffset(0, 0, 2, 4);
            listGo.SetActive(false);

            bool open = false;
            var optTexts = new Text[options.Count];

            Action close = () =>
            {
                open = false;
                listGo.SetActive(false);
                val.text = (options.Count > 0 ? options[idx] : "") + "  ▾";
            };

            for (int i = 0; i < options.Count; i++)
            {
                int oi = i;
                var opt = Rect("Opt_" + i, listGo.transform);
                var optLe = opt.AddComponent<LayoutElement>();
                optLe.preferredHeight = 26f;
                optLe.minHeight = 26f;
                var optBg = SolidImage(opt, Theme.RowBg);
                optBg.raycastTarget = true;
                HoverFill(opt, optBg, Theme.RowBgHover, Theme.RowBg);

                var t = labelChild(opt.transform, (oi == idx ? "● " : "   ") + options[oi],
                    (int)LabelFontSize, TextAnchor.MiddleLeft, oi == idx ? Theme.Text : Theme.TextMuted);
                t.rectTransform.offsetMin = new Vector2(20f, 0);
                optTexts[oi] = t;

                ClickHandler.Attach(opt, () =>
                {
                    if (oi != idx)
                    {
                        optTexts[idx].text = "   " + options[idx];
                        optTexts[idx].color = Theme.TextMuted;
                        idx = oi;
                        optTexts[idx].text = "● " + options[idx];
                        optTexts[idx].color = Theme.Text;
                        onChange?.Invoke(idx);
                    }
                    close();
                });
            }

            ClickHandler.Attach(header, () =>
            {
                open = !open;
                listGo.SetActive(open);
                val.text = (options.Count > 0 ? options[idx] : "") + (open ? "  ▴" : "  ▾");
            });

            return container;
        }

        // Single-line text input — label on left, editable field on right. Fires onCommit
        // on Enter / focus loss (not on every character). Used for free-form strings like
        // the combo display label text.
        public static GameObject TextInput(
            Transform parent,
            string label,
            string initial,
            Action<string> onCommit)
        {
            var row = Row(parent);
            const float labelW = 140f;

            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = labGo.AddComponent<Text>();
            lab.text = label;
            lab.font = Theme.Font;
            lab.fontSize = (int)LabelFontSize;
            lab.color = Theme.Text;
            lab.alignment = TextAnchor.MiddleLeft;
            lab.raycastTarget = false;

            var inGo = Rect("Input", row.transform);
            var inRect = (RectTransform)inGo.transform;
            inRect.anchorMin = new Vector2(0, 0.5f);
            inRect.anchorMax = new Vector2(1, 0.5f);
            inRect.pivot = new Vector2(0.5f, 0.5f);
            inRect.sizeDelta = new Vector2(-(labelW + 24f), 24f);
            inRect.anchoredPosition = new Vector2((labelW + 4f) * 0.5f, 0);
            var inBg = SolidImage(inGo, new Color(1, 1, 1, 0.06f));
            inBg.raycastTarget = true;

            var txtGo = Rect("Text", inGo.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(8f, 0);
            txtRect.offsetMax = new Vector2(-8f, 0);
            var txt = txtGo.AddComponent<Text>();
            txt.font = Theme.Font;
            txt.fontSize = (int)LabelFontSize;
            txt.color = Theme.Text;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.supportRichText = false;
            txt.raycastTarget = false;

            var input = inGo.AddComponent<InputField>();
            input.textComponent = txt;
            input.contentType = InputField.ContentType.Standard;
            input.lineType = InputField.LineType.SingleLine;
            input.caretWidth = 1;
            input.customCaretColor = true;
            input.caretColor = Theme.Text;
            input.selectionColor = new Color(Theme.ToggleOn.r, Theme.ToggleOn.g, Theme.ToggleOn.b, 0.45f);
            input.text = initial ?? "";

            input.onEndEdit.AddListener(s => onCommit?.Invoke(s));

            return row;
        }

        // Segmented control — label on left, fixed-width buttons on the right with one active.
        // For small enums like OverlayPosition (Left|Right).
        public static GameObject Segmented(
            Transform parent,
            string label,
            int currentIdx,
            string[] options,
            Action<int> onChange)
        {
            var row = Row(parent);
            const float labelW = 140f;
            const float segW = 52f;
            const float gap = 2f;

            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = labGo.AddComponent<Text>();
            lab.text = label;
            lab.font = Theme.Font;
            lab.fontSize = (int)LabelFontSize;
            lab.color = Theme.Text;
            lab.alignment = TextAnchor.MiddleLeft;
            lab.raycastTarget = false;

            var rightGo = Rect("Segs", row.transform);
            var rr = (RectTransform)rightGo.transform;
            rr.anchorMin = new Vector2(1, 0);
            rr.anchorMax = new Vector2(1, 1);
            rr.pivot = new Vector2(1, 0.5f);
            float totalW = segW * options.Length + gap * (options.Length - 1);
            rr.sizeDelta = new Vector2(totalW, 0);
            rr.anchoredPosition = new Vector2(-8f, 0);

            int active = Mathf.Clamp(currentIdx, 0, options.Length - 1);
            const float segHeight = 22f;
            var bgs = new RoundedRectGraphic[options.Length];
            var accentMarks = new AccentFill[options.Length];

            for (int i = 0; i < options.Length; i++)
            {
                int captured = i;
                var seg = Rect("Seg" + i, rightGo.transform);
                var sr = (RectTransform)seg.transform;
                // Vertically center inside the row rather than stretching — gives the segments
                // a chip-like look that's clearly shorter than the row.
                sr.anchorMin = new Vector2(0, 0.5f);
                sr.anchorMax = new Vector2(0, 0.5f);
                sr.pivot = new Vector2(0, 0.5f);
                sr.sizeDelta = new Vector2(segW, segHeight);
                sr.anchoredPosition = new Vector2(i * (segW + gap), 0);

                var bg = seg.AddComponent<RoundedRectGraphic>();
                bg.Radius = 3f;
                bg.AAFringe = 0.5f;
                bg.color = i == active ? Theme.ToggleOn : Theme.ButtonBg;
                bg.raycastTarget = true;
                bgs[i] = bg;
                var af = seg.AddComponent<AccentFill>();
                af.Active = (i == active);
                accentMarks[i] = af;

                labelChild(seg.transform, options[i], (int)LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);

                ClickHandler.Attach(seg, () => {
                    if (captured == active) return;
                    active = captured;
                    for (int j = 0; j < bgs.Length; j++)
                    {
                        bool on = j == active;
                        bgs[j].color = on ? Theme.ToggleOn : Theme.ButtonBg;
                        accentMarks[j].Active = on;
                    }
                    onChange?.Invoke(active);
                });
            }

            return row;
        }

        // Expandable section — like Collapsible but with no radio toggle. Just a clickable
        // header that toggles a body open/closed. Used as a structural grouping inside other
        // bodies (e.g. wrapping a gradient editor under a Color sub-header).
        public static GameObject ExpandSection(
            Transform parent,
            string title,
            Action<Transform> buildBody)
        {
            var container = Rect("Section_" + title, parent);
            var clVlg = container.AddComponent<VerticalLayoutGroup>();
            clVlg.childControlWidth = true;
            clVlg.childControlHeight = true;
            clVlg.childForceExpandWidth = true;
            clVlg.childForceExpandHeight = false;
            clVlg.spacing = 0f;

            var header = Rect("Header", container.transform);
            var headerLe = header.AddComponent<LayoutElement>();
            headerLe.preferredHeight = RowHeight;
            headerLe.minHeight = RowHeight;
            var headerBg = SolidImage(header, new Color(0, 0, 0, 0));
            headerBg.raycastTarget = true;

            var arrowGo = Rect("Arrow", header.transform);
            var arrowRect = (RectTransform)arrowGo.transform;
            arrowRect.anchorMin = new Vector2(0, 0);
            arrowRect.anchorMax = new Vector2(0, 1);
            arrowRect.pivot = new Vector2(0, 0.5f);
            arrowRect.sizeDelta = new Vector2(24f, 0);
            arrowRect.anchoredPosition = new Vector2(2f, 0);
            var chevron = labelChild(arrowGo.transform, "▶", 15, TextAnchor.MiddleCenter, Theme.TextMuted);

            var titleGo = Rect("Title", header.transform);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(30f, 0);
            titleRect.offsetMax = new Vector2(-8f, 0);
            var titleBg = SolidImage(titleGo, new Color(0, 0, 0, 0));
            titleBg.raycastTarget = true;
            labelChild(titleGo.transform, title, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            var bodyGo = Rect("Body", container.transform);
            var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
            bodyVlg.childControlWidth = true;
            bodyVlg.childControlHeight = true;
            bodyVlg.childForceExpandWidth = true;
            bodyVlg.childForceExpandHeight = false;
            bodyVlg.spacing = 2f;
            bodyVlg.padding = new RectOffset(24, 0, 2, 6);
            var bodyLe = bodyGo.AddComponent<LayoutElement>();
            bodyLe.preferredHeight = -1f;
            var bodyCg = bodyGo.AddComponent<CanvasGroup>();
            bodyCg.alpha = 0f;
            bodyGo.AddComponent<RectMask2D>();
            buildBody(bodyGo.transform);
            bodyGo.SetActive(false);

            var animator = bodyGo.AddComponent<ExpandAnimator>();
            animator.Body = (RectTransform)bodyGo.transform;
            animator.BodyLe = bodyLe;
            animator.BodyCg = bodyCg;
            animator.Chevron = chevron.rectTransform;

            bool expanded = false;
            Action toggleExpand = () => {
                expanded = !expanded;
                animator.Set(expanded);
            };
            ClickHandler.Attach(titleGo, toggleExpand);
            ClickHandler.Attach(arrowGo, toggleExpand);
            ClickHandler.Attach(header, toggleExpand);
            HoverFill(header, headerBg, Theme.RowBgHover, new Color(0, 0, 0, 0));

            return container;
        }

        // Gradient editor. Solid toggle hides the Stops list and shows a single ColorPicker
        // bound to Stops[0] (matches ColorGradient.Evaluate's solid-mode behavior). Stops have
        // numbered headers with a live position preview. Add/Remove triggers a full stops-list
        // rebuild so numbering stays correct.
        public static GameObject GradientEditor(
            Transform parent,
            string title,
            object gradient,
            Action onChange)
        {
            return ExpandSection(parent, title, body =>
            {
                var grad = (Bismuth.ColorGradient)gradient;

                GameObject stopsSection = null;
                GameObject solidPicker = null;

                Collapsible(body, "Solid", grad.IsSolid, v => {
                    grad.IsSolid = v;
                    if (stopsSection != null) stopsSection.SetActive(!v);
                    if (solidPicker != null) solidPicker.SetActive(v);
                    onChange?.Invoke();
                }, null);

                stopsSection = ExpandSection(body, "Stops", stopsBody =>
                {
                    var stopsList = Rect("StopsList", stopsBody);
                    var listVlg = stopsList.AddComponent<VerticalLayoutGroup>();
                    listVlg.childControlWidth = true;
                    listVlg.childControlHeight = true;
                    listVlg.childForceExpandWidth = true;
                    listVlg.childForceExpandHeight = false;
                    listVlg.spacing = 2f;

                    Action rebuild = null;
                    rebuild = () =>
                    {
                        // Detach + destroy all existing stop rows so we can re-number them.
                        for (int i = stopsList.transform.childCount - 1; i >= 0; i--)
                        {
                            var c = stopsList.transform.GetChild(i);
                            c.SetParent(null);
                            UnityEngine.Object.Destroy(c.gameObject);
                        }
                        for (int i = 0; i < grad.Stops.Count; i++)
                        {
                            BuildStopSection(stopsList.transform, i + 1, grad.Stops[i], grad, onChange, rebuild);
                        }
                    };
                    rebuild();

                    Button(stopsBody, "+ Add stop", () =>
                    {
                        grad.Stops.Add(new Bismuth.ColorStop
                        {
                            Progress = 1f, R = 1f, G = 1f, B = 1f, A = 1f
                        });
                        rebuild();
                        onChange?.Invoke();
                    });
                });

                // Solid-mode picker. Bound to Stops[0] since that's what ColorGradient.Evaluate
                // returns when IsSolid is true. Creates a stop on first edit if Stops is empty.
                Color firstColor = grad.Stops.Count > 0
                    ? new Color(grad.Stops[0].R, grad.Stops[0].G, grad.Stops[0].B, grad.Stops[0].A)
                    : Color.white;
                solidPicker = ColorPicker(body, "Color", firstColor, true, c =>
                {
                    if (grad.Stops.Count == 0)
                    {
                        grad.Stops.Add(new Bismuth.ColorStop
                        {
                            Progress = 0f, R = c.r, G = c.g, B = c.b, A = c.a
                        });
                    }
                    else
                    {
                        grad.Stops[0].R = c.r; grad.Stops[0].G = c.g;
                        grad.Stops[0].B = c.b; grad.Stops[0].A = c.a;
                    }
                    onChange?.Invoke();
                });

                // Initial visibility from the saved Solid flag.
                stopsSection.SetActive(!grad.IsSolid);
                solidPicker.SetActive(grad.IsSolid);

                var perfectColor = new Color(grad.PR, grad.PG, grad.PB, grad.PA);
                Collapsible(body, "Perfect color (t=1)", grad.HasPerfectColor, v =>
                {
                    grad.HasPerfectColor = v;
                    onChange?.Invoke();
                }, perfectBody =>
                {
                    ColorPicker(perfectBody, "Color", perfectColor, true, c =>
                    {
                        grad.PR = c.r; grad.PG = c.g; grad.PB = c.b; grad.PA = c.a;
                        onChange?.Invoke();
                    });
                });
            });
        }

        // Per-stop section: header has "Stop N" + a live position preview text (right-aligned).
        // Body has Position slider + ColorPicker + Remove. Slider's onChange updates the header
        // text in real-time so users can see the current position without opening the stop.
        private static GameObject BuildStopSection(
            Transform parent,
            int displayNumber,
            Bismuth.ColorStop stop,
            Bismuth.ColorGradient grad,
            Action onChange,
            Action rebuild)
        {
            var container = Rect("Stop_" + displayNumber, parent);
            var vlg = container.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;

            var header = Rect("Header", container.transform);
            var headerLe = header.AddComponent<LayoutElement>();
            headerLe.preferredHeight = RowHeight;
            headerLe.minHeight = RowHeight;
            var headerBg = SolidImage(header, new Color(0, 0, 0, 0));
            headerBg.raycastTarget = true;

            var arrowGo = Rect("Arrow", header.transform);
            var arrowRect = (RectTransform)arrowGo.transform;
            arrowRect.anchorMin = new Vector2(0, 0);
            arrowRect.anchorMax = new Vector2(0, 1);
            arrowRect.pivot = new Vector2(0, 0.5f);
            arrowRect.sizeDelta = new Vector2(24f, 0);
            arrowRect.anchoredPosition = new Vector2(2f, 0);
            var chevron = labelChild(arrowGo.transform, "▶", 15, TextAnchor.MiddleCenter, Theme.TextMuted);

            var titleGo = Rect("Title", header.transform);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(30f, 0);
            titleRect.offsetMax = new Vector2(-80f, 0);
            var titleBg = SolidImage(titleGo, new Color(0, 0, 0, 0));
            titleBg.raycastTarget = true;
            labelChild(titleGo.transform, "Stop " + displayNumber, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            // Position preview (right-aligned, muted) — live-updated by the body's position slider.
            var posGo = Rect("Pos", header.transform);
            var posRect = (RectTransform)posGo.transform;
            posRect.anchorMin = new Vector2(1, 0);
            posRect.anchorMax = new Vector2(1, 1);
            posRect.pivot = new Vector2(1, 0.5f);
            posRect.sizeDelta = new Vector2(72f, 0);
            posRect.anchoredPosition = new Vector2(-12f, 0);
            var posText = posGo.AddComponent<Text>();
            posText.font = Theme.Font;
            posText.fontSize = (int)LabelFontSize;
            posText.color = Theme.TextMuted;
            posText.alignment = TextAnchor.MiddleRight;
            posText.text = stop.Progress.ToString("0.00");
            posText.raycastTarget = false;

            var bodyGo = Rect("Body", container.transform);
            var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
            bodyVlg.childControlWidth = true;
            bodyVlg.childControlHeight = true;
            bodyVlg.childForceExpandWidth = true;
            bodyVlg.childForceExpandHeight = false;
            bodyVlg.spacing = 2f;
            bodyVlg.padding = new RectOffset(24, 0, 2, 6);
            var bodyLe = bodyGo.AddComponent<LayoutElement>();
            bodyLe.preferredHeight = -1f;
            var bodyCg = bodyGo.AddComponent<CanvasGroup>();
            bodyCg.alpha = 0f;
            bodyGo.AddComponent<RectMask2D>();

            Slider(bodyGo.transform, "Position", stop.Progress, 0f, 1f, v =>
            {
                stop.Progress = v;
                posText.text = v.ToString("0.00");
                onChange?.Invoke();
            }, "0.00");

            var stopColor = new Color(stop.R, stop.G, stop.B, stop.A);
            ColorPicker(bodyGo.transform, "Color", stopColor, true, c =>
            {
                stop.R = c.r; stop.G = c.g; stop.B = c.b; stop.A = c.a;
                onChange?.Invoke();
            });

            Button(bodyGo.transform, "Remove this stop", () =>
            {
                grad.Stops.Remove(stop);
                rebuild?.Invoke();
                onChange?.Invoke();
            });

            bodyGo.SetActive(false);

            var animator = bodyGo.AddComponent<ExpandAnimator>();
            animator.Body = (RectTransform)bodyGo.transform;
            animator.BodyLe = bodyLe;
            animator.BodyCg = bodyCg;
            animator.Chevron = chevron.rectTransform;

            bool expanded = false;
            Action toggleExpand = () => {
                expanded = !expanded;
                animator.Set(expanded);
            };
            ClickHandler.Attach(titleGo, toggleExpand);
            ClickHandler.Attach(arrowGo, toggleExpand);
            ClickHandler.Attach(header, toggleExpand);
            HoverFill(header, headerBg, Theme.RowBgHover, new Color(0, 0, 0, 0));

            return container;
        }

        // Color picker. Header (clickable to expand): arrow + label + HEX + swatch preview.
        // Body: editable HEX field + R/G/B(/A) sliders. Each control round-trips through a
        // shared RefreshDisplay so dragging a slider updates HEX/swatch and vice versa.
        public static GameObject ColorPicker(
            Transform parent,
            string label,
            Color initial,
            bool hasAlpha,
            Action<Color> onChange)
        {
            var container = Rect("ColorPicker", parent);
            var clVlg = container.AddComponent<VerticalLayoutGroup>();
            clVlg.childControlWidth = true;
            clVlg.childControlHeight = true;
            clVlg.childForceExpandWidth = true;
            clVlg.childForceExpandHeight = false;
            clVlg.spacing = 0f;

            Color current = initial;
            bool expanded = false;

            // Header
            var header = Rect("Header", container.transform);
            var headerLe = header.AddComponent<LayoutElement>();
            headerLe.preferredHeight = RowHeight;
            headerLe.minHeight = RowHeight;
            var headerBg = SolidImage(header, new Color(0, 0, 0, 0));
            headerBg.raycastTarget = true;

            // Chevron
            var arrowGo = Rect("Arrow", header.transform);
            var arrowRect = (RectTransform)arrowGo.transform;
            arrowRect.anchorMin = new Vector2(0, 0);
            arrowRect.anchorMax = new Vector2(0, 1);
            arrowRect.pivot = new Vector2(0, 0.5f);
            arrowRect.sizeDelta = new Vector2(24f, 0);
            arrowRect.anchoredPosition = new Vector2(2f, 0);
            var chevron = labelChild(arrowGo.transform, "▶", 15, TextAnchor.MiddleCenter, Theme.TextMuted);

            // Label
            var titleGo = Rect("Title", header.transform);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(0, 1);
            titleRect.pivot = new Vector2(0, 0.5f);
            titleRect.sizeDelta = new Vector2(180f, 0);
            titleRect.anchoredPosition = new Vector2(30f, 0);
            labelChild(titleGo.transform, label, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            // Swatch preview (right)
            const float swatchSize = 22f;
            var swatchGo = Rect("Swatch", header.transform);
            var swatchRect = (RectTransform)swatchGo.transform;
            swatchRect.anchorMin = new Vector2(1, 0.5f);
            swatchRect.anchorMax = new Vector2(1, 0.5f);
            swatchRect.pivot = new Vector2(1, 0.5f);
            swatchRect.anchoredPosition = new Vector2(-8f, 0);
            swatchRect.sizeDelta = new Vector2(swatchSize, swatchSize);
            var swatchImg = swatchGo.AddComponent<Image>();
            swatchImg.sprite = Theme.White;
            swatchImg.color = current;
            swatchImg.raycastTarget = false;

            // HEX text (right of label, left of swatch)
            var hexGo = Rect("Hex", header.transform);
            var hexRect = (RectTransform)hexGo.transform;
            hexRect.anchorMin = new Vector2(1, 0);
            hexRect.anchorMax = new Vector2(1, 1);
            hexRect.pivot = new Vector2(1, 0.5f);
            hexRect.sizeDelta = new Vector2(110f, 0);
            hexRect.anchoredPosition = new Vector2(-(swatchSize + 16f), 0);
            var hexText = hexGo.AddComponent<Text>();
            hexText.font = Theme.Font;
            hexText.fontSize = (int)LabelFontSize;
            hexText.color = Theme.TextMuted;
            hexText.alignment = TextAnchor.MiddleRight;
            hexText.raycastTarget = false;

            // Body
            var bodyGo = Rect("Body", container.transform);
            var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
            bodyVlg.childControlWidth = true;
            bodyVlg.childControlHeight = true;
            bodyVlg.childForceExpandWidth = true;
            bodyVlg.childForceExpandHeight = false;
            bodyVlg.spacing = 2f;
            bodyVlg.padding = new RectOffset(24, 0, 2, 6);
            var bodyLe = bodyGo.AddComponent<LayoutElement>();
            bodyLe.preferredHeight = -1f;
            var bodyCg = bodyGo.AddComponent<CanvasGroup>();
            bodyCg.alpha = 0f;
            bodyGo.AddComponent<RectMask2D>();

            // --- HEX input row ---
            InputField hexInput = null;
            {
                var hexRow = Row(bodyGo.transform);
                const float labelW = 140f;

                var lblGo = Rect("Lbl", hexRow.transform);
                var lblRect = (RectTransform)lblGo.transform;
                lblRect.anchorMin = new Vector2(0, 0);
                lblRect.anchorMax = new Vector2(0, 1);
                lblRect.pivot = new Vector2(0, 0.5f);
                lblRect.sizeDelta = new Vector2(labelW, 0);
                lblRect.anchoredPosition = new Vector2(8f, 0);
                labelChild(lblGo.transform, "HEX", (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

                var inGo = Rect("HexInput", hexRow.transform);
                var inRect = (RectTransform)inGo.transform;
                inRect.anchorMin = new Vector2(0, 0.5f);
                inRect.anchorMax = new Vector2(1, 0.5f);
                inRect.pivot = new Vector2(0.5f, 0.5f);
                inRect.sizeDelta = new Vector2(-(labelW + 24f), 24f);
                inRect.anchoredPosition = new Vector2((labelW + 4f) * 0.5f, 0);
                var inBg = SolidImage(inGo, new Color(1, 1, 1, 0.06f));
                inBg.raycastTarget = true;

                var inTxtGo = Rect("Text", inGo.transform);
                var inTxtRect = (RectTransform)inTxtGo.transform;
                inTxtRect.anchorMin = Vector2.zero;
                inTxtRect.anchorMax = Vector2.one;
                inTxtRect.offsetMin = new Vector2(8f, 0);
                inTxtRect.offsetMax = new Vector2(-8f, 0);
                var inTxt = inTxtGo.AddComponent<Text>();
                inTxt.font = Theme.Font;
                inTxt.fontSize = (int)LabelFontSize;
                inTxt.color = Theme.Text;
                inTxt.alignment = TextAnchor.MiddleLeft;
                inTxt.supportRichText = false;
                inTxt.raycastTarget = false;

                hexInput = inGo.AddComponent<InputField>();
                hexInput.textComponent = inTxt;
                hexInput.contentType = InputField.ContentType.Alphanumeric;
                hexInput.lineType = InputField.LineType.SingleLine;
                hexInput.characterLimit = 9;
                hexInput.caretWidth = 1;
                hexInput.customCaretColor = true;
                hexInput.caretColor = Theme.Text;
                hexInput.selectionColor = new Color(Theme.ToggleOn.r, Theme.ToggleOn.g, Theme.ToggleOn.b, 0.45f);
            }

            // --- RGB(A) sliders ---
            // We use the existing Slider factory but bypass its individual onChange:
            // every channel routes through a single ApplyComponent → RefreshDisplay → notify.
            SliderControl rCtrl = null, gCtrl = null, bCtrl = null, aCtrl = null;

            Action refresh = null;
            refresh = () => {
                swatchImg.color = current;
                string hex = "#" + (hasAlpha
                    ? ColorUtility.ToHtmlStringRGBA(current)
                    : ColorUtility.ToHtmlStringRGB(current));
                hexText.text = hex;
                if (hexInput != null && !hexInput.isFocused) hexInput.text = hex;
                if (rCtrl != null) { rCtrl.Value = current.r * 255f; rCtrl.ApplyVisuals(); }
                if (gCtrl != null) { gCtrl.Value = current.g * 255f; gCtrl.ApplyVisuals(); }
                if (bCtrl != null) { bCtrl.Value = current.b * 255f; bCtrl.ApplyVisuals(); }
                if (aCtrl != null) { aCtrl.Value = current.a * 255f; aCtrl.ApplyVisuals(); }
            };

            GameObject MakeChannel(string ch, Func<float> get, Action<float> set)
            {
                var row = Slider(bodyGo.transform, ch, get() * 255f, 0f, 255f, v => {
                    set(Mathf.Clamp01(v / 255f));
                    refresh();
                    onChange?.Invoke(current);
                }, "0", 1f);
                return row;
            }

            rCtrl = MakeChannel("R", () => current.r, x => current.r = x).GetComponentInChildren<SliderControl>();
            gCtrl = MakeChannel("G", () => current.g, x => current.g = x).GetComponentInChildren<SliderControl>();
            bCtrl = MakeChannel("B", () => current.b, x => current.b = x).GetComponentInChildren<SliderControl>();
            if (hasAlpha)
                aCtrl = MakeChannel("A", () => current.a, x => current.a = x).GetComponentInChildren<SliderControl>();

            // HEX commit — parse, apply, refresh + notify.
            hexInput.onEndEdit.AddListener(s => {
                string parsed = s.Trim();
                if (!parsed.StartsWith("#")) parsed = "#" + parsed;
                if (ColorUtility.TryParseHtmlString(parsed, out Color parsedColor))
                {
                    if (!hasAlpha) parsedColor.a = 1f;
                    current = parsedColor;
                    refresh();
                    onChange?.Invoke(current);
                }
                else
                {
                    // Revert display on parse failure
                    refresh();
                }
            });

            // Animator on the body, like Collapsible
            var animator = bodyGo.AddComponent<ExpandAnimator>();
            animator.Body = (RectTransform)bodyGo.transform;
            animator.BodyLe = bodyLe;
            animator.BodyCg = bodyCg;
            animator.Chevron = chevron.rectTransform;
            bodyGo.SetActive(false);

            Action toggleExpand = () => {
                expanded = !expanded;
                animator.Set(expanded);
            };
            ClickHandler.Attach(titleGo, toggleExpand);
            ClickHandler.Attach(arrowGo, toggleExpand);
            ClickHandler.Attach(header, toggleExpand);
            HoverFill(header, headerBg, Theme.RowBgHover, new Color(0, 0, 0, 0));

            refresh(); // initial paint
            return container;
        }

        // Accent color preset swatches — circular, with a ring around the selected one.
        public static GameObject AccentSwatches(
            Transform parent,
            string label,
            Color[] options,
            Color current,
            Action<Color> onChange)
        {
            var row = Row(parent, RowHeight + 8f);
            const float labelW = 140f;
            const float swatchSize = 18f;
            const float gap = 6f;

            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = labGo.AddComponent<Text>();
            lab.text = label;
            lab.font = Theme.Font;
            lab.fontSize = (int)LabelFontSize;
            lab.color = Theme.Text;
            lab.alignment = TextAnchor.MiddleLeft;
            lab.raycastTarget = false;

            var rightGo = Rect("Swatches", row.transform);
            var rightRect = (RectTransform)rightGo.transform;
            rightRect.anchorMin = new Vector2(0, 0);
            rightRect.anchorMax = new Vector2(1, 1);
            rightRect.offsetMin = new Vector2(labelW + 8f, 0);
            rightRect.offsetMax = new Vector2(-8f, 0);

            var swatchObjs = new RoundedRectGraphic[options.Length];
            var ringObjs = new GameObject[options.Length];

            int selectedIdx = -1;
            for (int i = 0; i < options.Length; i++)
            {
                if (Mathf.Approximately(options[i].r, current.r)
                 && Mathf.Approximately(options[i].g, current.g)
                 && Mathf.Approximately(options[i].b, current.b))
                {
                    selectedIdx = i;
                    break;
                }
            }

            for (int i = 0; i < options.Length; i++)
            {
                int captured = i;
                var swGo = Rect("Sw" + i, rightGo.transform);
                var swRect = (RectTransform)swGo.transform;
                swRect.anchorMin = new Vector2(0, 0.5f);
                swRect.anchorMax = new Vector2(0, 0.5f);
                swRect.pivot = new Vector2(0, 0.5f);
                swRect.sizeDelta = new Vector2(swatchSize, swatchSize);
                swRect.anchoredPosition = new Vector2(i * (swatchSize + gap), 0);
                var swImg = swGo.AddComponent<RoundedRectGraphic>();
                swImg.Radius = swatchSize * 0.5f;
                swImg.color = options[i];
                swImg.raycastTarget = true;
                swatchObjs[i] = swImg;

                // Selection ring
                var ringGo = Rect("Ring", swGo.transform);
                var ringRect = (RectTransform)ringGo.transform;
                ringRect.anchorMin = Vector2.zero;
                ringRect.anchorMax = Vector2.one;
                ringRect.offsetMin = new Vector2(-3f, -3f);
                ringRect.offsetMax = new Vector2(3f, 3f);
                var ringG = ringGo.AddComponent<RoundedRectGraphic>();
                ringG.Radius = (swatchSize + 6f) * 0.5f;
                ringG.BorderWidth = 1.5f;
                ringG.BorderColor = Theme.Text;
                ringG.color = new Color(0, 0, 0, 0);
                ringG.raycastTarget = false;
                ringGo.SetActive(i == selectedIdx);
                ringObjs[i] = ringGo;

                ClickHandler.Attach(swGo, () => {
                    for (int j = 0; j < ringObjs.Length; j++) ringObjs[j].SetActive(j == captured);
                    onChange?.Invoke(options[captured]);
                });
            }

            return row;
        }

        // Destructive-action button with two-click confirmation. First click arms it (label
        // changes to "Confirm: {label}?", bg brightens); second click within the timeout
        // fires onConfirm. Auto-reverts after 3s if not confirmed. Click elsewhere is fine —
        // arming just resets on its own timer.
        public static GameObject DangerButton(Transform parent, string label, Action onConfirm)
        {
            var row = Row(parent);
            var bg = SolidImage(row, Theme.DangerBg);
            bg.raycastTarget = true;

            var t = Label(row.transform, label, (int)LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);
            t.rectTransform.offsetMin = new Vector2(8f, 0f);
            t.rectTransform.offsetMax = new Vector2(-8f, 0f);

            HoverFill(row, bg, Theme.DangerHover, Theme.DangerBg);

            string originalLabel = label;
            bool armed = false;
            var state = row.AddComponent<DangerButtonState>();
            Action revert = () =>
            {
                armed = false;
                t.text = originalLabel;
                bg.color = Theme.DangerBg;
            };
            state.OnTimeout = revert;

            ClickHandler.Attach(row, () =>
            {
                if (!armed)
                {
                    armed = true;
                    t.text = "Click again to confirm";
                    bg.color = Theme.DangerArmed;
                    state.StartTimer(3f);
                }
                else
                {
                    state.CancelTimer();
                    revert();
                    onConfirm?.Invoke();
                }
            });

            return row;
        }

        public static GameObject Spacer(Transform parent, float height = SectionGap)
        {
            var go = Rect("Spacer", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return go;
        }

        // 1px hairline border on all four sides — sharp aesthetic.
        public static void AddBorder(GameObject parent, Color color, float thickness = 1f)
        {
            void Edge(string n, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
            {
                var go = Rect(n, parent.transform);
                var r = (RectTransform)go.transform;
                r.anchorMin = aMin;
                r.anchorMax = aMax;
                r.offsetMin = offMin;
                r.offsetMax = offMax;
                var img = go.AddComponent<Image>();
                img.sprite = Theme.White;
                img.color = color;
                img.raycastTarget = false;
            }
            Edge("BTop",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -thickness), new Vector2(0, 0));
            Edge("BBottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0),          new Vector2(0, thickness));
            Edge("BLeft",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0),          new Vector2(thickness, 0));
            Edge("BRight",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(-thickness, 0), new Vector2(0, 0));
        }

        private static void HoverFill(GameObject obj, Image target, Color hover, Color rest)
        {
            // Use HoverHandler, NOT EventTrigger — EventTrigger implements IScrollHandler and
            // absorbs the mouse-wheel event on every GameObject it's on, breaking ScrollRect
            // bubbling whenever the cursor is over a hover-tinted widget.
            var h = obj.GetComponent<HoverHandler>() ?? obj.AddComponent<HoverHandler>();
            h.OnEnter = () => target.color = hover;
            h.OnExit = () => target.color = rest;
        }
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
    internal class SliderControl : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public float Min, Max, Value;
        public RectTransform Track;
        public RectTransform Handle;
        public RectTransform Fill;
        public InputField ValueInput;
        public string Format = "0.00";
        public float Step = 0f;
        public Action<float> OnChange;

        public void OnPointerDown(PointerEventData e) { UpdateFromPointer(e); }
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
