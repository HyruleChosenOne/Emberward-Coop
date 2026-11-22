using System.Collections.Generic;
using UnityEngine;

namespace EmberwardCoop.Presence
{
    public enum StickerId : byte
    {
        ThumbsUp = 0,
        Heart = 1,
        Sweat = 2,
        MindBlown = 3,
        Cry = 4,
        Crown = 5,
        Coin = 6,
        Fire = 7,
        Clock = 8,
        Alert = 9
    }

    public static class StickerArtGenerator
    {
        private const int Size = 128;
        private static readonly Dictionary<StickerId, Sprite> _cache = new Dictionary<StickerId, Sprite>();

        public static Sprite GetSprite(StickerId id)
        {
            if (_cache.TryGetValue(id, out var existing) && existing != null)
                return existing;

            var spr = BuildSprite(id);
            _cache[id] = spr;
            return spr;
        }

        public static Color GetAccentColor(StickerId id) => id switch
        {
            StickerId.ThumbsUp  => new Color(0.32f, 0.92f, 0.48f, 1f),
            StickerId.Heart     => new Color(1.00f, 0.36f, 0.56f, 1f),
            StickerId.Sweat     => new Color(0.36f, 0.80f, 1.00f, 1f),
            StickerId.MindBlown => new Color(0.84f, 0.48f, 1.00f, 1f),
            StickerId.Cry       => new Color(0.45f, 0.68f, 1.00f, 1f),
            StickerId.Crown     => new Color(1.00f, 0.84f, 0.24f, 1f),
            StickerId.Coin      => new Color(1.00f, 0.78f, 0.18f, 1f),
            StickerId.Fire      => new Color(1.00f, 0.44f, 0.14f, 1f),
            StickerId.Clock     => new Color(0.72f, 0.86f, 0.98f, 1f),
            StickerId.Alert     => new Color(1.00f, 0.26f, 0.24f, 1f),
            _                   => Color.white
        };

        private static Sprite BuildSprite(StickerId id)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color accent = GetAccentColor(id);
            Color darkBg = new Color(0.11f, 0.09f, 0.15f, 0.96f);
            Color rimCol = Color.Lerp(accent, new Color(1f, 0.86f, 0.48f, 1f), 0.35f);

            var pixels = new Color[Size * Size];
            float cx = (Size - 1) * 0.5f;
            float cy = (Size - 1) * 0.5f;
            float outerR = 59f;
            float rimInner = 52f;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c = Color.clear;

