using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 画面の部品を組む小道具。JS版の CSS（丸い札・枠・影・字の大きさ）をそのまま数字で写すため。
    /// 1280×720 の画面で CSS の 1px ＝ ここの 1 になるようにしてある（Hud の CanvasScaler）。
    /// </summary>
    public static class UiKit
    {
        // ------------------------------------------------------------ 丸い板と枠
        static readonly Dictionary<int, Sprite> rings = new Dictionary<int, Sprite>();
        static Sprite ellipse;

        /// <summary>角の丸い板（半径15.5px の9分割）。</summary>
        public static Sprite Round { get { return Overhead.Round; } }

        /// <summary>角の丸い枠（太さ th px）。</summary>
        public static Sprite Ring(int th)
        {
            Sprite s;
            if (rings.TryGetValue(th, out s)) return s;
            const int N = 32; const float R = 15.5f;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, R, N - R), cy = Mathf.Clamp(y + 0.5f, R, N - R);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float outer = Mathf.Clamp01(R - d + 0.5f), inner = Mathf.Clamp01(d - (R - th) + 0.5f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(outer * inner * 255));
                }
            tex.SetPixels32(px); tex.Apply(false);
            s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect,
                              new Vector4(15, 15, 15, 15));
            rings[th] = s;
            return s;
        }

        /// <summary>楕円（NEXT の皿）。</summary>
        public static Sprite Ellipse
        {
            get
            {
                if (ellipse != null) return ellipse;
                const int N = 64;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01((1 - d) * N / 2 + 0.5f) * 255));
                    }
                tex.SetPixels32(px); tex.Apply(false);
                ellipse = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100);
                return ellipse;
            }
        }

        static float Mult(float radius) { return 15.5f / Mathf.Max(1f, radius); }

        public static RectTransform Rect(Transform parent, string name = "Ui")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            return r;
        }

        public static Image Img(Transform parent, Color c, float radius = 0, string name = "Img")
        {
            var r = Rect(parent, name);
            var im = r.gameObject.AddComponent<Image>();
            im.color = c; im.raycastTarget = false;
            if (radius > 0) { im.sprite = Round; im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = Mult(radius); }
            return im;
        }

        /// <summary>
        /// 札（JS版の #topbar>div や .pc）。地の色・枠の色と太さ・丸み・下の影。
        /// 枠は子に「中身の並びに入らない」板として重ねる。
        /// </summary>
        public static Image Card(Transform parent, Color bg, Color border, int borderPx, float radius, string name = "Card")
        {
            var im = Img(parent, bg, radius, name);
            if (borderPx > 0)
            {
                var b = Img(im.transform, border, 0, "Border");
                b.sprite = Ring(Mathf.RoundToInt(borderPx * Mult(radius)));
                b.type = Image.Type.Sliced; b.pixelsPerUnitMultiplier = Mult(radius);
                Stretch(b.rectTransform);
                b.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            // 札は中身の大きさのまま（中に伸びる物があっても、帯の空きを取り合わない）
            Size(im, -1, -1, 0);
            var sh = im.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.25f); sh.effectDistance = new Vector2(0, -3);
            return im;
        }

        public static void Stretch(RectTransform r, float inset = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
        }

        public static Text Label(Transform parent, string s, float size, Color c, bool bold = true,
                                 TextAnchor al = TextAnchor.MiddleLeft, string name = "Label")
        {
            var r = Rect(parent, name);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Fonts.UI; t.fontSize = Mathf.RoundToInt(size); t.color = c; t.text = s;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = al; t.raycastTarget = false; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1f;
            return t;
        }

        public static HorizontalLayoutGroup HRow(GameObject go, float spacing, RectOffset pad = null,
                                                 TextAnchor al = TextAnchor.MiddleLeft)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.padding = pad ?? new RectOffset();
            h.childAlignment = al;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return h;
        }

        public static VerticalLayoutGroup VCol(GameObject go, float spacing, RectOffset pad = null,
                                               TextAnchor al = TextAnchor.UpperLeft)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.padding = pad ?? new RectOffset();
            v.childAlignment = al;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = v.childForceExpandHeight = false;
            return v;
        }

        public static LayoutElement Size(Component c, float w = -1, float h = -1, float flexW = -1, float minW = -1)
        {
            // Unity の物は ?? で比べられない（消えた物も null に見えない）ので、明示して比べる
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) le.preferredWidth = w;
            if (h >= 0) le.preferredHeight = h;
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (minW >= 0) le.minWidth = minW;
            return le;
        }

        public static RectOffset Pad(int l, int r, int t, int b) { return new RectOffset(l, r, t, b); }

        /// <summary>バー（地と中身）。k は 0〜1。</summary>
        public static Image Bar(Transform parent, Color bg, Color fill, out Image back)
        {
            back = Img(parent, bg, 0, "Track");
            var f = Img(back.transform, fill, 0, "Fill");
            var r = f.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 0.5f);
            r.offsetMin = r.offsetMax = Vector2.zero;
            return f;
        }

        public static void SetBar(Image fill, float k)
        {
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1);
        }

        public static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }

        static readonly Dictionary<string, Sprite> grads = new Dictionary<string, Sprite>();
        /// <summary>角の丸い、縦のグラデーションの板（CSS の linear-gradient(180deg, top, bottom)）。大きさごとに1枚作って使い回す。</summary>
        public static Sprite Grad(int w, int h, float r, Color top, Color bottom)
        {
            string key = w + "x" + h + "r" + r + ColorUtility.ToHtmlStringRGBA(top) + ColorUtility.ToHtmlStringRGBA(bottom);
            Sprite s;
            if (grads.TryGetValue(key, out s)) return s;
            w = Mathf.Max(2, w); h = Mathf.Max(2, h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                var c = Color.Lerp(bottom, top, (y + 0.5f) / h);     // テクスチャの y は下から
                for (int x = 0; x < w; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = Mathf.Clamp(y + 0.5f, r, h - r);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f) * c.a;
                    px[y * w + x] = new Color(c.r, c.g, c.b, a);
                }
            }
            tex.SetPixels32(px); tex.Apply(false);
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100);
            grads[key] = s;
            return s;
        }

        /// <summary>丸い札のグラデーションに白い縁（border 6px）を焼き込んだ絵。</summary>
        public static Sprite PillGrad(int w, int h, Color top, Color bottom, int border)
        {
            w = Mathf.Max(4, w); h = Mathf.Max(4, h);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = h / 2f;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                var c = Color.Lerp(bottom, top, (y + 0.5f) / h);
                for (int x = 0; x < w; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = h / 2f;
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    float wb = Mathf.Clamp01(d - (r - border) + 0.5f);            // 縁の白
                    var col = Color.Lerp(c, Color.white, wb);
                    px[y * w + x] = new Color(col.r, col.g, col.b, a);
                }
            }
            t.SetPixels32(px); t.Apply(false);
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100);
        }
    }
}
