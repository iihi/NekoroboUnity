using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 指カーソル。JS版の PTR（ptrTick / ptrShow / ptrHide）を写したもの。
    ///
    /// **スティックでマウスのように動かす指。**十字キーは今までどおり「1つずつ移す」。
    /// 指が乗った物を「選んでいる物」にするだけで、決定は各画面の今までの処理（A・Enter）が拾う。
    ///
    /// マウスのカーソルも同じ指の絵にする（メニューの画面の間だけ）。
    /// スティックで動かしている間は、自前の指を描いてマウスのカーソルを消す
    /// （両方見えると、どちらが効いているのか分からなくなる）。マウスを動かせばマウスへ戻る。
    /// </summary>
    public static class Ptr
    {
        public static bool On;              // 指が出ている（マウスかスティックで動かした）
        public static Vector2 Pos;          // 画面の座標[px]（左下が原点）
        public static bool Moved;           // このフレームで動いた（下の物を選び直す）
        static bool viaStick;               // スティックで動かしている（自前の指を描く）

        static RectTransform img;
        static Texture2D curTex;
        static bool curSet;
        static int usedFrame = -10;
        static Vector2 lastMouse;

        const float DEAD = 0.22f;           // スティックの遊び（JS版 readPad の axX / axY と同じ）

        /// <summary>
        /// メニューの画面が開いている間、毎フレーム呼ぶ。
        /// from … 指の出はじめの場所（いま選んでいる物のまん中）。null なら画面のまん中
        /// </summary>
        public static void Tick(float dt, System.Func<Vector2?> from)
        {
            bool fresh = usedFrame < Time.frameCount - 1;      // 開いたばかり（前のフレームは使っていない）
            usedFrame = Time.frameCount;
            Moved = false;
            FingerCursor(true);

            var ms = Mouse.current;
            if (ms != null)
            {
                var mp = ms.position.ReadValue();
                // 開いたばかりのときは、遊んでいる間に動いたぶんを「動かした」と数えない
                if (!fresh && (mp - lastMouse).sqrMagnitude > 0.5f) { Pos = mp; On = true; viaStick = false; Moved = true; }
                lastMouse = mp;
            }

            // **いちばん倒れている台**のスティックを使う（どの台からでも動かせる）
            Vector2 s = Vector2.zero;
            foreach (var gp in Gamepad.all)
            {
                var v = gp.leftStick.ReadUnprocessedValue();         // 遊びは下で JS版と同じ値を当てる（Unity の遊びと二重にしない）
                if (Mathf.Abs(v.x) > Mathf.Abs(s.x)) s.x = v.x;
                if (Mathf.Abs(v.y) > Mathf.Abs(s.y)) s.y = v.y;
            }
            if (Mathf.Abs(s.x) <= DEAD) s.x = 0;
            if (Mathf.Abs(s.y) <= DEAD) s.y = 0;
            if (s.x != 0 || s.y != 0)
            {
                if (!On)
                {
                    // 出はじめは、いま選んでいる物の上から始める（画面の隅から出ない）
                    var f = from != null ? from() : null;
                    Pos = f ?? new Vector2(Screen.width / 2f, Screen.height / 2f);
                }
                On = true; viaStick = true;
                float sp = Mathf.Max(420, Screen.width) / 1.15f;          // 画面を 1.15 秒で横断
                Pos.x = Mathf.Clamp(Pos.x + s.x * sp * dt, 4, Screen.width - 4);
                Pos.y = Mathf.Clamp(Pos.y + s.y * sp * dt, 4, Screen.height - 4);
                Moved = true;
            }
            Draw();
        }

        /// <summary>十字キー・矢印キーを使ったら指は引っ込める（JS版 ptrHide）。</summary>
        public static void Hide()
        {
            On = false; viaStick = false;
            Draw();
        }

        /// <summary>
        /// ショップのように、自分の指を持っている画面。マウスのカーソルだけ指にする。
        /// （スティックはその画面の指が使う）
        /// </summary>
        public static void CursorOnly()
        {
            usedFrame = Time.frameCount;
            FingerCursor(true);
            On = false; viaStick = false; Moved = false;
            Draw();
        }

        // ---- 直前に使ったのがマウスか（JS版 LASTM / mouseStart）。画面が変わっても、マウスで遊んでいる人の指はマウスの所から始める
        static float lastMouseT = -99, lastKeyT = -99;
        /// <summary>毎フレーム（Hud の始め）。マウスを動かした・押した時刻と、キー・パッドを押した時刻を控える</summary>
        public static void TrackInput()
        {
            float now = Time.unscaledTime;
            var ms = Mouse.current;
            if (ms != null && (ms.delta.ReadValue().sqrMagnitude > 0.25f || ms.leftButton.wasPressedThisFrame)) lastMouseT = now;
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) lastKeyT = now;
            foreach (var gp in Gamepad.all)
                if (gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame || gp.dpad.ReadValue().sqrMagnitude > 0.25f
                    || gp.leftStick.ReadValue().sqrMagnitude > 0.25f) lastKeyT = now;
        }
        /// <summary>マウスで遊んでいるか（直前に使ったのがマウスで、4 秒以内）</summary>
        public static bool MouseUser { get { return lastMouseT > lastKeyT && Time.unscaledTime - lastMouseT < 4; } }
        /// <summary>マウスの所（box の中の「左上からの px」。box の外なら端に寄せる）。マウスで遊んでいなければ null</summary>
        public static Vector2? MouseIn(RectTransform box, float w, float h)
        {
            var ms = Mouse.current;
            if (!MouseUser || ms == null || box == null) return null;
            Vector2 lp;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(box, ms.position.ReadValue(), null, out lp)) return null;
            var r = box.rect;
            return new Vector2(Mathf.Clamp(lp.x - r.xMin, 0, w), Mathf.Clamp(r.yMax - lp.y, 0, h));
        }

        /// <summary>
        /// 面選び・ショップ・結果（人ごとの指）のように、**マウスが人の指を動かす画面**。本物のカーソルは画面いっぱいで消す
        /// （出ていると、人の指と2つ見える。窓の形でできる板の外の余白でも消す。JS版 c846acd）。毎フレーム呼ぶ
        /// </summary>
        public static void NoCursor()
        {
            usedFrame = Time.frameCount;
            FingerCursor(false);
            On = false; viaStick = false; Moved = false;
            if (img != null && img.gameObject.activeSelf) img.gameObject.SetActive(false);
            Cursor.visible = false;
        }

        /// <summary>Hud の毎フレームの最後に呼ぶ。このフレームにメニューが無ければ、指をしまって元のカーソルに戻す。</summary>
        public static void EndFrame()
        {
            if (usedFrame == Time.frameCount) return;
            if (!curSet && !On && (img == null || !img.gameObject.activeSelf)) return;
            FingerCursor(false);
            On = false; viaStick = false; Moved = false;
            Draw();
        }

        /// <summary>指の下に居るか（画面の物の当たり。オーバーレイのキャンバス用）。</summary>
        public static bool Over(RectTransform rt)
        {
            return On && rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, Pos, null);
        }

        /// <summary>物のまん中（画面の座標）。指の出はじめに使う。</summary>
        public static Vector2? Center(RectTransform rt)
        {
            if (rt == null) return null;
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            return (Vector2)((c[0] + c[2]) / 2f);
        }

        // ------------------------------------------------------------ 描く
        static void Draw()
        {
            bool show = On && viaStick;
            Cursor.visible = !show;
            if (!show && img == null) return;
            Make();
            if (img.gameObject.activeSelf != show) img.gameObject.SetActive(show);
            if (show) img.position = Pos;
        }

        static void Make()
        {
            if (img != null) return;
            var go = new GameObject("Ptr");
            Object.DontDestroyOnLoad(go);
            var cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 500;                                  // 画面のいちばん上
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1280, 720);
            sc.matchWidthOrHeight = 0.5f;
            var h = new GameObject("Finger", typeof(RectTransform));
            h.transform.SetParent(go.transform, false);
            var ri = h.AddComponent<RawImage>();
            ri.texture = DataRoot.Texture("ui/ptr/normal.png"); ri.raycastTarget = false;
            img = ri.rectTransform;
            // 清書1109 の「ノーマル」の指（HTML版 assets/ui/ptr/normal.png。PSD の大きさなので半分の 62×69 で置く）。指先は (24.5, 4)
            img.sizeDelta = new Vector2(62, 69);
            img.pivot = new Vector2(24.5f / 62f, 1f - 4f / 69f);
            img.anchorMin = img.anchorMax = Vector2.zero;
            h.SetActive(false);
        }

        /// <summary>マウスのカーソルを指の絵にする／戻す（JS版の CSS の cursor:url(指)）。</summary>
        static void FingerCursor(bool on)
        {
            if (on == curSet) return;
            curSet = on;
            if (on)
            {
                // マウスのカーソルの絵（HTML版 assets/ui/ptr/normal_cur.png、62×69。指先 24, 4）。
                // カーソルにする絵は読み書きできて縮小版の無い RGBA32 にしておく
                if (curTex == null)
                {
                    var src = DataRoot.Texture("ui/ptr/normal_cur.png");
                    if (src != null)
                    {
                        curTex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                        curTex.SetPixels32(src.GetPixels32(0)); curTex.Apply(false);
                    }
                }
                if (curTex != null) Cursor.SetCursor(curTex, new Vector2(24, 4), CursorMode.Auto);
            }
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
