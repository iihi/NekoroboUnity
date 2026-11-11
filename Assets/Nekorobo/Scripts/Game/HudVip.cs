using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 高額・VIP オーダーの画面の表示（JS版 vipRender / announce / salePop / coinFly）。
    ///   ・料理の上の札（受取口の皿・運んでいる皿・床の皿・VIP 台）と、VIP の届け先の客の王冠
    ///   ・まん中上の大きな知らせ（「VIP オーダー出現！」「高額オーダー出現！」）と、VIP の残り秒数の帯
    ///   ・届けたときの売上の式（値段 × 状態 × 速達 × 連続 ＝ 額）
    ///   ・攻撃で奪ったお金が、相手 → 自分 → 上の帯へ飛ぶ
    /// 絵の部品は HTML版も CSS の仮の形なので、ここも字と板で作る。
    /// </summary>
    public class HudVip
    {
        readonly RectTransform layer;
        readonly Game g;
        Camera Cam { get { return g.mainCam; } }

        class Ic { public RectTransform r; public Image bg; public Text t; }
        readonly Dictionary<object, Ic> icons = new Dictionary<object, Ic>();
        readonly HashSet<object> seen = new HashSet<object>();

        RectTransform annc, anncBx; Graphic anncSl; CanvasGroup anncCg; float anncT, anncAge, anncRot, anncTop, anncSc;
        readonly Queue<KeyValuePair<string, float>> anncQ = new Queue<KeyValuePair<string, float>>();
        RectTransform bar, barSt; Text barT; RawImage barBg, barTag, barStImg; string barKey; float barStT, barHurryT;
        int nKind; float nT, nAmt; Player nP;

        class Sale { public RectTransform r; public Text f, a; public Vector3 w; public float age; }
        class ComicT { public Text t; public Vector3 w; public float age; }
        readonly List<ComicT> comics = new List<ComicT>();
        readonly List<Sale> sales = new List<Sale>();
        class Coin { public RectTransform r; public Player from, to; public float t0, age; public float share; public Vector2 s0; public float lift; public Vector2? mid; }
        readonly List<Coin> coins = new List<Coin>();

        static readonly Color GOLD = Mats.Hex(0xffd400), INK = new Color(0.07f, 0.07f, 0.07f);

        public HudVip(RectTransform parent, Game game)
        {
            g = game;
            layer = UiKit.Rect(parent, "Vip");
            UiKit.Stretch(layer);
            layer.SetSiblingIndex(1);
            // まん中上の大きな知らせ
            // マンガ調（JS版 #annc。集中線と網点のギザギザの吹き出し）。中身は出すたびに作る
            annc = UiKit.Rect(layer, "Annc"); UiKit.Stretch(annc);
            annc.gameObject.SetActive(false);
            // VIP の帯（清書1109）。帯・人の札・成功/失敗の判子は絵（HTML版 assets/ui/play/vipbar・vipok・vipng・vtag1〜4・*_st）、
            // 残り秒数だけ字で重ねる。位置はまん中上（JS版 #vipBar：top 136px・455×50）
            bar = UiKit.Rect(layer, "VipBar");
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1); bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = new Vector2(0, -136); bar.sizeDelta = new Vector2(455, 50);
            System.Func<string, float, float, float, float, RawImage> pic = (nm, x, y, w, h) =>
            {
                var im = new GameObject(nm, typeof(RectTransform)).AddComponent<RawImage>();
                var r = im.rectTransform; r.SetParent(bar, false);
                r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
                r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
                im.raycastTarget = false;
                return im;
            };
            barBg = pic("Bg", 0, 0, 455, 50);
            barTag = pic("Tag", 20.5f, 10.5f, 60, 21);
            barT = UiBoard.T(bar, "", 341 + 38.5f / 2, 5 + (26.5f - 30) / 2, 30, Mats.Hex(0xe60012), 2, "Sec");
            barStImg = pic("St", 240, -23, 175.5f, 86.5f);
            barSt = barStImg.rectTransform; barSt.pivot = new Vector2(0.5f, 0.5f);
            barSt.anchoredPosition = new Vector2(240 + 175.5f / 2, 23 - 86.5f / 2);
            bar.gameObject.SetActive(false);
        }

        static Text Lbl(Transform p, string s, int size, Color c, Vector2 at)
        {
            var go = new GameObject("T", typeof(RectTransform), typeof(Text));
            var r = go.GetComponent<RectTransform>(); r.SetParent(p, false); r.anchoredPosition = at; r.sizeDelta = new Vector2(10, 10);
            var t = go.GetComponent<Text>();
            t.font = Fonts.UI; t.fontSize = size; t.fontStyle = FontStyle.Bold; t.color = c; t.text = s;
            t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public void Reset()
        {
            foreach (var i in icons.Values) Object.Destroy(i.r.gameObject);
            icons.Clear();
            anncQ.Clear(); anncT = 0; annc.gameObject.SetActive(false);
            nT = 0; nKind = 0; bar.gameObject.SetActive(false);
            foreach (var s in sales) Object.Destroy(s.r.gameObject);
            sales.Clear();
            foreach (var c in coins) Object.Destroy(c.r.gameObject);
            coins.Clear();
            foreach (var c in comics) Object.Destroy(c.t.gameObject);
            comics.Clear();
        }

        // ------------------------------------------------------------ 知らせ
        /// <summary>kind 0=発生 1=成功 2=失敗</summary>
        public void Notice(int kind, Player P, float amt)
        {
            nKind = kind; nP = P; nAmt = amt; nT = kind != 0 ? 2.8f : 0;
            if (kind == 0) Announce("vip", g.T.vipPrice);
        }
        public void Announce(string kind, float amt)
        {
            anncQ.Enqueue(new KeyValuePair<string, float>(kind, amt));
            if (anncT <= 0) AnncNext();
        }
        void AnncNext()
        {
            if (anncQ.Count == 0) { annc.gameObject.SetActive(false); anncT = 0; return; }
            var a = anncQ.Dequeue();
            bool vip = a.Key == "vip";
            for (int i = annc.childCount - 1; i >= 0; i--) Object.Destroy(annc.GetChild(i).gameObject);
            anncSl = ComicUi.SpeedLines(annc, 0.36f);
            // VIP も高額も、まん中上に出す。字の影の色だけ変える（VIP は赤紫、高額は橙）。高額は小さめで、もう少し上へ
            var cc = Mats.Hex(vip ? 0xc0006a : 0xe05a00);
            anncBx = UiKit.Rect(annc, "Bx");
            anncBx.anchorMin = anncBx.anchorMax = new Vector2(0.5f, 1); anncBx.pivot = new Vector2(0.5f, 0.5f);
            anncSc = vip ? 1 : 0.72f; anncTop = vip ? 112 : 92;
            anncCg = anncBx.gameObject.AddComponent<CanvasGroup>();
            var bstT = DataRoot.Texture("ui/play/" + (vip ? "vip" : "rich") + "_bst.png");
            if (bstT != null)
            {
                // 清書1109：トゲトゲと見出しは絵、金額は字（黄色・黒い太い縁取り）。大きさは 494×237.5（VIP は 494×256）
                float H = vip ? 256 : 237.5f;
                anncBx.sizeDelta = new Vector2(494, H);
                anncBx.anchoredPosition = new Vector2(0, -(anncTop + H / 2 * anncSc));
                foreach (var nm in new[] { "_bst", "_head" })
                {
                    var im = new GameObject(nm, typeof(RectTransform)).AddComponent<RawImage>();
                    im.rectTransform.SetParent(anncBx, false); UiKit.Stretch(im.rectTransform); im.raycastTarget = false;
                    im.texture = DataRoot.Texture("ui/play/" + (vip ? "vip" : "rich") + nm + ".png");
                }
                var amt = ComicUi.Label(anncBx, Game.Yen(a.Value), 66, Mats.Hex(0xffe45a), new Color(0, 0, 0, 0), 5.5f, Vector2.zero);
                var ar = amt.rectTransform; ar.anchorMin = ar.anchorMax = new Vector2(0, 1); ar.pivot = new Vector2(0.5f, 0.5f);
                ar.anchoredPosition = new Vector2(86.5f + 322 / 2f, -((vip ? 101 : 82.5f) + 84.5f / 2));
            }
            else
            {
                anncBx.sizeDelta = new Vector2(600, 132);
                anncBx.anchoredPosition = new Vector2(0, -(anncTop + 66 * anncSc));
                ComicUi.Burst(anncBx, 600, 270, 18, Mats.Hex(0xffd400), Color.white,
                              new ComicUi.Dots { bg = Color.white, dot = Mats.Hex(0xff4d6d), r = 3.1f, gap = 10 });
                var l1 = ComicUi.Label(anncBx, Lang.Tr(vip ? "♛ VIP オーダー出現！" : "高額オーダー出現！"), 44, Color.white, cc);
                l1.rectTransform.anchoredPosition = new Vector2(0, 42);
                var l2 = ComicUi.Label(anncBx, Game.Yen(a.Value) + (Lang.IsJa ? "！" : "!"), 74, Mats.Hex(0xffe14a), cc);
                l2.rectTransform.anchoredPosition = new Vector2(0, -25);
            }
            anncRot = (Random.value < 0.5f ? -1 : 1) * (3 + Random.value * 4);
            annc.gameObject.SetActive(true); annc.SetAsLastSibling();     // 料理の上の印より手前
            anncT = vip ? 2.2f : 1.7f; anncAge = 0;
            AnncAnim();
            Game.SeUi(vip ? "talk_open" : "buy");
        }

        /// <summary>出てくる（0.5 秒、回りながら 0.15 → 1）→ 脈打つ → 最後の 0.28 秒で大きくなって消える</summary>
        void AnncAnim()
        {
            if (anncBx == null) return; annc.SetAsLastSibling();
            float k = Mathf.Clamp01(anncAge / 0.5f), e = ComicUi.Bezier(k, 0.2f, 1.7f, 0.4f, 1);
            float rot = Mathf.LerpUnclamped(anncRot * 5, anncRot, e), sc = Mathf.LerpUnclamped(0.15f, 1, e), al = Mathf.Clamp01(e);
            if (anncAge > 0.5f)
            {
                float u = (anncAge - 0.5f) / 0.45f, ph = u - Mathf.Floor(u);
                if (((int)Mathf.Floor(u)) % 2 == 1) ph = 1 - ph;
                sc = 1 + 0.05f * (0.5f - 0.5f * Mathf.Cos(ph * Mathf.PI));
            }
            if (anncT < 0.28f)
            {
                float o = 1 - Mathf.Max(0, anncT) / 0.28f; o = o * o;
                sc = Mathf.Lerp(1, 1.6f, o); rot = anncRot; al = 1 - o;
            }
            anncBx.localScale = Vector3.one * sc * anncSc;
            anncBx.localRotation = Quaternion.Euler(0, 0, -rot);
            anncCg.alpha = al;
            float ks = Mathf.Clamp01(anncAge / 0.3f), es = 1 - Mathf.Pow(1 - ks, 3);
            anncSl.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1, es);
            var c = anncSl.color; c.a = 0.45f * es * (anncT < 0.28f ? Mathf.Max(0, anncT) / 0.28f : 1); anncSl.color = c;
        }

        // ------------------------------------------------------------ 売上の式
        public void SalePop(Vector3 at, string tag, float price, float[] ks, string[] names, float sale)
        {
            var r = UiKit.Rect(layer, "Sale"); r.sizeDelta = new Vector2(10, 10);
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(tag)) sb.Append(Lang.Tr(tag)).Append(" ");
            sb.Append(Game.Yen(price));
            for (int i = 0; i < ks.Length; i++)
            {
                // 1倍の倍率は出さない。状態と速達は毎回出す（連続は2連続から）
                bool always = names[i] == "状態" || names[i] == "速達";
                if (!always && Mathf.Abs(ks[i] - 1) < 0.001f) continue;
                sb.Append(" × ").Append(ks[i].ToString("0.0#")).Append("<size=13>").Append(Lang.Tr(names[i])).Append("</size>");
            }
            var f = Lbl(r, sb.ToString(), 17, Color.white, new Vector2(0, 18)); f.supportRichText = true;
            f.gameObject.AddComponent<Outline>().effectColor = INK;
            var a = Lbl(r, Game.Yen(sale), 34, GOLD, new Vector2(0, -10));
            var o = a.gameObject.AddComponent<Outline>(); o.effectColor = INK; o.effectDistance = new Vector2(2.5f, -2.5f);
            sales.Add(new Sale { r = r, f = f, a = a, w = at });
        }

        // ------------------------------------------------------------ 描き文字（「ドカッ！」など）。太い縁取りの字を少し傾けて弾ませる
        public void Comic(Vector3 w, string text, int px, int col)
        {
            var t = Lbl(layer, Lang.Tr(text), px, Color.white, Vector2.zero);
            t.fontStyle = FontStyle.Bold;
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = INK; o.effectDistance = new Vector2(3, -3);
            var o2 = t.gameObject.AddComponent<Outline>(); o2.effectColor = Mats.Hex(col); o2.effectDistance = new Vector2(5, -5);
            t.rectTransform.localRotation = Quaternion.Euler(0, 0, (Random.value - 0.5f) * 24);
            comics.Add(new ComicT { t = t, w = w });
        }

        // ------------------------------------------------------------ 奪ったお金
        public void CoinFly(Player from, Player to, float amt)
        {
            if (from == null || to == null || amt <= 0) return;
            to.lootFly += amt;
            int n = Mathf.Clamp(Mathf.RoundToInt(amt / 600), 4, 12);
            float rest = amt;
            for (int i = 0; i < n; i++)
            {
                float share = i == n - 1 ? rest : Mathf.Round(amt / n);
                rest -= share;
                var r = UiKit.Rect(layer, "Coin"); r.sizeDelta = new Vector2(26, 26);
                var im = r.gameObject.AddComponent<Image>(); im.sprite = Overhead.Round; im.type = Image.Type.Sliced; im.color = GOLD; im.raycastTarget = false;
                r.gameObject.AddComponent<Outline>().effectColor = Mats.Hex(0x7a4c00);
                Lbl(r, "¥", 16, Mats.Hex(0x7a4c00), Vector2.zero);
                r.gameObject.SetActive(false);
                coins.Add(new Coin { r = r, from = from, to = to, t0 = -i * 0.045f, share = share,
                                     s0 = new Vector2((Random.value - 0.5f) * 40, (Random.value - 0.5f) * 24), lift = 70 + Random.value * 60 });
            }
        }

        // ------------------------------------------------------------ 毎フレーム
        Vector2 Scr(Vector3 w, out bool vis)
        {
            var sp = Cam.WorldToScreenPoint(w);
            vis = sp.z > 0;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, sp, null, out var lp);
            return lp;
        }

        void Icon(object key, string kind, Vector3 w, string text)
        {
            seen.Add(key);
            Ic ic;
            if (!icons.TryGetValue(key, out ic))
            {
                var r = UiKit.Rect(layer, "OrdIc"); r.sizeDelta = new Vector2(10, 26);
                var bg = r.gameObject.AddComponent<Image>(); bg.sprite = Overhead.Round; bg.type = Image.Type.Sliced; bg.raycastTarget = false;
                var t = Lbl(r, "", 15, Color.white, Vector2.zero);
                icons[key] = ic = new Ic { r = r, bg = bg, t = t };
            }
            bool vis;
            var p = Scr(w, out vis);
            ic.r.gameObject.SetActive(vis);
            ic.r.anchoredPosition = p;
            if (ic.t.text != text) { ic.t.text = text; ic.r.sizeDelta = new Vector2(ic.t.preferredWidth + 18, 26); }
            ic.bg.color = kind == "rich" ? Mats.Hex(0xe05a00) : kind == "vipG" ? Mats.Hex(0xc0006a) : Mats.Hex(0xb8860b);
        }

        string IconText(Order o)
        {
            if (o == null) return null;
            if (o.vip) return "♛VIP" + (g.vipCur == o ? " " + Mathf.Max(0, Mathf.CeilToInt(o.left)) : "");
            // 高額は金額ではなく「高額オーダー」と出す（担当）
            if (o.rich) return Lang.Tr("高額オーダー");
            return null;
        }

        public void Tick(float dt)
        {
            if (Cam == null) return;
            seen.Clear();
            var o = g.vipCur;
            bool onStand = o != null && o.at == "stand" && g.vipPos != null;
            if (onStand) Icon("stand", "vip", g.vipPos.Value + Vector3.up * 1.45f, IconText(o));
            // 受取口に並ぶ皿。高額が並んだら、まん中に大きく知らせる（1つの注文に1回）
            for (int i = 0; i < g.CounterPlateCount; i++)
            {
                var od = g.oi + i < g.orders.Count ? g.orders[g.oi + i] : null;
                if (od != null && od.rich && !od.ann && !od.done && g.state == "play") { od.ann = true; Announce("rich", od.richAmt > 0 ? od.richAmt : g.T.richMin); }
                var tx = IconText(od);
                if (tx != null) Icon("cp" + i, od.vip ? "vip" : "rich", g.CounterPlateWorld(i) + Vector3.up * 0.42f, tx);
            }
            foreach (var P in g.players)
            {
                var tx = P.carried != null && !P.down ? IconText(P.carried.order) : null;
                if (tx != null) Icon(P, P.carried.order.vip ? "vip" : "rich", P.ent.transform.position + Vector3.up * 1.15f, tx);
            }
            foreach (var d in g.dropped)
            {
                var tx = IconText(d.order);
                if (tx != null && d.ent != null) Icon(d, d.order.vip ? "vip" : "rich", d.ent.transform.position + Vector3.up * 0.95f, tx);
            }
            foreach (var gu in g.guests)
                if (o != null && o.guest == gu && !gu.sunk && !gu.taken)
                    Icon(gu, "vipG", gu.transform.position + Vector3.up * (Mathf.Max(0.62f, gu.modelH - 0.52f + 0.22f) + 0.42f), "♛VIP");
            var gone = new List<object>();
            foreach (var k in icons.Keys) if (!seen.Contains(k)) gone.Add(k);
            foreach (var k in gone) { Object.Destroy(icons[k].r.gameObject); icons.Remove(k); }

            // まん中上の大きな知らせ
            if (anncT > 0)
            {
                anncT -= dt; anncAge += dt;
                AnncAnim();
                if (anncT <= 0) AnncNext();
            }
            // VIP の帯（清書1109 の絵）。出ている間は限定の人の札と残り秒数、終わったら成功（届けた人）／失敗（限定の人）の判子
            if (nT > 0) nT -= dt;
            string key = null; Player tagP = null; int sec = 0;
            if (anncT > 0) { }
            else if (o != null && g.state == "play") { key = "vipbar"; tagP = o.leader; sec = Mathf.Max(0, Mathf.CeilToInt(o.left)); }
            else if (nT > 0 && nKind != 0) { key = nKind == 1 ? "vipok" : "vipng"; tagP = nP; }
            bar.gameObject.SetActive(key != null);
            if (key != null)
            {
                if (key != barKey)
                {
                    barKey = key; barStT = 0;
                    barBg.texture = DataRoot.Texture("ui/play/" + key + ".png");
                    barStImg.gameObject.SetActive(key != "vipbar");
                    if (key != "vipbar") barStImg.texture = DataRoot.Texture("ui/play/" + key + "_st.png");
                }
                barTag.gameObject.SetActive(tagP != null);
                if (tagP != null) barTag.texture = DataRoot.Texture("ui/play/vtag" + (Mathf.Max(0, System.Array.IndexOf(Player.PCOL, tagP.col)) % 4 + 1) + ".png");
                barT.gameObject.SetActive(key == "vipbar");
                barT.text = sec.ToString();
                // 残り5秒からは秒数が脈打つ（1 ↔ 1.3、0.5 秒）
                barHurryT = key == "vipbar" && sec <= 5 ? barHurryT + dt : 0;
                float hp = barHurryT / 0.5f, ph = hp - Mathf.Floor(hp); if (((int)Mathf.Floor(hp)) % 2 == 1) ph = 1 - ph;
                barT.rectTransform.localScale = Vector3.one * (1 + 0.3f * (0.5f - 0.5f * Mathf.Cos(ph * Mathf.PI)));
                // 判子は押される（2.2 倍・−12° から、0.4 秒で弾んで止まる）
                if (key != "vipbar")
                {
                    barStT += dt;
                    float ks = Mathf.Clamp01(barStT / 0.4f), es = ComicUi.Bezier(ks, 0.2f, 1.8f, 0.4f, 1);
                    barSt.localScale = Vector3.one * Mathf.LerpUnclamped(2.2f, 1, es);
                    barSt.localRotation = Quaternion.Euler(0, 0, Mathf.LerpUnclamped(12, 0, es));
                    var sc = barStImg.color; sc.a = Mathf.Clamp01(es); barStImg.color = sc;
                }
            }
            else barKey = null;

            // 売上の式：1.6秒で上へ流れて消える
            for (int i = sales.Count - 1; i >= 0; i--)
            {
                var s = sales[i]; s.age += dt;
                if (s.age > 1.6f) { Object.Destroy(s.r.gameObject); sales.RemoveAt(i); continue; }
                bool vis; var p = Scr(s.w + Vector3.up * s.age * 0.5f, out vis);
                // 画面の端では内側へ寄せる
                var half = layer.rect.size / 2;
                p.x = Mathf.Clamp(p.x, -half.x + 120, half.x - 120);
                s.r.anchoredPosition = p; s.r.gameObject.SetActive(vis);
                float a = s.age < 1.2f ? 1 : 1 - (s.age - 1.2f) / 0.4f;
                var c1 = s.f.color; c1.a = a; s.f.color = c1;
                var c2 = s.a.color; c2.a = a; s.a.color = c2;
                s.r.localScale = Vector3.one * (s.age < 0.12f ? 0.6f + s.age / 0.12f * 0.4f : 1);
            }
            // 描き文字：0.85秒。出た瞬間に弾ませる
            for (int i = comics.Count - 1; i >= 0; i--)
            {
                var c = comics[i]; c.age += dt;
                if (c.age >= 0.85f) { Object.Destroy(c.t.gameObject); comics.RemoveAt(i); continue; }
                bool vis; var p = Scr(c.w + Vector3.up * c.age * 0.5f, out vis);
                c.t.rectTransform.anchoredPosition = p; c.t.gameObject.SetActive(vis);
                float s = c.age < 0.1f ? 0.4f + c.age / 0.1f * 0.9f : c.age < 0.18f ? 1.3f - (c.age - 0.1f) / 0.08f * 0.3f : 1;
                c.t.rectTransform.localScale = Vector3.one * s;
                var col = c.t.color; col.a = c.age > 0.6f ? Mathf.Max(0, (0.85f - c.age) / 0.25f) : 1; c.t.color = col;
            }
            // 奪ったお金：被害者 → 攻撃した人（0.56秒）→ 上の帯のその人の差引（0.48秒）
            for (int i = coins.Count - 1; i >= 0; i--)
            {
                var c = coins[i]; c.age += dt;
                float k = (c.age + c.t0) / 0.56f;
                if (k < 0) continue;
                c.r.gameObject.SetActive(true);
                bool vis;
                var a0 = Scr(c.from.ent.transform.position + Vector3.up * 0.9f, out vis) + c.s0;
                var b0 = Scr(c.to.ent.transform.position + Vector3.up * 0.9f, out vis);
                if (k < 1)
                {
                    var ctl = new Vector2((a0.x + b0.x) / 2, Mathf.Max(a0.y, b0.y) + c.lift);
                    c.r.anchoredPosition = Bez(a0, ctl, b0, k);
                    continue;
                }
                if (c.mid == null) c.mid = b0;
                var goal = g.hud.CardTotalPos(c.to, layer);
                float k2 = (k - 1) * 0.56f / 0.48f;
                if (goal == null || k2 >= 1)
                {
                    c.to.lootFly = Mathf.Max(0, c.to.lootFly - c.share);
                    Object.Destroy(c.r.gameObject); coins.RemoveAt(i); continue;
                }
                var m = c.mid.Value; var e = goal.Value;
                c.r.anchoredPosition = Bez(m, new Vector2(m.x, Mathf.Max(m.y, e.y) + 60), e, k2 * k2);
                c.r.localScale = Vector3.one * (1 - 0.35f * k2);
            }
        }
        static Vector2 Bez(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            return (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;
        }
        static string Who(Player P)
        {
            if (P == null) return "";
            return "<color=#" + ColorUtility.ToHtmlStringRGB(Mats.Hex(P.col)) + ">" + (P.idx + 1) + "P</color>";
        }
    }
}
