using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static class PageInput
    {
        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            // ── Menu ───────────────────────────────────────────────────────
            UIBuilder.SectionHeader(content, "Menu");
            UIBuilder.Collapsible(content, "Block game inputs while menu is open", s.BlockInputsWhileMenuOpen,
                v => { s.BlockInputsWhileMenuOpen = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(content);

            // ── Key Limiter ────────────────────────────────────────────────
            UIBuilder.SectionHeader(content, "Key Limiter");
            UIBuilder.Collapsible(content, "Enable", s.KeyLimiterEnabled,
                v => { s.KeyLimiterEnabled = v; notify?.Invoke(); }, null);

            GameObject customContainer = null;
            UIBuilder.Collapsible(content, "Use Key Viewer keys (active preset)", s.KeyLimiterUseKvKeys,
                v =>
                {
                    s.KeyLimiterUseKvKeys = v;
                    if (customContainer != null) customContainer.SetActive(!v);
                    notify?.Invoke();
                }, null);

            // Custom-keys editor sub-container (only when not using KV preset keys).
            customContainer = UIBuilder.Rect("CustomKeys", content);
            var ccVlg = customContainer.AddComponent<VerticalLayoutGroup>();
            ccVlg.childControlWidth = true;
            ccVlg.childControlHeight = true;
            ccVlg.childForceExpandWidth = true;
            ccVlg.childForceExpandHeight = false;
            ccVlg.spacing = 4f;
            ccVlg.padding = new RectOffset(0, 0, 4, 0);
            BuildCustomKeys(customContainer.transform, s, notify);
            customContainer.SetActive(!s.KeyLimiterUseKvKeys);

            // ── Chatter Blocker ────────────────────────────────────────────
            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, "Chatter Blocker");
            UIBuilder.Collapsible(content, "Enable", s.ChatterBlockerEnabled,
                v => { s.ChatterBlockerEnabled = v; notify?.Invoke(); }, null);
            UIBuilder.IntSlider(content, "Threshold (ms)", s.ChatterThresholdMs, 1, 200,
                v => { s.ChatterThresholdMs = v; notify?.Invoke(); });
        }

        // Allowed-keys editor: a Listen toggle + a flow of chip buttons. Clicking a chip
        // removes that key. Listening captures the next key press and adds (or removes if
        // already present) the corresponding token to settings.KeyLimiterCustomKeys.
        private static void BuildCustomKeys(Transform parent, Settings s, Action notify)
        {
            // Strip uses manual flow layout (no LayoutGroup) so chips wrap to new lines
            // when they overflow horizontally. Strip's preferredHeight is set dynamically
            // based on the computed line count.
            var stripGo = UIBuilder.Rect("Strip", parent);
            var stripLe = stripGo.AddComponent<LayoutElement>();
            stripLe.preferredHeight = 28f;
            stripLe.minHeight = 28f;

            // Persistent listener — its Active flag toggles via the Listen chip.
            var listenerGo = UIBuilder.Rect("Listener", parent);
            listenerGo.SetActive(true);
            var listener = listenerGo.AddComponent<KeyListener>();

            const float chipH = 22f;
            const float lineSpacing = 4f;
            const float lineHeight = chipH + lineSpacing;
            const float chipSpacing = 4f;

            Action layoutChips = () =>
            {
                var stripRt = (RectTransform)stripGo.transform;
                float availW = stripRt.rect.width;
                if (availW <= 0f) availW = 400f; // initial frame; will reflow when real width arrives
                float x = 0f;
                int line = 0;
                for (int i = 0; i < stripRt.childCount; i++)
                {
                    var child = (RectTransform)stripRt.GetChild(i);
                    var cle = child.GetComponent<LayoutElement>();
                    float w = cle != null ? cle.preferredWidth : 60f;
                    if (x > 0f && x + w > availW)
                    {
                        line++;
                        x = 0f;
                    }
                    child.anchorMin = new Vector2(0, 1);
                    child.anchorMax = new Vector2(0, 1);
                    child.pivot = new Vector2(0, 1);
                    child.anchoredPosition = new Vector2(x, -line * lineHeight);
                    child.sizeDelta = new Vector2(w, chipH);
                    x += w + chipSpacing;
                }
                float totalH = (line + 1) * chipH + line * lineSpacing;
                if (!Mathf.Approximately(stripLe.preferredHeight, totalH))
                {
                    stripLe.preferredHeight = totalH;
                    stripLe.minHeight = totalH;
                }
            };

            // Reflow when the strip's width changes (panel resize, scale change, etc.)
            var rc = stripGo.AddComponent<RectChanged>();
            rc.OnChange = () => layoutChips();

            Action rebuild = null;
            rebuild = () =>
            {
                for (int i = stripGo.transform.childCount - 1; i >= 0; i--)
                {
                    var c = stripGo.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }

                var listenChip = MakeChip(stripGo.transform, listener.Active ? Loc.T("■ Stop") : Loc.T("● Listen"),
                    listener.Active, () =>
                    {
                        listener.Active = !listener.Active;
                        rebuild();
                    });
                // Clicking Stop while listening must stop, not bind LMB to the strip.
                if (listener.Active) listener.CancelRect = (RectTransform)listenChip.transform;

                var tokens = ParseTokens(s.KeyLimiterCustomKeys);
                foreach (var tok in tokens)
                {
                    string label = "× " + KeyTokens.PrettyTokenLabel(tok);
                    string captured = tok;
                    MakeChip(stripGo.transform, label, false, () =>
                    {
                        tokens.Remove(captured);
                        s.KeyLimiterCustomKeys = string.Join(" ", tokens.ToArray());
                        rebuild();
                        notify?.Invoke();
                    });
                }
                layoutChips();
            };

            listener.OnKey = kc =>
            {
                if (kc == KeyCode.Escape) { listener.Active = false; rebuild(); return; }
                string tok = KeyTokens.TokenFromKeyCode(kc);
                var tokens = ParseTokens(s.KeyLimiterCustomKeys);
                int existing = tokens.IndexOf(tok);
                if (existing >= 0) tokens.RemoveAt(existing);
                else tokens.Add(tok);
                s.KeyLimiterCustomKeys = string.Join(" ", tokens.ToArray());
                rebuild();
                notify?.Invoke();
            };

            rebuild();
        }

        // Compact chip — auto-width based on a rough character count. Sharp rounded look
        // matches the Segmented buttons. Size is set by the flow layouter, not the LayoutElement.
        private static GameObject MakeChip(Transform parent, string text, bool active, Action onClick)
        {
            var go = UIBuilder.Rect("Chip", parent);
            float width = Mathf.Max(36f, text.Length * 8f + 14f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            bg.color = active ? Theme.ToggleOn : Theme.ButtonBg;
            bg.raycastTarget = true;
            if (active) go.AddComponent<AccentFill>();

            var txtGo = UIBuilder.Rect("T", go.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(6f, 0f);
            txtRect.offsetMax = new Vector2(-6f, 0f);
            var txt = UIBuilder.Tmp(txtGo, text, (int)UIBuilder.LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);

            ClickHandler.Attach(go, onClick);
            return go;
        }

        private static List<string> ParseTokens(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var t in s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                list.Add(t);
            return list;
        }
    }

    // Fires when its RectTransform's dimensions change (resize, scale, layout pass). Used
    // to re-flow chips whenever the strip's width changes.
    internal class RectChanged : MonoBehaviour
    {
        public Action OnChange;
        private void OnRectTransformDimensionsChange() { OnChange?.Invoke(); }
    }

    // Per-frame key polling. Active flag is flipped by the Listen chip. Only fires once
    // per key-down event; consumer is expected to clear / re-enable as needed.
    internal class KeyListener : MonoBehaviour
    {
        public Action<KeyCode> OnKey;

        // Rect of the control that armed this listen. A click inside it cancels the
        // listen instead of binding a mouse button — reported to OnKey as Escape, which
        // is the cancel every consumer already implements.
        public RectTransform CancelRect;

        private bool _active;
        private int _armedFrame = -1;
        private static int _activeCount;

        public bool Active
        {
            get { return _active; }
            set
            {
                if (_active == value) return;
                _active = value;
                _activeCount += value ? 1 : -1;
                // Down and up of one click can land in the same frame; without this the
                // click that arms a listen would immediately bind itself as LMB/RMB.
                if (value) _armedFrame = Time.frameCount;
            }
        }

        private void OnDisable() { Active = false; }   // also covers Destroy

        // While a bind is pending, every panel click belongs to the listener. The captured
        // click's release must not reach the widget under it either, hence the grace
        // window — it is a deadline, not a latch, so a destroyed listener can't wedge the UI.
        internal static float SwallowClicksUntil;
        internal static bool ClicksSwallowed
        {
            get { return _activeCount > 0 || Time.unscaledTime < SwallowClicksUntil; }
        }

        private static readonly KeyCode[] Watched = BuildWatched();

        // Every keyboard KeyCode, not a hand-picked list — a curated one silently drops
        // whatever it forgot (Menu, keypad, Print…). Filter by name, not by value: the
        // keycodes are not one contiguous block (F16-F24 sit at 670+, well past the
        // Mouse/Joystick range at 323-509). Mouse buttons are handled separately below.
        private static KeyCode[] BuildWatched()
        {
            var list = new List<KeyCode>();
            var seen = new HashSet<KeyCode>();   // Meta/Command/Apple share values
            foreach (string name in Enum.GetNames(typeof(KeyCode)))
            {
                if (name.StartsWith("Mouse") || name.StartsWith("Joystick") || name.StartsWith("Wheel"))
                    continue;
                var k = (KeyCode)Enum.Parse(typeof(KeyCode), name);
                if (k == KeyCode.None) continue;
                if (seen.Add(k)) list.Add(k);
            }
            return list.ToArray();
        }

        // LMB / RMB are bindable like any other key. Returns true once the click is spoken for.
        private bool TryMouse()
        {
            KeyCode btn = Input.GetMouseButtonDown(0) ? KeyCode.Mouse0
                        : Input.GetMouseButtonDown(1) ? KeyCode.Mouse1
                        : KeyCode.None;
            if (btn == KeyCode.None) return false;

            SwallowClicksUntil = Time.unscaledTime + 0.5f;
            bool onSelf = CancelRect != null && RectTransformUtility.RectangleContainsScreenPoint(
                CancelRect, Input.mousePosition, null);   // ScreenSpaceOverlay → no camera
            OnKey(onSelf ? KeyCode.Escape : btn);
            return true;
        }

        private static readonly HashSet<KeyCode> _asyncDown = new HashSet<KeyCode>();

        private void Update()
        {
            if (!_active || OnKey == null) return;
            if (Time.frameCount > _armedFrame && TryMouse()) return;
            // Capture happens while the menu is open, i.e. exactly when the raw
            // GetKeyDown block is engaged — exempt these reads.
            KeyLimiter.RawReadExempt = true;
            try
            {
                // Legacy polling is blind on some platforms (Proton/X11) while the game's
                // async press list keeps working — consult both.
                _asyncDown.Clear();
                KeyLimiter.CollectStateKeys(KeyLimiter.StateWentDown, _asyncDown);
                for (int i = 0; i < Watched.Length; i++)
                {
                    var k = Watched[i];
                    if (Input.GetKeyDown(k) || _asyncDown.Contains(k))
                    {
                        OnKey(k);
                        return;
                    }
                }
            }
            finally
            {
                KeyLimiter.RawReadExempt = false;
            }
        }
    }
}
