using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Bismuth
{
    /* Overlay is split into partial files by responsibility:
         Overlay.cs        (this): class shell, state, lifecycle (Awake/OnDestroy/scene hooks), helpers
         Overlay.Build.cs:  UGUI tree construction (canvas, containers, rows, combo, FPS, judgements)
         Overlay.Update.cs: per-frame Update + UpdateDisplay
         Overlay.Game.cs:   game-event handlers (OnAttempt, OnLevelStart, OnLevelEnd, ShowEmpty, SetFont, ResetAttempts)
         Overlay.Apply.cs:  ApplySettings, PlaceRows/Attach, ShowOrHideElements, ApplyLevelNameTransform */
    public partial class Overlay : MonoBehaviour
    {
        public static Overlay Instance { get; private set; }
        public bool InLevel => inLevel;

        /* Location-edit mode (Locations tab). Forces canvas visible so elements can be
           dragged outside a level; ShowEmpty() supplies placeholder values */
        private bool _editMode;
        internal bool EditMode
        {
            get { return _editMode; }
            set
            {
                _editMode = value;
                if (value && !inLevel) ShowEmpty();
            }
        }

        // Draggable element rects for location editor
        internal RectTransform LeftPanelRect  => leftContainer as RectTransform;
        internal RectTransform RightPanelRect => rightContainer as RectTransform;
        internal RectTransform ComboRect      => comboDisplayContainer;
        internal RectTransform JudgementsRect  => judgementsContainer;
        internal RectTransform AttemptsRect    => attemptsContainer;
        internal RectTransform TimingScaleRect => timingScaleContainer;
        internal RectTransform ComboLabelRect  => _comboLabelWrapper;

        private Canvas canvas;
        private Transform leftContainer;
        private Transform rightContainer;
        private RectTransform timingScaleContainer;
        private RectTransform judgementsContainer;
        private RectTransform attemptsContainer;

        private int _attempts;
        private int _fullAttempts;
        private string _currentLevelKey;
        private int _combo;
        private float _comboPulseT;
        /* Per-attempt hit counts (one slot per HitMargin). Tracked internally because
           game tracker.hitMarginsCount can carry stale checkpoint state into a fresh
           attempt */
        private readonly int[] _judgementCounts = new int[12];

        private GameObject progressRow;
        private TextMeshProUGUI progressLabel;
        private TextMeshProUGUI progressValue;
        private GameObject attemptsRow;
        private TextMeshProUGUI attemptsLabel;
        private TextMeshProUGUI attemptsValue;
        private GameObject attemptsFullRow;
        private TextMeshProUGUI attemptsFullLabel;
        private TextMeshProUGUI attemptsFullValue;
        private GameObject accRow;
        private TextMeshProUGUI accLabel;
        private TextMeshProUGUI accValue;
        private GameObject xaccRow;
        private TextMeshProUGUI xaccLabel;
        private TextMeshProUGUI xaccValue;
        private GameObject bpmRow;
        private TextMeshProUGUI bpmLabel;
        private TextMeshProUGUI bpmValue;
        private GameObject tileBpmRow;
        private TextMeshProUGUI tileBpmLabel;
        private TextMeshProUGUI tileBpmValue;
        private GameObject timingScaleRow;
        private TextMeshProUGUI timingScaleLabel;
        private TextMeshProUGUI timingScaleValue;
        private GameObject judgementsRow;
        private TextMeshProUGUI[] judgementTexts;
        private RectTransform comboDisplayContainer;
        private RectTransform _comboLabelWrapper;
        private TextMeshProUGUI comboDisplayLabel;
        private TextMeshProUGUI comboDisplayValue;
        private TmpShadow _comboValueShadow;
        private TmpShadow _comboLabelShadow;
        private GameObject fpsContainer;
        private TextMeshProUGUI fpsText;

        private float _fpsAccum;
        private int _fpsFrames;
        private const float FpsInterval = 0.2f;

        private const float ShadowBaseOffset     = 2f;
        private const int RowBaseFontSize        = 27;
        private const int ComboLabelBaseFontSize = 24;
        private const int ComboValueBaseFontSize = 90;
        private int? _levelNameOrigFontSize;

        private static readonly HitMargin[] DisplayedMargins =
        {
            HitMargin.FailOverload,
            HitMargin.TooEarly, HitMargin.VeryEarly, HitMargin.EarlyPerfect,
            HitMargin.Perfect,
            HitMargin.LatePerfect, HitMargin.VeryLate, HitMargin.TooLate,
            HitMargin.FailMiss,
        };

        private bool inLevel;
        private float _lastTileBpmTime = -1f;
        private float _lastTileBpm;
        private Vector2? _levelNameOrigPos;
        /* txtLevelName is the game's legacy uGUI Text. We render it in our TMP font via a
           GameTextShadow (hides the original, draws a TMP child with our font + drop shadow);
           no Bismuth legacy Font is involved. This is the chosen overlay TMP weight. */
        private TMP_FontAsset _levelNameFont;

        // Cached values to avoid per-frame string allocation when display unchanged
        private float _lastProgressT = -1f;
        private float _lastBpm = -1f;
        private float _lastTileBpmVal = -1f;
        private float _lastTimingScale = -1f;
        private int _lastComboDisplay = -1;
        private int _lastPrecision = -1;

        public static Overlay Create()
        {
            var go = new GameObject("BismuthOverlay");
            DontDestroyOnLoad(go);
            return go.AddComponent<Overlay>();
        }

        private void Awake()
        {
            Instance = this;
            BuildUI();
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            if (Instance == this) Instance = null;
        }

        private void OnSceneUnloaded(Scene _)
        {
            inLevel = false;
            RDC.noHud = false;
            _levelNameOrigPos = null;
            _levelNameOrigFontSize = null;
        }

        private void OnActiveSceneChanged(Scene _, Scene __)
        {
            ShowOrHideElements();
            /* New scene = fresh game text objects, so repaint them if the option is on.
               An immediate sweep alone is not enough: localization re-stamps fonts in
               object Start(), after this event, and the delayed sweeps catch that. */
            GameFontApplier.Reapply();
            GameFontApplier.RequestFullSweepSoon();
            GameUiLayout.Reapply();
        }

        private static Color MarginColor(HitMargin m)
        {
            // RDConstants.data is a lazy getter that can NRE inside during startup
            RDConstants data;
            try { data = RDConstants.data; }
            catch { return Color.white; }
            if (data == null) return Color.white;
            var c = data.hitMarginColoursUI;
            if (c == null) return Color.white;
            switch (m)
            {
                case HitMargin.TooEarly:     return c.colourTooEarly;
                case HitMargin.VeryEarly:    return c.colourVeryEarly;
                case HitMargin.EarlyPerfect: return c.colourLittleEarly;
                case HitMargin.Perfect:      return c.colourPerfect;
                case HitMargin.LatePerfect:  return c.colourLittleLate;
                case HitMargin.VeryLate:     return c.colourVeryLate;
                case HitMargin.TooLate:      return c.colourTooLate;
                case HitMargin.Multipress:   return c.colourMultipress;
                default:                     return c.colourFail;
            }
        }

        private const string DefaultStatSeparator = " | ";

        internal static string StatSeparator(Settings s) =>
            string.IsNullOrEmpty(s.StatSeparator) ? DefaultStatSeparator : s.StatSeparator;

        private static string TrimZeros(string s)
        {
            int dot = s.IndexOf('.');
            if (dot < 0) return s;
            s = s.TrimEnd('0');
            return s[s.Length - 1] == '.' ? s.Substring(0, s.Length - 1) : s;
        }

        private static void ConfigureScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private static void AddShadow(GameObject go)
        {
            TmpShadow.Attach(go, new Color(0f, 0f, 0f, 0.5f), new Vector2(2f, -2f));
        }
    }
}
