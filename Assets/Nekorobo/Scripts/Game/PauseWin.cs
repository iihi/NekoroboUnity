using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// ポーズメニュー（JS版 #pause・pauseDraw）。遊んでいる間に Esc・P・パッドの≡（START）で開く（担当の案）。
    ///   1人・同じ画面のマルチ … ゲームを止める（Time.timeScale = 0）。「再開」「タイトルに戻る」
    ///   オンライン           … 自分の画面に出すだけで、ゲームは止めない。「再開」「ゲームから抜ける」
    /// 「タイトルに戻る」「ゲームから抜ける」は確かめる画面を挟む（カーソルは押しても危なくない「やめない」から）。
    /// ↑↓で選んで Enter・スペース・A で決定。Esc・B は「再開」（確かめる画面では「やめない」）。マウス・指でも押せる。
    /// 見た目は JS版と同じ：後ろのゲームを暗くして、クリーム色の板に PAUSE と縦に2つのボタン
    /// </summary>
    public class PauseWin
    {
        readonly Game g;
        readonly UiBoard B;
        public bool Open { get; private set; }
        string view = "main";
        RectTransform[] btnRt; Image[] btnBg, btnEdge; bool[] btnSec; System.Action[] act;
        int cur; float lockT; bool armed, pUp, pDown, pOk, pBack;

        static readonly Color CREAM = Mats.Hex(0xfffdf6), INK = Mats.Hex(0x3a352c), SUB = Mats.Hex(0x8a8274), GOLD = Mats.Hex(0xf2b52c);
        static readonly Color BTN = Mats.Hex(0x2e3749), BTN_ON = Mats.Hex(0x45526b), SEC = Mats.Hex(0x7b766a), SEC_ON = Mats.Hex(0x968f80);

        public PauseWin(RectTransform parent, Game game)
        {
            g = game;
            B = new UiBoard(parent, "Pause", false);
            var dim = B.root.gameObject.AddComponent<Image>();      // 後ろのゲームを暗くする（クリックも止める）
            // JS版は rgba(10,14,22,.55)。Unity の UI はリニアで混ぜるので、同じ 0.55 だと薄く見える（明るさ 45% にそろえると 0.8 くらい）
            dim.color = new Color(10 / 255f, 14 / 255f, 22 / 255f, 0.8f);
            dim.raycastTarget = true;
        }

        public void Show()
        {
            Open = true; view = "main";
            Draw();
            B.Active = true;
            Game.SeUi("cur");
        }

        public void Close() { Open = false; B.Active = false; }

        void SetView(string v) { view = v; Draw(); }

        // 字は JS版の辞書のまま引く（<br> は改行に）
        static string Tx(string s) { return Lang.Tr(s).Replace("<br>", "\n"); }

        void Draw()
        {
            B.Clear();
            bool on = g.NpOn, host = g.NetHost;
            string head, note; string[] labels; System.Action[] acts; bool[] sec;
            if (view == "main")
            {
                head = "PAUSE";
                note = on ? Tx("オンラインでは、ゲームは止まりません。<br>メニューを開いている間、あなたのロボは動きません。") : "";
                labels = new[] { "再開", on ? "ゲームから抜ける" : "タイトルに戻る" };
                acts = new System.Action[] { () => g.PauseClose(), () => SetView(on ? "quit" : "title") };
            }
            else
            {
                bool quit = view == "quit";
                head = Lang.Tr(quit ? "ゲームから抜けますか？" : "タイトルに戻りますか？");
                note = !quit ? Tx("いま遊んでいる面の結果は残りません。<br>続けて遊んでいる途中なら、その続きもここで終わります（お金・アイテム・強化もなくなります）。")
                     : host ? Tx("あなたは<b>ホスト</b>です。<br>ホストが抜けると、<b>部屋の全員のゲームが終わります</b>。")
                            : Tx("あなたのロボはリタイアになり、この回は<b>最下位</b>になります。<br>ほかの人はそのまま続けます。");
                labels = new[] { "やめない", quit ? "抜ける" : "タイトルに戻る" };
                acts = new System.Action[] { () => SetView("main"), () => { if (quit) g.PauseQuit(); else g.PauseTitle(); } };
            }
            sec = new[] { false, true };
            act = acts;
            // ---- 板（白い縁・クリーム色）。高さは中身から
            var lines = note == "" ? new string[0] : note.Split('\n');
            const float W = 520, BW = 340, BH = 50, GAP = 12, LH = 22;
            float H = 20 + 38 + (lines.Length > 0 ? 6 + lines.Length * LH : 0) + 16 + BH * 2 + GAP + 24;
            float x0 = 640 - W / 2, y0 = 360 - H / 2;
            UiBoard.Fill(B.box, Color.white, x0 - 5, y0 - 5, W + 10, H + 10, 25, "Edge");
            UiBoard.Fill(B.box, CREAM, x0, y0, W, H, 20, "Card");
            float y = y0 + 20;
            UiBoard.T(B.box, head, 640, y, 30, INK, 2, "Head");
            y += 38;
            if (lines.Length > 0)
            {
                y += 6;
                foreach (var ln in lines) { UiBoard.T(B.box, ln, 640, y, 14, SUB, 2, "Note").supportRichText = true; y += LH; }
            }
            y += 16;
            btnRt = new RectTransform[2]; btnBg = new Image[2]; btnEdge = new Image[2]; btnSec = sec;
            for (int k = 0; k < 2; k++)
            {
                float bx = 640 - BW / 2, by = y + k * (BH + GAP);
                var r = UiBoard.Box(B.box, bx - 3, by - 3, BW + 6, BH + 6, "Btn" + k);
                btnEdge[k] = UiBoard.Fill(r, Color.white, 0, 0, BW + 6, BH + 6, 12, "Edge");
                btnBg[k] = UiBoard.Fill(r, sec[k] ? SEC : BTN, 3, 3, BW, BH, 10, "Bg");
                UiBoard.T(r, Lang.Tr(labels[k]), (BW + 6) / 2, 3 + (BH - (sec[k] ? 17 : 19)) / 2, sec[k] ? 17 : 19, Color.white, 2);
                btnRt[k] = r;
            }
            cur = 0;                                   // 押しても危なくない方（再開・やめない）から
            Focus(0, false);
            // 開いた Esc・切り替えた決定を、そのまま「戻る」「決定」と受け取らない
            lockT = g.T.menuLock; armed = false; pOk = true; pBack = true;
        }

        void Focus(int i, bool snd = true)
        {
            i = Mathf.Clamp(i, 0, 1);
            if (i != cur && snd) Game.SeUi("cur");
            cur = i;
            for (int k = 0; k < 2; k++)
            {
                bool f = k == cur;
                btnEdge[k].color = f ? GOLD : Color.white;
                btnBg[k].color = btnSec[k] ? (f ? SEC_ON : SEC) : (f ? BTN_ON : BTN);
            }
        }

        public void Tick(float dt)
        {
            if (!Open) return;
            if (lockT > 0) lockT -= dt;
            var kb = Keyboard.current;
            bool up = false, down = false, ok = false, back = false;
            if (kb != null)
            {
                up = kb.upArrowKey.isPressed; down = kb.downArrowKey.isPressed;
                ok = kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed;
                back = kb.escapeKey.isPressed || kb.backspaceKey.isPressed;
            }
            foreach (var gp in Gamepad.all)
            {
                up |= gp.dpad.up.isPressed; down |= gp.dpad.down.isPressed;
                ok |= gp.buttonSouth.isPressed; back |= gp.buttonEast.isPressed;
            }
            if ((up && !pUp) || (down && !pDown)) Ptr.Hide();
            if (up && !pUp) Focus(cur - 1);
            if (down && !pDown) Focus(cur + 1);
            if (!ok) armed = true;
            bool fire = ok && !pOk && armed && lockT <= 0;
            bool backHit = back && !pBack;
            pUp = up; pDown = down; pOk = ok; pBack = back;
            // 指カーソル（マウスかスティック）。乗った方を選ぶ
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
            if (backHit) { Game.SeUi("back"); act[0](); return; }          // 戻る＝再開（確かめる画面では「やめない」）
            if (fire) { Game.SeUi("ok"); act[cur](); }
        }
    }
}
