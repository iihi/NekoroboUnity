using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 結果の画面。JS版の #result（finish の表・renderResultOk・resultTick）を写したもの。
    ///
    ///   1人・協力 … 1段（幅560）。タイム・配達・売上・修理費・救急車・合計・所持金・ステージボーナス
    ///   個人戦・チーム戦 … 2段（幅1000）。左に順位、右にタイムとお店ぜんたい、その下に「準備OK」
    ///
    /// **みんなが「準備OK」したらショップへ**（自動では進まない。右パネルの「結果の自動送り」を除く）。
    /// NPC は少し待って自分で OK する。人はそれぞれ自分の決定（Enter・スペース／パッドの A）で自分のぶんだけ押す。
    /// 色・丸み・字の大きさは CSS と同じ数値（1280×720 で 1px ＝ ここの 1）。
    /// </summary>
    public class ResultWin
    {
        readonly Game g;
        readonly UiBoard B;
        public bool Open { get; private set; }

        readonly List<bool> ok = new List<bool>(), prevOk = new List<bool>();
        readonly List<float> npcT = new List<float>();
        float shownAt, autoT, goT, pressT;
        bool went;
        RectTransform oks; RawImage btnOn; Text okNote;

        static readonly string[] RES_COLS = { "blue", "red", "green", "orange" };
        static readonly float[] ROW_Y = { 190.5f, 312.5f, 435.5f, 558.5f };
        static readonly Color GREEN = Mats.Hex(0x68ff00), NEG = Mats.Hex(0xff6b5b), YEL = Mats.Hex(0xffd400), SUBC = Mats.Hex(0xcfd8de), TAGC = Mats.Hex(0x06223a);
        const string UI = "ui/res/";

        public ResultWin(RectTransform parent, Game game)
        {
            g = game;
            B = new UiBoard(parent, "Result");
        }

        public void Close() { Open = false; B.Active = false; }

        string ColKey(Player P)
        {
            if (g.mode == "team" && g.players.Count > 1) return RES_COLS[Mathf.Clamp(P.team, 0, 3)];
            int i = System.Array.IndexOf(Player.PCOL, P.col);
            return i >= 0 ? RES_COLS[i] : "blue";
        }
        string Tag(Player P) { return P.src.kind == "npc" ? "NPC" : (P.idx + 1) + "P"; }
        string DownOf(Player P) { return !P.down ? "" : P.revT > 0 ? Lang.Tr("復帰 {0}", Mathf.CeilToInt(P.revT)) : Lang.Tr("リタイア"); }
        static string Plus(float v) { return (v < 0 ? "−¥" : "＋¥") + Mathf.Abs(Mathf.Round(v)).ToString("#,0"); }

        class Band { public int rank; public string col, tag, sub = "", outS = ""; public float cash, bonus, bot = -1; public int deliv, combo = -1; public Game.Ledger L; }

        // ================================================================ 組み立て（清書 1027。資料/ネコ配膳_リザルト画面清書1027.psd）
        // 左に順位の帯（個人戦は人ごと、チーム戦はチームごと、1人・協力はお店ひとつぶんの1本）、右にタイム・お店全体・ボタン
        public void Show()
        {
            var R = g.result;
            B.Clear();
            ok.Clear(); prevOk.Clear(); npcT.Clear(); rokSent = "";
            for (int i = 0; i < g.players.Count; i++) { ok.Add(false); prevOk.Add(true); npcT.Add(0.9f + i * 0.35f); }
            went = false; goT = 0; pressT = 0; kira = null;
            shownAt = Time.unscaledTime;
            autoT = g.T.resultTime > 0 ? g.T.resultTime : 0;
            var bx = B.box;
            bool many = g.players.Count > 1;
            bool clear = R.cleared && !R.timeUp;

            // ---- 見出し。クリアは PSD の文字の絵、タイムアップ・失敗は同じ大きさの字で
            if (clear) B.In(UiBoard.Img(bx, UI + "title.png", 51, 45.5f, 539.5f, 56.5f), "slam", 0);
            else B.In(UiBoard.T(bx, R.timeUp ? "TIME UP" : "FAILED…", 51, 36, 76, Color.white), "slam", 0);

            // ---- 帯の中身
            var bands = new List<Band>();
            System.Func<Player, int, float> bonusOf = (P, team) =>
            {
                foreach (var b in R.bonuses) if (team >= 0 ? b.team == team : (b.P == null || b.P == P)) return b.amt;
                return 0;
            };
            if (many && g.mode == "team")
            {
                var ts = g.TeamsInPlay();
                var led = new Dictionary<int, Game.Ledger>();
                foreach (var t in ts) led[t] = g.LedgerOf(g.TeamLead(t));
                ts.Sort((a, b) => led[b].total.CompareTo(led[a].total));
                for (int i = 0; i < ts.Count; i++)
                {
                    var ms = g.players.FindAll(q => q.team == ts[i]);
                    int dl = 0; foreach (var q in ms) dl += q.delivered;
                    var sub = new List<string>();
                    foreach (var q in ms) sub.Add(Tag(q) + " " + Lang.Tr("{0}品", q.delivered) + (q.down ? "（" + DownOf(q) + "）" : ""));
                    bands.Add(new Band { rank = i + 1, col = RES_COLS[Mathf.Clamp(ts[i], 0, 3)], tag = Lang.Tr(new[] { "青", "赤" }[Mathf.Clamp(ts[i], 0, 1)]),
                                         cash = g.wallets[ts[i]].cash, deliv = dl, L = led[ts[i]], bonus = bonusOf(null, ts[i]), sub = string.Join("　", sub.ToArray()) });
                }
            }
            else if (many && g.mode == "versus")
            {
                var order = g.RankByTotal();
                for (int i = 0; i < order.Count; i++)
                {
                    var P = order[i];
                    bands.Add(new Band { rank = i + 1, col = ColKey(P), tag = Tag(P), cash = g.WalletOf(P).cash, deliv = P.delivered,
                                         L = g.LedgerOf(P), combo = P.bestCombo, bot = P.botDmg, bonus = bonusOf(P, -1), outS = DownOf(P) });
                }
            }
            else
            {
                int doneN = 0; foreach (var o in g.orders) if (o.done) doneN++;
                var sub = new List<string>();
                if (many) foreach (var q in g.players) sub.Add(Tag(q) + " " + Lang.Tr("{0}品", q.delivered) + " " + Game.Yen(q.sales) + (q.down ? "（" + DownOf(q) + "）" : ""));
                bands.Add(new Band { rank = 0, col = ColKey(g.me), tag = many ? Lang.Tr("みんな") : Tag(g.me), cash = g.WalletOf(g.me).cash,
                                     deliv = many ? doneN : g.me.delivered, L = g.LedgerOf(g.me), bot = many ? -1 : g.me.botDmg,
                                     bonus = bonusOf(null, -1), outS = many ? "" : DownOf(g.me), sub = string.Join("　", sub.ToArray()) });
            }
            bool ranked = bands[0].rank > 0;
            {
                var cap = UiBoard.Box(bx, 50, 142, 800, 40, "Cap");
                var c1 = UiBoard.T(cap, ranked ? "RANK" : "RESULT", 0, 0, 34, Color.white);
                UiBoard.T(cap, Lang.Tr(g.mode == "team" && many ? "差引＝チームの売上−(チームが壊したぶんの)修理費•救急車"
                                    : ranked ? "差引＝売上−(自分が壊したぶんの)修理費•救急車"
                                    : many ? "差引＝お店の売上−修理費•救急車（協力：お店ひとつぶん）" : "差引＝売上−修理費•救急車"),
                          c1.preferredWidth + 30, 12, 19, Color.white);
                B.In(cap, "fade", 1);
            }
            for (int k = 0; k < bands.Count && k < 4; k++)
            {
                var o = bands[k]; var L = o.L;
                var row = UiBoard.Box(bx, 30, ROW_Y[k], 832, 117, "Row" + k);
                if (o.rank > 0) UiBoard.Img(row, UI + "rank" + Mathf.Min(4, o.rank) + ".png", 0, 0, 144, 116);
                if (o.rank == 1) kira = UiBoard.Img(row, UI + "kira.png", 3.5f, 9.5f, 96.5f, 63.5f);
                if (o.rank == 0) UiBoard.Img(row, "ui/face_robo_" + (clear ? "smile2" : "fear") + ".png", 20, 16, 74, 92);
                UiBoard.Img(row, UI + "band_" + o.col + ".png", 110.5f, 0.5f, 731, 115.5f);
                bool jp = System.Text.RegularExpressions.Regex.IsMatch(o.tag, "[^\\x00-\\x7f]");
                UiBoard.T(row, o.tag, 185.5f, jp ? 5.5f : 1.5f, jp ? 19 : 28, TAGC, 2);
                string cash = Game.Yen(o.cash);
                if (o.bonus != 0) cash += "<size=13><color=#ffd400>  " + Lang.Tr("（ボーナス {0}）", Plus(o.bonus)) + "</color></size>";
                if (L.lootIn != 0 || L.lootOut != 0) cash += "<size=13><color=#ffd400>  " + Lang.Tr("（奪った {0}・奪われた {1}）", Plus(L.lootIn), Plus(-L.lootOut)) + "</color></size>";
                UiBoard.T(row, cash, 310, 7.5f, 18, Color.white);
                UiBoard.T(row, o.deliv.ToString(), 226, 50, 18, Color.white, 1);
                UiBoard.T(row, Plus(L.sales), 226, 75, 18, Color.white, 1);
                UiBoard.T(row, (o.combo >= 0 ? o.combo : L.best).ToString(), 337, 50, 18, Color.white, 1);
                UiBoard.T(row, Plus(L.wreck), 337, 75, 18, Color.white, 1);
                UiBoard.T(row, Mathf.RoundToInt(L.dmg) + "<size=11>%</size>", 448.5f, 50, 18, Color.white, 1);
                // 修理費と救急車（と救済のクレーン代）をまとめて引く（画面案に救急車の欄が無いので、ここに入れる）
                UiBoard.T(row, Plus(-(L.repair + L.ambCost + L.resc)), 448.5f, 75, 18, Color.white, 1);
                UiBoard.T(row, o.bot >= 0 ? Mathf.RoundToInt(o.bot) + " %" : "—", 526.5f, 75, 18, Color.white, 1);
                UiBoard.T(row, Lang.Tr("{0}人", L.amb), 588, 75, 18, Color.white, 1);
                var tot = UiBoard.T(row, Game.Yen(L.total), 722, 53, 36, L.total < 0 ? NEG : Color.white, 2);
                float tv = Mathf.Round(L.total);
                B.Count(tot, tv, v => Game.Yen(v));
                if (o.outS != "")
                {
                    var pill = UiBoard.Fill(row, Mats.Hex(0xe53935), 560, 9, 10, 21, 10, "Out");
                    var ot = UiBoard.T(pill.rectTransform, o.outS, 9, 4, 13, Color.white);
                    pill.rectTransform.sizeDelta = new Vector2(ot.preferredWidth + 18, 21);
                }
                if (o.sub != "") UiBoard.T(row, o.sub, 150, 122, 13, SUBC);
                B.In(row, "left", k + 2);
            }
            // ---- 右：タイム
            {
                var tb = UiBoard.Box(bx, 885.5f, 149, 361.5f, 158, "Time");
                UiBoard.Img(tb, UI + "time.png", 0, 0, 361.5f, 158);
                if (clear)
                {
                    var tm = UiBoard.T(tb, Game.FmtTime(g.frames), 301, 50, 58, GREEN, 1);
                    if (R.rec) { var sh = tm.gameObject.AddComponent<Outline>(); sh.effectColor = new Color(0.41f, 1, 0, 0.35f); sh.effectDistance = new Vector2(3, -3); }
                }
                else
                {
                    var why = UiBoard.T(tb, Lang.Tr(R.timeUp ? "時間切れ" : many ? "全員が動けなくなった" : "ダメージ100%で\n動けなくなった"), 60, 52, 24, NEG);
                    why.lineSpacing = 1.2f;
                }
                int best = PlayerPrefs.GetInt(g.BestKey(), 0);
                UiBoard.T(tb, best > 0 ? Game.FmtTime(best) : "—", 301, 111, 27, Color.white, 1);
                if (R.rec) UiBoard.Img(tb, UI + "rec.png", 142, 7.5f, 194.5f, 20);
                B.In(tb, "right", 2);
            }
            // ---- 右：お店全体
            {
                var sb = UiBoard.Box(bx, 0, 0, 1280, 720, "Shop");
                UiBoard.T(sb, Lang.Tr("お店全体"), 1062.5f, 332, 34, Color.white, 2);
                int doneN = 0; foreach (var o in g.orders) if (o.done) doneN++;
                var kv = new[] { new[] { "配膳完了数", doneN + "/" + g.orders.Count }, new[] { "店舗損壊率", Mathf.RoundToInt(g.shopDmg) + "%" },
                                 new[] { "倒れた客", Lang.Tr("{0}人", g.guests.FindAll(x => x.hp <= 50).Count) } };
                float[] ty = { 386, 432, 477 };
                for (int i = 0; i < 3; i++) { UiBoard.T(sb, Lang.Tr(kv[i][0]), 916, ty[i], 27, Color.white); UiBoard.T(sb, kv[i][1], 1220, ty[i], 27, GREEN, 1); }
                UiBoard.T(sb, Lang.Tr(g.mode == "team" && many ? "修理費と救急車の代金は壊した人のチームが支払っています。"
                                     : ranked ? "修理費と救急車の代金は壊した人が支払っています。" : "修理費と救急車の代金は売上から引いています。"),
                          1068, 523, 17, Color.white, 2);
                B.In(sb, "right", 3);
            }
            // ---- ラウンドマッチは、いま何ラウンド目を終えたかを右上に
            if (g.IsMatch)
                B.In(UiBoard.T(bx, "ROUND <size=46>" + g.runDone + "</size><size=26><color=#ffffff> / " + g.matchRounds + "</color></size>", 1200, 52, 30, YEL, 1), "right", 1);
            // ---- 右下：ボタン。押した人の札はその上（同じ画面の2人以上・オンライン）
            string goLabel = g.IsStory && !g.NpOn ? (!R.cleared ? "もう一度" : g.StoryNoShop ? "次へ" : "ショップへ")
                           : g.MatchOver ? "最終結果へ" : "ショップへ";
            var btn = UiBoard.Box(bx, 937, 591, 260, 83, "Btn");
            UiBoard.Img(btn, UI + "btn.png", 0, 0, 260, 83);
            btnOn = UiBoard.Img(btn, UI + "btn_on.png", 0, 0, 260, 83);
            UiBoard.T(btn, Lang.Tr(goLabel), 130, 41.5f - 16, 32, Color.white, 2);
            var bimg = btn.gameObject.AddComponent<Image>(); bimg.color = new Color(0, 0, 0, 0);
            var bb = btn.gameObject.AddComponent<Button>(); bb.targetGraphic = bimg;
            bb.onClick.AddListener(() => { if (Time.unscaledTime - shownAt > g.T.menuLock) PressMine(); });
            B.In(btn, "pop", 5);
            oks = UiBoard.Box(bx, 1067, 556, 0, 26, "Oks");
            okNote = UiBoard.T(bx, "", 1067, 681, 13, Mats.Hex(0xc8c8c8), 2);
            B.Active = true;
            Open = true;
            B.StartIn();
            Refresh();
        }
        RawImage kira;

        /// <summary>この画面の人ぶんの「OK」をまとめて切り替える（下の大きなボタン・1人の決定）</summary>
        void PressMine()
        {
            var mine = new List<int>();
            for (int i = 0; i < g.players.Count; i++) if (OwnSlot(i) && g.players[i].src.kind != "npc") mine.Add(i);
            bool to = !mine.TrueForAll(i => ok[i]);
            foreach (var i in mine) ok[i] = to;
            pressT = Game.PRESS_SHOW;
            Game.SeUi(to ? "ok" : "back");
            Refresh();
        }

        // 「OK」の札。2人以上のときは**全員ぶん出す**（誰を待っているのか分かるように）
        void Refresh()
        {
            foreach (Transform c in oks) Object.Destroy(c.gameObject);
            float x = 0; var chips = new List<RectTransform>();
            if (g.players.Count >= 2)
                for (int i = 0; i < g.players.Count; i++)
                {
                    var P = g.players[i]; var col = Mats.Hex(P.col);
                    var ch = UiBoard.Fill(oks, ok[i] ? col : new Color(0, 0, 0, 0.6f), x, 0, 10, 26, 13, "Ok");
                    var ring = UiBoard.Fill(ch.rectTransform, col, 0, 0, 10, 26, 13, "Ring");
                    ring.sprite = UiKit.Ring(3); ring.type = Image.Type.Sliced;
                    var t = UiBoard.T(ch.rectTransform, Tag(P) + (ok[i] ? " OK!" : " …"), 10, 6, 14, ok[i] ? Mats.Hex(0x06223a) : Color.white);
                    float w = t.preferredWidth + 20;
                    ch.rectTransform.sizeDelta = new Vector2(w, 26); ring.rectTransform.sizeDelta = new Vector2(w, 26);
                    chips.Add(ch.rectTransform); x += w + 6;
                }
            foreach (var c in chips) c.anchoredPosition += new Vector2(-(x - 6) / 2, 0);
            var mine = new List<int>();
            for (int i = 0; i < g.players.Count; i++) if (OwnSlot(i) && g.players[i].src.kind != "npc") mine.Add(i);
            bool on = mine.Count > 0 && mine.TrueForAll(i => ok[i]);
            if (btnOn != null) btnOn.color = new Color(1, 1, 1, on || pressT > 0 ? 1 : 0);
            int left = 0; foreach (var o in ok) if (!o) left++;
            okNote.text = g.players.Count < 2 ? "" : left > 0 ? Lang.Tr("全員が OK になると、みんなで進みます（あと {0} 人）", left) : "";
        }

        /// <summary>
        /// この画面で決める枠か（JS版 ownSlot）。オンラインの相手の枠は相手の画面が、NPC はホストの画面が決める。
        /// </summary>
        bool OwnSlot(int i)
        {
            var P = g.players[i];
            if (P.src.kind == "net") return false;
            if (P.src.kind == "npc") return !g.NpOn || g.NetHost;
            return true;
        }

        string rokSent = "";
        /// <summary>この画面で決める枠の「準備OK」を、部屋の全員へ配る（変わったときだけ）。</summary>
        void RokSend()
        {
            if (!g.NpOn) return;
            var rows = new Newtonsoft.Json.Linq.JArray();
            for (int i = 0; i < ok.Count; i++) if (OwnSlot(i)) rows.Add(new Newtonsoft.Json.Linq.JArray(i, ok[i] ? 1 : 0));
            if (rows.Count == 0) return;
            string sig = rows.ToString(Newtonsoft.Json.Formatting.None);
            if (sig == rokSent) return;
            rokSent = sig;
            Net.Send(new Newtonsoft.Json.Linq.JObject { ["p"] = "rok", ["r"] = rows });
        }

        /// <summary>相手の「準備OK」が届いた（送ってきた人がその枠を持っているかを確かめる）。</summary>
        public void OnRok(int from, Newtonsoft.Json.Linq.JArray rows)
        {
            if (!Open) return;
            int host = Net.Room != null ? ((int?)Net.Room["host"] ?? -1) : -1;
            bool hit = false;
            foreach (Newtonsoft.Json.Linq.JArray row in rows)
            {
                int i = (int)row[0];
                if (i < 0 || i >= ok.Count || OwnSlot(i)) continue;
                int owner = i < g.np.ids.Count ? g.np.ids[i] : -1;
                if (!(owner == from || (owner == -1 && from == host))) continue;
                ok[i] = (int)row[1] != 0; hit = true;
            }
            if (hit) Refresh();
        }

        /// <summary>ホストの合図（rgo）。みんな同時にショップへ入る。</summary>
        public void OnGo() { if (!Open || went) return; went = true; g.hud.Wipe(() => g.AfterResult()); }

        public void Tick(float dt)
        {
            if (!Open) return;
            B.Tick(dt);
            if (kira != null) { float k = (Time.unscaledTime % 1.4f) / 1.4f; float v = Mathf.Sin(k * Mathf.PI); kira.color = new Color(1, 1, 1, v * v); kira.rectTransform.localScale = Vector3.one * (0.85f + 0.2f * v); }
            if (pressT > 0) { pressT -= dt; if (pressT <= 0) Refresh(); }
            if (went) return;
            bool redraw = false;
            bool armed = Time.unscaledTime - shownAt > g.T.menuLock;      // 開いた直後は少しのあいだ効かない
            bool multi = g.MultiLocal();
            for (int i = 0; i < g.players.Count; i++)
            {
                var P = g.players[i];
                if (!OwnSlot(i)) continue;                                  // 相手の画面が決める枠
                if (P.src.kind == "npc")
                {
                    if (ok[i]) continue;
                    npcT[i] -= dt;
                    if (npcT[i] <= 0) { ok[i] = true; redraw = true; }      // NPC は少し待って自分で OK（人を待たせない）
                    continue;
                }
                bool now = P.input.ok;
                if (now && !prevOk[i] && armed)
                {
                    // 同じ画面の2人以上は、それぞれの決定で自分のぶんだけ。1人はボタンを押したのと同じ
                    if (multi) { ok[i] = !ok[i]; pressT = Game.PRESS_SHOW; Game.SeUi(ok[i] ? "ok" : "back"); redraw = true; }
                    else PressMine();
                }
                prevOk[i] = now;
            }
            if (autoT > 0)
            {
                autoT -= dt;
                if (autoT <= 0) { for (int i = 0; i < ok.Count; i++) ok[i] = true; redraw = true; }
            }
            if (redraw) Refresh();
            RokSend();
            // ---- ショップへ。**みんなそろって同時に入る。**押した絵が見えるよう、そろってから少しだけ待つ
            if (!ok.TrueForAll(x => x)) { goT = 0; return; }
            if ((goT += dt) < Game.PRESS_SHOW) return;
            if (g.NpOn && !g.NetHost) return;                               // 進める合図はホストが出す
            went = true;
            if (g.NpOn) Net.Send(new Newtonsoft.Json.Linq.JObject { ["p"] = "rgo" });
            g.hud.Wipe(() => g.AfterResult());
        }

    }
}
