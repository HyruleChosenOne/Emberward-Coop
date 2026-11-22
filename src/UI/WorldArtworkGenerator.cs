using System.Collections.Generic;
using UnityEngine;

namespace EmberwardCoop.UI
{
    public static class WorldArtworkGenerator
    {
        private const int W = 256;
        private const int H = 112;
        private static readonly Dictionary<eWorldType, Sprite> _cache = new Dictionary<eWorldType, Sprite>();

        public static Sprite GetBanner(eWorldType world)
        {
            if (_cache.TryGetValue(world, out var spr) && spr != null)
                return spr;

            spr = BuildBanner(world);
            _cache[world] = spr;
            return spr;
        }

        private static Sprite BuildBanner(eWorldType world)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color skyTop, skyBot, farCol, midCol, foreCol, glowCol;
            switch (world)
            {
                case eWorldType.WORLD_2_DESERT:
                    skyTop  = new Color(0.32f, 0.14f, 0.10f, 1f);
                    skyBot  = new Color(0.86f, 0.48f, 0.18f, 1f);
                    farCol  = new Color(0.56f, 0.30f, 0.14f, 1f);
                    midCol  = new Color(0.38f, 0.20f, 0.10f, 1f);
                    foreCol = new Color(0.18f, 0.10f, 0.08f, 1f);
                    glowCol = new Color(1.00f, 0.88f, 0.42f, 1f);
                    break;

                case eWorldType.WORLD_3_MOUNTAIN:
                    skyTop  = new Color(0.08f, 0.14f, 0.28f, 1f);
                    skyBot  = new Color(0.24f, 0.56f, 0.72f, 1f);
                    farCol  = new Color(0.40f, 0.62f, 0.78f, 1f);
                    midCol  = new Color(0.18f, 0.32f, 0.48f, 1f);
                    foreCol = new Color(0.09f, 0.15f, 0.24f, 1f);
                    glowCol = new Color(0.58f, 0.96f, 0.88f, 1f);
                    break;

                case eWorldType.WORLD_4_DEPTH:
                    skyTop  = new Color(0.08f, 0.05f, 0.16f, 1f);
                    skyBot  = new Color(0.24f, 0.12f, 0.42f, 1f);
                    farCol  = new Color(0.20f, 0.12f, 0.34f, 1f);
                    midCol  = new Color(0.13f, 0.08f, 0.24f, 1f);
                    foreCol = new Color(0.07f, 0.04f, 0.12f, 1f);
                    glowCol = new Color(0.45f, 0.94f, 0.88f, 1f);
                    break;

                case eWorldType.WORLD_5_COSMO:
                    skyTop  = new Color(0.06f, 0.04f, 0.18f, 1f);
                    skyBot  = new Color(0.44f, 0.14f, 0.46f, 1f);
                    farCol  = new Color(0.32f, 0.14f, 0.44f, 1f);
                    midCol  = new Color(0.18f, 0.08f, 0.30f, 1f);
                    foreCol = new Color(0.09f, 0.05f, 0.16f, 1f);
                    glowCol = new Color(1.00f, 0.72f, 0.98f, 1f);
                    break;

                case eWorldType.WORLD_100_CHAOS_REALM:
                    skyTop  = new Color(0.16f, 0.03f, 0.08f, 1f);
                    skyBot  = new Color(0.68f, 0.14f, 0.18f, 1f);
                    farCol  = new Color(0.44f, 0.10f, 0.18f, 1f);
                    midCol  = new Color(0.24f, 0.06f, 0.14f, 1f);
                    foreCol = new Color(0.10f, 0.04f, 0.08f, 1f);
                    glowCol = new Color(1.00f, 0.56f, 0.18f, 1f);
                    break;

                default: // WORLD_1_FOREST
                    skyTop  = new Color(0.07f, 0.16f, 0.18f, 1f);
                    skyBot  = new Color(0.22f, 0.48f, 0.34f, 1f);
                    farCol  = new Color(0.16f, 0.38f, 0.28f, 1f);
                    midCol  = new Color(0.10f, 0.25f, 0.20f, 1f);
                    foreCol = new Color(0.06f, 0.14f, 0.12f, 1f);
                    glowCol = new Color(1.00f, 0.84f, 0.34f, 1f);
                    break;
            }

            var px = new Color[W * H];
            float sunX = world == eWorldType.WORLD_2_DESERT ? 185f : 196f;
            float sunY = 76f;

