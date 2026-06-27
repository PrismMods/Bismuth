using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static class PageInput
    {
        public static void Build(RectTransform content)
        {
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

                MakeChip(stripGo.transform, listener.Active ? "■ Stop" : "● Listen",
                    listener.Active, () =>
                    {
                        listener.Active = !listener.Active;
                        rebuild();
                    });

                var tokens = ParseTokens(s.KeyLimiterCustomKeys);
                foreach (var tok in tokens)
                {
                    string label = "× " + PrettyTokenLabel(tok);
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
                if (kc == KeyCode.Escape) return;
                string tok = TokenFromKeyCode(kc);
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

        // Friendly display for a token.
        private static string PrettyTokenLabel(string token)
        {
            if (!KeyViewer.TryParseKey(token, out KeyCode kc)) return token;
            switch (kc)
            {
                case KeyCode.LeftShift:    return "LShift";
                case KeyCode.RightShift:   return "RShift";
                case KeyCode.LeftControl:  return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt:      return "LAlt";
                case KeyCode.RightAlt:     return "RAlt";
                case KeyCode.LeftCommand:  return "LCmd";
                case KeyCode.RightCommand: return "RCmd";
                case KeyCode.CapsLock:     return "Caps";
                case KeyCode.Return:       return "Enter";
                case KeyCode.Backspace:    return "Back";
                case KeyCode.Escape:       return "Esc";
                case KeyCode.UpArrow:      return "↑";
                case KeyCode.DownArrow:    return "↓";
                case KeyCode.LeftArrow:    return "←";
                case KeyCode.RightArrow:   return "→";
                default:                   return token;
            }
        }

        // Captured-key → storage token. Mirrors KeyTokens.TokenFromKeyCode (subset).
        private static string TokenFromKeyCode(KeyCode kc)
        {
            switch (kc)
            {
                case KeyCode.Tab:          return "Tab";
                case KeyCode.CapsLock:     return "Caps";
                case KeyCode.Space:        return "Space";
                case KeyCode.Return:       return "Enter";
                case KeyCode.Backspace:    return "Backspace";
                case KeyCode.Escape:       return "Escape";
                case KeyCode.LeftShift:    return "LShift";
                case KeyCode.RightShift:   return "RShift";
                case KeyCode.LeftControl:  return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt:      return "LAlt";
                case KeyCode.RightAlt:     return "RAlt";
                case KeyCode.LeftCommand:  return "LCmd";
                case KeyCode.RightCommand: return "RCmd";
                case KeyCode.UpArrow:      return "Up";
                case KeyCode.DownArrow:    return "Down";
                case KeyCode.LeftArrow:    return "Left";
                case KeyCode.RightArrow:   return "Right";
                case KeyCode.LeftBracket:  return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Backslash:    return "\\";
                case KeyCode.Semicolon:    return ";";
                case KeyCode.Quote:        return "'";
                case KeyCode.Comma:        return ",";
                case KeyCode.Period:       return ".";
                case KeyCode.Slash:        return "/";
                case KeyCode.BackQuote:    return "`";
                case KeyCode.Minus:        return "-";
                case KeyCode.Equals:       return "=";
            }
            // Alpha0–Alpha9 → "0".."9"; A–Z stays as-is via ToString(); F1-F12 likewise.
            if (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9)
                return ((int)(kc - KeyCode.Alpha0)).ToString();
            return kc.ToString();
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
        public bool Active;
        public Action<KeyCode> OnKey;

        private static readonly KeyCode[] Watched = BuildWatched();

        private static KeyCode[] BuildWatched()
        {
            var list = new List<KeyCode>();
            for (int k = (int)KeyCode.A; k <= (int)KeyCode.Z; k++) list.Add((KeyCode)k);
            for (int k = (int)KeyCode.Alpha0; k <= (int)KeyCode.Alpha9; k++) list.Add((KeyCode)k);
            for (int k = (int)KeyCode.F1; k <= (int)KeyCode.F12; k++) list.Add((KeyCode)k);
            list.AddRange(new[]
            {
                KeyCode.LeftShift, KeyCode.RightShift,
                KeyCode.LeftControl, KeyCode.RightControl,
                KeyCode.LeftAlt, KeyCode.RightAlt,
                KeyCode.LeftCommand, KeyCode.RightCommand,
                KeyCode.Space, KeyCode.Return, KeyCode.Tab, KeyCode.CapsLock, KeyCode.Backspace,
                KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Backslash,
                KeyCode.Semicolon, KeyCode.Quote, KeyCode.Comma, KeyCode.Period, KeyCode.Slash,
                KeyCode.BackQuote, KeyCode.Minus, KeyCode.Equals,
                KeyCode.Insert, KeyCode.Delete, KeyCode.Home, KeyCode.End,
                KeyCode.PageUp, KeyCode.PageDown,
            });
            return list.ToArray();
        }

        private void Update()
        {
            if (!Active || OnKey == null) return;
            // Capture happens while the menu is open, i.e. exactly when the raw
            // GetKeyDown block is engaged — exempt these reads.
            KeyLimiter.RawReadExempt = true;
            try
            {
                for (int i = 0; i < Watched.Length; i++)
                {
                    var k = Watched[i];
                    if (Input.GetKeyDown(k))
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
