using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityModManagerNet;

namespace Bismuth.UI
{
    internal static class UICore
    {
        private const string ToggleHint = "Ctrl + B";
        private static readonly Vector2 ReferenceResolution = new Vector2(1920, 1080);

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static CanvasScaler _scaler;
        private static RectTransform _panel;
        private static TabRail _tabs;
        private static bool _isOpen;
        private static Action _onSettingsChanged;
        private static Settings _settings;
        private static UnityModManager.ModEntry _modEntry;
        private static List<FontLoader.FontEntry> _availableFonts;

        public static TabRail Tabs { get { return _tabs; } }
        public static Settings Settings { get { return _settings; } }
        public static Action OnSettingsChanged { get { return _onSettingsChanged; } }
        // Separate from OnSettingsChanged because Key Viewer structural changes
        // (add/remove preset, switch active preset) need a full layout rebuild, not just
        // ApplySettings's color/position re-push. Wired by MainClass.
        public static Action OnKeyViewerRebuild;
        public static bool IsOpen { get { return _isOpen; } }
        public static GameObject CanvasRoot { get { return _canvasGo; } }
        public static IList<FontLoader.FontEntry> AvailableFonts { get { return _availableFonts; } }

        public static void Initialize(UnityModManager.ModEntry modEntry, Settings settings, Action onChanged, List<FontLoader.FontEntry> fonts = null)
        {
            if (_canvasGo != null) { BismuthLog.Log("[UI] Initialize skipped (already built)"); return; }
            _modEntry = modEntry;
            _settings = settings;
            _onSettingsChanged = onChanged;
            _availableFonts = fonts ?? new List<FontLoader.FontEntry>();

            try
            {
                // Apply saved theme BEFORE building so widgets pick up the right Accent/Font.
                Theme.ApplyAccent(new Color(settings.UiAccentR, settings.UiAccentG, settings.UiAccentB, 1f));
                var savedFont = ResolveSavedFont();
                if (savedFont != null) Theme.ApplyFont(savedFont);

                BuildRoot();
                BuildPanel();
                BuildTitleBar();
                BuildBody();
                BuildResizeGrip();
                // Re-attach resize handles LAST so they sit on top of everything else in
                // sibling order. Otherwise the footer / titlebar / rail render on top of the
                // handles in their overlapping zones and absorb drag events; the corner grip
                // would be visible but unresponsive. Handles have transparent raycast Images,
                // so the grip dots underneath show through cleanly.
                ResizeHandle.AttachAll(_panel);
                ApplyScale(_settings.UiScale);
                _canvasGo.SetActive(false);
                BismuthLog.Log("[UI] Initialized — hotkey Ctrl+B armed");
            }
            catch (Exception ex)
            {
                BismuthLog.Log("[UI] Initialize FAILED: " + ex);
                if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
                _canvasGo = null;
            }
        }

        private static Font ResolveSavedFont()
        {
            if (_availableFonts == null || string.IsNullOrEmpty(_settings.UiFontName)) return null;
            for (int i = 0; i < _availableFonts.Count; i++)
                if (_availableFonts[i].Name == _settings.UiFontName) return _availableFonts[i].Font;
            return null;
        }

        // Tracks the scale at which the panel's sizeDelta + anchoredPosition currently make sense.
        // Starts at 1.0 because BuildPanel sets canonical (840×540) at canvas-scale=1.0 units.
        private static float _appliedScale = 1f;

        public static void ApplyScale(float scale)
        {
            if (_scaler == null) return;
            scale = Mathf.Clamp(scale, 0.5f, 2f);
            _settings.UiScale = scale;
            // Bigger UI scale → smaller reference resolution → canvas-scale multiplies up,
            // so widget fontSize / preferredHeight / row spacing all render larger.
            _scaler.referenceResolution = ReferenceResolution / scale;

            // Counter-resize the panel rect so its on-screen size stays constant despite the
            // canvas-scale change. panel-screen-px = sizeDelta × canvasScale, and canvasScale
            // is proportional to uiScale, so sizeDelta must shrink by (applied/new). Same for
            // anchoredPosition — keeps a dragged-off-center panel pinned to the same screen point.
            if (_panel != null && _appliedScale > 0.001f)
            {
                float ratio = _appliedScale / scale;
                _panel.sizeDelta *= ratio;
                _panel.anchoredPosition *= ratio;
            }
            _appliedScale = scale;
        }

        public static void ApplyAccent(Color color)
        {
            _settings.UiAccentR = color.r;
            _settings.UiAccentG = color.g;
            _settings.UiAccentB = color.b;
            Theme.ApplyAccent(color, _canvasGo);
        }

        public static void ApplyFont(FontLoader.FontEntry entry)
        {
            if (entry == null || entry.Font == null) return;
            _settings.UiFontName = entry.Name;
            Theme.ApplyFont(entry.Font, _canvasGo);
        }

        public static void Dispose()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _canvas = null;
            _panel = null;
            _tabs = null;
            _isOpen = false;
        }

