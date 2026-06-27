using System.Collections.Generic;
using UnityEngine;

namespace Bismuth
{
    internal partial class KeyViewer
    {
        private Texture2D GetGradientTex()
        {
            if (_gradTex != null) return _gradTex;
            _gradTex = new Texture2D(1, 2, TextureFormat.RGBA32, false);
            _gradTex.filterMode = FilterMode.Bilinear;
            _gradTex.wrapMode   = TextureWrapMode.Clamp;
            _gradTex.SetPixel(0, 0, Color.white);
            _gradTex.SetPixel(0, 1, new Color(1f, 1f, 1f, 0f));
            _gradTex.Apply(false, false);
            return _gradTex;
        }

        private static float HorizBlurAlpha(int x, int w, int blur)
        {
            if (x < blur)
            {
                float t = Mathf.Clamp01((x + 0.5f) / blur);
                return t * t * (3f - 2f * t);
            }
            if (x >= w - blur)
            {
                float t = Mathf.Clamp01((w - x - 0.5f) / blur);
                return t * t * (3f - 2f * t);
            }
            return 1f;
        }

        // Sharp top — used when there's a rain tip above the body. Body meets tip seamlessly at bodyTop.
        private Sprite GetShadowBodySprite(int shadowSize)
            => BakeShadowBodySprite(shadowSize, softTop: false, _shadowBodySprites);

        // Soft top — used when the body's top IS the rain's top (no tip above), so the shadow gets
        // a soft fade above the rain instead of a hard edge.
        private Sprite GetShadowBodySpriteSoftTop(int shadowSize)
            => BakeShadowBodySprite(shadowSize, softTop: true, _shadowBodySpritesSoftTop);

        private Sprite BakeShadowBodySprite(int shadowSize, bool softTop, Dictionary<int, Sprite> cache)
        {
            if (cache.TryGetValue(shadowSize, out var cached)) return cached;
            int blur = Mathf.Max(1, shadowSize);
            int center = 4;
            int w = blur * 2 + center;
            int h = (softTop ? blur * 2 : blur) + center;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float vy;
                if (y < blur)
                {
                    float t = Mathf.Clamp01((y + 0.5f) / blur);
                    vy = t * t * (3f - 2f * t);
                }
                else if (softTop && y >= h - blur)
                {
                    float t = Mathf.Clamp01((h - y - 0.5f) / blur);
                    vy = t * t * (3f - 2f * t);
                }
                else vy = 1f;

                for (int x = 0; x < w; x++)
                {
                    float a = HorizBlurAlpha(x, w, blur) * vy;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _allTextures.Add(tex);

            int topBorder = softTop ? blur : 0;
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0u,
                SpriteMeshType.FullRect, new Vector4(blur, blur, blur, topBorder));
            cache[shadowSize] = sprite;
            _allSprites.Add(sprite);
            return sprite;
        }

        // Tip texture stretches 1:1 with the shadow tip rect (RawImage has no 9-slice), so
        // to keep the side blur fixed at `shadowSize` px regardless of column width the
        // texture width must match the rect width. Caches by (shadowSize, rainWidth).
        private Texture2D GetShadowTipTex(int shadowSize, int rainWidth)
        {
            int key = (shadowSize << 16) | (Mathf.Clamp(rainWidth, 1, 0xFFFF));
            if (_shadowTipTextures.TryGetValue(key, out var cached)) return cached;
            int blur = Mathf.Max(1, shadowSize);
            int center = Mathf.Max(1, rainWidth);
            int w = blur * 2 + center;
            var tex = new Texture2D(w, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            var px = new Color32[w * 2];
            for (int x = 0; x < w; x++)
            {
                byte aa = (byte)(HorizBlurAlpha(x, w, blur) * 255f);
                px[x]       = new Color32(255, 255, 255, aa); // bottom row: blur, opaque
                px[x + w]   = new Color32(255, 255, 255, 0);  // top row: transparent
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _allTextures.Add(tex);
            _shadowTipTextures[key] = tex;
            return tex;
        }

    }
}
