using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 上の帯（デザイン：ネコ配膳_プレイ画面清書1020.psd）。JS版の #hud2（updateHud2）を写したもの。
    /// 絵の部品は assets/ui/hud/（PSD のレイヤーを重ね直して切り出した物。位置は hud.json）。
    /// **座標は PSD の 1280×720 のまま**。人の札の中身は 1P の座標で書き、2P 以降は 263px ずつ右へずらす
    /// （下地の左端だけ色ごとに 1〜4px ずれているので、下地は色ごとの位置に置く）。
    /// 変わる物（数字・店名・HP・アイテムの数・運んでいる料理・順位）だけを、絵の上に字で書く。
    /// 重なる所は**前の人が上**。面の札はいちばん上。
    /// 帯は画面の幅と高さの小さいほうの倍率で縮める・広げる（JS版 h2Scale）。
    /// </summary>
    public class HudBar
    {
        readonly Game g;
        readonly RectTransform root, bar;
        const string UI = "ui/hud/";
        const float STEP = 263;                                   // 人の札の間隔（中身の並び）
        // 札の下地の絵の左端（色ごとに 1〜4px ずれている）
        static readonly float[] BG_X = { 191, 192, 194, 195 };
        // アイテムの絵の位置 [左, 上, 幅, 高さ, 絵の中の横位置]。items1〜4.png は17pxずつ3段（0 持っていない・1 選んでいる・2 持っている）
        // 2〜4 は同じ並び（5つ目から 1px ずれる）
        static readonly float[][][] IT = {
            new[] { new float[] { 223, 52, 15, 15, 0 }, new float[] { 245, 52, 18, 15, 15 }, new float[] { 268, 53, 21, 13, 33 }, new float[] { 294, 52, 17, 15, 54 }, new float[] { 317, 53, 21, 13, 71 }, new float[] { 345, 51, 13, 17, 92 }, new float[] { 364, 52, 15, 15, 105 }, new float[] { 385, 51, 17, 17, 120 } },
            new[] { new float[] { 223, 52, 15, 15, 0 }, new float[] { 245, 52, 18, 15, 15 }, new float[] { 268, 53, 21, 13, 33 }, new float[] { 294, 52, 17, 15, 54 }, new float[] { 317, 53, 22, 13, 71 }, new float[] { 345, 51, 13, 17, 93 }, new float[] { 364, 52, 15, 15, 106 }, new float[] { 385, 51, 17, 17, 121 } },
        };
        const float ITROW = 17;
        static readonly Color G = Mats.Hex(0x68ff00), NEG = Mats.Hex(0xff5a5a), YEL = Mats.Hex(0xffd400), DOWN = Mats.Hex(0xff4d4d);
        /// <summary>1人で遊ぶときに順位の場所へ出す文字（前の並びの名残。HudTop が使う）。</summary>
        public const string SOLO = "SOLO";

        // 面の札
        RectTransform sc;
        Text round, roundN, stageNm, done, total, dmg, dead, time;
        string nmShown;
        // 人の札
        readonly List<PCard> cards = new List<PCard>();
        string layoutKey = "";
        // コンボ
        Image comboCard; Text combo;
        // 耐久のバーの色違い（hue-rotate の代わりに、色相をずらした絵を作っておく）
        static readonly Dictionary<string, Texture2D> hpTex = new Dictionary<string, Texture2D>();

        class Roll { public int v, shown, from; public float t = -1, bump = -1; }
        class Tile { public RawImage im; public Text n; public string k; public float x, y, w, h, sx; public int row = -1; public float sc = 1; }
        class PCard
        {
            public Player P; public int ci; public RectTransform r;
            public RawImage panel, hp, rk, cl, pic; public Texture2D tex;
            public Text who, sales, cost, tot, dl, pz, em, downAr, downTx; public RectTransform down;
            public readonly List<Tile> tiles = new List<Tile>();
            public Roll rS, rT; public string hpK = ""; public int rkN = -1;
            public string key = ""; public Order obj; public float tier, downT = -1;
        }

        /// <summary>その人の札の差引の位置（layer の中）。札が無ければ null（奪ったお金が飛んでいく先）</summary>
        public Vector2? TotalPos(Player P, RectTransform layer)
        {
            var c = cards.Find(x => x.P == P);
            if (c == null || c.tot == null || !c.tot.gameObject.activeInHierarchy) return null;
            var r = c.tot.rectTransform.rect;
            var w = c.tot.rectTransform.TransformPoint(new Vector2(r.xMin + c.tot.preferredWidth / 2, r.center.y));
            return (Vector2)layer.InverseTransformPoint(w);
        }

        public HudBar(RectTransform root, Game game)
        {
            this.root = root; g = game;
            bar = UiKit.Rect(root, "TopBar2");
            bar.anchorMin = bar.anchorMax = new Vector2(0, 1); bar.pivot = new Vector2(0, 1);
            bar.anchoredPosition = Vector2.zero; bar.sizeDelta = new Vector2(1280, 130);

            // ---- 面の札。字の位置は PSD の字の箱から決めた（JS版の CSS のまま）
            sc = UiBoard.Box(bar, 0, 0, 236, 129, "Stage");
            UiBoard.Img(sc, UI + "stage.png", 0, 0, 236, 129);
            var ink = Mats.Hex(0x04141a);
            round = UiBoard.T(sc, "01", 31.5f, 3.7f, 21, ink, 2);
            roundN = UiBoard.T(sc, "", 45, 6, 11, ink);
            stageNm = UiBoard.T(sc, "—", 138, 17.5f, 18, Color.white, 2);
            done = UiBoard.T(sc, "", 236 - 189, 48.5f, 15.6f, G, 1);
            total = UiBoard.T(sc, "", 56, 48.5f, 15.6f, G);
            dmg = UiBoard.T(sc, "", 236 - 106, 48.5f, 15.6f, G, 1);
            dead = UiBoard.T(sc, "", 236 - 57, 48.5f, 15.6f, G, 1);
            time = UiBoard.T(sc, "", 236 - 57, 73.3f, 16.9f, G, 1);

            // ---- コンボ（帯の下に別の札で出す。JS版 #combo2）
            comboCard = UiKit.Card(bar, new Color(24 / 255f, 28 / 255f, 36 / 255f, 0.80f), new Color(1, 1, 1, 0.18f), 2, 15, "Combo");
            combo = UiKit.Label(comboCard.transform, "", 17, Color.white, true, TextAnchor.MiddleLeft);
            UiKit.Stretch(combo.rectTransform, 0); combo.rectTransform.offsetMin = new Vector2(12, 0);
            comboCard.gameObject.SetActive(false);
        }

        public void SetVisible(bool on)
        {
            if (bar.gameObject.activeSelf != on) bar.gameObject.SetActive(on);
        }

        public void Clear()
        {
            foreach (var c in cards) Object.Destroy(c.r.gameObject);
            cards.Clear(); layoutKey = "";
        }

        /// <summary>その人の札の色。ふだんは 1P 青・2P 赤・3P 緑・4P オレンジ。チーム戦はチームの色（JS版 h2ColOf）</summary>
        int ColOf(Player P, int slot)
        {
            if (g.mode == "team") return P.team != 0 ? 1 : 0;
            int i = System.Array.IndexOf(Player.PCOL, P.col);
            return i >= 0 ? i : slot % 4;
        }

        static Texture2D HpTex(int n, int lv)
        {
            string k = n + "/" + lv;
            Texture2D t;
            if (hpTex.TryGetValue(k, out t) && t != null) return t;
            var src = DataRoot.Texture(UI + "hp" + n + ".png");
            if (src == null) return null;
            if (lv == 0) t = src;
            else
            {
                // CSS の hue-rotate(-45deg) saturate(1.3) ／ hue-rotate(-85deg) saturate(1.6) のつもり
                float rot = lv == 1 ? -45 / 360f : -85 / 360f, sat = lv == 1 ? 1.3f : 1.6f;
                var px = src.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    float h, s, v; Color.RGBToHSV(px[i], out h, out s, out v);
                    h = Mathf.Repeat(h + rot, 1); s = Mathf.Min(1, s * sat);
                    var c = Color.HSVToRGB(h, s, v); c.a = px[i].a; px[i] = c;
                }
                t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(px); t.Apply(false);
            }
            hpTex[k] = t;
            return t;
        }

        /// <summary>字の拡大の中心を、字の左・上から 60% の所にする（CSS の transform-origin:0 60%）</summary>
        static void OriginLeft60(Text t)
        {
            var r = t.rectTransform;
            float h = r.sizeDelta.y;
            r.pivot = new Vector2(0, 0.4f);
            r.anchoredPosition += new Vector2(0, -0.6f * h);
        }

        // ------------------------------------------------------------ 人の札を組む
        PCard MakeCard(Player P, int slot)
        {
            int ci = ColOf(P, slot), n = ci + 1;
            var c = new PCard { P = P, ci = ci };
            c.r = UiBoard.Box(bar, slot * STEP, 0, 0, 0, "PCard_" + P.name);
            var panelTex = DataRoot.Texture(UI + "panel" + n + ".png");
            c.panel = UiBoard.Img(c.r, UI + "panel" + n + ".png", BG_X[ci], 0, panelTex != null ? panelTex.width : 300, 129);
            c.hp = UiBoard.Img(c.r, UI + "hp" + n + ".png", 303, 10, 134, 5);
            c.who = UiBoard.T(c.r, "", 257.5f, 4.3f, 16, Color.black, 2);
            c.sales = UiBoard.T(c.r, "", 240, 33.3f, 11, Color.white);
            c.cost = UiBoard.T(c.r, "", 298, 33.3f, 11, Color.white);
            c.tot = UiBoard.T(c.r, "", 356, 33.3f, 11, G);
            c.dl = UiBoard.T(c.r, "", 429, 33.3f, 11, Color.white);
            OriginLeft60(c.sales); OriginLeft60(c.tot);
            var it = IT[ci == 0 ? 0 : 1];
            var itTex = DataRoot.Texture(UI + "items" + n + ".png");
            for (int j = 0; j < Game.ITEM_ORDER.Length; j++)
            {
                var a = it[j];
                var t = new Tile { k = Game.ITEM_ORDER[j], x = a[0], y = a[1], w = a[2], h = a[3], sx = a[4] };
                t.im = UiBoard.Img(c.r, UI + "items" + n + ".png", t.x, t.y, t.w, t.h, "It_" + t.k);
                t.im.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                t.im.rectTransform.anchoredPosition = new Vector2(t.x + t.w / 2, -(t.y + t.h / 2));
                if (itTex != null) t.im.texture = itTex;
                t.n = UiBoard.T(c.r, "0", t.x + t.w / 2, 70.3f, 10.4f, Color.white, 2);
                c.tiles.Add(t);
            }
            // 運んでいる料理（料理名を出さずに絵だけ大きく。JS版 h2big）。空のときはクロッシュ
            c.cl = UiBoard.Img(c.r, UI + "cloche.png", 425, 69, 24, 15);
            c.tex = DishPic.NewTex();
            var pr = UiBoard.Box(c.r, 415, 46, 44, 44, "Pic");
            c.pic = pr.gameObject.AddComponent<RawImage>(); c.pic.texture = c.tex; c.pic.raycastTarget = false;
            c.pz = UiBoard.T(c.r, "", 437, 92.6f, 9, Mats.Hex(0xe5001b), 2);
            c.em = UiBoard.T(c.r, "", 437, 56, 8, Mats.Hex(0x4a5262), 2);
            c.em.lineSpacing = 1.15f;
            // 値段が下がったとき（▼ DOWN!）
            c.down = UiBoard.Box(c.r, 410, 60, 54, 30, "DishDown");
            c.downAr = UiBoard.T(c.down, "▼", 27, 0, 18, DOWN, 2);
            c.downTx = UiBoard.T(c.down, "DOWN!", 27, 18, 9, DOWN, 2);
            c.downTx.fontStyle = FontStyle.Italic;
            foreach (var t in new[] { c.downAr, c.downTx }) { UiBoard.Stroke(t, Color.white, 0.7f); t.gameObject.AddComponent<Shadow>().effectColor = Mats.Hex(0x5a0d0d); }
            c.down.gameObject.SetActive(false);
            // 順位（1人のときは出さない）
            c.rk = UiBoard.Img(c.r, UI + "rank1.png", 430, 0, 60, 57, "Rank");
            c.rk.gameObject.SetActive(false);
            return c;
        }

        // ------------------------------------------------------------ 毎フレーム
        public void Tick()
        {
            var PL = g.players;
            // 1280×720 のときの並びのまま、画面の大きさに合わせて縮める・広げる（幅と高さの小さいほうの倍率）
            var rr = root.rect;
            float s = rr.width > 0 ? Mathf.Min(rr.width / 1280f, rr.height / 720f) : 1;
            bar.localScale = new Vector3(s, s, 1);

            // ---- 面の札
            round.text = (g.runDone + (g.result != null ? 0 : 1)).ToString("00");
            roundN.text = g.IsMatch ? "/" + g.matchRounds.ToString("00") : "";
            string nm = Lang.Tr(string.IsNullOrEmpty(g.stage.n) ? "—" : g.stage.n);
            if (nm != nmShown)
            {
                nmShown = nm; stageNm.text = nm; stageNm.fontSize = 17;
                float pw = stageNm.preferredWidth;
                if (pw > 150) stageNm.fontSize = Mathf.Max(8, Mathf.FloorToInt(17 * 150 / pw));
            }
            int doneN = 0; foreach (var o in g.orders) if (o.done) doneN++;
            done.text = doneN.ToString("00");
            total.text = g.orders.Count.ToString("00");
            dmg.text = Mathf.RoundToInt(g.shopDmg).ToString();
            int deadN = 0; foreach (var gu in g.guests) if (gu.hp <= 0) deadN++;
            dead.text = deadN.ToString("00");
            {
                int tl = g.TimeLeftFrames();
                bool inf = tl < 0;
                time.text = inf ? "∞" : Game.FmtTime(tl);
                time.fontSize = inf ? 24 : 17;
                time.rectTransform.anchoredPosition = new Vector2(236 - 57, -(inf ? 68 : 73.3f) + time.fontSize * 0.12f);
                bool hurry = !inf && tl < 10 * 60 && g.result == null;
                bool rec = g.result != null && g.result.rec && inf;
                time.color = hurry ? Mats.Hex(0xff4b4b) : rec ? YEL : G;
            }

            // ---- コンボ
            string cb = HudTop.ComboText(g);
            comboCard.gameObject.SetActive(cb.Length > 0);
            if (cb.Length > 0)
            {
                combo.text = cb;
                var cr = comboCard.rectTransform;
                cr.anchorMin = cr.anchorMax = new Vector2(0, 1); cr.pivot = new Vector2(0, 1);
                cr.anchoredPosition = new Vector2(12, -134); cr.sizeDelta = new Vector2(combo.preferredWidth + 24, 30);
            }

            // ---- 人の札。器は人が変わったときだけ作り直す
            string key = "";
            for (int i = 0; i < PL.Count; i++) key += PL[i].GetHashCode() + ":" + ColOf(PL[i], i) + ",";
            if (key != layoutKey)
            {
                Clear(); layoutKey = key;
                for (int i = 0; i < PL.Count; i++) cards.Add(MakeCard(PL[i], i));
                // 前の人が上・面の札はいちばん上
                for (int i = cards.Count - 1; i >= 0; i--) cards[i].r.SetAsLastSibling();
                sc.SetAsLastSibling();
                comboCard.transform.SetAsLastSibling();
            }
            var RK = Ranks();
            foreach (var c in cards) TickCard(c, RK, PL.Count);
        }

        /// <summary>
        /// いまの順位（同じ値なら同じ順位）。JS版 h2Ranks。
        ///   個人戦 … 差引 ／ チーム戦 … チームの差引でチームの順位 ／ 協力 … 自分が売ったぶん（売上＋大暴れ）
        /// </summary>
        Dictionary<Player, int> Ranks()
        {
            return g.H2Ranks();
        }

        void TickCard(PCard c, Dictionary<Player, int> RK, int n)
        {
            var P = c.P; var W = g.WalletOf(P); var L = g.LedgerOf(P);
            float dt = Time.unscaledDeltaTime;
            // NPC は「NPC」（デザインの案のとおり）。人は 1P・2P…
            c.who.text = P.Label;
            // 耐久。残りが減っていく向き。半分を切ったら橙、1/4 を切ったら赤へ寄せる
            float left = P.down ? 0 : Mathf.Max(0, 100 - P.botDmg);
            c.hp.rectTransform.sizeDelta = new Vector2(134 * left / 100, 5);
            c.hp.uvRect = new Rect(0, 0, left / 100, 1);
            int lv = left > 50 ? 0 : left > 25 ? 1 : 2;
            string hk = (c.ci + 1) + "/" + lv;
            if (hk != c.hpK) { c.hpK = hk; var ht = HpTex(c.ci + 1, lv); if (ht != null) c.hp.texture = ht; }
            // 順位（1人のときは出さない）
            int rk = 0;
            if (n > 1) { RK.TryGetValue(P, out rk); rk = Mathf.Clamp(rk, 1, 4); }
            if (rk != c.rkN)
            {
                c.rkN = rk;
                c.rk.gameObject.SetActive(rk > 0);
                if (rk > 0) { var t = DataRoot.Texture(UI + "rank" + rk + ".png"); if (t != null) c.rk.texture = t; }
            }
            // お金：売上（大暴れも含む）・修理費（救急車・奪われた・クレーンも含む）・差引・配膳数
            // 奪ったお金は、飛んでいる間は足さない（着いたところで数え上がる）
            float fly = 0;
            if (g.mode == "team") { foreach (var q in g.players) if (q.team == P.team) fly += q.lootFly; }
            else fly = P.lootFly;
            RollTo(ref c.rS, c.sales, L.sales + L.wreck + L.lootIn - fly, v => Num(v), dt);
            c.cost.text = Num(L.repair + L.ambCost + L.lootOut + L.resc);
            RollTo(ref c.rT, c.tot, L.total - fly, v => (v < 0 ? "-" : "+") + Num(v), dt);
            c.tot.color = L.total - fly < 0 ? NEG : G;
            c.dl.text = P.delivered.ToString("00");

            // ---- アイテム
            g.FixSlot(P);
            foreach (var t in c.tiles)
            {
                var it = Game.ItemOf(t.k);
                int cnt = it.ammo > 0 ? (W.Has(it.k) > 0 ? W.ammo[it.k] : 0) : W.Has(it.k);
                t.n.text = cnt.ToString();
                bool own = cnt > 0, on = own && P.slot == it.k;
                int row = on ? 1 : own ? 2 : 0;
                if (row != t.row)
                {
                    t.row = row;
                    var tx = t.im.texture;
                    float tw = tx != null ? tx.width : 138, th = tx != null ? tx.height : 51;
                    t.im.uvRect = new Rect(t.sx / tw, 1 - (ITROW * row + t.h) / th, t.w / tw, t.h / th);
                    t.im.color = new Color(1, 1, 1, row == 0 ? 0.25f : 1);       // 持っていない物は透明度75%
                }
                t.n.color = on ? YEL : Color.white;
                t.sc = Mathf.MoveTowards(t.sc, on ? 1.28f : 1, dt * 0.28f / 0.12f);
                t.im.rectTransform.localScale = Vector3.one * t.sc;
            }

            // ---- 運んでいる料理（空のときはクロッシュ）
            var cd = P.carried;
            c.pic.gameObject.SetActive(cd != null); c.pz.gameObject.SetActive(cd != null);
            c.cl.gameObject.SetActive(cd == null && !P.down);
            c.em.gameObject.SetActive(cd == null && P.down);
            if (cd != null)
            {
                float tier = Dishes.PriceTier(cd.integ);
                string k = cd.dish.n + "/" + tier;
                if (k != c.key) { c.key = k; DishPic.Draw(c.tex, cd.dish, tier); }
                float price = cd.order != null ? g.OrdPrice(cd.order) : cd.dish.price * g.shop.price;
                c.pz.text = Mathf.RoundToInt(price * tier).ToString();
                if (cd.order == c.obj && tier < c.tier) c.downT = 0;          // 同じ注文の皿のまま段が下がった
                c.obj = cd.order; c.tier = tier;
            }
            else
            {
                c.key = "";
                if (P.down) c.em.text = P.revT > 0 ? Lang.Tr("復帰まで\n{0}", Mathf.CeilToInt(P.revT)) : Lang.Tr("リタイア");
            }
            // 値段が下がったときの演出（▼ DOWN! が落ちてきて、絵が揺れ、値段が跳ねる）
            if (c.downT >= 0)
            {
                c.downT += dt;
                float k = c.downT / 1.25f;
                if (k >= 1)
                {
                    c.downT = -1; c.down.gameObject.SetActive(false);
                    c.pic.rectTransform.anchoredPosition = new Vector2(415, -46); c.pz.rectTransform.localScale = Vector3.one;
                }
                else
                {
                    c.down.gameObject.SetActive(true);
                    float y = k < 0.14f ? -22 * (1 - k / 0.14f) : k < 0.24f ? -4 * (k - 0.14f) / 0.1f : k < 0.34f ? -4 * (1 - (k - 0.24f) / 0.1f)
                            : k < 0.75f ? 10 * (k - 0.34f) / 0.41f : 10 + 8 * (k - 0.75f) / 0.25f;
                    float a = k < 0.14f ? k / 0.14f : k < 0.75f ? 1 : 1 - (k - 0.75f) / 0.25f;
                    c.down.anchoredPosition = new Vector2(410, -60 - y);
                    c.down.localScale = Vector3.one * (k < 0.14f ? Mathf.Lerp(1.35f, 1, k / 0.14f) : 1);
                    foreach (var t in new[] { c.downAr, c.downTx }) { var cc = t.color; cc.a = a; t.color = cc; }
                    // 絵の揺れ（0.5秒）
                    float hk2 = c.downT / 0.5f;
                    float sx = hk2 >= 1 ? 0 : hk2 < 0.2f ? -5 * hk2 / 0.2f : hk2 < 0.4f ? Mathf.Lerp(-5, 4, (hk2 - 0.2f) / 0.2f)
                             : hk2 < 0.6f ? Mathf.Lerp(4, -2, (hk2 - 0.4f) / 0.2f) : Mathf.Lerp(-2, 0, (hk2 - 0.6f) / 0.4f);
                    c.pic.rectTransform.anchoredPosition = new Vector2(415 + sx, -46);
                    // 値段（0.9秒。前半で大きく）
                    float pk = c.downT / 0.9f;
                    c.pz.rectTransform.localScale = Vector3.one * (pk < 0.55f ? 1.12f : pk < 1 ? Mathf.Lerp(1.12f, 1, (pk - 0.55f) / 0.45f) : 1);
                }
            }

            // 倒れている間は札を暗く（CSS の grayscale(.75) brightness(.8) の代わり）
            var dim = P.down ? new Color(0.62f, 0.62f, 0.64f, 1) : Color.white;
            c.panel.color = dim; c.hp.color = dim;
            if (c.rk.gameObject.activeSelf) c.rk.color = dim;
        }

        /// <summary>
        /// 金額の数え上げ。**増えたときだけ**、前の額から 0.6 秒で数え上げて、字を少し大きくする（JS版 h2Roll）。
        /// 減ったとき（修理費・次の面）はそのまま替える
        /// </summary>
        static void RollTo(ref Roll st, Text el, float val, System.Func<int, string> fmt, float dt)
        {
            int v = Mathf.RoundToInt(val);
            if (st == null) { st = new Roll { v = v, shown = v }; el.text = fmt(v); return; }
            if (v != st.v)
            {
                bool up = v > st.v;
                st.v = v;
                if (!up) { st.shown = v; st.t = -1; el.text = fmt(v); }
                else { st.from = st.shown; st.t = 0; st.bump = 0; }
            }
            if (st.t >= 0)
            {
                st.t += dt;
                float k = Mathf.Min(1, st.t / 0.6f), e = 1 - Mathf.Pow(1 - k, 3);
                st.shown = Mathf.RoundToInt(st.from + (st.v - st.from) * e);
                el.text = fmt(st.shown);
                if (k >= 1) st.t = -1;
            }
            // 跳ねる（0.7秒：15% で 1.45、70% で 1.3、戻る）
            float s = 1;
            if (st.bump >= 0)
            {
                st.bump += dt;
                float b = st.bump / 0.7f;
                if (b >= 1) st.bump = -1;
                else s = b < 0.15f ? Mathf.Lerp(1, 1.45f, b / 0.15f) : b < 0.7f ? Mathf.Lerp(1.45f, 1.3f, (b - 0.15f) / 0.55f) : Mathf.Lerp(1.3f, 1, (b - 0.7f) / 0.3f);
            }
            el.rectTransform.localScale = Vector3.one * s;
        }

        /// <summary>6桁ぶんの欄に入れる数字（コンマは付けない。見本のとおり）。</summary>
        static string Num(float v) { return Mathf.RoundToInt(Mathf.Abs(v)).ToString(); }
    }
}
