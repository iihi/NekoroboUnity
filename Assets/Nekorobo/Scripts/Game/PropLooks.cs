using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 置き物の見た目（コードで組んだ仮）。JS版 props.js の BUILD をそのまま写したもの。
    /// どれも「W×D マス、高さ h」の箱に収まる。届いたモデルがある品物は、これをモデルへ差し替える。
    ///
    /// 写しやすいように、three.js の書き方に近い小道具（N）で組む。数値は JS版と同じで、
    /// 位置・回転・平行移動は N の中で Unity の向きへ直す（Coord の決まり）。
    /// </summary>
    public static class PropLooks
    {
        // ---------------------------------------------------------------- 小道具
        /// <summary>three.js の Mesh / Group の代わり。位置と回転は three.js の数字のまま入れる。</summary>
        public class N
        {
            public Transform t;
            public N(Transform t) { this.t = t; }
            public N P(float x, float y, float z) { t.localPosition = Coord.W(x, y, z); return this; }
            public N X(float x) { var p = t.localPosition; p.x = x; t.localPosition = p; return this; }
            public N Y(float y) { var p = t.localPosition; p.y = y; t.localPosition = p; return this; }
            public N Z(float z) { var p = t.localPosition; p.z = -z; t.localPosition = p; return this; }
            /// <summary>rotation.set(x, y, z)（ラジアン、XYZ 順）</summary>
            public N R(float x, float y, float z) { t.localRotation = Part.Euler3(x, y, z); return this; }
            public N S(float x, float y, float z) { t.localScale = new Vector3(x, y, z); return this; }
            public N SY(float y) { var s = t.localScale; s.y = y; t.localScale = s; return this; }
            public N SZ(float z) { var s = t.localScale; s.z = z; t.localScale = s; return this; }
            // translateX/Y/Z（自分の向きに沿って動かす。three.js の +Z は Unity の -Z）
            public N TX(float d) { t.localPosition += t.localRotation * new Vector3(d, 0, 0); return this; }
            public N TY(float d) { t.localPosition += t.localRotation * new Vector3(0, d, 0); return this; }
            public N TZ(float d) { t.localPosition += t.localRotation * new Vector3(0, 0, -d); return this; }
        }

        static Material M(string css) { return Mats.Get(Hex(css)); }
        static int Hex(string css)
        {
            css = (css ?? "#cccccc").TrimStart('#');
            int v; return int.TryParse(css, System.Globalization.NumberStyles.HexNumber, null, out v) ? v : 0xcccccc;
        }
        /// <summary>明るさをずらした色（three.js と同じく、リニアの色で掛ける）。</summary>
        static string Shade(string css, float k)
        {
            var c = Mats.Hex(Hex(css)).linear * k;
            var g = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b)).gamma;
            return "#" + ColorUtility.ToHtmlStringRGB(g);
        }

        static Transform root;
        static N Add(Mesh mesh, string col) { return new N(Part.Add(root, mesh, M(col), Vector3.zero).transform); }
        /// <summary>JS版 box(): w×h×d の箱を、底が y に来るように置く。</summary>
        static N Box(string col, float w, float h, float d, float y) { return Add(MeshGen.Box(w, h, d), col).Y(y + h / 2); }
        static N Cyl(string col, float rt, float rb, float h, int seg, bool open = false, float ts = 0, float tl = Mathf.PI * 2)
        { return Add(MeshGen.Cylinder(rt, rb, h, seg, open, ts, tl), col); }
        static N Cone(string col, float r, float h, int seg) { return Add(MeshGen.Cone(r, h, seg), col); }
        static N Sph(string col, float r, int ws, int hs, float ps = 0, float pl = Mathf.PI * 2, float ts = 0, float tl = Mathf.PI)
        { return Add(MeshGen.Sphere(r, ws, hs, ps, pl, ts, tl), col); }

        const float PI = Mathf.PI;

        // ---------------------------------------------------------------- 見た目（JS版と同じ順）
        static void tower(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.86f, d = D * 0.86f;
            Box(p.col, w, h, d, 0);
            int floors = Mathf.Max(2, Mathf.RoundToInt(h / 3));
            for (int i = 0; i < floors; i++)
            {
                float y = h * 0.12f + (h * 0.82f) * (i + 0.5f) / floors;
                Box(p.win ?? "#7fc7e8", w * 1.005f, h * 0.30f / floors, d * 1.005f, 0).Y(y);
            }
            int vi = p.vi;
            if (vi == 0) Box(Shade(p.col, 0.82f), w * 0.42f, h * 0.06f, d * 0.42f, h).X(w * 0.14f);
            else if (vi == 1)
            {
                foreach (var a in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) })
                    Box(Shade(p.col, 0.86f), a.x != 0 ? w * 0.08f : w, h * 0.05f, a.y != 0 ? d * 0.08f : d, h).P(a.x * w * 0.46f, h, a.y * d * 0.46f);
            }
            else
            {
                Box(Shade(p.col, 0.78f), w * 0.30f, h * 0.10f, d * 0.30f, h).P(-w * 0.18f, h, d * 0.12f);
                Cyl("#9aa2ad", w * 0.10f, w * 0.10f, h * 0.08f, 8).P(w * 0.22f, h + h * 0.04f, -d * 0.16f);
            }
        }

        static void house(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.84f, d = D * 0.84f;
            Box(p.col, w, h * 0.62f, d, 0);
            Cone(p.roof ?? "#8a4a3a", Mathf.Max(w, d) * 0.72f, h * 0.42f, 4).Y(h * 0.62f + h * 0.21f).R(0, PI / 4, 0).SZ(d / Mathf.Max(w, d));
            Box("#6b4230", w * 0.20f, h * 0.34f, 0.08f, 0).Z(d / 2);
        }

        static void shop(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.88f, d = D * 0.82f;
            Box(p.col, w, h * 0.78f, d, 0);
            Box("#8a4a3a", w * 1.05f, h * 0.10f, d * 1.05f, h * 0.78f);
            for (int i = 0; i < 5; i++)
                Box(i % 2 == 1 ? "#ffffff" : "#d94f4f", w / 5.4f, 0.06f, d * 0.34f, h * 0.52f)
                    .P((i - 2) * w / 5, h * 0.52f, d / 2 + d * 0.15f).R(-0.35f, 0, 0);
        }

        static void barn(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.86f, d = D * 0.86f;
            Box(p.col, w, h * 0.55f, d, 0);
            Cyl("#3a3a3c", w * 0.52f, w * 0.52f, d, 12, false, 0, PI).R(0, PI / 2, PI / 2).Y(h * 0.55f);
            Box("#f0f0ea", w * 0.34f, h * 0.36f, 0.08f, 0).Z(d / 2);
        }

        static void silo(PropDef p, float W, float D)
        {
            float h = p.h, r = Mathf.Min(W, D) * 0.40f;
            Cyl(p.col, r, r, h * 0.86f, 12).Y(h * 0.43f);
            Sph("#9aa2ad", r, 12, 6, 0, PI * 2, 0, PI / 2).Y(h * 0.86f);
        }

        static void tree(PropDef p, float W, float D)
        {
            float h = p.h, tr = Mathf.Min(W, D) * 0.11f;
            Cyl("#8a6a44", tr * 0.8f, tr, h * 0.5f, 7).Y(h * 0.25f);
            for (int i = 0; i < 3; i++)
            {
                float r = Mathf.Min(W, D) * 0.40f * (1 - i * 0.18f);
                Sph(p.col, r, 8, 6).Y(h * 0.50f + i * h * 0.16f + r * 0.45f).SY(0.86f);
            }
        }

        static void pine(PropDef p, float W, float D)
        {
            float h = p.h;
            Cyl("#7a5a3a", 0.09f, 0.12f, h * 0.26f, 6).Y(h * 0.13f);
            for (int i = 0; i < 3; i++)
            {
                float r = Mathf.Min(W, D) * 0.44f * (1 - i * 0.24f);
                Cone(p.col, r, h * 0.36f, 8).Y(h * 0.26f + i * h * 0.26f + h * 0.18f);
            }
        }

        static void bush(PropDef p, float W, float D)
        {
            for (int i = 0; i < 2; i++)
            {
                float r = Mathf.Min(W, D) * 0.34f * (1 - i * 0.2f);
                Sph(p.col, r, 7, 5).P((i - 0.5f) * Mathf.Min(W, D) * 0.22f, r * 0.8f, 0).SY(0.75f);
            }
        }

        static void rock(PropDef p, float W, float D)
        {
            float h = p.h, m = Mathf.Min(W, D);
            Add(MeshGen.Icosahedron(m * 0.42f), p.col).Y(h * 0.42f).S(W / m, 0.85f, D / m * 0.9f);
        }

        static void pot(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.7f, h * 0.42f, D * 0.7f, 0);
            Sph("#6fbf5a", Mathf.Min(W, D) * 0.28f, 7, 5).Y(h * 0.62f).SY(0.8f);
        }

        static void lamp(PropDef p, float W, float D)
        {
            float h = p.h;
            Box("#5f6672", 0.28f, 0.10f, 0.28f, 0);
            Cyl(p.col, 0.055f, 0.075f, h, 6).Y(h / 2);
            Box(p.col, 0.5f, 0.07f, 0.07f, h - 0.12f).X(0.22f);
            Box("#ffe9a8", 0.3f, 0.10f, 0.2f, h - 0.26f).X(0.42f);
        }

        static void sign(PropDef p, float W, float D)
        {
            float h = p.h;
            Cyl("#8b93a0", 0.05f, 0.06f, h * 0.6f, 6).Y(h * 0.3f);
            Box(p.col, W * 0.86f, h * 0.34f, 0.10f, h * 0.58f);
        }

        static void bench(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.86f, h * 0.12f, D * 0.5f, h * 0.34f);
            Box(p.col, W * 0.86f, h * 0.40f, 0.09f, h * 0.46f).TZ(-D * 0.20f);
            foreach (int s in new[] { -1, 1 }) Box("#6b6f76", 0.09f, h * 0.34f, D * 0.44f, 0).X(s * W * 0.33f);
        }

        static void stop(PropDef p, float W, float D)
        {
            float h = p.h;
            foreach (int s in new[] { -1, 1 }) Cyl("#8b93a0", 0.05f, 0.05f, h * 0.82f, 6).P(s * W * 0.36f, h * 0.41f, -D * 0.18f);
            Box(p.col, W * 0.9f, h * 0.08f, D * 0.7f, h * 0.82f);
            Box("#cfd6de", W * 0.86f, h * 0.34f, 0.07f, h * 0.30f).TZ(-D * 0.24f);
        }

        static void fence(PropDef p, float W, float D)
        {
            float h = p.h;
            foreach (int s in new[] { -1, 1 }) Box(p.col, 0.10f, h, 0.10f, 0).X(s * W * 0.42f);
            for (int i = 0; i < 2; i++) Box(p.col, W * 0.94f, h * 0.13f, 0.07f, h * (0.36f + i * 0.34f));
        }

        static void crate(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.76f, h * 0.52f, D * 0.76f, 0);
            Box(Shade(p.col, 1.1f), W * 0.58f, h * 0.44f, D * 0.58f, h * 0.52f).R(0, 0.3f, 0);
        }

        static void cont(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.94f, h * 0.5f, D * 0.86f, 0);
            Box(Shade(p.col, 0.8f), W * 0.9f, h * 0.46f, D * 0.82f, h * 0.52f).X(W * 0.03f);
        }

        static void truck(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.40f, h * 0.55f, D * 0.8f, h * 0.16f);
            Box("#e8e6e0", W * 0.56f, h * 0.62f, D * 0.84f, h * 0.16f).X(-W * 0.22f);
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                    Cyl("#2a2a2c", h * 0.16f, h * 0.16f, 0.16f, 8).R(PI / 2, 0, 0).P(sx * W * 0.30f, h * 0.16f, sz * D * 0.36f);
        }

        static void crane(PropDef p, float W, float D)
        {
            float h = p.h;
            Box("#8b93a0", W * 0.7f, 0.24f, D * 0.7f, 0);
            Box(p.col, W * 0.20f, h * 0.9f, D * 0.20f, 0.24f);
            Box(p.col, W * 2.0f, h * 0.06f, D * 0.16f, h * 0.86f).X(W * 0.55f);
            Box("#6b6f76", W * 0.3f, h * 0.10f, D * 0.22f, h * 0.84f).X(-W * 0.42f);
        }

        static void fact(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.9f, d = D * 0.86f;
            Box(p.col, w, h * 0.62f, d, 0);
            int n = Mathf.Max(3, Mathf.RoundToInt(w / 1.2f));
            for (int i = 0; i < n; i++)
                Box("#8fb8c8", w / n * 0.8f, h * 0.16f, d * 0.9f, h * 0.62f).X(-w / 2 + w / n * (i + 0.5f)).R(0, 0, 0.35f);
            Cyl("#b8b4ac", h * 0.06f, h * 0.08f, h * 0.7f, 8).P(w * 0.38f, h * 0.62f + h * 0.35f, -d * 0.3f);
        }

        static void stat(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.92f, d = D * 0.6f;
            Box(p.col, w, h * 0.7f, d, 0);
            Box("#8a8f96", w * 1.04f, h * 0.09f, d * 1.1f, h * 0.7f);
            Box("#b8bec6", w * 0.98f, h * 0.06f, D * 0.42f, h * 0.62f).Z(d / 2 + D * 0.22f);
            foreach (int sx in new[] { -1, 0, 1 }) Cyl("#8b93a0", 0.06f, 0.06f, h * 0.62f, 6).P(sx * w * 0.38f, h * 0.31f, d / 2 + D * 0.40f);
        }

        static void apart(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.88f, d = D * 0.88f;
            Box(p.col, w, h, d, 0);
            int floors = Mathf.Max(3, Mathf.RoundToInt(h / 2.8f));
            for (int i = 0; i < floors; i++)
            {
                float y = h * 0.10f + (h * 0.84f) * (i + 0.5f) / floors;
                Box(p.win ?? "#8fb8d8", w * 1.006f, h * 0.26f / floors, d * 1.006f, 0).Y(y);
                Box(Shade(p.col, 0.88f), w * 0.92f, h * 0.05f, 0.14f, 0).P(0, y - h * 0.14f / floors, d / 2 + 0.07f);
            }
            Box(Shade(p.col, 0.8f), w * 1.03f, h * 0.035f, d * 1.03f, h);
        }

        static void flat(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.92f, d = D * 0.88f;
            Box(p.col, w, h * 0.82f, d, 0);
            Box(Shade(p.col, 0.8f), w * 1.04f, h * 0.10f, d * 1.04f, h * 0.82f);
            Box(p.sign ?? "#ffffff", w * 0.70f, h * 0.20f, 0.10f, h * 0.50f).Z(d / 2 + 0.06f);
            for (int i = 0; i < 4; i++)
                Box(i % 2 == 1 ? "#ffffff" : p.col, w / 4.4f, 0.06f, d * 0.22f, h * 0.34f).P((i - 1.5f) * w / 4.2f, h * 0.34f, d / 2 + d * 0.10f).R(-0.3f, 0, 0);
        }

        static void civic(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.9f, d = D * 0.82f;
            Box(p.col, w, h * 0.78f, d, 0);
            Box(p.roof ?? "#5a6068", w * 1.04f, h * 0.12f, d * 1.04f, h * 0.78f);
            int n = Mathf.Max(3, Mathf.RoundToInt(w / 1.4f));
            for (int i = 0; i < n; i++) Cyl("#efeade", 0.09f, 0.09f, h * 0.62f, 7).P(-w * 0.34f + w * 0.68f * i / (n - 1), h * 0.31f, d / 2 + 0.22f);
            Box("#efeade", w * 0.76f, h * 0.10f, 0.5f, h * 0.62f).Z(d / 2 + 0.22f);
        }

        static void hosp(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.88f, d = D * 0.86f;
            Box(p.col, w, h, d, 0);
            int floors = Mathf.Max(3, Mathf.RoundToInt(h / 2.4f));
            for (int i = 0; i < floors; i++) Box("#9fc8e0", w * 1.006f, h * 0.22f / floors, d * 1.006f, 0).Y(h * 0.12f + (h * 0.8f) * (i + 0.5f) / floors);
            float m = Mathf.Min(w, d);
            Cyl("#6a7078", m * 0.34f, m * 0.34f, h * 0.02f, 16).Y(h + h * 0.01f);
            Box("#ffffff", m * 0.10f, h * 0.008f, m * 0.36f, h + h * 0.02f);
            Box("#d02f2f", w * 0.16f, h * 0.05f, 0.06f, h * 0.70f).TZ(d / 2);
            Box("#d02f2f", w * 0.05f, h * 0.16f, 0.06f, h * 0.64f).TZ(d / 2);
        }

        static void fire(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.92f, d = D * 0.86f;
            Box(p.col, w, h * 0.84f, d, 0);
            Box("#4a4a52", w * 1.04f, h * 0.12f, d * 1.04f, h * 0.84f);
            for (int i = 0; i < 2; i++) Box("#e8e6e0", w * 0.34f, h * 0.5f, 0.08f, 0).P((i - 0.5f) * w * 0.44f, h * 0.25f, d / 2);
        }

        static void church(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.62f, d = D * 0.86f;
            Box(p.col, w, h * 0.45f, d, 0);
            Cone(p.roof ?? "#3f5a8a", Mathf.Max(w, d) * 0.62f, h * 0.22f, 4).Y(h * 0.56f).R(0, PI / 4, 0);
            Box(p.col, w * 0.42f, h * 0.72f, w * 0.42f, 0).X(-W * 0.30f);
            Cone(p.roof ?? "#3f5a8a", w * 0.32f, h * 0.28f, 4).P(-W * 0.30f, h * 0.86f, 0).R(0, PI / 4, 0);
        }

        static void dome(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.86f, d = D * 0.86f;
            Box(p.col, w, h * 0.46f, d, 0);
            Sph(p.roof ?? "#4a9a72", Mathf.Min(w, d) * 0.36f, 12, 8, 0, PI * 2, 0, PI / 2).Y(h * 0.46f);
            foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            {
                Cyl(p.col, 0.10f, 0.12f, h * 0.62f, 7).P(s.x * w * 0.42f, h * 0.31f, s.y * d * 0.42f);
                Sph(p.roof ?? "#4a9a72", 0.16f, 8, 6).P(s.x * w * 0.42f, h * 0.66f, s.y * d * 0.42f);
            }
        }

        static void stadium(PropDef p, float W, float D)
        {
            float h = p.h;
            Box("#4a9a3a", W * 0.56f, 0.06f, D * 0.52f, 0);
            Box("#ffffff", W * 0.54f, 0.02f, 0.06f, 0.06f);
            float[][] st = { new[] { 0f, -1, W * 0.86f, D * 0.16f }, new[] { 0f, 1, W * 0.86f, D * 0.16f },
                             new[] { -1f, 0, W * 0.16f, D * 0.62f }, new[] { 1f, 0, W * 0.16f, D * 0.62f } };
            foreach (var a in st)
            {
                Box(p.col, a[2], h * 0.40f, a[3], 0).P(a[0] * W * 0.36f, h * 0.20f, a[1] * D * 0.34f);
                Box(p.roof ?? "#e8c020", a[2] * 1.02f, h * 0.06f, a[3] * 1.1f, 0).P(a[0] * W * 0.38f, h * 0.44f, a[1] * D * 0.35f);
            }
            foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
            {
                Cyl("#c8ccd0", 0.07f, 0.09f, h * 0.9f, 6).P(s.x * W * 0.46f, h * 0.45f, s.y * D * 0.44f);
                Box("#ffffff", 0.5f, 0.2f, 0.14f, h * 0.9f).P(s.x * W * 0.46f, h * 0.9f, s.y * D * 0.44f);
            }
        }

        static void park(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.9f, d = D * 0.86f;
            int n = Mathf.Max(2, Mathf.RoundToInt(h / 2.2f));
            for (int i = 0; i <= n; i++) Box(p.col, w, h * 0.06f, d, h * i / n);
            foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                Box(Shade(p.col, 0.82f), 0.18f, h, 0.18f, 0).P(s.x * w * 0.44f, h / 2, s.y * d * 0.44f);
        }

        static void round(PropDef p, float W, float D)
        {
            float h = p.h, r = Mathf.Min(W, D) * 0.42f;
            Cyl(p.col, r, r, h, 16).Y(h / 2);
            int floors = Mathf.Max(3, Mathf.RoundToInt(h / 2.6f));
            for (int i = 0; i < floors; i++) Cyl("#8fc8e0", r * 1.01f, r * 1.01f, h * 0.24f / floors, 16).Y(h * 0.10f + (h * 0.82f) * (i + 0.5f) / floors);
        }

        static void gas(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.92f, d = D * 0.86f;
            Box(p.col, w, h * 0.10f, d, h * 0.78f);
            Box("#ffffff", w * 0.96f, h * 0.05f, d * 0.9f, h * 0.72f);
            foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                Cyl("#c8ccd0", 0.09f, 0.09f, h * 0.72f, 7).P(s.x * w * 0.38f, h * 0.36f, s.y * d * 0.36f);
            foreach (int sx in new[] { -1, 1 }) Box("#e8e6e0", 0.3f, h * 0.28f, 0.5f, 0).X(sx * w * 0.2f);
        }

        static void ware(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.92f, d = D * 0.88f;
            Box(p.col, w, h * 0.62f, d, 0);
            Cyl(Shade(p.col, 0.82f), w * 0.5f, w * 0.5f, d, 14, false, 0, PI).R(0, PI / 2, PI / 2).Y(h * 0.62f);
            for (int i = 0; i < 2; i++) Box("#a8aeb6", w * 0.26f, h * 0.36f, 0.08f, 0).P((i - 0.5f) * w * 0.4f, h * 0.18f, d / 2);
        }

        static void chim(PropDef p, float W, float D)
        {
            float h = p.h;
            Cyl(p.col, 0.22f, 0.34f, h, 10).Y(h / 2);
            for (int i = 0; i < 3; i++) Cyl("#ffffff", 0.26f, 0.26f, h * 0.04f, 10).Y(h * (0.55f + i * 0.14f));
        }

        static void tank(PropDef p, float W, float D)
        {
            float h = p.h, r = Mathf.Min(W, D) * 0.40f;
            Cyl(p.col, r, r, h * 0.82f, 14).Y(h * 0.41f);
            Sph(Shade(p.col, 0.9f), r, 14, 6, 0, PI * 2, 0, PI / 2).Y(h * 0.82f);
        }

        static void gantry(PropDef p, float W, float D)
        {
            float h = p.h;
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                    Box(p.col, 0.16f, h * 0.8f, 0.16f, 0).P(sx * W * 0.36f, h * 0.4f, sz * D * 0.3f);
            Box(p.col, W * 1.5f, h * 0.10f, D * 0.22f, h * 0.8f);
            Box("#e8e6e0", 0.5f, h * 0.12f, 0.5f, h * 0.66f).X(W * 0.2f);
        }

        static void scaf(PropDef p, float W, float D)
        {
            float h = p.h;
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                    Cyl(p.col, 0.05f, 0.05f, h, 6).P(sx * W * 0.36f, h / 2, sz * D * 0.36f);
            int n = Mathf.Max(2, Mathf.RoundToInt(h / 1.6f));
            for (int i = 1; i <= n; i++) Box(p.col, W * 0.78f, 0.06f, D * 0.78f, h * i / n);
        }

        static void green(PropDef p, float W, float D)
        {
            float h = p.h, w = W * 0.9f, d = D * 0.88f;
            Box("#e0ded4", w, h * 0.16f, d, 0);
            Cyl(p.col, w * 0.48f, w * 0.48f, d, 12, false, 0, PI).R(0, PI / 2, PI / 2).Y(h * 0.16f);
        }

        static void field(PropDef p, float W, float D)
        {
            Box(Shade(p.col, 0.8f), W * 0.96f, 0.08f, D * 0.96f, 0);
            int n = Mathf.Max(3, Mathf.RoundToInt(W * 1.6f));
            for (int i = 0; i < n; i++) Box(p.col, W * 0.9f / n * 0.6f, 0.14f, D * 0.9f, 0.06f).X(-W * 0.45f + W * 0.9f * (i + 0.5f) / n);
        }

        static void hay(PropDef p, float W, float D)
        {
            float h = p.h;
            Cyl(p.col, h * 0.46f, h * 0.46f, W * 0.7f, 12).R(0, 0, PI / 2).Y(h * 0.46f);
        }

        static void wind(PropDef p, float W, float D)
        {
            float h = p.h;
            Cyl(p.col, 0.10f, 0.22f, h * 0.9f, 8).Y(h * 0.45f);
            Sph(p.col, 0.18f, 8, 6).Y(h * 0.9f);
            for (int i = 0; i < 3; i++) Box("#ffffff", 0.14f, h * 0.42f, 0.05f, 0).Y(h * 0.9f).R(0, 0, i * PI * 2 / 3).TY(h * 0.21f);
        }

        static void stump(PropDef p, float W, float D)
        {
            float h = p.h;
            Cyl(p.col, h * 0.5f, h * 0.6f, h, 8).Y(h / 2);
            Cyl(Shade(p.col, 1.25f), h * 0.5f, h * 0.5f, 0.04f, 8).Y(h);
        }

        static void signal(PropDef p, float W, float D)
        {
            float h = p.h;
            Cyl(p.col, 0.055f, 0.07f, h, 6).Y(h / 2);
            Box(p.col, 0.2f, 0.52f, 0.16f, h - 0.6f);
            var cols = new[] { "#d02f2f", "#e8c020", "#4a9a4a" };
            for (int i = 0; i < 3; i++) Cyl(cols[i], 0.055f, 0.055f, 0.04f, 8).R(PI / 2, 0, 0).P(0, h - 0.24f - i * 0.16f, 0.09f);
        }

        static void board(PropDef p, float W, float D)
        {
            float h = p.h;
            foreach (int sx in new[] { -1, 1 }) Cyl("#8b93a0", 0.06f, 0.07f, h * 0.56f, 6).P(sx * W * 0.26f, h * 0.28f, 0);
            Box(p.col, W * 0.88f, h * 0.42f, 0.10f, h * 0.54f);
        }

        static void bin(PropDef p, float W, float D)
        {
            float h = p.h;
            Cyl(p.col, h * 0.28f, h * 0.24f, h * 0.86f, 8).Y(h * 0.43f);
            Cyl(Shade(p.col, 0.8f), h * 0.31f, h * 0.31f, h * 0.10f, 8).Y(h * 0.90f);
        }

        static void phone(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, 0.62f, h, 0.62f, 0);
            Box("#cfe8f2", 0.44f, h * 0.56f, 0.66f, h * 0.24f);
            Box(Shade(p.col, 0.8f), 0.72f, h * 0.07f, 0.72f, h);
        }

        static void hydrant(PropDef p, float W, float D)
        {
            float h = p.h;
            Cyl(p.col, h * 0.22f, h * 0.26f, h * 0.8f, 8).Y(h * 0.4f);
            Sph(p.col, h * 0.22f, 8, 6).Y(h * 0.8f);
            foreach (int sx in new[] { -1, 1 }) Cyl(p.col, h * 0.09f, h * 0.09f, h * 0.24f, 6).R(0, 0, PI / 2).P(sx * h * 0.20f, h * 0.55f, 0);
        }

        static void balloon(PropDef p, float W, float D)
        {
            float h = p.h, r = Mathf.Min(W, D) * 0.42f;
            Sph(p.col, r, 12, 10).SY(1.25f).Y(h - r * 1.25f);
            Box("#a5763f", r * 0.5f, r * 0.4f, r * 0.5f, h - r * 2.5f);
        }

        static void wheels(float W, float D, float y, float r)
        {
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                    Cyl("#2a2a2c", r, r, 0.16f, 8).R(PI / 2, 0, 0).P(sx * W * 0.30f, y, sz * D * 0.34f);
        }

        static void car(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.88f, h * 0.42f, D * 0.76f, h * 0.16f);
            Box("#cfe8f2", W * 0.46f, h * 0.30f, D * 0.66f, h * 0.56f);
            wheels(W, D, h * 0.18f, h * 0.18f);
        }

        static void wagon(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.9f, h * 0.56f, D * 0.78f, h * 0.14f);
            Box("#cfe8f2", W * 0.62f, h * 0.26f, D * 0.72f, h * 0.68f);
            wheels(W, D, h * 0.16f, h * 0.16f);
        }

        static void bus(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.94f, h * 0.62f, D * 0.8f, h * 0.16f);
            for (int i = 0; i < 5; i++) Box("#cfe8f2", W * 0.14f, h * 0.24f, D * 0.84f, h * 0.44f).X(-W * 0.36f + W * 0.72f * i / 4);
            wheels(W, D, h * 0.16f, h * 0.16f);
        }

        static void dump(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.34f, h * 0.5f, D * 0.76f, h * 0.16f);
            Box(Shade(p.col, 0.78f), W * 0.56f, h * 0.44f, D * 0.82f, h * 0.24f).X(-W * 0.20f).R(0, 0, -0.06f);
            wheels(W, D, h * 0.17f, h * 0.17f);
        }

        static void tanker(PropDef p, float W, float D)
        {
            float h = p.h;
            Box("#e8e6e0", W * 0.26f, h * 0.5f, D * 0.74f, h * 0.16f);
            Cyl(p.col, h * 0.26f, h * 0.26f, W * 0.62f, 12).R(0, 0, PI / 2).P(-W * 0.16f, h * 0.56f, 0);
            wheels(W, D, h * 0.16f, h * 0.16f);
        }

        static void tractor(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.72f, h * 0.34f, D * 0.6f, h * 0.24f);
            Box("#cfe8f2", W * 0.32f, h * 0.30f, D * 0.56f, h * 0.56f);
            foreach (int sx in new[] { -1, 1 })
            {
                float r = sx > 0 ? h * 0.30f : h * 0.18f;
                foreach (int sz in new[] { -1, 1 }) Cyl("#2a2a2c", r, r, 0.2f, 8).R(PI / 2, 0, 0).P(sx * W * 0.28f, r, sz * D * 0.34f);
            }
        }

        static void train(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.94f, h * 0.56f, D * 0.8f, h * 0.20f);
            Box(Shade(p.col, 0.75f), W * 0.94f, h * 0.10f, D * 0.84f, h * 0.76f);
            for (int i = 0; i < 6; i++) Box("#cfe8f2", W * 0.11f, h * 0.20f, D * 0.84f, h * 0.46f).X(-W * 0.38f + W * 0.76f * i / 5);
            Box("#4a4a52", W * 0.9f, h * 0.12f, D * 0.5f, h * 0.08f);
        }

        static void ship(PropDef p, float W, float D)
        {
            float h = p.h;
            Box(p.col, W * 0.9f, h * 0.36f, D * 0.7f, 0);
            Cone(p.col, D * 0.35f, W * 0.3f, 4).R(0, PI / 4, -PI / 2).P(W * 0.55f, h * 0.18f, 0);
            Box("#f0f0ea", W * 0.34f, h * 0.34f, D * 0.5f, h * 0.36f).TX(-W * 0.16f);
            Cyl("#d02f2f", h * 0.10f, h * 0.10f, h * 0.30f, 8).P(-W * 0.28f, h * 0.85f, 0);
        }

        delegate void Fn(PropDef p, float W, float D);
        static readonly Dictionary<string, Fn> BUILD = new Dictionary<string, Fn>
        {
            {"tower",tower},{"house",house},{"shop",shop},{"barn",barn},{"silo",silo},{"fact",fact},{"stat",stat},
            {"tree",tree},{"pine",pine},{"bush",bush},{"rock",rock},{"pot",pot},{"lamp",lamp},{"sign",sign},
            {"bench",bench},{"stop",stop},{"fence",fence},{"crate",crate},{"cont",cont},{"truck",truck},{"crane",crane},
            {"apart",apart},{"flat",flat},{"civic",civic},{"hosp",hosp},{"fire",fire},{"church",church},{"dome",dome},
            {"stadium",stadium},{"park",park},{"round",round},{"gas",gas},{"ware",ware},{"chim",chim},{"tank",tank},
            {"gantry",gantry},{"scaf",scaf},{"green",green},{"field",field},{"hay",hay},{"wind",wind},{"stump",stump},
            {"signal",signal},{"board",board},{"bin",bin},{"phone",phone},{"hydrant",hydrant},{"balloon",balloon},
            {"car",car},{"wagon",wagon},{"bus",bus},{"dump",dump},{"tanker",tanker},{"tractor",tractor},{"train",train},{"ship",ship},
        };

        /// <summary>1つぶんの見た目（JS版 buildProp）。原点は床の上の中心。W,D はマス数ぶんのメートル。</summary>
        public static GameObject Build(Transform parent, PropDef p, float W, float D)
        {
            var g = new GameObject("Prop_" + p.k);
            g.transform.SetParent(parent, false);
            root = g.transform;
            Fn f;
            if (!BUILD.TryGetValue(p.look ?? "", out f)) f = tower;
            f(p, W, D);
            root = null;
            return g;
        }
    }
}