        private static void BuildRoot()
        {
            _canvasGo = new GameObject("BismuthUI", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);

            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;

            _scaler = _canvasGo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = ReferenceResolution;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;

            _canvasGo.AddComponent<GraphicRaycaster>();

            // Bismuth runs alongside UMM's own EventSystem. Only add one if none exists.
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var esGo = new GameObject("BismuthEventSystem");
                esGo.transform.SetParent(_canvasGo.transform, false);
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }
        }

        private static void BuildPanel()
        {
            var panelGo = UIBuilder.Rect("Panel", _canvasGo.transform);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            // Restore saved dimensions; ApplyScale will adjust for current UI scale right after.
            _panel.sizeDelta = new Vector2(_settings.UiPanelWidth, _settings.UiPanelHeight);
            _panel.anchoredPosition = Vector2.zero;

            UIBuilder.SolidImage(panelGo, Theme.Panel);
            UIBuilder.AddBorder(panelGo, Theme.PanelBorder, 1f);
            // ResizeHandle.AttachAll is called from Initialize() as the very last step,
            // so handles sit on top of the footer / titlebar / rail and catch drags in
            // their overlapping edge zones.
        }

        // Visible 6-dot staircase in the bottom-right corner — affordance for the existing
        // invisible bottom-right ResizeHandle. The dots have raycastTarget=false so drag
        // events fall through to the underlying handle.
        private static void BuildResizeGrip()
        {
            var gripGo = UIBuilder.Rect("ResizeGrip", _panel);
            var gripRect = (RectTransform)gripGo.transform;
            gripRect.anchorMin = new Vector2(1, 0);
            gripRect.anchorMax = new Vector2(1, 0);
            gripRect.pivot = new Vector2(1, 0);
            gripRect.sizeDelta = new Vector2(16f, 16f);
            gripRect.anchoredPosition = new Vector2(-4f, 4f);

            // Staircase pointing into the corner: 3 dots bottom, 2 dots middle, 1 dot top.
            // Coordinates are bottom-left anchored inside the 16×16 grip box.
            Vector2[] dotPositions = new[]
            {
                new Vector2(0f, 0f),  new Vector2(6f, 0f),  new Vector2(12f, 0f),
                new Vector2(6f, 6f),  new Vector2(12f, 6f),
                new Vector2(12f, 12f),
            };
            foreach (var pos in dotPositions)
            {
                var dotGo = UIBuilder.Rect("Dot", gripGo.transform);
                var dotRect = (RectTransform)dotGo.transform;
                dotRect.anchorMin = new Vector2(0, 0);
                dotRect.anchorMax = new Vector2(0, 0);
                dotRect.pivot = new Vector2(0, 0);
                dotRect.sizeDelta = new Vector2(3f, 3f);
                dotRect.anchoredPosition = pos;
                var img = dotGo.AddComponent<Image>();
                img.sprite = Theme.White;
                img.color = Theme.TextMuted;
                img.raycastTarget = false;
            }
        }

        private static RectTransform _railRect;
        private static RectTransform _pageHostRect;

        private static void BuildTitleBar()
        {
            const float titleH = 36f;
            var bar = UIBuilder.Rect("TitleBar", _panel);
            var r = (RectTransform)bar.transform;
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(0, titleH);
            r.anchoredPosition = Vector2.zero;
            UIBuilder.SolidImage(bar, Theme.TitleBar);

            // Single bottom divider line — sharper than a full border
            var divider = UIBuilder.Rect("Divider", bar.transform);
            var dr = (RectTransform)divider.transform;
            dr.anchorMin = new Vector2(0, 0);
            dr.anchorMax = new Vector2(1, 0);
            dr.offsetMin = Vector2.zero;
            dr.offsetMax = new Vector2(0, 1f);
            var dImg = divider.AddComponent<Image>();
            dImg.sprite = Theme.White;
            dImg.color = Theme.PanelBorder;
            dImg.raycastTarget = false;

            bar.AddComponent<DragHandle>();

            // Title
            var titleGo = UIBuilder.Rect("Title", bar.transform);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = new Vector2(0, 0);
            tr.anchorMax = new Vector2(1, 1);
            tr.offsetMin = new Vector2(12f, 0f);
            tr.offsetMax = new Vector2(-32f, 0f);
            var title = titleGo.AddComponent<Text>();
            title.text = "Bismuth";
            title.font = Theme.Font;
            title.fontSize = 15;
            title.color = Theme.Text;
            title.fontStyle = FontStyle.Bold;
            title.alignment = TextAnchor.MiddleLeft;
            title.raycastTarget = false;

            // Close
            var close = UIBuilder.Rect("Close", bar.transform);
            var cr = (RectTransform)close.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 0.5f);
            cr.pivot = new Vector2(1f, 0.5f);
            cr.anchoredPosition = new Vector2(-8f, 0f);
            cr.sizeDelta = new Vector2(20f, 20f);
            var closeBg = UIBuilder.SolidImage(close, new Color(0, 0, 0, 0));
            closeBg.raycastTarget = true;
            var x = UIBuilder.Label(close.transform, "×", 14, TextAnchor.MiddleCenter, Theme.Text);
            x.fontStyle = FontStyle.Bold;
            var closeHover = close.AddComponent<HoverHandler>();
            closeHover.OnEnter = () => closeBg.color = Theme.CloseHover;
            closeHover.OnExit = () => closeBg.color = new Color(0, 0, 0, 0);
            ClickHandler.Attach(close, () => Close());
        }

        private static void BuildBody()
        {
            const float titleH = 36f;
            const float footerH = 26f;

            // Left rail
            var rail = UIBuilder.Rect("Rail", _panel);
            _railRect = (RectTransform)rail.transform;
            _railRect.anchorMin = new Vector2(0, 0);
            _railRect.anchorMax = new Vector2(0, 1);
            _railRect.pivot = new Vector2(0, 0.5f);
            _railRect.sizeDelta = new Vector2(TabRail.Width, 0);
            _railRect.offsetMin = new Vector2(0, footerH);
            _railRect.offsetMax = new Vector2(TabRail.Width, -titleH);

            // Rail divider
            var rd = UIBuilder.Rect("RailDivider", _panel);
            var rdr = (RectTransform)rd.transform;
            rdr.anchorMin = new Vector2(0, 0);
            rdr.anchorMax = new Vector2(0, 1);
            rdr.pivot = new Vector2(0, 0.5f);
            rdr.sizeDelta = new Vector2(1f, 0);
            rdr.offsetMin = new Vector2(TabRail.Width, footerH);
            rdr.offsetMax = new Vector2(TabRail.Width + 1f, -titleH);
            var rdImg = rd.AddComponent<Image>();
            rdImg.sprite = Theme.White;
            rdImg.color = Theme.PanelBorder;
            rdImg.raycastTarget = false;

            // Page host (right of rail, below title, above footer)
            var pageHost = UIBuilder.Rect("PageHost", _panel);
            _pageHostRect = (RectTransform)pageHost.transform;
            _pageHostRect.anchorMin = new Vector2(0, 0);
            _pageHostRect.anchorMax = new Vector2(1, 1);
            _pageHostRect.offsetMin = new Vector2(TabRail.Width + 1f, footerH);
            _pageHostRect.offsetMax = new Vector2(0, -titleH);

            // Footer
            var footer = UIBuilder.Rect("Footer", _panel);
            var fr = (RectTransform)footer.transform;
            fr.anchorMin = new Vector2(0, 0);
            fr.anchorMax = new Vector2(1, 0);
            fr.pivot = new Vector2(0.5f, 0f);
            fr.sizeDelta = new Vector2(0, footerH);
            fr.anchoredPosition = Vector2.zero;
            UIBuilder.SolidImage(footer, Theme.TitleBar);

            // Footer top divider
            var fd = UIBuilder.Rect("FooterDivider", footer.transform);
            var fdr = (RectTransform)fd.transform;
            fdr.anchorMin = new Vector2(0, 1);
            fdr.anchorMax = new Vector2(1, 1);
            fdr.offsetMin = new Vector2(0, -1f);
            fdr.offsetMax = Vector2.zero;
            var fdImg = fd.AddComponent<Image>();
            fdImg.sprite = Theme.White;
            fdImg.color = Theme.PanelBorder;
            fdImg.raycastTarget = false;

            var hint = UIBuilder.Label(footer.transform, ToggleHint + " to toggle", 12, TextAnchor.MiddleLeft, Theme.TextMuted);
            hint.rectTransform.offsetMin = new Vector2(10f, 0f);
            hint.rectTransform.offsetMax = new Vector2(-10f, 0f);

            string ver = (_modEntry != null && _modEntry.Info != null) ? _modEntry.Info.Version : "";
            var ver2 = UIBuilder.Label(footer.transform, "v" + ver, 12, TextAnchor.MiddleRight, Theme.TextMuted);
            ver2.rectTransform.offsetMin = new Vector2(10f, 0f);
            // Right offset kept clear of the resize grip's hit area in the bottom-right corner.
            ver2.rectTransform.offsetMax = new Vector2(-28f, 0f);

            _tabs = new TabRail(_railRect, _pageHostRect);
        }

        public static void HandleUpdate()
        {
            if (_canvasGo == null) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.B)) Toggle();
        }

        public static void Toggle() { if (_isOpen) Close(); else Open(); }

        public static void Open()
        {
            if (_isOpen) return;
            _isOpen = true;
            // Always re-center on open. Dimensions are restored from settings on Build /
            // re-applied via ApplyScale; only position resets to (0,0).
            if (_panel != null) _panel.anchoredPosition = Vector2.zero;
            _canvasGo.SetActive(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public static void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            // Save current dimensions in canonical (scale=1.0) units so they restore
            // correctly across UI scale changes. sizeDelta is scaled inversely by
            // ApplyScale, so multiplying by _appliedScale undoes that.
            if (_panel != null && _settings != null)
            {
                _settings.UiPanelWidth  = _panel.sizeDelta.x * _appliedScale;
                _settings.UiPanelHeight = _panel.sizeDelta.y * _appliedScale;
            }
            _canvasGo.SetActive(false);
        }
    }
}
