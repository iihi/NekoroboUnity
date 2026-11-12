using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// ラウンドマッチの最終結果（JS版 showFinal）。絵はまだ無いので、リザルトの板（背景・書体）に字と図形で組む。
    ///   個人戦 … 表彰台（1位まん中・2位左・3位右、4位は台の下）
    ///   チーム戦 … 青と赤を並べて WIN / LOSE / DRAW
    ///   1人・協力 … 順位は出さず、お店ひとつぶんの結果だけ
    /// 順位は**利益（各ラウンドの差引）の合計**。同じ額なら同じ順位。右に、ラウンドごとの内訳の表。
    /// ボタンは「もう一度」（オンラインは「部屋へ戻る」）と「タイトルへ」。
    /// </summary>
    public class FinalWin
    {
        readonly Game g;
        readonly UiBoard B;
        public bool Open { get; private set; }
        readonly RawImage[] b0 = new RawImage[2], b1 = new RawImage[2]; readonly Texture[] b0Tex = new Texture[2];
        readonly RectTransform[] btnRt = new RectTransform[2]; System.Action[] act;
        int cur; float lockT, goT = -1; bool armed, pL, pR, pOk, room, auto;   // auto … 自動マッチングの部屋
        const string UI = "ui/res/";
        static readonly Color YEL = Mats.Hex(0xffd400), SUB = Mats.Hex(0xcfd8de), NEG = Mats.Hex(0xff6b5b), PANEL = new Color(8 / 255f, 20 / 255f, 32 / 255f, 0.65f);

        public FinalWin(RectTransform parent, Game game)
        {
            g = game;
            B = new UiBoard(parent, "Final");
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
        static string Tag(Player P) { return P.src.kind == "npc" ? "NPC" : (P.idx + 1) + "P"; }
        static Color PCol(Player P) { return Mats.Hex(P.col); }

        /// <summary>ネコロボの顔（タイトルの solo の絵を人の色で）</summary>
        static RawImage Face(RectTransform p, Color c, float x, float y, float w)
        {
            var im = UiBoard.Img(p, "", x, y, w, w, "Face");
            im.texture = Resources.Load<Texture2D>("Ui/Title/solo"); im.color = c;
            return im;
        }
        /// <summary>字（x, y は字の左上。al 0 左・1 右・2 まん中）。縦はまん中合わせにしたいときは y に size/2 を引いた値を渡す</summary>
        static Text T(RectTransform p, string s, float x, float y, float size, Color c, int al = 0) { return UiBoard.T(p, s, x, y, size, c, al); }

        public void Show()
        {
            g.AchMatchEnd();
            EarlyQuit.Done();                                   // 最後まで遊んだ（自動マッチングの途中退出の印を消す）
            // オンラインは、試合が終わったので同期を畳む（部屋には残る）
            room = g.NpOn && Net.Room != null;
            auto = room && (bool?)Net.Room["auto"] == true;
            g.NpEnd();
            B.Clear();
            var bx = B.box;
            var PL = g.players; bool many = PL.Count > 1;
            var H = g.matchHist;
            int rounds = g.matchRounds;
            {
                var t = T(bx, "FINAL RESULT", 51, 34, 78, YEL);
                t.fontStyle = FontStyle.Italic;
                var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = Mats.Hex(0x7a4b00); sh.effectDistance = new Vector2(0, -5);
                B.In(t, "slam", 0);
            }
            {
                var sub = UiBoard.Box(bx, 0, 0, 1280, 720, "Sub");
                T(sub, Lang.Tr("{0}ラウンド 終了", rounds), 1190, 58, 34, Color.white, 1);
                T(sub, many ? Lang.Tr(Game.ModeName(g.mode)) : Lang.Tr("1人"), 1190, 58 + 34 + 8, 18, SUB, 1);
                B.In(sub, "right", 1);
            }
            // 表の列。vals … ラウンドごとの差引、tot … 合計
            var cols = new List<Col>();
            if (many && g.mode == "team")
            {
                // ---- チーム戦：青と赤を並べて、勝ち負け
                var ts = g.TeamsInPlay();
                var tot = new List<float>();
                foreach (var t in ts) tot.Add(SumOf(PL.IndexOf(g.TeamLead(t))));
                float best = float.MinValue; foreach (var v in tot) best = Mathf.Max(best, v);
                int nb = 0; foreach (var v in tot) if (v == best) nb++;
                bool draw = nb > 1;
                float w = (690 - 24 * (ts.Count - 1)) / Mathf.Max(1, ts.Count);
                for (int k = 0; k < ts.Count; k++)
                {
                    int tm = ts[k];
                    string res = draw && tot[k] == best ? "DRAW" : tot[k] == best ? "WIN" : "LOSE";
                    var box = UiBoard.Box(bx, 50 + k * (w + 24), 150, w, 450, "Team");
                    var tc = Mats.Hex(Game.TEAM_HEX[tm]);
                    if (res == "WIN") UiBoard.Fill(box, YEL, -4, -4, w + 8, 458, 18);
                    UiBoard.Fill(box, tc, 0, 0, w, 450, 16);
                    UiBoard.Fill(box, new Color(tc.r * 0.3f, tc.g * 0.3f, tc.b * 0.3f, 0.92f), 3, 3, w - 6, 444, 14);
                    T(box, Lang.Tr(Game.TEAM_NAME[tm]), w / 2, 100 - 17, 34, Color.white, 2);
                    var rt = T(box, res, w / 2, 170 - 42, 84, res == "WIN" ? YEL : res == "LOSE" ? Mats.Hex(0x9fb0c0) : Color.white, 2);
                    rt.fontStyle = FontStyle.Italic;
                    if (res == "WIN") { var s2 = rt.gameObject.AddComponent<Shadow>(); s2.effectColor = Mats.Hex(0x7a4b00); s2.effectDistance = new Vector2(0, -5); }
                    var ft = T(box, Game.Yen(tot[k]), w / 2, 262 - 23, 46, tot[k] < 0 ? NEG : YEL, 2);
                    float tk = tot[k]; B.Count(ft, tk, v => Game.Yen(v));
                    var ms = PL.FindAll(q => q.team == tm);
                    Members(box, ms, w / 2, 330);
                    B.In(box, "up", 2 + k);
                    var vals = new List<float>(); foreach (var r in H) vals.Add(r.per[PL.IndexOf(g.TeamLead(tm))].tot);
                    cols.Add(new Col { h = Lang.Tr(tm == 1 ? "赤" : "青"), col = tm == 1 ? Mats.Hex(0xff6b6b) : Mats.Hex(0x2f9bff), vals = vals, tot = tot[k] });
                }
            }
            else if (many && g.mode == "versus")
            {
                // ---- 個人戦：表彰台（1〜3位）と、4位は台の下
                var tot = new List<float>(); for (int i = 0; i < PL.Count; i++) tot.Add(SumOf(i));
                var order = new List<int>(); for (int i = 0; i < PL.Count; i++) order.Add(i);
                order.Sort((a, b) => tot[b].CompareTo(tot[a]));
                float[] SX = { 405, 175, 635 }, SH = { 190, 140, 100 };      // 1位はまん中、2位は左、3位は右
                for (int k = 0; k < order.Count; k++)
                {
                    int i = order[k]; var P = PL[i];
                    int rk = 1; foreach (var v in tot) if (v > tot[i]) rk++;
                    if (k < 3)
                    {
                        float x = SX[k] - 105, h = SH[k], bottom = 720 - 92, fw = k == 0 ? 92 : 70;
                        var pod = UiBoard.Box(bx, x, 0, 210, 720, "Pod");
                        // 台
                        Color top, bot2, ink;
                        if (rk == 1) { top = Mats.Hex(0xffd84a); bot2 = Mats.Hex(0xb8860b); ink = Mats.Hex(0x3a2500); }
                        else if (rk == 2) { top = Mats.Hex(0xe3ebf2); bot2 = Mats.Hex(0x8a9aa8); ink = Mats.Hex(0x1d2833); }
                        else if (rk == 3) { top = Mats.Hex(0xe7a46a); bot2 = Mats.Hex(0x8e5326); ink = Mats.Hex(0x2e1604); }
                        else { top = Mats.Hex(0x3d5a78); bot2 = Mats.Hex(0x1d2d40); ink = Color.white; }
                        var blk = UiBoard.Box(pod, 0, bottom - h, 210, h, "Blk");
                        var bi = blk.gameObject.AddComponent<RawImage>(); bi.raycastTarget = false; bi.texture = VGrad(top, bot2);
                        T(blk, rk.ToString(), 105, 10, 64, ink, 2);
                        T(blk, Lang.Tr("{0}品", (int)SumOf(i, "deliv")), 105, 10 + 64 + 6, 17, new Color(ink.r, ink.g, ink.b, 0.85f), 2);
                        // 金額と顔
                        float fs = k == 0 ? 38 : 30;
                        var ft = T(pod, Game.Yen(tot[i]), 105, bottom - h - 8 - fs, fs, tot[i] < 0 ? NEG : YEL, 2);
                        float tv = tot[i]; B.Count(ft, tv, v => Game.Yen(v));
                        float ty = bottom - h - 8 - fs - 8 - 28;
                        T(pod, Tag(P), 105, ty, 28, PCol(P), 2);
                        Face(pod, PCol(P), 105 - fw / 2, ty - 2 - fw, fw);
                        B.In(pod, "up", 5 - k);
                    }
                    else
                    {
                        var row = UiBoard.Box(bx, 50, 636, 640, 46, "Fourth");
                        UiBoard.Fill(row, PANEL, 0, 0, 640, 46, 10);
                        T(row, Lang.Tr("{0}位", rk), 18, 23 - 12, 24, SUB);
                        Face(row, PCol(P), 80, 8, 30);
                        T(row, Tag(P), 118, 23 - 11, 22, PCol(P));
                        T(row, Lang.Tr("{0}品", (int)SumOf(i, "deliv")), 200, 23 - 8, 16, SUB);
                        T(row, Game.Yen(tot[i]), 622, 23 - 11, 22, tot[i] < 0 ? NEG : YEL, 1);
                        B.In(row, "left", 6);
                    }
                }
                for (int i = 0; i < PL.Count; i++)
                {
                    var vals = new List<float>(); foreach (var r in H) vals.Add(r.per[i].tot);
                    cols.Add(new Col { h = Tag(PL[i]), col = PCol(PL[i]), vals = vals, tot = tot[i] });
                }
            }
            else
            {
                // ---- 1人・協力：順位は出さず、お店ひとつぶんの結果だけ
                float tot = SumOf(0); int deliv = 0; for (int i = 0; i < PL.Count; i++) deliv += (int)SumOf(i, "deliv");
                int clr = 0; foreach (var r in H) if (r.frames > 0) clr++;
                var box = UiBoard.Box(bx, 50, 160, 690, 430, "Solo");
                UiBoard.Fill(box, PANEL, 0, 0, 690, 430, 16);
                float y = many ? 40 : 62;
                T(box, Lang.Tr(many ? "みんなで稼いだ利益" : "稼いだ利益") + "<size=18><color=#cfd8de>  " + Lang.Tr("（{0}ラウンドの合計）", rounds) + "</color></size>", 345, y, 30, Color.white, 2);
                var ft = T(box, Game.Yen(tot), 345, y + 50, 96, tot < 0 ? NEG : YEL, 2);
                var fsh = ft.gameObject.AddComponent<Shadow>(); fsh.effectColor = Mats.Hex(0x7a4b00); fsh.effectDistance = new Vector2(0, -5);
                B.Count(ft, tot, v => Game.Yen(v));
                float cost = SumOf(0, "cost");
                string[][] st =
                {
                    new[] { "売上", Game.Yen(SumOf(0, "sales")) }, new[] { "修理費・救急車", (cost != 0 ? "−" : "") + Game.Yen(cost) },
                    new[] { "配膳数", Lang.Tr("{0}品", deliv) }, new[] { "クリア", Lang.Tr("{0} / {1}ラウンド", clr, rounds) },
                };
                float sy = y + 50 + 96 + 30, cw = (690 - 72 - 40) / 2f;
                for (int i = 0; i < 4; i++)
                {
                    float x = 36 + (i % 2) * (cw + 40), yy = sy + (i / 2) * 50;
                    T(box, Lang.Tr(st[i][0]), x, yy + 6, 19, SUB);
                    T(box, st[i][1], x + cw, yy, 26, Color.white, 1);
                    UiBoard.Fill(box, new Color(1, 1, 1, 0.18f), x, yy + 36, cw, 2);
                }
                if (many) Members(box, PL, 345, sy + 110);
                B.In(box, "left", 2);
                var vals = new List<float>(); foreach (var r in H) vals.Add(r.per[0].tot);
                cols.Add(new Col { h = Lang.Tr("利益"), col = Color.white, vals = vals, tot = tot });
            }
            Table(bx, cols, H);
            // ---- ボタン。オンラインは部屋へ戻る（ホストがもう一度始められる）
            act = new System.Action[] { Again, ToTitle };
            Btn(0, room ? (auto ? "もう一度さがす" : "部屋へ戻る") : "もう一度", 728, 7);
            Btn(1, "タイトルへ", 998, 8);
            cur = 0; lockT = g.T.menuLock; armed = false; goT = -1;
            Focus(0);
            B.Active = true;
            Open = true;
            B.StartIn();
        }

        class Col { public string h; public Color col; public List<float> vals; public float tot; }

        /// <summary>メンバーの並び（顔・名前・配膳数）。まん中を cx に</summary>
        void Members(RectTransform p, List<Player> ms, float cx, float y)
        {
            float w = 0; var ws = new List<float>();
            foreach (var q in ms) { float ww = 30 + 6 + 40 + 6 + 52; ws.Add(ww); w += ww; }
            w += 18 * (ms.Count - 1);
            float x = cx - w / 2;
            for (int k = 0; k < ms.Count; k++)
            {
                var q = ms[k];
                int i = g.players.IndexOf(q);
                Face(p, PCol(q), x, y, 30);
                T(p, Tag(q), x + 36, y + 15 - 10, 19, PCol(q));
                T(p, Lang.Tr("{0}品", (int)SumOf(i, "deliv")), x + 36 + 46, y + 15 - 10, 19, Color.white);
                x += ws[k] + 18;
            }
        }

        /// <summary>右：ラウンドごとの内訳の表</summary>
        void Table(RectTransform bx, List<Col> cols, List<Game.MatchRound> H)
        {
            bool one = cols.Count == 1;
            float rh = Mathf.Min(38, Mathf.Floor(400f / (H.Count + 2)));
            float W = 420, x0 = 770, y0 = 150;
            var tb = UiBoard.Box(bx, x0, y0, W, rh * (H.Count + 1) + rh * 1.25f, "Tbl");
            UiBoard.Fill(tb, new Color(8 / 255f, 20 / 255f, 32 / 255f, 0.7f), 0, 0, W, tb.sizeDelta.y, 14);
            // 列の位置（左の ROUND 62px、one は 面の名前と金額 130px）
            float inner = W - 6 - 14, vx0 = 6 + 62 + 6;
            float cw = one ? 130 : (inner - 62 - 6 * cols.Count) / cols.Count;
            System.Func<int, float> vr = c => one ? W - 14 : vx0 + (c + 1) * cw + c * 6;      // 列 c の右端
            System.Action<float, float, string, string[], Color[], float, bool> row = (y, h, rd, vs, cs, fs, head) =>
            {
                T(tb, rd, 6 + 31, y + h / 2 - fs * (head ? 0.36f / 0.48f : 1) / 2, head ? rh * 0.36f : fs, SUB, 2);
                for (int c = 0; c < vs.Length; c++) T(tb, vs[c], vr(c), y + h / 2 - fs / 2, fs, cs[c], 1);
            };
            // 見出し
            UiBoard.Fill(tb, new Color(1, 1, 1, 0.12f), 0, 0, W, rh);
            {
                var hs = new string[cols.Count]; var hc = new Color[cols.Count];
                for (int c = 0; c < cols.Count; c++) { hs[c] = cols[c].h; hc[c] = cols[c].col; }
                row(0, rh, "ROUND", hs, hc, rh * 0.45f, true);
                if (one) T(tb, Lang.Tr("面"), vx0, rh / 2 - rh * 0.2f, rh * 0.4f, SUB);
            }
            for (int k = 0; k < H.Count; k++)
            {
                float y = rh * (k + 1);
                if (k % 2 == 1) UiBoard.Fill(tb, new Color(1, 1, 1, 0.05f), 0, y, W, rh);
                var vs = new string[cols.Count]; var cs = new Color[cols.Count];
                for (int c = 0; c < cols.Count; c++) { float v = k < cols[c].vals.Count ? cols[c].vals[k] : 0; vs[c] = Game.Yen(v); cs[c] = v < 0 ? NEG : Color.white; }
                row(y, rh, (k + 1).ToString(), vs, cs, rh * 0.48f, false);
                if (one) { var nm = T(tb, Lang.Tr(H[k].name), vx0, y + rh / 2 - rh * 0.2f, rh * 0.4f, SUB); }
            }
            {
                float y = rh * (H.Count + 1), h = rh * 1.25f;
                UiBoard.Fill(tb, new Color(1, 212 / 255f, 0, 0.16f), 0, y, W, h);
                var vs = new string[cols.Count]; var cs = new Color[cols.Count];
                for (int c = 0; c < cols.Count; c++) { vs[c] = Game.Yen(cols[c].tot); cs[c] = cols[c].tot < 0 ? NEG : YEL; }
                row(y, h, Lang.Tr("合計"), vs, cs, rh * 0.5f, false);
            }
            B.In(tb, "right", 2);
        }

        static Texture2D VGrad(Color top, Color bottom)
        {
            var t = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) t.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            t.Apply(false);
            return t;
        }

        void Btn(int i, string label, float x, int order)
        {
            var r = UiBoard.Box(B.box, x, 620, 240, 77, "Btn" + i);
            b0[i] = UiBoard.Img(r, UI + "btn.png", 0, 0, 240, 77);
            b0Tex[i] = b0[i].texture;
            b1[i] = UiBoard.Img(r, UI + "btn_on.png", 0, 0, 240, 77);
            b1[i].color = new Color(1, 1, 1, 0);
            UiBoard.T(r, Lang.Tr(label), 120, 38.5f - 14, 28, Color.white, 2);
            btnRt[i] = r;
            B.In(r, "pop", order);
        }

        void Focus(int i)
        {
            i = Mathf.Clamp(i, 0, 1);
            if (i != cur && Open) Game.SeUi("cur");
            cur = i;
            for (int k = 0; k < 2; k++) if (b0Tex[k] != null) b0[k].texture = k == cur ? UiBoard.Bright(b0Tex[k]) : b0Tex[k];
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
            bool l = false, r = false, ok = false;
            if (kb != null) { l = kb.leftArrowKey.isPressed || kb.upArrowKey.isPressed; r = kb.rightArrowKey.isPressed || kb.downArrowKey.isPressed; ok = kb.enterKey.isPressed || kb.spaceKey.isPressed; }
            foreach (var gp in Gamepad.all) { l |= gp.dpad.left.isPressed; r |= gp.dpad.right.isPressed; ok |= gp.buttonSouth.isPressed; }
            if ((l && !pL) || (r && !pR)) Ptr.Hide();
            if (l && !pL) Focus(cur - 1);
            if (r && !pR) Focus(cur + 1);
            if (!ok) armed = true;
            bool fire = ok && !pOk && armed && lockT <= 0;
            pL = l; pR = r; pOk = ok;
            Ptr.Tick(dt, () => Ptr.Center(btnRt[cur]));
            if (Ptr.Moved)
                for (int k = 0; k < 2; k++) if (Ptr.Over(btnRt[k]) && cur != k) Focus(k);
            var ms = Mouse.current;
            if (ms != null && ms.leftButton.wasPressedThisFrame && lockT <= 0)
            {
                var mp = ms.position.ReadValue();
                for (int k = 0; k < 2; k++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(btnRt[k], mp, null)) { Focus(k); fire = true; }
            }
            if (fire) { Game.SeUi("ok"); b1[cur].color = Color.white; goT = 0; }
        }
    }
}
