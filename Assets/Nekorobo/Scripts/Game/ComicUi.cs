using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// マンガ調の大きな知らせの部品（JS版 comicBurstSvg・speedLinesSvg と .cm の字）。
    ///   ギザギザの吹き出し … 外の星形（黒い縁 7px）と内の星形（黒い縁 3px・網点にもできる）
    ///   集中線 … 画面のまわりからまん中へ向かう細い三角 90 本
    ///   字 … 太い黒の縁取りと、右下へずらした色の影（ComicStroke）
    /// </summary>
    public static class ComicUi
    {
        /// <summary>CSS の cubic-bezier(x1, y1, x2, y2)。k は 0〜1 の時間</summary>
        public static float Bezier(float k, float x1, float y1, float x2, float y2)
        {
            if (k <= 0) return 0; if (k >= 1) return 1;
            float t = k;
            for (int i = 0; i < 8; i++)
            {
                float x = 3 * (1 - t) * (1 - t) * t * x1 + 3 * (1 - t) * t * t * x2 + t * t * t - k;
                float dx = 3 * (1 - t) * (1 - t) * x1 + 6 * (1 - t) * t * (x2 - x1) + 3 * t * t * (1 - x2);
                if (Mathf.Abs(dx) < 1e-5f) break;
                t = Mathf.Clamp01(t - x / dx);
            }
            return 3 * (1 - t) * (1 - t) * t * y1 + 3 * (1 - t) * t * t * y2 + t * t * t;
        }

        /// <summary>ギザギザの吹き出し（w×h、とげ n 本）。inner が null でなければ内側をその色に、dots を渡せば網点にする</summary>
        public static RectTransform Burst(RectTransform parent, float w, float h, int n, Color fill, Color inner, Dots dots = null)
        {
            var r = UiKit.Rect(parent, "Burst");
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h); r.anchoredPosition = Vector2.zero;
            // 落ち影（drop-shadow 0 8px 0 rgba(0,0,0,.3)）
            var outer = Star(n, w / 2 - 8, h / 2 - 8, 0.78f, 0.1f);
            var inn = Star(n, w / 2 * 0.8f, h / 2 * 0.74f, 0.86f, 0.06f);
            Fan(r, "Shadow", Grow(outer, 3.5f), new Color(0, 0, 0, 0.3f), new Vector2(0, -8));
            Fan(r, "Edge", Grow(outer, 3.5f), Mats.Hex(0x111111), Vector2.zero);
            Fan(r, "Fill", Grow(outer, -3.5f), fill, Vector2.zero);
            Fan(r, "InEdge", Grow(inn, 1.5f), Mats.Hex(0x111111), Vector2.zero);
            var f = Fan(r, "In", Grow(inn, -1.5f), inner, Vector2.zero);
            if (dots != null) { f.tex = dots.Tex(); f.uvGap = dots.gap; f.color = Color.white; }
            return r;
        }

        /// <summary>網点（45°に傾けた格子に丸）。CSS の pattern の代わり</summary>
        public class Dots
        {
            public Color bg, dot; public float r, gap;
            static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
            public Texture2D Tex()
            {
                string k = UiKit.Hex(bg) + UiKit.Hex(dot) + r + "/" + gap;
                Texture2D t;
                if (cache.TryGetValue(k, out t) && t != null) return t;
                const int S = 32;
                t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                var px = new Color[S * S];
                float rr = r / gap * S;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        px[y * S + x] = Color.Lerp(dot, bg, Mathf.Clamp01(d - rr + 0.5f));
                    }
                t.SetPixels(px); t.Apply(false);
                cache[k] = t;
                return t;
            }
        }

        static List<Vector2> Star(int n, float rx, float ry, float k0, float jit)
        {
            var l = new List<Vector2>();
            for (int i = 0; i < n * 2; i++)
            {
                float a = i / (n * 2f) * Mathf.PI * 2 + (Random.value - 0.5f) * 0.06f;
                float rr = (i % 2 == 1 ? k0 : 1) * (1 - Random.value * jit);
                l.Add(new Vector2(Mathf.Cos(a) * rx * rr, Mathf.Sin(a) * ry * rr));
            }
            return l;
        }

        /// <summary>星形を外へ d px 広げる（縁の太さのぶん。角の二等分線の向きへずらす）</summary>
        public static List<Vector2> Grow(List<Vector2> p, float d)
        {
            var o = new List<Vector2>();
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
                Vector2 n1 = new Vector2(b.y - a.y, a.x - b.x).normalized, n2 = new Vector2(c.y - b.y, b.x - c.x).normalized;
                // 点の並びは反時計回り（y 上向きで見ると時計回り）なので、外向きの向きをそろえる
                var bis = (n1 + n2); float len = bis.magnitude;
                if (len < 1e-4f) { o.Add(b); continue; }
                bis /= len;
                float cos = Vector2.Dot(bis, n1);
                float m = d / Mathf.Max(0.35f, Mathf.Abs(cos));
                if (Vector2.Dot(bis, b) < 0) bis = -bis;
                o.Add(b + bis * m);
            }
            return o;
        }

        public static UiFan Fan(RectTransform parent, string name, List<Vector2> pts, Color c, Vector2 off)
        {
            var r = UiKit.Rect(parent, name);
            UiKit.Stretch(r); r.anchoredPosition = off;
            r.gameObject.AddComponent<CanvasRenderer>();
            var f = r.gameObject.AddComponent<UiFan>();
            f.raycastTarget = false; f.color = c;
            // SVG の y は下向き。ここは上向きなので裏返す（形は上下ほぼ対称）
            var q = new List<Vector2>(); foreach (var v in pts) q.Add(new Vector2(v.x, -v.y));
            f.pts = q; f.SetVerticesDirty();
            return f;
        }

        /// <summary>集中線（親いっぱい。1600×900 の絵を、はみ出す向きに合わせて敷く）。cy … 中心の高さ（0〜1、上から）</summary>
        public static Graphic SpeedLines(RectTransform parent, float cy = 0.5f)
        {
            var r = UiKit.Rect(parent, "SpeedLines"); UiKit.Stretch(r);
            r.gameObject.AddComponent<CanvasRenderer>();
            var s = r.gameObject.AddComponent<UiSpeedLines>();
            s.raycastTarget = false; s.color = Mats.Hex(0x111111); s.cy = cy;
            s.Make();
            return s;
        }

        /// <summary>マンガ調の字（白・黒い縁取り・色の影）。CSS .cm .l1（-webkit-text-stroke:10px #111・text-shadow 5px 7px 0 cc）</summary>
        public static Text Label(RectTransform parent, string s, int size, Color col, Color shadow, float stroke = 5, Vector2? shOff = null)
        {
            var t = UiKit.Label(parent, s, size, col, false, TextAnchor.MiddleCenter);
            t.font = UiBoard.Font; t.fontStyle = FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var e = t.gameObject.AddComponent<ComicStroke>();
            e.stroke = Mats.Hex(0x111111); e.width = stroke; e.shadow = shadow; e.shadowOff = shOff ?? new Vector2(5, -7);
            return t;
        }
    }

    /// <summary>点を中心からの扇で塗る多角形（星形のように、中心から見て凸でない形でも塗れる）</summary>
    public class UiFan : MaskableGraphic
    {
        public List<Vector2> pts = new List<Vector2>();
        public Texture tex; public float uvGap;
        public override Texture mainTexture { get { return tex != null ? tex : base.mainTexture; } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (pts == null || pts.Count < 3) return;
            var c = rectTransform.rect.center;
            System.Func<Vector2, Vector2> uv = p =>
            {
                if (tex == null || uvGap <= 0) return Vector2.zero;
                // 45°に傾けた格子
                float k = 0.70710678f;
                return new Vector2((p.x + p.y) * k, (p.y - p.x) * k) / uvGap;
            };
            vh.AddVert(c, color, uv(Vector2.zero));
            foreach (var p in pts) vh.AddVert(c + p, color, uv(p));
            int n = pts.Count;
            for (int i = 0; i < n; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % n);
        }
    }

    /// <summary>集中線（JS版 speedLinesSvg）</summary>
    public class UiSpeedLines : MaskableGraphic
    {
        public float cy = 0.5f;
        readonly List<Vector2[]> tris = new List<Vector2[]>();
        public void Make()
        {
            tris.Clear();
            float Y = 900 * cy;
            for (int i = 0; i < 90; i++)
            {
                float a = i / 90f * Mathf.PI * 2 + Random.value * 0.05f, da = 0.004f + Random.value * 0.01f;
                float r0 = 300 + Random.value * 120, r1 = 1100;
                System.Func<float, float, Vector2> p = (rr, t) => new Vector2(800 + Mathf.Cos(t) * rr * 1.6f, Y + Mathf.Sin(t) * rr);
                tris.Add(new[] { p(r0, a), p(r1, a - da), p(r1, a + da) });
            }
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            // preserveAspectRatio="xMidYMid slice"
            float s = Mathf.Max(r.width / 1600f, r.height / 900f);
            Vector2 o = new Vector2(r.center.x - 800 * s, r.center.y + 450 * s);
            int k = 0;
            foreach (var t in tris)
            {
                foreach (var v in t) vh.AddVert(new Vector3(o.x + v.x * s, o.y - v.y * s), color, Vector2.zero);
                vh.AddTriangle(k, k + 1, k + 2); k += 3;
            }
        }
    }

    /// <summary>太い縁取りと色の影（字の形をまわり 16 方向にずらして重ねる）</summary>
    public class ComicStroke : BaseMeshEffect
    {
        public Color stroke = Color.black, shadow = new Color(0, 0, 0, 0); public float width = 5; public Vector2 shadowOff = new Vector2(5, -7);
        static readonly List<UIVertex> src = new List<UIVertex>(), dst = new List<UIVertex>();
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            src.Clear(); dst.Clear();
            vh.GetUIVertexStream(src);
            const int N = 16;
            // 影（縁取りした形ごと）→ 縁取り → 字
            if (shadow.a > 0)
                for (int d = 0; d < N; d++) Copy(Dir(d, N) * width + shadowOff, shadow);
            for (int d = 0; d < N; d++) Copy(Dir(d, N) * width, stroke);
            dst.AddRange(src);
            vh.Clear();
            vh.AddUIVertexTriangleStream(dst);
        }
        static Vector2 Dir(int d, int n) { float a = d * Mathf.PI * 2 / n; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
        void Copy(Vector2 off, Color c)
        {
            foreach (var v0 in src)
            {
                var v = v0; v.position += (Vector3)off;
                var cc = (Color32)c; cc.a = (byte)(cc.a * v0.color.a / 255); v.color = cc;
                dst.Add(v);
            }
        }
    }
}
