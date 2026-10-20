using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 運んでいる料理の今の状態の絵（企画書P4）。JS版 drawDishState を写したもの。
    /// きれい → 少し乱れた → バラバラ → もう食べ物ではない、の4段。
    /// canvas の代わりに、小さな塗り（多角形・楕円）を自前で描く。
    ///
    /// **料理のモデルがあれば、その写真で描く**（JS版 dishShot / drawDishShot）。料理ごとに1回だけ、
    /// 別のカメラでモデルを小さく撮っておき、崩れの4段は写真を傾けたり割ったり、色をくすませたりして見せる。
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

        // ---------------------------------------------------------------- 料理の写真
        const int S = 192;                    // 写真の大きさ（JS版と同じ）
        const int LAYER = 31;                 // 写真を撮るときだけ使う層（ほかのカメラには映らない）
        static readonly Dictionary<string, Color32[]> shots = new Dictionary<string, Color32[]>();
        static Camera shotCam;
        static RenderTexture shotRt;
        static Light shotLight;

        /// <summary>料理のモデルを小さな絵に撮る（料理ごとに1回だけ）。モデルが無ければ null。</summary>
        static Color32[] Shot(Dish dish)
        {
            Color32[] got;
            if (shots.TryGetValue(dish.k, out got)) return got;
            ModelEntry M;
            if (!ModelStore.DishModels.TryGetValue(dish.k, out M) || M.roots.Count == 0) return null;
            if (shotCam == null)
            {
                var cg = new GameObject("DishShotCam");
                Object.DontDestroyOnLoad(cg);
                shotCam = cg.AddComponent<Camera>();
                shotCam.enabled = false;
                shotCam.clearFlags = CameraClearFlags.SolidColor;
                shotCam.backgroundColor = new Color(0, 0, 0, 0);
                shotCam.fieldOfView = 24; shotCam.nearClipPlane = 0.01f; shotCam.farClipPlane = 20;
                shotCam.cullingMask = 1 << LAYER;
                shotCam.allowHDR = false;
                shotRt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                shotCam.targetTexture = shotRt;
                // 本体とは別の明かり（JS版も本体の画面の設定とは別の小さな描画器で撮る）。撮るときだけ点ける
                var lg = new GameObject("DishShotLight");
                Object.DontDestroyOnLoad(lg);
                shotLight = lg.AddComponent<Light>();
                shotLight.type = LightType.Directional;
                shotLight.intensity = 2.0f / Mathf.PI * 1.5f;
                shotLight.cullingMask = 1 << LAYER;
                shotLight.enabled = false;
                lg.transform.rotation = Quaternion.LookRotation(-Coord.W(-1.2f, 3, 2).normalized);
            }
            var root = new GameObject("DishShot");
            root.transform.position = new Vector3(0, -1000, 0);
            var outer = new GameObject("M").transform;
            outer.SetParent(root.transform, false);
            outer.localRotation = Part.Euler3(M.rotDeg.x * Mathf.Deg2Rad, M.rotDeg.y * Mathf.Deg2Rad, M.rotDeg.z * Mathf.Deg2Rad);
            var inst = Object.Instantiate(M.roots[0], outer, false);
            inst.transform.localRotation = Quaternion.Euler(0, 180, 0);       // glTFast の向きをそろえる（ModelStore と同じ）
            inst.SetActive(true);
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LAYER;
            bool any = false; var b = new Bounds();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            // 斜め上（52度）から、料理がちょうど収まる距離で撮る。three.js の手前（+z）は Unity の -z
            float rad = b.extents.magnitude, el = 52 * Mathf.Deg2Rad;
            float d = rad / Mathf.Sin(shotCam.fieldOfView / 2 * Mathf.Deg2Rad) * 0.92f;
            shotCam.transform.position = b.center + new Vector3(0, Mathf.Sin(el) * d, -Mathf.Cos(el) * d);
            shotCam.transform.LookAt(b.center);
            shotLight.enabled = true;
#if UNITY_EDITOR
            // エディタはシェーダーを裏で順にコンパイルし、済んでいない物は描かない（1枚目が空の写真になった）。
            // 撮る間だけ、その場でコンパイルさせる
            bool asyncWas = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            shotCam.Render();
            UnityEditor.ShaderUtil.allowAsyncCompilation = asyncWas;
#else
            shotCam.Render();
#endif
            shotLight.enabled = false;
            var prev = RenderTexture.active;
            RenderTexture.active = shotRt;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, S, S), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            got = tex.GetPixels32();
            Object.Destroy(tex);
            Object.DestroyImmediate(root);
            shots[dish.k] = got;
            return got;
        }

        /// <summary>写真を置く（JS版 drawDishShot の put）。x, y は中心（canvas の座標）、sx, sw は写真の横の切り出し 0〜1。</summary>
        static void Put(Color32[] img, float x, float y, float rot, float w0, float sc, float sx, float sw, bool dull)
        {
            float w = w0 * sc * sw, h = w0 * sc;
            float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
            float ext = Mathf.Sqrt(w * w + h * h) / 2 + 1;
            for (int py = Mathf.Max(0, (int)(y - ext)); py <= Mathf.Min(N - 1, (int)(y + ext)); py++)
                for (int qx = Mathf.Max(0, (int)(x - ext)); qx <= Mathf.Min(N - 1, (int)(x + ext)); qx++)
                {
                    // 画面の点 → 写真の中の点（回転を戻す。canvas は y が下向き）
                    float dx = qx + 0.5f - x, dy = py + 0.5f - y;
                    float lx = dx * cs + dy * sn, ly = -dx * sn + dy * cs;
                    if (lx < -w / 2 || lx >= w / 2 || ly < -h / 2 || ly >= h / 2) continue;
                    float u = (sx + (lx + w / 2) / w * sw) * S, v = (ly + h / 2) / h * S;     // v は上から
                    var c = Sample(img, u, v);
                    if (c.a <= 0.002f) continue;
                    if (dull)
                    {
                        // CSS の saturate(0.4) brightness(0.8)
                        float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                        c = new Color((l + (c.r - l) * 0.4f) * 0.8f, (l + (c.g - l) * 0.4f) * 0.8f, (l + (c.b - l) * 0.4f) * 0.8f, c.a);
                    }
                    Blend(qx, py, c);
                }
        }

        /// <summary>写真の色（u, v は左上が原点の画素の座標）。4点をならす。</summary>
        static Color Sample(Color32[] img, float u, float v)
        {
            u -= 0.5f; v -= 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, S - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, S - 1);
            int x1 = Mathf.Min(S - 1, x0 + 1), y1 = Mathf.Min(S - 1, y0 + 1);
            float fx = Mathf.Clamp01(u - x0), fy = Mathf.Clamp01(v - y0);
            // 写真の配列は下の行から（ReadPixels）
            Color a = img[(S - 1 - y0) * S + x0], b = img[(S - 1 - y0) * S + x1], c = img[(S - 1 - y1) * S + x0], d = img[(S - 1 - y1) * S + x1];
            // 透明な所の色がにじまないよう、色は不透明さで重みを付けてならす
            float wa = (1 - fx) * (1 - fy) * a.a, wb = fx * (1 - fy) * b.a, wc = (1 - fx) * fy * c.a, wd = fx * fy * d.a;
            float sum = wa + wb + wc + wd;
            if (sum <= 1e-5f) return new Color(0, 0, 0, 0);
            return new Color((a.r * wa + b.r * wb + c.r * wc + d.r * wd) / sum, (a.g * wa + b.g * wb + c.g * wc + d.g * wd) / sum,
                             (a.b * wa + b.b * wb + c.b * wc + d.b * wd) / sum, sum);
        }

        /// <summary>写真で4段の崩れ具合を描く（JS版 drawDishShot）。きれい→少し乱れて汁がはねる→割れてバラバラ→紫に変わる</summary>
        static void DrawShot(Color32[] img, float tier, Color col, System.Func<float> R, System.Action<float, float, float, int, float, Color> blob)
        {
            float W = N, H = N, cx = W / 2, cy = H / 2 + 2, s = W * 0.96f;
            System.Action<int, float, float, float> splash = (n, r0, r1, spread) =>
            {
                for (int i = 0; i < n; i++)
                {
                    float a = R() * Mathf.PI * 2, dd = (0.18f + R() * spread) * W;
                    float bx = cx + Mathf.Cos(a) * dd, by = cy + Mathf.Sin(a) * dd * 0.8f, br = r0 + R() * (r1 - r0);
                    blob(bx, by, br, 9, 0.5f, col);
                }
            };
            if (tier >= 1) Put(img, cx, cy, 0, s, 1, 0, 1, false);
            else if (tier >= 0.75f)
            {
                splash(6, 3, 6, 0.26f);
                Put(img, cx + 3, cy + 2, -0.22f, s, 0.92f, 0, 1, false);
            }
            else if (tier >= 0.5f)
            {
                splash(8, 4, 8, 0.3f);
                Put(img, cx - W * 0.23f, cy + 4, -0.4f, s, 0.76f, 0, 0.38f, false);
                Put(img, cx + W * 0.01f, cy - 7, 0.2f, s, 0.76f, 0.38f, 0.3f, false);
                Put(img, cx + W * 0.25f, cy + 7, 0.55f, s, 0.76f, 0.68f, 0.32f, false);
            }
            else
            {
                Put(img, cx, cy + 4, 0.6f, s, 0.8f, 0, 0.5f, true);
                Put(img, cx + W * 0.12f, cy - 2, -0.4f, s, 0.8f, 0.5f, 0.5f, true);
                var pu = Mats.Hex(0x9b3fc7);
                for (int i = 0; i < 9; i++)
                {
                    float a = R() * Mathf.PI * 2, dd = R() * W * 0.28f;
                    blob(cx + Mathf.Cos(a) * dd, cy + Mathf.Sin(a) * dd * 0.7f, W * 0.07f + R() * W * 0.06f, 12, 0.35f, pu);
                }
                for (int i = 0; i < 5; i++)
                {
                    float a = R() * Mathf.PI * 2, dd = R() * W * 0.24f;
                    float r = 3 + R() * 3;
                    Ellipse(cx + Mathf.Cos(a) * dd, cy + Mathf.Sin(a) * dd * 0.7f, r, r, new Color(1, 1, 1, 0.35f));
                }
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
            // 料理のモデルがあれば、その写真で描く（崩れ具合は写真を傾けたり割ったりして見せる）
            var shot = Shot(dish);
            if (shot != null)
            {
                DrawShot(shot, tier, col, R, blob);
                tex.SetPixels(px);
                tex.Apply(false);
                return;
            }
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
