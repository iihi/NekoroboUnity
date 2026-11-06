using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// ストーリークリア（全ステージ終了）。JS版 showEnding を写したもの。
    /// 清書 1029（資料/ネコ配膳_ストーリークリア画面清書1029.psd）。枠・ボタンは PSD から切り出した絵（assets/ui/story/）。字はゲームが重ねる。
    ///   STORY CLEAR ／ ○店舗の派遣を終えました。 ／ 通しタイムと面ごとのタイム（4列） ／ 所持金とひとこと ／ もう一度プレイする・タイトルへ
    /// ←→ で選んで Enter・スペース・A で決定。マウス・指でも押せる。押した絵（緑）を見せてから進む
    /// </summary>
    public class EndingWin
    {
        readonly Game g;
        readonly UiBoard B;
        public bool Open { get; private set; }
        readonly RawImage[] b0 = new RawImage[2], b1 = new RawImage[2]; readonly Texture[] b0Tex = new Texture[2];
        readonly RectTransform[] btnRt = new RectTransform[2]; System.Action[] act;
        int cur; float lockT, goT = -1; bool armed, pL, pR, pOk;
        const string U = "ui/story/";
        static readonly Color GREEN = Mats.Hex(0x68ff00), NEG = Mats.Hex(0xff6b5b), GREY = Mats.Hex(0x9a9a9a);

        public EndingWin(RectTransform parent, Game game)
        {
            g = game;
            B = new UiBoard(parent, "Ending");
        }

        public void Close() { Open = false; B.Active = false; }

        public void Show()
        {
            B.Clear();
            var bx = B.box;
            int n = g.course != null ? g.course.stages.Count : 0;
            float net = g.WalletOf(g.me).cash;
            B.In(UiBoard.T(bx, "STORY CLEAR", 51, 36, 76, Color.white), "slam", 0);
            // 字の上下のまん中を y に合わせる（CSS の translateY(-50%)）
            System.Func<string, float, float, float, Color, int, Text> T = (s, x, y, size, c, al) => UiBoard.T(bx, s, x, y - size / 2, size, c, al);
            B.In(T(Lang.Tr("{0}店舗の派遣を終えました。", n), 50, 157.75f, 29, Color.white, 0), "fade", 1);
            // ---- 通しタイムと、面ごとのタイム（4列。12面なら3段）。位置は PSD の ROUND1〜12 から
            var time = UiBoard.Box(bx, 0, 0, 1280, 720, "Time");
            UiBoard.Img(time, U + "time.png", 45.5f, 216.5f, 1191.5f, 158);
            UiBoard.T(time, Game.FmtTime(g.RunFrames()), 233, 307.25f - 28, 56, GREEN, 2);
            int rows = Mathf.Max(1, (n + 3) / 4);
            float pitch = Mathf.Min(33, 99f / Mathf.Max(1, rows - 1)), y0 = 307.5f - pitch * (rows - 1) / 2;
            for (int i = 0; i < n; i++)
            {
                float x = 412 + (i % 4) * 196.5f, y = y0 + (i / 4) * pitch;
                UiBoard.T(time, "ROUND" + (i + 1), x, y - 9, 18, GREY);
                int f = g.RunTime(i);
                UiBoard.T(time, f > 0 ? Game.FmtTime(f) : "—", x + 184, y - 11.5f, 23, Color.white, 1);
            }
            B.In(time, "left", 2);
            // ---- 所持金とひとこと
            var money = UiBoard.Box(bx, 0, 0, 1280, 720, "Money");
            UiBoard.Img(money, U + "money.png", 45.5f, 399, 361.5f, 158);
            UiBoard.T(money, Game.Yen(net), 234, 489.75f - 28, 56, net < 0 ? NEG : GREEN, 2);
            B.In(money, "left", 3);
            B.In(T(Lang.Tr(net >= 0 ? "黒字で終えました。おつかれさまにゃ！" : "赤字で終わりました…"), 435, 495.25f, 29, net < 0 ? Mats.Hex(0xff8a7a) : Mats.Hex(0xffd84a), 0), "fade", 3);
            // ---- ボタン
            act = new System.Action[] { () => g.ResetRun(), () => g.BackToTitle() };
            Btn(0, "retry", "もう一度プレイする", 462, 466.5f, 4);
            Btn(1, "title", "タイトルへ", 937, 260, 5);
            cur = 0; lockT = g.T.menuLock; armed = false; goT = -1;
            Focus(0);
            B.Active = true;
            Open = true;
            B.StartIn();
        }

        void Btn(int i, string f, string label, float x, float w, int order)
        {
            var r = UiBoard.Box(B.box, x, 591, w, 83, "Btn_" + f);
            b0[i] = UiBoard.Img(r, U + "btn_" + f + ".png", 0, 0, w, 83);
            b0Tex[i] = b0[i].texture;
            b1[i] = UiBoard.Img(r, U + "btn_" + f + "_on.png", 0, 0, w, 83);
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
            // 選んでいるときは地を少し明るく（光彩は付けない。担当）
            for (int k = 0; k < 2; k++) if (b0Tex[k] != null) b0[k].texture = k == cur ? UiBoard.Bright(b0Tex[k]) : b0Tex[k];
        }

        public void Tick(float dt)
        {
            if (!Open) return;
            B.Tick(dt);
            // 押した絵（緑）を見せてから進む
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
            if (fire) { Game.SeUi("ok"); b1[cur].color = Color.white; goT = 0; }
        }
    }
}
