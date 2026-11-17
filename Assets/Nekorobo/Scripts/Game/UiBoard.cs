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

        public UiBoard(Transform parent, string name, bool bg = true, string bgSrc = "ui/res/bg.jpg")
        {
            root = UiKit.Rect(parent, name);
            UiKit.Stretch(root);
            if (bg)
            {
                bgImg = root.gameObject.AddComponent<RawImage>();
                bgImg.texture = DataRoot.Texture(bgSrc);
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
                var lf = Fonts.ForLang();                // 中国語・韓国語はその言語の書体
                if (lf != null) return lf;
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

        static readonly System.Collections.Generic.Dictionary<Texture, Texture2D> dull = new System.Collections.Generic.Dictionary<Texture, Texture2D>();
        /// <summary>灰色がかって暗くした絵（CSS の filter:grayscale(g) brightness(k)。面選びの、まだ押せない「スタート」）</summary>
        public static Texture Dull(Texture src, float gray = 0.85f, float k = 0.75f)
        {
            var t2 = src as Texture2D;
            if (t2 == null || !t2.isReadable) return src;
            Texture2D o;
            if (dull.TryGetValue(src, out o) && o != null) return o;
            var px = t2.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                px[i] = new Color(Mathf.Lerp(c.r, l, gray) * k, Mathf.Lerp(c.g, l, gray) * k, Mathf.Lerp(c.b, l, gray) * k, c.a);
            }
            o = new Texture2D(t2.width, t2.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            o.SetPixels(px); o.Apply(false);
            dull[src] = o;
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

        // ---------------------------------------------------------------- 絵から抜いた字（言語ごとに書く。HTML版 UI_TXT・uiLbl）
        // 清書の絵に焼き込んであった日本語は、HTML版の tools/psd/cutlang.py で字だけ消して切り直してある。字はゲームが訳して書く。
        // [鍵（日本語）, x, y（絵の左上からの字の真ん中）, 字の大きさ, 色, 収める幅, 寄せ（"" 真ん中・l 左端・r 右端）]
        static readonly Dictionary<string, object[][]> UI_TXT = new Dictionary<string, object[][]>
        {
            { "shop/h_up", new[] { L("能力強化", 61.0f, 26.0f, 24.9f, 0xffffff, 160) } },
            { "shop/h_it", new[] { L("アイテム", 61.2f, 27.5f, 25.5f, 0xffffff, 127) } },
            { "shop/btn", new[] { L("購入完了", 130.5f, 37.5f, 31.2f, 0xffffff, 197) } },
            { "shop/btn_on", new[] { L("購入完了", 130.5f, 37.5f, 31.2f, 0xffffff, 197) } },
            { "shop/pp_blue", new[] { L("所持金", 107.9f, 10.8f, 17.2f, 0x45c3ff, 46, "l") } },
            { "shop/pp_red", new[] { L("所持金", 107.9f, 10.8f, 17.2f, 0xff5a45, 46, "l") } },
            { "shop/pp_green", new[] { L("所持金", 107.9f, 10.8f, 17.2f, 0x4ac15d, 46, "l") } },
            { "shop/pp_orange", new[] { L("所持金", 107.9f, 10.8f, 17.2f, 0xff9f45, 46, "l") } },
            { "res/time", new[] { L("タイム", 88.8f, 18.2f, 25.3f, 0x000000, 94), L("ベスト", 55.0f, 125.2f, 25.6f, 0xcccccc, 92) } },
            { "res/band_blue", Band(0x45c3ff) }, { "res/band_red", Band(0xff5a45) }, { "res/band_green", Band(0x4ac15d) }, { "res/band_orange", Band(0xff9f45) },
            { "story/money", new[] { L("所持金", 110.5f, 18.5f, 25.0f, 0x000000, 119) } },
            { "story/time", new[] { L("通しタイム", 111.2f, 18.8f, 25.3f, 0x000000, 167) } },
            { "hud/stage", new[] { L("ラウンド", 31.0f, 31.6f, 10.6f, 0x04141a, 54) } },
            { "play/vipbar", new[] { L("VIPオーダー", 137.2f, 20.2f, 21.4f, 0x402801, 173), L("あと", 289.0f, 20.0f, 21.1f, 0x402801, 50, "l"), L("秒", 424.0f, 20.0f, 21.1f, 0x402801, 50, "r") } },
            { "play/vipok", new[] { L("VIPオーダー", 137.2f, 20.2f, 21.4f, 0x402801, 173) } },
            { "play/vipng", new[] { L("VIPオーダー", 137.2f, 20.2f, 21.4f, 0x402801, 173) } },
        };
        static object[] L(string k, float x, float y, float fs, int col, float mw, string al = "") { return new object[] { k, x, y, fs, col, mw, al }; }
        static object[][] Band(int c)
        {
            return new[] { L("所持金", 145.0f, 17.2f, 16.1f, c, 46, "l"), L("配膳", 58.0f, 57.5f, 10.5f, c, 43, "l"), L("コンボ", 164.0f, 57.5f, 10.6f, c, 48, "l"),
                           L("損壊率", 279.1f, 57.5f, 10.5f, c, 34, "l"), L("ダメージ", 387.0f, 57.5f, 10.4f, c, 45, "l"), L("昇天", 454.5f, 57.8f, 10.4f, c, 24, "l") };
        }
        /// <summary>key の絵の字を、parent の中の (ox, oy)（その絵の左上）を基準に書く。長い訳は字を小さくして幅に収める</summary>
        public static List<Text> Lbl(RectTransform parent, string key, float ox, float oy)
        {
            var o = new List<Text>();
            object[][] rows;
            if (!UI_TXT.TryGetValue(key, out rows)) return o;
            foreach (var a in rows)
            {
                string al = (string)a[6]; float fs = (float)a[3], mw = (float)a[5];
                var t = T(parent, "", 0, 0, fs, Mats.Hex((int)a[4]), al == "r" ? 1 : al == "l" ? 0 : 2, "Lbl");
                var r = t.rectTransform;
                r.pivot = new Vector2(al == "r" ? 1 : al == "l" ? 0 : 0.5f, 0.5f);
                r.anchoredPosition = new Vector2(ox + (float)a[1], -(oy + (float)a[2]));
                t.alignment = al == "r" ? TextAnchor.MiddleRight : al == "l" ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                t.text = Lang.Tr((string)a[0]);
                float w = t.preferredWidth;
                if (w > mw) t.fontSize = Mathf.Max(6, Mathf.FloorToInt(fs * mw / w));
                o.Add(t);
            }
            return o;
        }
        // 飾り文字の絵（高額・VIP の見出し、成功・失敗の判子）は言語ごとに作ってある（<名前>.<言語>.png。日本語は元の絵）
        public static string LangImg(string p)
        {
            string c = Lang.Cur;
            return c == "en" || c == "zh-Hans" || c == "zh-Hant" || c == "ko" ? p.Replace(".png", "." + c + ".png") : p;
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
            t.font = Font; t.fontSize = Mathf.RoundToInt(size); t.color = col; t.text = Lang.Tr(s);
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
        // 絵を貼るとき（面選びの見取り図を平行四辺形に切る）。uv は箱の中の位置から、uvRect の範囲で取る
        public UnityEngine.Texture tex; public UnityEngine.Rect uvRect = new UnityEngine.Rect(0, 0, 1, 1);
        public override UnityEngine.Texture mainTexture { get { return tex != null ? tex : base.mainTexture; } }
        public void Set(params UnityEngine.Vector2[] p) { pts = p; SetVerticesDirty(); }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            if (pts == null || pts.Length < 3) return;
            var r = rectTransform.rect;
            foreach (var p in pts)
            {
                var uv = new UnityEngine.Vector2(uvRect.x + p.x / UnityEngine.Mathf.Max(1, r.width) * uvRect.width,
                                                 uvRect.y + (1 - p.y / UnityEngine.Mathf.Max(1, r.height)) * uvRect.height);
                vh.AddVert(new UnityEngine.Vector3(r.xMin + p.x, r.yMax - p.y), color, uv);
            }
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
            // 清書1109 の人の指（HTML版 assets/ui/ptr/p1〜p4.png。1P 青・2P 赤・3P 緑・4P 橙）。色に合う絵が無ければノーマル。
            // 絵は PSD の大きさなので半分の 62×69 で置く。指先（絵の 24.5, 4）を合わせる点にする（傾けない・影は絵に入っている）
            int i = System.Array.IndexOf(Player.PCOL, col);
            var holder = UiBoard.Box(parent, 0, 0, 0, 0, "Hand");
            var r = UiBoard.Box(holder, 0, 0, 62, 69, "Img");
            r.pivot = new UnityEngine.Vector2(24.5f / 62f, 1 - 4f / 69f);
            r.anchoredPosition = UnityEngine.Vector2.zero;
            var im = r.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            im.texture = DataRoot.Texture("ui/ptr/" + (i < 0 ? "normal" : "p" + (i % 4 + 1)) + ".png"); im.raycastTarget = false;
            return holder;
        }
    }
}
