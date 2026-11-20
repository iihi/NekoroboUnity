using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// ラウンドマッチの最終結果（JS版 showFinal）。清書1113（資料/ネコ配膳_ラウンドマッチ結果画面清書1113.psd）。
    /// 部品の絵は HTML版の assets/ui/fin（tools/psd/cut1113.py）、ボタンはストーリークリアと同じ絵（assets/ui/story）。位置は PSD の半分。
    ///   左 … 個人戦は表彰台（1〜3位。ロボはそれぞれの機体をその場で撮った絵）と、その下に4位・リタイアの行。
    ///        チーム戦は青/赤の勝ち負け、1人・協力は順位ではなくお店ひとつぶんの結果。
    ///   右 … ラウンド詳細。札（タブ）4つのグラフと、ひとこと。札は LB / RB・↑で札の段へ入って ← →・押して替える。
    /// 順位は**利益（各ラウンドの差引）の合計**。同じ額なら同じ順位。途中で抜けた人（リタイア）は台に乗せない。
    /// 「累積売上」「ラウンド売上」の札は、順位と同じ利益（差引）で描く（台の金額とグラフの終わりがそろうように）。
    /// </summary>
    public class FinalWin
    {
        readonly Game g;
        readonly UiBoard B;
        public bool Open { get; private set; }
        readonly RawImage[] b0 = new RawImage[2], b1 = new RawImage[2]; readonly Texture[] b0Tex = new Texture[2];
        readonly RectTransform[] btnRt = new RectTransform[2]; System.Action[] act;
        int cur, hov = -1; float lockT, goT = -1; bool armed, pL, pR, pU, pD, pOk, pLB, pRB, room, auto;   // auto … 自動マッチングの部屋
        bool onTabs;                 // ↑で札の段を選んでいる
        int tab;                     // いまの札
        RectTransform det;           // 札・グラフ・ひとこと（札を替えたら描き直す）
        readonly RectTransform[] tabRt = new RectTransform[4];
        List<Ser> ser; string kind; int nRound;
        const string U = "ui/fin/", US = "ui/story/";
        static readonly Color NEG = Mats.Hex(0xff6b5b), YEL2 = Mats.Hex(0xffd23f), AXC = Mats.Hex(0xe8dfc6), YEL = Mats.Hex(0xffd400);
        static readonly string[] RES_COLS = { "blue", "red", "green", "orange" };
        static readonly Dictionary<string, Color> FIN_COL = new Dictionary<string, Color>
        { ["blue"] = Mats.Hex(0x38b4ff), ["red"] = Mats.Hex(0xff5b4c), ["green"] = Mats.Hex(0x4ccf6a), ["orange"] = Mats.Hex(0xffa53c) };
        // 札。on … 選んでいる札の絵、off … 選んでいない札の絵（左・上・幅・高さ）。x … 字の真ん中
        static readonly string[] TAB_K = { "sum", "round", "dmg", "amb" };
        static readonly string[] TAB_N = { "累積売上", "ラウンド売上", "店舗ダメージ", "昇天数" };
        static readonly float[] TAB_X = { 838.75f, 945.25f, 1051.75f, 1158.25f };
        static readonly float[][] TAB_ON = { new[] { 780f, 189, 121, 44 }, new[] { 886.5f, 189, 121, 44 }, new[] { 993f, 189, 121, 44 }, new[] { 1099.5f, 189, 121, 44 } };
        static readonly float[][] TAB_OFF = { new[] { 782.5f, 193, 117.5f, 41.5f }, new[] { 889f, 193, 117.5f, 41.5f }, new[] { 995.5f, 193, 117.5f, 41.5f }, new[] { 1102f, 193, 117.5f, 41.5f } };
        // 表彰台の上の場所（1位・2位・3位）。ロボの絵の箱・札・金額（左端と字の真ん中）・金額の色・順位の印の箱
        static readonly float[][] SP_ROBO = { new[] { 294.5f, 207, 96, 186 }, new[] { 113f, 247, 96, 186 }, new[] { 477f, 283.5f, 96, 186 } };
        static readonly float[][] SP_TAG = { new[] { 254.5f, 158.5f }, new[] { 72f, 201 }, new[] { 436.5f, 236 } };
        static readonly float[][] SP_AMT = { new[] { 317.5f, 169.5f }, new[] { 135f, 212 }, new[] { 499.5f, 247 } };
        static readonly int[] SP_COL = { 0xe5a40a, 0x8fa9a9, 0xbc7d54 };
        static readonly float[][] SP_RK = { new[] { 270.5f, 405, 144.5f, 88 }, new[] { 120.5f, 448.5f, 83, 88 }, new[] { 485f, 487, 83, 81 } };

        class Ser { public string tag; public Color col; public string ck; public float[] round, sum, dmg, amb; public float tot; }

        public FinalWin(RectTransform parent, Game game)
        {
            g = game;
            B = new UiBoard(parent, "Final", true, "ui/fin/bg.jpg");
        }

        public void Close() { Open = false; B.Active = false; }

        float SumOf(int i, string k = "tot")
        {
            float n = 0;
            foreach (var r in g.matchHist)
            {
                if (i >= r.per.Count) continue;
                var p = r.per[i];
                n += k == "sales" ? p.sales : k == "deliv" ? p.deliv : k == "cost" ? p.cost : p.tot;
            }
            return n;
        }
        static string Tag(Player P) { return (P.idx + 1) + "P"; }        // NPC も「〇P」（担当）
        string ColKey(Player P)
        {
            if (g.mode == "team" && g.players.Count > 1) return RES_COLS[Mathf.Clamp(P.team, 0, 3)];
            int i = System.Array.IndexOf(Player.PCOL, P.col);
            return i >= 0 ? RES_COLS[i] : "blue";
        }

        // ---------------------------------------------------------------- 字
        /// <summary>字。y は字の真ん中の高さ。al 0 … x が左端、1 … 右端、2 … 真ん中。mw を超える訳は縮める</summary>
        static Text T(RectTransform p, string s, float x, float y, float size, Color c, int al = 0, float mw = 0)
        {
            var t = UiBoard.T(p, s, x, y - size / 2, size, c, al);
            if (mw > 0) FitW(t, mw, size, y);
            return t;
        }
        static void FitW(Text t, float mw, float size, float cy)
        {
            float w = t.preferredWidth;
            if (w <= mw || w <= 0) return;
            float s2 = Mathf.Max(6, size * mw / w);
            t.fontSize = Mathf.RoundToInt(s2);
            var r = t.rectTransform;
            r.anchoredPosition = new Vector2(r.anchoredPosition.x, -(cy - s2 / 2) + s2 * 0.12f);
            r.sizeDelta = new Vector2(0, s2 * 1.2f);
        }

        /// <summary>人の札（色の絵と、黒い斜めの字）</summary>
        RectTransform TagBox(RectTransform p, Player P, float x, float y, float w = 60, float h = 21)
        {
            var r = UiBoard.Box(p, x, y, w, h, "Tag");
            UiBoard.Img(r, U + "tag_" + ColKey(P) + ".png", 0, 0, w, h);
            bool npc = P.src.kind == "npc";
            float fs = (npc ? 13 : 17) * w / 60;
            var t = T(r, Tag(P), w * 31 / 60, h / 2 + 0.5f, fs, Color.black, 2);
            t.fontStyle = FontStyle.Italic;          // CSS の skewX(-8deg) の代わり
            return r;
        }

        public void Show()
        {
            g.AchMatchEnd();
            // オンラインは、試合が終わったので同期を畳む（部屋には残る）
            room = g.NpOn && Net.Room != null;
            auto = room && (bool?)Net.Room["auto"] == true;
            g.NpEnd();
            B.Clear();
            var bx = B.box;
            var PL = g.players; bool many = PL.Count > 1;
            var H = g.matchHist;
            int rounds = g.matchRounds;
            nRound = H.Count;
            B.In(UiBoard.Img(bx, U + "title.png", 51, 45.5f, 536.5f, 56.5f), "slam", 0);
            ser = new List<Ser>();
            System.Func<int, string, string, Ser> serOf = (i, tag, ck) =>
            {
                var s = new Ser { tag = tag, ck = ck, col = FIN_COL.ContainsKey(ck) ? FIN_COL[ck] : Color.white, round = new float[H.Count], sum = new float[H.Count],
                                  dmg = new float[H.Count], amb = new float[H.Count], tot = SumOf(i) };
                float c = 0;
                for (int r = 0; r < H.Count; r++)
                {
                    var p = i < H[r].per.Count ? H[r].per[i] : null;
                    s.round[r] = p != null ? p.tot : 0; c += s.round[r]; s.sum[r] = c;
                    s.dmg[r] = p != null ? p.dmg : 0; s.amb[r] = p != null ? p.amb : 0;
                }
                return s;
            };
            var shots = new List<KeyValuePair<RawImage, Player>>(); var shotFace = new List<string>();
            if (many && g.mode == "team")
            {
                // ---- チーム戦：青と赤を並べて、勝ち負け
                kind = "team";
                var ts = g.TeamsInPlay();
                var tot = new List<float>();
                foreach (var t in ts) tot.Add(SumOf(PL.IndexOf(g.TeamLead(t))));
                float best = float.MinValue; foreach (var v in tot) best = Mathf.Max(best, v);
                int nb = 0; foreach (var v in tot) if (v == best) nb++;
                bool draw = nb > 1;
                float w = (570 - 20 * (ts.Count - 1)) / Mathf.Max(1, ts.Count);
                for (int k = 0; k < ts.Count; k++)
                {
                    int tm = ts[k];
                    string res = draw && tot[k] == best ? "DRAW" : tot[k] == best ? "WIN" : "LOSE";
                    var box = UiBoard.Box(bx, 50 + k * (w + 20), 150, w, 420, "Team");
                    var tc = Mats.Hex(Game.TEAM_HEX[tm]);
                    if (res == "WIN") UiBoard.Fill(box, YEL, -4, -4, w + 8, 428, 18);
                    UiBoard.Fill(box, tc, 0, 0, w, 420, 16);
                    UiBoard.Fill(box, new Color(tc.r * 0.3f, tc.g * 0.3f, tc.b * 0.3f, 0.92f), 3, 3, w - 6, 414, 14);
                    T(box, Lang.Tr(Game.TEAM_NAME[tm]), w / 2, 100, 34, Color.white, 2);
                    var rt = T(box, res, w / 2, 170, 84, res == "WIN" ? YEL : res == "LOSE" ? Mats.Hex(0x9fb0c0) : Color.white, 2);
                    rt.fontStyle = FontStyle.Italic;
                    if (res == "WIN") { var s2 = rt.gameObject.AddComponent<Shadow>(); s2.effectColor = Mats.Hex(0x7a4b00); s2.effectDistance = new Vector2(0, -5); }
                    var ft = T(box, Game.Yen(tot[k]), w / 2, 262, 46, tot[k] < 0 ? NEG : YEL, 2);
                    float tk = tot[k]; B.Count(ft, tk, v => Game.Yen(v));
                    Members(box, PL.FindAll(q => q.team == tm), w / 2, 330);
                    B.In(box, "up", 2 + k);
                }
                foreach (var t in ts) ser.Add(serOf(PL.IndexOf(g.TeamLead(t)), Lang.Tr(t == 1 ? "赤" : "青"), RES_COLS[Mathf.Clamp(t, 0, 3)]));
            }
            else if (many && g.mode == "versus")
            {
                // ---- 個人戦：表彰台（1〜3位）。4位とリタイアは台の下の行
                kind = "versus";
                var tot = new List<float>(); for (int i = 0; i < PL.Count; i++) tot.Add(SumOf(i));
                var live = new List<int>(); var outs = new List<int>();
                for (int i = 0; i < PL.Count; i++) (PL[i].quit ? outs : live).Add(i);
                var order = new List<int>(live);
                order.Sort((a, b) => tot[b].CompareTo(tot[a]));
                System.Func<int, int> rankOf = i => { int rk = 1; foreach (var j in live) if (tot[j] > tot[i]) rk++; return rk; };
                B.In(UiBoard.Img(bx, U + "podium.png", 61, 331.5f, 562.5f, 222), "up", 1);
                for (int k = 0; k < order.Count && k < 3; k++)
                {
                    int i = order[k]; var P = PL[i]; int rk = Mathf.Min(rankOf(i), 3);
                    var robo = UiBoard.Img(bx, "", SP_ROBO[k][0], SP_ROBO[k][1], SP_ROBO[k][2], SP_ROBO[k][3], "Robo" + k);
                    shots.Add(new KeyValuePair<RawImage, Player>(robo, P)); shotFace.Add(rk == 1 ? "happy" : "norm");
                    B.In(robo, "up", 4 - k);
                    // 順位の印は、その場所の印の真ん中・下にそろえる（同じ額で2人が1位なら、2位の場所にも1位の印）
                    var sr = SP_RK[k]; var rr = SP_RK[rk - 1];
                    var rki = UiBoard.Img(bx, U + "rank" + rk + ".png", sr[0] + sr[2] / 2 - rr[2] / 2, sr[1] + sr[3] - rr[3], rr[2], rr[3]);
                    B.In(rki, "pop", 6 - k);
                    var tg = TagBox(bx, P, SP_TAG[k][0], SP_TAG[k][1]);
                    B.In(tg, "left", 5 - k);
                    var amt = T(bx, Game.Yen(tot[i]), SP_AMT[k][0], SP_AMT[k][1], 34, tot[i] < 0 ? NEG : Mats.Hex(SP_COL[k]));
                    float tv = tot[i]; B.Count(amt, tv, v => Game.Yen(v));
                    B.In(amt, "left", 5 - k);
                }
                // 台の下の行：4位と、途中で抜けた人
                var low = new List<KeyValuePair<int, string>>();
                for (int k = 3; k < order.Count; k++) low.Add(new KeyValuePair<int, string>(order[k], Lang.Tr("{0}位", rankOf(order[k]))));
                foreach (var i in outs) low.Add(new KeyValuePair<int, string>(i, Lang.Tr("リタイア")));
                for (int n = 0; n < low.Count; n++)
                {
                    float y = 577 + n * 26;
                    var row = UiBoard.Box(bx, 0, 0, 1280, 720, "Low");
                    T(row, low[n].Value, 75, y, 20, Color.white, 0, 62);
                    TagBox(row, PL[low[n].Key], 142, y - 10.5f);
                    T(row, Game.Yen(tot[low[n].Key]), 231, y, 18, tot[low[n].Key] < 0 ? NEG : Color.white);
                    B.In(row, "left", 7 + n);
                }
                for (int i = 0; i < PL.Count; i++) ser.Add(serOf(i, Tag(PL[i]), ColKey(PL[i])));
            }
            else
            {
                // ---- 1人・協力：順位は出さず、お店ひとつぶんの結果だけ
                kind = "solo";
                float tot = SumOf(0); int deliv = 0; for (int i = 0; i < PL.Count; i++) deliv += (int)SumOf(i, "deliv");
                int clr = 0; foreach (var r in H) if (r.frames > 0) clr++;
                var box = UiBoard.Box(bx, 50, 150, 570, 420, "Solo");
                UiBoard.Fill(box, new Color(8 / 255f, 20 / 255f, 32 / 255f, 0.65f), 0, 0, 570, 420, 16);
                float y = many ? 56 : 78;
                T(box, Lang.Tr(many ? "みんなで稼いだ利益" : "稼いだ利益") + "<size=18><color=#cfd8de>  " + Lang.Tr("（{0}ラウンドの合計）", rounds) + "</color></size>", 285, y, 30, Color.white, 2);
                var ft = T(box, Game.Yen(tot), 285, y + 98, 96, tot < 0 ? NEG : YEL, 2);
                var fsh = ft.gameObject.AddComponent<Shadow>(); fsh.effectColor = Mats.Hex(0x7a4b00); fsh.effectDistance = new Vector2(0, -5);
                B.Count(ft, tot, v => Game.Yen(v));
                float cost = SumOf(0, "cost");
                string[][] st =
                {
                    new[] { "売上", Game.Yen(SumOf(0, "sales")) }, new[] { "修理費・救急車", (cost != 0 ? "−" : "") + Game.Yen(cost) },
                    new[] { "配膳数", Lang.Tr("{0}品", deliv) }, new[] { "クリア", Lang.Tr("{0} / {1}ラウンド", clr, rounds) },
                };
                float sy = y + 170, cw = (570 - 72 - 40) / 2f;
                for (int i = 0; i < 4; i++)
                {
                    float x = 36 + (i % 2) * (cw + 40), yy = sy + (i / 2) * 50;
                    T(box, Lang.Tr(st[i][0]), x, yy + 16, 19, Mats.Hex(0xcfd8de));
                    T(box, st[i][1], x + cw, yy + 13, 26, Color.white, 1);
                    UiBoard.Fill(box, new Color(1, 1, 1, 0.18f), x, yy + 36, cw, 2);
                }
                if (many) Members(box, PL, 285, sy + 110);
                B.In(box, "left", 2);
                var s0 = serOf(0, Lang.Tr("利益"), "orange"); s0.col = Mats.Hex(0xffd84a);
                ser.Add(s0);
            }
            // ---- 右：ラウンド詳細（見出し。札・枠・グラフ・ひとことは DrawDet）
            {
                var hd = UiBoard.Box(bx, 0, 0, 1280, 720, "Head");
                UiBoard.Img(hd, U + "icon.png", 840.5f, 142, 21, 25);
                T(hd, Lang.Tr("ラウンド詳細"), 869, 152.5f, 33, Color.white, 0, 220);
                UiBoard.Img(hd, U + "key_lb.png", 1100, 140.5f, 24.5f, 24);
                T(hd, "/", 1133, 152.5f, 19, Color.white, 2);
                UiBoard.Img(hd, U + "key_rb.png", 1141, 140.5f, 24.5f, 24);
                T(hd, Lang.Tr("で切替"), 1171.5f, 151, 19, Color.white, 0, 70);
                B.In(hd, "right", 2);
            }
            det = UiBoard.Box(bx, 0, 0, 1280, 720, "Det");
            if (tab < 0 || tab > 3) tab = 0;
            DrawDet();
            B.In(det, "right", 3);
            // ---- ボタン（ストーリークリアと同じ絵）。オンラインは部屋へ戻る（ホストがもう一度始められる）
            act = new System.Action[] { Again, ToTitle };
            Btn(0, "retry", room ? (auto ? "もう一度さがす" : "部屋へ戻る") : "もう一度プレイする", 484.5f, 466.5f, 9);
            Btn(1, "title", "タイトルへ", 959.5f, 260, 10);
            cur = 0; onTabs = false; lockT = g.T.menuLock; armed = false; goT = -1;
            Focus(0);
            B.Active = true;
            Open = true;
            B.StartIn();
            // 表彰台のロボは、それぞれの機体を撮って置く（1位は笑顔）
            for (int k = 0; k < shots.Count; k++) RoboShot(shots[k].Key, shots[k].Value, shotFace[k], SP_ROBO[k]);
        }

        /// <summary>メンバーの並び（札と配膳数）。まん中を cx に</summary>
        void Members(RectTransform p, List<Player> ms, float cx, float y)
        {
            float one = 48 + 6 + 52, w = one * ms.Count + 18 * (ms.Count - 1);
            float x = cx - w / 2;
            foreach (var q in ms)
            {
                TagBox(p, q, x, y, 48, 17);
                T(p, Lang.Tr("{0}品", (int)SumOf(g.players.IndexOf(q), "deliv")), x + 54, y + 9, 19, Color.white);
                x += one + 18;
            }
        }

        // ---------------------------------------------------------------- 札・グラフ・ひとこと
        void DrawDet()
        {
            foreach (Transform c in det) Object.Destroy(c.gameObject);
            string k = TAB_K[tab];
            // 札。選んでいない札は枠の下、選んでいる札は枠の上
            for (int i = 0; i < 4; i++)
            {
                if (i == tab) continue;
                var o = TAB_OFF[i];
                var r = UiBoard.Box(det, o[0], o[1], o[2], o[3], "Tab" + i);
                var im = UiBoard.Img(r, U + "tab_" + TAB_K[i] + ".png", 0, 0, o[2], o[3]);
                if (onTabs && i == tab) im.texture = UiBoard.Bright(im.texture);
                T(r, Lang.Tr(TAB_N[i]), TAB_X[i] - o[0], 211.25f - o[1], 15, Color.white, 2, 76);
                tabRt[i] = r;
            }
            UiBoard.Img(det, U + "panel.png", 650, 223.5f, 564, 325);
            {
                var o = TAB_ON[tab];
                var r = UiBoard.Box(det, o[0], o[1], o[2], o[3], "Tab" + tab);
                var im = UiBoard.Img(r, U + "tab_" + k + "_on.png", 0, 0, o[2], o[3]);
                if (onTabs) im.texture = UiBoard.Bright(im.texture, 1.12f);
                T(r, Lang.Tr(TAB_N[tab]), TAB_X[tab] - o[0], 211.25f - o[1], 15, Color.white, 2, 76);
                tabRt[tab] = r;
            }
            string tt = k == "sum" ? "累計の利益の推移" : k == "round" ? "ラウンドごとの利益" : k == "dmg" ? "ラウンドごとの店舗ダメージ" : "ラウンドごとの昇天数";
            T(det, Lang.Tr(tt), 675.5f, 262.25f, 20, YEL2, 0, 500);
            Chart(UiBoard.Box(det, 672, 285.5f, 522, 197, "Chart"), k);
            UiBoard.Img(det, U + "mvp_plate.png", 721.5f, 498, 473, 34);
            UiBoard.Img(det, U + "mvp_crown.png", 669, 500, 48.5f, 28);
            T(det, Word(k), 745.5f, 515.25f, 16, YEL2, 0, 440);
        }

        static string Fmt(string k, float v) { return k == "dmg" ? (Mathf.Round(v * 10) / 10) + "%" : k == "amb" ? Mathf.RoundToInt(v).ToString() : Game.Yen(v); }
        float[] Vals(Ser s, string k) { return k == "sum" ? s.sum : k == "round" ? s.round : k == "dmg" ? s.dmg : s.amb; }

        /// <summary>線（太さ w）。x, y は箱の左上からの位置</summary>
        static void Line(RectTransform p, float x1, float y1, float x2, float y2, float w, Color c)
        {
            float dx = x2 - x1, dy = y2 - y1, len = Mathf.Sqrt(dx * dx + dy * dy);
            var r = UiKit.Rect(p, "L");
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2((x1 + x2) / 2, -(y1 + y2) / 2); r.sizeDelta = new Vector2(len, w);
            r.localEulerAngles = new Vector3(0, 0, -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
            var im = r.gameObject.AddComponent<Image>(); im.color = c; im.raycastTarget = false;
        }
        static void Dot(RectTransform p, float x, float y, Color c)
        {
            foreach (var o in new[] { new { r = 5.3f, c = c }, new { r = 2.7f, c = Color.white } })
            {
                var im = UiBoard.Box(p, x - o.r, y - o.r, o.r * 2, o.r * 2, "D").gameObject.AddComponent<Image>();
                im.sprite = Overhead.Circle(false); im.color = o.c; im.raycastTarget = false;
            }
        }

        /// <summary>グラフ（箱 522×197）。右の列に、線の終わりの値の札（ダメージ・昇天数はラウンドの合計）</summary>
        void Chart(RectTransform cv, string k)
        {
            float W = 522, Hh = 197; int n = Mathf.Max(1, nRound);
            float L = 66, Rt = 392, Tp = 40, Bt = 168;
            float lo = 0, hi = 0;
            foreach (var s in ser) foreach (var v in Vals(s, k)) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            if (hi == lo) hi = lo + (k == "dmg" ? 10 : k == "amb" ? 4 : 1000);
            // 目盛り（1・2・2.5・5 の刻みで4つほど）
            float raw = (hi - lo) / 4, mag = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(raw)));
            float stp = raw;
            foreach (var m in new[] { 1f, 2, 2.5f, 5, 10 }) if (m * mag >= raw) { stp = m * mag; break; }
            float step = k == "amb" ? Mathf.Max(1, Mathf.Ceil(stp)) : stp;
            float y0 = Mathf.Floor(lo / step) * step, y1 = Mathf.Ceil(hi / step) * step;
            System.Func<int, float> X = r => n == 1 ? (L + Rt) / 2 : L + (Rt - L) * r / (n - 1);
            System.Func<float, float> Y = v => Bt - (Bt - Tp) * (v - y0) / Mathf.Max(1e-6f, y1 - y0);
            string un = k == "dmg" ? "ダメージ（％）" : k == "amb" ? "昇天数（人）" : "利益（円）";
            T(cv, Lang.Tr(un), 2, Tp - 22, 13, AXC);
            // 凡例
            for (int i = 0; i < ser.Count; i++)
            {
                float x = 170 + i * 58;
                Line(cv, x, 10, x + 20, 10, 2, ser[i].col);
                Dot(cv, x + 10, 10, ser[i].col);
                T(cv, ser[i].tag, x + 25, 10, 14, Color.white);
            }
            // 目盛りと格子
            for (float v = y0; v <= y1 + step / 2; v += step)
            {
                float y = Y(v);
                Line(cv, L, y, Rt, y, 1, Mathf.Abs(v) < 1e-6f ? new Color(1, 1, 1, 0.55f) : new Color(1, 1, 1, 0.16f));
                Line(cv, L - 4, y, L, y, 1.4f, Color.white);
                T(cv, Fmt(k, v), L - 7, y, 13, Color.white, 1);
            }
            for (int r = 0; r < n; r++)
            {
                float x = X(r);
                Line(cv, x, Tp, x, Bt, 1, new Color(1, 1, 1, 0.12f));
                Line(cv, x, Bt, x, Bt + 4, 1.4f, Color.white);
                T(cv, "R" + (r + 1), x, Bt + 15, 14, Color.white, 2);
            }
            Line(cv, L, Tp, L, Bt, 1.4f, Color.white);
            Line(cv, L, Bt, Rt + 14, Bt, 1.4f, Color.white);
            T(cv, Lang.Tr("ラウンド"), Rt + 16, Bt + 13, 13, AXC);
            // 線（下の人から描いて、上位を上に重ねる）
            bool sum = k == "dmg" || k == "amb";
            System.Func<Ser, float> fin = s => { var a = Vals(s, k); if (sum) { float t = 0; foreach (var v in a) t += v; return t; } return a.Length > 0 ? a[a.Length - 1] : 0; };
            var ord = new List<Ser>(ser); ord.Sort((a, b) => fin(a).CompareTo(fin(b)));
            foreach (var s in ord)
            {
                var a = Vals(s, k);
                for (int r = 1; r < a.Length; r++) Line(cv, X(r - 1), Y(a[r - 1]), X(r), Y(a[r]), 2.6f, s.col);
            }
            foreach (var s in ord) { var a = Vals(s, k); for (int r = 0; r < a.Length; r++) Dot(cv, X(r), Y(a[r]), s.col); }
            // 右の札。線の終わりの高さに置き、重ならないように詰める
            var tags = new List<KeyValuePair<Ser, float[]>>();      // [線の終わりの y, 札の y]
            foreach (var s in ser) { var a = Vals(s, k); tags.Add(new KeyValuePair<Ser, float[]>(s, new[] { Y(a.Length > 0 ? a[a.Length - 1] : 0), 0 })); }
            tags.Sort((a, b) => a.Value[0].CompareTo(b.Value[0]));
            float GAP = 21, top = 18 + (sum ? 14 : 0), bot = Hh - 12;
            for (int i = 0; i < tags.Count; i++) tags[i].Value[1] = Mathf.Max(tags[i].Value[0], i > 0 ? tags[i - 1].Value[1] + GAP : top);
            for (int i = tags.Count - 1; i >= 0; i--) { float lim = bot - (tags.Count - 1 - i) * GAP; if (tags[i].Value[1] > lim) tags[i].Value[1] = lim; }
            if (sum) T(cv, Lang.Tr("合計"), 468, top - 12, 13, AXC, 2);
            foreach (var t in tags)
            {
                var c = t.Key.col; float x = 418, y = t.Value[1], ex = X(n - 1), ey = t.Value[0];
                Line(cv, ex + 5, ey, x - 8, ey, 1.4f, c);
                Line(cv, x - 8, ey, x, y, 1.4f, c);
                UiBoard.Fill(cv, c, x - 1, y - 10, 102, 20, 5, "Lb");
                UiBoard.Fill(cv, Mats.Hex(0x2a2416), x + 0.8f, y - 8.2f, 98.4f, 16.4f, 3.5f, "Lb");
                UiBoard.Fill(cv, c, x + 1, y - 8, 28, 16, 3, "Lb");
                T(cv, t.Key.tag, x + 15, y + 0.5f, 12, Color.black, 2, 26);
                T(cv, Fmt(k, fin(t.Key)), x + 96, y + 0.5f, 13, Color.white, 1);
            }
        }

        /// <summary>ひとこと（王冠の帯）。札ごとに、いちばん目立つことを1つ（JS版 finWord と同じ）</summary>
        string Word(string k)
        {
            bool solo = kind == "solo"; int n = nRound;
            System.Func<List<Ser>, string> join = a => { var s = new List<string>(); foreach (var o in a) s.Add(o.tag); return string.Join("・", s); };
            if (k == "sum")
            {
                if (solo) { float v = ser[0].tot; return v >= 0 ? Lang.Tr("{0}ラウンドで{1}稼ぎました！", n, Game.Yen(v)) : Lang.Tr("{0}ラウンドで{1}の赤字でした…", n, Game.Yen(-v)); }
                float best = float.MinValue; foreach (var o in ser) best = Mathf.Max(best, o.tot);
                var top = ser.FindAll(o => o.tot == best);
                if (kind == "team") return top.Count > 1 ? Lang.Tr("引き分け！") : Lang.Tr("{0}の勝ち！", top[0].tag);
                if (top.Count > 1) return Lang.Tr("{0}が同点で優勝！", join(top));
                var w = top[0];
                int led = -1; bool always = true;
                for (int r = 0; r < n; r++)
                {
                    float m = float.MinValue; foreach (var o in ser) m = Mathf.Max(m, o.sum[r]);
                    int cnt = 0; foreach (var o in ser) if (o.sum[r] == m) cnt++;
                    bool lead = w.sum[r] == m && cnt == 1;
                    if (!lead) { always = false; led = -1; } else if (led < 0) led = r;
                }
                if (always && n > 1) return Lang.Tr("{0}が最初から最後までトップで優勝！", w.tag);
                if (led > 0) return Lang.Tr("{0}がR{1}で逆転して優勝！", w.tag, led + 1);
                return Lang.Tr("{0}が優勝！", w.tag);
            }
            if (k == "round")
            {
                Ser bs = null; float bv = 0; int br = 0;
                foreach (var o in ser) for (int r = 0; r < o.round.Length; r++) if (bs == null || o.round[r] > bv) { bs = o; bv = o.round[r]; br = r; }
                if (bs == null) return "";
                return solo ? Lang.Tr("いちばん稼いだのはR{0}（{1}）", br + 1, Game.Yen(bv)) : Lang.Tr("いちばん稼いだのはR{0}の{1}（{2}）", br + 1, bs.tag, Game.Yen(bv));
            }
            var tot = new List<float>(); float mx = 0;
            foreach (var o in ser) { float t = 0; foreach (var v in (k == "dmg" ? o.dmg : o.amb)) t += v; tot.Add(t); mx = Mathf.Max(mx, t); }
            var most = new List<Ser>(); for (int i = 0; i < ser.Count; i++) if (tot[i] == mx) most.Add(ser[i]);
            if (k == "dmg")
            {
                if (mx <= 0) return Lang.Tr("お店はまったく壊れませんでした！");
                if (solo) return Lang.Tr("店舗ダメージは合計{0}", Fmt("dmg", mx));
                return Lang.Tr("いちばんお店を壊したのは{0}（合計{1}）", join(most), Fmt("dmg", mx));
            }
            if (mx <= 0) return Lang.Tr("昇天した客はいませんでした！");
            if (solo) return Lang.Tr("昇天した客は合計{0}人", Mathf.RoundToInt(mx));
            return Lang.Tr("いちばん客を昇天させたのは{0}（{1}人）", join(most), Mathf.RoundToInt(mx));
        }

        void SetTab(int i)
        {
            int n = 4;
            int t = ((i % n) + n) % n;
            if (t == tab) { DrawDet(); return; }
            tab = t;
            Game.SeUi("cur");
            DrawDet();
        }

        // ---------------------------------------------------------------- 表彰台のロボを撮る
        const int LAYER = 31;                 // 撮るときだけ使う層（料理の写真と同じ。ほかのカメラには映らない）
        void RoboShot(RawImage img, Player P, string face, float[] box)
        {
            if (P.look == null || P.look.root == null) return;
            const int W = 288, Hh = 558;
            string keep = P.look.faceNow;
            P.look.DrawFace(face);
            P.look.DrawMsg("");                  // 胴の画面のセリフは消す（試合は終わっている）
            // 写すのは機体の親（robot）の下の子ぜんぶ。内蔵の見た目（Look）と、届いたモデル（Model_robot_1p〜4p）が並んでいる。
            // 親そのものは物理の部品を持つので写さない
            var src = P.look.root.transform.parent ?? P.look.root.transform;
            var o = new GameObject("RoboShot");
            o.transform.position = new Vector3(0, -1000, 0);
            o.transform.rotation = Quaternion.identity;
            o.transform.localScale = src.lossyScale;
            Transform lookCopy = null;
            foreach (Transform c in src)
            {
                if (!c.gameObject.activeSelf) continue;
                var cc = Object.Instantiate(c.gameObject, o.transform, false);
                if (c == P.look.root.transform) lookCopy = cc.transform;
            }
            // 運んでいる皿は外す（Look の中の同じ場所にある物）
            if (P.look.plate != null && lookCopy != null)
            {
                string path = P.look.plate.name; var t = P.look.plate.parent;
                while (t != null && t != P.look.root.transform) { path = t.name + "/" + path; t = t.parent; }
                var pl = lookCopy.Find(path); if (pl != null) pl.gameObject.SetActive(false);
            }
            foreach (var t in o.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LAYER;
            foreach (var c in o.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            bool any = false; var b = new Bounds();
            foreach (var r in o.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) { Object.DestroyImmediate(o); P.look.DrawFace(keep); return; }
            var cg = new GameObject("RoboShotCam");
            var cam = cg.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.fieldOfView = 20; cam.nearClipPlane = 0.01f; cam.farClipPlane = 50; cam.cullingMask = 1 << LAYER; cam.allowHDR = false;
            var rt = new RenderTexture(W, Hh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.aspect = W / (float)Hh;
            // 白い機体が灰色にくすまないよう、明るめに当てる（前から1つ・上から1つ）
            var lights = new List<GameObject>();
            System.Action<Vector3, float> light = (from, k) =>
            {
                var lg = new GameObject("RoboShotLight"); var l = lg.AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = k; l.cullingMask = 1 << LAYER; l.shadows = LightShadows.None;
                lg.transform.rotation = Quaternion.LookRotation(-from.normalized);
                lights.Add(lg);
            };
            light(Coord.W(0.8f, 3, -2.4f), 1.1f);
            light(Coord.W(-1.5f, 1, -3), 0.7f);
            // 前は three.js の -Z（Unity の +Z）。少し右・少し上から（PSD の仮の絵の向き）
            var dir = Coord.W(-0.2f, 0.28f, -1).normalized;
            float th = Mathf.Tan(cam.fieldOfView / 2 * Mathf.Deg2Rad);
            float d = Mathf.Max(b.size.y / 2 / th, Mathf.Max(b.size.x, b.size.z) / 2 / (th * cam.aspect)) * 1.18f;
            cam.transform.position = b.center + dir * d;
            cam.transform.LookAt(b.center);
            var amb = RenderSettings.ambientLight; var fog = RenderSettings.fog;
            RenderSettings.fog = false;
#if UNITY_EDITOR
            bool asyncWas = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            cam.Render();
            UnityEditor.ShaderUtil.allowAsyncCompilation = asyncWas;
#else
            cam.Render();
#endif
            RenderSettings.fog = fog; RenderSettings.ambientLight = amb;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, Hh, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, Hh), 0, 0); tex.Apply(false);
            RenderTexture.active = prev;
            Object.DestroyImmediate(o); Object.Destroy(cg); foreach (var lg in lights) Object.Destroy(lg); rt.Release(); Object.Destroy(rt);
            P.look.DrawFace(string.IsNullOrEmpty(keep) ? "norm" : keep);
            // 透けている所を切り落とし、箱の下にそろえて置く（縦横比はそのまま）
            var px = tex.GetPixels32();
            int x0 = W, y0 = Hh, x1 = -1, y1 = -1;
            for (int y = 0; y < Hh; y++) for (int x = 0; x < W; x++) if (px[y * W + x].a > 8)
                    { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (x1 < 0) { Object.Destroy(tex); return; }
            int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
            var outT = new Texture2D(cw, ch, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            outT.SetPixels(tex.GetPixels(x0, y0, cw, ch)); outT.Apply(false);
            Object.Destroy(tex);
            img.texture = outT; img.color = Color.white;
            float k2 = Mathf.Min(box[2] / cw, box[3] / ch), w2 = cw * k2, h2 = ch * k2;
            var r2 = img.rectTransform;
            r2.anchoredPosition = new Vector2(box[0] + (box[2] - w2) / 2, -(box[1] + box[3] - h2));
            r2.sizeDelta = new Vector2(w2, h2);
            var sh = img.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.35f); sh.effectDistance = new Vector2(0, -6);
        }

        // ---------------------------------------------------------------- ボタン
        void Btn(int i, string f, string label, float x, float w, int order)
        {
            var r = UiBoard.Box(B.box, x, 591, w, 83, "Btn_" + f);
            b0[i] = UiBoard.Img(r, US + "btn_" + f + ".png", 0, 0, w, 83);
            b0Tex[i] = b0[i].texture;
            b1[i] = UiBoard.Img(r, US + "btn_" + f + "_on.png", 0, 0, w, 83);
            b1[i].color = new Color(1, 1, 1, 0);
            UiBoard.T(r, Lang.Tr(label), w / 2, 35.5f - 13, 26, Color.white, 2);
            btnRt[i] = r;
            B.In(r, "pop", order);
        }

        void Focus(int i)
        {
            i = Mathf.Clamp(i, 0, 1);
            if (i != cur && Open) Game.SeUi("cur");
            cur = i;
            // 選んでいるときは地を少し明るく（札の段を選んでいる間は、ボタンは明るくしない）
            // マウスが乗っているボタンも明るく（選ぶのは押したとき）
            for (int k = 0; k < 2; k++) if (b0Tex[k] != null) b0[k].texture = (k == cur && !onTabs) || k == hov ? UiBoard.Bright(b0Tex[k]) : b0Tex[k];
        }

        /// <summary>もう一度。ローカルは同じ人数・ルール・ラウンド数で ROUND 1 の面選びへ。オンラインは部屋の待機画面へ</summary>
        void Again()
        {
            g.matchHist.Clear(); g.matchRounds = 0;
            // 自動マッチングの部屋は、出て新しく探す（同じ顔ぶれで続けたいときはプライベートで）
            if (Net.Room != null && (bool?)Net.Room["auto"] == true) { Net.LeaveRoom(); g.NpEnd(); g.hud.OpenTitle("Online"); g.hud.MmAgain(); return; }
            if (Net.Room != null) { g.hud.OpenTitle("Wait"); return; }
            g.hud.OpenTitle("Free", false);
        }
        void ToTitle()
        {
            if (Net.Room != null) Net.LeaveRoom();
            g.NpEnd();
            g.matchHist.Clear(); g.matchRounds = 0;
            g.BackToTitle();
        }

        public void Tick(float dt)
        {
            if (!Open) return;
            B.Tick(dt);
            if (goT >= 0)
            {
                goT += dt;
                if (goT >= Game.PRESS_SHOW) { goT = -1; Close(); act[cur](); }
                return;
            }
            if (lockT > 0) lockT -= dt;
            var kb = Keyboard.current;
            bool l = false, r = false, u = false, d = false, ok = false, lb = false, rb = false;
            if (kb != null)
            {
                l = kb.leftArrowKey.isPressed; r = kb.rightArrowKey.isPressed; u = kb.upArrowKey.isPressed; d = kb.downArrowKey.isPressed;
                ok = kb.enterKey.isPressed || kb.spaceKey.isPressed;
            }
            foreach (var gp in Gamepad.all)
            {
                l |= gp.dpad.left.isPressed; r |= gp.dpad.right.isPressed; u |= gp.dpad.up.isPressed; d |= gp.dpad.down.isPressed;
                ok |= gp.buttonSouth.isPressed; lb |= gp.leftShoulder.isPressed; rb |= gp.rightShoulder.isPressed;
            }
            Nav.Apply(ref u, ref d, ref l, ref r);
            // 札：LB / RB はいつでも。↑で札の段へ入ると ← → で替わる、↓でボタンへ戻る
            if (lb && !pLB) SetTab(tab - 1);
            if (rb && !pRB) SetTab(tab + 1);
            if (u && !pU && !onTabs) { onTabs = true; Game.SeUi("cur"); Focus(cur); DrawDet(); }
            else if (d && !pD && onTabs) { onTabs = false; Game.SeUi("cur"); Focus(cur); DrawDet(); }
            else if (onTabs) { if (l && !pL) SetTab(tab - 1); if (r && !pR) SetTab(tab + 1); }
            else { if (l && !pL) Focus(cur - 1); if (r && !pR) Focus(cur + 1); }
            if (!ok) armed = true;
            bool fire = ok && !pOk && armed && lockT <= 0 && !onTabs;
            pL = l; pR = r; pU = u; pD = d; pOk = ok; pLB = lb; pRB = rb;
            // マウスは乗せただけでは選ばない（少し明るくするだけ。押したら選んで決める。担当）
            Ptr.Tick(dt, () => Ptr.Center(onTabs && tabRt[tab] != null ? tabRt[tab] : btnRt[cur]));
            var ms = Mouse.current;
            {
                int hv = -1;
                if (ms != null) for (int k = 0; k < 2; k++) if (RectTransformUtility.RectangleContainsScreenPoint(btnRt[k], ms.position.ReadValue(), null)) hv = k;
                if (hv != hov) { hov = hv; Focus(cur); }
            }
            if (ms != null && ms.leftButton.wasPressedThisFrame && lockT <= 0)
            {
                var mp = ms.position.ReadValue();
                for (int k = 0; k < 2; k++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(btnRt[k], mp, null)) { onTabs = false; Focus(k); fire = true; }
                for (int k = 0; k < 4; k++)
                    if (tabRt[k] != null && RectTransformUtility.RectangleContainsScreenPoint(tabRt[k], mp, null)) { SetTab(k); break; }
            }
            // 選んでいる物を選択枠で囲む（ラウンド詳細の札は傾きが小さい）
            if (onTabs && tabRt[tab] != null) SelFrame.Show(tabRt[tab], -0.15f, false); else SelFrame.Show(btnRt[cur], -0.35f, false, img: true);
            if (fire) { Game.SeUi("ok"); b1[cur].color = Color.white; goT = 0; }
        }
    }
}
