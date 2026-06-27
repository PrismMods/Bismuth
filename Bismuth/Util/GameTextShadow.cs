using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    /* Shadow renderer for a game legacy uGUI Text. The game's own scripts hold typed
       references to the original Text and keep writing to it, so we can't replace the
       component — instead we leave it alive and layout-contributing but render it
       invisible (canvasRenderer alpha 0) and draw a child TextMeshProUGUI that mirrors
       its content with the Bismuth font. This is how Bismuth styles legacy game text
       without keeping a legacy Font: the original stays legacy, the visible glyphs are
       TMP.

       Lives on the original Text's GameObject, so it dies with it on scene unload.
       GameFontApplier (re)pushes the font/style/scale via Configure; everything else is
       mirrored live in LateUpdate (after the game's Update writes). */
    [DisallowMultipleComponent]
    internal class GameTextShadow : MonoBehaviour
    {
        internal const string ChildName = "BismuthShadow";

        // Managed by an external system (the level-name overlay) rather than GameFontApplier's
        // sweep — Restore() leaves owned shadows alone instead of detaching them.
        internal bool Owned;

        private Text _src;
        private TextMeshProUGUI _tmp;

        // Pushed by GameFontApplier each sweep.
        private TMP_FontAsset _font;
        private FontStyles _style;
        private float _scale = 1f;
        // None = mirror the original's wrap/size. Width = force one line and shrink to fit
        // the rect width (titles/credits). Box = keep wrapping but autosize the whole block
        // down to fit the rect — for portal credits that overflow their boxes / break mid-word.
        internal enum FitMode { None, Width, Box }
        private FitMode _fit;
        // Title/credits cap as a fraction of natural size; autosize shrinks further to fit.
        // Per-call (some credits want a gentler cap), defaults to the wrapped-credits value.
        internal const float DefaultFitShrink = 0.7f;
        private float _fitShrink = DefaultFitShrink;
        // Strip embedded <size=…> tags before mirroring — guest-track labels bake an absolute
        // size into their text that overrides (and fights) our scaling.
        private bool _stripSize;
        private static readonly Regex SizeTag = new Regex("</?size[^>]*>", RegexOptions.IgnoreCase);
        // Wrap role-LABEL lines in <b> while mirroring — guest-track text mixes a bold label
        // with a regular name in one component ("객원 레벨 디자인:\nRikri"), which component
        // weight can't split.
        private bool _boldLabelLines;
        // Extra line advance (TMP units) — guest-track label+name components read cramped at
        // default spacing.
        private float _lineSpacing;
        // Collapse newlines to spaces while mirroring — the level name keeps the speed-trial
        // multiplier ("…\n(1.1배)") inline on one line.
        private bool _collapseNewlines;
        private bool _configDirty;

        // Last-mirrored values — only write to TMP on change so it doesn't rebuild its
        // mesh every frame.
        private string _lastText;
        private Color _lastColor;
        private float _lastSize = float.NaN;
        private TextAlignmentOptions _lastAlign = (TextAlignmentOptions)(-1);
        private bool _lastWrap;
        private bool _lastRich;
        private bool _lastEnabled = true;

        // Vanilla uGUI drop shadow (hidden along with the original) → mirrored onto the
        // TMP via the SDF underlay. Re-fetched per sweep; AbsoluteRotationShadow animates
        // its offset, so we re-read it each frame (TmpShadow.Apply no-ops unless it changed).
        private Shadow _srcShadow;
        private TmpShadow _shadow;
        // When set directly via SetShadow (level-name overlay), the auto-mirror is bypassed.
        private bool _explicitShadow;

        internal static GameTextShadow Attach(Text src, bool owned = false)
        {
            var sh = src.GetComponent<GameTextShadow>();
            if (sh == null)
            {
                sh = src.gameObject.AddComponent<GameTextShadow>();
                sh._src = src;
                sh.Build();
            }
            sh.Owned = owned;
            return sh;
        }

        private void Build()
        {
            var go = new GameObject(ChildName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _tmp = go.AddComponent<TextMeshProUGUI>();
            _tmp.raycastTarget = false; // clicks fall through to the still-live original
            _tmp.overflowMode = TextOverflowModes.Overflow;
        }

        // Font/style/scale decisions from GameFontApplier (bold, element weight, …).
        // fitWidth = keep on one line and shrink to fit the rect (title/credits).
        internal void Configure(TMP_FontAsset font, FontStyles style, float scale, FitMode fit = FitMode.None,
            float fitShrink = DefaultFitShrink, bool stripSize = false, bool boldLabelLines = false,
            float lineSpacing = 0f, bool collapseNewlines = false)
        {
            _font = font;
            _style = style;
            _scale = scale;
            _fit = fit;
            _fitShrink = fitShrink;
            _stripSize = stripSize;
            _boldLabelLines = boldLabelLines;
            _lineSpacing = lineSpacing;
            _collapseNewlines = collapseNewlines;
            _configDirty = true;
        }

        private void LateUpdate()
        {
            if (_src == null || _tmp == null) return;

            // Re-hide the original every frame: cheap, and survives the game re-rendering
            // it (SetAlpha is a CanvasRenderer multiplier, separate from the .color the
            // game writes — which we still read below).
            var cr = _src.canvasRenderer;
            if (cr != null) cr.SetAlpha(0f);

            // Mirror the original's visibility, so disabling/hiding it hides the shadow too.
            bool enabled = _src.enabled;
            if (enabled != _lastEnabled) { _tmp.enabled = enabled; _lastEnabled = enabled; }
            if (!enabled) return;

            if (_configDirty)
            {
                _configDirty = false;
                if (_font != null && _tmp.font != _font) _tmp.font = _font;
                if (_tmp.fontStyle != _style) _tmp.fontStyle = _style;
                if (_tmp.lineSpacing != _lineSpacing) _tmp.lineSpacing = _lineSpacing;
                _srcShadow = _src.GetComponent<Shadow>(); // Outline derives from Shadow
                _lastSize = float.NaN; // re-derive size against the new scale
            }

            string text = _src.text;
            if (_stripSize && !string.IsNullOrEmpty(text) && text.IndexOf("<size", System.StringComparison.OrdinalIgnoreCase) >= 0)
                text = SizeTag.Replace(text, "");
            if (_collapseNewlines && !string.IsNullOrEmpty(text) && text.IndexOf('\n') >= 0)
                text = text.Replace('\n', ' ');
            if (_boldLabelLines) text = BoldLabelLines(text);
            if (text != _lastText) { _tmp.text = text; _lastText = text; }

            Color color = _src.color;
            if (color != _lastColor) { _tmp.color = color; _lastColor = color; }

            var align = AlignFrom(_src.alignment);
            if (align != _lastAlign) { _tmp.alignment = align; _lastAlign = align; }

            // Force rich text on when we inject <b> tags ourselves (these credits already use
            // rich markup, but don't depend on it).
            bool rich = _src.supportRichText || _boldLabelLines;
            if (rich != _lastRich) { _tmp.richText = rich; _lastRich = rich; }

            if (_fit != FitMode.None)
            {
                // Width: never wrap, autosize the single line down to the rect width — caps the
                // wrapped, oversized credits to one shrunk line. Box: keep wrapping but autosize
                // the whole block down to fit the rect, so portal credits stop overflowing their
                // boxes and breaking names mid-word (the font shrinks until each word fits).
                bool box = _fit == FitMode.Box;
                var wrap = box ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
                if (_tmp.textWrappingMode != wrap) _tmp.textWrappingMode = wrap;
                float natural = (_src.resizeTextForBestFit ? _src.resizeTextMaxSize : _src.fontSize) * _scale;
                float max = Mathf.Max(1f, natural * _fitShrink);
                // Box credits use tiny world-space fontSizes, so the shrink floor must be
                // RELATIVE or there's no room to shrink; one-line width text floors at 1.
                float min = box ? Mathf.Max(0.01f, max * 0.1f) : 1f;
                if (!_tmp.enableAutoSizing || max != _lastSize)
                {
                    _tmp.enableAutoSizing = true;
                    _tmp.fontSizeMin = min;
                    _tmp.fontSizeMax = max;
                    _lastSize = max;
                }
            }
            else
            {
                bool wrap = _src.horizontalOverflow == HorizontalWrapMode.Wrap;
                if (wrap != _lastWrap)
                {
                    _tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
                    _lastWrap = wrap;
                }

                // Best-fit originals auto-size between bounds (TMP's analog); others use fontSize.
                if (_src.resizeTextForBestFit)
                {
                    float max = Mathf.Max(1f, _src.resizeTextMaxSize * _scale);
                    if (!_tmp.enableAutoSizing || max != _lastSize)
                    {
                        _tmp.enableAutoSizing = true;
                        _tmp.fontSizeMin = Mathf.Max(1f, _src.resizeTextMinSize * _scale);
                        _tmp.fontSizeMax = max;
                        _lastSize = max;
                    }
                }
                else
                {
                    float size = _src.fontSize * _scale;
                    if (size != _lastSize)
                    {
                        if (_tmp.enableAutoSizing) _tmp.enableAutoSizing = false;
                        _tmp.fontSize = size;
                        _lastSize = size;
                    }
                }
            }

            UpdateShadow();
        }

        // Mirror the original's uGUI Shadow onto the TMP's SDF underlay. Created lazily,
        // only for text the game actually shadows, so unshadowed text stays batchable.
        // Set the drop shadow directly instead of mirroring the original's uGUI Shadow. Used by
        // the level-name overlay, which gives the level name its own Bismuth drop shadow.
        internal void SetShadow(bool enabled, Color color, Vector2 offsetPx)
        {
            _explicitShadow = true;
            if (_tmp == null) return;
            if (_shadow == null) _shadow = _tmp.gameObject.AddComponent<TmpShadow>();
            _shadow.Enabled = enabled;
            _shadow.Color = color;
            _shadow.OffsetPx = offsetPx;
            _shadow.Apply();
        }

        private void UpdateShadow()
        {
            if (_explicitShadow) return; // managed by SetShadow
            if (_srcShadow == null)
            {
                if (_shadow != null && _shadow.Enabled) { _shadow.Enabled = false; _shadow.Apply(); }
                return;
            }
            if (_shadow == null) _shadow = _tmp.gameObject.AddComponent<TmpShadow>();
            _shadow.Enabled = _srcShadow.isActiveAndEnabled;
            _shadow.Color = _srcShadow.effectColor;
            _shadow.OffsetPx = _srcShadow.effectDistance;
            _shadow.Apply(); // internally guarded — regenerates only on actual change
        }

        // Toggle-off / restore: un-hide the original and drop the TMP child.
        internal void Detach()
        {
            if (_src != null && _src.canvasRenderer != null) _src.canvasRenderer.SetAlpha(1f);
            if (_tmp != null) Destroy(_tmp.gameObject);
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (_src != null && _src.canvasRenderer != null) _src.canvasRenderer.SetAlpha(1f);
        }

        // Wrap each role-label line in <b>, leaving artist-name lines regular. A label line
        // ends with ":" (Korean: "객원 작곡가:") or " by" (English: "Guest track by"); names
        // don't. Handles components that hold a label AND a name across a newline.
        private static string BoldLabelLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf('\n') < 0)
                return IsLabelLine(s) ? "<b>" + s + "</b>" : s;
            var lines = s.Split('\n');
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                if (IsLabelLine(lines[i])) sb.Append("<b>").Append(lines[i]).Append("</b>");
                else sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        private static bool IsLabelLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            var t = line.TrimEnd();
            return t.EndsWith(":") ||
                   t.EndsWith(" by", System.StringComparison.OrdinalIgnoreCase);
        }

        internal static TextAlignmentOptions AlignFrom(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft:    return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter:  return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight:   return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft:   return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight:  return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft:    return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter:  return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight:   return TextAlignmentOptions.BottomRight;
                default:                      return TextAlignmentOptions.Left;
            }
        }
    }
}
