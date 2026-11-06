using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    // ================================================================ ステージ選択（投票）。JS版 ss*（担当の画面案 STAGE SELECT）
    // 各ステージの説明は出さない。
    //   ・遊ぶ人（キーボード・パッド）がそれぞれ自分の指でカードを選んで**投票**する（決定でそのカードに1票。選び直せる）
    //   ・「スタート」で決める。いちばん票の多い面。**同じ票数ならその中から抽選、誰も入れていなければランダム**
    //     「？」のカードへの票は「ランダム」への票
    //   ・「スタート」は全員が票を入れてから押せる（まだの人が居る間はボタンを暗くして、下にまだの人の札）
    //   ・人間が2人以上なら、右上にショップと同じ残り時間（stageSelTime）。時間切れはその時の票で決める
    //   ・決まった面を点滅させてから、画面転換して始める
    //   ・指はショップと同じ動き方：スティックは自由に動く、十字キー・矢印キーはカードからカードへ1つずつ跳ぶ
    // NPC は投票しない。オンラインも部屋の全員が投票する（ホストがまとめて配り、決めるのもホスト）。
    // パーツの絵はまだ無いので、カード・ボタンは仮の形（HTML版と同じ）。
    public partial class TitleWin
    {
        public const bool SS_V2 = true;
        class SsVoter
        {
            public int n, id; public PlayerSrc src; public bool local; public int col; public string lb;
            public int at = -1; public float x, y; public int seOn = -1; public int? vote;
            public bool pOk = true, pBack = true, armed; public string repK = ""; public float repT;
            public RectTransform el;
        }
        class SsCtx { public bool forRoom, goNext, goOn, cont, multi; public string head; }
        class SsState
        {
            public bool open; public SsCtx ctx; public List<SsVoter> cur = new List<SsVoter>();
            public float t, lockT, winT; public bool timed, went, anyPad; public int win = -1; public FreeE go; public string why = "";
            public string net, key = "", sig = "", mine = "", shut = ""; public float sendT, gapT;
        }
        readonly SsState SS = new SsState();
        UiBoard ssB;
        Text ssSec; RawImage ssBar; RectTransform ssFingRoot, ssWait, ssGoBtn; Text ssWaitT;
        const int SS_START = FREE_SLOTS + 1, SS_BACK = FREE_SLOTS + 2;
        static Vector2 SsPos(int i) { return new Vector2(59 + (i % 8) * 145, 138 + (i / 8) * 112); }
        public bool SsOpenNow { get { return SS.open; } }

        SsCtx SsMakeCtx()
        {
            bool forRoom = from == "Wait";
            bool goNext = forRoom && next, goOn = !forRoom && next;
            bool cont = goNext || goOn, multi = from == "Local";
            int rn = g.matchRounds;
            string head = rn > 0 ? "ROUND " + (cont ? g.runDone + 1 : 1) + " / " + rn : cont ? Lang.Tr("次のステージ") : "";
            return new SsCtx { forRoom = forRoom, goNext = goNext, goOn = goOn, cont = cont, multi = multi, head = head };
        }
        SsVoter SsNewVoter(int n, PlayerSrc src, int id, bool local)
        {
            var c = new SsVoter { n = n, src = src, id = id, local = local, col = Player.PCOL[n % 4], lb = (n + 1) + "P" };
            SsAim(c, Mathf.Min(pick, FREE_SLOTS));
            c.seOn = c.at;
            return c;
        }
        bool NetHostNow { get { return Net.Room != null && (int?)Net.Room["host"] == Net.Id; } }
        // オンライン：部屋の席の並び（1P〜4P）で、人の居る席ぶんの指。自分の席だけこの画面で動かす
        void SsRoomVoters()
        {
            var old = new Dictionary<int, SsVoter>();
            foreach (var c in SS.cur) old[c.id] = c;
            SS.cur.Clear();
            var ps = Net.Room != null ? Net.Room["players"] as JArray : null;
            if (ps == null) return;
            for (int i = 0; i < ps.Count; i++)
            {
                var m = ps[i] as JObject; if (m == null) continue;
                int id = (int?)m["id"] ?? -1;
                var c = SsNewVoter(i, new PlayerSrc(), id, id == Net.Id);
                SsVoter o;
                if (old.TryGetValue(id, out o)) { c.at = o.at; c.x = o.x; c.y = o.y; c.seOn = o.seOn; c.vote = o.vote; c.pOk = o.pOk; c.armed = o.armed; }
                SS.cur.Add(c);
            }
        }

        void SsOpen()
        {
            flist = FreeList();
            SS.ctx = SsMakeCtx();
            SS.cur.Clear();
            SS.net = SS.ctx.forRoom && Net.Room != null ? (NetHostNow ? "host" : "guest") : null;
            if (SS.net != null) { SsRoomVoters(); SS.key = Net.Id + ":" + System.DateTime.Now.Ticks; }
            else
            {
                var live = SS.ctx.multi ? Live() : new List<PlayerSrc> { new PlayerSrc() };
                for (int n = 0; n < live.Count; n++) if (live[n].kind == "key" || live[n].kind == "pad") SS.cur.Add(SsNewVoter(n, live[n], n, true));
            }
            if (SS.cur.Count == 0) SS.cur.Add(SsNewVoter(0, new PlayerSrc(), Net.Id, true));
            SS.timed = SS.cur.Count > 1 && g.T.stageSelTime > 0;
            SS.t = g.T.stageSelTime;
            SsShow();
        }
        void SsOpenGuest(JObject d)
        {
            flist = FreeList();
            SS.ctx = new SsCtx { forRoom = true, goNext = (int?)d["gn"] == 1, goOn = false, cont = (int?)d["gn"] == 1, multi = false, head = (string)d["head"] ?? "" };
            SS.net = "guest"; SS.key = (string)d["k"];
            SS.cur.Clear();
            SsRoomVoters();
            SS.timed = (int?)d["tm"] == 1; SS.t = d["t"] != null ? (float)d["t"] : g.T.stageSelTime;
            g.hud.NetWaitHide();
            SsShow();
            SsNetApply(d);
        }
        void SsShow()
        {
            SS.anyPad = SS.cur.Exists(c => c.src.kind == "pad");
            SS.win = -1; SS.winT = 0; SS.went = false; SS.go = null; SS.why = ""; SS.lockT = g.T.menuLock;
            SS.sig = ""; SS.sendT = 0; SS.mine = ""; SS.gapT = 0;
            SS.open = true;
            if (ssB == null) ssB = new UiBoard(root.parent, "StageSel");
            ssB.Active = true;
            SsDraw(true);
        }
        public void SsClose()
        {
            if (!SS.open) return;
            SS.open = false;
            SS.shut = SS.key;
            if (ssB != null) ssB.Active = false;
        }

        // ---- 描く
        void SsDraw(bool first)
        {
            ssB.Clear();
            var bx = ssB.box;
            var title = UiBoard.T(bx, "STAGE SELECT", 46, 36, 76, Color.white);
            ssB.In(title, "slam", 0);
            if (!string.IsNullOrEmpty(SS.ctx.head)) ssB.In(UiBoard.T(bx, SS.ctx.head, 50, 116, 17, Mats.Hex(0xffd400)), "left", 1);
            ssSec = null; ssBar = null;
            if (SS.timed)
            {
                var ck = UiBoard.Box(bx, 0, 0, 1280, 140, "Clock");
                UiBoard.Img(ck, "ui/shop/bar.png", 643, 61.5f, 432, 25);
                var fb = UiBoard.Box(ck, 655, 69, 409, 10, "Fill"); fb.gameObject.AddComponent<RectMask2D>();
                ssBar = UiBoard.Img(fb, "ui/shop/fill.png", 0, 0, 409, 10);
                UiBoard.T(ck, Lang.Tr("残り"), 1086.5f, 61, 24, Color.white);
                ssSec = UiBoard.T(ck, "", 1199.5f, 49, 58, Mats.Hex(0x68ff00), 1);
                UiBoard.T(ck, Lang.Tr("秒"), 1238.5f, 61, 24, Color.white, 1);
                ssB.In(ck, "right", 1);
            }
            for (int i = 0; i <= FREE_SLOTS; i++)
            {
                var p = SsPos(i); bool rnd = i == FREE_SLOTS; var e = rnd ? null : FreePick(i);
                var gone = !rnd && flist != null && i < flist.Count && flist[i].missing ? flist[i] : null;
                var votes = SS.cur.FindAll(c => c.vote == i);
                var card = UiBoard.Box(bx, p.x, p.y, 137, 96, "Card" + i);
                bool win = i == SS.win;
                float dim = SS.win >= 0 && !win ? 0.35f : 1;
                var bgc = rnd ? Mats.Hex(0x0d0d0d) : Mats.Hex(0x1c1c1c); bgc *= dim; bgc.a = 1;
                UiPoly.Skew(card, bgc, 0, 0, 137, 96, -13);
                if (!rnd)
                {
                    // 見取り図（使い回しの絵）。斜めには切らず、少し内側に置く
                    if (e != null && e.c != null)
                    {
                        var th = UiBoard.Box(card, 14, 3, 109, 72, "Th");
                        var ri = th.gameObject.AddComponent<RawImage>(); ri.texture = Thumb.Of(e.refKey, e.c); ri.raycastTarget = false; ri.color = new Color(dim, dim, dim);
                    }
                    UiPoly.Skew(card, Mats.Hex(0x2a2a2a) * dim, 0, 75, 137, 21, -13);
                    var nm = UiBoard.T(card, e != null ? Lang.Tr(e.n) : gone != null ? "× " + Lang.Tr(gone.n) : Lang.Tr("準備中"), 68.5f, 79, 12, new Color(dim, dim, dim), 2);
                    if (e == null) card.gameObject.AddComponent<CanvasGroup>().alpha = 0.35f;
                }
                else UiBoard.T(card, "?", 68.5f, 22, 50, new Color(dim, dim, dim), 2);
                UiPoly.SkewRing(card, win ? Mats.Hex(0xffd400) : Mats.Hex(0xd2d2d2) * dim, 0, 0, 137, 96, -13, 3);
                if (votes.Count > 0)
                {
                    // 投票した人の色の枠と、上に人の札（何人も入れたら札を並べる。枠は最初に入れた人の色）
                    UiPoly.SkewRing(card, Mats.Hex(votes[0].col), -8, -8, 153, 112, -13, 5);
                    float tx = 0;
                    foreach (var v in votes)
                    {
                        var tg = UiBoard.Fill(card, Mats.Hex(v.col), tx, -26, 32, 18, 0, "Vt");
                        UiBoard.T(tg.rectTransform, v.lb, 16, 2.5f, 13, Color.white, 2);
                        tx += 35;
                    }
                }
                if (win) card.gameObject.AddComponent<SsBlink>();
                ssB.In(card, "fade", 2 + (i % 8) * 0.25f + (i / 8) * 0.5f);
            }
            // スタート（まだ票を入れていない人が居る間は暗くする）と、もどる
            var wait = SS.win >= 0 ? new List<SsVoter>() : SS.cur.FindAll(c => c.vote == null);
            ssGoBtn = UiBoard.Box(bx, 474, 591, 439, 73, "Go");
            UiPoly.Skew(ssGoBtn, wait.Count > 0 ? Mats.Hex(0x30332f) : Mats.Hex(0x225520), 0, 0, 439, 73, -13);
            UiPoly.SkewRing(ssGoBtn, wait.Count > 0 ? Mats.Hex(0x7b8478) : Mats.Hex(0x6cf04f), 0, 0, 439, 73, -13, 4);
            UiBoard.T(ssGoBtn, Lang.Tr("スタート"), 219.5f, 20, 30, wait.Count > 0 ? Mats.Hex(0xa9b0a6) : Color.white, 2);
            ssB.In(ssGoBtn, "fade", 5);
            if (wait.Count > 0)
            {
                ssWait = UiBoard.Box(bx, 474, 679, 439, 22, "Wait");
                string s = Lang.Tr(SS.cur.Count > 1 ? "全員が選ぶとスタートできます" : "ステージを選ぶとスタートできます");
                if (SS.cur.Count > 1) foreach (var v in wait) s += "  <color=#" + ColorUtility.ToHtmlStringRGB(Mats.Hex(v.col)) + ">" + v.lb + "</color>";
                ssWaitT = UiBoard.T(ssWait, s, 219.5f, 0, 17, Mats.Hex(0xe6e6e6), 2);
            }
            else { ssWait = null; ssWaitT = null; }
            if (SS.net != "guest")
            {
                var back = UiBoard.Box(bx, 950, 591, 229, 73, "Back");
                UiPoly.Skew(back, Mats.Hex(0x2e2e2e), 0, 0, 229, 73, -13);
                UiPoly.SkewRing(back, Mats.Hex(0xe8e8e8), 0, 0, 229, 73, -13, 4);
                UiBoard.T(back, Lang.Tr(SS.ctx.cont ? "やめる" : "もどる"), 114.5f, 20, 30, Color.white, 2);
                ssB.In(back, "fade", 5.5f);
            }
            if (SS.why != "") UiBoard.T(bx, SS.why, 52, 618, 19, Mats.Hex(0xffd400));
            // 人ごとの指
            ssFingRoot = UiBoard.Box(bx, 0, 0, 1280, 720, "Fingers");
            foreach (var c in SS.cur)
            {
                c.el = UiBoard.Box(ssFingRoot, 0, 0, 0, 0, "Finger");
                Hand.Add(c.el, c.col);
                if (SS.cur.Count > 1)
                {
                    var tg = UiBoard.Fill(c.el, Mats.Hex(c.col), 14, 53, 30, 20, 4, "Tg");
                    UiBoard.T(tg.rectTransform, c.lb, 15, 3, 13, Color.white, 2);
                }
                if (SS.win >= 0) c.el.gameObject.SetActive(false);
            }
            if (first) ssB.StartIn();
            SsPlace();
        }

        // カード・ボタンの枠（斜めにする前の箱）。参加者の画面には「もどる」が無い
        Rect? SsRectOf(int i)
        {
            if (i == SS_START) return new Rect(474, 591, 439, 73);
            if (i == SS_BACK) return SS.net == "guest" ? (Rect?)null : new Rect(950, 591, 229, 73);
            var p = SsPos(i); return new Rect(p.x, p.y, 137, 96);
        }
        static readonly float SS_TAN = Mathf.Tan(13 * Mathf.Deg2Rad);
        int SsHit(float x, float y)
        {
            for (int i = 0; i <= SS_BACK; i++)
            {
                var r = SsRectOf(i); if (r == null) continue;
                var R = r.Value; float cy = R.y + R.height / 2, lx = x - (R.x + R.width / 2) + SS_TAN * (y - cy);
                if (Mathf.Abs(lx) <= R.width / 2 + 4 && Mathf.Abs(y - cy) <= R.height / 2 + 4) return i;
            }
            return -1;
        }
        void SsAim(SsVoter c, int i)
        {
            var r = SsRectOf(i) ?? SsRectOf(SS_START).Value; int k = c.n % 4;
            c.x = r.x + r.width / 2 + (k - 1.5f) * 16; c.y = r.y + r.height / 2 + k * 5 - 4; c.at = i;
        }
        int SsNear(float x, float y, int dx, int dy)
        {
            int best = -1; float bs = 1e9f;
            for (int i = 0; i <= SS_BACK; i++)
            {
                var r = SsRectOf(i); if (r == null) continue;
                float ax = r.Value.center.x - x, ay = r.Value.center.y - y;
                float along = ax * dx + ay * dy, side = Mathf.Abs(ax * dy - ay * dx);
                if (along < 4) continue;
                float sc = along + side * 2.5f;
                if (sc < bs) { bs = sc; best = i; }
            }
            return best;
        }
        // 上下左右で隣へ。いちばん下の段から下は「スタート」（右の2列は「もどる」）
        int SsMove(int at, int dx, int dy)
        {
            if (at <= FREE_SLOTS)
            {
                int col = at % 8, row = at / 8;
                if (dx != 0) { int nc = col + dx; return nc < 0 || nc > 7 ? at : row * 8 + nc; }
                if (dy < 0) return row > 0 ? at - 8 : at;
                return row < 3 ? at + 8 : (col >= 6 && SS.net != "guest" ? SS_BACK : SS_START);
            }
            if (at == SS_START) return dx > 0 && SS.net != "guest" ? SS_BACK : dy < 0 ? 27 : at;
            if (at == SS_BACK) return dx < 0 ? SS_START : dy < 0 ? 31 : at;
            return at;
        }
        bool SsStep(SsVoter c, int dx, int dy)
        {
            int at = SsHit(c.x, c.y);
            int nx = at >= 0 ? SsMove(at, dx, dy) : SsNear(c.x, c.y, dx, dy);
            if (nx < 0 || nx == at) return false;
            SsAim(c, nx);
            return true;
        }
        void SsPlace()
        {
            foreach (var c in SS.cur)
            {
                c.at = SsHit(c.x, c.y);
                if (c.el != null) c.el.anchoredPosition = new Vector2(c.x, -c.y);
            }
        }
        void SsCurSe(SsVoter c) { if (c.at == c.seOn) return; if (c.at >= 0) Game.SeUi("cur", 0.7f); c.seOn = c.at; }

        // その人の入力。十字キー・矢印キーは跳ぶ、スティックは自由に動く（ショップと同じ）
        BotInput SsIn(SsVoter c)
        {
            if (c.src.kind == "pad")
            {
                var b = new BotInput();
                if (c.src.index < Gamepad.all.Count)
                {
                    var gp = Gamepad.all[c.src.index];
                    b.dpLeft = gp.dpad.left.isPressed; b.dpRight = gp.dpad.right.isPressed; b.dpUp = gp.dpad.up.isPressed; b.dpDown = gp.dpad.down.isPressed;
                    var st = gp.leftStick.ReadValue();
                    b.axX = Mathf.Abs(st.x) > 0.22f ? st.x : 0; b.axY = Mathf.Abs(st.y) > 0.22f ? -st.y : 0;
                    b.ok = gp.buttonSouth.isPressed; b.back = gp.buttonEast.isPressed;
                }
                return b;
            }
            var i = new BotInput();
            var kb = Keyboard.current;
            if (kb != null)
            {
                i.dpLeft = kb.leftArrowKey.isPressed; i.dpRight = kb.rightArrowKey.isPressed; i.dpUp = kb.upArrowKey.isPressed; i.dpDown = kb.downArrowKey.isPressed;
                i.ok = kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed;
                i.back = kb.escapeKey.isPressed || kb.backspaceKey.isPressed;
            }
            if (!SS.anyPad)
                foreach (var gp in Gamepad.all)
                {
                    i.dpLeft |= gp.dpad.left.isPressed; i.dpRight |= gp.dpad.right.isPressed; i.dpUp |= gp.dpad.up.isPressed; i.dpDown |= gp.dpad.down.isPressed;
                    var st = gp.leftStick.ReadValue();
                    if (Mathf.Abs(st.x) > 0.22f && i.axX == 0) i.axX = st.x;
                    if (Mathf.Abs(st.y) > 0.22f && i.axY == 0) i.axY = -st.y;
                    i.ok |= gp.buttonSouth.isPressed; i.back |= gp.buttonEast.isPressed;
                }
            return i;
        }

        void SsOk(SsVoter c)
        {
            if (SS.win >= 0 || c.at < 0) return;
            if (c.at == SS_START)
            {
                // まだ選んでいない人が居る間は押せない（ボタンを揺らして、下の「まだの人」を見せる）
                if (SS.cur.Exists(v => v.vote == null)) { Game.SeUi("ng"); nagT = 0.36f; return; }
                if (SS.net == "guest") { Game.SeUi("ok"); Net.Send(new JObject { ["p"] = "ssgo", ["k"] = SS.key }); return; }
                SsDecide(); return;
            }
            if (c.at == SS_BACK) { if (SS.net == "guest") return; Game.SeUi("back"); SsBack(); return; }
            if (c.at != FREE_SLOTS && FreePick(c.at) == null) { Game.SeUi("ng"); return; }
            c.vote = c.at;
            Game.SeUi("ok");
            if (SS.cur.Count == 1) SsAim(c, SS_START);         // 1人なら、入れたらそのまま「スタート」へ
            SsDraw(false);
        }
        float nagT;

        // 戻る。続きの最中はやめるか確かめる
        void SsBack()
        {
            var x = SS.ctx;
            if (SS.net == "guest") return;
            if (SS.net == "host" && !x.goNext) Net.Send(new JObject { ["p"] = "ss", ["o"] = 0, ["k"] = SS.key });
            SsClose();
            if (x.goNext)
            {
                ConfirmOpen("やめてタイトルへ戻りますか？", "やめると、<b>みんなの続きがここで終わります</b>（お金・アイテム・強化もなくなります）。",
                            "やめる", "やめない（面を選ぶ）", "Free", () => { Net.LeaveRoom(); g.NpEnd(); next = false; Go("Top"); });
                return;
            }
            if (x.goOn) { next = false; Go("Top"); return; }
            Go(from);
        }

        // 票を数えて決める。いちばん多い面。同じ票数なら抽選、誰も入れていなければランダム
        void SsDecide()
        {
            if (SS.win >= 0) return;
            var cnt = new Dictionary<int, int>();
            foreach (var c in SS.cur) if (c.vote != null) { int v = c.vote.Value; cnt[v] = (cnt.ContainsKey(v) ? cnt[v] : 0) + 1; }
            int pk = FREE_SLOTS; bool tie = false;
            if (cnt.Count > 0)
            {
                int mx = 0; foreach (var v in cnt.Values) mx = Mathf.Max(mx, v);
                var top = new List<int>(); foreach (var kv in cnt) if (kv.Value == mx) top.Add(kv.Key);
                tie = top.Count > 1;
                pk = top[Random.Range(0, top.Count)];
            }
            var e = pk == FREE_SLOTS ? null : FreePick(pk);
            if (e == null)
            {
                var pool = flist.FindAll(f => f != null && !f.missing);
                if (pool.Count == 0) { Game.SeUi("ng"); return; }
                e = pool[Random.Range(0, pool.Count)];
            }
            SS.win = flist.IndexOf(e); SS.winT = 1.5f; SS.go = e;
            SS.why = cnt.Count == 0 ? Lang.Tr("誰も選ばなかったので、ランダムで決めました")
                   : tie ? Lang.Tr("同じ票数だったので、抽選で決めました")
                   : pk == FREE_SLOTS ? Lang.Tr("ランダムで決めました") : "";
            pick = SS.win;
            Game.SeUi("buy");
            SsDraw(false);
            if (SS.net == "host") Net.Send(new JObject { ["p"] = "ssd", ["k"] = SS.key, ["ref"] = e.refKey, ["why"] = SS.why });
        }
        void SsGuestDecide(JObject d)
        {
            if (!SS.open || SS.win >= 0) return;
            string rf = (string)d["ref"];
            int i = flist.FindIndex(f => f != null && f.refKey == rf);
            SS.win = i >= 0 ? i : FREE_SLOTS; SS.winT = 1.5f; SS.go = i >= 0 ? flist[i] : null;
            SS.why = (string)d["why"] ?? "";
            Game.SeUi("buy");
            SsDraw(false);
        }
        void SsGo()
        {
            var x = SS.ctx; var e = SS.go;
            if (SS.net == "guest")
            {
                SsClose();
                if (x.goNext) g.hud.NetWait("次の面が決まりました", Lang.Tr("「{0}」が始まるのを待っています…", e != null ? Lang.Tr(e.n) : "?"));
                else if (Open) Go("Wait");
                return;
            }
            pick = SS.win;
            if (x.forRoom)
            {
                SsClose();
                RoomPick();
                // ショップのあとの面選びは、決まったらそのまま始める（前はホストが待機の画面で「始める」を押していた）
                if (x.goNext && SS.net == "host") RoomStart();
                return;
            }
            var live = x.multi ? Live() : new List<PlayerSrc> { new PlayerSrc() };
            g.hud.Wipe(() => { SsClose(); FreeStart(x.multi, live); });
        }

        /// <summary>毎フレーム（Hud から。タイトルが閉じていても、オンラインの参加者の画面で動く）</summary>
        public void SsTick(float dt)
        {
            if (!SS.open) return;
            ssB.Tick(dt);
            if (nagT > 0 && ssGoBtn != null)
            {
                nagT -= dt; float u = 1 - nagT / 0.36f;
                ssGoBtn.anchoredPosition = new Vector2(474 + Mathf.Sin(u * Mathf.PI * 5) * 8 * (1 - u), -591);
                if (ssWaitT != null) ssWaitT.color = nagT > 0 ? Mats.Hex(0xffd400) : Mats.Hex(0xe6e6e6);
            }
            if (SS.win >= 0)
            {
                SS.winT -= dt;
                if (SS.winT <= 0 && !SS.went) { SS.went = true; SsGo(); }
                return;
            }
            if (SS.lockT > 0) SS.lockT -= dt;
            bool moved = false;
            // マウスは最初のキーボードの人（居なければ最初の人）の指を動かす
            var mc = SS.cur.Find(c => c.local && c.src.kind == "key") ?? SS.cur.Find(c => c.local);
            var ms = Mouse.current;
            if (mc != null && ms != null)
            {
                Vector2 lp;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(ssB.box, ms.position.ReadValue(), null, out lp))
                {
                    float mx = lp.x + 640, my = 360 - lp.y;
                    if (ms.delta.ReadValue().sqrMagnitude > 0.01f) { mc.x = Mathf.Clamp(mx, 0, 1280); mc.y = Mathf.Clamp(my, 0, 720); mc.at = SsHit(mc.x, mc.y); SsCurSe(mc); moved = true; }
                    if (ms.leftButton.wasPressedThisFrame && SS.lockT <= 0) { mc.x = mx; mc.y = my; mc.at = SsHit(mx, my); SsOk(mc); if (!SS.open) return; }
                }
            }
            float sp = 1150 / Mathf.Max(0.2f, g.T.ptrCross);
            foreach (var c in SS.cur)
            {
                if (!c.local) continue;                           // オンラインの相手の指は、届いた位置へ置く
                var now = SsIn(c);
                int dx = now.dpLeft ? -1 : now.dpRight ? 1 : 0, dy = dx != 0 ? 0 : now.dpUp ? -1 : now.dpDown ? 1 : 0;
                string key = dx != 0 || dy != 0 ? dx + "," + dy : "";
                bool step = false, mv = false;
                if (key == "") c.repK = "";
                else if (c.repK != key) { c.repK = key; c.repT = 0.36f; step = true; }
                else if ((c.repT -= dt) <= 0) { c.repT = 0.11f; step = true; }
                if (step && SsStep(c, dx, dy)) mv = true;
                if (now.axX != 0 || now.axY != 0)
                {
                    c.x = Mathf.Clamp(c.x + now.axX * sp * dt, 0, 1280);
                    c.y = Mathf.Clamp(c.y + now.axY * sp * dt, 0, 720);
                    mv = true;
                }
                if (mv) { moved = true; c.at = SsHit(c.x, c.y); SsCurSe(c); }
                if (!now.ok) c.armed = true;                      // 開いてから一度離すまで決定しない
                if (now.ok && !c.pOk && c.armed && SS.lockT <= 0) { c.pOk = now.ok; c.pBack = now.back; SsOk(c); return; }
                // B / Esc … 自分の票を取り消す。票が無ければ「もどる」（続きの最中は、ボタンからだけ）
                if (now.back && !c.pBack && SS.lockT <= 0)
                {
                    c.pBack = now.back; c.pOk = now.ok;
                    if (c.vote != null) { c.vote = null; Game.SeUi("back"); SsDraw(false); return; }
                    if (!SS.ctx.cont) { Game.SeUi("back"); SsBack(); return; }
                }
                c.pOk = now.ok; c.pBack = now.back;
            }
            if (moved) SsPlace();
            SsNetTick(dt);
            if (SS.timed)
            {
                int was = Mathf.CeilToInt(SS.t);
                SS.t -= dt;
                if (SS.t > 0 && SS.t <= 5 && Mathf.CeilToInt(SS.t) != was) Game.SeUi("tick");
                if (ssSec != null)
                {
                    ssSec.text = Mathf.CeilToInt(Mathf.Max(0, SS.t)).ToString();
                    ssSec.color = SS.t <= 10 ? Mats.Hex(0xff4b4b) : Mats.Hex(0x68ff00);
                    ssBar.rectTransform.sizeDelta = new Vector2(409 * Mathf.Clamp01(SS.t / Mathf.Max(1, g.T.stageSelTime)), 10);
                }
                if (SS.t <= 0 && SS.net != "guest") SsDecide();   // 時間切れで決めるのはホスト
            }
        }

        // ---- オンライン。ホストは全員の指と票・残り時間を配る（変わったとき＋1秒ごと）。参加者は自分の指と票を送る
        JObject SsNetState()
        {
            var v = new JArray();
            foreach (var c in SS.cur) v.Add(new JArray(c.id, Mathf.Round(c.x), Mathf.Round(c.y), c.vote.HasValue ? (JToken)c.vote.Value : JValue.CreateNull()));
            return new JObject { ["p"] = "ss", ["o"] = 1, ["k"] = SS.key, ["gn"] = SS.ctx.goNext ? 1 : 0, ["head"] = SS.ctx.head, ["tm"] = SS.timed ? 1 : 0,
                                 ["t"] = Mathf.Round(SS.t * 10) / 10, ["v"] = v };
        }
        void SsNetTick(float dt)
        {
            if (SS.net == null) return;
            if (Net.Room == null) { SsClose(); return; }
            SS.gapT -= dt;
            if (SS.net == "host")
            {
                var st = SsNetState(); string sig = st["v"].ToString(Newtonsoft.Json.Formatting.None);
                SS.sendT -= dt;
                if ((sig != SS.sig && SS.gapT <= 0) || SS.sendT <= 0) { SS.sig = sig; SS.sendT = 1; SS.gapT = 0.08f; Net.Send(st); }
            }
            else
            {
                var me = SS.cur.Find(c => c.local);
                if (me == null) return;
                int x = Mathf.RoundToInt(me.x), y = Mathf.RoundToInt(me.y);
                string sig = x + "," + y + "," + me.vote;
                if (sig != SS.mine && SS.gapT <= 0)
                {
                    SS.mine = sig; SS.gapT = 0.08f;
                    Net.Send(new JObject { ["p"] = "ssv", ["k"] = SS.key, ["x"] = x, ["y"] = y, ["v"] = me.vote.HasValue ? (JToken)me.vote.Value : JValue.CreateNull() });
                }
            }
        }
        void SsNetApply(JObject d)
        {
            bool redraw = false;
            if (d["v"] is JArray va)
                foreach (JArray r in va)
                {
                    int id = (int)r[0];
                    var c = SS.cur.Find(o => o.id == id);
                    if (c == null || c.local) continue;
                    int? vote = r[3].Type == JTokenType.Null ? (int?)null : (int)r[3];
                    if (c.vote != vote) { c.vote = vote; redraw = true; }
                    c.x = (float)r[1]; c.y = (float)r[2];
                }
            if (d["t"] != null) SS.t = (float)d["t"];
            if (redraw) SsDraw(false); else SsPlace();
        }
        void SsNetVote(int id, JObject d)
        {
            var c = SS.cur.Find(o => o.id == id && !o.local);
            if (c == null || SS.win >= 0) return;
            var was = c.vote;
            c.x = Mathf.Clamp((float?)d["x"] ?? 0, 0, 1280); c.y = Mathf.Clamp((float?)d["y"] ?? 0, 0, 720);
            c.vote = d["v"] == null || d["v"].Type == JTokenType.Null ? (int?)null : Mathf.Clamp((int)d["v"], 0, FREE_SLOTS);
            if (c.vote != was) SsDraw(false); else SsPlace();
        }
        /// <summary>部屋の中の便りのうち ss で始まるもの（Net.OnFrom から）</summary>
        void SsOnFrom(int from, JObject d)
        {
            string p = (string)d["p"] ?? "";
            if (!p.StartsWith("ss")) return;
            int host = Net.Room != null ? ((int?)Net.Room["host"] ?? -1) : -1;
            bool fromHost = from == host;
            string k = (string)d["k"];
            if (p == "ss" && fromHost)
            {
                if ((int?)d["o"] != 1) { if (SS.open && SS.key == k) { SsClose(); if (Open) Go("Wait"); } return; }
                if (!SS.open) { if (k != SS.shut) SsOpenGuest(d); return; }
                if (SS.key == k) SsNetApply(d);
                return;
            }
            if (p == "ssd" && fromHost) { if (SS.key == k) SsGuestDecide(d); return; }
            if (SS.net != "host" || k != SS.key) return;
            if (p == "ssv") SsNetVote(from, d);
            else if (p == "ssgo" && SS.win < 0 && !SS.cur.Exists(c => c.vote == null)) SsDecide();
        }
        /// <summary>部屋の人が入れ替わったら、指を作り直す</summary>
        public void SsOnRoom()
        {
            if (!SS.open || SS.net == null || Net.Room == null) return;
            SsRoomVoters();
            if (SS.net == "host") { SS.sig = ""; SS.timed = SS.cur.Count > 1 && g.T.stageSelTime > 0; }
            SsDraw(false);
        }
    }

    /// <summary>決まった面の点滅（ほかのカードは暗く）</summary>
    public class SsBlink : MonoBehaviour
    {
        void Update()
        {
            bool on = Mathf.FloorToInt(Time.unscaledTime / 0.25f) % 2 == 0;
            transform.localScale = Vector3.one * (on ? 1.04f : 1f);
            foreach (var g in GetComponentsInChildren<Graphic>()) { var c = g.color; float k = on ? 1.6f : 1f; g.material = null; }
            var cg = GetComponent<CanvasGroup>(); if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            cg.alpha = on ? 1 : 0.75f;
        }
    }
}
