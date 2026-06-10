using System.Collections.Generic;
using UnityModManagerNet;

namespace Bismuth
{
    public enum OverlayPosition { Left, Right }

    public class KvColor
    {
        public float R, G, B, A;
        public UnityEngine.Color ToColor() => new UnityEngine.Color(R, G, B, A);
    }

    public class KeyViewerCell
    {
        public string Token = "";    // "Tab" / "KPS" / "Total" / "A" / "[" / etc.
        public string Label = null;  // optional display override; null = auto from Token
        public float WidthMul = 1f;  // relative width within its row (default 1.0)
    }

    public class KeyViewerRow
    {
        public List<KeyViewerCell> Cells = new List<KeyViewerCell>();
        public float Height = 60f;
        public KvColor RainColor = null;
        public bool ShowRain = true;

        public void EnsureDefaults()
        {
            if (Cells == null) Cells = new List<KeyViewerCell>();
            foreach (var c in Cells) if (c.WidthMul <= 0f) c.WidthMul = 1f;
        }
    }

    public class KeyViewerPreset
    {
        public string Name              = "Preset";
        public bool   PersistCounts     = true;
        public List<KeyViewerRow> Rows  = null;
        public float  KeyWidth          = 60f;
        public int    Radius            = 8;
        public float  BorderWidth       = 0f;
        public KvColor BorderIdle       = null;
        public KvColor BorderHeld       = null;
        public float  Gap               = 4f;
        public float  X                 = 0.01f;
        public float  Y                 = 0.01f;
        public float  Scale             = 1.0f;
        public float  RainDistance      = 300f;
        public float  RainTrackLength    = 390f;
        public float  RainSpeed         = 500f;
        public float  RainWidthStep     = 14f;
        public float  RainShadowSize    = 0f;
        public KvColor RainShadowColor  = null;
        public KvColor BgIdle           = null;
        public KvColor BgHeld           = null;
        public KvColor TxtIdle          = null;
        public KvColor TxtHeld          = null;
        public int     LabelSize        = 16;
        public bool    ShowLabel        = true;
        public KvColor CountIdle        = null;
        public KvColor CountHeld        = null;
        public int     CountSize        = 13;
        public bool    ShowCount        = true;

        // Ghost keys: keys whose press only spawns rain (no key cell, no count, no tile-hit).
        // Indexed against the top row's non-stat cells. Token "None" or empty = unassigned slot.
        public bool         GhostKeysEnabled = false;
        public List<string> GhostKeys        = null;
        public KvColor      GhostRainColor   = null;

