using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 運んでいる料理の今の状態の絵（企画書P4）。JS版 drawDishState を写したもの。
    /// きれい → 少し乱れた → バラバラ → もう食べ物ではない、の4段。
    /// canvas の代わりに、小さな塗り（多角形・楕円）を自前で描く。
    /// </summary>
    public static class DishPic
    {
        const int N = 96;

        public static Texture2D NewTex()
        {
            return new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        }

        // ---- 塗り（canvas と同じく y は下向き）
        static Color[] px;
        static void Blend(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= N || y >= N) return;
            int i = (N - 1 - y) * N + x;
            var d = px[i];
            float a = c.a;
            px[i] = new Color(d.r + (c.r - d.r) * a, d.g + (c.g - d.g) * a, d.b + (c.b - d.b) * a, 1f);
        }

        static void FillPoly(List<Vector2> p, Color c)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var v in p) { x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y); }
            for (int y = Mathf.Max(0, (int)y0); y <= Mathf.Min(N - 1, (int)y1 + 1); y++)
                for (int x = Mathf.Max(0, (int)x0); x <= Mathf.Min(N - 1, (int)x1 + 1); x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    bool inside = false;
                    for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
                        if ((p[i].y > fy) != (p[j].y > fy) &&
                            fx < (p[j].x - p[i].x) * (fy - p[i].y) / (p[j].y - p[i].y) + p[i].x) inside = !inside;
                    if (inside) Blend(x, y, c);
                }
        }

        static void Ellipse(float cx, float cy, float rx, float ry, Color fill, Color? stroke = null, float lw = 0)
        {
            float ox = rx + lw, oy = ry + lw;
            for (int y = (int)(cy - oy - 1); y <= (int)(cy + oy + 1); y++)
                for (int x = (int)(cx - ox - 1); x <= (int)(cx + ox + 1); x++)
                {
                    float dx = (x + 0.5f - cx), dy = (y + 0.5f - cy);
                    float k = dx * dx / (rx * rx) + dy * dy / (ry * ry);
                    if (stroke != null)
                    {
                        float kin = dx * dx / ((rx - lw / 2) * (rx - lw / 2)) + dy * dy / ((ry - lw / 2) * (ry - lw / 2));
                        float kout = dx * dx / ((rx + lw / 2) * (rx + lw / 2)) + dy * dy / ((ry + lw / 2) * (ry + lw / 2));
                        if (kin <= 1) Blend(x, y, fill);
                        else if (kout <= 1) Blend(x, y, stroke.Value);
                    }
                    else if (k <= 1) Blend(x, y, fill);
                }
        }

        /// <summary>tier は値段の割合（1 / 0.75 / 0.5 / 0.25）。</summary>
        public static void Draw(Texture2D tex, Dish dish, float tier)
        {
            px = new Color[N * N];
            var bg = Mats.Hex(0xe6d7bd);                    // 地（企画書と同じベージュ）
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            var col = Mats.Hex(dish.col);
            var rr = new Lcg(20250916);
            System.Func<float> R = () => (float)rr.Next();
            System.Action<float, float, float, int, float, Color> blob = (x, y, r, n, wob, c) =>
            {
                var p = new List<Vector2>();
                for (int i = 0; i <= n; i++)
                {
                    float a = (float)i / n * Mathf.PI * 2;
                    float rad = r * (1 - wob / 2 + R() * wob);
                    p.Add(new Vector2(x + Mathf.Cos(a) * rad, y + Mathf.Sin(a) * rad * 0.78f));
                }
                FillPoly(p, c);
            };
            float W = N, H = N, cx = W / 2, cy = H / 2 + 3;
            // 皿。半分より下では割れて無くなる
            if (tier >= 0.75f) Ellipse(cx, cy, W * 0.37f, H * 0.27f, Mats.Hex(0xf6f3ec), Mats.Hex(0xcdc4b3), 3);
            // 付け合わせ（緑と橙）。崩れるほど散らばる
            float spread = tier >= 1 ? 0.16f : tier >= 0.75f ? 0.36f : 0.46f;
            int gn = tier >= 0.5f ? 7 : 4;
            for (int i = 0; i < gn; i++)
            {
                var c = (i % 2) == 1 ? Mats.Hex(0x6fbf46) : Mats.Hex(0xe8923a);
                float a = R() * Mathf.PI * 2, d = (0.22f + R() * spread) * W;
                float bx = cx + Mathf.Cos(a) * d, by = cy + Mathf.Sin(a) * d * 0.8f, br = 4 + R() * 3;
                blob(bx, by, br, 7, 0.5f, c);
            }
            if (tier >= 1)
            {
                blob(cx, cy, W * 0.19f, 16, 0.14f, col);
                blob(cx - 4, cy - 5, W * 0.08f, 12, 0.3f, new Color(1, 1, 1, 0.18f));
            }
            else if (tier >= 0.75f)
            {
                blob(cx + 3, cy + 2, W * 0.30f, 22, 0.42f, col);
                blob(cx + 8, cy + 8, W * 0.16f, 14, 0.5f, new Color(0, 0, 0, 0.14f));
            }
            else if (tier >= 0.5f)
            {
                for (int i = 0; i < 5; i++)
                {
                    float a = R() * Mathf.PI * 2, d = R() * W * 0.28f;
                    float bx = cx + Mathf.Cos(a) * d, by = cy + Mathf.Sin(a) * d * 0.8f, br = W * 0.11f + R() * W * 0.05f;
                    blob(bx, by, br, 14, 0.4f, col);
                }
            }
            else
            {
                blob(cx, cy + 4, W * 0.34f, 24, 0.4f, col);
                var pu = Mats.Hex(0x9b3fc7);
                for (int i = 0; i < 9; i++)
                {
                    float a = R() * Mathf.PI * 2, d = R() * W * 0.28f;
                    float bx = cx + Mathf.Cos(a) * d, by = cy + Mathf.Sin(a) * d * 0.7f, br = W * 0.07f + R() * W * 0.06f;
                    blob(bx, by, br, 12, 0.35f, pu);
                }
                for (int i = 0; i < 5; i++)
                {
                    float a = R() * Mathf.PI * 2, d = R() * W * 0.24f;
                    float r = 3 + R() * 3;
                    Ellipse(cx + Mathf.Cos(a) * d, cy + Mathf.Sin(a) * d * 0.7f, r, r, new Color(1, 1, 1, 0.35f));
                }
            }
            tex.SetPixels(px);
            tex.Apply(false);
        }

        /// <summary>料理の段ごとの色（JS版 tierCol）。</summary>
        public static Color TierCol(float t)
        {
            return Mats.Hex(t >= 1 ? 0x6fe08a : t >= 0.75f ? 0xffd24a : t >= 0.5f ? 0xff9e6b : 0xff6b6b);
        }
    }
}
