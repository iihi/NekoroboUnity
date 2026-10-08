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
        readonly RectTransform root;
        RectTransform box;
        public bool Open { get; private set; }

        readonly List<bool> ok = new List<bool>(), prevOk = new List<bool>();
        readonly List<float> npcT = new List<float>();
        readonly List<OkBtn> okBtns = new List<OkBtn>();
        Text okNote;
        float shownAt, autoT;
        bool went;

        class OkBtn { public Player P; public Image bg, chip; public Image ring; public Text nm, b; public bool mine; }

        // ---- 色（CSS の --ui*）
        static readonly Color CREAM = Mats.Hex(0xfffdf6), INK = Mats.Hex(0x3a352c), SUB = Mats.Hex(0x8a8274), LINE = Mats.Hex(0xe7ddc6);
        static readonly Color GREEN = Mats.Hex(0x4cbf68), GREEN_D = Mats.Hex(0x2f9448), RED = Mats.Hex(0xe8604a), RED_D = Mats.Hex(0xc33f2c);
        static readonly Color GOLD = Mats.Hex(0xf2b52c), GOLD_D = Mats.Hex(0xd18f10), BLUE = Mats.Hex(0x4aa8e0);
        const string YES = "#1a9e4b", NO = "#e53935";

        public ResultWin(RectTransform parent, Game game)
        {
            g = game;
            root = UiKit.Rect(parent, "Result");
            UiKit.Stretch(root);
            var bg = root.gameObject.AddComponent<RawImage>();
            bg.texture = BgTex(); bg.raycastTarget = true;
            root.gameObject.SetActive(false);
        }

        public void Close() { Open = false; root.gameObject.SetActive(false); }

        // ================================================================ 組み立て
        public void Show()
        {
            var R = g.result;
            if (box != null) Object.Destroy(box.gameObject);
            okBtns.Clear(); ok.Clear(); prevOk.Clear(); npcT.Clear();
            for (int i = 0; i < g.players.Count; i++) { ok.Add(false); prevOk.Add(true); npcT.Add(0.9f + i * 0.35f); }
            went = false;
            shownAt = Time.unscaledTime;
            autoT = g.T.resultTime > 0 ? g.T.resultTime : 0;

            bool many = g.players.Count > 1;
            bool wide = many && g.mode != "coop";
            // ---- 白い縁のクリーム色の箱
            var bi = UiKit.Img(root, CREAM, 20, "Box");
            box = bi.rectTransform;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f); box.pivot = new Vector2(0.5f, 0.5f);
            Ring(bi.transform, Color.white, 5, 20);
            var sh = bi.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.18f); sh.effectDistance = new Vector2(0, -10);
            var v = UiKit.VCol(bi.gameObject, 0, UiKit.Pad(29, 29, 23, 25), TextAnchor.UpperCenter);
            v.childForceExpandWidth = true;
            var le = UiKit.Size(bi, wide ? 1000 : 560); le.minWidth = le.preferredWidth;
            var fit = bi.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ---- 見出し（成否で色を変える大きな帯）と、ひとこと
            Heading(box, R.cleared);
            var cap = Txt(box, R.cleared ? "╲(^o^)╱" : (many ? "全員が動けなくなった" : "ダメージ100%で動けなくなった"), 14, SUB, TextAnchor.MiddleCenter);
            Gap(cap.transform.parent, 0); UiKit.Size(cap, -1, 32);

            if (wide)
            {
                var cols = UiKit.Rect(box, "Cols");
                var hr = UiKit.HRow(cols.gameObject, 16, null, TextAnchor.UpperLeft);
                hr.childForceExpandWidth = false;
                var colL = UiKit.Rect(cols, "ColL");
                UiKit.VCol(colL.gameObject, 0, null, TextAnchor.UpperLeft).childForceExpandWidth = true;
                UiKit.Size(colL, 1000 - 58 - 16 - 300, -1, 1);
                var colR = UiKit.Rect(cols, "ColR");
                UiKit.VCol(colR.gameObject, 12, null, TextAnchor.UpperLeft).childForceExpandWidth = true;
                UiKit.Size(colR, 300, -1, 0, 300);
                if (g.mode == "team") TeamRanks(colL); else VersusRanks(colL);
                if (R.cleared)
                {
                    var tp = Pane(colR);
                    TimeRows(tp);
                }
                var sp = Pane(colR);
                Sec2(sp, "お店ぜんたい");
                int doneN = 0; foreach (var o in g.orders) if (o.done) doneN++;
                Row(sp, "配達できた料理", doneN + " / " + g.orders.Count, null);
                Row(sp, "店舗損壊率", Mathf.RoundToInt(g.shopDmg) + "%", null);
                Row(sp, "倒れた客", g.guests.FindAll(x => x.hp <= 50).Count + "人", null, false);
                Fin(sp, g.mode == "team" ? "修理費と救急車の代金は、壊した人のチームが払っています"
                                         : "修理費と救急車の代金は、上の表のとおり壊した人が払っています");
                BonusCard(colR, true);
                OkButtons(colR, true);
                okNote = Txt(box, "", 12, SUB, TextAnchor.MiddleCenter);
                UiKit.Size(okNote, -1, 26);
            }
            else
            {
                if (R.cleared) TimeRows(box);
                if (many)
                {
                    // 協力：財布はひとつ。お店ひとつぶんの成績にして、誰がいくら売ったかを内訳に出す
                    Sec2(box, "配膳の内訳（協力：お店ひとつぶん）");
                    foreach (var P in g.players) Row(box, Dot(P) + Nm(P) + Out(P), P.delivered + "品　" + Game.Yen(P.sales), null);
                    Sec2(box, "お店の収支");
                }
                Ledger(box, g.LedgerOf(g.me), many);
                Row(box, "所持金", Game.Yen(g.WalletOf(g.me).cash), g.WalletOf(g.me).cash >= 0 ? "#111111" : NO, false);
                BonusCard(box, false);
                OkButtons(box, false);
                okNote = Txt(box, "", 12, SUB, TextAnchor.MiddleCenter);
                UiKit.Size(okNote, -1, 24);
            }
            root.gameObject.SetActive(true);
            Open = true;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(box);
            // 4人ぶん並ぶと縦に入りきらないことがある。JS版は箱の中を巻き取る（max-height:92vh）。ここは縮めて収める
            float h = box.rect.height;
            box.localScale = Vector3.one * (h > 662 ? 662f / h : 1f);
            Refresh();
        }

        void Heading(RectTransform parent, bool cleared)
        {
            var holder = UiKit.Rect(parent, "H2");
            var t = UiKit.Label(holder, cleared ? "STAGE CLEAR" : "FAILED…", 40, Color.white, true, TextAnchor.MiddleCenter);
            float w = t.preferredWidth + 88 + 10, h = 40 * 1.25f + 12 + 10;
            UiKit.Size(holder, -1, h + 10);
            var top = cleared ? Mats.Hex(0x63d47f) : Mats.Hex(0xff8f7c);
            var bot = cleared ? GREEN : RED;
            var dark = cleared ? GREEN_D : RED_D;
            var shd = UiKit.Img(holder, dark, h / 2, "Shadow");
            var bg = UiKit.Img(holder, Color.white, 0, "Bg");
            bg.sprite = UiKit.PillGrad(Mathf.RoundToInt(w), Mathf.RoundToInt(h), top, bot, 5);
            foreach (var r in new[] { shd.rectTransform, bg.rectTransform, t.rectTransform })
            {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 1); r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(w, h); r.anchoredPosition = Vector2.zero;
            }
            shd.rectTransform.anchoredPosition = new Vector2(0, -4);
            t.transform.SetAsLastSibling();
            var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = dark; s.effectDistance = new Vector2(0, -3);
        }

        void TimeRows(RectTransform parent)
        {
            var R = g.result;
            var row = Row(parent, "タイム", Game.FmtTime(g.frames), null);
            row.fontSize = 34; row.color = GOLD_D;
            row.transform.parent.GetComponent<LayoutElement>().preferredHeight = 50;
            if (R.rec) Row(parent, "記録", "NEW RECORD!", UiKit.Hex(GOLD_D));
            int best = PlayerPrefs.GetInt(g.BestKey(), 0);
            if (best > 0 && !R.rec) Row(parent, "ベスト", Game.FmtTime(best), null);
        }

        /// <summary>お店ひとつぶんの明細（1人・協力）。JS版 resultLedger。</summary>
        void Ledger(RectTransform parent, Game.Ledger L, bool many)
        {
            int doneN = 0; foreach (var o in g.orders) if (o.done) doneN++;
            Row(parent, "配達できた料理", (many ? doneN : g.me.delivered) + " / " + g.orders.Count, null);
            Row(parent, "売上", Game.Yen(L.sales), YES);
            if (L.best >= 2)
                Row(parent, "最高 " + L.best + "連鎖" + (L.wreck > 0 ? " → 大暴れボーナス" : "（" + g.T.wreckMin + "連鎖から付きます）"),
                    L.wreck > 0 ? Game.Yen(L.wreck) : "—", L.wreck > 0 ? "#ffb300" : "#8b93a0");
            Row(parent, "店舗損壊率 " + Mathf.RoundToInt(L.dmg) + "% → 修理費", Game.Yen(-L.repair), NO);
            Row(parent, "救急車 " + L.amb + "台" + (g.T.countAmb ? "" : "（合計に含めず）"), g.T.countAmb ? Game.Yen(-L.ambCost) : "—", NO, false);
            // 合計（上に太い線）
            var line = UiKit.Img(parent, LINE, 0, "TotLine"); UiKit.Size(line, -1, 3);
            var tot = Row(parent, "合計", Game.Yen(L.total), L.total >= 0 ? YES : NO, false);
            tot.fontSize = 23;
            tot.transform.parent.GetComponent<LayoutElement>().preferredHeight = 44;
            var lab = tot.transform.parent.GetChild(0).GetComponent<Text>(); lab.fontSize = 23;
            Fin(parent, L.total >= 0 ? "黒字！ このお金で本体を強化できる" : "赤字：足りないぶんは所持金から引かれた");
        }

        /// <summary>個人戦の順位（左の段）。勝ち負けは「差引」で決める。</summary>
        void VersusRanks(RectTransform parent)
        {
            var pane = Pane(parent);
            Sec2(pane, "順位（差引＝売上 −（自分が壊したぶんの）修理費・救急車）");
            var order = g.RankByTotal();
            for (int i = 0; i < order.Count; i++)
            {
                var P = order[i]; var L = g.LedgerOf(P); var W = g.WalletOf(P);
                var card = RankCard(pane, i, P.src.kind != "npc");
                RankHead(card, i, Dot(P) + "<size=17>" + Nm(P) + "</size>" + Out(P), L.total);
                Small(card, P.delivered + "品 " + Game.Yen(L.sales)
                      + (L.wreck > 0 ? " ／ 大暴れ" + P.bestCombo + "連鎖 " + Game.Yen(L.wreck) : "")
                      + " ／ 損壊" + Mathf.RoundToInt(L.dmg) + "% " + Game.Yen(-L.repair)
                      + (L.amb > 0 ? " ／ 救急車" + L.amb + "台 " + Game.Yen(-L.ambCost) : "")
                      + " ／ 機体" + Mathf.RoundToInt(P.botDmg) + "%", SUB);
                Small(card, "所持金 <color=" + (W.cash >= 0 ? "#111111" : NO) + ">" + Game.Yen(W.cash) + "</color>", INK, true);
            }
        }

        /// <summary>チーム戦の順位。チームの中で誰がいくら売ったかも添える。</summary>
        void TeamRanks(RectTransform parent)
        {
            var pane = Pane(parent);
            Sec2(pane, "チーム順位（差引＝チームの売上 − チームが壊したぶんの修理費・救急車）");
            var ts = g.TeamsInPlay();
            var led = new Dictionary<int, Game.Ledger>();
            foreach (var t in ts) led[t] = g.LedgerOf(g.TeamLead(t));
            ts.Sort((a, b) => led[b].total.CompareTo(led[a].total));
            for (int i = 0; i < ts.Count; i++)
            {
                var L = led[ts[i]]; var ms = g.players.FindAll(q => q.team == ts[i]);
                int dl = 0; foreach (var q in ms) dl += q.delivered;
                var card = RankCard(pane, i, true);
                RankHead(card, i, "<size=12>" + ms.Count + "人</size>", L.total, ts[i]);
                Small(card, dl + "品 " + Game.Yen(L.sales)
                      + (L.wreck > 0 ? " ／ 大暴れ" + L.best + "連鎖 " + Game.Yen(L.wreck) : "")
                      + " ／ 損壊" + Mathf.RoundToInt(L.dmg) + "% " + Game.Yen(-L.repair)
                      + (L.amb > 0 ? " ／ 救急車" + L.amb + "台 " + Game.Yen(-L.ambCost) : ""), SUB);
                foreach (var P in ms)
                    Small(card, Dot(P) + Nm(P) + "　" + P.delivered + "品 " + Game.Yen(P.sales) + " ／ 壊" + Mathf.RoundToInt(P.shopDmg) + "%" + Out(P), SUB);
                var W = g.wallets[ts[i]];
                Small(card, "チームの所持金 <color=" + (W.cash >= 0 ? "#111111" : NO) + ">" + Game.Yen(W.cash) + "</color>", INK, true);
            }
        }

        RectTransform RankCard(RectTransform parent, int i, bool human)
        {
            // 上位3つは金・銀・銅で縁取る。人が操作している機体は色を敷く
            Color bd = LINE, bg = Color.white;
            if (human) { bd = Mats.Hex(0xbfdcf5); bg = Mats.Hex(0xf2f8ff); }
            if (i == 0) { bd = GOLD; bg = Mats.Hex(0xfff6dc); }
            else if (i == 1) { bd = Mats.Hex(0xc6ccd4); bg = Mats.Hex(0xf6f8fa); }
            else if (i == 2) { bd = Mats.Hex(0xd9a877); bg = Mats.Hex(0xfdf4e9); }
            if (i > 0) Gap(parent, 8);
            var c = UiKit.Img(parent, bg, 14, "Rank" + (i + 1));
            Ring(c.transform, bd, 3, 14);
            var v = UiKit.VCol(c.gameObject, 2, UiKit.Pad(15, 15, 10, 11), TextAnchor.UpperLeft);
            v.childForceExpandWidth = true;
            return c.rectTransform;
        }

        void RankHead(RectTransform card, int i, string who, float total, int team = -1)
        {
            var row = UiKit.Rect(card, "Hd");
            UiKit.HRow(row.gameObject, 8, null, TextAnchor.MiddleLeft);
            UiKit.Size(row, -1, 26);
            var plc = i == 0 ? GOLD_D : i == 1 ? Mats.Hex(0x8f979f) : i == 2 ? Mats.Hex(0xb07f4f) : Mats.Hex(0xa7a094);
            Chip(row, (i + 1) + "位", plc, Color.white, 13, 11);
            if (team >= 0) Chip(row, Game.TEAM_NAME[team], Mats.Hex(Game.TEAM_HEX[team]), Color.white, 16, 10);   // チームの札
            UiKit.Label(row, who, 15, INK);
            var sp = UiKit.Rect(row, "Sp"); UiKit.Size(sp, 0, 1, 1);
            UiKit.Label(row, "<color=" + (total >= 0 ? YES : NO) + ">" + Game.Yen(total) + "</color>", 17, INK, true, TextAnchor.MiddleRight);
        }

        // ================================================================ 準備OK
        void OkButtons(RectTransform parent, bool column)
        {
            if (!column) Gap(parent, 14);              // 右の段に入れるときは、段の間隔（12）だけ
            var foot = UiKit.Rect(parent, "Foot");
            if (column) UiKit.VCol(foot.gameObject, 9, null, TextAnchor.UpperCenter).childForceExpandWidth = true;
            else { var h = UiKit.HRow(foot.gameObject, 9, null, TextAnchor.MiddleCenter); h.childForceExpandWidth = true; }
            bool multiLocal = g.MultiLocal();
            foreach (var P in g.players)
            {
                var b = new OkBtn { P = P, mine = P.src.kind != "npc" };
                var bg = UiKit.Img(foot, CREAM, 14, "Ok_" + P.name);
                b.bg = bg;
                b.ring = Ring(bg.transform, Color.white, 4, 14);
                var sh = bg.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.18f); sh.effectDistance = new Vector2(0, -4);
                var row = UiKit.HRow(bg.gameObject, 7, UiKit.Pad(20, 20, 14, 14), TextAnchor.MiddleLeft);
                var le = UiKit.Size(bg, -1, 52, 1, 150); le.minHeight = 52;
                var dot = UiKit.Img(bg.transform, Mats.Hex(P.col)); dot.sprite = UiKit.Ellipse;
                var dl = UiKit.Size(dot, 12, 12, 0, 12); dl.minHeight = 12; dl.flexibleHeight = 0;
                b.nm = UiKit.Label(bg.transform, P.Label, 14, INK);
                var sp = UiKit.Rect(bg.transform, "Sp"); UiKit.Size(sp, 0, 1, 1);
                var ch = Chip(bg.transform, "", Mats.Hex(0xdfe4ea), Mats.Hex(0x4a525e), 13, 9);
                b.chip = ch.transform.parent.GetComponent<Image>(); b.b = ch;
                if (b.mine)
                {
                    // マウスでも押せるように
                    var btn = bg.gameObject.AddComponent<Button>(); btn.targetGraphic = bg; bg.raycastTarget = true;
                    btn.navigation = new Navigation { mode = Navigation.Mode.None };
                    var cb = btn.colors; cb.highlightedColor = new Color(0.96f, 0.96f, 0.96f); btn.colors = cb;
                    int idx = g.players.IndexOf(P);
                    btn.onClick.AddListener(() => { if (Time.unscaledTime - shownAt > g.T.menuLock) { ok[idx] = !ok[idx]; Refresh(); } });
                }
                okBtns.Add(b);
            }
        }

        void Refresh()
        {
            bool multiLocal = g.MultiLocal();
            for (int i = 0; i < okBtns.Count; i++)
            {
                var b = okBtns[i]; bool on = ok[i];
                var pc = Mats.Hex(b.P.col);
                b.bg.color = on ? GREEN : CREAM;
                // 1人のときは JS版のカーソルの金色の枠（自分の札にいる）
                b.ring.color = on ? pc : (b.mine && !multiLocal ? Mats.Hex(0xffd23a) : Color.white);
                b.nm.color = on ? Color.white : INK;
                b.b.text = on ? "準備OK" : (b.mine ? (b.P.src.kind == "pad" ? "A" : "Enter") : "待っています");
                b.chip.color = on ? new Color(1, 1, 1, 0.9f) : Mats.Hex(0xdfe4ea);
                b.b.color = on ? GREEN_D : Mats.Hex(0x4a525e);
            }
            int left = 0; foreach (var o in ok) if (!o) left++;
            okNote.text = left > 0 ? "全員が「準備OK」になると、みんなでショップへ進みます（あと " + left + " 人）" : "ショップへ…";
        }

        public void Tick(float dt)
        {
            if (!Open || went) return;
            bool redraw = false;
            bool armed = Time.unscaledTime - shownAt > g.T.menuLock;      // 開いた直後は少しのあいだ効かない
            for (int i = 0; i < g.players.Count; i++)
            {
                var P = g.players[i];
                if (ok[i]) { prevOk[i] = true; continue; }
                if (P.src.kind == "npc")
                {
                    npcT[i] -= dt;
                    if (npcT[i] <= 0) { ok[i] = true; redraw = true; }      // NPC は少し待って自分で OK（人を待たせない）
                    continue;
                }
                bool now = P.input.ok;
                if (now && !prevOk[i] && armed) { ok[i] = true; redraw = true; }
                prevOk[i] = now;
            }
            // 自動送り。既定は 0（＝進まない）。0 より大きいときだけ数える
            if (autoT > 0)
            {
                autoT -= dt;
                if (autoT <= 0) { for (int i = 0; i < ok.Count; i++) ok[i] = true; redraw = true; }
            }
            if (redraw) Refresh();
            if (ok.TrueForAll(x => x))
            {
                went = true;
                g.AfterResult();
            }
        }

        // ================================================================ 部品
        static string Dot(Player P) { return "<color=" + UiKit.Hex(Mats.Hex(P.col)) + ">●</color> "; }
        static string Nm(Player P) { return P.src.kind == "npc" ? "NPC（" + NpcLevels.Get(P.src.lv).name + "）" : P.name; }
        static string Out(Player P)
        {
            return P.down ? "  <color=#e8604a><size=11>" + (P.revT > 0 ? "復帰 " + Mathf.CeilToInt(P.revT) : "リタイア") + "</size></color>" : "";
        }

        static Text Txt(RectTransform parent, string s, float size, Color c, TextAnchor al)
        {
            var t = UiKit.Label(parent, s, size, c, true, al);
            return t;
        }

        static void Gap(Transform parent, float h)
        {
            var r = UiKit.Rect(parent, "Gap"); var le = UiKit.Size(r, -1, h); le.minHeight = h;
        }

        /// <summary>左に名前、右に値の1行（下に薄い線）。値の Text を返す。</summary>
        static Text Row(RectTransform parent, string k, string val, string col, bool border = true)
        {
            var row = UiKit.Rect(parent, "L");
            UiKit.HRow(row.gameObject, 8, UiKit.Pad(0, 0, 5, 5), TextAnchor.MiddleLeft);
            UiKit.Size(row, -1, 30);
            UiKit.Label(row, k, 15, SUB);
            var sp = UiKit.Rect(row, "Sp"); UiKit.Size(sp, 0, 1, 1);
            var v = UiKit.Label(row, col != null ? "<color=" + col + ">" + val + "</color>" : val, 15, INK, true, TextAnchor.MiddleRight);
            if (border)
            {
                var ln = UiKit.Img(row, Mats.Hex(0xf2ede0), 0, "Line");
                var lr = ln.rectTransform; lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(1, 0); lr.pivot = new Vector2(0.5f, 0);
                lr.sizeDelta = new Vector2(0, 2); lr.anchoredPosition = Vector2.zero;
                ln.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            return v;
        }

        static void Fin(RectTransform parent, string s)
        {
            Gap(parent, 6);
            var t = UiKit.Label(parent, s, 12, SUB, true, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Size(t, -1, -1);
        }

        static void Small(RectTransform parent, string s, Color c, bool bold = false)
        {
            var t = UiKit.Label(parent, s, 12, c, bold);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        /// <summary>白い札（3px の縁、丸み16）。</summary>
        static RectTransform Pane(RectTransform parent)
        {
            var p = UiKit.Img(parent, Color.white, 16, "Pane");
            Ring(p.transform, LINE, 3, 16);
            var v = UiKit.VCol(p.gameObject, 0, UiKit.Pad(17, 17, 13, 15), TextAnchor.UpperLeft);
            v.childForceExpandWidth = true;
            return p.rectTransform;
        }

        /// <summary>青い丸い見出し（.sec2）。</summary>
        static void Sec2(RectTransform parent, string s)
        {
            var h = UiKit.Rect(parent, "Sec2");
            UiKit.Size(h, -1, 36);
            var bg = UiKit.Img(h, BLUE, 14, "Bg");
            var r = bg.rectTransform; r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            r.sizeDelta = new Vector2(0, 28); r.anchoredPosition = Vector2.zero;
            var t = UiKit.Label(bg.transform, s, 14, Color.white, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(t.rectTransform);
            var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.2f); sh.effectDistance = new Vector2(0, -1);
        }

        /// <summary>丸い札（中の字を返す）。</summary>
        static Text Chip(Transform parent, string s, Color bg, Color fg, float fs, int padX)
        {
            var im = UiKit.Img(parent, bg, 10, "Chip");
            UiKit.HRow(im.gameObject, 0, UiKit.Pad(padX, padX, 0, 0), TextAnchor.MiddleCenter);
            var le = UiKit.Size(im, -1, fs + 8); le.minHeight = fs + 8; le.flexibleHeight = 0;
            var t = UiKit.Label(im.transform, s, fs, fg);
            return t;
        }

        /// <summary>ステージ達成ボーナスの札（金色）。</summary>
        void BonusCard(RectTransform parent, bool column)
        {
            var R = g.result;
            if (R.bonuses.Count == 0) return;
            if (!column) Gap(parent, 12);
            var c = UiKit.Img(parent, Mats.Hex(0xfff0c0), 13, "Bonus");
            Ring(c.transform, GOLD, 3, 13);
            var sh = c.gameObject.AddComponent<Shadow>(); sh.effectColor = GOLD_D; sh.effectDistance = new Vector2(0, -3);
            // 狭い段（右の段）では1行ずつ縦に並べる（JS版は flex-wrap で折り返す）
            if (column) UiKit.VCol(c.gameObject, 4, UiKit.Pad(17, 17, 12, 12), TextAnchor.UpperLeft);
            else UiKit.HRow(c.gameObject, 16, UiKit.Pad(17, 17, 12, 12), TextAnchor.MiddleLeft);
            UiKit.Label(c.transform, "ステージ達成ボーナス", 15, Mats.Hex(0x5a4413));
            foreach (var b in R.bonuses)
                UiKit.Label(c.transform, (b.solo ? "" : b.team != null ? b.rank + "位 " + Game.TEAM_NAME[b.team.Value]
                                          : b.P != null ? b.rank + "位 " + Dot(b.P) + Nm(b.P) : (g.players.Count > 1 ? "みんなで" : ""))
                                    + " <color=#1a7f3c>" + Game.Yen(b.amt) + "</color>", 13, Mats.Hex(0x5a4413));
        }

        static Image Ring(Transform parent, Color c, int th, float radius)
        {
            var b = UiKit.Img(parent, c, 0, "Ring");
            b.sprite = UiKit.Ring(Mathf.Max(1, Mathf.RoundToInt(th * 15.5f / radius)));
            b.type = Image.Type.Sliced; b.pixelsPerUnitMultiplier = 15.5f / radius;
            UiKit.Stretch(b.rectTransform);
            b.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return b;
        }

        /// <summary>背景（空と芝生。radial-gradient(900px 260px at 20% 12%, 白 .5) ＋ 4色の縦の帯）。</summary>
        static Texture2D BgTex()
        {
            const int W = 160, H = 90;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color a = Mats.Hex(0x74c7ea), b = Mats.Hex(0xa8dcf0), c = Mats.Hex(0xcbe89a), d = Mats.Hex(0xa9d478);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float X = (x + 0.5f) / W * 1280, Y = (1 - (y + 0.5f) / H) * 720, v = Y / 720f;
                    var col = v < 0.48f ? Color.Lerp(a, b, v / 0.48f) : Color.Lerp(c, d, (v - 0.48f) / 0.52f);
                    float dd = Mathf.Sqrt(Mathf.Pow((X - 256) / 900f, 2) + Mathf.Pow((Y - 86) / 260f, 2));
                    float w = 0.5f * Mathf.Clamp01(1 - dd / 0.7f);
                    tex.SetPixel(x, y, Color.Lerp(col, Color.white, w));
                }
            tex.Apply(false);
            return tex;
        }
    }
}
