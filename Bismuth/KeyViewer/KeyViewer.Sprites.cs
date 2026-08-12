using System.Collections.Generic;
using UnityEngine;

namespace Bismuth
{
    internal partial class KeyViewer
    {
        // Falloff across a halo's blur band. t = 1 at the column edge, 0 at the outer edge.
        // Glow decays quadratically: brighter against the column, longer faint tail.
        private static float HaloFalloff(float t, bool glow)
        {
            t = Mathf.Clamp01(t);
            return glow ? t * t : t * t * (3f - 2f * t);
        }

        // Signed distance to a rounded rect; <= 0 inside.
        private static float RoundRectDistance(float px, float py,
            float x0, float y0, float x1, float y1, float r)
        {
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            float qx = Mathf.Abs(px - cx) - ((x1 - x0) * 0.5f - r);
            float qy = Mathf.Abs(py - cy) - ((y1 - y0) * 0.5f - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        // Halo (drop shadow / glow) behind a rain column, 9-sliced so both the corner arcs
        // and the blur band keep their pixel size at any column height. An end is "soft" when
        // it is the column's real end; where two segments meet, the shared end stays hard.
        private Sprite GetHaloBodySprite(int size, int radius, bool softBottom, bool softTop, bool glow)
        {
            int key = (Mathf.Clamp(size, 0, 255) << 16) | (Mathf.Clamp(radius, 0, 255) << 8)
                    | (softBottom ? 4 : 0) | (softTop ? 2 : 0) | (glow ? 1 : 0);
            if (_haloBodySprites.TryGetValue(key, out var cached)) return cached;

            int blur = Mathf.Max(1, size);
            int r = Mathf.Max(0, radius);
            int pad = blur + r;
            const int center = 4;
            int w = pad * 2 + center;
            int h = (softBottom ? pad : 0) + (softTop ? pad : 0) + center;

            // Solid core = the rain column itself: inset by blur, corners radius r. An end
            // that butts against another segment pushes its arc off-texture instead, so the
            // seam stays a hard cut with no notch.
            float x0 = blur, x1 = w - blur;
            float y0 = softBottom ? blur : -r;
            float y1 = softTop ? h - blur : h + r;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = RoundRectDistance(x + 0.5f, y + 0.5f, x0, y0, x1, y1, r);
                    float a = HaloFalloff(1f - Mathf.Max(d, 0f) / blur, glow);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _allTextures.Add(tex);

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0u,
                SpriteMeshType.FullRect,
                new Vector4(pad, softBottom ? pad : 0, pad, softTop ? pad : 0));
            _haloBodySprites[key] = sprite;
            _allSprites.Add(sprite);
            return sprite;
        }

        // Rain column segment, 9-sliced so the corner arcs keep their pixel size at any
        // height. Each end rounds only when it is the column's real end — rounding a seam
        // between the body and the tip would notch the silhouette.
        private Sprite GetRainSprite(int radius, bool roundBottom, bool roundTop)
        {
            int key = (Mathf.Clamp(radius, 0, 0x3FFF) << 2) | (roundBottom ? 2 : 0) | (roundTop ? 1 : 0);
            if (_rainSprites.TryGetValue(key, out var cached)) return cached;

            int r = Mathf.Max(1, radius);
            const int center = 2;
            int w = r * 2 + center;
            int h = (roundBottom ? r : 0) + (roundTop ? r : 0) + center;
            float y0 = roundBottom ? 0f : -r;
            float y1 = roundTop ? h : h + r;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = RoundRectDistance(x + 0.5f, y + 0.5f, 0f, y0, w, y1, r);
                    float a = Mathf.Clamp01(0.5f - d);   // half-pixel ramp = cheap AA
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _allTextures.Add(tex);

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0u,
                SpriteMeshType.FullRect,
                new Vector4(r, roundBottom ? r : 0, r, roundTop ? r : 0));
            _rainSprites[key] = sprite;
            _allSprites.Add(sprite);
            return sprite;
        }


    }
}
