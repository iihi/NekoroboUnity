using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// ストーリーの画面。JS版の #talk（会話）・#tutBar（遊んでいる間の案内）・#bang（ステージの終わりの知らせ）を写したもの。
    /// 位置と大きさは 1280×720 の画面で CSS と同じ数値（1px ＝ ここの 1）。
    ///
    ///   会話 … 面の前後とショップで出す。読み終わるまでゲームは止まる（3・2・1 も止まる）。
    ///          決定（Enter・スペース・Z／パッドの A・X）か画面のクリックで次へ。スキップか Esc で全部飛ばす。
    ///   案内 … 左下の店主の吹き出し。操作は止めない。ロボが後ろに入ったら薄くする。
    ///   バン … 「STAGE CLEAR!」「FAILED…」を 1.5 秒出してから、結果へ移る。
    /// </summary>
    public class StoryUi
    {
        readonly Game g;
        readonly RectTransform root;

        // ---- 会話
        RectTransform talkRoot, talkBox, talkSkip, talkKeyBg;
        Text talkTitle, talkTitleSmall, talkText, talkNx, talkName, talkArrow, talkKey;
        RawImage talkFrame, faceL, faceR;
        CanvasGroup talkCg;
        string talkSide; float whoT = 1;
        static readonly Color TALK_INK = Mats.Hex(0x1a1a1a), TALK_GREY = Mats.Hex(0x8f8f8f);
        List<string[]> lines;
        int li;
        System.Action done;
        bool prevKey = true;
        float popT;
        public bool TalkOn { get; private set; }

        // ---- 案内
        RectTransform tutRoot, tutBubble;
        Image tutBg, tutRing, tutTail, tutFace;
        Text tutSt, tutText;
        CanvasGroup tutCg;
        float tutPop = 1;
        public bool TutShown { get { return tutRoot.gameObject.activeSelf; } }

        // ---- バン
        RectTransform bangRoot, bangBox, bangBx, bangWord; Graphic bangSl; CanvasGroup bangCg; float bangRot, bangSlA;
        float bangT = -1; System.Action bangThen;
        public bool BangOn { get { return bangT >= 0; } }

        static readonly Color INK = Mats.Hex(0x3b2a1c), PAPER = Mats.Hex(0xfffdf6), TEXT = Mats.Hex(0x2a2018);

        public StoryUi(RectTransform parent, Game game)
        {
            g = game;
            root = UiKit.Rect(parent, "Story");
            UiKit.Stretch(root);
            BuildArrow();
            BuildBang();
            BuildTut();
            BuildTalk();
        }

        // ================================================================ 会話
        static readonly Dictionary<string, string[]> TALKERS = new Dictionary<string, string[]>
        {
            { "店主", new[] { "屋台の店主", "L" } },
            { "ロボ", new[] { "ネコ配ロボ", "R" } },
        };

        void BuildTalk()
        {
            talkRoot = UiKit.Rect(root, "Talk"); UiKit.Stretch(talkRoot);
            // 暗い幕（上は薄く、下は濃く）。押すと次へ
            var bg = talkRoot.gameObject.AddComponent<RawImage>();
            bg.texture = VGrad(new Color(10 / 255f, 14 / 255f, 22 / 255f, 0.25f), new Color(10 / 255f, 14 / 255f, 22 / 255f, 0.72f));
            bg.raycastTarget = true;
            var box = Box1280(talkRoot);
            // 見出し（STAGE 1 ／ はじめての配達）
            talkTitleSmall = Lbl(box, "", 14, new Color(1, 1, 1, 0.8f), TextAnchor.UpperCenter, 0, 22, 1280, 20);
            talkTitle = Lbl(box, "", 28, Color.white, TextAnchor.UpperCenter, 0, 40, 1280, 40);
            foreach (var t in new[] { talkTitleSmall, talkTitle })
            {
                var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.53f); sh.effectDistance = new Vector2(0, -3);
            }
            // 枠と顔は PSD（2560×1440）から切り出した絵。1280×720 の板に半分の座標で置く。
            // 話している人だけを出す（店主は左・ロボは右）
            faceL = UiBoard.Img(box, "ui/face_owner.png", 24, 447, 171, 236, "FaceL");
            faceR = UiBoard.Img(box, "ui/face_robo.png", 1098, 459, 178, 220.5f, "FaceR");
            foreach (var f in new[] { faceL, faceR })
            {
                var sh = f.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.35f); sh.effectDistance = new Vector2(0, -8);
            }
            // 吹き出し（出るときに 50% 85% を中心に弾む）
            talkBox = UiBoard.Box(box, 0, 0, 1280, 720, "TalkBox");
            talkBox.pivot = new Vector2(0.5f, 0.15f); talkBox.anchoredPosition = new Vector2(640, -720 * 0.85f);
            talkCg = talkBox.gameObject.AddComponent<CanvasGroup>();
            talkFrame = UiBoard.Img(talkBox, "ui/talk/frame_owner.png", 219, 531.5f, 844, 158, "Frame");
            talkName = UiBoard.T(talkBox, "", 285.5f, 535, 26, TALK_INK);
            talkText = UiBoard.T(talkBox, "", 266, 580, 24, TALK_INK);
            talkText.horizontalOverflow = HorizontalWrapMode.Wrap;
            talkText.rectTransform.sizeDelta = new Vector2(760, 90);
            talkText.lineSpacing = 1.05f;
            // 何行目か・▼・決定のボタン（右下）
            talkNx = UiBoard.T(talkBox, "", 0, 649.75f, 17, TALK_GREY, 1);
            talkArrow = UiBoard.T(talkBox, "▼", 0, 649.75f, 17, TALK_GREY, 1);
            var kb = UiBoard.Fill(talkBox, TALK_GREY, 0, 647, 40, 22.5f, 6, "Key");
            talkKeyBg = kb.rectTransform;
            talkKey = UiBoard.T(talkKeyBg, "", 0, 3.75f, 15, Color.white, 2);
            talkKey.rectTransform.anchorMin = talkKey.rectTransform.anchorMax = new Vector2(0.5f, 1);
            // スキップ
            var sk = Img(box, new Color(0, 0, 0, 0.35f), 15, "Skip");
            Ring(sk.transform, new Color(1, 1, 1, 0.53f), 2, 15);
            var sl = UiKit.Label(sk.transform, "スキップ ▶▶", 13, Color.white, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(sl.rectTransform);
            talkSkip = sk.rectTransform;
            Place(talkSkip, 1280 - 18 - 106, 18, 106, 31);
            talkRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// ロボの表情。せりふの3つ目に書けばそれ（"smile" / "smile2" / "fear"）。
        /// 書かなければ、せりふから決める：「……」はこわごわ（怯え）、「にゃー！」と伸ばすのは＞＜の笑顔（JS版 roboFace）
        /// </summary>
        static string RoboFace(string text, string face)
        {
            if (!string.IsNullOrEmpty(face)) return face == "smile" ? "" : face;
            var t = text ?? "";
            return t.Contains("……") || t.EndsWith("…") ? "fear" : t.Contains("ー！") ? "smile2" : "";
        }

        /// <summary>会話を出す。lines は [話す人, 文] の並び。読み終わったら done。</summary>
        public void TalkOpen(List<string[]> ls, System.Action onDone, string title)
        {
            if (ls == null || ls.Count == 0) { if (onDone != null) onDone(); return; }
            lines = ls; li = 0; done = onDone;
            prevKey = true;                 // 押したままの指で読み飛ばさない
            Game.SeUi("talk_open");
            var tt = (title ?? "").Split('　');
            talkTitleSmall.text = tt.Length > 1 ? tt[0] : "";
            talkTitle.text = tt.Length > 1 ? tt[1] : tt[0];
            if (tt.Length <= 1) { talkTitle.rectTransform.anchoredPosition = new Vector2(0, -22); }
            else talkTitle.rectTransform.anchoredPosition = new Vector2(0, -40);
            TalkOn = true;
            talkRoot.gameObject.SetActive(true);
            talkRoot.SetAsLastSibling();
            TalkRender();
        }

        void TalkRender()
        {
            var ln = lines[li];
            string who = ln.Length > 0 ? ln[0] : "", text = ln.Length > 1 ? ln[1] : "";
            string[] sp; if (!TALKERS.TryGetValue(who, out sp)) sp = new[] { who, "L" };
            bool L = sp[1] == "L";
            // 話している人の顔だけ。前と同じ人が続けて話すときは、顔を飛び込ませない
            faceL.gameObject.SetActive(L); faceR.gameObject.SetActive(!L);
            if (!L)
            {
                string rf = RoboFace(text, ln.Length > 2 ? ln[2] : null);
                var ft = DataRoot.Texture("ui/face_robo" + (rf != "" ? "_" + rf : "") + ".png");
                if (ft == null) ft = DataRoot.Texture("ui/face_robo.png");
                if (ft != null) faceR.texture = ft;
            }
            if (li == 0 || talkSide != sp[1]) whoT = 0;
            talkSide = sp[1];
            var fr = DataRoot.Texture("ui/talk/frame_" + (L ? "owner" : "robo") + ".png");
            if (fr != null) { talkFrame.texture = fr; talkFrame.rectTransform.sizeDelta = new Vector2(fr.width * 158f / fr.height, 158); }
            talkName.text = Lang.Tr(sp[0]);
            talkName.rectTransform.anchoredPosition = new Vector2(L ? 285.5f : 936, talkName.rectTransform.anchoredPosition.y);
            talkText.text = TutText(text, 24);
            // 右下：1／5 ▼ [Enter]
            talkKey.text = Gamepad.all.Count > 0 ? "A" : "Enter";
            float kw = talkKey.preferredWidth + 12, right = 1280 - 266.5f;
            talkKeyBg.sizeDelta = new Vector2(kw, 22.5f); talkKeyBg.anchoredPosition = new Vector2(right - kw, -647);
            talkKey.rectTransform.anchoredPosition = new Vector2(0, -3.75f + 15 * 0.12f);
            float ax = right - kw - 7;
            talkArrow.rectTransform.anchoredPosition = new Vector2(ax, talkArrow.rectTransform.anchoredPosition.y);
            talkNx.text = (li + 1) + "／" + lines.Count;
            talkNx.rectTransform.anchoredPosition = new Vector2(ax - talkArrow.preferredWidth - 7, talkNx.rectTransform.anchoredPosition.y);
            popT = 0;
        }

        void TalkNext()
        {
            Game.SeUi("talk");
            if (++li >= lines.Count) TalkClose(false);
            else TalkRender();
        }

        /// <summary>畳む。quiet なら、後に続く処理（done）を呼ばない（面を作り直すとき）。</summary>
        public void TalkClose(bool quiet)
        {
            if (!TalkOn) return;
            TalkOn = false;
            talkRoot.gameObject.SetActive(false);
            var d = done; done = null;
            if (d != null && !quiet) d();
        }

        void TalkTick(float dt)
        {
            // 箱が出るときの小さな弾み（scale .97 → 1、薄い → 濃い。0.22 秒）
            popT += dt;
            float k = Mathf.Clamp01(popT / 0.22f), e = ComicUi.Bezier(k, 0.2f, 1.4f, 0.4f, 1);
            talkBox.localScale = Vector3.one * Mathf.LerpUnclamped(0.97f, 1f, e);
            talkCg.alpha = Mathf.Lerp(0.4f, 1, Mathf.Clamp01(e));
            // 顔が下から飛び込む（0.25 秒）
            whoT = Mathf.Min(1, whoT + dt / 0.25f);
            {
                float ew = ComicUi.Bezier(whoT, 0.2f, 1.4f, 0.4f, 1);
                var f = talkSide == "R" ? faceR : faceL;
                float fy = talkSide == "R" ? 459 : 447;
                f.rectTransform.anchoredPosition = new Vector2(f.rectTransform.anchoredPosition.x, -(fy + 24 * (1 - ew)));
                f.color = new Color(1, 1, 1, Mathf.Clamp01(ew));
            }
            // ▼ が上下に揺れる（0.7 秒で行って戻る）
            {
                float u = Time.unscaledTime / 0.7f, ph = u - Mathf.Floor(u);
                if (((int)Mathf.Floor(u)) % 2 == 1) ph = 1 - ph;
                var ar = talkArrow.rectTransform;
                ar.anchoredPosition = new Vector2(ar.anchoredPosition.x, -649.75f + 17 * 0.12f - (-2 + 4 * ph));
                var c = talkArrow.color; c.a = Mathf.Lerp(0.35f, 1, ph); talkArrow.color = c;
            }
            var kb = Keyboard.current;
            bool now = kb != null && (kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed || kb.zKey.isPressed);
            foreach (var gp in Gamepad.all) if (gp.buttonSouth.isPressed || gp.buttonWest.isPressed) now = true;
            var ms = Mouse.current;
            bool click = ms != null && ms.leftButton.wasPressedThisFrame;
            if (click)
            {
                Vector2 lp;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(talkSkip, ms.position.ReadValue(), null, out lp);
                if (talkSkip.rect.Contains(lp)) { TalkClose(false); return; }
            }
            if ((now && !prevKey) || click) TalkNext();
            prevKey = now;
            if (TalkOn && kb != null && kb.escapeKey.wasPressedThisFrame) TalkClose(false);
        }

        // ================================================================ 案内
        void BuildTut()
        {
            tutRoot = UiKit.Rect(root, "Tut"); UiKit.Stretch(tutRoot);
            var box = Box1280(tutRoot);
            tutCg = box.gameObject.AddComponent<CanvasGroup>();
            tutCg.blocksRaycasts = false; tutCg.interactable = false;
            // 店主の顔
            var fr = UiKit.Rect(box, "Face");
            fr.anchorMin = fr.anchorMax = new Vector2(0, 1); fr.pivot = new Vector2(0, 1);
            fr.sizeDelta = new Vector2(78, 78); fr.anchoredPosition = new Vector2(12, -(720 - 12 - 78));
            tutFace = fr.gameObject.AddComponent<Image>();
            var tex = Resources.Load<Texture2D>("Ui/face_owner");
            if (tex != null) tutFace.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
            tutFace.raycastTarget = false;
            // 吹き出し（左下を原点にして、文の大きさで伸びる）
            tutBubble = UiKit.Rect(box, "Bubble");
            tutBubble.anchorMin = tutBubble.anchorMax = new Vector2(0, 1); tutBubble.pivot = new Vector2(0, 0);
            var sh = Img(tutBubble, new Color(59 / 255f, 42 / 255f, 28 / 255f, 0.35f), 14, "Shadow");
            UiKit.Stretch(sh.rectTransform); sh.rectTransform.offsetMin = new Vector2(0, -5); sh.rectTransform.offsetMax = new Vector2(0, -5);
            tutBg = Img(tutBubble, PAPER, 14, "Bg"); UiKit.Stretch(tutBg.rectTransform);
            tutRing = Ring(tutBubble, INK, 4, 14);
            // しっぽ（左を向いた三角）
            var tl = UiKit.Rect(tutBubble, "Tail");
            tl.anchorMin = tl.anchorMax = new Vector2(0, 0); tl.pivot = new Vector2(1, 0);
            tl.sizeDelta = new Vector2(12, 16); tl.anchoredPosition = new Vector2(0, 16);
            tutTail = tl.gameObject.AddComponent<Image>(); tutTail.sprite = Tri(); tutTail.color = INK; tutTail.raycastTarget = false;
            tutSt = UiKit.Label(tutBubble, "", 11, Mats.Hex(0x8a7a66));
            tutText = UiKit.Label(tutBubble, "", 18, TEXT);
            tutText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tutText.alignment = TextAnchor.UpperLeft;
            tutText.lineSpacing = 1.1f;
            tutRoot.gameObject.SetActive(false);
        }

        /// <summary>案内を出す。st は「1 / 2」、ok なら「できた」の色。pop なら弾ませて出す。</summary>
        public void TutShow(string text, string st, bool ok, bool pop)
        {
            tutRoot.gameObject.SetActive(true);
            var ink = ok ? Mats.Hex(0x1a7a3c) : INK;
            tutBg.color = ok ? Mats.Hex(0xeafbe9) : PAPER;
            tutRing.color = ink; tutTail.color = ink;
            tutText.color = ok ? Mats.Hex(0x1a7a3c) : TEXT;
            tutText.text = TutText(text, 18);
            tutSt.text = st ?? "";
            // 幅は文の長さ（最大 476）。高さは折り返したぶん
            const float maxW = 560 - 78 - 6 - 40;
            float w = Mathf.Min(maxW, tutText.preferredWidth + 2);
            var set = tutText.GetGenerationSettings(new Vector2(w, 0));
            float th = tutText.cachedTextGeneratorForLayout.GetPreferredHeight(tutText.text, set) / tutText.pixelsPerUnit;
            float sth = tutSt.text.Length > 0 ? 15 : 0;
            float bw = w + 40, bh = th + sth + 18 + 8;
            tutBubble.sizeDelta = new Vector2(bw, bh);
            tutBubble.anchoredPosition = new Vector2(12 + 78 + 6 + 12, -(720 - 12 - 12));
            var sr = tutSt.rectTransform; sr.anchorMin = sr.anchorMax = new Vector2(0, 1); sr.pivot = new Vector2(0, 1);
            sr.anchoredPosition = new Vector2(20, -11); sr.sizeDelta = new Vector2(w, 15);
            var tr = tutText.rectTransform; tr.anchorMin = tr.anchorMax = new Vector2(0, 1); tr.pivot = new Vector2(0, 1);
            tr.anchoredPosition = new Vector2(20, -(11 + sth)); tr.sizeDelta = new Vector2(w, th + 4);
            if (pop) tutPop = 0;
        }

        public void TutHide() { tutRoot.gameObject.SetActive(false); }

        // ================================================================ 案内の矢印
        // **画面に重ねる平面の「⇩」**（JS版 #tutArrow の SVG と同じ形）。指す物の上に置いて、上下に弾ませる。
        // 絵は SVG の座標（52×54）で描いて、46×48 で出す。影（drop-shadow 0 3px 3px）も絵に焼き込む。
        RawImage arrow;
        const float AR_PAD = 6, AR_W = 52 + AR_PAD * 2, AR_H = 54 + AR_PAD * 2 + 4, AR_K = 46f / 52f;

        void BuildArrow()
        {
            arrow = UiKit.Rect(root, "TutArrow").gameObject.AddComponent<RawImage>();
            arrow.raycastTarget = false;
            arrow.texture = ArrowTex();
            var r = arrow.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.sizeDelta = new Vector2(AR_W * AR_K, AR_H * AR_K);
            // 置く点は絵の下のまん中（SVG の (26, 54)）。JS版の translate(-50%, -100%)
            r.pivot = new Vector2((26 + AR_PAD) / AR_W, 1 - (54 + AR_PAD) / AR_H);
            arrow.gameObject.SetActive(false);
        }

        /// <summary>矢印を画面の点 sp（WorldToScreenPoint）に出す。bounce は上へ持ち上げる量（CSS の px）。</summary>
        public void ArrowAt(Vector3 sp, float bounce)
        {
            var cv = root.GetComponentInParent<Canvas>();
            float k = cv != null ? cv.scaleFactor : 1;
            arrow.rectTransform.position = new Vector3(sp.x, sp.y + bounce * k, 0);
            if (!arrow.gameObject.activeSelf) arrow.gameObject.SetActive(true);
        }
        public void ArrowHide() { if (arrow.gameObject.activeSelf) arrow.gameObject.SetActive(false); }

        static Texture2D arrowTex;
        static Texture2D ArrowTex()
        {
            if (arrowTex != null) return arrowTex;
            const float S = 3;                                     // SVG の 1 単位を何ピクセルで描くか
            int W = Mathf.CeilToInt(AR_W * S), H = Mathf.CeilToInt(AR_H * S);
            // M16 3h20v23h13L26 51 3 26h13z
            var poly = new[] { new Vector2(16, 3), new Vector2(36, 3), new Vector2(36, 26), new Vector2(49, 26),
                               new Vector2(26, 51), new Vector2(3, 26), new Vector2(16, 26) };
            var hl = new[] { new Vector2(20, 7), new Vector2(26, 7), new Vector2(26, 27) };   // M20 7h6v20（光）
            System.Func<Vector2, Vector2, Vector2, float> seg = (p, a, b) =>
            {
                var d = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
                return (a + d * t - p).magnitude;
            };
            System.Func<Vector2, bool> inside = p =>
            {
                bool c = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                        p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
                return c;
            };
            Color fill = Mats.Hex(0xffc83a), ink = Mats.Hex(0x2f2a22), shine = Mats.Hex(0xfff3c4);
            var col = new Color[W * H]; var al = new float[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // テクスチャは下から上。SVG は上から下
                    var p = new Vector2((x + 0.5f) / S - AR_PAD, (H - 1 - y + 0.5f) / S - AR_PAD);
                    float dEdge = 1e9f;
                    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) dEdge = Mathf.Min(dEdge, seg(p, poly[j], poly[i]));
                    float sd = inside(p) ? -dEdge : dEdge;                // 中なら負
                    float a = Mathf.Clamp01(0.5f - (sd - 1.75f) * S);     // 線（3.5）の外側まで
                    float st = Mathf.Clamp01((sd + 1.75f) * S + 0.5f);    // 線の内側の縁から先は線の色
                    var c = Color.Lerp(fill, ink, st);
                    float dh = Mathf.Min(seg(p, hl[0], hl[1]), seg(p, hl[1], hl[2]));
                    float h = Mathf.Clamp01(0.5f - (dh - 1.5f) * S) * 0.8f * (1 - st);
                    c = Color.Lerp(c, shine, h);
                    col[y * W + x] = c; al[y * W + x] = a;
                }
            // 影：形の透明度をぼかして、下へずらす（CSS の 3px は SVG の 3/AR_K 単位）
            var sh = Blur(al, W, H, Mathf.RoundToInt(1.5f / AR_K * S));
            int dy = Mathf.RoundToInt(3 / AR_K * S);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int sy = y + dy;
                    float sa = sy < H ? sh[sy * W + x] * 0.4f : 0;
                    float a = al[y * W + x];
                    float oa = a + sa * (1 - a);
                    var c = oa > 0 ? (col[y * W + x] * a + Color.black * sa * (1 - a)) / oa : Color.clear;
                    c.a = oa;
                    px[y * W + x] = c;
                }
            arrowTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            arrowTex.SetPixels(px); arrowTex.Apply(false);
            return arrowTex;
        }

        /// <summary>箱ぼかしを3回（ガウスぼかしの代わり）。r は半径のピクセル数。</summary>
        static float[] Blur(float[] src, int W, int H, int r)
        {
            var a = (float[])src.Clone(); var b = new float[W * H];
            int rr = Mathf.Max(1, Mathf.RoundToInt(r * 0.8f));
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float s = 0; int n = 0;
                        for (int k = -rr; k <= rr; k++) { int xx = x + k; if (xx >= 0 && xx < W) s += a[y * W + xx]; n++; }
                        b[y * W + x] = s / n;
                    }
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float s = 0; int n = 0;
                        for (int k = -rr; k <= rr; k++) { int yy = y + k; if (yy >= 0 && yy < H) s += b[yy * W + x]; n++; }
                        a[y * W + x] = s / n;
                    }
            }
            return a;
        }

        void TutTick(float dt)
        {
            if (!TutShown) return;
            tutPop = Mathf.Min(1, tutPop + dt / 0.32f);
            float e = EaseBack(tutPop, 1.5f);
            tutBubble.localScale = Vector3.one * Mathf.LerpUnclamped(0.7f, 1f, e);
            // ロボが吹き出しの後ろに入ったら薄くする（客や床が隠れないように）
            float a = 1;
            var P = g.me;
            if (P != null && P.ent != null && g.mainCam != null)
            {
                var sp = g.mainCam.WorldToScreenPoint(P.ent.transform.position);
                Vector2 lp;
                if (sp.z > 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(tutBubble, sp, null, out lp))
                {
                    var r = tutBubble.rect;
                    if (lp.x > r.xMin - 30 - 96 && lp.x < r.xMax + 30 && lp.y > r.yMin - 20 && lp.y < r.yMax + 40) a = 0.28f;
                }
            }
            tutCg.alpha = Mathf.MoveTowards(tutCg.alpha, a, dt / 0.2f);
        }

        // ================================================================ バン
        void BuildBang()
        {
            bangRoot = UiKit.Rect(root, "Bang"); UiKit.Stretch(bangRoot);
            var bg = bangRoot.gameObject.AddComponent<RawImage>();
            bg.texture = Radial(); bg.raycastTarget = false;
            bangBox = Box1280(bangRoot);
            bangRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// ステージの終わりの大きな知らせ（マンガ調。JS版 showBang）。1.5 秒出してから then を呼ぶ。
        /// 集中線＋ギザギザの吹き出し＋太い縁取りの字。タイムアップは青、クリアは黄、失敗は灰色
        /// </summary>
        public void Bang(bool cleared, bool timeUp, string sub, System.Action then)
        {
            bool tu = cleared && timeUp;
            if (bangSl != null) Object.Destroy(bangSl.gameObject);
            if (bangBx != null) Object.Destroy(bangBx.gameObject);
            bangSl = ComicUi.SpeedLines(bangRoot);
            bangSl.transform.SetSiblingIndex(0);
            bangSlA = cleared ? 0.45f : 0.25f;
            bangBx = UiKit.Rect(bangBox, "Bx");
            bangBx.anchorMin = bangBx.anchorMax = new Vector2(0.5f, 0.5f); bangBx.pivot = new Vector2(0.5f, 0.5f);
            bangBx.sizeDelta = new Vector2(1060, 400); bangBx.anchoredPosition = new Vector2(0, 25);
            bangCg = bangBx.gameObject.AddComponent<CanvasGroup>();
            bangWord = null;
            var fin = cleared ? DataRoot.Texture("ui/play/finish.png") : null;
            if (fin != null)
            {
                // クリアとタイムアップは FINISH!（清書1109 の絵。トゲトゲの上に字、字は少し遅れて弾む）
                bangBx.sizeDelta = new Vector2(817, 393.5f); bangBx.anchoredPosition = Vector2.zero;
                var bst = new GameObject("Bst", typeof(RectTransform)).AddComponent<RawImage>();
                bst.rectTransform.SetParent(bangBx, false); UiKit.Stretch(bst.rectTransform); bst.raycastTarget = false;
                bst.texture = DataRoot.Texture("ui/play/finish_bst.png");
                var wd = new GameObject("Word", typeof(RectTransform)).AddComponent<RawImage>();
                wd.rectTransform.SetParent(bangBx, false); UiKit.Stretch(wd.rectTransform); wd.raycastTarget = false;
                wd.texture = fin; wd.gameObject.AddComponent<CanvasGroup>();
                bangWord = wd.rectTransform;
            }
            else
            {
                var cc = Mats.Hex(tu ? 0x1f5fd0 : cleared ? 0x13924a : 0xd0302a);
                ComicUi.Burst(bangBx, 1060, 400, 22, Mats.Hex(tu ? 0x7fd0ff : cleared ? 0xffd400 : 0xa9b8cc), Mats.Hex(cleared ? 0xfffbe6 : 0xeef2f7));
                var l1 = ComicUi.Label(bangBx, tu ? "TIME UP!!" : cleared ? "STAGE CLEAR!!" : "FAILED…", 84, Color.white, cc);
                l1.rectTransform.anchoredPosition = new Vector2(0, 16);
                var l3 = ComicUi.Label(bangBx, Lang.Tr(sub), 22, Color.white, new Color(0, 0, 0, 0), 3, Vector2.zero);
                l3.rectTransform.anchoredPosition = new Vector2(0, -49);
            }
            bangRot = -4;                                             // CSS の --rot:-4deg（右回りが正）
            Game.SeUi(cleared ? "clear" : "fail");
            bangThen = then; bangT = 0;
            bangRoot.gameObject.SetActive(true);
            BangTick(0);
        }

        /// <summary>出している知らせを捨てる（やり直しや次の面が始まったとき）。</summary>
        public void HideBang() { bangT = -1; bangThen = null; bangRoot.gameObject.SetActive(false); }

        void BangTick(float dt)
        {
            if (bangT < 0) return;
            bangT += dt;
            // 出てくる（0.5 秒。回りながら 0.15 → 1、少し行き過ぎて戻る）、そのあと 0.45 秒ごとに 1 ↔ 1.05 で脈打つ
            float k = Mathf.Clamp01(bangT / 0.5f), e = ComicUi.Bezier(k, 0.2f, 1.7f, 0.4f, 1);
            float rot = Mathf.LerpUnclamped(bangRot * 5, bangRot, e), sc = Mathf.LerpUnclamped(0.15f, 1, e);
            if (bangT > 0.5f)
            {
                float u = (bangT - 0.5f) / 0.45f, ph = u - Mathf.Floor(u);
                if (((int)Mathf.Floor(u)) % 2 == 1) ph = 1 - ph;
                sc = 1 + 0.05f * (0.5f - 0.5f * Mathf.Cos(ph * Mathf.PI));
            }
            bangBx.localScale = Vector3.one * sc;
            bangBx.localRotation = Quaternion.Euler(0, 0, -rot);
            bangCg.alpha = Mathf.Clamp01(e);
            if (bangWord != null)
            {
                float kw = Mathf.Clamp01((bangT - 0.08f) / 0.45f), ew = ComicUi.Bezier(kw, 0.2f, 1.8f, 0.4f, 1);
                bangWord.localScale = Vector3.one * Mathf.LerpUnclamped(1.8f, 1, ew);
                bangWord.GetComponent<CanvasGroup>().alpha = Mathf.Clamp01(ew);
            }
            // 集中線（0.3 秒で 1.35 → 1）
            float ks = Mathf.Clamp01(bangT / 0.3f), es = 1 - Mathf.Pow(1 - ks, 3);
            bangSl.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1, es);
            var c = bangSl.color; c.a = bangSlA * es; bangSl.color = c;
            if (bangT >= 1.5f)
            {
                var then = bangThen;
                HideBang();
                if (then != null) then();
            }
        }

        // ================================================================ 毎フレーム
        public void Tick(float dt)
        {
            if (TalkOn) TalkTick(dt);
            tutRoot.gameObject.SetActive(tutRoot.gameObject.activeSelf && !TalkOn);
            TutTick(dt);
            BangTick(dt);
        }

        // ================================================================ 文
        /// <summary>
        /// 案内の文。{up} などはその人の押すボタンに置き換える（パッドがあればパッドの名前）。JS版 tutText。
        /// JS版はボタン名を黒い札（kbd）にしている。ここの字は札を描けないので、色を変えて［ ］で囲む。
        /// </summary>
        public static string TutText(string s, int size)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return System.Text.RegularExpressions.Regex.Replace(s, @"\{(\w+)\}", m => Key(m.Groups[1].Value, size));
        }

        static string Key(string k, int size)
        {
            bool pd = Gamepad.all.Count > 0;
            string v;
            switch (k)
            {
                case "up": v = pd ? "A" : "↑"; break;
                case "back": v = pd ? "B" : "↓"; break;
                case "turn": v = pd ? "スティック" : "← →"; break;
                case "jump": v = pd ? "X(□)" : "スペース"; break;
                case "use": v = pd ? "Y(△)" : "Z"; break;
                case "cycle": v = pd ? "R1" : "X"; break;
                case "ok": v = pd ? "A" : "Enter"; break;
                case "move": v = pd ? "スティック" : "↑↓←→"; break;
                default: return "{" + k + "}";
            }
            return "<color=#9a5a1c>［" + v + "］</color>";
        }

        // ================================================================ 部品
        static RectTransform Box1280(RectTransform parent)
        {
            var b = UiKit.Rect(parent, "Box");
            b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.5f); b.pivot = new Vector2(0.5f, 0.5f);
            b.sizeDelta = new Vector2(1280, 720);
            return b;
        }

        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
        }

        static Text Lbl(RectTransform parent, string s, float size, Color c, TextAnchor al, float x, float y, float w, float h)
        {
            var t = UiKit.Label(parent, s, size, c, true, al);
            Place(t.rectTransform, x, y, w, h);
            return t;
        }

        static Image Img(Transform parent, Color c, float radius, string name)
        {
            return UiKit.Img(parent, c, radius, name);
        }

        static Image Ring(Transform parent, Color c, int th, float radius)
        {
            var b = UiKit.Img(parent, c, 0, "Ring");
            b.sprite = UiKit.Ring(Mathf.Max(1, Mathf.RoundToInt(th * 15.5f / radius)));
            b.type = Image.Type.Sliced; b.pixelsPerUnitMultiplier = 15.5f / radius;
            UiKit.Stretch(b.rectTransform);
            return b;
        }

        /// <summary>行き過ぎて戻る（CSS の cubic-bezier(.2, 1.5, .4, 1) のかわり）。</summary>
        static float EaseBack(float k, float over)
        {
            float c1 = over, c3 = c1 + 1;
            return 1 + c3 * Mathf.Pow(k - 1, 3) + c1 * Mathf.Pow(k - 1, 2);
        }

        static Texture2D VGrad(Color top, Color bottom)
        {
            var t = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) t.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            t.Apply(false);
            return t;
        }

        /// <summary>まん中が暗い楕円（radial-gradient(60% 50% at 50% 45%, rgba(0,0,0,.45), transparent)）。</summary>
        static Texture2D Radial()
        {
            const int W = 128, H = 72;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = 1 - (y + 0.5f) / H;
                    float d = Mathf.Sqrt(Mathf.Pow((u - 0.5f) / 0.6f, 2) + Mathf.Pow((v - 0.45f) / 0.5f, 2));
                    t.SetPixel(x, y, new Color(0, 0, 0, 0.45f * Mathf.Clamp01(1 - d)));
                }
            t.Apply(false);
            return t;
        }

        static Sprite tri;
        static Sprite Tri()
        {
            if (tri != null) return tri;
            const int W = 24, H = 32;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fy = Mathf.Abs((y + 0.5f) - H / 2f) / (H / 2f);      // 0 = まん中
                    float edge = (x + 0.5f) / W;                              // 左が先
                    float a = Mathf.Clamp01((edge - fy) * W + 0.5f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply(false);
            tri = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100);
            return tri;
        }

    }
}
