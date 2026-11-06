using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 凍りの床の絵（JS版 iceTex / iceEnv）。どちらも起動して初めて使うときに1回だけ描く。
    ///   Ice … 盤面ぜんたいでつながる淡い氷（1枚で 6m。つなぎ目が出ないよう、はみ出す物は上下左右にずらして描く）
    ///         色むら（白いもや・少し深い青）、滑った跡（細い白い弧）、ひび（白いふちの中に細い青い線）、霜の粒
    ///   Env … 氷に映す空。上は白く、地平は淡い青、下は暗い。奥の上のほうに天井の明かり（ぼかした白い帯）
    /// </summary>
    public static class IceArt
    {
        public const float TEX_M = 6;
        static Texture2D ice, env;

        // ---- 描く道具（色は 0〜1 の浮動小数で持ち、最後に色の空間を直す）
        class Canvas
        {
            public readonly int S; public readonly Color[] px;
            public Canvas(int s, Color bg) { S = s; px = new Color[s * s]; for (int i = 0; i < px.Length; i++) px[i] = bg; }
            int W(int v) { v %= S; return v < 0 ? v + S : v; }
            public void Blend(int x, int y, Color c, float a)
            {
                if (a <= 0) return;
                int i = W(y) * S + W(x);
                px[i] = Color.Lerp(px[i], c, Mathf.Clamp01(a));
            }
            public void Blob(float cx, float cy, float r, Color c, float a)
            {
                int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r), y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                        if (d < 1) Blend(x, y, c, a * (1 - d));
                    }
            }
            public void Line(float ax, float ay, float bx, float by, float w, Color c, float a)
            {
                float L = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                int n = Mathf.Max(1, Mathf.CeilToInt(L * 2));
                float hw = w / 2;
                for (int s = 0; s <= n; s++)
                {
                    float t = s / (float)n, x = ax + (bx - ax) * t, y = ay + (by - ay) * t;
                    for (int yy = Mathf.FloorToInt(y - hw - 1); yy <= Mathf.CeilToInt(y + hw + 1); yy++)
                        for (int xx = Mathf.FloorToInt(x - hw - 1); xx <= Mathf.CeilToInt(x + hw + 1); xx++)
                        {
                            float d = Mathf.Sqrt((xx - x) * (xx - x) + (yy - y) * (yy - y));
                            float k = Mathf.Clamp01(hw + 0.5f - d);
                            if (k > 0) { int i = W(yy) * S + W(xx); px[i] = Color.Lerp(px[i], c, a * k * 0.5f); }
                        }
                }
            }
        }

        public static Texture2D Ice()
        {
            if (ice != null) return ice;
            const int S = 1024;
            var g = new Canvas(S, Hex(0xc4e6f3));
            uint seed = 11;
            System.Func<float> R = () => { seed = (uint)((seed * 16807UL) % 2147483647UL); return seed / 2147483647f; };
            // 色むら（白いもやと、少し深い青）
            for (int k = 0; k < 46; k++)
            {
                float x = R() * S, y = R() * S, r = 70 + R() * 200; bool light = R() < 0.7f; float a = 0.3f + R() * 0.3f;
                g.Blob(x, y, r, light ? Hex(0xf6fcff) : Hex(0x8cc8e2), light ? a : a * 0.45f);
            }
            // 滑った跡（スケートの刃の跡。細い白い弧）
            for (int k = 0; k < 90; k++)
            {
                float x = R() * S, y = R() * S, r = 120 + R() * 520, a0 = R() * 6.283f, da = 0.15f + R() * 0.45f;
                float w = 0.7f + R() * 1.8f, al = 0.22f + R() * 0.38f;
                int n = Mathf.Max(4, Mathf.CeilToInt(r * da / 6));
                for (int q = 0; q < n; q++)
                {
                    float t0 = a0 + da * q / n, t1 = a0 + da * (q + 1) / n;
                    g.Line(x + Mathf.Cos(t0) * r, y + Mathf.Sin(t0) * r, x + Mathf.Cos(t1) * r, y + Mathf.Sin(t1) * r, w, Color.white, al * 2);
                }
            }
            // ひび（白いふちの中に細い青い線）
            for (int k = 0; k < 6; k++)
            {
                float x = R() * S, y = R() * S, a = R() * 6.283f;
                int n = 5 + Mathf.FloorToInt(R() * 6);
                var pts = new Vector2[n + 1]; pts[0] = new Vector2(x, y);
                for (int q = 1; q <= n; q++) { a += (R() - 0.5f) * 1.3f; float L = 18 + R() * 42; x += Mathf.Cos(a) * L; y += Mathf.Sin(a) * L; pts[q] = new Vector2(x, y); }
                for (int q = 0; q < n; q++) g.Line(pts[q].x, pts[q].y, pts[q + 1].x, pts[q + 1].y, 3.2f, Color.white, 1.6f);
                for (int q = 0; q < n; q++) g.Line(pts[q].x, pts[q].y, pts[q + 1].x, pts[q + 1].y, 1.1f, Hex(0x3c82aa), 1.2f);
            }
            // 霜の粒
            for (int k = 0; k < 1400; k++)
            {
                float x = R() * S, y = R() * S, r = 0.8f + R() * 1.8f, a = 0.25f + R() * 0.55f;
                for (int yy = 0; yy < Mathf.CeilToInt(r); yy++) for (int xx = 0; xx < Mathf.CeilToInt(r); xx++) g.Blend((int)x + xx, (int)y + yy, Color.white, a);
            }
            ice = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 8, name = "IceTex" };
            ice.SetPixels(g.px); ice.Apply(true);
            return ice;
        }

        public static Texture2D Env()
        {
            if (env != null) return env;
            const int W = 512, H = 256;
            var px = new Color[W * H];
            // 上から：白 → 淡い青（0.42）→ 灰青（0.5）→ 暗い（1）
            var stops = new[] { 0f, 0.42f, 0.5f, 1f };
            var cols = new[] { Hex(0xf4fbff), Hex(0xb4d2e4), Hex(0x7d97a8), Hex(0x3a4650) };
            for (int y = 0; y < H; y++)
            {
                float v = y / (float)(H - 1);              // 0 が上（空）
                Color c = cols[3];
                for (int s = 0; s < 3; s++) if (v <= stops[s + 1]) { c = Color.Lerp(cols[s], cols[s + 1], (v - stops[s]) / (stops[s + 1] - stops[s])); break; }
                for (int x = 0; x < W; x++) px[(H - 1 - y) * W + x] = c;
            }
            // 明かり（ぼかした白い帯）。u＝0.25 が奥（−Z）、上から 1/4 あたりが高さ 45°
            var bars = new[] { new Vector4(0.18f, 0.13f, 0.05f, 0.05f), new Vector4(0.25f, 0.10f, 0.07f, 0.04f), new Vector4(0.32f, 0.14f, 0.05f, 0.05f),
                               new Vector4(0.21f, 0.24f, 0.035f, 0.035f), new Vector4(0.29f, 0.25f, 0.035f, 0.035f), new Vector4(0.25f, 0.33f, 0.03f, 0.03f) };
            foreach (var b in bars)
            {
                float bx0 = (b.x - b.z / 2) * W, bx1 = (b.x + b.z / 2) * W, by0 = (b.y - b.w / 2) * H, by1 = (b.y + b.w / 2) * H;
                for (int y = Mathf.FloorToInt(by0 - 8); y <= Mathf.CeilToInt(by1 + 8); y++)
                    for (int x = Mathf.FloorToInt(bx0 - 8); x <= Mathf.CeilToInt(bx1 + 8); x++)
                    {
                        if (x < 0 || x >= W || y < 0 || y >= H) continue;
                        float dx = Mathf.Max(0, Mathf.Max(bx0 - x, x - bx1)), dy = Mathf.Max(0, Mathf.Max(by0 - y, y - by1));
                        float a = 0.95f * Mathf.Exp(-(dx * dx + dy * dy) / (2 * 6 * 6));
                        int i = (H - 1 - y) * W + x;
                        px[i] = Color.Lerp(px[i], Color.white, a);
                    }
            }
            env = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "IceEnv" };
            env.wrapModeU = TextureWrapMode.Repeat;
            env.SetPixels(px); env.Apply(false);
            return env;
        }

        /// <summary>氷の床の素材（岸つきの絵 skin の上に、氷の絵と映り込み）。</summary>
        public static Material Material(Texture skin, float reflect)
        {
            var sh = Shader.Find("Hidden/Nekorobo/Ice");
            if (sh == null) return null;
            var m = new Material(sh) { name = "Ice" };
            m.SetTexture("_MainTex", skin);
            m.SetTexture("_Ice", Ice());
            m.SetTexture("_Env", Env());
            m.SetFloat("_IceK", 1f / TEX_M);
            m.SetFloat("_Reflect", reflect);
            return m;
        }

        // 絵の色（sRGB）を、リニアの描き方の素材へ渡す色にする（Texture2D は sRGB として読まれる）
        static Color Hex(int h) { return Mats.Hex(h); }
    }
}
