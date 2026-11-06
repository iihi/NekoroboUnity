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
    /// 品物の絵は HTML版の ICONS（SVG）を PNG にした物（Resources/Ui。作り方は Tools/shop_icons）。
    /// 位置と大きさは 1280×720 の画面で CSS と同じ数値にしてある（1px ＝ ここの 1）。
    /// </summary>
    public class ShopWin
    {
        readonly Game g;
        readonly UiBoard B;
        RectTransform pick, fingRoot;
        public bool Open { get; private set; }

        float t; bool timed;
        string msg = "";
        float gw, gh;                                   // 指の動ける箱の大きさ（板いっぱい）
        readonly List<Good> goods = new List<Good>();
        readonly List<Cur> cur = new List<Cur>();
        readonly List<CardUi> cards = new List<CardUi>();
        Rect rdyRect;
        Text secText, msgText; RawImage clockFill; float clockW;
        readonly List<PpUi> pps = new List<PpUi>();
        readonly List<Text> descRows = new List<Text>();
        RawImage btnOn, btnOff; Text undoText; float pressT; Texture btnOffTex;
        RectTransform root { get { return B.root; } }

        static readonly Color GREEN = Mats.Hex(0x68ff00), POOR = Mats.Hex(0xff5a45), UPC = Mats.Hex(0x45c3ff), ITC = Mats.Hex(0x4ac15d);
        const string U = "ui/shop/";
        // 品物の絵の大きさ（板の上の px）。PSD の元の絵を、画面案と同じ 0.147 倍にした値（JS版 ICON_SZ）
        static readonly Dictionary<string, Vector2> ICON_SZ = new Dictionary<string, Vector2> {
            { "repair", new Vector2(46, 46) }, { "banana", new Vector2(63, 50) }, { "drone", new Vector2(63, 31) }, { "boomerang", new Vector2(60, 51) },
            { "laser", new Vector2(62, 30) }, { "ball", new Vector2(41, 58.5f) }, { "missile", new Vector2(53, 53) }, { "star", new Vector2(58, 55) },
            { "accel", new Vector2(56, 46) }, { "armor", new Vector2(47, 53.5f) }, { "jump", new Vector2(46, 56) }, { "light", new Vector2(49, 52) } };
        // 下の札の小さい絵の真ん中（札の左上からの x）。並びは ORDER と同じ
        static readonly float[] PP_UP_X = { 45.8f, 71, 95.5f, 119.2f };
        static readonly float[] PP_IT_X = { 33, 56.8f, 81, 104.8f, 130.2f, 153.8f, 174, 196.2f };
        static readonly string[] RES_COLS = { "blue", "red", "green", "orange" };

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
            public int seOn = -2;               // カーソルの音を鳴らした所（品物の番号、-3＝購入完了）
            public float nx = -1, ny = -1; public bool netSeen;          // オンラインの相手の指（割合で届く）
            public float lockT, buyT; public bool pOk, pUse;
            public string navK = ""; public float navT;   // 矢印キー・十字キーの押しっぱなし（少し待ってから続けて跳ぶ）
            public RectTransform el; public Text tag; public Image tagBg; public Image tagCant; public Text cantT;
        }
        class CardUi
        {
            public Good gd; public RectTransform r, sel; public RawImage bg, ic, full; public Image iv; public Text nm, pz, cp, fullT; public RectTransform own;
            public float hitX, hitY, hitW, hitH;      // 指の動ける箱（板）の中での位置（y は下向き）
            public float flash;
        }
        class PpUi { public Player P; public RectTransform r; public Text cs, sb; public List<Text> n = new List<Text>(); public RawImage pu, pi; public GameObject done; public bool doneNow; }

        // 並べる順は画面案のとおり（JS版 SHOP_ORDER）
        static readonly string[] ORDER = { "accel", "armor", "jump", "light", "repair", "banana", "drone", "boomerang", "laser", "ball", "missile", "star" };
        static readonly Dictionary<string, int> UP_COL = new Dictionary<string, int> { { "accel", 0x2f9bff }, { "armor", 0x5fd08a }, { "jump", 0xffb300 }, { "light", 0xa98cff } };

        public ShopWin(RectTransform parent, Game game)
        {
            g = game;
            B = new UiBoard(parent, "Shop");
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
                cur.Add(new Cur { P = g.players[i], lockT = g.T.menuLock, buyT = 0.8f + i * 0.45f,
                                  // 開く前に「もう買い終わった」と届いていたら、それを引き継ぐ
                                  ready = g.NpOn && g.np.rdy.ContainsKey(i) && g.np.rdy[i] });
            shopSig = ""; ptrSig = ""; ptrT = 0; pressT = 0;
            Build();
            B.Active = true;
            Open = true;
            B.StartIn();
        }

        /// <summary>開いた直後と同じく、少しのあいだ決定を受け付けない（店主の話を読み終えた指で買わないため）。</summary>
        public void ResetLocks() { foreach (var c in cur) { c.lockT = g.T.menuLock; c.armed = false; } }

        public void Close()
        {
            Open = false;
            B.Active = false;
        }

        List<Player> Viewers() { return g.players.FindAll(P => P.src.kind == "key" || P.src.kind == "pad"); }
        bool IsViewer(Player P) { return P.src.kind == "key" || P.src.kind == "pad"; }
        string NmOf(Player P)
        {
            return (P.src.kind == "npc" ? "NPC（" + NpcLevels.Get(P.src.lv).name + "）" : P.name)
                 + (g.mode == "team" ? "・" + Game.TEAM_NAME[P.team] : "");
        }

        // ================================================================ 組み立て（清書 1027。資料/ネコ配膳_ショップ画面清書1027.psd）
        // 1280×720 の板に PSD から切り出した部品の絵（HTML版の assets/ui/shop/）を置き、字と数字を重ねる。
        //   上：SHOP と残り時間 → 能力強化（1段）→ アイテム（2段）→ 指している物の説明 → 下：人ごとの札と「購入完了」
        // 品物の絵は黒い影絵（PSD の元の絵）。色は塗り替える（強化は青、アイテムは緑、下の札は人の色）
        string ColKey(Player P)
        {
            if (g.mode == "team" && g.players.Count > 1) return RES_COLS[Mathf.Clamp(P.team, 0, 3)];
            int i = System.Array.IndexOf(Player.PCOL, P.col);
            return i >= 0 ? RES_COLS[i] : "blue";
        }
        static readonly Dictionary<string, Texture2D> tinted = new Dictionary<string, Texture2D>();
        /// <summary>影絵の絵を、色 col で塗った絵（形は透明度のまま）。CSS の mask の代わり</summary>
        static Texture2D Tint(string src, Color col)
        {
            string key = src + "|" + ColorUtility.ToHtmlStringRGB(col);
            Texture2D t;
            if (tinted.TryGetValue(key, out t) && t != null) return t;
            var o = DataRoot.Texture(src);
            if (o == null) return null;
            var px = o.GetPixels32();
            var c32 = (Color32)col;
            for (int i = 0; i < px.Length; i++) { var a = px[i].a; px[i] = c32; px[i].a = a; }
            t = new Texture2D(o.width, o.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px); t.Apply(false);
            tinted[key] = t;
            return t;
        }

        void Build()
        {
            B.Clear();
            cards.Clear(); pps.Clear(); descRows.Clear();
            var bx = B.box;
            var viewers = Viewers();
            var one = viewers.Count == 1 ? viewers[0] : null;
            B.In(UiBoard.Img(bx, U + "title.png", 49, 45.5f, 204, 56.5f), "slam", 0);
            // ---- 残り時間
            var ck = UiBoard.Box(bx, 0, 0, 1280, 140, "Clock");
            if (timed)
            {
                UiBoard.Img(ck, U + "bar.png", 302, 61.5f, 773.5f, 25);
                var fillBox = UiBoard.Box(ck, 323, 69, 742.5f, 10, "Fill");
                fillBox.gameObject.AddComponent<RectMask2D>();
                clockFill = UiBoard.Img(fillBox, U + "fill.png", 0, 0, 742.5f, 10);
                clockW = 742.5f;
                UiBoard.T(ck, Lang.Tr("残り"), 1086.5f, 61, 24, Color.white);
                secText = UiBoard.T(ck, "", 1199.5f, 49, 58, GREEN, 1);
                UiBoard.T(ck, Lang.Tr("秒"), 1238.5f, 61, 24, Color.white, 1);
            }
            else
            {
                secText = null; clockFill = null;
                UiBoard.T(ck, Lang.Tr("1人のときは時間制限なし（右パネルで付けられます）"), 302, 66, 16, Mats.Hex(0xb8b8b8));
            }
            B.In(ck, "right", 1);
            // ---- 指カーソルの動ける箱（板いっぱい）
            pick = UiBoard.Box(bx, 0, 0, 1280, 720, "Pick");
            gw = 1280; gh = 720;
            B.In(UiBoard.Img(pick, U + "h_up.png", 50, 148, 145.5f, 60), "left", 2);
            B.In(UiBoard.Img(pick, U + "h_it.png", 50, 290, 145.5f, 60), "left", 3);
            int nUp = 0, nIt = 0;
            foreach (var gd in goods)
            {
                int k = gd.kind == "up" ? nUp++ : nIt++;
                float x = 226.5f + 258.5f * (k % 4);
                float y = gd.kind == "up" ? 147.5f : 290.5f + 110.5f * (k / 4);
                var c = new CardUi { gd = gd, hitX = x, hitY = y, hitW = 239, hitH = 106.5f };
                c.r = UiBoard.Box(pick, x, y, 239, 106.5f, "Card_" + gd.k);
                c.bg = UiBoard.Img(c.r, U + "card_" + gd.kind + ".png", 0, 0, 239, 106.5f);
                c.iv = UiBoard.Fill(c.r, gd.kind == "up" ? UPC : ITC, 3.5f, 3.5f, 92.5f, 92, 10, "Iv");
                c.iv.color = new Color(c.iv.color.r, c.iv.color.g, c.iv.color.b, 0);
                Vector2 sz; if (!ICON_SZ.TryGetValue(gd.k, out sz)) sz = new Vector2(56, 50);
                c.ic = UiBoard.Img(c.r, U + "ic_" + gd.k + ".png", 50 - sz.x / 2, 49 - sz.y / 2, sz.x, sz.y);
                c.ic.texture = Tint(U + "ic_" + gd.k + ".png", gd.kind == "up" ? UPC : ITC);
                bool lng = gd.n.Length > (Lang.IsJa ? 6 : 11);
                c.nm = UiBoard.T(c.r, Lang.Tr(gd.n), 115.5f, lng ? 25.25f : 23.75f, lng ? 13 : 16, Color.white);
                c.pz = UiBoard.T(c.r, "", 115.5f, 43.5f, 18, GREEN);
                c.cp = UiBoard.T(c.r, "", 115.5f, 66.25f, 10, Color.white);
                c.own = UiBoard.Box(c.r, 60, 80, 0, 15, "Own");
                c.full = UiBoard.Img(c.r, U + "full.png", -1, 1.5f, 239, 106.5f);
                c.full.color = new Color(1, 1, 1, 0.9f);
                c.fullT = UiBoard.T(c.full.rectTransform, Lang.Tr(gd.kind == "up" ? "レベルが上限に達しています" : "所持数が上限に達しています"), 110.5f, 44, 16, Color.white, 2);
                c.sel = UiBoard.Box(c.r, -6.5f, -6.5f, 112, 112, "Sel");
                var bimg = c.r.gameObject.AddComponent<Image>(); bimg.color = new Color(0, 0, 0, 0);
                B.In(c.r, "pop", gd.kind == "up" ? 2 + k * 0.5f : 3 + k * 0.5f);
                cards.Add(c);
            }
            // ---- 指している物の説明（1人1行）
            {
                var ds = UiBoard.Box(bx, 226.5f, 507.5f, 1010, 54.5f, "Desc");
                bool many = viewers.Count > 1;
                for (int i = 0; i < Mathf.Max(1, viewers.Count); i++)
                {
                    float x = many ? (i % 2) * 514 : 0, y = many ? (viewers.Count > 2 ? 6 + (i / 2) * 22 : 20) : 18;
                    var tt = UiBoard.T(ds, "", x, y, many ? 12 : 15, Color.white);
                    descRows.Add(tt);
                }
            }
            // ---- 下：人ごとの札（所持金・強化の Lv・アイテムの数・購入中／購入完了）
            for (int n = 0; n < g.players.Count; n++)
            {
                var P = g.players[n]; var ck2 = ColKey(P);
                var pp = new PpUi { P = P };
                pp.r = UiBoard.Box(pick, 49 + 222 * n, 562, 250, 113, "Pp" + n);
                UiBoard.Img(pp.r, U + "pp_" + ck2 + ".png", 0, 0, 250, 113);
                pp.pu = UiBoard.Img(pp.r, U + "pi_up_" + ck2 + ".png", 40, 33.5f, 84.5f, 11.5f);
                pp.pi = UiBoard.Img(pp.r, U + "pi_it_" + ck2 + ".png", 28, 67.5f, 174.5f, 12);
                UiBoard.T(pp.r, P.src.kind == "npc" ? "NPC" : (P.idx + 1) + "P", 61.5f, 1.25f, 18, Mats.Hex(0x06223a), 2);
                pp.cs = UiBoard.T(pp.r, "", 163, 0.5f, 18, Color.white);
                for (int j = 0; j < 4; j++) pp.n.Add(UiBoard.T(pp.r, "", PP_UP_X[j], 51, 11, Color.white, 2));
                for (int j = 0; j < 8; j++) pp.n.Add(UiBoard.T(pp.r, "", PP_IT_X[j], 85, 11, Color.white, 2));
                // 購入中…（枠で囲んでゆっくり点滅）
                var sbB = UiPoly.Skew(pp.r, new Color(0, 0, 0, 0.55f), 137, 30, 90, 22, -12);
                UiPoly.SkewRing(pp.r, new Color(1, 1, 1, 0.9f), 137, 30, 90, 22, -12, 2);
                pp.sb = UiBoard.T(pp.r, Lang.Tr("購入中…"), 182, 34, 14, Color.white, 2);
                // 購入完了：札の中を暗く塗って、大きく「購入完了！」（担当の見本）
                var dn = UiBoard.Box(pp.r, 0, 0, 250, 113, "Done");
                UiPoly.Add(dn, new Color(0.03f, 0.03f, 0.03f, 0.72f), 0, 0, 250, 113, new Vector2(35, 26), new Vector2(239, 26), new Vector2(212, 98.5f), new Vector2(6, 98.5f));
                var dt = UiBoard.T(dn, Lang.Tr("購入完了！"), 122, 43, 33, Mats.Hex(0xffe14d), 2);
                UiBoard.Stroke(dt, Mats.Hex(0x1b1405), 2.5f);
                pp.done = dn.gameObject;
                B.In(pp.r, "up", 6 + n * 0.6f);
                pps.Add(pp);
            }
            // ---- 購入完了のボタン（Y(△)/Z と同じはたらき。全員そろうと進む）。右端は上の「秒」の字の右端にそろえる
            {
                var btn = UiBoard.Box(pick, 978, 591, 260, 83, "Ready");
                btnOff = UiBoard.Img(btn, U + "btn.png", 0, 0, 260, 83); btnOffTex = null;
                btnOn = UiBoard.Img(btn, U + "btn_on.png", 0, 0, 260, 83);
                undoText = UiBoard.T(btn, Lang.Tr("もう一度押すと取り消し"), 130, 86, 11, Mats.Hex(0xc8c8c8), 2);
                B.In(btn, "pop", 7);
                rdyRect = new Rect(978, 591, 260, 83);
            }
            msgText = UiBoard.T(bx, "", 688.75f, 98, 16, Mats.Hex(0xffd400), 2);
            // ---- 指カーソル
            fingRoot = UiBoard.Box(pick, 0, 0, 1280, 720, "Fingers");
            for (int i = 0; i < cur.Count; i++)
            {
                var c = cur[i];
                var cd = cards[i % cards.Count];
                c.x = cd.hitX + cd.hitW / 2; c.y = cd.hitY + cd.hitH / 2; c.placed = true;
                MakeFinger(c);
            }
            Refresh();
        }

        void MakeFinger(Cur c)
        {
            var f = UiBoard.Box(fingRoot, 0, 0, 0, 0, "Finger_" + c.P.name);
            Hand.Add(f, c.P.col);
            // 値札（指の下）。人の色
            c.tagBg = UiBoard.Fill(f, Mats.Hex(c.P.col), 6, 44, 10, 20, 6, "Tag");
            c.tag = UiBoard.T(c.tagBg.rectTransform, "", 6, 3, 13, Color.white);
            // 「買えません」（指の左上に灰色の札。担当の見本）
            c.tagCant = UiBoard.Fill(f, Mats.Hex(0xd9d9d9), -10, -30, 10, 22, 6, "Cant");
            c.cantT = UiBoard.T(c.tagCant.rectTransform, Lang.Tr("買えません"), 7, 4, 13, Mats.Hex(0x333333));
            var cb = c.tagCant.gameObject.AddComponent<Outline>(); cb.effectColor = Color.white; cb.effectDistance = new Vector2(2, -2);
            c.el = f;
            if (!IsViewer(c.P) && c.P.src.kind != "net") f.gameObject.SetActive(false);   // NPC の指は出さない（オンラインの相手の指は出す）
        }

        // ================================================================ 毎フレーム
        public void Tick(float dt)
        {
            if (!Open) return;
            var kb = Keyboard.current;
            if (!g.NpOn)                                     // オンラインでは、やり直しとタイトルへは効かない（みんなの続きが終わる）
            {
                if (kb != null && kb.rKey.wasPressedThisFrame) { g.ResetRun(); return; }
                if (kb != null && kb.tKey.wasPressedThisFrame) { g.BackToTitle(); return; }
            }

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
                        if (mc.onRdy) { mc.ready = !mc.ready; pressT = Game.PRESS_SHOW; Game.SeUi(mc.ready ? "ok" : "back"); }
                        else if (mc.idx >= 0 && !mc.ready) Buy(mc);
                    }
                }
            }

            for (int i = 0; i < cur.Count; i++)
            {
                var c = cur[i];
                if (c.lockT > 0) c.lockT -= dt;
                if (!Mine(i))
                {
                    // 相手の指は、届いた場所へなめらかに寄せる（12回/秒のまま置くとカクつく）
                    if (c.nx >= 0)
                    {
                        float k = Mathf.Min(1, dt * 14);
                        c.x += (c.nx * gw - c.x) * k; c.y += (c.ny * gh - c.y) * k;
                        Over(c);
                    }
                    continue;
                }
                if (c.P.src.kind == "npc") { NpcTick(c, dt); continue; }
                var inp = c.P.input;
                // この画面で動かしている人（キーボード・パッド）は、入力ごとに動かし方を分ける。
                //   矢印キー・十字キー … **品物から品物へ1つずつ跳ぶ**（選択枠）
                //   スティック          … 指が自由に動く（マウスと同じ）
                // 前は矢印キー・十字キーでも指が自由に動くだけで、狙った品物に止めにくかった
                bool moved = false;
                float sp = Mathf.Max(320, gw) / Mathf.Max(0.2f, g.T.ptrCross);
                if (c.P.src.kind == "key" || c.P.src.kind == "pad")
                {
                    int dx = inp.dpLeft ? -1 : inp.dpRight ? 1 : 0;
                    int dy = dx != 0 ? 0 : inp.dpUp ? -1 : inp.dpDown ? 1 : 0;
                    string key = dx != 0 || dy != 0 ? dx + "," + dy : "";
                    if (key == "") c.navK = "";
                    else if (c.navK != key) { c.navK = key; c.navT = 0.38f; Jump(c, dx, dy); moved = true; }
                    else if ((c.navT -= dt) <= 0) { c.navT = 0.12f; Jump(c, dx, dy); moved = true; }
                    if (inp.axX != 0 || inp.axY != 0)
                    {
                        c.x = Mathf.Clamp(c.x + inp.axX * sp * dt, 0, gw);
                        c.y = Mathf.Clamp(c.y + inp.axY * sp * dt, 0, gh);
                        moved = true;
                    }
                }
                else
                {
                    // 方向は nav*（十字とスティック）だけを見る。A は加速と兼用なので
                    int vx = (inp.navRight ? 1 : 0) - (inp.navLeft ? 1 : 0);
                    int vy = (inp.navDown ? 1 : 0) - (inp.navUp ? 1 : 0);
                    if (vx != 0 || vy != 0)
                    {
                        float k = (vx != 0 && vy != 0) ? 0.7071f : 1f;          // 斜めでも速くならないように
                        c.x = Mathf.Clamp(c.x + vx * sp * k * dt, 0, gw);
                        c.y = Mathf.Clamp(c.y + vy * sp * k * dt, 0, gh);
                        moved = true;
                    }
                }
                Over(c);
                if (moved)
                {
                    // 自分の指が別の品物（か「購入完了」）に乗ったら、カーソルの音
                    int on = c.onRdy ? -3 : c.idx;
                    if (on != c.seOn) { if (on != -1) Game.SeUi("cur", 0.7f); c.seOn = on; }
                }
                if (!inp.ok) c.armed = true;                        // 一度離すまで決定しない
                if (inp.ok && !c.pOk && c.armed && c.lockT <= 0)
                {
                    if (c.onRdy) { c.ready = !c.ready; pressT = Game.PRESS_SHOW; Game.SeUi(c.ready ? "ok" : "back"); }   // 下の「購入完了」の上なら Y/Z と同じ
                    else if (!c.ready) Buy(c);
                }
                if (inp.use && !c.pUse && c.lockT <= 0) { c.ready = !c.ready; pressT = Game.PRESS_SHOW; Game.SeUi(c.ready ? "ok" : "back"); }
                c.pOk = inp.ok; c.pUse = inp.use;
            }

            // オンライン：買い物の様子と指の位置を配る
            NetSendW(false); NetSendPtr(dt);
            // 全員が買い終わったら、残り時間を待たずに進む。
            // オンラインでは**自分の画面で動かしている枠だけ**を見る（相手の入力は相手の画面にしか届かない）
            bool all = true; bool any = false;
            for (int i = 0; i < cur.Count; i++) { if (g.NpOn && !Mine(i)) continue; any = true; if (!cur[i].ready) all = false; }
            if (any && all) { if ((allT += dt) >= Game.PRESS_SHOW) { allT = 0; g.ShopDone(); return; } }
            else allT = 0;
            if (t > 0)
            {
                int was = Mathf.CeilToInt(t);
                t -= dt;
                if (t > 0 && t <= 5 && Mathf.CeilToInt(t) != was) Game.SeUi("tick");   // 残り5秒から1秒ごと
                if (t <= 0) { g.ShopDone(); return; }
            }
            Refresh();
        }

        // ================================================================ オンライン（JS版 npShopMine / npShopSend / npShopPtr と "shopw" "shopp"）
        /// <summary>この画面で動かしている枠か。相手の枠は相手の画面が、NPC はホストの画面が買う。</summary>
        public bool Mine(int i)
        {
            if (i < 0 || i >= cur.Count) return false;
            var k = cur[i].P.src.kind;
            if (k == "net") return false;
            if (k == "npc") return !g.NpOn || g.NetHost;
            return true;
        }
        string shopSig = "", ptrSig = ""; float ptrT, allT;
        /// <summary>この画面で動かしている枠の財布と「買い終わり」を配る（変わったときだけ）。last は買い終わった時の1回。</summary>
        public void NetSendW(bool last)
        {
            if (!g.NpOn || (!Open && !last)) return;
            var rows = new Newtonsoft.Json.Linq.JArray();
            for (int i = 0; i < cur.Count; i++)
            {
                if (!Mine(i)) continue;
                rows.Add(new Newtonsoft.Json.Linq.JArray(i, Game.WalletJson(g.WalletOf(cur[i].P)), cur[i].ready ? 1 : 0));
            }
            if (rows.Count == 0) return;
            string sig = rows.ToString(Newtonsoft.Json.Formatting.None);
            if (sig == shopSig) return;
            shopSig = sig;
            Net.Send(new Newtonsoft.Json.Linq.JObject { ["p"] = "shopw", ["r"] = rows });
        }
        /// <summary>指の位置。窓の大きさが人によって違うので、並びの幅・高さに対する割合で送る（12回/秒）。</summary>
        void NetSendPtr(float dt)
        {
            if (!g.NpOn || gw <= 0 || gh <= 0) return;
            ptrT -= dt;
            if (ptrT > 0) return;
            ptrT = 1f / 12;
            var rows = new Newtonsoft.Json.Linq.JArray();
            for (int i = 0; i < cur.Count; i++)
            {
                if (!Mine(i)) continue;
                var c = cur[i];
                rows.Add(new Newtonsoft.Json.Linq.JArray(i, Mathf.Round(c.x / gw * 1000) / 1000, Mathf.Round(c.y / gh * 1000) / 1000, c.idx, c.onRdy ? 1 : 0));
            }
            if (rows.Count == 0) return;
            string sig = rows.ToString(Newtonsoft.Json.Formatting.None);
            if (sig == ptrSig) return;                     // 止まっている間は送らない
            ptrSig = sig;
            Net.Send(new Newtonsoft.Json.Linq.JObject { ["p"] = "shopp", ["r"] = rows });
        }
        static bool Owns(Game g, int i, int from)
        {
            int host = Net.Room != null ? ((int?)Net.Room["host"] ?? -1) : -1;
            int owner = i < g.np.ids.Count ? g.np.ids[i] : -1;
            return owner == from || (owner == -1 && from == host);
        }
        /// <summary>相手の買い物の様子が届いた。財布を写し、買い終わりを立てる（ショップを開く前にも届く）。</summary>
        public void OnShopW(int from, Newtonsoft.Json.Linq.JArray rows)
        {
            foreach (Newtonsoft.Json.Linq.JArray row in rows)
            {
                int i = (int)row[0];
                if (i < 0 || i >= g.players.Count || !Owns(g, i, from)) continue;
                if (Open && Mine(i)) continue;
                if (!Open && (g.players[i].src.kind == "key" || g.players[i].src.kind == "pad")) continue;
                Game.WalletFromJson(g.WalletOf(g.players[i]), row[1] as Newtonsoft.Json.Linq.JObject);
                g.np.rdy[i] = (int)row[2] != 0;
                if (Open && i < cur.Count) cur[i].ready = (int)row[2] != 0;
            }
        }
        /// <summary>相手の指の位置が届いた。初めて届いたときは寄せずにその場へ置く。</summary>
        public void OnShopP(int from, Newtonsoft.Json.Linq.JArray rows)
        {
            if (!Open) return;
            foreach (Newtonsoft.Json.Linq.JArray row in rows)
            {
                int i = (int)row[0];
                if (i < 0 || i >= cur.Count || Mine(i) || !Owns(g, i, from)) continue;
                var c = cur[i];
                c.nx = (float)row[1]; c.ny = (float)row[2];
                if (!c.netSeen) { c.netSeen = true; c.x = c.nx * gw; c.y = c.ny * gh; Over(c); }
            }
        }
        /// <summary>ホストが参加者の「買い終わり」を受けた（bought）。</summary>
        public void MarkReady(int i) { if (Open && i >= 0 && i < cur.Count) cur[i].ready = true; }
        /// <summary>この画面で動かしている枠を、全部買い終わりにする（時間切れ・購入完了）。</summary>
        public void ReadyMine() { for (int i = 0; i < cur.Count; i++) if (Mine(i)) cur[i].ready = true; }

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

        /// <summary>
        /// 指を、(dx, dy) の向きにある**いちばん近い品物（か「購入完了」）**の真ん中へ跳ばす（JS版 shopJump）。
        /// 向きから外れている物ほど遠いとみなすので、横に並んだ物は横、縦は縦へ素直に移る。y は下向き。
        /// </summary>
        void Jump(Cur c, int dx, int dy)
        {
            var all = new List<Rect>();
            foreach (var cd in cards) all.Add(new Rect(cd.hitX, cd.hitY, cd.hitW, cd.hitH));
            all.Add(rdyRect);
            int onI = c.onRdy ? all.Count - 1 : c.idx;
            // いま乗っている物の真ん中から測る（何にも乗っていなければ指の位置から）
            Vector2 o = onI >= 0 ? all[onI].center : new Vector2(c.x, c.y);
            int best = -1; float bs = 1e9f;
            for (int i = 0; i < all.Count; i++)
            {
                if (i == onI) continue;
                var m = all[i].center; float ax = m.x - o.x, ay = m.y - o.y;
                float along = ax * dx + ay * dy, side = Mathf.Abs(ax * dy - ay * dx);
                if (along < 4) continue;                       // 逆向き・真横は選ばない
                float sc = along + side * 2.5f;
                if (sc < bs) { bs = sc; best = i; }
            }
            if (best < 0 && onI < 0)                           // 何にも乗っていない＋その向きに無い → いちばん近い物
                for (int i = 0; i < all.Count; i++) { float d = (all[i].center - o).magnitude; if (d < bs) { bs = d; best = i; } }
            if (best < 0) return;
            c.x = all[best].center.x; c.y = all[best].center.y;
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
            float sv = c.P.src.kind == "npc" ? 0.5f : 1;    // NPC の買い物は控えめに
            if (gd.Full(W)) { msg = Lang.Tr("{0}はもう最大です", Lang.Tr(gd.n)); Game.SeUi("ng", sv); return; }
            // 足りなければ買えない（借金はやめた）
            if (W.cash < gd.Cost(W)) { msg = Lang.Tr("所持金が足りません（{0} {1}）", Lang.Tr(gd.n), Game.Yen(gd.Cost(W))); Game.SeUi("ng", sv); return; }
            msg = "";
            Game.SeUi("buy", sv);
            if (gd.kind == "up") g.BuyUpgrade(gd.k, W); else g.BuyItem(gd.k, W);
            g.AchBuy(c.P, W);                                // 実績（お買い物デビュー・能力を最大まで）
            if (IsViewer(c.P)) cards[c.idx].flash = 0.2f;
        }

        // ------------------------------------------------------------ 表示を今の値に合わせる
        void Refresh()
        {
            var viewers = Viewers();
            var one = viewers.Count == 1 ? viewers[0] : null;
            B.Tick(Time.unscaledDeltaTime);
            if (secText != null)
            {
                secText.text = Mathf.CeilToInt(Mathf.Max(0, t)).ToString();
                secText.color = t <= 10 ? Mats.Hex(0xff4b4b) : GREEN;
                if (clockFill != null) clockFill.rectTransform.sizeDelta = new Vector2(clockW * Mathf.Clamp01(Mathf.Max(0, t) / Mathf.Max(1, g.T.shopTime)), 10);
            }
            // この画面の人が全員「購入完了」なら、品物を選べない見た目に
            bool lockAll = viewers.Count > 0 && viewers.TrueForAll(P => { var c = cur.Find(x => x.P == P); return c != null && c.ready; });
            foreach (var cd in cards)
            {
                var gd = cd.gd;
                var W1 = one != null ? g.WalletOf(one) : null;
                int cost = one != null ? gd.Cost(W1) : Mathf.RoundToInt(gd.bas);
                // 強化は買うほど上がるので「〜」を付ける。**アイテムは固定額なので付けない**
                cd.pz.text = Game.Yen(cost) + (gd.rise && one == null ? "〜" : "");
                cd.pz.color = one != null && W1.cash < cost && !gd.Full(W1) ? POOR : GREEN;
                cd.cp.text = one != null ? (gd.ammo > 0 ? Lang.Tr("残{0}/{1}", gd.Rest(W1), gd.ammo) : gd.kind == "up" ? "Lv" + gd.Lv(W1) + "/" + gd.max : Lang.Tr("所持{0}/{1}", gd.Lv(W1), gd.max))
                                        : (gd.ammo > 0 ? Lang.Tr("{0}発入り", gd.ammo) : gd.kind == "up" ? Lang.Tr("最大Lv{0}", gd.max) : Lang.Tr("最大{0}個", gd.max));
                bool full = viewers.Count > 0 && viewers.TrueForAll(P => gd.Full(g.WalletOf(P)));
                cd.full.gameObject.SetActive(full);
                // 何人も居れば、持っている数を人の色の札で絵の右下に
                foreach (Transform ch in cd.own) Object.Destroy(ch.gameObject);
                if (one == null)
                {
                    float ox = 0;
                    foreach (var P in viewers)
                    {
                        int v = gd.ammo > 0 ? gd.Rest(g.WalletOf(P)) : gd.Lv(g.WalletOf(P));
                        if (v <= 0) continue;
                        var chip = UiBoard.Fill(cd.own, Mats.Hex(P.col), ox, 0, 17, 15, 8, "Own");
                        UiBoard.T(chip.rectTransform, v.ToString(), 8.5f, 2.5f, 10, Color.white, 2);
                        ox += 19;
                    }
                }
                // 買ったとき：絵の白い四角を一瞬だけ色反転（四角は品物の色、絵は白）
                if (cd.flash > 0) cd.flash -= Time.unscaledDeltaTime;
                var ic = cd.iv.color; ic.a = cd.flash > 0 ? 1 : Mathf.MoveTowards(ic.a, 0, Time.unscaledDeltaTime * 4); cd.iv.color = ic;
                cd.ic.color = cd.flash > 0 ? Color.white : Color.white;
                cd.ic.texture = Tint(U + "ic_" + gd.k + ".png", cd.flash > 0 ? Color.white : (gd.kind == "up" ? UPC : ITC));
                cd.bg.color = cd.ic.color = lockAll ? new Color(0.45f, 0.45f, 0.45f) : Color.white;
                foreach (Transform ch in cd.sel) Object.Destroy(ch.gameObject);
            }
            // 指カーソル
            foreach (var c in cur)
            {
                if (c.el == null) continue;
                c.el.anchoredPosition = new Vector2(c.x, -c.y);
                var cg = c.el.GetComponent<CanvasGroup>();
                if (cg == null) cg = c.el.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = c.ready ? 0.45f : 1f;
                var col = Mats.Hex(c.P.col);
                c.tagCant.gameObject.SetActive(false);
                if (c.onRdy)
                {
                    c.tagBg.gameObject.SetActive(true);
                    SetTag(c, Lang.Tr(c.ready ? "取り消す" : "購入完了"), col);
                    continue;
                }
                if (c.idx < 0) { c.tagBg.gameObject.SetActive(false); continue; }
                var gd = goods[c.idx]; var W = g.WalletOf(c.P);
                int lv = gd.Lv(W), cost = gd.Cost(W); bool maxed = gd.Full(W);
                // 所持金が足りない物は「買えません」（担当）
                if (!maxed && W.cash < cost)
                {
                    c.tagBg.gameObject.SetActive(false);
                    c.tagCant.gameObject.SetActive(true);
                    c.tagCant.rectTransform.sizeDelta = new Vector2(c.cantT.preferredWidth + 14, 22);
                    c.tagCant.rectTransform.anchoredPosition = new Vector2(-c.cantT.preferredWidth - 14 + 6, 30);
                }
                else
                {
                    c.tagBg.gameObject.SetActive(true);
                    SetTag(c, (gd.ammo > 0 ? Lang.Tr("残 {0}/{1}", gd.Rest(W), gd.ammo) : gd.kind == "up" ? "Lv" + lv + "/" + gd.max : "×" + lv + "/" + gd.max)
                              + " " + (maxed ? Lang.Tr("最大") : Game.Yen(cost)), maxed ? Mats.Hex(0x8b93a0) : col);
                }
                // 指している枠（PSD の「選択1P」どおり、外が人の色・内側が少し明るい2重の縁）。何人も同じ物に乗ったら外へ重ねる
                if (!c.ready && IsViewer(c.P))
                {
                    var sel = cards[c.idx].sel; int k = sel.childCount / 2;
                    var o = UiBoard.Fill(sel, col, -6 * k, -6 * k, 112 + 12 * k, 112 + 12 * k, 16 + 6 * k, "SelO");
                    o.sprite = UiKit.Ring(5); o.type = Image.Type.Sliced; o.pixelsPerUnitMultiplier = 32f / (16 + 6 * k);
                    var i2 = UiBoard.Fill(sel, Color.Lerp(col, Color.white, 0.4f), 4.5f - 6 * k, 4.5f - 6 * k, 103 + 12 * k, 103 + 12 * k, 12 + 6 * k, "SelI");
                    i2.sprite = UiKit.Ring(4); i2.type = Image.Type.Sliced; i2.pixelsPerUnitMultiplier = 32f / (12 + 6 * k);
                }
            }
            // 下の札
            foreach (var pp in pps)
            {
                var W = g.WalletOf(pp.P); var c = cur.Find(x => x.P == pp.P);
                bool done = c != null && c.ready;
                pp.cs.text = Game.Yen(W.cash); pp.cs.color = W.cash < 0 ? POOR : Color.white;
                for (int j = 0; j < 12 && j < pp.n.Count; j++)
                {
                    var gd = goods.Find(x => x.k == ORDER[j]);
                    pp.n[j].text = gd == null ? "" : (gd.ammo > 0 ? gd.Rest(W) : gd.Lv(W)).ToString();
                    pp.n[j].color = done ? Mats.Hex(0x8a8a8a) : Color.white;
                }
                pp.done.SetActive(done);
                pp.sb.transform.parent.Find("Poly");
                float bl = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(Time.unscaledTime / 1.6f * Mathf.PI * 2));
                pp.sb.color = new Color(1, 1, 1, done ? 0 : bl);
                var gr = done ? new Color(0.35f, 0.35f, 0.35f) : Color.white;
                pp.pu.color = gr; pp.pi.color = gr;
                foreach (var poly in pp.r.GetComponentsInChildren<UiPoly>(true))
                    if (poly.transform.parent == pp.r) { var cc = poly.color; poly.enabled = !done; }
            }
            // 購入完了のボタン（この画面の人のぶん）
            var meC = cur.Find(c => c.P == g.me) ?? cur.Find(c => c.P.src.kind != "npc");
            bool rdy = meC != null && meC.ready;
            if (pressT > 0) pressT -= Time.unscaledDeltaTime;
            bool hov = cur.Exists(c => c.onRdy && IsViewer(c.P));
            btnOn.color = new Color(1, 1, 1, rdy || pressT > 0 ? 1 : 0);
            if (btnOffTex == null) btnOffTex = btnOff.texture;
            if (btnOffTex != null) btnOff.texture = hov ? UiBoard.Bright(btnOffTex) : btnOffTex;   // 選んでいるときは地を少し明るく
            undoText.gameObject.SetActive(rdy);
            // 説明の行
            for (int i = 0; i < descRows.Count; i++)
            {
                var P = i < viewers.Count ? viewers[i] : null;
                var c = P != null ? cur.Find(x => x.P == P) : null;
                descRows[i].text = c != null ? DescOf(c) : "";
            }
            msgText.text = msg;
        }
        void SetTag(Cur c, string s, Color col)
        {
            c.tag.text = s; c.tagBg.color = col;
            c.tagBg.rectTransform.sizeDelta = new Vector2(c.tag.preferredWidth + 12, 20);
        }

        string DescOf(Cur c)
        {
            string head = "<color=" + UiKit.Hex(Mats.Hex(c.P.col)) + ">●</color> ";
            const string DS = "#c8d0d8";
            if (c.onRdy) return head + Lang.Tr("買い終わり") + "  <color=" + DS + ">"
                + Lang.Tr(c.ready ? "決定でもう一度押すと、買い物に戻れます。" : "決定で「買い終わり」になります（全員そろうと次のステージへ）。") + "</color>";
            if (c.idx < 0) return head + "<color=" + DS + ">" + Lang.Tr(c.ready ? "買い終わり" : "品物の上へカーソルを動かしてください") + "</color>";
            var gd = goods[c.idx]; var W = g.WalletOf(c.P);
            int lv = gd.Lv(W), cost = gd.Cost(W); bool maxed = gd.Full(W);
            return head + Lang.Tr(gd.n) + (gd.sub.Length > 0 ? (Lang.IsJa ? "（" + Lang.Tr(gd.sub) + "）" : " (" + Lang.Tr(gd.sub) + ")") : "") + "  "
                 + (gd.ammo > 0 ? Lang.Tr("残 {0}/{1}", gd.Rest(W), gd.ammo) : gd.kind == "up" ? "Lv" + lv + "/" + gd.max : "×" + lv + "/" + gd.max) + "  "
                 + "<color=" + (maxed ? "#8b93a0" : W.cash >= cost ? "#68ff00" : "#ff5a45") + ">"
                 + (maxed ? Lang.Tr("最大") : Game.Yen(cost) + (W.cash >= cost ? "" : Lang.Tr("（足りません）"))) + "</color>  "
                 + "<color=" + DS + ">" + Lang.Tr(gd.ds) + "</color>";
        }
    }
}