            for (int y = 0; y < H; y++)
            {
                float ny = y / (float)(H - 1);
                for (int x = 0; x < W; x++)
                {
                    float nx = x / (float)(W - 1);
                    Color c = Color.Lerp(skyBot, skyTop, Mathf.Pow(ny, 0.85f));

                    float dx = x - sunX;
                    float dy = y - sunY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float halo = Mathf.Exp(-dist / 34f);
                    c = Color.Lerp(c, glowCol, halo * 0.55f);
                    if (dist < 14f)
                        c = Color.Lerp(c, Color.Lerp(glowCol, Color.white, 0.45f), Mathf.Clamp01(14.5f - dist));

                    if (world == eWorldType.WORLD_5_COSMO)
                    {
                        float ringD = Mathf.Abs(Mathf.Sqrt((dx * 0.55f) * (dx * 0.55f) + (dy * 1.6f) * (dy * 1.6f)) - 19f);
                        if (ringD < 2.2f) c = Color.Lerp(c, glowCol, (2.2f - ringD) * 0.42f);
                    }
                    else if (world == eWorldType.WORLD_3_MOUNTAIN)
                    {
                        float waveY = 82f + Mathf.Sin(nx * 9f) * 10f;
                        float band = Mathf.Exp(-Mathf.Abs(y - waveY) / 7f);
                        c = Color.Lerp(c, glowCol, band * 0.32f);
                    }

                    float farH, midH, foreH;
                    if (world == eWorldType.WORLD_2_DESERT)
                    {
                        farH = 44f + Mathf.Sin(nx * 6.5f) * 12f;
                        float pyr = Mathf.Max(0f, 34f - Mathf.Abs(x - 102f) * 0.82f);
                        farH = Mathf.Max(farH, 34f + pyr);
                        midH = 30f + Mathf.Sin(nx * 8.2f + 1.4f) * 9f;
                        foreH = 18f + Mathf.Sin(nx * 5.0f + 2.8f) * 6f;
                    }
                    else if (world == eWorldType.WORLD_3_MOUNTAIN)
                    {
                        farH = 46f + Mathf.Abs(Mathf.Sin(nx * 9.5f)) * 28f;
                        midH = 32f + Mathf.Abs(Mathf.Sin(nx * 13.0f + 1.1f)) * 20f;
                        foreH = 17f + Mathf.Abs(Mathf.Sin(nx * 18.0f + 2.4f)) * 11f;
                    }
                    else if (world == eWorldType.WORLD_4_DEPTH)
                    {
                        farH = 42f + Mathf.Sin(nx * 11f) * 10f;
                        midH = 29f + Mathf.Cos(nx * 15f) * 8f;
                        foreH = 16f + Mathf.Sin(nx * 22f) * 5f;
                        float ceilH = H - (12f + Mathf.Abs(Mathf.Sin(nx * 24f)) * 18f);
                        if (y > ceilH) c = foreCol;
                    }
                    else
                    {
                        farH = 44f + Mathf.Sin(nx * 7.5f) * 12f + Mathf.Sin(nx * 22f) * 4f;
                        midH = 31f + Mathf.Sin(nx * 11.5f + 1.6f) * 9f + Mathf.Abs(Mathf.Sin(nx * 36f)) * 5f;
                        foreH = 18f + Mathf.Sin(nx * 6.0f + 2.2f) * 5f + Mathf.Abs(Mathf.Sin(nx * 44f)) * 4f;
                    }

                    if (y < farH) c = Color.Lerp(farCol, glowCol, halo * 0.14f);
                    if (y < midH) c = midCol;
                    if (y < foreH) c = foreCol;

                    float edgeX = Mathf.Min(nx, 1f - nx);
                    float edgeY = Mathf.Min(ny, 1f - ny);
                    float vig = Mathf.Clamp01(Mathf.Min(edgeX * 14f, edgeY * 8f));
                    c *= Mathf.Lerp(0.55f, 1.0f, vig);
                    c.a = 1f;

                    px[y * W + x] = c;
                }
            }

            var rng = new System.Random((int)world * 7919 + 42);
            int sparkCount = world == eWorldType.WORLD_5_COSMO ? 42 : 20;
            for (int i = 0; i < sparkCount; i++)
            {
                int sx = rng.Next(8, W - 8);
                int sy = rng.Next(world == eWorldType.WORLD_5_COSMO ? 38 : 16, H - 10);
                int r = (i % 5 == 0) ? 2 : 1;
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int xx = sx + dx;
                        int yy = sy + dy;
                        if (xx < 0 || xx >= W || yy < 0 || yy >= H) continue;
                        float falloff = 1f - Mathf.Sqrt(dx * dx + dy * dy) / (r + 0.8f);
                        if (falloff > 0f)
                        {
                            int idx = yy * W + xx;
                            px[idx] = Color.Lerp(px[idx], Color.Lerp(glowCol, Color.white, 0.5f), falloff * 0.85f);
                        }
                    }
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
