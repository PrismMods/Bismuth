using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI
{
    // Full-screen edit overlay for the GAME's own HUD elements (Locations tab) — the
    // counterpart of LocationEditor. Drag moves, scroll scales, right-click resets one
    // element. Currently-inactive elements (death %, congrats, …) keep a dimmed handle at
    // their last position so they're editable without dying first. Writes go through
    // GameUiLayout (wrapper transforms / error meter override) and apply live.
    internal static class GameUiEditor
    {
        public static bool IsActive => _canvasGo != null;

        private static GameObject _canvasGo;
        private static Canvas _canvas;

        // Whether to reopen the settings panel when the editor closes (it's hidden while
        // editing so it doesn't cover the HUD being positioned).
        private static bool _reopenPanel;

        public static void Open()
        {
            if (IsActive) return;
            LocationEditor.Close(); // one editor at a time (both at 31000)

            _reopenPanel = UICore.IsOpen;
            if (_reopenPanel) UICore.Close();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            _canvasGo = new GameObject("BismuthGameUiEditor");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 31000; // above game, below the settings panel (32000)
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            var dim = UIBuilder.Rect("Dim", _canvasGo.transform);
            var dimRect = (RectTransform)dim.transform;
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            var dimImg = UIBuilder.SolidImage(dim, new Color(0f, 0f, 0f, 0.35f));
            dimImg.raycastTarget = false;

            ForceShowTargets();

            foreach (var t in GameUiLayout.Targets)
                MakeElementHandle(t);
            MakeMeterHandle();
            MakeDoneButton();
            _canvasGo.AddComponent<HandleSorter>();
            EditorUndo.Reset();
            _canvasGo.AddComponent<UndoPoller>();
        }

        // Keeps smaller handles grabbable above larger ones (congrats/results boxes blanket
        // the center). uGUI raycasts later siblings on top, so order the handle block by
        // area descending. Re-orders only on actual change (SetSiblingIndex dirties the
        // canvas); runs off last frame's sizes, so one frame of staleness is invisible.
        private class HandleSorter : MonoBehaviour
        {
            private readonly List<LocHandle> _handles = new List<LocHandle>();

            private void LateUpdate()
            {
                _handles.Clear();
                int firstIdx = int.MaxValue;
                for (int i = 0; i < transform.childCount; i++)
                {
                    var h = transform.GetChild(i).GetComponent<LocHandle>();
                    if (h == null) continue;
                    _handles.Add(h);
                    firstIdx = Mathf.Min(firstIdx, h.transform.GetSiblingIndex());
                }
                if (_handles.Count < 2) return;
                _handles.Sort((a, b) => Area(b).CompareTo(Area(a)));
                bool inOrder = true;
                for (int i = 0; i < _handles.Count && inOrder; i++)
                    inOrder = _handles[i].transform.GetSiblingIndex() == firstIdx + i;
                if (inOrder) return;
                // Handles occupy a contiguous block (Dim before, Done/hint after).
                // Assigning ascending indices in the desired order settles correctly.
                for (int i = 0; i < _handles.Count; i++)
                    _handles[i].transform.SetSiblingIndex(firstIdx + i);
            }

            private static float Area(LocHandle h)
            {
                var sz = ((RectTransform)h.transform).sizeDelta;
                return sz.x * sz.y;
            }
        }

        public static void Close()
        {
            if (!IsActive) return;
            RestoreShown();
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _canvas = null;
            UICore.OnSettingsChanged?.Invoke();
            if (_reopenPanel && UICore.CanvasRoot != null) UICore.Open();
            _reopenPanel = false;
        }

        // ── Force-show while editing ───────────────────────────────────────
        // Most game elements only show at specific moments (death %, congrats, …) and many
        // sit inside inactive containers, so activating just the element left invisible
        // handles. Activate the whole ancestor chain to the canvas, lift faded
        // CanvasGroup/text alphas, and fill empty texts with samples. Restored on Close.

        private static readonly List<KeyValuePair<GameObject, bool>> _shownGos =
            new List<KeyValuePair<GameObject, bool>>();
        private static readonly List<KeyValuePair<CanvasGroup, float>> _liftedCgs =
            new List<KeyValuePair<CanvasGroup, float>>();
        private static readonly List<KeyValuePair<Text, string>> _sampledTexts =
            new List<KeyValuePair<Text, string>>();
        private static readonly List<KeyValuePair<Text, Color>> _liftedColors =
            new List<KeyValuePair<Text, Color>>();

        private static readonly Dictionary<string, string> _sampleText =
            new Dictionary<string, string>
            {
                { "percent", "100% 완료" },
                { "countdown", "3" },
                { "congrats", "축하합니다!" },
                { "strictclear", "엄격한 판정 클리어!" },
                { "presstostart", "아무 키나 눌러 시작" },
                { "results", "(결과)" },
            };

        private static void ForceShowTargets()
        {
            foreach (var t in GameUiLayout.Targets)
            {
                var rt = t.Get?.Invoke();
                if (rt == null) continue;

                // Activate the chain from element up to canvas. The canvas itself stays as-is.
                for (var tr = rt.transform; tr != null && tr.GetComponent<Canvas>() == null; tr = tr.parent)
                {
                    if (!tr.gameObject.activeSelf)
                    {
                        _shownGos.Add(new KeyValuePair<GameObject, bool>(tr.gameObject, false));
                        tr.gameObject.SetActive(true);
                    }
                    var cg = tr.GetComponent<CanvasGroup>();
                    if (cg != null && cg.alpha < 0.05f)
                    {
                        _liftedCgs.Add(new KeyValuePair<CanvasGroup, float>(cg, cg.alpha));
                        cg.alpha = 1f;
                    }
                }

                var txt = rt.GetComponentInChildren<Text>(true);
                if (txt != null)
                {
                    string sample;
                    if (string.IsNullOrEmpty(txt.text) && _sampleText.TryGetValue(t.Key, out sample))
                    {
                        if (t.Key == "results") sample = ResultsSample(sample);
                        _sampledTexts.Add(new KeyValuePair<Text, string>(txt, txt.text));
                        txt.text = sample;
                    }
                    // Faded-out text stays invisible even when active, so lift alpha.
                    if (txt.color.a < 0.05f)
                    {
                        _liftedColors.Add(new KeyValuePair<Text, Color>(txt, txt.color));
                        var c = txt.color; c.a = 1f; txt.color = c;
                    }
                }
            }
        }

        // The real results screen is a multi-line colored breakdown
        // (DetailedResults.GenerateResults), so a plain placeholder looks nothing like it.
        // Generate the authentic string from the current margin tracker. GenerateResults is
        // private, hence the reflection call.
        private static string ResultsSample(string fallback)
        {
            try
            {
                var dr = scrUIController.instance?.txtResults;
                var players = ADOBase.playerManager?.players;
                var tracker = players != null && players.Length > 0 && players[0] != null
                    ? players[0].marginTracker : null;
                if (dr != null && tracker != null)
                {
                    var m = typeof(DetailedResults).GetMethod("GenerateResults",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var s = m?.Invoke(dr, new object[] { tracker }) as string;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch (Exception e)
            {
                BismuthLog.Debug("GameUiEditor: results sample failed: " + e.Message);
            }
            return fallback;
        }

        private static void RestoreShown()
        {
            // Reverse order so nested activations unwind cleanly.
            for (int i = _shownGos.Count - 1; i >= 0; i--)
                if (_shownGos[i].Key != null) _shownGos[i].Key.SetActive(_shownGos[i].Value);
            foreach (var kv in _liftedCgs)
                if (kv.Key != null) kv.Key.alpha = kv.Value;
            foreach (var kv in _sampledTexts)
                if (kv.Key != null) kv.Key.text = kv.Value;
            foreach (var kv in _liftedColors)
                if (kv.Key != null) kv.Key.color = kv.Value;
            _shownGos.Clear();
            _liftedCgs.Clear();
            _sampledTexts.Clear();
            _liftedColors.Clear();
        }

        // ── Element handles (wrapper-offset model) ─────────────────────────

        private static void MakeElementHandle(GameUiLayout.TargetDef t)
        {
            Vector2 startOff = Vector2.zero;
            float gameScale = 1f;

            var h = MakeHandle(t.Label, t.Get);
            h.ShowInactive = true;
            h.BeginDragCapture = () =>
            {
                var o = GameUiLayout.GetOverride(t.Key, create: true);
                startOff = new Vector2(o.OffX, o.OffY);
                gameScale = CanvasScale(t.Get?.Invoke());
            };
            h.DragBy = d =>
            {
                var o = GameUiLayout.GetOverride(t.Key, create: true);
                o.OffX = startOff.x + d.x / gameScale;
                o.OffY = startOff.y + d.y / gameScale;
                GameUiLayout.ApplyOne(t.Key);
            };
            h.GetScale = () =>
            {
                var o = GameUiLayout.GetOverride(t.Key, create: false);
                return o != null ? o.Scale : 1f;
            };
            h.SetScale = v =>
            {
                var o = GameUiLayout.GetOverride(t.Key, create: true);
                o.Scale = Mathf.Clamp(v, 0.25f, 4f);
                GameUiLayout.ApplyOne(t.Key);
            };
            h.ResetTarget = () => GameUiLayout.ResetToDefault(t.Key);
            h.CaptureUndo = () =>
            {
                var o = GameUiLayout.GetOverride(t.Key, create: false);
                bool had = o != null;
                float ox = had ? o.OffX : 0f, oy = had ? o.OffY : 0f, scl = had ? o.Scale : 1f;
                int al = had ? o.Align : -1;
                return () =>
                {
                    if (!had) { GameUiLayout.RemoveOverride(t.Key); return; }
                    var r = GameUiLayout.GetOverride(t.Key, create: true);
                    r.OffX = ox; r.OffY = oy; r.Scale = scl; r.Align = al;
                    GameUiLayout.ApplyOne(t.Key);
                };
            };
        }

        // Screen px → element-parent units: undo the game canvas's scale factor.
        private static float CanvasScale(RectTransform rt)
        {
            var canvas = rt != null ? rt.GetComponentInParent<Canvas>() : null;
            float sf = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            return sf > 0.0001f ? sf : 1f;
        }

        // Error meter handle (absolute normalized position + scale mul)

        private static void MakeMeterHandle()
        {
            var s = UICore.Settings;
            Vector2 start = Vector2.zero;

            var h = MakeHandle("Error Meter", () => GameUiLayout.CurrentMeter()?.wrapperRectTransform);
            h.ShowInactive = true;
            // The wrapper rect extends well below the drawn meter, so hug the content.
            h.TightBounds = true;
            h.BeginDragCapture = () =>
            {
                EnableMeterOverride();
                start = new Vector2(s.GameErrorMeterX, s.GameErrorMeterY);
            };
            h.DragBy = d =>
            {
                s.GameErrorMeterX = Mathf.Clamp01(start.x + d.x / Screen.width);
                s.GameErrorMeterY = Mathf.Clamp01(start.y + d.y / Screen.height);
                GameUiLayout.ApplyErrorMeter(GameUiLayout.CurrentMeter());
            };
            h.GetScale = () => s.GameErrorMeterScale;
            h.SetScale = v =>
            {
                EnableMeterOverride();
                s.GameErrorMeterScale = Mathf.Clamp(v, 0.25f, 4f);
                GameUiLayout.ApplyErrorMeter(GameUiLayout.CurrentMeter());
            };
            h.ResetTarget = () =>
            {
                s.GameErrorMeterOverride = false;
                s.GameErrorMeterX = 0.5f;
                s.GameErrorMeterY = 0.03f;
                s.GameErrorMeterScale = 1f;
                GameUiLayout.RestoreErrorMeter();
            };
            h.CaptureUndo = () =>
            {
                bool ov = s.GameErrorMeterOverride;
                float x = s.GameErrorMeterX, y = s.GameErrorMeterY, scl = s.GameErrorMeterScale;
                return () =>
                {
                    s.GameErrorMeterOverride = ov;
                    s.GameErrorMeterX = x; s.GameErrorMeterY = y; s.GameErrorMeterScale = scl;
                    if (ov) GameUiLayout.ApplyErrorMeter(GameUiLayout.CurrentMeter());
                    else GameUiLayout.RestoreErrorMeter();
                };
            };
        }

        // Switching the override on must not move the meter: seed the normalized
        // position from where the game currently has it (the wrapper's pivot point).
        private static void EnableMeterOverride()
        {
            var s = UICore.Settings;
            if (s.GameErrorMeterOverride) return;
            var w = GameUiLayout.CurrentMeter()?.wrapperRectTransform;
            if (w != null && Screen.width > 0 && Screen.height > 0)
            {
                s.GameErrorMeterX = Mathf.Clamp01(w.position.x / Screen.width);
                s.GameErrorMeterY = Mathf.Clamp01(w.position.y / Screen.height);
            }
            s.GameErrorMeterOverride = true;
        }

        // ── Construction ───────────────────────────────────────────────────

        private static LocHandle MakeHandle(string label, Func<RectTransform> get)
        {
            var go = UIBuilder.Rect("Handle_" + label, _canvasGo.transform);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 4f;
            bg.AAFringe = 0.5f;
            bg.BorderWidth = 1.5f;
            bg.BorderColor = Theme.Accent;
            bg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.12f);
            bg.raycastTarget = true;

            var lbl = UIBuilder.Label(go.transform, label.ToUpperInvariant(),
                (int)UIBuilder.SmallCapsFontSize, TextAnchor.MiddleCenter, Theme.Text);
            lbl.fontStyle = FontStyles.Bold;

            var cg = go.AddComponent<CanvasGroup>();

            var h = go.AddComponent<LocHandle>();
            h.GetTarget = get;
            h.EditorCanvas = _canvas;
            h.Group = cg;
            return h;
        }

        private static void MakeDoneButton()
        {
            var btn = UIBuilder.Rect("Done", _canvasGo.transform);
            var rect = (RectTransform)btn.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -16f);
            rect.sizeDelta = new Vector2(190f, 34f);

            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 17f;
            bg.AAFringe = 0.5f;
            bg.color = Theme.Accent;
            bg.raycastTarget = true;

            var lbl = UIBuilder.Label(btn.transform, "✓ Done editing", (int)UIBuilder.LabelFontSize,
                TextAnchor.MiddleCenter, Color.black);
            lbl.fontStyle = FontStyles.Bold;

            ClickHandler.Attach(btn, Close);

            var hint = UIBuilder.Label(_canvasGo.transform,
                "Drag to move (Shift: 1 axis)  ·  Grips / scroll to scale  ·  Right-click reset  ·  Ctrl/⌘+Z undo",
                (int)UIBuilder.SmallCapsFontSize, TextAnchor.MiddleCenter, Theme.TextMuted);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.anchoredPosition = new Vector2(0f, -54f);
            hintRect.sizeDelta = new Vector2(520f, 20f);
        }
    }
}