                    if (d <= outerR + 1.5f)
                    {
                        float aa = Mathf.Clamp01(outerR + 1.0f - d);
                        if (d >= rimInner)
                        {
                            float highlight = Mathf.Clamp01(0.65f + 0.35f * (dy / outerR));
                            c = Color.Lerp(rimCol * 0.75f, rimCol * 1.15f, highlight);
                            c.a = aa;
                        }
                        else
                        {
                            float glow = Mathf.Clamp01(1f - d / rimInner);
                            c = Color.Lerp(darkBg, new Color(accent.r * 0.42f, accent.g * 0.42f, accent.b * 0.42f, 0.98f), glow * glow * 0.75f);
                            c.a = 0.96f;
                        }
                    }
                    pixels[y * Size + x] = c;
                }
            }

            switch (id)
            {
                case StickerId.ThumbsUp:
                    DrawRoundedBox(pixels, 38, 36, 54, 68, 4f, new Color(0.28f, 0.60f, 0.92f, 1f)); // cuff
                    DrawRoundedBox(pixels, 54, 38, 92, 68, 10f, new Color(1.0f, 0.84f, 0.56f, 1f)); // palm/fingers
                    DrawCapsule(pixels, 63, 66, 70, 92, 9f, new Color(1.0f, 0.84f, 0.56f, 1f));    // thumb
                    DrawCircle(pixels, 95, 86, 5f, accent);
                    DrawCircle(pixels, 42, 88, 3.5f, new Color(1f, 0.95f, 0.65f, 1f));
                    break;

                case StickerId.Heart:
                    DrawHeart(pixels, cx, cy - 3f, 31f, accent, new Color(1f, 0.72f, 0.82f, 1f));
                    break;

                case StickerId.Sweat:
                    DrawCircle(pixels, cx, cy, 34f, new Color(1.0f, 0.82f, 0.34f, 1f));
                    DrawCircle(pixels, cx - 12f, cy + 7f, 5f, new Color(0.14f, 0.11f, 0.18f, 1f));
                    DrawCircle(pixels, cx + 12f, cy + 7f, 5f, new Color(0.14f, 0.11f, 0.18f, 1f));
                    DrawCapsule(pixels, cx - 13f, cy - 12f, cx + 13f, cy - 12f, 4.5f, new Color(0.18f, 0.12f, 0.20f, 1f));
                    DrawTeardrop(pixels, cx + 27f, cy + 16f, 12f, new Color(0.32f, 0.84f, 1f, 1f));
                    break;

                case StickerId.MindBlown:
                    DrawCircle(pixels, cx, cy - 10f, 25f, new Color(1.0f, 0.82f, 0.34f, 1f));
                    DrawCircle(pixels, cx - 9f, cy - 6f, 4.2f, new Color(0.14f, 0.11f, 0.18f, 1f));
                    DrawCircle(pixels, cx + 9f, cy - 6f, 4.2f, new Color(0.14f, 0.11f, 0.18f, 1f));
                    DrawCircle(pixels, cx, cy - 19f, 5.5f, new Color(0.20f, 0.10f, 0.18f, 1f));
                    DrawCircle(pixels, cx, cy + 20f, 17f, accent);
                    DrawCircle(pixels, cx - 18f, cy + 16f, 12f, new Color(1f, 0.56f, 0.26f, 1f));
                    DrawCircle(pixels, cx + 18f, cy + 16f, 12f, new Color(1f, 0.82f, 0.32f, 1f));
                    DrawCircle(pixels, cx, cy + 28f, 11f, new Color(1f, 0.94f, 0.65f, 1f));
                    break;

                case StickerId.Cry:
                    DrawCircle(pixels, cx, cy, 34f, new Color(0.95f, 0.78f, 0.36f, 1f));
                    DrawRoundedBox(pixels, cx - 19f, cy - 30f, cx - 9f, cy + 6f, 4f, new Color(0.36f, 0.78f, 1f, 0.92f));
                    DrawRoundedBox(pixels, cx + 9f, cy - 30f, cx + 19f, cy + 6f, 4f, new Color(0.36f, 0.78f, 1f, 0.92f));
                    DrawCapsule(pixels, cx - 19f, cy + 8f, cx - 8f, cy + 4f, 3f, new Color(0.14f, 0.11f, 0.18f, 1f));
                    DrawCapsule(pixels, cx + 8f, cy + 4f, cx + 19f, cy + 8f, 3f, new Color(0.14f, 0.11f, 0.18f, 1f));
                    DrawCircle(pixels, cx, cy - 14f, 7f, new Color(0.20f, 0.12f, 0.18f, 1f));
                    break;

                case StickerId.Crown:
                    DrawTriangle(pixels, new Vector2(32, 44), new Vector2(40, 86), new Vector2(62, 54), accent);
                    DrawTriangle(pixels, new Vector2(46, 44), new Vector2(64, 94), new Vector2(82, 44), new Color(1f, 0.90f, 0.38f, 1f));
                    DrawTriangle(pixels, new Vector2(66, 54), new Vector2(88, 86), new Vector2(96, 44), accent);
                    DrawRoundedBox(pixels, 32, 36, 96, 52, 5f, new Color(0.92f, 0.64f, 0.14f, 1f));
                    DrawCircle(pixels, 64, 44, 5.5f, new Color(1f, 0.24f, 0.34f, 1f));
                    DrawCircle(pixels, 45, 44, 4.0f, new Color(0.26f, 0.86f, 1f, 1f));
                    DrawCircle(pixels, 83, 44, 4.0f, new Color(0.36f, 0.95f, 0.48f, 1f));
                    break;

                case StickerId.Coin:
                    DrawCircle(pixels, cx, cy, 35f, new Color(0.92f, 0.64f, 0.12f, 1f));
                    DrawCircle(pixels, cx, cy, 29f, new Color(1.00f, 0.84f, 0.28f, 1f));
                    DrawCircle(pixels, cx, cy, 22f, new Color(0.88f, 0.58f, 0.10f, 1f));
                    DrawTriangle(pixels, new Vector2(cx, cy + 16), new Vector2(cx - 12, cy), new Vector2(cx + 12, cy), new Color(1f, 0.94f, 0.56f, 1f));
                    DrawTriangle(pixels, new Vector2(cx, cy - 16), new Vector2(cx - 12, cy), new Vector2(cx + 12, cy), new Color(1f, 0.82f, 0.28f, 1f));
                    break;

                case StickerId.Fire:
                    DrawCircle(pixels, cx, cy - 10f, 28f, new Color(0.98f, 0.34f, 0.10f, 1f));
                    DrawTriangle(pixels, new Vector2(cx - 26, cy - 4), new Vector2(cx, cy + 40), new Vector2(cx + 26, cy - 4), new Color(1.0f, 0.46f, 0.14f, 1f));
                    DrawCircle(pixels, cx, cy - 13f, 18f, new Color(1.0f, 0.74f, 0.20f, 1f));
                    DrawTriangle(pixels, new Vector2(cx - 15, cy - 10), new Vector2(cx, cy + 22), new Vector2(cx + 15, cy - 10), new Color(1.0f, 0.92f, 0.48f, 1f));
                    break;

                case StickerId.Clock:
                    DrawCircle(pixels, cx, cy, 35f, new Color(0.75f, 0.84f, 0.94f, 1f));
                    DrawCircle(pixels, cx, cy, 29f, new Color(0.14f, 0.16f, 0.24f, 1f));
                    DrawCapsule(pixels, cx, cy, cx, cy + 20f, 3.8f, new Color(1f, 0.84f, 0.32f, 1f));
                    DrawCapsule(pixels, cx, cy, cx + 15f, cy - 6f, 3.4f, new Color(0.96f, 0.95f, 0.98f, 1f));
                    DrawCircle(pixels, cx, cy, 5f, new Color(1f, 0.52f, 0.20f, 1f));
                    break;

                case StickerId.Alert:
                    DrawTriangle(pixels, new Vector2(cx, cy + 38), new Vector2(cx - 38, cy - 28), new Vector2(cx + 38, cy - 28), accent);
                    DrawTriangle(pixels, new Vector2(cx, cy + 26), new Vector2(cx - 28, cy - 22), new Vector2(cx + 28, cy - 22), new Color(0.16f, 0.08f, 0.10f, 1f));
                    DrawCapsule(pixels, cx, cy + 12f, cx, cy - 5f, 4.8f, new Color(1f, 0.90f, 0.34f, 1f));
                    DrawCircle(pixels, cx, cy - 15f, 4.8f, new Color(1f, 0.90f, 0.34f, 1f));
                    break;
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void BlendPixel(Color[] pixels, int x, int y, Color src, float alphaScale = 1f)
        {
            if (x < 0 || x >= Size || y < 0 || y >= Size) return;
            float a = Mathf.Clamp01(src.a * alphaScale);
            if (a <= 0.001f) return;
            int idx = y * Size + x;
            Color dst = pixels[idx];
            float outA = a + dst.a * (1f - a);
            if (outA <= 0.001f) return;
            pixels[idx] = new Color(
                (src.r * a + dst.r * dst.a * (1f - a)) / outA,
                (src.g * a + dst.g * dst.a * (1f - a)) / outA,
                (src.b * a + dst.b * dst.a * (1f - a)) / outA,
                outA);
        }

        private static void DrawCircle(Color[] px, float cx, float cy, float r, Color col)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - 2));
            int x1 = Mathf.Min(Size - 1, Mathf.CeilToInt(cx + r + 2));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - 2));
            int y1 = Mathf.Min(Size - 1, Mathf.CeilToInt(cy + r + 2));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float aa = Mathf.Clamp01(r + 0.8f - d);
                    if (aa > 0f) BlendPixel(px, x, y, col, aa);
                }
        }

        private static void DrawRoundedBox(Color[] px, float minX, float minY, float maxX, float maxY, float radius, Color col)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX - 2));
            int x1 = Mathf.Min(Size - 1, Mathf.CeilToInt(maxX + 2));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(minY - 2));
            int y1 = Mathf.Min(Size - 1, Mathf.CeilToInt(maxY + 2));
            float hx = (maxX - minX) * 0.5f - radius;
            float hy = (maxY - minY) * 0.5f - radius;
            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f;

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float qx = Mathf.Max(Mathf.Abs(x - cx) - hx, 0f);
                    float qy = Mathf.Max(Mathf.Abs(y - cy) - hy, 0f);
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - radius;
                    float aa = Mathf.Clamp01(0.8f - d);
                    if (aa > 0f) BlendPixel(px, x, y, col, aa);
                }
        }

        private static void DrawCapsule(Color[] px, float x1, float y1, float x2, float y2, float r, Color col)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x1, x2) - r - 2));
            int maxX = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(x1, x2) + r + 2));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y1, y2) - r - 2));
            int maxY = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(y1, y2) + r + 2));
            Vector2 a = new Vector2(x1, y1);
            Vector2 ba = new Vector2(x2 - x1, y2 - y1);
            float baDot = Vector2.Dot(ba, ba);

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 pa = new Vector2(x, y) - a;
                    float h = baDot > 0.0001f ? Mathf.Clamp01(Vector2.Dot(pa, ba) / baDot) : 0f;
                    float d = (pa - ba * h).magnitude - r;
                    float aa = Mathf.Clamp01(0.8f - d);
                    if (aa > 0f) BlendPixel(px, x, y, col, aa);
                }
        }

        private static void DrawTriangle(Color[] px, Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - 2));
            int maxX = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + 2));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - 2));
            int maxY = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) + 2));

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x, y);
                    if (PointInTriangle(p, a, b, c))
                        BlendPixel(px, x, y, col, 1f);
                }
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = CrossSign(p, a, b);
            float d2 = CrossSign(p, b, c);
            float d3 = CrossSign(p, c, a);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        private static float CrossSign(Vector2 p1, Vector2 p2, Vector2 p3)
            => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        private static void DrawHeart(Color[] px, float cx, float cy, float scale, Color col, Color sheen)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float nx = (x - cx) / scale;
                    float ny = (y - cy) / scale;
                    float eq = Mathf.Pow(nx * nx + ny * ny - 1f, 3f) - nx * nx * ny * ny * ny;
                    if (eq <= 0.06f)
                    {
                        float aa = Mathf.Clamp01((0.06f - eq) * 14f);
                        bool highlight = nx < -0.15f && ny > 0.25f && eq < -0.15f;
                        BlendPixel(px, x, y, highlight ? Color.Lerp(col, sheen, 0.45f) : col, aa);
                    }
                }
        }

        private static void DrawTeardrop(Color[] px, float cx, float cy, float r, Color col)
        {
            DrawCircle(px, cx, cy - r * 0.35f, r * 0.65f, col);
            DrawTriangle(px, new Vector2(cx, cy + r), new Vector2(cx - r * 0.62f, cy - r * 0.22f), new Vector2(cx + r * 0.62f, cy - r * 0.22f), col);
        }
    }
}
