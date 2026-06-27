using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth.UI
{
    // Standalone "update available" popup on its own canvas, above the settings panel.
    // Built by UpdateChecker when Repository.json reports a newer version.
    internal static class UpdatePopup
    {
        private static GameObject _canvasGo;
        private static TextMeshProUGUI _status;
        private static GameObject _updateBtn;

        public static void Show(string current, string latest, string releasesUrl, Action updateNow)
        {
            Close();

            _canvasGo = new GameObject("BismuthUpdatePopup");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32600;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            var panel = UIBuilder.Rect("Panel", _canvasGo.transform);
            var pr = (RectTransform)panel.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(560f, 184f);
            pr.anchoredPosition = new Vector2(0f, 230f);
            var bg = panel.AddComponent<Image>();
            bg.sprite = Theme.White;
            bg.color = new Color(0.10f, 0.10f, 0.12f, 0.98f);
            bg.raycastTarget = true;

            MakeText(panel.transform, "Title", "Bismuth — update available",
                17, FontStyle.Bold, Theme.Text, -8f, 30f);
            MakeText(panel.transform, "Body", $"v{current}  →  v{latest}",
                15, FontStyle.Normal, Theme.Text, -42f, 24f);
            _status = MakeText(panel.transform, "Status", "",
                13, FontStyle.Normal, Theme.TextMuted, -68f, 22f);

            const float btnY = 18f;
            _updateBtn = MakeButton(panel.transform, "Update now (requires restart)", -135f, 260f, btnY,
                () => updateNow?.Invoke());
            MakeButton(panel.transform, "Manual update", 70f, 130f, btnY,
                () => Application.OpenURL(releasesUrl));
            MakeButton(panel.transform, "Later", 185f, 90f, btnY, Close);
        }

        // allowRetry re-shows the Update button after a failed attempt. In-flight
        // states keep it hidden.
        public static void SetStatus(string msg, bool allowRetry)
        {
            if (_status != null) _status.text = msg;
            if (_updateBtn != null) _updateBtn.SetActive(allowRetry);
        }

        // Successful update: the primary button turns into "Close" instead of vanishing.
        public static void SetDone(string msg)
        {
            if (_status != null) _status.text = msg;
            if (_updateBtn == null) return;
            _updateBtn.SetActive(true);
            var label = _updateBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "Close";
            ClickHandler.Attach(_updateBtn, Close);
        }

        public static void Close()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _status = null;
            _updateBtn = null;
        }

        internal static TextMeshProUGUI MakeText(Transform parent, string name, string text,
            int size, FontStyle style, Color color, float top, float height)
        {
            var go = UIBuilder.Rect(name, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, top);
            rt.sizeDelta = new Vector2(-24f, height);
            var t = UIBuilder.Tmp(go, text, size, TextAnchor.MiddleCenter, color);
            t.fontStyle = style == FontStyle.Bold ? FontStyles.Bold
                        : style == FontStyle.Italic ? FontStyles.Italic
                        : style == FontStyle.BoldAndItalic ? (FontStyles.Bold | FontStyles.Italic)
                        : FontStyles.Normal;
            return t;
        }

        internal static GameObject MakeButton(Transform parent, string label,
            float centerX, float width, float bottom, Action onClick)
        {
            var go = UIBuilder.Rect("Btn_" + label, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(centerX, bottom);
            rt.sizeDelta = new Vector2(width, 34f);

            var idle = new Color(0.18f, 0.18f, 0.22f, 1f);
            var bg = go.AddComponent<Image>();
            bg.sprite = Theme.White;
            bg.color = idle;
            bg.raycastTarget = true;

            var txt = MakeText(go.transform, "Label", label, 13, FontStyle.Normal, Theme.Text, 0f, 0f);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = Vector2.zero;

            var hover = go.AddComponent<HoverHandler>();
            hover.OnEnter = () => bg.color = Theme.TabHover;
            hover.OnExit = () => bg.color = idle;
            ClickHandler.Attach(go, onClick);
            return go;
        }
    }

    // Shown when both <game>/Mods/Bismuth and <game>/UMMMods/Bismuth exist. Step 1 asks
    // which loader is in use; step 2 offers deleting the unused copy (persistent data is
    // carried over if the deleted copy is the running one). "Keep both" remembers via
    // Settings.IgnoreDuplicateInstall. onClosed always fires exactly once so UpdateChecker
    // can continue the version check.
    internal static class DuplicateInstallPopup
    {
        private static GameObject _canvasGo;
        private static GameObject _panel;
        private static TextMeshProUGUI _body;
        private static readonly System.Collections.Generic.List<GameObject> _buttons
            = new System.Collections.Generic.List<GameObject>();
        private static Action _onClosed;

        public static void Show(string modsDir, string ummModsDir, string activeDir, Action onClosed)
        {
            Close();
            _onClosed = onClosed;

            _canvasGo = new GameObject("BismuthDuplicatePopup");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32601;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            _panel = UIBuilder.Rect("Panel", _canvasGo.transform);
            var pr = (RectTransform)_panel.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(640f, 196f);
            pr.anchoredPosition = new Vector2(0f, 0f);
            var bg = _panel.AddComponent<Image>();
            bg.sprite = Theme.White;
            bg.color = new Color(0.10f, 0.10f, 0.12f, 0.98f);
            bg.raycastTarget = true;

            UpdatePopup.MakeText(_panel.transform, "Title", "Bismuth — duplicate install found",
                17, FontStyle.Bold, Theme.Text, -8f, 30f);
            _body = UpdatePopup.MakeText(_panel.transform, "Body",
                "Bismuth exists in both Mods/ (UMM) and UMMMods/ (MelonLoader + UMMCompat).\nWhich mod loader do you use?",
                13, FontStyle.Normal, Theme.Text, -44f, 60f);

            SetButtons(
                ("UMM (Mods)", () => ChooseLoader(keep: modsDir, del: ummModsDir)),
                ("MelonLoader (UMMMods)", () => ChooseLoader(keep: ummModsDir, del: modsDir)),
                ("Keep both", KeepBoth));
        }

        private static void ChooseLoader(string keep, string del)
        {
            _body.text = "Delete the unused copy?\n" + del +
                         "\n(Your settings and counters are carried over if needed.)";
            _body.fontSize = 12;
            SetButtons(
                ("Delete unused copy", () => DoDelete(keep, del)),
                ("Keep both", KeepBoth));
        }

        private static void DoDelete(string keep, string del)
        {
            if (UpdateChecker.DeleteInstall(keep, del, out string error, out bool deletedActive))
                _body.text = deletedActive
                    ? "Deleted. This session keeps running; restart the game with your selected loader."
                    : "Deleted the unused copy.";
            else
                _body.text = "Delete failed: " + error;
            SetButtons(("Close", Close));
        }

        private static void KeepBoth()
        {
            UpdateChecker.MarkKeepBoth();
            Close();
        }

        private static void SetButtons(params (string label, Action onClick)[] defs)
        {
            foreach (var b in _buttons)
                if (b != null) UnityEngine.Object.Destroy(b);
            _buttons.Clear();

            const float gap = 12f;
            float totalW = 0f;
            var widths = new float[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                widths[i] = 60f + defs[i].label.Length * 8f;
                totalW += widths[i];
            }
            totalW += gap * (defs.Length - 1);

            float x = -totalW / 2f;
            for (int i = 0; i < defs.Length; i++)
            {
                float center = x + widths[i] / 2f;
                _buttons.Add(UpdatePopup.MakeButton(_panel.transform, defs[i].label, center, widths[i], 16f, defs[i].onClick));
                x += widths[i] + gap;
            }
        }

        public static void Close()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _panel = null;
            _body = null;
            _buttons.Clear();
            var cb = _onClosed;
            _onClosed = null;
            cb?.Invoke();
        }
    }
}
