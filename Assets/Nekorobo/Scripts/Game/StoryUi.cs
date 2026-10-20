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
        RectTransform talkRoot, talkBox, talkSkip;
        Text talkTitle, talkTitleSmall, talkText, talkNx, talkName;
        Image talkNameBg, faceL, faceR;
        RectTransform talkBoxShadow;
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
        RectTransform bangRoot, bangPill;
        Text bangText, bangSub;
        Image bangBg, bangShadow;
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
            // 顔（左が店主、右がロボ）。話していないほうは暗く小さく
            faceL = Face(box, "face_owner", 1280 * 0.07f);
            faceR = Face(box, "face_robo", 1280 - 1280 * 0.07f - 230);
            // 吹き出しの箱
            talkBoxShadow = Img(box, new Color(59 / 255f, 42 / 255f, 28 / 255f, 0.4f), 18, "BoxShadow").rectTransform;
            var bb = Img(box, PAPER, 18, "Box");
            talkBox = bb.rectTransform;
            Ring(bb.transform, INK, 5, 18);
            talkText = UiKit.Label(talkBox, "", 22, TEXT);
            talkText.horizontalOverflow = HorizontalWrapMode.Wrap;
            talkText.alignment = TextAnchor.UpperLeft;
            talkText.lineSpacing = 1.12f;
            var tr = talkText.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(29, 33); tr.offsetMax = new Vector2(-29, -23);
            talkNx = UiKit.Label(talkBox, "", 12, Mats.Hex(0x8a7a66), true, TextAnchor.LowerRight);
            var nr = talkNx.rectTransform; nr.anchorMin = new Vector2(1, 0); nr.anchorMax = new Vector2(1, 0); nr.pivot = new Vector2(1, 0);
            nr.anchoredPosition = new Vector2(-21, 9); nr.sizeDelta = new Vector2(300, 18);
            // 名前の札（箱の上の縁にかかる）
            talkNameBg = Img(talkBox, Mats.Hex(0xe8682c), 17, "Name");
            Ring(talkNameBg.transform, INK, 3, 17);
            var nb = talkNameBg.rectTransform; nb.anchorMin = nb.anchorMax = new Vector2(0, 1);
            talkName = UiKit.Label(talkNameBg.transform, "", 15, Color.white, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(talkName.rectTransform);
            // スキップ
            var sk = Img(box, new Color(0, 0, 0, 0.35f), 15, "Skip");
            Ring(sk.transform, new Color(1, 1, 1, 0.53f), 2, 15);
            var sl = UiKit.Label(sk.transform, "スキップ ▶▶", 13, Color.white, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(sl.rectTransform);
            talkSkip = sk.rectTransform;
            Place(talkSkip, 1280 - 18 - 106, 18, 106, 31);
            talkRoot.gameObject.SetActive(false);
        }

        Image Face(RectTransform parent, string pic, float x)
        {
            var r = UiKit.Rect(parent, "Face_" + pic);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(230, 230);
            r.anchoredPosition = new Vector2(x + 115, -(720 - 160 - 230 + 115));
            var im = r.gameObject.AddComponent<Image>();
            var tex = Resources.Load<Texture2D>("Ui/" + pic);
            if (tex != null) im.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
            im.raycastTarget = false;
            var sh = r.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.3f); sh.effectDistance = new Vector2(0, -8);
            return im;
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
            SetFace(faceL, L); SetFace(faceR, !L);
            talkText.text = TutText(text, 22);
            // 箱の高さは文の長さで決める（最低 120）
            float tw = 880 - 58;
            var set = talkText.GetGenerationSettings(new Vector2(tw, 0));
            float th = talkText.cachedTextGeneratorForLayout.GetPreferredHeight(talkText.text, set) / talkText.pixelsPerUnit;
            float h = Mathf.Max(120, th + 23 + 33 + 4);
            Place(talkBox, 200, 720 - 30 - h, 880, h);
            Place(talkBoxShadow, 200, 720 - 30 - h + 7, 880, h);
            talkName.text = sp[0];
            talkNameBg.color = L ? Mats.Hex(0xe8682c) : Mats.Hex(0x2f9bff);
            float nw = talkName.preferredWidth + 36 + 6;
            var nb = talkNameBg.rectTransform; nb.pivot = new Vector2(L ? 0 : 1, 1);
            nb.anchorMin = nb.anchorMax = new Vector2(L ? 0 : 1, 1);
            nb.sizeDelta = new Vector2(nw, 34); nb.anchoredPosition = new Vector2(L ? 24 : -24, 21);
            talkNx.text = (li + 1) + " / " + lines.Count + "　▼ " + Key("ok", 12);
            popT = 0;
        }

        void SetFace(Image im, bool on)
        {
            im.color = on ? Color.white : new Color(0.42f, 0.42f, 0.45f, 1);
            im.rectTransform.localScale = Vector3.one * (on ? 1f : 0.9f);
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
            // 箱が出るときの小さな弾み（scale .96 → 1、薄い → 濃い）
            popT += dt;
            float k = Mathf.Clamp01(popT / 0.22f), e = EaseBack(k, 1.4f);
            talkBox.localScale = Vector3.one * Mathf.LerpUnclamped(0.96f, 1f, e);
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
            var box = Box1280(bangRoot);
            bangPill = UiKit.Rect(box, "Pill");
            bangPill.anchorMin = bangPill.anchorMax = new Vector2(0.5f, 0.5f);
            bangShadow = Img(bangPill, Mats.Hex(0x2f9448), 0, "Shadow");
            bangBg = Img(bangPill, Color.white, 0, "Bg");
            UiKit.Stretch(bangBg.rectTransform);
            var sr = bangShadow.rectTransform; UiKit.Stretch(sr); sr.offsetMin = new Vector2(0, -6); sr.offsetMax = new Vector2(0, -6);
            bangText = UiKit.Label(bangPill, "", 78, Color.white, true, TextAnchor.MiddleCenter);
            UiKit.Stretch(bangText.rectTransform);
            bangText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -4);
            bangSub = UiKit.Label(box, "", 20, Color.white, true, TextAnchor.MiddleCenter);
            var bs = bangSub.gameObject.AddComponent<Shadow>(); bs.effectColor = new Color(0, 0, 0, 0.8f); bs.effectDistance = new Vector2(0, -2);
            bangRoot.gameObject.SetActive(false);
        }

        /// <summary>ステージの終わりの大きな知らせ。1.5 秒出してから then を呼ぶ。</summary>
        public void Bang(bool cleared, string sub, System.Action then)
        {
            bangText.text = cleared ? "STAGE CLEAR!" : "FAILED…";
            Game.SeUi(cleared ? "clear" : "fail");
            bangSub.text = sub;
            var top = cleared ? Mats.Hex(0x63d47f) : Mats.Hex(0xff8f7c);
            var bot = cleared ? Mats.Hex(0x4cbf68) : Mats.Hex(0xe8604a);
            var dark = cleared ? Mats.Hex(0x2f9448) : Mats.Hex(0xc33f2c);
            float w = bangText.preferredWidth + 108 + 12, h = 78 * 1.25f + 12 + 12;
            bangPill.sizeDelta = new Vector2(w, h);
            bangPill.anchoredPosition = new Vector2(0, 19);           // 下の字と合わせて、まん中より少し上
            bangBg.sprite = UiKit.PillGrad(Mathf.RoundToInt(w), Mathf.RoundToInt(h), top, bot, 6);
            bangShadow.sprite = UiKit.Round; bangShadow.type = Image.Type.Sliced; bangShadow.pixelsPerUnitMultiplier = 15.5f / (h / 2);
            bangShadow.color = dark;
            bangText.GetComponent<Shadow>().effectColor = dark;
            var sr = bangSub.rectTransform; sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.5f);
            sr.sizeDelta = new Vector2(800, 30); sr.anchoredPosition = new Vector2(0, bangPill.anchoredPosition.y - h / 2 - 10 - 15 - 6);
            bangThen = then; bangT = 0;
            bangRoot.gameObject.SetActive(true);
        }

        /// <summary>出している知らせを捨てる（やり直しや次の面が始まったとき）。</summary>
        public void HideBang() { bangT = -1; bangThen = null; bangRoot.gameObject.SetActive(false); }

        void BangTick(float dt)
        {
            if (bangT < 0) return;
            bangT += dt;
            // 大きく傾いた所から、行き過ぎて止まる（scale 2.4 → 1、-4° → 0、0.45 秒）。下の字は 0.12 秒遅れて
            float k = Mathf.Clamp01(bangT / 0.45f), e = EaseBack(k, 1.5f);
            bangPill.localScale = Vector3.one * Mathf.LerpUnclamped(2.4f, 1f, e);
            bangPill.localRotation = Quaternion.Euler(0, 0, Mathf.LerpUnclamped(4f, 0f, e));
            var cg = bangPill.GetComponent<CanvasGroup>(); if (cg == null) cg = bangPill.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = Mathf.Clamp01(k * 2);
            float k2 = Mathf.Clamp01((bangT - 0.12f) / 0.45f), e2 = EaseBack(k2, 1.5f);
            bangSub.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(2.4f, 1f, e2);
            var sc = bangSub.color; sc.a = Mathf.Clamp01(k2 * 2); bangSub.color = sc;
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
