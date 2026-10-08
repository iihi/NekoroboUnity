using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 上の帯と右の人ごとの札。JS版の #topbar と #pcards（updateHUD / updatePlayerCards）を写したもの。
    ///
    ///   上の帯（高さ78px。カメラの自動調整がこのぶんを空けている）
    ///     面の名前・店と摩擦・配膳と所持金 ／ 店舗ダメージ ／ NEXT ／ コンボ ／（空き）／ タイム ／ お金
    ///   右の札（人ごと）
    ///     名前と操作 ／ 運んでいる料理の絵と今の値段（手ぶら）／ アイテム ／ 配った数と所持金
    /// </summary>
    public class HudTop
    {
        readonly Game g;
        static readonly Color CARD = new Color(24 / 255f, 28 / 255f, 36 / 255f, 0.80f);
        static readonly Color CARD_B = new Color(1, 1, 1, 0.18f);
        static readonly Color SUB = Mats.Hex(0xb6bdc8);
        static readonly Color INK = Mats.Hex(0x3a352c), INK_SUB = Mats.Hex(0x8a8274);
        static readonly Color GREEN_D = Mats.Hex(0x2f9448), RED_D = Mats.Hex(0xc33f2c), GOLD_D = Mats.Hex(0xd18f10);

        Text stageName, shopSub, progress, shopTxt, timeTxt, mSales, mWreck, mAmb, mCost, mTotal, combo;
        Image shopFill;
        GameObject comboCard;
        readonly List<NextItem> next = new List<NextItem>();
        RectTransform pcards;
        readonly List<PCard> cards = new List<PCard>();

        class NextItem { public GameObject go; public CanvasGroup cg; public Image dish; public Text name; }
        class PCard
        {
            public Player P; public GameObject go; public Image border; public Text nm, src, dn, bag, dl, cash, down;
            public RawImage pic; public Texture2D tex; public string key = ""; public Order obj; public float tier; public float downT;
        }

        public HudTop(RectTransform root, Game game)
        {
            g = game;
            // ---- 上の帯
            var bar = UiKit.Rect(root, "TopBar");
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(0, 78); bar.anchoredPosition = Vector2.zero;
            UiKit.HRow(bar.gameObject, 9, UiKit.Pad(12, 12, 0, 0));

            // 面の名前と店
            var c1 = UiKit.Card(bar, CARD, CARD_B, 2, 15, "Store");
            UiKit.VCol(c1.gameObject, 2, UiKit.Pad(12, 12, 9, 9));
            stageName = UiKit.Label(c1.transform, "—", 17, Color.white);
            shopSub = UiKit.Label(c1.transform, "", 11, SUB);
            progress = UiKit.Label(c1.transform, "", 11, SUB);

            // 店舗ダメージ
            var c2 = UiKit.Card(bar, CARD, CARD_B, 2, 15, "Bars");
            UiKit.HRow(c2.gameObject, 8, UiKit.Pad(12, 12, 9, 9));
            Image track;
            shopFill = UiKit.Bar(c2.transform, Mats.Hex(0x2b313c), Mats.Hex(0xff8a00), out track);
            UiKit.Size(track, 130, 13);
            shopTxt = UiKit.Label(c2.transform, "", 12, Color.white);

            // NEXT
            var c3 = UiKit.Card(bar, CARD, CARD_B, 2, 15, "Next");
            UiKit.HRow(c3.gameObject, 10, UiKit.Pad(12, 12, 5, 5));
            UiKit.Label(c3.transform, "NEXT", 11, Mats.Hex(0x5f6672), false);
            for (int i = 0; i < 3; i++)
            {
                var it = new NextItem();
                var col = UiKit.Rect(c3.transform, "Dish" + i);
                UiKit.VCol(col.gameObject, 3, null, TextAnchor.UpperCenter);
                it.cg = col.gameObject.AddComponent<CanvasGroup>();
                it.cg.alpha = i == 0 ? 1 : 0.45f;
                // 皿：白い楕円に青いふち、まん中に料理の色
                var plate = UiKit.Rect(col, "Plate");
                UiKit.Size(plate, 30, 22);
                var rim = UiKit.Img(plate, Mats.Hex(0x3d6fb5)); rim.sprite = UiKit.Ellipse; UiKit.Stretch(rim.rectTransform);
                var wh = UiKit.Img(plate, Color.white); wh.sprite = UiKit.Ellipse; UiKit.Stretch(wh.rectTransform, 2);
                it.dish = UiKit.Img(plate, Color.gray); it.dish.sprite = UiKit.Ellipse;
                it.dish.rectTransform.sizeDelta = new Vector2(16, 11);
                it.name = UiKit.Label(col, "", 11, Mats.Hex(0xc9ced6), true, TextAnchor.MiddleCenter);
                it.go = col.gameObject;
                next.Add(it);
            }

            // コンボ（NEXT とは別の札）
            var c4 = UiKit.Card(bar, CARD, CARD_B, 2, 15, "Combo");
            UiKit.HRow(c4.gameObject, 14, UiKit.Pad(12, 12, 8, 8));
            combo = UiKit.Label(c4.transform, "", 17, Color.white);
            comboCard = c4.gameObject;

            // あいだを空ける（タイムとお金は右端）
            var sp = UiKit.Rect(bar, "Spacer");
            UiKit.Size(sp, 0, 1, 1);

            // タイム
            var c5 = UiKit.Card(bar, CARD, CARD_B, 2, 15, "Timer");
            UiKit.HRow(c5.gameObject, 0, UiKit.Pad(12, 12, 6, 6), TextAnchor.MiddleCenter);
            UiKit.Size(c5, -1, -1, -1, 132);
            timeTxt = UiKit.Label(c5.transform, "0:00.00", 27, Color.white, true, TextAnchor.MiddleCenter);

            // お金（明るい地。ここがいちばん見られる数字）
            var c6 = UiKit.Card(bar, Mats.Hex(0xfffdf6), Color.white, 2, 15, "Money");
            UiKit.VCol(c6.gameObject, 0, UiKit.Pad(12, 12, 5, 5));
            UiKit.Size(c6, -1, -1, -1, 196);
            mSales = MoneyRow(c6.transform, "売上", out mWreck, 13);
            mCost = MoneyRow(c6.transform, "修理費", out mAmb, 13);
            mCost.color = RED_D;
            var line = UiKit.Img(c6.transform, Mats.Hex(0xe7ddc6), 0, "Line");
            UiKit.Size(line, -1, 2);
            Text dummy;
            mTotal = MoneyRow(c6.transform, "差引", out dummy, 17, INK);

            // ---- 右の人ごとの札
            pcards = UiKit.Rect(root, "PCards");
            pcards.anchorMin = pcards.anchorMax = new Vector2(1, 1); pcards.pivot = new Vector2(1, 1);
            pcards.anchoredPosition = new Vector2(-12, -134);
            UiKit.VCol(pcards.gameObject, 7, null, TextAnchor.UpperRight);
            var fit = pcards.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        static Text MoneyRow(Transform parent, string label, out Text sub, float vsize, Color? labelCol = null)
        {
            var row = UiKit.Rect(parent, "Row_" + label);
            UiKit.HRow(row.gameObject, 12, null, TextAnchor.LowerLeft);
            var lb = UiKit.Label(row, label, 10, labelCol ?? INK_SUB);
            sub = UiKit.Label(row, "", 10, Mats.Hex(0xa79f90), false);
            var sp = UiKit.Rect(row, "Sp"); UiKit.Size(sp, 0, 1, 1);
            var v = UiKit.Label(row, "", vsize, INK, true, TextAnchor.MiddleRight);
            return v;
        }

        PCard MakeCard(Player P)
        {
            var c = new PCard { P = P };
            var pc = Mats.Hex(P.col);
            var card = UiKit.Card(pcards, new Color(24 / 255f, 28 / 255f, 36 / 255f, 0.84f), pc, 3, 14, "PCard_" + P.name);
            c.go = card.gameObject;
            c.border = card.transform.Find("Border").GetComponent<Image>();
            UiKit.VCol(card.gameObject, 2, UiKit.Pad(8, 8, 6, 7), TextAnchor.UpperCenter);
            UiKit.Size(card, 172);
            // 名前と操作
            var hd = UiKit.Rect(card.transform, "Hd");
            UiKit.HRow(hd.gameObject, 5);
            var dot = UiKit.Img(hd, pc); dot.sprite = UiKit.Ellipse; dot.preserveAspect = true;
            var dl = UiKit.Size(dot, 10, 10); dl.minWidth = 10; dl.minHeight = 10; dl.flexibleHeight = 0;
            c.nm = UiKit.Label(hd, P.name, 13, Mats.Hex(0xeef2f7));
            var sp = UiKit.Rect(hd, "Sp"); UiKit.Size(sp, 0, 1, 1);
            c.src = UiKit.Label(hd, "キーボード", 9, Mats.Hex(0x9aa2ae));
            // 運んでいる料理の絵
            c.tex = DishPic.NewTex();
            var picR = UiKit.Rect(card.transform, "Pic");
            c.pic = picR.gameObject.AddComponent<RawImage>();
            c.pic.texture = c.tex; c.pic.raycastTarget = false;
            UiKit.Size(c.pic, 96, 96);
            c.down = UiKit.Label(picR, "▼ DOWN!", 18, Mats.Hex(0xff4d4d), true, TextAnchor.MiddleCenter);
            c.down.gameObject.AddComponent<Outline>().effectColor = Color.white;
            c.down.gameObject.SetActive(false);
            c.dn = UiKit.Label(card.transform, "手ぶら", 11, Mats.Hex(0xc9ced6), true, TextAnchor.MiddleCenter);
            c.bag = UiKit.Label(card.transform, "アイテムなし", 9, Mats.Hex(0x77808d));
            UiKit.Size(c.bag, 156);
            // 配った数と所持金
            var ft = UiKit.Rect(card.transform, "Ft");
            UiKit.HRow(ft.gameObject, 6, null, TextAnchor.LowerLeft);
            UiKit.Size(ft, 156);
            c.dl = UiKit.Label(ft, "0品", 11, Mats.Hex(0xc9ced6));
            var sp2 = UiKit.Rect(ft, "Sp"); UiKit.Size(sp2, 0, 1, 1);
            c.cash = UiKit.Label(ft, "", 13, Color.white, true, TextAnchor.MiddleRight);
            return c;
        }

        public void Clear()
        {
            foreach (var c in cards) Object.Destroy(c.go);
            cards.Clear();
        }

        // ------------------------------------------------------------ 毎フレーム
        public void Tick()
        {
            var L = g.LedgerOf(g.me);
            stageName.text = g.stage.n ?? "—";
            int total = g.course != null && g.entry != null ? g.course.stages.Count : 1;
            shopSub.text = (total > 1 ? "STAGE " + (g.courseIndex + 1) + "/" + total + "　" : "")
                         + g.shop.n + "（摩擦：" + g.shop.fricLabel + "）";
            progress.text = "配膳 " + (g.me != null ? g.me.delivered : 0) + " / " + g.orders.Count
                          + "　所持金 " + Game.Yen(g.cash);
            UiKit.SetBar(shopFill, g.shopDmg / 100f);
            shopTxt.text = "店舗ダメージ " + Mathf.RoundToInt(g.shopDmg) + "%";
            for (int i = 0; i < next.Count; i++)
            {
                var o = g.oi + i < g.orders.Count ? g.orders[g.oi + i] : null;
                next[i].go.SetActive(o != null);
                if (o == null) continue;
                next[i].dish.color = Mats.Hex(o.dish.col);
                next[i].name.text = o.dish.n;
            }
            // コンボ
            var P = g.me;
            string cb = "";
            if (P != null && P.combo >= 2)
            {
                var T = g.T;
                cb += "<color=" + (P.combo >= T.wreckMin ? "#ffb300" : "#ff3b30") + ">" + P.combo + " combo"
                    + (P.combo >= T.wreckMin
                        ? "<size=12> 大暴れ " + Mathf.RoundToInt(Mathf.Min(T.wreckMax, T.wreckBack + T.wreckStep * (P.combo - T.wreckMin)) * 100) + "%戻し</size>"
                        : "<size=12> あと" + (T.wreckMin - P.combo) + "で大暴れ</size>") + "</color>";
            }
            if (P != null && P.dcombo >= 2)
                cb += (cb.Length > 0 ? "　" : "") + "<color=#ff9500>" + P.dcombo + "連続 +"
                    + Mathf.RoundToInt(g.T.comboBonus * Mathf.Min(P.dcombo - 1, g.T.comboMax) * 100)
                    + "%<size=12> 残" + P.dcomboT.ToString("0.0") + "秒</size></color>";
            combo.text = cb;
            comboCard.SetActive(cb.Length > 0);
            // タイムとお金
            timeTxt.text = Game.FmtTime(g.frames);
            timeTxt.color = g.result != null && g.result.rec ? Mats.Hex(0xf2b52c) : Color.white;
            mSales.text = Game.Yen(L.sales);
            mWreck.text = L.wreck > 0 ? "<color=" + UiKit.Hex(GOLD_D) + ">大暴れ +" + Game.Yen(L.wreck) + "</color>" : "";
            mAmb.text = g.T.countAmb && L.amb > 0 ? "＋救急" + L.amb + "台" : "";
            mCost.text = Game.Yen(-(L.repair + L.ambCost));
            mTotal.text = Game.Yen(L.total);
            mTotal.color = L.total >= 0 ? GREEN_D : RED_D;

            // ---- 右の人ごとの札
            if (cards.Count != g.players.Count || (cards.Count > 0 && cards[0].P != g.players[0]))
            {
                Clear();
                foreach (var Q in g.players) cards.Add(MakeCard(Q));
            }
            foreach (var c in cards) TickCard(c);
        }

        void TickCard(PCard c)
        {
            var P = c.P;
            c.nm.text = P.name;
            c.src.text = "キーボード";                         // 1人で遊ぶときはパッドもキーボードの枠（JS版と同じ）
            var cd = P.carried;
            c.pic.gameObject.SetActive(cd != null);
            if (cd != null)
            {
                float tier = Dishes.PriceTier(cd.integ);
                string k = cd.dish.n + "/" + tier;
                if (k != c.key) { c.key = k; DishPic.Draw(c.tex, cd.dish, tier); }
                float now = cd.dish.price * g.shop.price * tier;   // 届けたらいくらになるか（コンボは入れない）
                c.dn.text = cd.dish.n + " <size=12><color=" + UiKit.Hex(DishPic.TierCol(tier)) + ">" + Game.Yen(now) + "</color></size>";
                // 同じ注文の皿のまま段が下がったら「▼ DOWN!」
                if (cd.order == c.obj && tier < c.tier) c.downT = 0.9f;
                c.obj = cd.order; c.tier = tier;
            }
            else
            {
                c.key = "";
                c.dn.text = P.down ? (P.revT > 0 ? "復帰まで " + Mathf.CeilToInt(P.revT) : "リタイア") : "手ぶら";
            }
            // 値段が下がったときの演出（▼ が落ちてきて、枠が赤く光る）
            if (c.downT > 0)
            {
                c.downT -= Time.deltaTime;
                float k = 1 - c.downT / 0.9f;
                c.down.gameObject.SetActive(true);
                c.down.rectTransform.anchoredPosition = new Vector2(0, 20 - Mathf.Min(1, k * 3) * 20);
                var dc = c.down.color; dc.a = k < 0.7f ? 1 : 1 - (k - 0.7f) / 0.3f; c.down.color = dc;
                c.border.color = Color.Lerp(Mats.Hex(0xff4d4d), Mats.Hex(P.col), k);
            }
            else { c.down.gameObject.SetActive(false); c.border.color = Mats.Hex(P.col); }
            c.bag.text = "アイテムなし";                      // アイテムを移したらここに並べる
            c.dl.text = P.delivered + "品";
            c.cash.text = Game.Yen(g.cash);
            var cg = c.go.GetComponent<CanvasGroup>();
            if (cg == null) cg = c.go.AddComponent<CanvasGroup>();
            cg.alpha = P.down ? 0.55f : 1f;
        }
    }
}
