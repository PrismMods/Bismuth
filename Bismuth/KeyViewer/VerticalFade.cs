using UnityEngine;
using UnityEngine.UI;

namespace Bismuth
{
    /* Linear bottom-to-top alpha ramp across a Graphic's own rect. The rain tip used to get
       its fade from a gradient texture sampled with RawImage.uvRect, but a rounded tip needs
       a 9-sliced sprite, and Image has no uvRect. The ramp is exact either way: the gradient
       was linear, and a sliced mesh's vertex rows interpolate a linear function exactly. */
    internal class VerticalFade : BaseMeshEffect
    {
        private float _bottom = 1f, _top = 1f;

        public void Set(float bottomAlpha, float topAlpha)
        {
            if (Mathf.Approximately(_bottom, bottomAlpha) && Mathf.Approximately(_top, topAlpha)) return;
            _bottom = bottomAlpha;
            _top    = topAlpha;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var rect = graphic.rectTransform.rect;
            if (rect.height <= 0f) return;

            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = Mathf.Clamp01((v.position.y - rect.yMin) / rect.height);
                var c = v.color;
                c.a = (byte)(c.a * Mathf.Clamp01(Mathf.Lerp(_bottom, _top, t)));
                v.color = c;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
