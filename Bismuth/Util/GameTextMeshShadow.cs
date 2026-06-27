using TMPro;
using UnityEngine;

namespace Bismuth
{
    /* Shadow for a game 3D TextMesh (results / congrats / world-space labels). Same idea
       as GameTextShadow but world-space: the game keeps writing to the original, so we
       leave it alive, stop it drawing (MeshRenderer.forceRenderingOff — which, unlike
       disabling the renderer, keeps its bounds updating), and parent a 3D TextMeshPro
       under it that mirrors its content.

       Size is matched by scaling the child so its rendered world height equals the
       TextMesh's, so we don't need to know Unity's TextMesh→world metric. Because the
       child is parented under the original, it also inherits the original's position /
       rotation / scale animation for free. */
    [DisallowMultipleComponent]
    internal class GameTextMeshShadow : MonoBehaviour
    {
        private const string ChildName = GameTextShadow.ChildName; // "BismuthShadow"
        private const float RefFontSize = 36f; // TMP measured at this, then scaled to fit

        private TextMesh _src;
        private MeshRenderer _srcRenderer;
        private TextMeshPro _tmp;

        private TMP_FontAsset _font;
        private FontStyles _style;
        private float _scale = 1f;
        private bool _configDirty;

        private string _lastText;
        private Color _lastColor;
        private TextAlignmentOptions _lastAlign = (TextAlignmentOptions)(-1);
        private bool _needFit = true;
        private float _fitCharSize = float.NaN;

        internal static GameTextMeshShadow Attach(TextMesh src)
        {
            var sh = src.GetComponent<GameTextMeshShadow>();
            if (sh == null)
            {
                var r = src.GetComponent<MeshRenderer>();
                if (r == null) return null;
                sh = src.gameObject.AddComponent<GameTextMeshShadow>();
                sh._src = src;
                sh._srcRenderer = r;
                sh.Build();
            }
            return sh;
        }

        private void Build()
        {
            var go = new GameObject(ChildName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.localPosition = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            rt.sizeDelta = Vector2.zero;            // unbounded; alignment positions the text
            rt.pivot = new Vector2(0.5f, 0.5f);
            _tmp = go.AddComponent<TextMeshPro>();
            _tmp.textWrappingMode = TextWrappingModes.NoWrap;
            _tmp.overflowMode = TextOverflowModes.Overflow;
            _tmp.enableAutoSizing = false;
            _tmp.fontSize = RefFontSize;
            // Draw just in front of the (hidden) original so it wins any z-tie.
            var mr = _tmp.GetComponent<MeshRenderer>();
            if (mr != null && _srcRenderer != null)
            {
                mr.sortingLayerID = _srcRenderer.sortingLayerID;
                mr.sortingOrder = _srcRenderer.sortingOrder;
            }
        }

        internal void Configure(TMP_FontAsset font, FontStyles style, float scale)
        {
            _font = font;
            _style = style;
            _scale = scale;
            _configDirty = true;
        }

        private void LateUpdate()
        {
            if (_src == null || _tmp == null) return;

            // Re-hide the original every frame; forceRenderingOff keeps bounds live.
            if (_srcRenderer != null && !_srcRenderer.forceRenderingOff)
                _srcRenderer.forceRenderingOff = true;

            // TextMesh has no .enabled (it's a Component, not a Behaviour); the game shows/
            // hides it via the MeshRenderer, which we leave enabled and only forceRenderingOff.
            bool on = _srcRenderer != null && _srcRenderer.enabled;
            if (_tmp.enabled != on) _tmp.enabled = on;
            if (!on) return;

            if (_configDirty)
            {
                _configDirty = false;
                if (_font != null && _tmp.font != _font) _tmp.font = _font;
                if (_tmp.fontStyle != _style) _tmp.fontStyle = _style;
                _needFit = true;
            }

            string text = _src.text;
            if (text != _lastText) { _tmp.text = text; _lastText = text; _needFit = true; }

            Color color = _src.color;
            if (color != _lastColor) { _tmp.color = color; _lastColor = color; }

            var align = GameTextShadow.AlignFrom(_src.anchor);
            if (align != _lastAlign) { _tmp.alignment = align; _lastAlign = align; }

            // Re-fit the child scale so the TMP's world height matches the TextMesh's,
            // whenever the text or its size driver (characterSize) changes. The fit ratio
            // is parent-scale-independent, so the inherited animation still applies on top.
            if (_needFit || _src.characterSize != _fitCharSize) Fit();
        }

        private void Fit()
        {
            _needFit = false;
            _fitCharSize = _src.characterSize;
            var child = _tmp.transform;
            child.localScale = Vector3.one;
            _tmp.ForceMeshUpdate();
            float tmpH = _tmp.GetComponent<Renderer>().bounds.size.y;
            float srcH = _srcRenderer.bounds.size.y;
            if (tmpH > 1e-5f && srcH > 1e-5f)
            {
                float s = srcH / tmpH * _scale;
                child.localScale = new Vector3(s, s, s);
            }
        }

        internal void Detach()
        {
            if (_srcRenderer != null) _srcRenderer.forceRenderingOff = false;
            if (_tmp != null) Destroy(_tmp.gameObject);
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (_srcRenderer != null) _srcRenderer.forceRenderingOff = false;
        }
    }
}
