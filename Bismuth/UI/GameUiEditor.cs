using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI
{
    // LocationEditor's counterpart for the GAME's HUD: drag moves, scroll scales, right-click
    // resets. Inactive elements (death %, congrats, …) keep a dimmed handle so they're editable
    // without dying first. Writes go through GameUiLayout and apply live.
    internal static class GameUiEditor
    {
        public static bool IsActive => _canvasGo != null;

        private static GameObject _canvasGo;
        private static Canvas _canvas;

        // Whether to reopen the settings panel when the editor closes (it's hidden while
        // editing so it doesn't cover the HUD being positioned).
        private static bool _reopenPanel;

        /* Two modes over the same editor. HUD mode edits everything the game shows during
           play; results mode edits only what appears once a level is cleared, plus Bismuth's
           timing graph. GameUiLayout.ResultsKeys decides which side a target falls on. */
        /* One on-screen editor over three layers, switched live from the pill at the top.
           Merged from what used to be two editors and three modes: they were the same shell
           (full-screen canvas, dim, LocHandles, Done, undo) differing only in which targets
           they built, and having to close one to open another made cross-layer alignment
           guesswork.

             GameUi  — everything the game shows during play, plus the error meter
             Overlay — Bismuth's own overlay and key viewer (LocationEditor's targets)
             Results — the results screen: the game's texts, the timing graph, and either
                       the game's single Results block or Bismuth's custom fields, whichever
                       is actually being drawn. */
        internal enum Layer { GameUi, Overlay, Results }
        private static Layer _layer;

        internal static Layer CurrentLayer => _layer;
        // The overlay layer's combo-label target converts a screen delta into canvas units.
        internal static float EditorCanvasScale => _canvas != null ? _canvas.scaleFactor : 1f;
        public static bool IsResults => IsActive && _layer == Layer.Results;

        public static void Open() => OpenMode(Layer.GameUi);
        public static void OpenOverlay() => OpenMode(Layer.Overlay);
        public static void OpenResults() => OpenMode(Layer.Results);
        // Kept so the Custom-results button keeps working; same layer now.
        public static void OpenCustomResults() => OpenMode(Layer.Results);

        /* Does this game target belong to the layer that is open? Results owns the results
           keys; GameUi owns the rest; Overlay owns no game targets at all. */
        private static bool InMode(GameUiLayout.TargetDef t)
        {
            if (_layer == Layer.Overlay) return false;
            bool isResults = GameUiLayout.IsResultsKey(t.Key);
            if (_layer != Layer.Results) return !isResults;
            // With the custom results drawn, the game's own block is blanked — a handle on it
            // would move something invisible.
            if (t.Key == "results" && UICore.Settings.CustomResults) return false;
            return true;
        }

        private static void OpenMode(Layer layer)
        {
            if (IsActive) { SwitchLayer(layer); return; }
            _layer = layer;

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

            BuildLayer();
            MakeLayerPicker();
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

        /* Rebuild the handles for the current layer. Handles are plain canvas children, so
           clearing means destroying the ones that carry a LocHandle — the dim, pill, Done and
           hint stay put. */
        private static void BuildLayer()
        {
            var overlay = Overlay.Instance;
            if (overlay != null)
            {
                overlay.EditMode = _layer == Layer.Overlay;
                // The graph is editable in both layers: Overlay places it for play, Results
                // for the results screen.
                overlay.GraphPreview = _layer == Layer.Results || _layer == Layer.Overlay;
                overlay.GraphPreviewResults = _layer == Layer.Results;
                overlay.ResultsPreview = _layer == Layer.Results && UICore.Settings.CustomResults;
                overlay.ApplySettings(UICore.Settings);
            }

            ForceShowTargets();

            foreach (var t in GameUiLayout.Targets)
                if (InMode(t)) MakeElementHandle(t);

            if (_layer == Layer.GameUi) MakeMeterHandle();
            else if (_layer == Layer.Overlay)
            {
                LocationEditor.AttachHandles(MakeHandle);
                MakeGraphHandle(results: false);
            }
            else
            {
                MakeGraphHandle(results: UICore.Settings.TimingGraphResultsPos);
                MakeResultsFieldHandles();
            }
        }

        private static void SwitchLayer(Layer layer)
        {
            if (!IsActive || _layer == layer) return;
            _layer = layer;

            RestoreShown();   // un-force the previous layer's elements before showing the next
            for (int i = _canvasGo.transform.childCount - 1; i >= 0; i--)
            {
                var child = _canvasGo.transform.GetChild(i);
                if (child.GetComponent<LocHandle>() != null)
                {
                    child.SetParent(null);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }

            BuildLayer();
            RefreshLayerPicker();
            EditorUndo.Reset();   // undo history belongs to the layer it was recorded in
        }

        public static void Close()
        {
            if (!IsActive) return;
            RestoreShown();
            if (Overlay.Instance != null) Overlay.Instance.EditMode = false;
            if (Overlay.Instance != null)
            {
                Overlay.Instance.GraphPreview = false;
                Overlay.Instance.GraphPreviewResults = false;
                Overlay.Instance.ResultsPreview = false;
            }
            _layer = Layer.GameUi;
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _canvas = null;
            UICore.OnSettingsChanged?.Invoke();
            if (_reopenPanel && UICore.CanvasRoot != null) UICore.Open();
            _reopenPanel = false;
        }

        // ── Force-show while editing ───────────────────────────────────────
        // Elements that only show at specific moments sit in inactive containers: activate the
        // ancestor chain, lift faded alphas, fill empty texts with samples. Restored on Close.

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
                if (!InMode(t)) continue;
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
            h.GetRotation = () => GameUiLayout.GetOverride(t.Key, create: false)?.Rotation ?? 0f;
            h.SetRotation = v =>
            {
                var o = GameUiLayout.GetOverride(t.Key, create: true);
                o.Rotation = Mathf.Repeat(v, 360f);
                GameUiLayout.ApplyOne(t.Key);
            };
            h.ResetTarget = () => GameUiLayout.ResetToDefault(t.Key);
            h.CaptureUndo = () =>
            {
                var o = GameUiLayout.GetOverride(t.Key, create: false);
                bool had = o != null;
                float ox = had ? o.OffX : 0f, oy = had ? o.OffY : 0f, scl = had ? o.Scale : 1f;
                float rot = had ? o.Rotation : 0f;
                int al = had ? o.Align : -1;
                return () =>
                {
                    if (!had) { GameUiLayout.RemoveOverride(t.Key); return; }
                    var r = GameUiLayout.GetOverride(t.Key, create: true);
                    r.OffX = ox; r.OffY = oy; r.Scale = scl; r.Align = al; r.Rotation = rot;
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

        /* Timing graph handle. Same model as the error meter — a normalized screen anchor
           plus a scale — because the graph sits on its own full-screen canvas, where the
           anchor fraction IS the screen fraction. */
        /* `results` picks which of the graph's two placements this handle writes. In the
           Results layer it only writes the results one when that placement is enabled —
           otherwise both layers would be dragging the same play values under different names. */
        private static void MakeGraphHandle(bool results)
        {
            var s = UICore.Settings;
            Vector2 start = Vector2.zero;
            void Apply() => Overlay.Instance?.ApplyTimingGraph(s);

            float GetX() => results ? s.TimingGraphResultsX : s.TimingGraphX;
            float GetY() => results ? s.TimingGraphResultsY : s.TimingGraphY;
            void SetXY(float x, float y)
            {
                if (results) { s.TimingGraphResultsX = x; s.TimingGraphResultsY = y; }
                else { s.TimingGraphX = x; s.TimingGraphY = y; }
            }
            float GetSc() => results ? s.TimingGraphResultsScale : s.TimingGraphScale;
            void SetSc(float v)
            {
                if (results) s.TimingGraphResultsScale = v; else s.TimingGraphScale = v;
            }
            float GetRot() => results ? s.TimingGraphResultsRotation : s.TimingGraphRotation;
            void SetRot(float v)
            {
                if (results) s.TimingGraphResultsRotation = v; else s.TimingGraphRotation = v;
            }

            var h = MakeHandle(results ? "Timing Graph (results)" : "Timing Graph",
                () => Overlay.Instance?.TimingGraphRect);
            h.ShowInactive = true;
            h.BeginDragCapture = () => start = new Vector2(GetX(), GetY());
            h.DragBy = d =>
            {
                SetXY(Mathf.Clamp01(start.x + d.x / Screen.width),
                      Mathf.Clamp01(start.y + d.y / Screen.height));
                Apply();
            };
            h.GetScale = GetSc;
            h.SetScale = v => { SetSc(Mathf.Clamp(v, 0.25f, 4f)); Apply(); };
            h.GetRotation = GetRot;
            h.SetRotation = v => { SetRot(Mathf.Repeat(v, 360f)); Apply(); };
            h.ResetTarget = () =>
            {
                SetXY(0.5f, 0.14f); SetSc(1f); SetRot(0f);
                Apply();
            };
            h.CaptureUndo = () =>
            {
                float x = GetX(), y = GetY(), scl = GetSc(), rot = GetRot();
                return () => { SetXY(x, y); SetSc(scl); SetRot(rot); Apply(); };
            };
        }

        /* One handle per custom results field. Only built when the custom screen is on — with
           it off the game draws its own single block, which the "results" target already
           covers. Positions are normalized screen fractions, like the graph. */
        private static void MakeResultsFieldHandles()
        {
            var s = UICore.Settings;
            if (!s.CustomResults) return;
            var overlay = Overlay.Instance;
            if (overlay == null) return;

            foreach (var def in ResultsFields.All)
            {
                var d = def;
                if (s.ResultsFieldFor(d.Key)?.Hidden == true) continue;

                Vector2 start = Vector2.zero;
                void Apply() => Overlay.Instance?.ApplyResultsScreen(s);
                // An unmoved field has NaN stored: fall back to its built-in default.
                Vector2 Current()
                {
                    var o = s.ResultsFieldFor(d.Key);
                    return new Vector2(o != null && !float.IsNaN(o.X) ? o.X : d.X,
                                       o != null && !float.IsNaN(o.Y) ? o.Y : d.Y);
                }

                var h = MakeHandle(Loc.T(d.Label), () => Overlay.Instance?.ResultsFieldRect(d.Key));
                h.ShowInactive = true;
                h.TightBounds = true;
                h.BeginDragCapture = () => start = Current();
                h.DragBy = delta =>
                {
                    var o = s.ResultsFieldFor(d.Key, create: true);
                    o.X = Mathf.Clamp01(start.x + delta.x / Screen.width);
                    o.Y = Mathf.Clamp01(start.y + delta.y / Screen.height);
                    Apply();
                };
                h.GetScale = () => s.ResultsFieldFor(d.Key)?.Scale ?? 1f;
                h.SetScale = v =>
                {
                    s.ResultsFieldFor(d.Key, create: true).Scale = Mathf.Clamp(v, 0.25f, 4f);
                    Apply();
                };
                h.GetRotation = () => s.ResultsFieldFor(d.Key)?.Rotation ?? 0f;
                h.SetRotation = v =>
                {
                    s.ResultsFieldFor(d.Key, create: true).Rotation = Mathf.Repeat(v, 360f);
                    Apply();
                };
                h.ResetTarget = () =>
                {
                    var o = s.ResultsFieldFor(d.Key, create: true);
                    o.X = float.NaN; o.Y = float.NaN; o.Scale = 1f; o.Rotation = 0f;
                    Apply();
                };
                h.CaptureUndo = () =>
                {
                    var o = s.ResultsFieldFor(d.Key);
                    bool had = o != null;
                    float x = had ? o.X : float.NaN, y = had ? o.Y : float.NaN;
                    float sc = had ? o.Scale : 1f, rot = had ? o.Rotation : 0f;
                    return () =>
                    {
                        var r = s.ResultsFieldFor(d.Key, create: true);
                        r.X = x; r.Y = y; r.Scale = sc; r.Rotation = rot;
                        Apply();
                    };
                };
            }
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

        private static readonly (Layer L, string Label)[] LayerTabs =
        {
            (Layer.GameUi,  "Game UI"),
            (Layer.Overlay, "Overlay"),
            (Layer.Results, "Results"),
        };

        private static readonly List<(Layer L, RoundedRectGraphic Bg, TMP_Text Label)> _layerTabs =
            new List<(Layer, RoundedRectGraphic, TMP_Text)>();

        // Segmented pill under the Done button — three layers read better side by side than
        // in a dropdown, and switching is one click instead of two.
        private static void MakeLayerPicker()
        {
            _layerTabs.Clear();
            const float w = 130f, h = 28f;
            float total = w * LayerTabs.Length;

            for (int i = 0; i < LayerTabs.Length; i++)
            {
                var tab = LayerTabs[i];
                var go = UIBuilder.Rect("Layer_" + tab.L, _canvasGo.transform);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(-total / 2f + w * (i + 0.5f), -78f);
                rect.sizeDelta = new Vector2(w - 4f, h);

                var bg = go.AddComponent<RoundedRectGraphic>();
                bg.Radius = 14f;
                bg.AAFringe = 0.5f;
                bg.BorderWidth = 1.25f;
                bg.raycastTarget = true;

                var lbl = UIBuilder.Label(go.transform, Loc.T(tab.Label),
                    (int)UIBuilder.LabelFontSize - 1, TextAnchor.MiddleCenter, Theme.Text);
                var target = tab.L;
                ClickHandler.Attach(go, () => SwitchLayer(target));
                _layerTabs.Add((tab.L, bg, lbl));
            }
            RefreshLayerPicker();
        }

        private static void RefreshLayerPicker()
        {
            foreach (var (l, bg, lbl) in _layerTabs)
            {
                bool on = l == _layer;
                if (bg != null)
                {
                    bg.color = on ? Theme.Accent : new Color(1f, 1f, 1f, 0.06f);
                    bg.BorderColor = on ? Theme.Accent : new Color(1f, 1f, 1f, 0.18f);
                }
                if (lbl != null) lbl.color = on ? Color.black : Theme.TextMuted;
            }
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

            var lbl = UIBuilder.Label(btn.transform, Loc.T("✓ Done editing"), (int)UIBuilder.LabelFontSize,
                TextAnchor.MiddleCenter, Color.black);
            lbl.fontStyle = FontStyles.Bold;

            ClickHandler.Attach(btn, Close);

            var hint = UIBuilder.Label(_canvasGo.transform,
                Loc.T("Drag to move (Shift: 1 axis)  ·  Grips / scroll to scale  ·  Knob to rotate (Shift: 15°)  ·  Right-click reset  ·  Ctrl/⌘+Z undo"),
                (int)UIBuilder.SmallCapsFontSize, TextAnchor.MiddleCenter, Theme.TextMuted);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.anchoredPosition = new Vector2(0f, -112f);
            hintRect.sizeDelta = new Vector2(520f, 20f);
        }
    }
}
