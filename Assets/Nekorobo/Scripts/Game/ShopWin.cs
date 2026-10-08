using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 全画面の強化ショップ。JS版の #shopwin（openShop / renderShop / layoutShop / placeFingers / shopTick）を写したもの。
    ///
    ///   上    … タイトル（強化ショップ）・残り時間・人ごとの「ショッピング中／購入完了」
    ///   まん中 … 能力強化（1段）とアイテム（2段）のタイル。高さは 1:2 で分ける（CSS の flex 1 / 2）
    ///   下    … 指している物の説明（この画面で動かしている人ごとに1行）・購入完了のボタン・注意書き
    ///
    /// 指カーソルは人ごとに1つ。升目に吸い付かず、マウスのように自由に動く。
    /// 品物の絵は HTML版の ICONS（SVG）を PNG にした物（Resources/ShopIcons。作り方は Tools/shop_icons）。
    /// 位置と大きさは 1280×720 の画面で CSS と同じ数値にしてある（1px ＝ ここの 1）。
    /// </summary>
    public class ShopWin
    {
        readonly Game g;
        readonly RectTransform root;
        RectTransform box, pick, fingRoot;
        public bool Open { get; private set; }

        float t; bool timed;
        string msg = "";
        float gw, gh;                                   // 指の動ける箱の大きさ
        readonly List<Good> goods = new List<Good>();
        readonly List<Cur> cur = new List<Cur>();
        readonly List<CardUi> cards = new List<CardUi>();
        Rect rdyRect;
        // 上と下
        Text secText; Image clockFill; GameObject clockHurryTint;
        readonly List<PillUi> pstrip = new List<PillUi>();
        RectTransform pstripRow, clockRect;
        bool single;
        readonly List<Text> descRows = new List<Text>();
        Image readyBg, readyHov; Text readyText; RectTransform readyKeys;
        Text note2;

        // ---- 色（CSS の --ui*）
        static readonly Color SUB2 = Mats.Hex(0x9fb0d4), CLK = Mats.Hex(0xcfd8ee), GOLD = Mats.Hex(0xffd558);
        static readonly Color GREEN = Mats.Hex(0x4cbf68), GREEN_D = Mats.Hex(0x2f9448);

        class Good
        {
            public string kind, k, n, sub, ds; public int col, max, ammo; public float bas; public bool rise;
            public UpgradeDef u; public ItemDef it;
            public int Lv(Wallet W) { return kind == "up" ? W.Up(k) : W.Has(k); }
            public int Rest(Wallet W) { return W.ammo.ContainsKey(k) ? W.ammo[k] : 0; }
            public bool Full(Wallet W) { return kind == "up" ? W.Up(k) >= max : Game.ItemFull(it, W); }
            public int Cost(Wallet W) { return kind == "up" ? Game.UpCost(u, W) : Game.ItemCost(it); }
        }
        class Cur
        {
            public Player P; public float x, y; public int idx = -1; public bool onRdy, placed, armed, ready;
            public float lockT, buyT; public bool pOk, pUse;
            public RectTransform el; public Text tag; public Image tagBg; public Image tagPoor;
        }
        class CardUi
        {
            public Good gd; public RectTransform r; public CanvasGroup cg; public Image hov;
            public PillUi price, mine, cap; public Text own;
            public float hitX, hitY, hitW, hitH;      // 指の動ける箱の中での位置（y は下向き）
        }
        class PillUi
        {
            public RectTransform r; public Image bg; public Text t, stText; public float padX;
            public void Set(string s)
            {
                if (t.text == s) return;
                t.text = s;
            }
        }

        // 並べる順は画面案のとおり（JS版 SHOP_ORDER）
        static readonly string[] ORDER = { "accel", "armor", "jump", "light", "repair", "banana", "drone", "boomerang", "laser", "ball", "missile", "star" };
        static readonly Dictionary<string, int> UP_COL = new Dictionary<string, int> { { "accel", 0x2f9bff }, { "armor", 0x5fd08a }, { "jump", 0xffb300 }, { "light", 0xa98cff } };

        public ShopWin(RectTransform parent, Game game)
        {
            g = game;
            root = UiKit.Rect(parent, "Shop");
            UiKit.Stretch(root);
            // 背景：上から青い光（radial-gradient）＋ 縦の濃い青（linear-gradient）
            var bg = root.gameObject.AddComponent<RawImage>();
            bg.texture = BgTex(); bg.raycastTarget = true;          // 後ろのゲームの画面を触らせない
            root.gameObject.SetActive(false);
        }

        public bool Contains(Player P) { return cur.Exists(c => c.P == P); }

        // ================================================================ 開く・閉じる
        public void OpenNow()
        {
            goods.Clear();
            foreach (var u in Game.UPGRADES)
                goods.Add(new Good { kind = "up", k = u.k, n = u.n.Replace("アップ", ""), sub = "", ds = u.ds, col = UP_COL.ContainsKey(u.k) ? UP_COL[u.k] : 0x2f9bff,
                                     bas = u.bas, max = u.max, rise = true, u = u });
            foreach (var it in Game.ITEMS)
                goods.Add(new Good { kind = "it", k = it.k, n = it.n, sub = it.sn ?? "", ds = it.ds, col = it.col, ammo = it.ammo, bas = it.price, max = it.max, it = it });
            goods.Sort((a, b) =>
            {
                if (a.kind != b.kind) return a.kind == "up" ? -1 : 1;
                int ia = System.Array.IndexOf(ORDER, a.k), ib = System.Array.IndexOf(ORDER, b.k);
                return (ia < 0 ? 99 : ia).CompareTo(ib < 0 ? 99 : ib);
            });
            msg = "";
            // 1人で遊ぶときは待たせる相手が居ないので、既定では時間を計らない
            timed = g.players.Count > 1 || g.T.soloShopTimer;
            t = timed ? g.T.shopTime : 0;
            cur.Clear();
            for (int i = 0; i < g.players.Count; i++)
                cur.Add(new Cur { P = g.players[i], lockT = g.T.menuLock, buyT = 0.8f + i * 0.45f });
            Build();
            root.gameObject.SetActive(true);
            Open = true;
        }

        public void Close()
        {
            Open = false;
            root.gameObject.SetActive(false);
        }

        List<Player> Viewers() { return g.players.FindAll(P => P.src.kind == "key" || P.src.kind == "pad"); }
        bool IsViewer(Player P) { return P.src.kind == "key" || P.src.kind == "pad"; }
        string NmOf(Player P)
        {
            return (P.src.kind == "npc" ? "NPC（" + NpcLevels.Get(P.src.lv).name + "）" : P.name)
                 + (g.mode == "team" ? "・" + Game.TEAM_NAME[P.team] : "");
        }

        // ================================================================ 組み立て（開いたときに1回）
        void Build()
        {
            if (box != null) Object.Destroy(box.gameObject);
            box = UiKit.Rect(root, "Box");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f); box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(1280, 720);
            cards.Clear(); pstrip.Clear(); descRows.Clear();
            var viewers = Viewers();
            single = g.players.Count <= 1;
            const float L = 38, W = 1204;

            // ---- 上：タイトル（金色の丸い札）
            {
                // 金色の丸い札に白い縁、下に濃い金の影（CSS の box-shadow 0 4px 0）
                Box(box, L, 14 + 4, 260, 62, 31, GoldD(), "TitleShadow");
                var ti = Box(box, L, 14, 260, 62, 0, Color.white, "Title");
                ti.sprite = Grad(260, 62, 31, Mats.Hex(0xffd558), Mats.Hex(0xf2b52c));
                Ring(ti.transform, Color.white, 4, 31);
                var tx = Lbl(box, "強化ショップ", 34, Color.white, TextAnchor.MiddleCenter, L, 14, 260, 60);
                var s = tx.gameObject.AddComponent<Shadow>(); s.effectColor = GoldD(); s.effectDistance = new Vector2(0, -2);
            }

            // ---- 人ごとの状態（1人ならタイトルの行の右端、何人もいれば2行目の左から）
            pstripRow = UiKit.Rect(box, "PStrip");
            pstripRow.anchorMin = pstripRow.anchorMax = new Vector2(0, 1);
            pstripRow.pivot = single ? new Vector2(1, 0.5f) : new Vector2(0, 0.5f);
            pstripRow.anchoredPosition = single ? new Vector2(L + W, -45) : new Vector2(L, -98);
            var ph = UiKit.HRow(pstripRow.gameObject, 8, null, TextAnchor.MiddleLeft);
            var pf = pstripRow.gameObject.AddComponent<ContentSizeFitter>();
            pf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; pf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            foreach (var P in g.players) pstrip.Add(MakePlayerPill(pstripRow, P, IsViewer(P)));
            // 幅を測る前に中身を入れておく（空のまま測ると、時間の帯が札の下に潜る）
            for (int i = 0; i < pstrip.Count; i++) if (pstrip[i].t != null) pstrip[i].t.text = Game.Yen(g.WalletOf(g.players[i]).cash);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(pstripRow);
            float pw = single ? pstripRow.rect.width : 0;

            // ---- 残り時間（タイトルの横）
            {
                float x0 = L + 260 + 27, x1 = single ? L + W - pw - 28 : L + W;
                var ck = Box(box, x0, 28, x1 - x0, 33, 16.5f, new Color(1, 1, 1, 0.1f), "Clock");
                clockRect = ck.rectTransform;
                Ring(ck.transform, new Color(1, 1, 1, 0.22f), 3, 16.5f);
                var row = UiKit.Rect(ck.transform, "Row"); UiKit.Stretch(row);
                UiKit.HRow(row.gameObject, 10, UiKit.Pad(17, 17, 0, 0), TextAnchor.MiddleLeft);
                if (timed)
                {
                    UiKit.Label(row, "残り", 12, CLK);
                    secText = UiKit.Label(row, "", 22, GOLD, true, TextAnchor.MiddleRight);
                    UiKit.Size(secText, 42, -1, 0, 42);
                    UiKit.Label(row, "秒", 12, CLK);
                    Image back;
                    clockFill = UiKit.Bar(row, new Color(0, 0, 0, 0.35f), GREEN, out back);
                    back.sprite = UiKit.Round; back.type = Image.Type.Sliced; back.pixelsPerUnitMultiplier = 15.5f / 7f;
                    clockFill.sprite = UiKit.Round; clockFill.type = Image.Type.Sliced; clockFill.pixelsPerUnitMultiplier = 15.5f / 7f;
                    var bl = UiKit.Size(back, 0, 14, 1); bl.minHeight = 14; bl.flexibleHeight = 0;
                    UiKit.Label(row, "0秒で次のステージへ", 12, CLK);
                }
                else UiKit.Label(row, "1人のときは時間制限なし（右パネルで付けられます）", 12, CLK);
            }

            // ---- 品物の並び。上から「能力強化」「アイテム」。高さは 1:2（CSS の flex）
            int nDesc = Mathf.Max(1, viewers.Count);
            float descH = 15 + 17 * nDesc, descTop = 570 - descH;
            float gridTop = single ? 88 : 125;
            float H = descTop - 9 - gridTop;
            float secUp = (H - 12) / 3f, secIt = secUp * 2;
            float upH = secUp - 26, itH = (secIt - 26 - 11) / 2f;
            float cw = (W - 3 * 9) / 4f;
            Lbl(box, "能力強化", 16, Color.white, TextAnchor.LowerLeft, L, gridTop, 300, 20);
            Lbl(box, "アイテム", 16, Color.white, TextAnchor.LowerLeft, L, gridTop + secUp + 12, 300, 20);
            int ui = 0, ii = 0;
            for (int i = 0; i < goods.Count; i++)
            {
                var gd = goods[i];
                float x, y, h;
                if (gd.kind == "up") { x = L + (ui % 4) * (cw + 9); y = gridTop + 26; h = upH; ui++; }
                else { x = L + (ii % 4) * (cw + 9); y = gridTop + secUp + 12 + 26 + (ii / 4) * (itH + 11); h = itH; ii++; }
                cards.Add(MakeCard(gd, x, y, cw, h, single || viewers.Count == 1));
            }

            // ---- 指している物の説明（この画面の人ごとに1行）
            {
                var ds = Box(box, L, descTop, W, descH, 12, new Color(1, 1, 1, 0.1f), "Desc");
                Ring(ds.transform, new Color(1, 1, 1, 0.2f), 2, 12);
                ds.gameObject.AddComponent<RectMask2D>();
                for (int i = 0; i < nDesc; i++)
                {
                    var tt = Lbl(ds.transform, "", 12, Color.white, TextAnchor.MiddleLeft, 14, 6 + i * 17, W - 28, 17);
                    tt.fontStyle = FontStyle.Normal;
                    descRows.Add(tt);
                }
            }

            // ---- 購入完了のボタン（Y(△)/Z と同じはたらき。全員そろうと進む）
            {
                const float by = 579, bh = 44;
                Box(box, L, by + 4, W, bh, 14, GREEN_D, "ReadyShadow");
                readyBg = Box(box, L, by, W, bh, 0, Color.white, "Ready");
                readyHov = Box(box, L - 7, by - 7, W + 14, bh + 14, 21, Color.white, "ReadyHov");
                readyHov.sprite = UiKit.Ring(Mathf.RoundToInt(4 * 15.5f / 21f)); readyHov.type = Image.Type.Sliced; readyHov.pixelsPerUnitMultiplier = 15.5f / 21f;
                readyHov.enabled = false;
                readyKeys = UiKit.Rect(readyBg.transform, "Row");
                readyKeys.anchorMin = readyKeys.anchorMax = new Vector2(0.5f, 0.5f);
                UiKit.HRow(readyKeys.gameObject, 7, null, TextAnchor.MiddleCenter);
                var kf = readyKeys.gameObject.AddComponent<ContentSizeFitter>();
                kf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; kf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                foreach (var k in new[] { "Y", "Z" })
                {
                    var kc = UiKit.Img(readyKeys, Color.white, 7, "Key");
                    var kt = UiKit.Label(kc.transform, k, 13, Mats.Hex(0x3a352c), true, TextAnchor.MiddleCenter);
                    UiKit.Stretch(kt.rectTransform);
                    var kl = UiKit.Size(kc, 24, 20, 0, 24); kl.minHeight = 20;
                }
                readyText = UiKit.Label(readyKeys, "購入完了", 19, Color.white);
                var rs = readyText.gameObject.AddComponent<Shadow>(); rs.effectColor = new Color(0, 0, 0, 0.22f); rs.effectDistance = new Vector2(0, -2);
            }

            // ---- 注意書き
            {
                string sub;
                if (g.entry == null) sub = "買い物がすんだら、次に遊ぶ面を選びます（買ったものは持ち越します）";
                else
                {
                    int n = g.course.stages.Count, i = g.courseIndex;
                    bool last = i + 1 >= n - 1;
                    if (i + 1 >= n) sub = "ステージ " + (i + 1) + " 終了 — これで最後です";
                    else
                    {
                        var ne = g.course.stages[i + 1];
                        var nc = ne.FileName != null ? StageCfg.Load(ne.FileName) : null;
                        sub = "ステージ " + (i + 1) + " 終了 — 次は " + (last ? "最終ステージ"
                            : "ステージ " + (i + 2) + "（" + ne.shop + " × " + (nc != null ? nc.n : ne.cfg) + "）");
                    }
                }
                Lbl(box, sub, 11, SUB2, TextAnchor.MiddleCenter, 0, 632, 1280, 16);
                note2 = Lbl(box, "", 12, SUB2, TextAnchor.MiddleCenter, 0, 658, 1280, 16);
                var n3 = Lbl(box, "カーソルを下の「購入完了」へ動かしても押せます　／　[ R ] 最初から", 12, SUB2, TextAnchor.MiddleCenter, 0, 688, 1280, 16);
                n3.color = new Color(SUB2.r, SUB2.g, SUB2.b, 0.65f);
            }

            // ---- 指カーソルの動ける箱（品物の並びから「購入完了」まで）
            pick = UiKit.Rect(box, "Pick");
            pick.anchorMin = pick.anchorMax = new Vector2(0, 1); pick.pivot = new Vector2(0, 1);
            pick.anchoredPosition = new Vector2(L, -gridTop);
            gw = W; gh = 579 + 44 - gridTop;
            pick.sizeDelta = new Vector2(gw, gh);
            foreach (var c in cards)
            {
                var p = c.r.anchoredPosition;
                c.hitX = p.x - L; c.hitY = -p.y - gridTop; c.hitW = c.r.sizeDelta.x; c.hitH = c.r.sizeDelta.y;
            }
            rdyRect = new Rect(0, 579 - gridTop, W, 44);
            fingRoot = UiKit.Rect(pick, "Fingers"); UiKit.Stretch(fingRoot);
            // 初めて開いたときは、それぞれ別のカードの上から始める（重なって始まらないように）
            for (int i = 0; i < cur.Count; i++)
            {
                var c = cur[i];
                var cd = cards[i % cards.Count];
                c.x = cd.hitX + cd.hitW / 2; c.y = cd.hitY + cd.hitH / 2; c.placed = true;
                MakeFinger(c);
            }
            Refresh();
        }

        // ------------------------------------------------------------ 部品
        CardUi MakeCard(Good gd, float x, float y, float w, float h, bool one)
        {
            var c = new CardUi { gd = gd };
            var col0 = gd.kind == "up" ? Mats.Hex(0x25a8e6) : Mats.Hex(0x1cc266);
            var col1 = gd.kind == "up" ? Mats.Hex(0x0f78b8) : Mats.Hex(0x0a9046);
            var bg = Box(box, x, y, w, h, 0, Color.white, "Card_" + gd.k);
            bg.sprite = Grad(Mathf.RoundToInt(w), Mathf.RoundToInt(h), 14, col0, col1);
            c.r = bg.rectTransform;
            c.cg = bg.gameObject.AddComponent<CanvasGroup>();
            Ring(bg.transform, new Color(1, 1, 1, 0.55f), 3, 14);
            // 指している人の色で縁取る（CSS の inset 3px の影）
            c.hov = Ring(bg.transform, Color.white, 3, 11, 3);
            c.hov.enabled = false;
            // 絵（白い板に乗せる。4:3、高さは 100 まで）
            float ih = Mathf.Min(h - 18, 100), iw = ih * 4 / 3f;
            var ic = Box(bg.transform, 13, (h - ih) / 2, iw, ih, 12, new Color(1, 1, 1, 0.92f), "Icon");
            var tex = Resources.Load<Texture2D>("ShopIcons/" + gd.k);
            if (tex != null)
            {
                var ri = UiKit.Rect(ic.transform, "Pic").gameObject.AddComponent<RawImage>();
                ri.texture = tex; ri.raycastTarget = false;
                float aw = Mathf.Min(iw - 8, 104), ah = ih - 8;
                float pw = Mathf.Min(aw, ah * 1.5f), phh = pw / 1.5f;
                ri.rectTransform.sizeDelta = new Vector2(pw, phh);
            }
            // 文字の列（名前・値段・自分の Lv）。縦のまん中にそろえる
            float tx0 = 13 + iw + 13;
            var col = UiKit.Rect(bg.transform, "Tx");
            col.anchorMin = new Vector2(0, 0.5f); col.anchorMax = new Vector2(0, 0.5f); col.pivot = new Vector2(0, 0.5f);
            col.anchoredPosition = new Vector2(tx0, 0);
            var v = UiKit.VCol(col.gameObject, 3, null, TextAnchor.MiddleLeft);
            var vf = col.gameObject.AddComponent<ContentSizeFitter>();
            vf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; vf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var nm = UiKit.Label(col, gd.n, 15, Color.white);
            var ns = nm.gameObject.AddComponent<Shadow>(); ns.effectColor = new Color(0, 0, 0, 0.25f); ns.effectDistance = new Vector2(0, -2);
            if (one)
            {
                // 1人なら、値段と自分の Lv を大きく縦に
                c.price = Pill(col, 13, 8, new Color(0, 0, 0, 0.32f));
                c.mine = Pill(col, 14, 10, new Color(0, 0, 0, 0.35f));
            }
            else
            {
                // 何人もいれば、値段（強化は「〜」）と最大の札。持っている数は人の色の札で
                var row = UiKit.Rect(col, "Row");
                UiKit.HRow(row.gameObject, 6, null, TextAnchor.MiddleLeft);
                c.price = Pill(row, 13, 8, new Color(0, 0, 0, 0.32f));
                c.cap = Pill(row, 9, 6, new Color(0, 0, 0, 0.28f));
                c.cap.Set(gd.ammo > 0 ? gd.ammo + "発入り" : gd.kind == "up" ? "最大Lv" + gd.max : "最大" + gd.max + "個");
                c.own = UiKit.Label(col, "", 11, Color.white);
            }
            return c;
        }

        PillUi MakePlayerPill(RectTransform parent, Player P, bool viewer)
        {
            var pl = new PillUi();
            var im = UiKit.Img(parent, new Color(1, 1, 1, 0.1f), 16, "P_" + P.name);
            Ring(im.transform, new Color(1, 1, 1, 0.2f), 2, 16);
            UiKit.HRow(im.gameObject, 9, UiKit.Pad(14, 14, 0, 0), TextAnchor.MiddleLeft);
            var ie = UiKit.Size(im, -1, 32); ie.minHeight = 32;
            var dot = UiKit.Img(im.transform, Mats.Hex(P.col)); dot.sprite = UiKit.Ellipse;
            var dl = UiKit.Size(dot, 12, 12, 0, 12); dl.minHeight = 12; dl.flexibleHeight = 0;
            UiKit.Label(im.transform, NmOf(P), 13, Color.white);
            pl.t = viewer ? UiKit.Label(im.transform, "", 15, Color.white) : null;     // 所持金はこの画面の人だけ
            var st = UiKit.Img(im.transform, new Color(1, 1, 1, 0.16f), 9, "St");
            var se = UiKit.Size(st, -1, 18); se.minHeight = 18;
            UiKit.HRow(st.gameObject, 0, UiKit.Pad(10, 10, 1, 1), TextAnchor.MiddleCenter);
            var stt = UiKit.Label(st.transform, "ショッピング中", 11, Mats.Hex(0xdfe6f5));
            pl.bg = st; pl.r = st.rectTransform;
            pl.stText = stt;
            return pl;
        }

        PillUi Pill(Transform parent, float fs, int padX, Color bg)
        {
            var p = new PillUi { padX = padX };
            var im = UiKit.Img(parent, bg, 10, "Pill");
            UiKit.HRow(im.gameObject, 0, UiKit.Pad(padX, padX, 0, 0), TextAnchor.MiddleCenter);
            p.t = UiKit.Label(im.transform, "", fs, Color.white);
            var le = UiKit.Size(p.t, -1, Mathf.Round(fs * 1.45f)); le.minHeight = le.preferredHeight;
            // 角丸の絵の大きさ（30）が中身より優先されないよう、札の高さを決め打ちにする
            var pe = UiKit.Size(im, -1, Mathf.Round(fs * 1.45f)); pe.minHeight = pe.preferredHeight;
            p.bg = im; p.r = im.rectTransform;
            return p;
        }

        void MakeFinger(Cur c)
        {
            var f = UiKit.Rect(fingRoot, "Finger_" + c.P.name);
            f.anchorMin = f.anchorMax = new Vector2(0, 1); f.pivot = new Vector2(0, 1);
            var hand = UiKit.Rect(f, "Hand").gameObject.AddComponent<RawImage>();
            hand.texture = Resources.Load<Texture2D>("ShopIcons/finger"); hand.raycastTarget = false;
            var hr = hand.rectTransform; hr.anchorMin = hr.anchorMax = new Vector2(0, 1); hr.pivot = new Vector2(0, 1);
            hr.sizeDelta = new Vector2(32, 32); hr.anchoredPosition = new Vector2(-5, 5);
            c.tagBg = UiKit.Img(f, Mats.Hex(c.P.col), 5, "Tag");
            var tr = c.tagBg.rectTransform; tr.anchorMin = tr.anchorMax = new Vector2(0, 1); tr.pivot = new Vector2(0, 1);
            tr.anchoredPosition = new Vector2(9, -22);
            UiKit.HRow(c.tagBg.gameObject, 0, UiKit.Pad(5, 5, 1, 1), TextAnchor.MiddleCenter);
            var tf = c.tagBg.gameObject.AddComponent<ContentSizeFitter>();
            tf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; tf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            c.tag = UiKit.Label(c.tagBg.transform, "", 11, Color.white);
            c.tagPoor = Ring(c.tagBg.transform, Mats.Hex(0xe8604a), 2, 5);
            c.el = f;
            if (!IsViewer(c.P)) f.gameObject.SetActive(false);      // NPC の指は出さない（何を見ているか出さない）
        }

        // ================================================================ 毎フレーム
        public void Tick(float dt)
        {
            if (!Open) return;
            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) { g.ResetRun(); return; }

            // マウスでも買えるように（この画面の人のカーソルがマウスに付いていく）
            var mc = cur.Find(c => c.P == g.me && IsViewer(c.P)) ?? cur.Find(c => IsViewer(c.P));
            var ms = Mouse.current;
            if (mc != null && ms != null && !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            {
                Vector2 lp;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(pick, ms.position.ReadValue(), null, out lp))
                {
                    float mx = lp.x, my = -lp.y;
                    bool inside = mx >= 0 && mx <= gw && my >= 0 && my <= gh;
                    if (inside && ms.delta.ReadValue().sqrMagnitude > 0.01f) { mc.x = mx; mc.y = my; }
                    if (inside && ms.leftButton.wasPressedThisFrame)
                    {
                        mc.x = mx; mc.y = my; Over(mc);
                        if (mc.onRdy) mc.ready = !mc.ready;
                        else if (mc.idx >= 0 && !mc.ready) Buy(mc);
                    }
                }
            }

            for (int i = 0; i < cur.Count; i++)
            {
                var c = cur[i];
                if (c.lockT > 0) c.lockT -= dt;
                if (c.P.src.kind == "npc") { NpcTick(c, dt); continue; }
                var inp = c.P.input;
                // 方向は nav*（十字とスティック）だけを見る。A は加速と兼用なので
                int vx = (inp.navRight ? 1 : 0) - (inp.navLeft ? 1 : 0);
                int vy = (inp.navDown ? 1 : 0) - (inp.navUp ? 1 : 0);
                if (vx != 0 || vy != 0)
                {
                    float k = (vx != 0 && vy != 0) ? 0.7071f : 1f;          // 斜めでも速くならないように
                    float sp = Mathf.Max(320, gw) / Mathf.Max(0.2f, g.T.ptrCross);
                    c.x = Mathf.Clamp(c.x + vx * sp * k * dt, 0, gw);
                    c.y = Mathf.Clamp(c.y + vy * sp * k * dt, 0, gh);
                }
                Over(c);
                if (!inp.ok) c.armed = true;                        // 一度離すまで決定しない
                if (inp.ok && !c.pOk && c.armed && c.lockT <= 0)
                {
                    if (c.onRdy) c.ready = !c.ready;                // 下の「購入完了」の上なら Y/Z と同じ
                    else if (!c.ready) Buy(c);
                }
                if (inp.use && !c.pUse && c.lockT <= 0) c.ready = !c.ready;
                c.pOk = inp.ok; c.pUse = inp.use;
            }

            // 全員が買い終わったら、残り時間を待たずに進む
            if (cur.Count > 0 && cur.TrueForAll(x => x.ready)) { g.ShopDone(); return; }
            if (t > 0)
            {
                t -= dt;
                if (t <= 0) { g.ShopDone(); return; }
            }
            Refresh();
        }

        /// <summary>NPC も同じ画面で買う（裏で一瞬で済ませると、何を買われたか分からない）。</summary>
        void NpcTick(Cur c, float dt)
        {
            if (c.ready) return;
            // 協力は財布がひとつなので、NPC に人の金を使わせない
            if (!g.NpcMayShop(c.P)) { c.ready = true; return; }
            c.buyT -= dt;
            if (c.buyT > 0) return;
            c.buyT = 0.5f + Random.value * 0.7f;
            var W = g.WalletOf(c.P);
            var can = new List<int>();
            for (int i = 0; i < goods.Count; i++) if (!goods[i].Full(W) && goods[i].Cost(W) <= W.cash) can.Add(i);
            if (can.Count == 0) { c.ready = true; return; }
            // 3回に1回はアイテム。強化は半分は「いちばん高い＝効きの大きい」物、半分は適当に
            var items = can.FindAll(i => goods[i].kind == "it");
            var ups = can.FindAll(i => goods[i].kind == "up");
            ups.Sort((a, b) => goods[b].Cost(W).CompareTo(goods[a].Cost(W)));
            int pickI = (items.Count > 0 && Random.value < 0.34f) ? items[Random.Range(0, items.Count)]
                      : ups.Count > 0 ? (Random.value < 0.5f ? ups[0] : ups[Random.Range(0, ups.Count)]) : items[0];
            var cd = cards[pickI];
            c.x = cd.hitX + cd.hitW / 2; c.y = cd.hitY + cd.hitH / 2;
            c.idx = pickI;
            Buy(c);
        }

        void Over(Cur c)
        {
            c.idx = -1;
            for (int i = 0; i < cards.Count; i++)
            {
                var cd = cards[i];
                if (c.x >= cd.hitX && c.x <= cd.hitX + cd.hitW && c.y >= cd.hitY && c.y <= cd.hitY + cd.hitH) { c.idx = i; break; }
            }
            c.onRdy = c.idx < 0 && rdyRect.Contains(new Vector2(c.x, c.y));
        }

        /// <summary>指している物を買う（JS版 shopBuy）。</summary>
        void Buy(Cur c)
        {
            if (c.idx < 0 || c.idx >= goods.Count) return;
            var gd = goods[c.idx]; var W = g.WalletOf(c.P);
            if (gd.Full(W)) { msg = gd.n + "はもう最大です"; return; }
            // 足りなければ買えない（借金はやめた）
            if (W.cash < gd.Cost(W)) { msg = "所持金が足りません（" + gd.n + " " + Game.Yen(gd.Cost(W)) + "）"; return; }
            msg = "";
            if (gd.kind == "up") g.BuyUpgrade(gd.k, W); else g.BuyItem(gd.k, W);
        }

        // ------------------------------------------------------------ 表示を今の値に合わせる
        void Refresh()
        {
            var viewers = Viewers();
            var one = viewers.Count == 1 ? viewers[0] : null;
            // 1人のときは、時間の帯を右の札の手前で止める（札の幅は中身が決まってからでないと測れない）
            if (single) clockRect.sizeDelta = new Vector2(38 + 1204 - pstripRow.rect.width - 28 - clockRect.anchoredPosition.x, 33);
            if (secText != null)
            {
                secText.text = Mathf.CeilToInt(Mathf.Max(0, t)).ToString();
                bool hurry = t <= 10;
                secText.color = hurry ? Mats.Hex(0xff8f7c) : GOLD;
                UiKit.SetBar(clockFill, Mathf.Max(0, t) / Mathf.Max(1, g.T.shopTime));
                clockFill.color = hurry ? Mats.Hex(0xe8604a) : GREEN;
            }
            // 人ごとの状態
            for (int i = 0; i < pstrip.Count && i < cur.Count; i++)
            {
                var c = cur[i]; var pl = pstrip[i]; var W = g.WalletOf(c.P);
                if (pl.t != null) { pl.t.text = Game.Yen(W.cash); pl.t.color = W.cash < 0 ? Mats.Hex(0xe8604a) : Color.white; }
                pl.stText.text = c.ready ? "購入完了" : "ショッピング中";
                pl.stText.color = c.ready ? Color.white : Mats.Hex(0xdfe6f5);
                pl.bg.color = c.ready ? GREEN : new Color(1, 1, 1, 0.16f);
            }
            // タイル
            foreach (var cd in cards)
            {
                var gd = cd.gd;
                bool full = viewers.Count > 0 && viewers.TrueForAll(P => gd.Full(g.WalletOf(P)));
                cd.cg.alpha = full ? 0.5f : 1f;
                if (one != null)
                {
                    var W = g.WalletOf(one);
                    cd.price.Set(Game.Yen(gd.Cost(W)));
                    cd.mine.Set(gd.ammo > 0 ? "残 " + gd.Rest(W) + "/" + gd.ammo
                              : gd.kind == "up" ? "Lv " + gd.Lv(W) + "/" + gd.max : "所持 " + gd.Lv(W) + "/" + gd.max);
                }
                else
                {
                    // 強化は買うほど上がるので「〜」を付ける。アイテムは固定額なので付けない
                    cd.price.Set(Game.Yen(gd.bas) + (gd.rise ? "〜" : ""));
                    var sb = new System.Text.StringBuilder();
                    foreach (var P in viewers)
                    {
                        int v = gd.ammo > 0 ? gd.Rest(g.WalletOf(P)) : gd.Lv(g.WalletOf(P));
                        if (v > 0) sb.Append("<color=" + UiKit.Hex(Mats.Hex(P.col)) + ">●</color>" + v + " ");
                    }
                    cd.own.text = sb.ToString().TrimEnd();
                    cd.own.gameObject.SetActive(cd.own.text.Length > 0);
                }
                cd.hov.enabled = false;
            }
            // 指カーソル
            bool anyRdyHov = false;
            foreach (var c in cur)
            {
                if (c.el == null) continue;
                c.el.anchoredPosition = new Vector2(c.x - 11 + 6, -(c.y - 2));
                var cg = c.el.GetComponent<CanvasGroup>();
                if (cg == null) cg = c.el.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = c.ready ? 0.45f : 1f;
                var col = Mats.Hex(c.P.col);
                c.tagPoor.enabled = false;
                if (c.onRdy)
                {
                    c.tagBg.gameObject.SetActive(true);
                    c.tag.text = c.ready ? "取り消す" : "購入完了";
                    c.tagBg.color = col;
                    if (IsViewer(c.P)) { readyHov.enabled = true; readyHov.color = col; anyRdyHov = true; }
                    continue;
                }
                if (c.idx < 0) { c.tagBg.gameObject.SetActive(false); continue; }
                c.tagBg.gameObject.SetActive(true);
                var gd = goods[c.idx]; var W = g.WalletOf(c.P);
                int lv = gd.Lv(W), cost = gd.Cost(W); bool maxed = gd.Full(W);
                c.tag.text = (gd.ammo > 0 ? "残 " + gd.Rest(W) + "/" + gd.ammo : gd.kind == "up" ? "Lv" + lv + "/" + gd.max : "×" + lv + "/" + gd.max)
                           + (maxed ? " 最大" : " " + Game.Yen(cost));
                c.tagBg.color = maxed ? Mats.Hex(0x8b93a0) : col;
                c.tagPoor.enabled = !maxed && W.cash < cost;            // 所持金が足りない物は赤で囲う
                if (!c.ready && IsViewer(c.P)) { cards[c.idx].hov.enabled = true; cards[c.idx].hov.color = col; }
            }
            if (!anyRdyHov) readyHov.enabled = false;
            // 購入完了のボタン（この画面の人のぶん）
            var meC = cur.Find(c => c.P == g.me) ?? cur.Find(c => c.P.src.kind != "npc");
            bool rdy = meC != null && meC.ready;
            readyBg.sprite = rdy ? Grad(1204, 44, 14, Mats.Hex(0x9aa3ae), Mats.Hex(0x6f7884)) : Grad(1204, 44, 14, Mats.Hex(0x63d47f), GREEN);
            readyText.text = rdy ? "購入完了（もう一度で取り消し）" : "購入完了";
            // 説明の行
            for (int i = 0; i < descRows.Count; i++)
            {
                var P = i < viewers.Count ? viewers[i] : null;
                var c = P != null ? cur.Find(x => x.P == P) : null;
                descRows[i].text = c != null ? DescOf(c) : "";
            }
            note2.text = msg.Length > 0 ? msg : "所持金の範囲で買えます（足りない物は買えません）　／　全員が購入完了になると残り時間を待たずに進みます";
            note2.color = msg.Length > 0 ? Mats.Hex(0xffb4a6) : SUB2;
        }

        string DescOf(Cur c)
        {
            string head = "<color=" + UiKit.Hex(Mats.Hex(c.P.col)) + ">●</color>  ";
            const string DS = "#cfd8ee";
            if (c.onRdy) return head + "<b>買い終わり</b>  <color=" + DS + ">"
                + (c.ready ? "決定でもう一度押すと、買い物に戻れます。" : "決定で「買い終わり」になります（全員そろうと次のステージへ）。") + "</color>";
            if (c.idx < 0) return head + "<color=" + DS + ">" + (c.ready ? "買い終わり" : "品物の上へカーソルを動かしてください") + "</color>";
            var gd = goods[c.idx]; var W = g.WalletOf(c.P);
            int lv = gd.Lv(W), cost = gd.Cost(W); bool maxed = gd.Full(W);
            return head + "<b><size=13>" + gd.n + "</size></b>" + (gd.sub.Length > 0 ? "（" + gd.sub + "）" : "") + "  "
                 + (gd.ammo > 0 ? "残 " + gd.Rest(W) + "/" + gd.ammo : gd.kind == "up" ? "Lv" + lv + "/" + gd.max : "×" + lv + "/" + gd.max) + "  "
                 + "<b><color=" + (maxed ? "#8b93a0" : W.cash >= cost ? "#4cd07a" : "#ff7a66") + ">"
                 + (maxed ? "最大" : Game.Yen(cost) + (W.cash >= cost ? "" : "（足りません）")) + "</color></b>  "
                 + "<color=" + DS + ">" + gd.ds + "</color>";
        }

        // ================================================================ 絵の部品
        static Color GoldD() { return Mats.Hex(0xd18f10); }

        /// <summary>左上を (x, y) にした板（y は下向き）。</summary>
        static Image Box(Transform parent, float x, float y, float w, float h, float radius, Color c, string name)
        {
            var im = UiKit.Img(parent, c, radius, name);
            var r = im.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            return im;
        }

        static Text Lbl(Transform parent, string s, float size, Color c, TextAnchor al, float x, float y, float w, float h)
        {
            var t = UiKit.Label(parent, s, size, c, true, al);
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            return t;
        }

        /// <summary>角の丸い枠を、親いっぱい（inset だけ内側）に重ねる。</summary>
        static Image Ring(Transform parent, Color c, int th, float radius, float inset = 0)
        {
            var b = UiKit.Img(parent, c, 0, "Ring");
            b.sprite = UiKit.Ring(Mathf.Max(1, Mathf.RoundToInt(th * 15.5f / radius)));
            b.type = Image.Type.Sliced; b.pixelsPerUnitMultiplier = 15.5f / radius;
            UiKit.Stretch(b.rectTransform, inset);
            var le = b.gameObject.AddComponent<LayoutElement>(); le.ignoreLayout = true;
            return b;
        }

        static readonly Dictionary<string, Sprite> grads = new Dictionary<string, Sprite>();
        /// <summary>角の丸い、縦のグラデーションの板（CSS の linear-gradient(180deg, top, bottom)）。大きさごとに1枚作って使い回す。</summary>
        static Sprite Grad(int w, int h, float r, Color top, Color bottom)
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

        /// <summary>背景（radial-gradient(1200px 500px at 50% -10%, #1b3f8f, transparent 70%) と linear-gradient(#0b2463, #071a4a)）。</summary>
        static Texture2D BgTex()
        {
            const int W = 160, H = 90;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var top = Mats.Hex(0x0b2463); var bot = Mats.Hex(0x071a4a); var glow = Mats.Hex(0x1b3f8f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float X = (x + 0.5f) / W * 1280, Y = (1 - (y + 0.5f) / H) * 720;
                    var c = Color.Lerp(top, bot, Y / 720f);
                    float d = Mathf.Sqrt(Mathf.Pow((X - 640) / 1200f, 2) + Mathf.Pow((Y + 72) / 500f, 2));
                    float a = Mathf.Clamp01(1 - d / 0.7f);
                    tex.SetPixel(x, y, Color.Lerp(c, glow, a));
                }
            tex.Apply(false);
            return tex;
        }
    }
}