        public void EnsureDefaults()
        {
            if (BgIdle    == null) BgIdle    = new KvColor { R = 0f,   G = 0f,   B = 0f,   A = 0.70f };
            if (BgHeld    == null) BgHeld    = new KvColor { R = 1f,   G = 1f,   B = 1f,   A = 1f };
            if (TxtIdle   == null) TxtIdle   = new KvColor { R = 1f,   G = 1f,   B = 1f,   A = 1f };
            if (TxtHeld   == null) TxtHeld   = new KvColor { R = 0f,   G = 0f,   B = 0f,   A = 1f };
            if (CountIdle == null) CountIdle = new KvColor { R = 0.7f, G = 0.7f, B = 0.7f, A = 1f };
            if (CountHeld == null) CountHeld = new KvColor { R = 0f,   G = 0f,   B = 0f,   A = 1f };
            if (RainShadowColor == null) RainShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.05f };
            if (BorderIdle      == null) BorderIdle      = new KvColor { R = 1f, G = 1f, B = 1f, A = 1f };
            if (BorderHeld      == null) BorderHeld      = new KvColor { R = 1f, G = 1f, B = 1f, A = 1f };
            if (Rows != null) foreach (var r in Rows) r?.EnsureDefaults();
        }
    }

    public class Settings : UnityModManager.ModSettings
    {
        public bool ShowProgress = true;
        public bool ShowAcc = false;
        public bool ShowXAcc = true;
        public bool ShowBpm = true;
        public bool ShowTileBpm = true;
        public bool ShowTimingScale = true;
        public bool ShowJudgements = true;
        public float JudgementsSize = 0.9f;
        public float JudgementsY = 0f;
        public float JudgementsGap = 12f;
        public float Scale = 1.0f;
        public int Precision = 2;
        public string FontName = "Maplestory Bold";
        public bool ShowOverlay = true;
        public bool ShowFps = false;
        public bool OptSpectrumThrottle = true;
        public bool OptTextureNonReadable = true;
        public bool OptTextureDXT = false;
        public bool OptPhysicsNonAlloc = true;
        public bool OptUnloadAssets = true;
        public bool OptVolumeTrackDOTween = true;
        public bool ShowAttempts = false;
        public bool ShowFullAttempts = false;

        public List<KeyViewerPreset> KvHandPresets = null;
        public List<KeyViewerPreset> KvFootPresets = null;
        public int  KvActiveHand   = 0;
        public int  KvActiveFoot   = 0;
        public bool ShowKeyViewer  = true;
        public bool ShowHandViewer = true;
        public bool ShowFootViewer = false;

        [System.Xml.Serialization.XmlIgnore]
        public KeyViewerPreset Hand =>
            (KvHandPresets != null && KvActiveHand >= 0 && KvActiveHand < KvHandPresets.Count)
                ? KvHandPresets[KvActiveHand] : null;

        [System.Xml.Serialization.XmlIgnore]
        public KeyViewerPreset Foot =>
            (KvFootPresets != null && KvActiveFoot >= 0 && KvActiveFoot < KvFootPresets.Count)
                ? KvFootPresets[KvActiveFoot] : null;

        public bool ShowComboDisplay = true;
        public float ComboDisplayY = 0f;
        public float ComboDisplaySize = 1.0f;
        public string ComboDisplayText = "Perfect Combo";
        public float ComboPulseDuration = 0.2f;
        public float ComboPulseOffsetY = 8f;
        public float ComboPulseScale   = 0.2f;
        public float ComboLabelY = 65f;
        public float ComboLabelSize = 1.0f;
        public float ComboCountSize = 1.0f;
        // Count (value) shadow.
        public float ComboShadowOffsetX = 4f;
        public float ComboShadowOffsetY = -4f;
        public KvColor ComboShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };

        // Label shadow — independent. Defaults match the previously-derived proportional size
        // (sqrt(30/80) ≈ 0.612 × 4 ≈ 2.45 → rounded to 2.5).
        public float ComboLabelShadowOffsetX = 2.5f;
        public float ComboLabelShadowOffsetY = -2.5f;
        public KvColor ComboLabelShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };
        public bool ComboCountAuto = false;

        public bool BlockInputsWhileMenuOpen = true;

        public bool KeyLimiterEnabled = true;
        public bool KeyLimiterUseKvKeys = true;
        public string KeyLimiterCustomKeys = "";

        public bool ChatterBlockerEnabled = false;
        public int  ChatterThresholdMs = 50;

        public bool HideUiEnabled = true;
        public bool HideAllUI = false;
        public bool HideHitmeter = false;
        public bool HideAutoplayText = false;
        public bool HideAutoplayIcon = false;
        public bool HideNoFail = false;
        public bool HideDifficulty = false;
        public bool HidePerfectJudgements = false;
        public bool HideLevelName = false;
        public bool HideBetaBuild = false;

        // Effective Hide* — each Hide flag is gated by the section's master toggle so consumers
        // never need to repeat the && HideUiEnabled check.
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideAllUI             => HideUiEnabled && HideAllUI;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideHitmeter          => HideUiEnabled && HideHitmeter;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideAutoplayText      => HideUiEnabled && HideAutoplayText;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideAutoplayIcon      => HideUiEnabled && HideAutoplayIcon;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideNoFail            => HideUiEnabled && HideNoFail;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideDifficulty        => HideUiEnabled && HideDifficulty;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHidePerfectJudgements => HideUiEnabled && HidePerfectJudgements;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideLevelName         => HideUiEnabled && HideLevelName;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideBetaBuild         => HideUiEnabled && HideBetaBuild;

        public OverlayPosition ProgressPosition  = OverlayPosition.Left;
        public OverlayPosition AccPosition       = OverlayPosition.Left;
        public OverlayPosition XAccPosition      = OverlayPosition.Left;
        public OverlayPosition BpmPosition       = OverlayPosition.Right;
        public OverlayPosition TileBpmPosition   = OverlayPosition.Right;
        public float TimingScaleY    = 0f;
        public float TimingScaleSize = 0.75f;
        public float AttemptsX = 0.77f;
        public float AttemptsY = 0.05f;
        public float LevelNameScale = 0.5f;
        public float LevelNameY     = 40f;

        // Normalized screen anchors for the draggable elements (Locations tab). Defaults
        // reproduce the historical fixed placements at the 1920×1080 reference resolution
        // (status panels: 10px inset from the top corners).
        public float StatusLeftX  = 0.0052f;
        public float StatusLeftY  = 0.9907f;
        public float StatusRightX = 0.9948f;
        public float StatusRightY = 0.9907f;
        public float ComboDisplayX       = 0.5f;
        public float ComboDisplayAnchorY = 0.85f;
        public float JudgementsX         = 0.5f;
        public float JudgementsAnchorY   = 0f;
        public float TimingScaleX        = 0.5f;
        public float TimingScaleAnchorY  = 0.12f;

        public ColorGradient ProgressGradient;
        public ColorGradient AccGradient;
        public ColorGradient BpmGradient;
        public ColorGradient ComboGradient;
        public float ComboGradientMax = 1000f;

        // X-Accuracy and Tile BPM inherit from Acc/Bpm by default. Toggle off to give them
        // their own gradient. The standalone gradients persist either way so users don't
        // lose work when toggling back and forth.
        public bool XAccUseAccGradient = true;
        public ColorGradient XAccGradient;
        public bool TileBpmUseBpmGradient = true;
        public ColorGradient TileBpmGradient;

        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ActiveXAccGradient => XAccUseAccGradient ? AccGradient : XAccGradient;
        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ActiveTileBpmGradient => TileBpmUseBpmGradient ? BpmGradient : TileBpmGradient;

        // UGUI settings panel preferences (new shell — see UI/).
        public float UiScale = 1.0f;
        public string UiFontName = "";
        public bool UiAccentCustom = false;
        public float UiAccentR = 0.604f;
        public float UiAccentG = 0.706f;
        public float UiAccentB = 1.0f;
        // Panel dimensions are saved across sessions; position is not (always re-centered).
        public float UiPanelWidth = 840f;
        public float UiPanelHeight = 540f;

        public void EnsureDefaults()
        {
            if (ProgressGradient == null || ProgressGradient.Stops.Count == 0)
                ProgressGradient = MakeProgressDefault();
            if (AccGradient == null || AccGradient.Stops.Count == 0)
                AccGradient = MakeAccDefault();
            if (BpmGradient == null || BpmGradient.Stops.Count == 0)
                BpmGradient = MakeBpmDefault();
            if (ComboGradient == null || ComboGradient.Stops.Count == 0)
                ComboGradient = MakeComboDefault();
            if (XAccGradient == null || XAccGradient.Stops.Count == 0)
                XAccGradient = MakeAccDefault();
            if (TileBpmGradient == null || TileBpmGradient.Stops.Count == 0)
                TileBpmGradient = MakeBpmDefault();
            if (KvHandPresets == null || KvHandPresets.Count == 0)
            {
                KvHandPresets = new List<KeyViewerPreset>
                {
                    Make10kHandPreset(),
                    Make12kHandPreset(),
                    Make16kHandPreset(),
                };
                KvActiveHand = 1;
            }
            if (KvFootPresets == null || KvFootPresets.Count == 0)
            {
                KvFootPresets = new List<KeyViewerPreset>
                {
                    Make2kFootPreset(),
                    Make4kFootPreset(),
                    Make8kFootPreset(),
                    Make16kFootPreset(),
                };
                KvActiveFoot = 0;
            }
            foreach (var p in KvHandPresets) p.EnsureDefaults();
            foreach (var p in KvFootPresets) p.EnsureDefaults();
        }

        // Shorthand builder for default-preset rows. Tokens are space-separated; append `:Label` to
        // override the displayed text (e.g. `Backspace:Back`).
        private static KeyViewerRow Row(string tokens, float height, KvColor rain = null)
        {
            var cells = new List<KeyViewerCell>();
            foreach (var raw in tokens.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw;
                string lbl = null;
                int c = raw.IndexOf(':');
                if (c > 0 && c < raw.Length - 1) { tok = raw.Substring(0, c); lbl = raw.Substring(c + 1); }
                cells.Add(new KeyViewerCell { Token = tok, Label = lbl });
            }
            return new KeyViewerRow { Cells = cells, Height = height, RainColor = rain };
        }

        private static readonly KvColor BlueRain = new KvColor { R = 0.20f, G = 0.60f, B = 1.00f, A = 1f };

        private static KeyViewerPreset Make10kHandPreset() => new KeyViewerPreset
        {
            Name = "10k",
            Rows = new List<KeyViewerRow>
            {
                Row("Tab 1 2 E P = Backspace \\", 60f),
                Row("KPS Space ,:RAlt Total", 45f, BlueRain),
            }
        };

        private static KeyViewerPreset Make12kHandPreset() => new KeyViewerPreset
        {
            Name = "12k",
            Rows = new List<KeyViewerRow>
            {
                Row("Tab 1 2 E P = Backspace \\", 60f),
                Row("KPS C Space ,:RAlt . Total", 45f, BlueRain),
            }
        };

        private static KeyViewerPreset Make16kHandPreset() => new KeyViewerPreset
        {
            Name = "16k",
            Rows = new List<KeyViewerRow>
            {
                Row("Tab 1 2 E P = Backspace \\", 60f),
                Row("`:Caps LShift C Space ,:RAlt . RShift Enter", 60f, BlueRain),
                Row("KPS Total", 35f),
            }
        };

        private static KeyViewerPreset Make2kFootPreset() => new KeyViewerPreset
        {
            Name = "2k",
            KeyWidth = 30f, Gap = 2f, X = 0.3f, Y = 0.01f,
            LabelSize = 14, CountSize = 8, ShowCount = false,
            Rows = new List<KeyViewerRow> { Row("F8 F3", 30f) }
        };

        private static KeyViewerPreset Make4kFootPreset() => new KeyViewerPreset
        {
            Name = "4k",
            KeyWidth = 30f, Gap = 2f, X = 0.3f, Y = 0.01f,
            LabelSize = 14, CountSize = 8, ShowCount = false,
            Rows = new List<KeyViewerRow> { Row("F8 F7 F3 F2", 30f) }
        };

        private static KeyViewerPreset Make8kFootPreset() => new KeyViewerPreset
        {
            Name = "8k",
            KeyWidth = 30f, Gap = 2f, X = 0.3f, Y = 0.01f,
            LabelSize = 14, CountSize = 8, ShowCount = false,
            Rows = new List<KeyViewerRow> { Row("F8 F7 F6 F5 F4 F3 F2 F1", 30f) }
        };

        private static KeyViewerPreset Make16kFootPreset() => new KeyViewerPreset
        {
            Name = "16k",
            KeyWidth = 30f, Gap = 2f, X = 0.3f, Y = 0.01f,
            LabelSize = 14, CountSize = 8, ShowCount = false,
            Rows = new List<KeyViewerRow>
            {
                Row("F8 F7 F6 F5 F4 F3 F2 F1", 30f),
                Row("0 9 8 7 6 5 4 3", 30f),
            }
        };

        private static ColorGradient MakeProgressDefault() => new ColorGradient
        {
            HasPerfectColor = true,
            PR = 1f, PG = 0.855f, PB = 0f, PA = 1f,
            Stops = new List<ColorStop>
            {
                new ColorStop { Progress = 0.0f, R = 0.9f, G = 0.9f, B = 0.9f, A = 1f },
                new ColorStop { Progress = 1.0f, R = 0.20f, G = 0.70f, B = 1.00f, A = 1f },
            }
        };

        private static ColorGradient MakeAccDefault() => new ColorGradient
        {
            HasPerfectColor = true,
            PR = 1f, PG = 0.855f, PB = 0f, PA = 1f,
            Stops = new List<ColorStop>
            {
                new ColorStop { Progress = 0.90f, R = 1.00f, G = 0.00f, B = 0.03f, A = 1f },
                new ColorStop { Progress = 0.95f, R = 1.00f, G = 0.40f, B = 0.15f, A = 1f },
                new ColorStop { Progress = 0.97f, R = 0.40f, G = 0.70f, B = 0.30f, A = 1f },
                new ColorStop { Progress = 0.99f, R = 0.40f, G = 0.70f, B = 1.00f, A = 1f },
                new ColorStop { Progress = 1.00f, R = 0.20f, G = 0.60f, B = 1.00f, A = 1f },
            }
        };

        private static ColorGradient MakeComboDefault() => new ColorGradient
        {
            Stops = new List<ColorStop>
            {
                new ColorStop { Progress = 0f,   R = 0.50f, G = 0.75f, B = 1.00f, A = 1f },
                new ColorStop { Progress = 1.0f, R = 0.20f, G = 0.60f, B = 1.00f, A = 1f },
            }
        };

        private static ColorGradient MakeBpmDefault() => new ColorGradient
        {
            Stops = new List<ColorStop>
            {
                new ColorStop { Progress = 0.15f, R = 1.00f, G = 1.00f, B = 1.00f, A = 1f },
                new ColorStop { Progress = 1.00f, R = 0.20f, G = 0.60f, B = 1.00f, A = 1f },
            }
        };

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
