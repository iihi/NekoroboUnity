using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// エンディング（全ステージ終了）。JS版 showEnding を写したもの。
    /// ショップと同じ濃い青の地に、白い札（通しタイム・面ごとのタイム・所持金）と「もう一度」「タイトルへ」。
    /// ←→ で選んで Enter・スペース・A で決定。マウスでも押せる。
    /// </summary>
    public class EndingWin
    {
        readonly Game g;
        readonly RectTransform root;
        RectTransform box;
        public bool Open { get; private set; }
        Image[] btnRing; RectTransform[] btnRt; System.Action[] act;
        int cur; float lockT; bool armed, pL, pR, pOk;

        static readonly Color INK = Mats.Hex(0x1d2433), GOLD = Mats.Hex(0xf2b52c), GOLD_D = Mats.Hex(0xd18f10);

        public EndingWin(RectTransform parent, Game game)
        {
            g = game;
            root = UiKit.Rect(parent, "Ending");
            UiKit.Stretch(root);
            var bg = root.gameObject.AddComponent<RawImage>();
            bg.texture = ShopWin.BgTex(); bg.raycastTarget = true;
            root.gameObject.SetActive(false);
        }

        public void Close() { Open = false; root.gameObject.SetActive(false); }

        public void Show()
        {
            if (box != null) Object.Destroy(box.gameObject);
            box = UiKit.Rect(root, "Box");
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f); box.sizeDelta = new Vector2(1280, 720);
            int n = g.course != null ? g.course.stages.Count : 0;
            float net = g.WalletOf(g.me).cash;
            // ---- 見出し（金の丸い札）
            {
                var t = UiKit.Label(box, "全ステージ終了", 24, Color.white, true, TextAnchor.MiddleCenter);
                float w = t.preferredWidth + 88 + 8, h = 60;
                var sh = UiKit.Img(box, GOLD_D, h / 2, "Shadow"); Place(sh.rectTransform, 640 - w / 2, 145 + 4, w, h);
                var bg = UiKit.Img(box, Color.white, 0, "H2"); bg.sprite = UiKit.PillGrad(Mathf.RoundToInt(w), (int)h, Mats.Hex(0xffd558), GOLD, 4);
                Place(bg.rectTransform, 640 - w / 2, 145, w, h);
                t.transform.SetAsLastSibling(); Place(t.rectTransform, 640 - w / 2, 145, w, h);
                var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = GOLD_D; s.effectDistance = new Vector2(0, -2);
            }
            Sub(n + "店舗の派遣を終えました", 216);
            Row("通しタイム", Game.FmtTime(g.RunFrames()), INK, 248);
            // ---- 面ごとのタイム（4列）
            {
                int rows = Mathf.Max(1, (n + 3) / 4);
                float h = 16 + rows * 21;
                var tb = UiKit.Img(box, new Color(1, 1, 1, 0.12f), 12, "Times"); Place(tb.rectTransform, 320, 319, 640, h);
                float cw = (640 - 24 - 3 * 8) / 4f;
                for (int i = 0; i < n; i++)
                {
                    float x = 12 + (i % 4) * (cw + 8), y = 8 + (i / 4) * 21;
                    var a = UiKit.Label(tb.transform, "STAGE " + (i + 1), 11, new Color(1, 1, 1, 0.7f), true, TextAnchor.MiddleLeft);
                    Place(a.rectTransform, x, y, cw, 21);
                    int f = g.RunTime(i);
                    var b = UiKit.Label(tb.transform, f > 0 ? Game.FmtTime(f) : "—", 13, Color.white, true, TextAnchor.MiddleRight);
                    Place(b.rectTransform, x, y, cw, 21);
                }
                Row("所持金", Game.Yen(net), net >= 0 ? Mats.Hex(0x1a9e4b) : Mats.Hex(0xe53935), 319 + h + 10);
                Sub(net >= 0 ? "黒字で終えました。おつかれさまにゃ！" : "赤字で終わりました…", 319 + h + 10 + 60 + 10);
                // ---- もう一度（緑）／タイトルへ（灰）
                float by = 319 + h + 10 + 60 + 10 + 30;
                btnRing = new Image[2]; btnRt = new RectTransform[2];
                act = new System.Action[] { () => g.ResetRun(), () => g.BackToTitle() };
                Btn(0, "もう一度", 320, by, 480, 66, Mats.Hex(0x63d47f), Mats.Hex(0x4cbf68), Mats.Hex(0x2f9448), 19);
                Btn(1, "タイトルへ", 810, by + 19, 150, 40, Mats.Hex(0xb8b1a4), Mats.Hex(0xa7a094), Mats.Hex(0x867f74), 15);
            }
            cur = 0; lockT = g.T.menuLock; armed = false;
            Focus(0);
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            Open = true;
        }

        void Sub(string s, float y)
        {
            var t = UiKit.Label(box, s, 15, Color.white, true, TextAnchor.MiddleCenter);
            Place(t.rectTransform, 320, y, 640, 20);
            var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.5f); sh.effectDistance = new Vector2(0, -2);
        }

        void Row(string k, string v, Color vc, float y)
        {
            var sh = UiKit.Img(box, new Color(0, 0, 0, 0.18f), 14, "RowShadow"); Place(sh.rectTransform, 320, y + 4, 640, 60);
            var r = UiKit.Img(box, Color.white, 14, "Row"); Place(r.rectTransform, 320, y, 640, 60);
            var a = UiKit.Label(r.transform, k, 17, INK, true, TextAnchor.LowerLeft); Place(a.rectTransform, 22, 10, 300, 36);
            var b = UiKit.Label(r.transform, v, 30, vc, true, TextAnchor.MiddleRight); Place(b.rectTransform, 22, 8, 640 - 44, 44);
        }

        void Btn(int i, string label, float x, float y, float w, float h, Color top, Color bot, Color edge, float fs)
        {
            var sh = UiKit.Img(box, edge, 14, "BtnShadow"); Place(sh.rectTransform, x, y + 4, w, h);
            var bg = UiKit.Img(box, Color.white, 0, "Btn"); bg.sprite = UiKit.Grad(Mathf.RoundToInt(w), Mathf.RoundToInt(h), 14, top, bot);
            Place(bg.rectTransform, x, y, w, h);
            var t = UiKit.Label(bg.transform, label, fs, Color.white, true, TextAnchor.MiddleCenter); UiKit.Stretch(t.rectTransform);
            var ts = t.gameObject.AddComponent<Shadow>(); ts.effectColor = new Color(0, 0, 0, 0.22f); ts.effectDistance = new Vector2(0, -2);
            // 選んでいるときの金の縁（外側 4px）
            var ring = UiKit.Img(box, GOLD, 0, "Foc");
            ring.sprite = UiKit.Ring(Mathf.RoundToInt(4 * 15.5f / 18)); ring.type = Image.Type.Sliced; ring.pixelsPerUnitMultiplier = 15.5f / 18;
            Place(ring.rectTransform, x - 5, y - 5, w + 10, h + 10);
            btnRing[i] = ring; btnRt[i] = bg.rectTransform;
        }

        void Focus(int i)
        {
            cur = Mathf.Clamp(i, 0, 1);
            for (int k = 0; k < 2; k++) btnRing[k].enabled = k == cur;
        }

        public void Tick(float dt)
        {
            if (!Open) return;
            if (lockT > 0) lockT -= dt;
            var kb = Keyboard.current;
            bool l = false, r = false, ok = false;
            if (kb != null) { l = kb.leftArrowKey.isPressed || kb.upArrowKey.isPressed; r = kb.rightArrowKey.isPressed || kb.downArrowKey.isPressed; ok = kb.enterKey.isPressed || kb.spaceKey.isPressed; }
            foreach (var gp in Gamepad.all) { l |= gp.dpad.left.isPressed; r |= gp.dpad.right.isPressed; ok |= gp.buttonSouth.isPressed; }
            if (l && !pL) Focus(cur - 1);
            if (r && !pR) Focus(cur + 1);
            if (!ok) armed = true;
            bool fire = ok && !pOk && armed && lockT <= 0;
            pL = l; pR = r; pOk = ok;
            var ms = Mouse.current;
            if (ms != null)
            {
                var mp = ms.position.ReadValue();
                for (int k = 0; k < 2; k++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(btnRt[k], mp, null))
                    {
                        if (cur != k) Focus(k);
                        if (ms.leftButton.wasPressedThisFrame) fire = true;
                    }
            }
            if (fire) { Close(); act[cur](); }
        }

        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
        }
    }
}
