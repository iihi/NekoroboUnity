using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 上の帯の新しい並び（担当からの案。**仮**で、デザインが来たら差し替える）。JS版の #hud2（updateHud2）を写したもの。
    ///
    ///   左の面の札：何回戦・店名 ／ 配膳数・損壊率・昇天数 ／ 経過時間
    ///   右へ人数ぶんの札：名前・耐久・順位 ／ 売上・修理費・差引・配膳数 ／ アイテム8種 ／ 運んでいる料理の名前・絵・今の値段
    ///
    /// NEXT は無く、前の上の帯と右の人ごとの札（HudTop）は、これ1本にまとめる。
    /// 設定パネルの「上の帯を新しい並び（試し）にする」を切ると、前の並び（HudTop）に戻る。
    /// 置き方は JS版の CSS の数字をそのまま使う（1280×720 で CSS の 1px ＝ ここの 1）。
    /// </summary>
    public class HudBar
    {
        readonly Game g;
        readonly RectTransform root, bar;
        static readonly Color INK = Mats.Hex(0x1d2433), SUB = Mats.Hex(0x4a5262), GREY = Mats.Hex(0x8a92a0);
        static readonly Color BLUE = Mats.Hex(0x1a9bd7), GOLD = Mats.Hex(0xf0b400);
        static readonly Color GREEN_D = Mats.Hex(0x2f9448), RED_D = Mats.Hex(0xc33f2c);
        /// <summary>1人で遊ぶときに順位の場所へ出す文字（試し。あとで変えるかもしれない）。JS版 H2_SOLO。</summary>
        public const string SOLO = "SOLO";

        // 面の札
        RectTransform sc;
        Text round, stageNm, done, dmg, dead, time;
        float scW = -1;
        // 人の札
        readonly List<PCard> cards = new List<PCard>();
        string layoutKey = "";
        // コンボ
        Image comboCard; Text combo;

        class Tile { public RectTransform r; public CanvasGroup cg; public Image on, onBg; public Text n; public string k; }
        class PCard
        {
            public Player P; public RectTransform r; public Image border, glow, glowRing; public CanvasGroup cg;
            public Text who, rkA, rkN, rkB, sales, cost, yen, tot, dl, dn, pz, em, down;
            public Image hpFill; public RectTransform hpBack, rkBox; public Text spark1, spark2;
            public ShineText shine; public Outline rkGlow;
            public RawImage pic; public Texture2D tex; public Image dishBorder;
            public readonly List<Tile> tiles = new List<Tile>();
            public string key = ""; public Order obj; public float tier, downT; public int rank = -1;
            public float w; public bool many;
        }

        public HudBar(RectTransform root, Game game)
        {
            this.root = root; g = game;
            bar = UiKit.Rect(root, "TopBar2");
            bar.anchorMin = bar.anchorMax = new Vector2(0, 1); bar.pivot = new Vector2(0, 1);
            bar.anchoredPosition = Vector2.zero; bar.sizeDelta = new Vector2(1280, 130);

            // ---- 面の札（幅は人数で変わるので、中身は左右のはしに付けて伸び縮みさせる）
            var c = UiKit.Card(bar, Color.white, BLUE, 3, 14, "Stage");
            sc = c.rectTransform;
            HLine(sc, 0, 1, 35, 0, 0);                         // 1段目の下
            HLine(sc, 0, 1, 75, 0, 0);                         // 2段目の下
            VLine(sc, 59, 3, 32);                              // 何回戦｜店名
            round = L(sc, "", 10, INK, true, TextAnchor.MiddleCenter);
            Place(round.rectTransform, 3, 5, 56, 30);
            stageNm = L(sc, "—", 13, INK, true, TextAnchor.MiddleCenter);
            At(stageNm.rectTransform, 0, 1, 3, 32, 67, 9);
            stageNm.horizontalOverflow = HorizontalWrapMode.Wrap; stageNm.verticalOverflow = VerticalWrapMode.Truncate;
            stageNm.resizeTextForBestFit = true; stageNm.resizeTextMinSize = 9; stageNm.resizeTextMaxSize = 13;
            // 2段目：配膳数（幅1.35）｜損壊率｜昇天数
            float[] cut = { 0, 1.35f / 3.35f, 2.35f / 3.35f, 1 };
            for (int i = 1; i < 3; i++) { var l = UiKit.Img(sc, BLUE, 0, "V"); AtV(l.rectTransform, cut[i], 37, 38); }
            string[] heads = { "配膳数", "損壊率", "昇天数" };
            var vals = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                float l = i == 0 ? 3 + 6 : 2 + 6, r = i == 2 ? 3 + 6 : 6;
                var h = L(sc, heads[i], 9, SUB);
                At(h.rectTransform, cut[i], cut[i + 1], 38, 13, l, r);
                vals[i] = L(sc, "", 17, INK, true, i == 0 ? TextAnchor.MiddleCenter : TextAnchor.MiddleRight);
                At(vals[i].rectTransform, cut[i], cut[i + 1], 51, 22, l, r);
            }
            done = vals[0]; dmg = vals[1]; dead = vals[2];
            // 3段目：経過時間
            var et = L(sc, "経過時間", 11, SUB);
            At(et.rectTransform, 0, 1, 77, 24, 3 + 12, 10);
            time = L(sc, "0:00.00", 19, INK, true, TextAnchor.MiddleRight);
            At(time.rectTransform, 0, 1, 77, 24, 3 + 12, 3 + 10);

            // ---- コンボ（帯の中には場所が無いので、左の札の下に別の札で出す）
            comboCard = UiKit.Card(bar, new Color(24 / 255f, 28 / 255f, 36 / 255f, 0.80f), new Color(1, 1, 1, 0.18f), 2, 15, "Combo");
            combo = L(comboCard.transform, "", 17, Color.white);
            UiKit.Stretch(combo.rectTransform, 0); combo.rectTransform.offsetMin = new Vector2(12, 0);
            comboCard.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ 置き方の小道具
        /// <summary>親の左右 ax0〜ax1 に付け、上から y の所に高さ h で置く（l・r は左右の内側への寄せ）。</summary>
        static void At(RectTransform r, float ax0, float ax1, float y, float h, float l, float rr)
        {
            r.anchorMin = new Vector2(ax0, 1); r.anchorMax = new Vector2(ax1, 1); r.pivot = new Vector2(0, 1);
            r.offsetMin = new Vector2(l, -(y + h)); r.offsetMax = new Vector2(-rr, -y);
        }
        /// <summary>親の左上から (x, y) に w×h で置く。</summary>
        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
        }
        static void HLine(RectTransform p, float ax0, float ax1, float y, float l, float r)
        {
            var im = UiKit.Img(p, BLUE, 0, "H"); At(im.rectTransform, ax0, ax1, y, 2, l + 3, r + 3);
        }
        static void VLine(RectTransform p, float x, float y, float h)
        {
            var im = UiKit.Img(p, BLUE, 0, "V"); Place(im.rectTransform, x, y, 2, h);
        }
        static void AtV(RectTransform r, float ax, float y, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(ax, 1); r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(0, -y); r.sizeDelta = new Vector2(2, h);
        }
        static Texture2D Tex(string k) { return Resources.Load<Texture2D>("Ui/Hud/" + k); }
        /// <summary>帯の字。12px 以上の太字は Thicken で太らせる（JS版は font-weight:800〜900）。</summary>
        static Text L(Transform p, string s, float size, Color c, bool bold = true, TextAnchor al = TextAnchor.MiddleLeft)
        {
            var t = UiKit.Label(p, s, size, c, bold, al);
            if (bold && size >= 12) t.gameObject.AddComponent<Thicken>();     // 小さい字は太らせるとつぶれる
            return t;
        }

        public void SetVisible(bool on)
        {
            if (bar.gameObject.activeSelf != on) bar.gameObject.SetActive(on);
        }

        public void Clear()
        {
            foreach (var c in cards) { Object.Destroy(c.r.gameObject); if (c.glow != null) Object.Destroy(c.glow.gameObject); }
            cards.Clear(); layoutKey = "";
        }

        // ------------------------------------------------------------ 人の札を組む
        PCard MakeCard(Player P, float x, float w, bool many)
        {
            var c = new PCard { P = P, w = w, many = many };
            var pc = Mats.Hex(P.col);
            // 1位の光（札の後ろに、少し大きい金の板とふち）
            c.glow = UiKit.Img(bar, new Color(1f, 0.75f, 0f, 0.35f), 18, "Glow");
            Place(c.glow.rectTransform, x - 5, 8 - 5, w + 10, 104 + 10);
            c.glowRing = UiKit.Img(c.glow.transform, new Color(1f, 0.8f, 0.1f, 0.55f), 0, "Ring");
            c.glowRing.sprite = UiKit.Ring(Mathf.RoundToInt(2 * 15.5f / 16)); c.glowRing.type = Image.Type.Sliced;
            c.glowRing.pixelsPerUnitMultiplier = 15.5f / 16;
            UiKit.Stretch(c.glowRing.rectTransform, 3);
            c.glow.gameObject.SetActive(false);

            var card = UiKit.Card(bar, Color.white, pc, 3, 14, "PCard_" + P.name);
            c.r = card.rectTransform; Place(c.r, x, 8, w, 104);
            c.border = card.transform.Find("Border").GetComponent<Image>();
            c.cg = card.gameObject.AddComponent<CanvasGroup>();
            float dishW = many ? 47 : 76, gap = many ? 4 : 7;
            float lx = many ? 3 + 7 : 3 + 9, rx = 3 + (many ? 5 : 6);
            float dishX = w - rx - dishW, lw = dishX - gap - lx;

            // ---- 1段目：名前・耐久・順位
            c.who = L(c.r, "", 12, INK);
            Place(c.who.rectTransform, lx, 8, 80, 26);
            // 順位（右から「位」「数字」「順位」）
            c.rkBox = UiKit.Rect(c.r, "Rank");
            c.rkA = L(c.rkBox, "順位", 10, SUB, true, TextAnchor.LowerLeft);
            c.rkN = L(c.rkBox, "1", many ? 20 : 25, GREY, true, TextAnchor.LowerCenter);
            c.shine = c.rkN.gameObject.AddComponent<ShineText>();
            c.rkGlow = c.rkN.gameObject.AddComponent<Outline>();
            c.rkB = L(c.rkBox, "位", 10, SUB, true, TextAnchor.LowerLeft);
            c.spark1 = L(c.rkBox, "✦", 10, Mats.Hex(0xffc400), true, TextAnchor.MiddleCenter);
            c.spark2 = L(c.rkBox, "✦", 8, Mats.Hex(0xffc400), true, TextAnchor.MiddleCenter);
            // 耐久のバー（濃いふち 1.5px・白地・中身）
            var hpB = UiKit.Img(c.r, INK, 3, "Hp");
            c.hpBack = hpB.rectTransform;
            var hpIn = UiKit.Img(c.hpBack, Color.white, 2, "In"); UiKit.Stretch(hpIn.rectTransform, 1.5f);
            Image track;
            c.hpFill = UiKit.Bar(hpIn.transform, new Color(0, 0, 0, 0), Mats.Hex(0x4cc35a), out track);
            UiKit.Stretch(track.rectTransform);

            // ---- 2段目：売上・修理費・差引（6桁ぶんの幅を取る）・配膳数
            {
                float fs = many ? 9 : 12, ic = many ? 11 : 17, ih = many ? 10 : 15, gp = many ? 2 : 6, x0 = lx, y0 = 35, h = 18;
                float digit = 0.62f * fs;
                var coin = UiKit.Rect(c.r, "Coin").gameObject.AddComponent<RawImage>(); coin.texture = Tex("h2_coin"); coin.raycastTarget = false;
                Place(coin.rectTransform, x0, y0 + (h - ih) / 2, ic, ih); x0 += ic + (many ? 1 : 2);
                c.sales = L(c.r, "0", fs, INK); Place(c.sales.rectTransform, x0, y0, digit * 6, h); x0 += digit * 6 + gp;
                var wr = UiKit.Rect(c.r, "Wrench").gameObject.AddComponent<RawImage>(); wr.texture = Tex("h2_wrench"); wr.raycastTarget = false;
                Place(wr.rectTransform, x0, y0 + (h - ih) / 2, ic, ih); x0 += ic + (many ? 1 : 2);
                c.cost = L(c.r, "0", fs, INK); Place(c.cost.rectTransform, x0, y0, digit * 6, h); x0 += digit * 6 + gp;
                c.yen = L(c.r, "¥", fs, INK); Place(c.yen.rectTransform, x0, y0, fs * 0.62f, h); x0 += fs * 0.62f + (many ? 1 : 2);
                c.tot = L(c.r, "0", fs, INK); Place(c.tot.rectTransform, x0, y0, digit * 7, h);
                c.dl = L(c.r, "", many ? 12 : 15, INK, true, TextAnchor.MiddleRight);
                Place(c.dl.rectTransform, lx, y0, lw, h);
            }

            // ---- 3段目：アイテム8種（いつも同じ並び）
            {
                float tw = (lw - 7 * 2) / 8f, ty = 54, th = 42;
                float isz = many ? 16 : 22, cs = many ? 11 : 13;
                for (int i = 0; i < Game.ITEM_ORDER.Length; i++)
                {
                    var t = new Tile { k = Game.ITEM_ORDER[i] };
                    t.r = UiKit.Rect(c.r, "It_" + t.k);
                    Place(t.r, lx + i * (tw + 2), ty, tw, th);
                    t.cg = t.r.gameObject.AddComponent<CanvasGroup>();
                    t.onBg = UiKit.Img(t.r, new Color(GOLD.r, GOLD.g, GOLD.b, 0.10f), many ? 5 : 7, "OnBg"); UiKit.Stretch(t.onBg.rectTransform);
                    t.on = UiKit.Img(t.r, GOLD, 0, "On");
                    float rad = many ? 5 : 7;
                    t.on.sprite = UiKit.Ring(Mathf.RoundToInt(2 * 15.5f / rad)); t.on.type = Image.Type.Sliced;
                    t.on.pixelsPerUnitMultiplier = 15.5f / rad;
                    UiKit.Stretch(t.on.rectTransform);
                    var ico = UiKit.Rect(t.r, "Icon").gameObject.AddComponent<RawImage>();
                    ico.texture = Tex("h2_" + t.k); ico.raycastTarget = false;
                    ico.rectTransform.anchorMin = ico.rectTransform.anchorMax = new Vector2(0.5f, 1); ico.rectTransform.pivot = new Vector2(0.5f, 1);
                    ico.rectTransform.anchoredPosition = new Vector2(0, -3); ico.rectTransform.sizeDelta = new Vector2(isz, isz);
                    t.n = L(t.r, "0", cs, INK, true, TextAnchor.MiddleCenter);
                    t.n.rectTransform.anchorMin = new Vector2(0, 0); t.n.rectTransform.anchorMax = new Vector2(1, 0); t.n.rectTransform.pivot = new Vector2(0.5f, 0);
                    t.n.rectTransform.anchoredPosition = new Vector2(0, 2); t.n.rectTransform.sizeDelta = new Vector2(0, cs + 2);
                    c.tiles.Add(t);
                }
            }

            // ---- 右の箱：料理の名前・絵・今の値段
            {
                var box = UiKit.Img(c.r, new Color(1, 1, 1, 0), 0, "Dish");
                Place(box.rectTransform, dishX, 8, dishW, 88);
                c.dishBorder = UiKit.Img(box.transform, pc, 0, "Border");
                c.dishBorder.sprite = UiKit.Ring(Mathf.RoundToInt(2 * 15.5f / 10)); c.dishBorder.type = Image.Type.Sliced;
                c.dishBorder.pixelsPerUnitMultiplier = 15.5f / 10;
                UiKit.Stretch(c.dishBorder.rectTransform);
                float ps = many ? 36 : 52;
                c.dn = L(box.transform, "", many ? 8 : 10, INK, true, TextAnchor.MiddleCenter);
                At(c.dn.rectTransform, 0, 1, (88 - ps) / 2 - 15, 13, 3, 3);
                c.tex = DishPic.NewTex();
                c.pic = UiKit.Rect(box.transform, "Pic").gameObject.AddComponent<RawImage>();
                c.pic.texture = c.tex; c.pic.raycastTarget = false;
                c.pic.rectTransform.anchorMin = c.pic.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                c.pic.rectTransform.sizeDelta = new Vector2(ps, ps); c.pic.rectTransform.anchoredPosition = new Vector2(0, -1);
                c.pz = L(box.transform, "", many ? 10 : 11, INK, true, TextAnchor.MiddleCenter);
                At(c.pz.rectTransform, 0, 1, (88 + ps) / 2 + 1, 13, 2, 2);
                c.em = L(box.transform, "手ぶら", 10, GREY, true, TextAnchor.MiddleCenter);
                UiKit.Stretch(c.em.rectTransform);
                c.down = L(box.transform, "▼\nDOWN!", many ? 10 : 13, Mats.Hex(0xff4d4d), true, TextAnchor.MiddleCenter);
                c.down.lineSpacing = 0.8f;
                UiKit.Stretch(c.down.rectTransform);
                c.down.gameObject.AddComponent<Outline>().effectColor = Color.white;
                c.down.gameObject.SetActive(false);
            }
            return c;
        }

        // ------------------------------------------------------------ 毎フレーム
        public void Tick()
        {
            var PL = g.players;
            bool many = PL.Count > 2;
            // 窓が1280より狭いときは、1280のときの並びのまま全体を縮める
            float rootW = root.rect.width;
            float s = Mathf.Min(1f, rootW / 1280f);
            float W = rootW / s;
            bar.localScale = new Vector3(s, s, 1);
            bar.sizeDelta = new Vector2(W, 130);

            // ---- 面の札
            float lw = many ? 198 : 220;
            if (scW != lw) { scW = lw; Place(sc, 12, 8, lw, 104); }
            round.text = "<size=21>" + (g.runDone + (g.result != null ? 0 : 1)) + "</size><size=10>回戦</size>";
            stageNm.text = g.stage.n ?? "—";
            int doneN = 0; foreach (var o in g.orders) if (o.done) doneN++;
            done.text = doneN + "<size=13><color=#4a5262> / </color></size>" + g.orders.Count;
            dmg.text = Mathf.RoundToInt(g.shopDmg) + "<size=11>%</size>";
            int deadN = 0; foreach (var gu in g.guests) if (gu.hp <= 0) deadN++;
            dead.text = deadN + "<size=11>人</size>";
            time.text = Game.FmtTime(g.frames);
            time.color = g.result != null && g.result.rec ? Mats.Hex(0xd18f10) : INK;

            // ---- コンボ
            string cb = HudTop.ComboText(g);
            comboCard.gameObject.SetActive(cb.Length > 0);
            if (cb.Length > 0)
            {
                combo.text = cb;
                Place(comboCard.rectTransform, 12, 120, combo.preferredWidth + 24, 30);
            }

            // ---- 人の札。並び（人数・幅）が変わったときだけ作り直す
            int n = PL.Count;
            float cw = n > 0 ? (W - 24 - lw - 8 * n) / n : 0;
            if (!many) cw = Mathf.Min(372, cw);
            cw = Mathf.Floor(cw);
            string key = n + "/" + cw + "/" + many;
            for (int i = 0; i < n; i++) key += "/" + PL[i].GetHashCode();
            if (key != layoutKey)
            {
                Clear(); layoutKey = key;
                for (int i = 0; i < n; i++) cards.Add(MakeCard(PL[i], 12 + lw + 8 + i * (cw + 8), cw, many));
            }
            var RK = Ranks();
            foreach (var c in cards) TickCard(c, RK, n);
        }

        /// <summary>
        /// いまの順位（同じ値なら同じ順位）。JS版 h2Ranks。
        ///   個人戦 … 差引 ／ チーム戦 … チームの差引でチームの順位 ／ 協力 … 自分が売ったぶん（売上＋大暴れ）
        /// </summary>
        Dictionary<Player, int> Ranks()
        {
            var PL = g.players;
            var val = new Dictionary<Player, float>();
            foreach (var P in PL) val[P] = g.mode == "coop" ? P.sales + P.wreck : g.LedgerOf(P).total;
            var pool = new List<float>();
            if (g.mode == "team") foreach (var t in g.TeamsInPlay()) pool.Add(g.LedgerOf(g.TeamLead(t)).total);
            else foreach (var P in PL) pool.Add(val[P]);
            var r = new Dictionary<Player, int>();
            foreach (var P in PL)
            {
                int k = 1; foreach (var v in pool) if (v > val[P] + 1e-4f) k++;
                r[P] = k;
            }
            return r;
        }

        static readonly Color[] GOLD_C = { Mats.Hex(0xc27c00), Mats.Hex(0xffcc1a), Mats.Hex(0xfffbe0), Mats.Hex(0xffcc1a), Mats.Hex(0xc27c00) };
        static readonly Color[] SILVER_C = { Mats.Hex(0x7d8794), Mats.Hex(0xcfd6de), Color.white, Mats.Hex(0xcfd6de), Mats.Hex(0x7d8794) };
        static readonly Color[] BRONZE_C = { Mats.Hex(0x8a4f22), Mats.Hex(0xd58c4f), Mats.Hex(0xf3c49a), Mats.Hex(0xd58c4f), Mats.Hex(0x8a4f22) };
        static readonly float[] AT_GS = { 0, 0.35f, 0.5f, 0.65f, 1 }, AT_B = { 0, 0.4f, 0.5f, 0.6f, 1 };

        void TickCard(PCard c, Dictionary<Player, int> RK, int n)
        {
            var P = c.P; var W = g.WalletOf(P); var L = g.LedgerOf(P);
            bool many = c.many;
            float lx = many ? 3 + 7 : 3 + 9;
            // NPC は強さだけ添える（何台もいると見分けが付かないため）。キー・パッドの別は出さない
            c.who.text = P.Label + (P.src.kind == "npc" ? " <size=9><color=#8a92a0>" + NpcLevels.Get(P.src.lv).name + "</color></size>" : "");
            float whoW = c.who.preferredWidth;
            c.who.rectTransform.sizeDelta = new Vector2(whoW + 2, 26);

            // ---- 順位（1人なら SOLO。光らせない）
            int rk; RK.TryGetValue(P, out rk); if (rk < 1) rk = 1;
            bool solo = n == 1;
            int key = solo ? 0 : Mathf.Min(4, rk);
            if (c.rank != key || c.rkN.text != (solo ? SOLO : rk.ToString()))
            {
                c.rank = key;
                c.rkN.text = solo ? SOLO : rk.ToString();
                c.rkA.gameObject.SetActive(!solo); c.rkB.gameObject.SetActive(!solo);
                c.rkN.fontSize = solo ? 21 : (many ? 20 : 25);
                c.rkN.color = Color.white;
                c.rkGlow.enabled = true;
                switch (key)
                {
                    case 1: c.shine.enabled = true; c.shine.Set(GOLD_C, AT_GS, 1.6f);
                        c.rkGlow.effectColor = new Color(1f, 0.75f, 0f, 0.75f); c.rkGlow.effectDistance = new Vector2(1.5f, -1.5f); break;
                    case 2: c.shine.enabled = true; c.shine.Set(SILVER_C, AT_GS, 2.6f);
                        c.rkGlow.effectColor = new Color(0.35f, 0.38f, 0.43f, 0.8f); c.rkGlow.effectDistance = new Vector2(1, -1); break;
                    case 3: c.shine.enabled = true; c.shine.Set(BRONZE_C, AT_B, 0, 0.5f);
                        c.rkGlow.effectColor = new Color(0.37f, 0.2f, 0.08f, 0.8f); c.rkGlow.effectDistance = new Vector2(1, -1); break;
                    default:
                        c.shine.enabled = false; c.rkGlow.enabled = false;
                        c.rkN.color = solo ? SUB : GREY; break;
                }
            }
            // 並べ直す（右から「位」「数字」「順位」）
            float bW = solo ? 0 : c.rkB.preferredWidth, nW = Mathf.Max(c.rkN.preferredWidth, c.rkN.fontSize * 0.7f), aW = solo ? 0 : c.rkA.preferredWidth;
            float rkW = aW + 2 + nW + 2 + bW;
            float lw = c.w - (many ? 3 + 5 : 3 + 6) - (many ? 47 : 76) - (many ? 4 : 7) - lx;
            float rkX = lx + lw - rkW;
            Place(c.rkBox, rkX, 8, rkW, 26);
            Place(c.rkA.rectTransform, 0, 0, aW, 22);
            Place(c.rkN.rectTransform, aW + 2, -2, nW, 26);
            Place(c.rkB.rectTransform, aW + 2 + nW + 2, 0, bW, 22);
            // 1位はまわりのきらめきと、札の金の光
            bool sparkle = key == 1;
            c.spark1.gameObject.SetActive(sparkle); c.spark2.gameObject.SetActive(sparkle);
            if (sparkle)
            {
                float t = Time.unscaledTime;
                Sparkle(c.spark1, rkW - 1, -1, t / 1.2f);
                Sparkle(c.spark2, aW + nW * 0.9f, 24, t / 1.2f + 0.5f);
            }
            c.glow.gameObject.SetActive(key == 1 && n > 1);
            // 耐久のバー（名前と順位のあいだ）
            float hx = lx + whoW + 7, hw = Mathf.Max(30, rkX - 7 - hx);
            Place(c.hpBack, hx, 8 + (26 - (many ? 11 : 13)) / 2f, hw, many ? 11 : 13);
            float left = Mathf.Max(0, 100 - P.botDmg);
            UiKit.SetBar(c.hpFill, P.down ? 0 : left / 100f);
            c.hpFill.color = Mats.Hex(left > 50 ? 0x4cc35a : left > 25 ? 0xffa726 : 0xe53935);

            // ---- お金と配膳数
            c.sales.text = Num(L.sales + L.wreck);
            c.cost.text = Num(L.repair + L.ambCost);
            c.tot.text = (L.total < 0 ? "-" : "") + Num(L.total);
            var tc = L.total >= 0 ? GREEN_D : RED_D;
            c.tot.color = tc; c.yen.color = tc;
            c.dl.text = "<size=" + (many ? 8 : 10) + "><color=#4a5262>配膳数</color></size> " + P.delivered;

            // ---- アイテム
            g.FixSlot(P);
            foreach (var t in c.tiles)
            {
                var it = Game.ItemOf(t.k);
                int cnt = it.ammo > 0 ? (W.Has(it.k) > 0 ? W.ammo[it.k] : 0) : W.Has(it.k);
                t.n.text = cnt.ToString();
                t.cg.alpha = cnt > 0 ? 1 : 0.2f;
                bool on = cnt > 0 && P.slot == it.k;
                t.on.enabled = on; t.onBg.enabled = on;
            }

            // ---- 運んでいる料理
            var cd = P.carried;
            c.pic.gameObject.SetActive(cd != null); c.pz.gameObject.SetActive(cd != null); c.dn.gameObject.SetActive(cd != null);
            c.em.gameObject.SetActive(cd == null);
            if (cd != null)
            {
                float tier = Dishes.PriceTier(cd.integ);
                string k = cd.dish.n + "/" + tier;
                if (k != c.key) { c.key = k; DishPic.Draw(c.tex, cd.dish, tier); }
                c.dn.text = cd.dish.n;
                c.pz.text = Mathf.RoundToInt(cd.dish.price * g.shop.price * tier).ToString();   // 届けたらいくらになるか（コンボは入れない）
                var col = DishPic.TierCol(tier);
                c.pz.color = tier >= 1 ? INK : (UiKit.Hex(col) == "#FFD24A" ? Mats.Hex(0xc98a00) : col);
                if (cd.order == c.obj && tier < c.tier) c.downT = 1.25f;          // 同じ注文の皿のまま段が下がった
                c.obj = cd.order; c.tier = tier;
            }
            else
            {
                c.key = "";
                c.em.text = P.down ? (P.revT > 0 ? "復帰まで\n" + Mathf.CeilToInt(P.revT) : "リタイア") : "手ぶら";
            }
            // 値段が下がったときの演出（▼ DOWN! が落ちてきて、箱のふちが赤くなって揺れる）
            if (c.downT > 0)
            {
                c.downT -= Time.deltaTime;
                float k = 1 - c.downT / 1.25f;
                c.down.gameObject.SetActive(true);
                c.down.rectTransform.anchoredPosition = new Vector2(0, k < 0.14f ? 22 * (1 - k / 0.14f) : -18 * Mathf.Max(0, (k - 0.34f) / 0.66f));
                var dc = c.down.color; dc.a = k < 0.14f ? k / 0.14f : k < 0.75f ? 1 : 1 - (k - 0.75f) / 0.25f; c.down.color = dc;
                c.dishBorder.color = Color.Lerp(Mats.Hex(0xff4d4d), Mats.Hex(P.col), Mathf.Clamp01(k * 2.5f));
                float sx = k < 0.4f ? Mathf.Sin(k * 40) * 5 * (1 - k / 0.4f) : 0;
                c.dishBorder.rectTransform.anchoredPosition = new Vector2(sx, 0);
            }
            else { c.down.gameObject.SetActive(false); c.dishBorder.color = Mats.Hex(P.col); c.dishBorder.rectTransform.anchoredPosition = Vector2.zero; }

            c.cg.alpha = P.down ? 0.6f : 1f;
        }

        static void Sparkle(Text t, float x, float y, float ph)
        {
            float k = ph - Mathf.Floor(ph);
            float a = Mathf.Sin(k * Mathf.PI);                          // 0 → 1 → 0
            var col = t.color; col.a = a; t.color = col;
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(12, 12);
            r.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, a);
            r.localEulerAngles = new Vector3(0, 0, 45 * k);
        }

        /// <summary>6桁ぶんの欄に入れる数字（コンマは付けない。見本のとおり）。</summary>
        static string Num(float v) { return Mathf.RoundToInt(Mathf.Abs(v)).ToString(); }
    }
}
