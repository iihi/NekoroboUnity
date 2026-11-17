using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 実績を取ったときの知らせ（HTML版 achToast。清書1111 の「実績獲得」の帯）。画面の下のまん中。
    /// 帯が左から飛び込み、光が広がって、帯に光が走り、星が1つずつ付く。いくつも取ったら1つずつ順に出す（後ろが待っているときは少し短く）。
    /// 絵は PSD の半分の大きさで置く（HTML版の assets/ui/ach/get_*）。大きさは上の帯と同じ倍率で縮める。会話が出ている間は会話の枠の上に出す。
    /// </summary>
    public class AchToast
    {
        const float SHOW = 4.4f, SHOW_Q = 3.2f;
        const string U = "ui/ach/";
        // 取った時の帯の仕分けのアイコンの大きさ（PSD の半分）
        static readonly float[][] IC_GET = { new[] { 38.5f, 57 }, new[] { 70.5f, 52 }, new[] { 58f, 58 }, new[] { 64.5f, 64 }, new[] { 68.5f, 65 }, new[] { 66f, 57 }, new[] { 68.5f, 45 } };
        readonly Game g;
        readonly RectTransform root;
        readonly Queue<AchDef> q = new Queue<AchDef>();
        Item cur;
        static Texture2D shineTex;

        class Item
        {
            public AchDef a; public float t, show;
            public RectTransform r, bd, ry, fr, ic, tx, shine; public CanvasGroup cg, cgBd, cgRy, cgFr, cgIc, cgTx;
            public readonly List<RectTransform> stars = new List<RectTransform>();
            public bool[] sePlayed = new bool[3];
        }

        public AchToast(RectTransform parent, Game game)
        {
            g = game;
            root = UiKit.Rect(parent, "AchToast");
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0); root.pivot = new Vector2(0.5f, 0);
            root.sizeDelta = new Vector2(940, 151.5f);
        }

        public void Push(AchDef a)
        {
            q.Enqueue(a);
            if (cur == null) Next();
        }

        void Next()
        {
            if (cur != null) { Object.Destroy(cur.r.gameObject); cur = null; }
            if (q.Count == 0) return;
            var a = q.Dequeue();
            cur = Build(a, q.Count > 0 ? SHOW_Q : SHOW);
            Game.SeUi("ach");
        }

        static RectTransform Pivot(RectTransform r, float px, float py)
        {
            // 箱の中の (px, py)（0〜1。y は上から）を軸にする（位置は変えない）
            var sz = r.sizeDelta; var p0 = r.pivot;
            r.pivot = new Vector2(px, 1 - py);
            r.anchoredPosition += new Vector2((r.pivot.x - p0.x) * sz.x, (r.pivot.y - p0.y) * sz.y);
            return r;
        }

        Item Build(AchDef a, float show)
        {
            var it = new Item { a = a, show = show };
            it.r = UiBoard.Box(root, 0, 0, 940, 151.5f, "Ach_" + a.id);
            it.cg = it.r.gameObject.AddComponent<CanvasGroup>();
            Pivot(it.r, 0.5f, 1);
            // 後ろの光（下のまん中を軸に広がる）
            var ry = UiBoard.Img(it.r, U + "get_ray.png", 0, 0, 940, 119);
            it.ry = Pivot(ry.rectTransform, 0.5f, 1); it.cgRy = ry.gameObject.AddComponent<CanvasGroup>();
            // 帯ぜんぶ（左から飛び込む）
            it.bd = UiBoard.Box(it.r, 0, 0, 940, 151.5f, "Bd"); it.cgBd = it.bd.gameObject.AddComponent<CanvasGroup>();
            var pt = UiBoard.Img(it.bd, U + "get_plate.png", 216.5f, 62, 570.5f, 83);
            // 帯に光が走る（帯の絵の形で切る）
            {
                var mk = pt.gameObject.AddComponent<Mask>(); mk.showMaskGraphic = true;
                if (shineTex == null)
                {
                    shineTex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    for (int i = 0; i < 64; i++) { float u = Mathf.Abs(i / 63f - 0.5f) * 2; shineTex.SetPixel(i, 0, new Color(1, 1, 1, 0.8f * Mathf.Clamp01(1 - u))); }
                    shineTex.Apply();
                }
                var sh = new GameObject("Shine", typeof(RectTransform)).AddComponent<RawImage>();
                sh.rectTransform.SetParent(pt.rectTransform, false);
                sh.texture = shineTex; sh.raycastTarget = false;
                it.shine = sh.rectTransform;
                it.shine.anchorMin = it.shine.anchorMax = new Vector2(0, 0.5f); it.shine.pivot = new Vector2(0.5f, 0.5f);
                it.shine.sizeDelta = new Vector2(274, 140); it.shine.localRotation = Quaternion.Euler(0, 0, -10);
                it.shine.anchoredPosition = new Vector2(-400, 0);
            }
            UiBoard.Img(it.bd, U + "get_stars.png", 643, 84, 102.5f, 33);
            for (int i = 0; i < a.s && i < 3; i++)
            {
                var s = UiBoard.Img(it.bd, U + "get_star_on.png", 646 + i * 34.25f, 87, 28.5f, 27);
                it.stars.Add(Pivot(s.rectTransform, 0.5f, 0.5f));
            }
            var fr = UiBoard.Img(it.bd, U + "get_frame.png", 161, 52.5f, 99, 99);
            it.fr = Pivot(fr.rectTransform, 0.5f, 0.5f); it.cgFr = fr.gameObject.AddComponent<CanvasGroup>();
            var icw = IC_GET[Mathf.Clamp(a.c, 0, IC_GET.Length - 1)];
            var ic = UiBoard.Img(it.bd, U + "bic" + a.c + ".png", 206.5f - icw[0] / 2, 98.75f - icw[1] / 2, icw[0], icw[1]);
            it.ic = Pivot(ic.rectTransform, 0.5f, 0.5f); it.cgIc = ic.gameObject.AddComponent<CanvasGroup>();
            // 字（名前「を獲得!」。名前が長いときは字を小さくして、星にかからないようにする）
            {
                it.tx = UiBoard.Box(it.bd, 280.5f, 100.5f, 0, 0, "Tx"); it.cgTx = it.tx.gameObject.AddComponent<CanvasGroup>();
                var col = Mats.Hex(0x402801);
                var nm = UiBoard.T(it.tx, "", 0, 0, 20, col); nm.text = Lang.Tr(a.n);
                var gt = UiBoard.T(it.tx, "", 0, 0, 20, col); gt.text = Lang.Tr("を獲得!");
                foreach (var t in new[] { nm, gt }) { t.rectTransform.pivot = new Vector2(0, 0.5f); t.alignment = TextAnchor.MiddleLeft; }
                float w = nm.preferredWidth + 8 + gt.preferredWidth;
                if (w > 350) { int fs = Mathf.Max(8, Mathf.FloorToInt(20 * 350 / w)); nm.fontSize = gt.fontSize = fs; }
                nm.rectTransform.anchoredPosition = Vector2.zero;
                gt.rectTransform.anchoredPosition = new Vector2(nm.preferredWidth + 8, 0);
            }
            return it;
        }

        static float Bz(float k, float x1, float y1, float x2, float y2) { return ComicUi.Bezier(Mathf.Clamp01(k), x1, y1, x2, y2); }

        public void Tick(float dt)
        {
            // 画面の大きさに合わせる（上の帯と同じ倍率）。会話が出ている間は会話の枠の上へ
            var pr = (RectTransform)root.parent;
            float s = pr.rect.width > 0 ? Mathf.Min(pr.rect.width / 1280f, pr.rect.height / 720f) : 1;
            root.localScale = Vector3.one * s;
            root.anchoredPosition = new Vector2(0, (g.TalkOn ? 215 : 30) * s);
            root.SetAsLastSibling();
            if (cur == null) return;
            var it = cur;
            it.t += dt;
            float t = it.t;
            // 全体：最後の 0.45 秒で下へ沈んで消える
            {
                float to = (t - (it.show - 0.45f)) / 0.45f, k = Mathf.Clamp01(to); k = k * k;
                it.r.anchoredPosition = new Vector2(470, -151.5f - 40 * k);
                it.r.localScale = Vector3.one * Mathf.Lerp(1, 0.9f, k);
                it.cg.alpha = 1 - k;
            }
            // 帯：0.5 秒で左から（72% で少し行き過ぎて戻る）
            {
                float u = Mathf.Clamp01(t / 0.5f), e = Bz(u, 0.2f, 1.35f, 0.35f, 1);
                it.bd.anchoredPosition = new Vector2(Mathf.LerpUnclamped(-700, 0, e), 0);
                it.cgBd.alpha = Mathf.Clamp01(u / 0.72f);
            }
            // 光：0.12 秒後に 0.6 秒で広がり、そのあとゆっくり明滅
            {
                float u = Mathf.Clamp01((t - 0.12f) / 0.6f), e = Bz(u, 0.2f, 1.3f, 0.4f, 1);
                float sy = Mathf.LerpUnclamped(0, 1, e), sx = Mathf.LerpUnclamped(0.4f, 1, e), al = Mathf.Clamp01(e);
                if (t > 0.75f) { float p = Mathf.PingPong((t - 0.75f) / 1.3f, 1); p = p * p * (3 - 2 * p); al = Mathf.Lerp(1, 0.6f, p); sy = Mathf.Lerp(1, 0.92f, p); }
                it.ry.localScale = new Vector3(sx, sy, 1); it.cgRy.alpha = al;
                // ★3 は光が虹色にゆらぐ
                if (it.a.s >= 3 && t > 0.75f) it.ry.GetComponent<RawImage>().color = Color.HSVToRGB(Mathf.Repeat((t - 0.75f) / 2.4f, 1), 0.35f, 1);
            }
            // 帯に光が走る（0.5 秒後に 0.8 秒）
            {
                float u = Mathf.Clamp01((t - 0.5f) / 0.8f), e = u * u * (3 - 2 * u);
                it.shine.anchoredPosition = new Vector2(Mathf.Lerp(-0.5f, 1.5f, e) * 570.5f, 0);
            }
            // 枠（0.15 秒後）とアイコン（0.25 秒後）：大きい所から
            Pop(it.fr, it.cgFr, t - 0.15f);
            Pop(it.ic, it.cgIc, t - 0.25f);
            // 字（0.35 秒後に左から）
            {
                float u = Mathf.Clamp01((t - 0.35f) / 0.45f), e = Bz(u, 0.2f, 1.2f, 0.4f, 1);
                it.tx.anchoredPosition = new Vector2(280.5f + Mathf.LerpUnclamped(-30, 0, e), -100.5f);
                it.cgTx.alpha = u;
            }
            // ★（1つずつ、少しずつ高い音で）
            for (int i = 0; i < it.stars.Count; i++)
            {
                var st = it.stars[i];
                float d = 0.75f + i * 0.18f, u = (t - d) / 0.4f;
                if (t >= d + 0.05f && !it.sePlayed[i]) { it.sePlayed[i] = true; Game.SeUi("cur", 0.8f, 1.4f + i * 0.25f); }
                float sc2 = u <= 0 ? 0 : u < 0.7f ? Mathf.Lerp(0, 1.6f, u / 0.7f) : u < 1 ? Mathf.Lerp(1.6f, 1, (u - 0.7f) / 0.3f) : 1;
                float r2 = u <= 0 ? -90 : u < 0.7f ? Mathf.Lerp(-90, 10, u / 0.7f) : u < 1 ? Mathf.Lerp(10, 0, (u - 0.7f) / 0.3f) : 0;
                st.localScale = Vector3.one * sc2;
                st.localRotation = Quaternion.Euler(0, 0, -r2);
            }
            if (t >= it.show) Next();
        }

        static void Pop(RectTransform r, CanvasGroup cg, float t)
        {
            float u = Mathf.Clamp01(t / 0.45f), e = Bz(u, 0.2f, 1.6f, 0.4f, 1);
            r.localScale = Vector3.one * Mathf.LerpUnclamped(2.2f, 1, e);
            cg.alpha = Mathf.Clamp01(e);
            if (t <= 0) { r.localScale = Vector3.one * 2.2f; cg.alpha = 0; }
        }
    }
}
