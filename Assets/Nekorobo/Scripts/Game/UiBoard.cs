using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 清書の画面の板（JS版の .r2 / .s2 / #ssBox）。**1280×720 の板に、PSD から切り出した部品の絵（HTML版の assets/ui/）を置き、字を重ねる。**
    /// 座標は HTML版の CSS と同じ（左上が原点、px）。板は画面に合わせて縮める（JS版 uiFit の --k）。
    ///   Img … 絵（assets/ からの相対パス）。T … 字（left・top。右寄せ rr・まん中寄せ cx は CSS の translateX と同じ）
    ///   In  … 開いたときに部品を順に飛び込ませる（JS版 uiIn。data-a と data-i）
    ///   Count … 金額を数え上げる（JS版 data-count）
    /// 書体は HTML版と同じ「コーポレート・ロゴ（ラウンド）」（Resources/Fonts。SIL OFL 1.1）
    /// </summary>
    public class UiBoard
    {
        public readonly RectTransform root, box;
        readonly RawImage bgImg;

        public UiBoard(Transform parent, string name, bool bg = true)
        {
            root = UiKit.Rect(parent, name);
            UiKit.Stretch(root);
            if (bg)
            {
                bgImg = root.gameObject.AddComponent<RawImage>();
                bgImg.texture = DataRoot.Texture("ui/res/bg.jpg");
                bgImg.color = bgImg.texture != null ? Color.white : Color.black;
                bgImg.raycastTarget = true;
            }
            box = UiKit.Rect(root, "Board");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f); box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(1280, 720);
            root.gameObject.SetActive(false);
        }

        public bool Active { get { return root.gameObject.activeSelf; } set { root.gameObject.SetActive(value); if (value) { root.SetAsLastSibling(); Fit(); } } }

        /// <summary>板を画面に合わせて縮める（縦横どちらかいっぱい）。背景は覆うように（cover）</summary>
        public void Fit()
        {
            var r = root.rect;
            if (r.width <= 0 || r.height <= 0) return;
            float k = Mathf.Min(r.width / 1280f, r.height / 720f);
            box.localScale = Vector3.one * k;
            if (bgImg != null && bgImg.texture != null)
            {
                float ta = bgImg.texture.width / (float)bgImg.texture.height, ra = r.width / r.height;
                bgImg.uvRect = ra > ta ? new Rect(0, (1 - ta / ra) / 2, 1, ta / ra) : new Rect((1 - ra / ta) / 2, 0, ra / ta, 1);
            }
        }

        public void Clear()
        {
            foreach (Transform c in box) Object.Destroy(c.gameObject);
            anims.Clear(); counts.Clear();
        }

        // ---------------------------------------------------------------- 部品
        static Font round;
        public static Font Font
        {
            get
            {
                if (round == null) round = Resources.Load<Font>("Fonts/CorpRound");
                return round != null ? round : Fonts.UI;
            }
        }

        /// <summary>板の上の箱（左上 x, y・幅 w・高さ h）。</summary>
        public static RectTransform Box(RectTransform parent, float x, float y, float w, float h, string name = "Box")
        {
            var r = UiKit.Rect(parent, name);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            return r;
        }

        /// <summary>絵（assets/ からの相対パス）。読めなければ透明の箱。</summary>
        public static RawImage Img(RectTransform parent, string src, float x, float y, float w, float h, string name = null)
        {
            var r = Box(parent, x, y, w, h, name ?? src);
            var im = r.gameObject.AddComponent<RawImage>();
            var t = DataRoot.Texture(src);
            if (t != null) { t.wrapMode = TextureWrapMode.Clamp; im.texture = t; } else im.color = new Color(0, 0, 0, 0);
            im.raycastTarget = false;
            return im;
        }

        static readonly System.Collections.Generic.Dictionary<Texture, Texture2D> bright = new System.Collections.Generic.Dictionary<Texture, Texture2D>();
        /// <summary>明るくした絵（CSS の filter:brightness(k)。uGUI の色は 1 を超えられないので、絵を作っておく）</summary>
        public static Texture Bright(Texture src, float k = 1.35f)
        {
            var t2 = src as Texture2D;
            if (t2 == null || !t2.isReadable) return src;
            Texture2D o;
            if (bright.TryGetValue(src, out o) && o != null) return o;
            var px = t2.GetPixels();
            for (int i = 0; i < px.Length; i++) { var c = px[i]; px[i] = new Color(Mathf.Min(1, c.r * k), Mathf.Min(1, c.g * k), Mathf.Min(1, c.b * k), c.a); }
            o = new Texture2D(t2.width, t2.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            o.SetPixels(px); o.Apply(false);
            bright[src] = o;
            return o;
        }

        /// <summary>色の板（角の丸み radius）。</summary>
        public static Image Fill(RectTransform parent, Color c, float x, float y, float w, float h, float radius = 0, string name = "Fill")
        {
            var r = Box(parent, x, y, w, h, name);
            var im = r.gameObject.AddComponent<Image>();
            im.color = c; im.raycastTarget = false;
            if (radius > 0) { im.sprite = Overhead.Round; im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = 32f / Mathf.Max(1, radius); }
            return im;
        }

        /// <summary>
        /// 字。(x, y) は CSS の left・top。al … 0 左寄せ／1 右寄せ（.rr）／2 まん中（.cx）。
        /// line-height 1 の CSS と同じく、字の箱の上を top に合わせる（uGUI は字の上に少し余白があるので詰める）
        /// </summary>
        public static Text T(RectTransform parent, string s, float x, float y, float size, Color col, int al = 0, string name = "T")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(al == 1 ? 1 : al == 2 ? 0.5f : 0, 1);
            r.anchoredPosition = new Vector2(x, -y + size * 0.12f);
            r.sizeDelta = new Vector2(0, size * 1.2f);
            var t = go.GetComponent<Text>();
            t.font = Font; t.fontSize = Mathf.RoundToInt(size); t.color = col; t.text = s;
            t.alignment = al == 1 ? TextAnchor.UpperRight : al == 2 ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true; t.raycastTarget = false;
            return t;
        }

        /// <summary>字のふち（CSS の -webkit-text-stroke の代わり）。</summary>
        public static void Stroke(Text t, Color c, float w)
        {
            foreach (var d in new[] { new Vector2(w, w), new Vector2(-w, w), new Vector2(w, -w), new Vector2(-w, -w) })
            {
                var o = t.gameObject.AddComponent<Outline>(); o.effectColor = c; o.effectDistance = d;
            }
        }

        // ---------------------------------------------------------------- 開いたときの動き（JS版 uiIn）
        class Anim { public RectTransform r; public CanvasGroup cg; public string k; public float delay, dur; public Vector2 p0; }
        readonly List<Anim> anims = new List<Anim>();
        float animT;
        class Cnt { public Text t; public float to, delay; public System.Func<float, string> fmt; }
        readonly List<Cnt> counts = new List<Cnt>();

        /// <summary>k … left / right / up / slam / pop / fade、i … 順番（70ミリ秒ずつ遅れる）</summary>
        public void In(Component c, string k, float i)
        {
            var r = c is RectTransform ? (RectTransform)c : c.GetComponent<RectTransform>();
            var cg = r.GetComponent<CanvasGroup>();
            if (cg == null) cg = r.gameObject.AddComponent<CanvasGroup>();      // Unity の「無い」は ?? で拾えない
            anims.Add(new Anim { r = r, cg = cg, k = k, delay = i * 0.07f, dur = k == "slam" ? 0.56f : 0.48f, p0 = r.anchoredPosition });
        }
        /// <summary>金額を数え上げる（入ってから）。</summary>
        public void Count(Text t, float to, System.Func<float, string> fmt)
        {
            counts.Add(new Cnt { t = t, to = to, delay = 0.65f, fmt = fmt });
            t.text = fmt(0);
        }
        public void StartIn() { animT = 0; Tick(0); }

        static float Ease(float u) { return 1 - Mathf.Pow(1 - Mathf.Clamp01(u), 3); }

        public void Tick(float dt)
        {
            if (!Active) return;
            Fit();
            animT += dt;
            foreach (var a in anims)
            {
                if (a.r == null) continue;
                float u = Mathf.Clamp01((animT - a.delay) / a.dur);
                float e = Ease(u);
                Vector2 off = Vector2.zero; float s = 1, rot = 0, al = Mathf.Clamp01(u * 3);
                switch (a.k)
                {
                    case "left": off.x = -1400 * (1 - e); break;
                    case "right": off.x = 1400 * (1 - e); break;
                    case "up": off.y = -420 * (1 - e); break;
                    case "slam": s = u < 0.6f ? Mathf.Lerp(2.6f, 0.9f, u / 0.6f) : u < 0.8f ? Mathf.Lerp(0.9f, 1.03f, (u - 0.6f) / 0.2f) : Mathf.Lerp(1.03f, 1, (u - 0.8f) / 0.2f);
                                 rot = u < 0.6f ? Mathf.Lerp(-6, 1, u / 0.6f) : Mathf.Lerp(1, 0, (u - 0.6f) / 0.4f); break;
                    case "pop": s = u < 0.65f ? Mathf.Lerp(0, 1.15f, u / 0.65f) : Mathf.Lerp(1.15f, 1, (u - 0.65f) / 0.35f); break;
                    default: al = e; break;
                }
                a.r.anchoredPosition = a.p0 + off;
                a.r.localScale = Vector3.one * s;
                a.r.localRotation = Quaternion.Euler(0, 0, -rot);
                a.cg.alpha = al;
            }
            foreach (var c in counts)
            {
                if (c.t == null) continue;
                float u = Mathf.Clamp01((animT - c.delay) / 0.7f);
                c.t.text = c.fmt(c.to * (1 - Mathf.Pow(1 - u, 3)));
            }
        }
    }
}

