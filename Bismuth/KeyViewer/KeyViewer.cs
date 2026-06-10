using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    // KeyViewer is split into partial files by responsibility:
    //   KeyViewer.cs         (this) — class shell, state fields, internal cell/column types, lifecycle
    //   KeyViewer.Build.cs   — BuildLayout / BuildPresetPanel / cell + layer construction
    //   KeyViewer.Rain.cs    — per-frame Update, StartRainColumn / StopRainColumn
    //   KeyViewer.Sprites.cs — rounded sprite + shadow body sprite + shadow tip texture + gradient texture
    //   KeyViewer.Keys.cs    — TryParseKey + GetDisplayName
    internal partial class KeyViewer : MonoBehaviour
    {
        internal static KeyViewer Instance { get; private set; }

        private Settings _settings;
        private Canvas _canvas;
        private RectTransform _handPanel;
        private RectTransform _footPanel;

        // Draggable panel rects for the location editor.
        internal RectTransform HandPanel => _handPanel;
        internal RectTransform FootPanel => _footPanel;
        private Font _font;

        // Persistent across rebuilds
        // Per-preset counts: presetName → (key → count). Each preset persists its own totals.
        private readonly Dictionary<string, Dictionary<KeyCode, int>> _counts = new Dictionary<string, Dictionary<KeyCode, int>>();
        private readonly Queue<float> _hitTimes = new Queue<float>();

        private class KeyCellRefs
        {
            public RoundedRectGraphic Bg;
            public Text  Name;
            public Text  Count;
            public KeyViewerPreset Preset;
        }

        private class StatCellRefs
        {
            public RoundedRectGraphic Bg;
            public Text  Name;
            public Text  Value;
            public KeyViewerPreset Preset;
        }

        // Per-key UI refs (latest cell wins on overlap)
        // Multiple cells per KeyCode (e.g. same key in both Hand and Foot presets).
        private readonly Dictionary<KeyCode, List<KeyCellRefs>> _keyCells = new Dictionary<KeyCode, List<KeyCellRefs>>();
        private readonly List<StatCellRefs> _kpsCells   = new List<StatCellRefs>();
        private readonly List<StatCellRefs> _totalCells = new List<StatCellRefs>();

        private readonly List<KeyCode> _keys = new List<KeyCode>();
        private readonly Dictionary<KeyCode, string>  _customLabels = new Dictionary<KeyCode, string>();
        private readonly Dictionary<KeyCode, float>   _rainX        = new Dictionary<KeyCode, float>();
        private readonly Dictionary<KeyCode, int>     _rainRowIndex = new Dictionary<KeyCode, int>();
        private readonly Dictionary<KeyCode, KvColor> _rainColors   = new Dictionary<KeyCode, KvColor>();
        private readonly HashSet<KeyCode>             _rainEnabled  = new HashSet<KeyCode>();
        // Ghost keys: spawn rain only — no key cell, not in _keyCells, no count++, no KPS/Total contribution.
        private readonly HashSet<KeyCode>             _ghostKeys    = new HashSet<KeyCode>();
        private readonly Dictionary<int, RectTransform>      _rainLayers   = new Dictionary<int, RectTransform>();
        private readonly Dictionary<int, RectTransform>      _shadowLayers = new Dictionary<int, RectTransform>();
        private readonly Dictionary<int, float>              _rowPanelH    = new Dictionary<int, float>();
        private readonly Dictionary<int, float>              _rowKeyW      = new Dictionary<int, float>();
        private readonly Dictionary<int, int>                _rowRainDepth = new Dictionary<int, int>();
        private readonly Dictionary<int, float>              _rowGap       = new Dictionary<int, float>();
        private readonly Dictionary<int, KeyViewerPreset>    _rowPreset    = new Dictionary<int, KeyViewerPreset>();

        private readonly List<RainColumn> _rainColumns = new List<RainColumn>();
        private readonly List<GameObject> _allPanels   = new List<GameObject>();
        private readonly List<Sprite>     _allSprites  = new List<Sprite>();
        private readonly List<Texture2D>  _allTextures = new List<Texture2D>();
        private readonly Dictionary<int, Sprite>    _shadowBodySprites       = new Dictionary<int, Sprite>();
        private readonly Dictionary<int, Sprite>    _shadowBodySpritesSoftTop = new Dictionary<int, Sprite>();
        private readonly Dictionary<int, Texture2D> _shadowTipTextures       = new Dictionary<int, Texture2D>();
        private Texture2D _gradTex;
        private int _nextRowIdx;

        private class RainColumn
        {
            public KeyCode       Key;
            public RectTransform BodyRt;
            public Image         BodyImg;
            public RectTransform TipRt;
            public RawImage      TipImg;
            public RectTransform ShadowBodyRt;
            public Image         ShadowBodyImg;
            public RectTransform ShadowTipRt;
            public RawImage      ShadowTipImg;
            public Color         BaseColor;
            public Color         ShadowColor;
            public float         Width;
            public float         ShadowSize;
            public float         Height;
            public float         BotY;
            public float         PanelHeight;
            public float         Gap;
            public KeyViewerPreset Preset;
            public bool          Growing;
        }

        private int _lastKps   = -1;
        private readonly Dictionary<string, int> _lastTotalPerPreset = new Dictionary<string, int>();

        private static bool AnyViewerOn(Settings s) =>
            !s.ActiveHideAllUI && s.ShowKeyViewer &&
            ((s.ShowHandViewer && s.Hand != null) || (s.ShowFootViewer && s.Foot != null));

        private static bool NeedsPersist(Settings s) =>
            (s.Hand != null && s.Hand.PersistCounts) || (s.Foot != null && s.Foot.PersistCounts);

        internal static KeyViewer Create(Settings settings)
        {
            var go = new GameObject("BismuthKeyViewer");
            DontDestroyOnLoad(go);
            var kv = go.AddComponent<KeyViewer>();
            Instance = kv;
            kv._settings = settings;
            kv.BuildCanvas();
            if (NeedsPersist(settings))
                kv.LoadCounts();
            if (AnyViewerOn(settings))
                kv.BuildLayout();
            return kv;
        }

        internal void ApplySettings(Settings settings)
        {
            _settings = settings;
            _canvas.gameObject.SetActive(AnyViewerOn(settings));
            if (AnyViewerOn(settings) && _handPanel == null && _footPanel == null)
                BuildLayout();
            if (_handPanel != null && settings.Hand != null)
            {
                _handPanel.anchorMin = new Vector2(settings.Hand.X, settings.Hand.Y);
                _handPanel.anchorMax = new Vector2(settings.Hand.X, settings.Hand.Y);
                _handPanel.localScale = Vector3.one * settings.Hand.Scale;
            }
            if (_footPanel != null && settings.Foot != null)
            {
                _footPanel.anchorMin = new Vector2(settings.Foot.X, settings.Foot.Y);
                _footPanel.anchorMax = new Vector2(settings.Foot.X, settings.Foot.Y);
                _footPanel.localScale = Vector3.one * settings.Foot.Scale;
            }
            ApplyColors();
        }

        private void ApplyColors()
        {
            foreach (var kvp in _keyCells)
                foreach (var c in kvp.Value)
                {
                    if (c?.Preset == null) continue;
                    if (c.Bg    != null) { c.Bg.color = c.Preset.BgIdle.ToColor(); c.Bg.BorderColor = c.Preset.BorderIdle.ToColor(); }
                    if (c.Name  != null) { c.Name.color  = c.Preset.TxtIdle.ToColor();   c.Name.fontSize  = c.Preset.LabelSize; }
                    if (c.Count != null) { c.Count.color = c.Preset.CountIdle.ToColor(); c.Count.fontSize = c.Preset.CountSize; }
                }
            ApplyStatColors(_kpsCells);
            ApplyStatColors(_totalCells);
        }

        private static void ApplyStatColors(List<StatCellRefs> cells)
        {
            foreach (var s in cells)
            {
                if (s?.Preset == null) continue;
                if (s.Bg    != null) { s.Bg.color = s.Preset.BgIdle.ToColor(); s.Bg.BorderColor = s.Preset.BorderIdle.ToColor(); }
                if (s.Name  != null) { s.Name.color  = s.Preset.TxtIdle.ToColor();   s.Name.fontSize  = s.Preset.LabelSize; }
                if (s.Value != null) { s.Value.color = s.Preset.CountIdle.ToColor(); s.Value.fontSize = s.Preset.CountSize; }
            }
        }

        internal void Rebuild(Settings settings)
        {
            _settings = settings;
            ClearLayout();
            BuildLayout();
            _canvas.gameObject.SetActive(AnyViewerOn(settings));
        }

        // Move a cell's persisted press count from its old key to the new one when the user
        // rebinds a KV cell. Called by the settings UI before the rebuild so the freshly-built
        // cell picks up the carried-over count from _counts. Old key is only forgotten if no
        // other cell in the same preset still uses it (otherwise that other cell would zero
        // out on the next rebuild).
        internal void TransferKeyCount(KeyViewerPreset preset, KeyCode oldKey, KeyCode newKey)
        {
            if (preset == null || oldKey == newKey) return;
            string pn = preset.Name ?? "";
            if (!_counts.TryGetValue(pn, out var dict)) return;
            if (!dict.TryGetValue(oldKey, out int count) || count <= 0) return;

            dict.TryGetValue(newKey, out int existing);
            dict[newKey] = existing + count;

            bool oldStillUsed = false;
            if (preset.Rows != null)
                foreach (var row in preset.Rows)
                {
                    if (row?.Cells == null) continue;
                    foreach (var c in row.Cells)
                        if (c?.Token != null && TryParseKey(c.Token, out var kc) && kc == oldKey)
                        { oldStillUsed = true; break; }
                    if (oldStillUsed) break;
                }
            if (!oldStillUsed) dict.Remove(oldKey);
        }

        internal void ResetCounts()
        {
            foreach (var preset in _counts.Values)
                foreach (var k in new List<KeyCode>(preset.Keys)) preset[k] = 0;
            while (_hitTimes.Count > 0) _hitTimes.Dequeue();
            foreach (var kvp in _keyCells)
                foreach (var c in kvp.Value)
                    if (c?.Count != null) c.Count.text = "0";
            foreach (var s in _kpsCells)   if (s?.Value != null) s.Value.text = "0";
            foreach (var s in _totalCells) if (s?.Value != null) s.Value.text = "0";
            _lastKps = -1;
            _lastTotalPerPreset.Clear();
        }

        internal void SetFont(Font font)
        {
            _font = font;
            foreach (var kvp in _keyCells)
                foreach (var c in kvp.Value)
                {
                    if (c == null) continue;
                    if (c.Name  != null) c.Name.font  = font;
                    if (c.Count != null) c.Count.font = font;
                }
            foreach (var s in _kpsCells)   { if (s?.Name != null) s.Name.font = font; if (s?.Value != null) s.Value.font = font; }
            foreach (var s in _totalCells) { if (s?.Name != null) s.Name.font = font; if (s?.Value != null) s.Value.font = font; }
        }

        internal void SaveCounts()
        {
            if (!NeedsPersist(_settings)) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var presetEntry in _counts)
                    foreach (var kvp in presetEntry.Value)
                        sb.Append(presetEntry.Key).Append('\t').Append(kvp.Key.ToString()).Append('\t').Append(kvp.Value).Append('\n');
                File.WriteAllText(CountsPath(), sb.ToString());
            }
            catch (Exception e) { MainClass.Logger.Log("KeyViewer: save failed: " + e.Message); }
        }

        private void LoadCounts()
        {
            try
            {
                string path = CountsPath();
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 3 || !int.TryParse(parts[2], out int c)) continue;
                    try
                    {
                        var kc = (KeyCode)Enum.Parse(typeof(KeyCode), parts[1], true);
                        if (!_counts.TryGetValue(parts[0], out var dict))
                            _counts[parts[0]] = dict = new Dictionary<KeyCode, int>();
                        dict[kc] = c;
                    }
                    catch { }
                }
            }
            catch (Exception e) { MainClass.Logger.Log("KeyViewer: load failed: " + e.Message); }
        }

        private static string CountsPath() =>
            Path.Combine(MainClass.ModPath, "keycounts.txt");

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            ConfigureScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.SetActive(AnyViewerOn(_settings));
        }

        private static void ConfigureScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void ClearLayout()
        {
            foreach (var col in _rainColumns)
            {
                if (col.BodyRt != null) Destroy(col.BodyRt.gameObject);
                if (col.TipRt  != null) Destroy(col.TipRt.gameObject);
                if (col.ShadowBodyRt != null) Destroy(col.ShadowBodyRt.gameObject);
                if (col.ShadowTipRt  != null) Destroy(col.ShadowTipRt.gameObject);
            }
            _rainColumns.Clear();
            _rainColors.Clear();
            _rainEnabled.Clear();
            _ghostKeys.Clear();
            _rainX.Clear();
            _rainRowIndex.Clear();
            _rainLayers.Clear();
            _shadowLayers.Clear();
            _rowPanelH.Clear();
            _rowKeyW.Clear();
            _rowRainDepth.Clear();
            _rowGap.Clear();
            _rowPreset.Clear();
            foreach (var p in _allPanels) if (p != null) Destroy(p);
            _allPanels.Clear();
            _handPanel = null;
            _footPanel = null;
            foreach (var s in _allSprites) if (s != null) Destroy(s);
            _allSprites.Clear();
            foreach (var t in _allTextures) if (t != null) Destroy(t);
            _allTextures.Clear();
            _shadowBodySprites.Clear();
            _shadowBodySpritesSoftTop.Clear();
            _shadowTipTextures.Clear();
            _keyCells.Clear();
            _kpsCells.Clear();
            _totalCells.Clear();
            _keys.Clear();
            _customLabels.Clear();
        }
    }
}
