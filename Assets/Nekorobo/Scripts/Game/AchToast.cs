using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 実績を取ったときの知らせ（JS版 achToast）。画面の下のまん中に、左にメダル（後ろで光が回り、輪が広がり、火花が散る）、
    /// 右に斜めの札（「実績解除！」・取った数・名前・説明・★）。いくつも取ったら1つずつ順に出す（後ろが待っているときは少し短く）。
    /// 会話が出ている間は、会話の枠の上に出す。
    /// </summary>
    public class AchToast
    {
        const float SHOW = 4.4f, SHOW_Q = 3.2f;
        readonly Game g;
        readonly RectTransform root;
        readonly Queue<AchDef> q = new Queue<AchDef>();
        Item cur;
        static readonly Color GOLD = Mats.Hex(0xffd34a), DARK = Mats.Hex(0x4a3500);

        class Spark { public RectTransform r; public float a, rr, d; }
        class Item
        {
            public AchDef a; public float t, show; public RectTransform r, rays, medal, ring, flash, lb, nm, glow; public CanvasGroup cg, cgNm, cgFade;
            public Graphic glowG, ringG, flashG; public readonly List<Spark> sp = new List<Spark>(); public readonly List<Text> stars = new List<Text>();
            public bool[] sePlayed = new bool[3];
        }

        public AchToast(RectTransform parent, Game game)
        {
            g = game;
            root = UiKit.Rect(parent, "AchToast");
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0); root.pivot = new Vector2(0.5f, 0);
            root.sizeDelta = new Vector2(680, 140);
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

        Item Build(AchDef a, float show)
        {
            var it = new Item { a = a, show = show };
            it.r = UiBoard.Box(root, 0, 0, 680, 140, "Ach_" + a.id);
            it.cg = it.r.gameObject.AddComponent<CanvasGroup>();
            it.r.pivot = new Vector2(0.5f, 0); it.r.anchoredPosition = new Vector2(340, -140);
            // ---- 札（斜め -10°）。外の濃い縁・金の縁・紺の地。光彩は後ろでゆっくり明滅
            {
                var pl = UiBoard.Box(it.r, 0, 0, 680, 140, "Pl");
                var gl = UiPoly.Skew(pl, new Color(GOLD.r, GOLD.g, GOLD.b, 0.3f), 62 - 14, 18 - 14, 612 + 28, 104 + 28, -10);
                it.glowG = gl;
                UiPoly.Skew(pl, DARK, 62 - 7, 18 - 7, 612 + 14, 104 + 14, -10);
                UiPoly.Skew(pl, GOLD, 62 - 4, 18 - 4, 612 + 8, 104 + 8, -10);
                UiPoly.Skew(pl, Mats.Hex(0x1f2a40), 62, 18, 612, 104, -10);
                UiPoly.Skew(pl, Mats.Hex(0x2b3954), 62, 18, 612, 40, -10);
            }
            // ---- メダル
            {
                var md = UiBoard.Box(it.r, 0, 0, 140, 140, "Md");
                // 後ろで回る光（★3 は虹色）
                it.rays = UiKit.Rect(md, "Rays"); it.rays.anchorMin = it.rays.anchorMax = new Vector2(0.5f, 0.5f); it.rays.sizeDelta = new Vector2(208, 208);
                Color[] rc = a.s >= 3 ? new[] { Mats.Hex(0xff5a5a), GOLD, Mats.Hex(0x5adf6a), Mats.Hex(0x4ab8ff), Mats.Hex(0xc46aff) } : new[] { new Color(1, 232 / 255f, 140 / 255f, 0.9f) };
                int n = a.s >= 3 ? 24 : 16; float step = 360f / n, wd = a.s >= 3 ? 7 : 9;
                for (int k = 0; k < n; k++)
                {
                    float a0 = k * step * Mathf.Deg2Rad, a1 = (k * step + wd) * Mathf.Deg2Rad;
                    var pts = new List<Vector2> { Vector2.zero, new Vector2(Mathf.Sin(a0) * 104, -Mathf.Cos(a0) * 104), new Vector2(Mathf.Sin(a1) * 104, -Mathf.Cos(a1) * 104) };
                    var c = rc[k % rc.Length]; c.a *= 0.55f;
                    ComicUi.Fan(it.rays, "R", pts, c, Vector2.zero);
                }
                // 出た瞬間の白い光
                it.flash = Disk(md, 220, new Color(1, 0.97f, 0.85f, 0)); it.flashG = it.flash.GetComponent<Image>();
                // 広がる輪
                {
                    var rg = UiKit.Img(md, new Color(1, 0.95f, 0.72f, 0), 0, "Ring");
                    rg.sprite = UiKit.Ring(1); rg.type = Image.Type.Sliced; rg.pixelsPerUnitMultiplier = 15.5f / 60f;
                    it.ring = rg.rectTransform; it.ring.anchorMin = it.ring.anchorMax = new Vector2(0.5f, 0.5f); it.ring.sizeDelta = new Vector2(120, 120);
                    it.ringG = rg;
                }
                // メダル本体（濃い縁・金・左上のつや・メダルの絵）
                it.medal = UiKit.Rect(md, "Dk"); it.medal.anchorMin = it.medal.anchorMax = new Vector2(0.5f, 0.5f); it.medal.sizeDelta = new Vector2(112, 112);
                var sh = Disk(it.medal, 112, new Color(0, 0, 0, 0.45f)); sh.anchoredPosition = new Vector2(0, -6);
                Disk(it.medal, 112, DARK);
                Disk(it.medal, 102, Mats.Hex(0xd98a00));
                Disk(it.medal, 92, GOLD);
                var hi = Disk(it.medal, 50, new Color(1, 0.96f, 0.77f, 0.75f)); hi.anchoredPosition = new Vector2(-16, 18);
                var ic = new GameObject("Ic", typeof(RectTransform)).AddComponent<RawImage>();
                ic.rectTransform.SetParent(it.medal, false); ic.raycastTarget = false;
                ic.texture = Resources.Load<Texture2D>("Ui/Title/medal"); ic.color = Mats.Hex(0x6a3c00);
                ic.rectTransform.sizeDelta = new Vector2(66, 66);
                // 火花（★が多いほど多く・遠くへ）
                int ns = 10 + a.s * 4;
                for (int k = 0; k < ns; k++)
                {
                    var s = new Spark { a = (k * 360f / ns + Random.value * 14) * Mathf.Deg2Rad, rr = 90 + a.s * 20 + Random.value * 50, d = 0.38f + Random.value * 0.12f };
                    s.r = UiKit.Rect(md, "Sp"); s.r.anchorMin = s.r.anchorMax = new Vector2(0.5f, 0.5f); s.r.sizeDelta = new Vector2(22, 22);
                    var pts = new List<Vector2>();
                    for (int j = 0; j < 8; j++) { float aa = j * Mathf.PI / 4; float rr = j % 2 == 0 ? 11 : 2.2f; pts.Add(new Vector2(Mathf.Sin(aa) * rr, -Mathf.Cos(aa) * rr)); }
                    ComicUi.Fan(s.r, "S", pts, k % 3 == 2 ? Mats.Hex(0xffb627) : Mats.Hex(0xfff1a8), Vector2.zero);
                    s.r.gameObject.SetActive(false);
                    it.sp.Add(s);
                }
            }
            // ---- 字（傾けない）
            {
                var tx = UiBoard.Box(it.r, 176, 25, 474, 90, "Tx");
                var lb = UiBoard.T(tx, "実績解除！", 0, 0, 24, GOLD);
                lb.gameObject.AddComponent<Shadow>().effectColor = DARK;
                // 大きい所から縮むので、字のまん中を中心にする
                it.lb = lb.rectTransform;
                { float w = lb.preferredWidth, h = it.lb.sizeDelta.y; it.lb.sizeDelta = new Vector2(w, h); it.lb.pivot = new Vector2(0.5f, 0.5f); it.lb.anchoredPosition += new Vector2(w / 2, -h / 2); }
                var cn = UiBoard.T(tx, Game.AchCount(), 474, 3, 16, Mats.Hex(0xaebbd2), 1);
                it.cgFade = UiBoard.Box(tx, 0, 0, 474, 90, "Fade").gameObject.AddComponent<CanvasGroup>();
                cn.transform.SetParent(it.cgFade.transform, true);
                var nmBox = UiBoard.Box(tx, 0, 0, 474, 90, "Nm");
                it.nm = nmBox; it.cgNm = nmBox.gameObject.AddComponent<CanvasGroup>();
                var nm = UiBoard.T(nmBox, a.n, 0, 29, 36, Color.white);
                var ns2 = nm.gameObject.AddComponent<Shadow>(); ns2.effectColor = new Color(0, 0, 0, 0.55f); ns2.effectDistance = new Vector2(0, -3);
                if (nm.preferredWidth > 474) nm.fontSize = Mathf.FloorToInt(36 * 474 / nm.preferredWidth);
                var ds = UiBoard.T(it.cgFade.transform as RectTransform, a.d, 0, 90 - 16, 16, Mats.Hex(0xc3cde0));
                if (ds.preferredWidth > 354) ds.fontSize = Mathf.Max(10, Mathf.FloorToInt(16 * 354 / ds.preferredWidth));
                for (int i = 0; i < 3; i++)
                {
                    var st = UiBoard.T(tx, "★", 474 - (2 - i) * 33 - 15, 90 + 4 - 30, 30, i < a.s ? GOLD : new Color(1, 1, 1, 0.18f), 2);
                    if (i < a.s) { var o = st.gameObject.AddComponent<Shadow>(); o.effectColor = DARK; o.effectDistance = new Vector2(0, -2); }
                    it.stars.Add(st);
                }
            }
            return it;
        }

        static RectTransform Disk(RectTransform p, float d, Color c)
        {
            var im = UiKit.Img(p, c, 0, "Disk");
            im.sprite = Overhead.Round; im.type = Image.Type.Simple;
            var r = im.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(d, d); r.anchoredPosition = Vector2.zero;
            return r;
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
            // 全体：下から出て（0.5秒）、最後の 0.45 秒で下へ沈んで消える
            float kin = Bz(t / 0.5f, 0.2f, 1.5f, 0.35f, 1);
            float y = Mathf.LerpUnclamped(-60, 0, kin), sc = Mathf.LerpUnclamped(0.6f, 1, kin), al = Mathf.Clamp01(t / 0.5f * 2);
            float to = t - (it.show - 0.45f);
            if (to > 0) { float k = Mathf.Clamp01(to / 0.45f); k = k * k; y = Mathf.Lerp(0, -50, k); sc = Mathf.Lerp(1, 0.85f, k); al = 1 - k; }
            it.r.anchoredPosition = new Vector2(340, -140 + y);
            it.r.localScale = Vector3.one * sc;
            it.cg.alpha = al;
            // 札の光彩
            if (it.glowG != null) { var c = it.glowG.color; c.a = 0.18f + 0.17f * (0.5f - 0.5f * Mathf.Cos(Mathf.Max(0, t - 0.4f) / 1.4f * Mathf.PI)); it.glowG.color = c; }
            // 光：0.15 秒後に 0.5 秒で広がり、6 秒で1回り
            float rk = Mathf.Clamp01((t - 0.15f) / 0.5f);
            it.rays.localScale = Vector3.one * (1 - Mathf.Pow(1 - rk, 3));
            it.rays.localRotation = Quaternion.Euler(0, 0, -t / 6f * 360);
            // メダル：0→1.25→1 に大きくなりながら、縦の軸で2回まわる
            {
                float u = Mathf.Clamp01((t - 0.05f) / 0.75f), e = Bz(u, 0.25f, 1.3f, 0.4f, 1);
                float scm = u < 0.6f ? Mathf.Lerp(0, 1.25f, u / 0.6f) : Mathf.Lerp(1.25f, 1, (u - 0.6f) / 0.4f);
                float rot = e * 720 * Mathf.Deg2Rad;
                it.medal.localScale = new Vector3(Mathf.Max(0, scm) * Mathf.Cos(rot), Mathf.Max(0, scm), 1);
            }
            // 輪（0.45 秒後に 0.6 秒）と白い光（0.35 秒後に 0.5 秒）
            {
                float u = (t - 0.45f) / 0.6f;
                if (u >= 0 && u <= 1) { it.ring.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.9f, 1 - (1 - u) * (1 - u)); var c = it.ringG.color; c.a = 1 - u; it.ringG.color = c; }
                else { var c = it.ringG.color; c.a = 0; it.ringG.color = c; }
                float f = (t - 0.35f) / 0.5f;
                if (f >= 0 && f <= 1) { it.flash.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.3f, f); var c = it.flashG.color; c.a = 1 - f; it.flashG.color = c; }
                else { var c = it.flashG.color; c.a = 0; it.flashG.color = c; }
            }
            // 火花（0.9 秒で外へ）
            foreach (var sp in it.sp)
            {
                float u = (t - sp.d) / 0.9f;
                bool on = u >= 0 && u <= 1;
                sp.r.gameObject.SetActive(on);
                if (!on) continue;
                float e = 1 - (1 - u) * (1 - u), rr = Mathf.Lerp(48, sp.rr, e);
                sp.r.anchoredPosition = new Vector2(Mathf.Cos(sp.a) * rr, Mathf.Sin(sp.a) * rr);
                sp.r.localScale = Vector3.one * Mathf.Lerp(1.4f, 0.3f, e);
                sp.r.localRotation = Quaternion.Euler(0, 0, -sp.a * Mathf.Rad2Deg);
            }
            // 字：「実績解除！」は 0.3 秒後に大きい所から、名前は 0.4 秒後に左から、説明と数は 0.6 秒後に
            {
                float u = (t - 0.3f) / 0.4f, e = Bz(u, 0.2f, 1.6f, 0.4f, 1);
                it.lb.localScale = Vector3.one * (u <= 0 ? 0 : Mathf.LerpUnclamped(2.2f, 1, e));
                float v = (t - 0.4f) / 0.45f, ev = Bz(v, 0.2f, 1.2f, 0.4f, 1);
                it.nm.anchoredPosition = new Vector2(Mathf.LerpUnclamped(-30, 0, ev), 0);
                it.cgNm.alpha = Mathf.Clamp01(v);
                it.cgFade.alpha = Mathf.Clamp01((t - 0.6f) / 0.4f);
            }
            // ★（1つずつ、少しずつ高い音で）
            for (int i = 0; i < it.stars.Count; i++)
            {
                var st = it.stars[i].rectTransform;
                if (i >= it.a.s) { st.localScale = Vector3.one; continue; }
                float d = 0.75f + i * 0.18f, u = (t - d) / 0.4f;
                if (u >= 0 && !it.sePlayed[i]) { it.sePlayed[i] = true; Game.SeUi("cur", 0.8f, 1.4f + i * 0.25f); }
                float sc2 = u <= 0 ? 0 : u < 0.7f ? Mathf.Lerp(0, 1.6f, u / 0.7f) : u < 1 ? Mathf.Lerp(1.6f, 1, (u - 0.7f) / 0.3f) : 1;
                float r2 = u <= 0 ? -90 : u < 0.7f ? Mathf.Lerp(-90, 10, u / 0.7f) : u < 1 ? Mathf.Lerp(10, 0, (u - 0.7f) / 0.3f) : 0;
                st.localScale = Vector3.one * sc2;
                st.localRotation = Quaternion.Euler(0, 0, -r2);
            }
            if (t >= it.show) Next();
        }
    }
}