namespace Nekorobo
{
    /// <summary>
    /// 多角形の板（CSS の clip-path: polygon / skewX の平行四辺形の代わり）。点は箱の左上からの px（y は下向き）。
    /// </summary>
    public class UiPoly : UnityEngine.UI.MaskableGraphic
    {
        public UnityEngine.Vector2[] pts = new UnityEngine.Vector2[0];
        public void Set(params UnityEngine.Vector2[] p) { pts = p; SetVerticesDirty(); }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            if (pts == null || pts.Length < 3) return;
            var r = rectTransform.rect;
            foreach (var p in pts) vh.AddVert(new UnityEngine.Vector3(r.xMin + p.x, r.yMax - p.y), color, UnityEngine.Vector2.zero);
            for (int i = 1; i + 1 < pts.Length; i++) vh.AddTriangle(0, i, i + 1);
        }
        public static UiPoly Add(UnityEngine.RectTransform parent, UnityEngine.Color c, float x, float y, float w, float h, params UnityEngine.Vector2[] p)
        {
            var r = UiBoard.Box(parent, x, y, w, h, "Poly");
            r.gameObject.AddComponent<UnityEngine.CanvasRenderer>();          // 自前の絵の部品には描く役が付かないので先に足す
            var g = r.gameObject.AddComponent<UiPoly>();
            g.color = c; g.raycastTarget = false; g.Set(p);
            return g;
        }
        /// <summary>skewX(deg) の平行四辺形（箱 w×h を、中心を軸に傾けた形）</summary>
        public static UiPoly Skew(UnityEngine.RectTransform parent, UnityEngine.Color c, float x, float y, float w, float h, float deg)
        {
            float k = UnityEngine.Mathf.Tan(-deg * UnityEngine.Mathf.Deg2Rad) * h / 2;   // 上の辺が右へずれる量（skewX(-13deg) で右）
            return Add(parent, c, x, y, w, h, new UnityEngine.Vector2(k, 0), new UnityEngine.Vector2(w + k, 0), new UnityEngine.Vector2(w - k, h), new UnityEngine.Vector2(-k, h));
        }
        /// <summary>skewX の平行四辺形の枠（太さ th）</summary>
        public static void SkewRing(UnityEngine.RectTransform parent, UnityEngine.Color c, float x, float y, float w, float h, float deg, float th)
        {
            float k = UnityEngine.Mathf.Tan(-deg * UnityEngine.Mathf.Deg2Rad) * h / 2;
            Add(parent, c, x, y, w, h, new UnityEngine.Vector2(k, 0), new UnityEngine.Vector2(w + k, 0), new UnityEngine.Vector2(w + k - 2 * k * th / h, th), new UnityEngine.Vector2(k - 2 * k * th / h, th));
            Add(parent, c, x, y, w, h, new UnityEngine.Vector2(-k + 2 * k * th / h, h - th), new UnityEngine.Vector2(w - k + 2 * k * th / h, h - th), new UnityEngine.Vector2(w - k, h), new UnityEngine.Vector2(-k, h));
            Add(parent, c, x, y, w, h, new UnityEngine.Vector2(k, 0), new UnityEngine.Vector2(k + th, 0), new UnityEngine.Vector2(-k + th, h), new UnityEngine.Vector2(-k, h));
            Add(parent, c, x, y, w, h, new UnityEngine.Vector2(w + k - th, 0), new UnityEngine.Vector2(w + k, 0), new UnityEngine.Vector2(w - k, h), new UnityEngine.Vector2(w - k - th, h));
        }
    }

    /// <summary>人の色の手（JS版 HAND_SVG。Resources/Ui/hand_0〜3.png）。指先が (0, 0) に来るよう置く</summary>
    public static class Hand
    {
        public static UnityEngine.RectTransform Add(UnityEngine.RectTransform parent, int col)
        {
            int i = System.Array.IndexOf(Player.PCOL, col); if (i < 0) i = 0;
            var holder = UiBoard.Box(parent, 0, 0, 0, 0, "Hand");
            var r = UiBoard.Box(holder, 0, 0, 51.75f, 63.25f, "Img");
            r.pivot = new UnityEngine.Vector2(23f / 51.75f, 1 - 8.6f / 63.25f);       // 指先
            r.anchoredPosition = UnityEngine.Vector2.zero;
            r.localRotation = UnityEngine.Quaternion.Euler(0, 0, 16);                 // 少し傾ける（CSS rotate(-16deg)）
            var im = r.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            im.texture = UnityEngine.Resources.Load<UnityEngine.Texture2D>("Ui/hand_" + i); im.raycastTarget = false;
            var sh = r.gameObject.AddComponent<UnityEngine.UI.Shadow>(); sh.effectColor = new UnityEngine.Color(0, 0, 0, 0.6f); sh.effectDistance = new UnityEngine.Vector2(0, -3);
            return holder;
        }
    }
}
