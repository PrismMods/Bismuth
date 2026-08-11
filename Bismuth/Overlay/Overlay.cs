using System.Collections.Generic;
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
        private GameObject kpsRow;
        private TextMeshProUGUI kpsLabel;
        private TextMeshProUGUI kpsValue;
        private GameObject songDurRow;
        private TextMeshProUGUI songDurLabel;
        private TextMeshProUGUI songDurValue;
        private GameObject levelDurRow;
        private TextMeshProUGUI levelDurLabel;
        private TextMeshProUGUI levelDurValue;
        private GameObject bestRow;
        private TextMeshProUGUI bestLabel;
        private TextMeshProUGUI bestValue;
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
        private GameObject progressBarGo;
        private RectTransform progressBarFill;
        private UnityEngine.UI.Image progressBarFillImg;
        private float _lastBarT = -1f;

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

        /* A judgement column is a HitMargin cast to int, except for these three: with the
           XPerfect mod loaded, the Perfect column splits into its breakdown and the counts
           come from XPerfect rather than _judgementCounts. Negative so they can never
           collide with a HitMargin value. */
        private const int ColXPerfect = -1, ColPlusPerfect = -2, ColMinusPerfect = -3;

        private static int[] _columns;
        // Dropped whenever XPerfect's availability might have changed, so the row can be rebuilt.
        private static void InvalidateJudgementColumns() => _columns = null;

        private static int[] JudgementColumns
        {
            get
            {
                if (_columns != null) return _columns;
                var list = new List<int>(DisplayedMargins.Length + 2);
                foreach (var m in DisplayedMargins)
                {
                    if (m == HitMargin.Perfect && XPerfectBridge.Available)
                    {
                        /* Early → late, matching the rest of the row (and the hit error meter
                           above it). XPerfect's GetDetailedJudge assigns PlusPerfect when its
                           signed delta is NEGATIVE, and its delta is (hitAngle - refAngle)
                           flipped for direction — negative means the planet hadn't reached the
                           reference angle yet, i.e. early. So +Perfect is the early side. */
                        list.Add(ColPlusPerfect);
                        list.Add(ColXPerfect);
                        list.Add(ColMinusPerfect);
                    }
                    else list.Add((int)m);
                }
                return _columns = list.ToArray();
            }
        }

        // XPerfect maintains its own counters (including across a checkpoint revive, which is
        // why RebuildFromTracker doesn't touch them) — read, never derive.
        private int ColumnCount(int col)
        {
            switch (col)
            {
                case ColXPerfect:     return XPerfectBridge.XPerfect;
                case ColPlusPerfect:  return XPerfectBridge.PlusPerfect;
                case ColMinusPerfect: return XPerfectBridge.MinusPerfect;
                default:              return _judgementCounts[col];
            }
        }

        private static Color ColumnColor(int col)
        {
            switch (col)
            {
                case ColXPerfect:     return XPerfectBridge.XColor;
                case ColPlusPerfect:
                case ColMinusPerfect: return XPerfectBridge.PlusColor;
                default:              return MarginColor((HitMargin)col);
            }
        }

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
        private float _lastKpsVal = -1f;
        private float _lastTimingScale = -1f;
        private int _lastComboDisplay = -1;
        private int _lastPrecision = -1;
        // Cached "F<precision>" numeric format, rebuilt only when Precision changes — the
        // per-frame `"F" + Precision` string concat was a steady gameplay allocation.
        private string _fmt = "F2";
        // Last no-fail state pushed to the judgement toggles, so SetActive fires only on change.
        private bool? _lastNoFail;
        // Duration rows show elapsed/total; totals computed lazily per attempt, elapsed
        // text rebuilt only when the displayed second ticks.
        private float _songDurTotal = -1f;
        private string _songDurTotalText;
        private int _lastSongElapsed = -1;
        private float _levelDurTotal = -1f;
        private string _levelDurTotalText;
        private int _lastLevelElapsed = -1;

        // Best-% tracking: quantized furthest progress during full (from-0%) attempts.
        // Loaded per level, persisted at attempt boundaries when dirty.
        private float _bestPct;
        private bool _isFullAttempt;
        private bool _bestDirty;

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

        private void OnActiveSceneChanged(Scene _, Scene to)
        {
            ShowOrHideElements();
            /* New scene = fresh game text objects, so repaint them if the option is on.
               An immediate sweep alone is not enough: localization re-stamps fonts in
               object Start(), after this event, and the delayed sweeps catch that.

               A retry reloads the gameplay scene (scnGame) on every attempt; full-scanning
               a large map's thousands of tile/decoration texts each time hitched, so there
               we sweep only the HUD + autoplay label (scoped, delayed scoped re-sweep for
               late HUD spawns). The first entry still full-sweeps via OnLevelStart, so
               nothing styled there is missed. Menu scenes keep the full sweep — their world
               text activates late and outside any canvas. */
            bool gameplay = false;
            try { gameplay = to.name == "scnGame"; } catch { }
            if (gameplay)
            {
                GameFontApplier.ReapplyHud();
                GameFontApplier.RequestSweepSoon();
            }
            else
            {
                GameFontApplier.Reapply();
                GameFontApplier.RequestFullSweepSoon();
            }
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
