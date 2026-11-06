using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 煽り（エモート）。JS版 emoIn / emoWheel / emoShow / emoTick を写したもの（担当「煽り文句システム」）。
    /// L2（キーボードは Q）を押している間は応援、R2（E）は挑発の丸い板が自分のロボの所に出て、
    /// 右スティックで8方向から選び（キーボードは X / C で次・前）、離すと自分の頭の上にフキダシで出す（2.6 秒）。
    /// 真ん中（倒していない）のまま離すとやめる。並びは上から時計回り。
    /// **絵文字だけで、文字は出さない**（言語対応が要らないように）。絵文字は Chrome で描いた PNG（Resources/Ui/Emo/セット_番号）。
    /// 出したあと 1 秒は次を出せない（待ちの間に離したら、明けたところで出す）。
    /// オンラインは、参加者は自分の板を手元で出して、選んだものをホストへ送る（p:"emo"）。ホストが全員に配る（出来事 "emo"）。
    /// </summary>
    public partial class Game
    {
        public const float EMO_SHOW = 2.6f, EMO_CD = 1.0f;
        /// <summary>確かめる用：1P が L2（0）・R2（1）を押して、右スティックを debugEmoDir の向きに倒していることにする（-1 で切る）</summary>
        [System.NonSerialized] public int debugEmo = -1, debugEmoDir = -1;
        static readonly Color EMO_GOOD = Mats.Hex(0x3fd46a), EMO_BAD = Mats.Hex(0xff4b4b);

        class EmoWheel
        {
            public RectTransform r; public int s, dir = -1; public float lastT; public bool stick, nx, pv;
            public UiFan hl; public RawImage[] it = new RawImage[8]; public CanvasGroup mid;
        }
        class EmoBubble { public RectTransform r, body; public float t; public int s; }
        readonly Dictionary<Player, EmoWheel> emoW = new Dictionary<Player, EmoWheel>();
        readonly Dictionary<Player, EmoBubble> emoB = new Dictionary<Player, EmoBubble>();
        RectTransform emoLayer, emoTop;
        static readonly Texture2D[,] emoTex = new Texture2D[2, 8];

        static Texture2D EmoTex(int s, int i)
        {
            if (emoTex[s, i] == null) emoTex[s, i] = Resources.Load<Texture2D>("Ui/Emo/" + s + "_" + i);
            return emoTex[s, i];
        }

        void EmoLayers()
        {
            if (emoLayer != null) return;
            // フキダシは上の帯より奥（JS版 z-index 7）、板は帯より手前（30）
            emoLayer = UiKit.Rect(hud.Root, "Emo"); UiKit.Stretch(emoLayer); emoLayer.SetSiblingIndex(1);
            emoTop = UiKit.Rect(hud.Root, "EmoTop"); UiKit.Stretch(emoTop);
        }

        /// <summary>面を作り直すとき</summary>
        void EmoClear()
        {
            foreach (var w in emoW.Values) if (w.r != null) Object.Destroy(w.r.gameObject);
            foreach (var b in emoB.Values) if (b.r != null) Object.Destroy(b.r.gameObject);
            emoW.Clear(); emoB.Clear();
        }

        // 押しているか（L＝応援・R＝挑発）と、選ぶ向き（右スティック）、キーボードの次・前（X / C）
        struct EmoIn { public bool L, R, nx, pv, rs; public float x, y; }
        EmoIn EmoInput(Player P)
        {
            var e = new EmoIn();
            BotInput i;
            if (P.src.kind == "pad") i = ReadPadInput(P.src.index);
            else
            {
                i = ReadInput();
                var kb = Keyboard.current;
                if (kb != null) { e.nx = kb.xKey.isPressed; e.pv = kb.cKey.isPressed; }
            }
            e.L = i.emL; e.R = i.emR; e.rs = i.rescue; e.x = i.axRX; e.y = i.axRY;
            if (P.idx == 0 && debugEmo >= 0) { e.L = debugEmo == 0; e.R = debugEmo == 1; if (debugEmoDir >= 0) { float a = debugEmoDir * Mathf.PI / 4; e.x = Mathf.Sin(a); e.y = -Mathf.Cos(a); } }
            return e;
        }

        // ---------------------------------------------------------------- 板
        EmoWheel EmoWheelMake(Player P, int set)
        {
            EmoLayers();
            var w = new EmoWheel { s = set };
            var c = set == 1 ? EMO_BAD : EMO_GOOD;
            w.r = UiKit.Rect(emoTop, "EmoWheel");
            w.r.sizeDelta = new Vector2(136, 136);
            System.Func<float, Color, Image> disk = (d, col) =>
            {
                var im = UiKit.Img(w.r, col, 0, "Disk");
                im.sprite = Overhead.Round; im.type = Image.Type.Simple;
                var rr = im.rectTransform; rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(d, d); rr.anchoredPosition = Vector2.zero;
                return im;
            };
            var sh = disk(150, new Color(0, 0, 0, 0.35f)); sh.rectTransform.anchoredPosition = new Vector2(0, -6);
            disk(162, new Color(c.r, c.g, c.b, 0.25f));                 // 光（box-shadow 0 0 14px）
            disk(148, c);                                               // 色の縁 3px
            disk(142, Color.white);                                     // 白い縁 3px
            disk(136, new Color(14 / 255f, 18 / 255f, 26 / 255f, 0.95f));
            // 区切りの細い線（22.5° から 45° ごと）
            for (int k = 0; k < 8; k++)
            {
                float a = (22.5f + 45 * k) * Mathf.Deg2Rad, da = 0.012f;
                var pts = new List<Vector2> { Vector2.zero, new Vector2(Mathf.Sin(a - da) * 66, -Mathf.Cos(a - da) * 66), new Vector2(Mathf.Sin(a + da) * 66, -Mathf.Cos(a + da) * 66) };
                ComicUi.Fan(w.r, "Sp", pts, new Color(1, 1, 1, 0.22f), Vector2.zero);
            }
            // 選んだ区画の光
            w.hl = ComicUi.Fan(w.r, "Hl", new List<Vector2> { Vector2.zero, Vector2.up, Vector2.right }, new Color(c.r, c.g, c.b, 0.42f), Vector2.zero);
            // 絵文字（上から時計回り）
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4;
                var im = new GameObject("It", typeof(RectTransform)).AddComponent<RawImage>();
                im.rectTransform.SetParent(w.r, false);
                im.texture = EmoTex(set, k); im.raycastTarget = false;
                im.rectTransform.sizeDelta = new Vector2(30, 30);
                im.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(a) * 46, Mathf.Cos(a) * 46);
                var ds = im.gameObject.AddComponent<Shadow>(); ds.effectColor = new Color(0, 0, 0, 0.5f); ds.effectDistance = new Vector2(0, -2);
                w.it[k] = im;
            }
            // 真ん中の「×」（やめる）
            {
                var m = UiKit.Rect(w.r, "Mid"); m.sizeDelta = new Vector2(32, 32);
                w.mid = m.gameObject.AddComponent<CanvasGroup>();
                var b = UiKit.Img(m, c, 0, "B"); b.sprite = Overhead.Round; UiKit.Stretch(b.rectTransform);
                var f = UiKit.Img(m, Mats.Hex(0x1b2230), 0, "F"); f.sprite = Overhead.Round; UiKit.Stretch(f.rectTransform, 3);
                var x = UiKit.Label(m, "×", 20, Color.white, false, TextAnchor.MiddleCenter); x.font = UiBoard.Font; UiKit.Stretch(x.rectTransform);
            }
            EmoWheelSel(w);
            return w;
        }

        void EmoWheelSel(EmoWheel w)
        {
            bool sel = w.dir >= 0;
            w.hl.gameObject.SetActive(sel);
            if (sel)
            {
                // CSS の conic-gradient(from dir*45-22.5°)：上から時計回りに 45° の扇
                var pts = new List<Vector2> { Vector2.zero };
                float a0 = (w.dir * 45 - 22.5f) * Mathf.Deg2Rad;
                for (int k = 0; k <= 8; k++) { float a = a0 + k / 8f * Mathf.PI / 4; pts.Add(new Vector2(Mathf.Sin(a) * 65, -Mathf.Cos(a) * 65)); }
                w.hl.pts = new List<Vector2>(); foreach (var v in pts) w.hl.pts.Add(new Vector2(v.x, -v.y));
                w.hl.SetVerticesDirty();
            }
            for (int k = 0; k < 8; k++) w.it[k].rectTransform.localScale = Vector3.one * (k == w.dir ? 1.35f : 1);
            w.mid.alpha = sel ? 0.35f : 1;
        }

        // ---------------------------------------------------------------- 出す
        /// <summary>出す。参加者はホストへ送り、ホストが配る（自分の画面にも、配られてから出る）</summary>
        void EmoSend(Player P, int set, int i)
        {
            P.emoCd = EMO_CD;
            if (NetGuest) Net.Send(new JObject { ["p"] = "emo", ["s"] = set, ["i"] = i });
            else EmoFire(P, set, i);
        }
        public void EmoFire(Player P, int set, int i)
        {
            if (set < 0 || set > 1 || i < 0 || i > 7) return;
            NetEv("emo", P.idx, set, i);
            EmoShow(P, set, i);
        }

        /// <summary>頭の上のフキダシ。応援は丸いフキダシ、挑発はトゲトゲの爆発型。中は絵文字だけ</summary>
        public void EmoShow(Player P, int set, int i)
        {
            EmoLayers();
            EmoBubble old;
            if (emoB.TryGetValue(P, out old) && old.r != null) Object.Destroy(old.r.gameObject);
            bool bad = set == 1;
            float W = bad ? 70 : 62, H = bad ? 64 : 54;
            var b = new EmoBubble { s = set, t = EMO_SHOW };
            b.r = UiKit.Rect(emoLayer, "EmoB");
            b.r.sizeDelta = new Vector2(W, H); b.r.pivot = new Vector2(0.5f, 0);
            b.body = UiKit.Rect(b.r, "Body"); UiKit.Stretch(b.body);
            b.body.pivot = new Vector2(0.5f, 0);
            b.r.gameObject.AddComponent<CanvasGroup>();
            // 形（100×70 の箱の点を、フキダシの大きさへ引き伸ばす）。点は箱のまん中から、y は下向き
            var pts = new List<Vector2>();
            if (!bad)
            {
                // 楕円（まん中 50,30・半径 48×28）と、下のしっぽ（45,58 → 51,69 → 60,58）
                bool tail = false;
                for (int k = 0; k < 40; k++)
                {
                    float a = k / 40f * Mathf.PI * 2;                 // 0 が右、時計回り（y 下向き）
                    var v = new Vector2(50 + Mathf.Cos(a) * 48, 30 + Mathf.Sin(a) * 28);
                    if (v.y > 50 && v.x > 44 && v.x < 61)
                    {
                        // しっぽの所は抜いて、しっぽの3点に替える
                        if (!tail) { tail = true; pts.Add(new Vector2(60, 58)); pts.Add(new Vector2(51, 69)); pts.Add(new Vector2(45, 58)); }
                        continue;
                    }
                    pts.Add(v);
                }
            }
            else
            {
                const int n = 16;
                for (int k = 0; k < n * 2; k++)
                {
                    float a = (k / (n * 2f)) * Mathf.PI * 2 - Mathf.PI / 2;
                    bool o = k % 2 == 0; float j = o ? 0.9f + Random.value * 0.18f : 0.72f + Random.value * 0.06f;
                    var v = new Vector2(50 + Mathf.Cos(a) * 50 * j, 31 + Mathf.Sin(a) * 31 * j);
                    if (k == n) v = new Vector2(52, 70);              // 下のトゲだけ長く（しっぽ）
                    pts.Add(v);
                }
            }
            var c = new Vector2(50, 35);
            var rel = new List<Vector2>();
            foreach (var v in pts) rel.Add(new Vector2((v.x - c.x) * W / 100f, (v.y - c.y) * H / 70f));
            var col = bad ? EMO_BAD : EMO_GOOD;
            ComicUi.Fan(b.body, "Sh", ComicUi.Grow(rel, 1.5f), new Color(0, 0, 0, 0.3f), new Vector2(0, -6));
            ComicUi.Fan(b.body, "Col", ComicUi.Grow(rel, 1.5f), col, new Vector2(0, -3));
            ComicUi.Fan(b.body, "Edge", ComicUi.Grow(rel, 1.5f), Mats.Hex(0x1a1a1a), Vector2.zero);
            ComicUi.Fan(b.body, "Fill", ComicUi.Grow(rel, -1.5f), bad ? Mats.Hex(0xfffbe0) : Color.white, Vector2.zero);
            // 絵文字は体（しっぽを除いた所）のまん中へ
            var im = new GameObject("E", typeof(RectTransform)).AddComponent<RawImage>();
            im.rectTransform.SetParent(b.body, false); im.raycastTarget = false;
            im.texture = EmoTex(set, i);
            im.rectTransform.sizeDelta = new Vector2(34, 34);
            im.rectTransform.anchoredPosition = new Vector2(0, -((bad ? 31f : 30f) - 35) * H / 70f);
            emoB[P] = b;
            Game.SeUi("cur");
        }

        // ---------------------------------------------------------------- 毎フレーム
        void EmoTick(float dt)
        {
            if (players == null || mainCam == null) return;
            bool play = state == "play" && !hud.MenuOpen && !TalkOn;
            float sf = hud.Root != null ? hud.Root.lossyScale.y : 1;
            foreach (var P in players)
            {
                if (P.ent == null) continue;
                if (P.emoCd > 0) P.emoCd -= dt;
                if (P.emoQs >= 0 && !(P.emoCd > 0)) { int qs = P.emoQs; P.emoQs = -1; if (play) EmoSend(P, qs, P.emoQi); }
                var rp = P.ent.transform.position;
                // 頭の上のフキダシ（セリフと同じ高さ。かぶせて出す）
                EmoBubble b;
                if (emoB.TryGetValue(P, out b))
                {
                    b.t -= dt;
                    if (b.t <= 0 || b.r == null) { if (b.r != null) Object.Destroy(b.r.gameObject); emoB.Remove(P); }
                    else
                    {
                        var sp = mainCam.WorldToScreenPoint(rp + Vector3.up * 1.28f);
                        b.r.gameObject.SetActive(sp.z > 0);
                        b.r.position = sp;
                        float age = EMO_SHOW - b.t;
                        float u = Mathf.Clamp01(age / (b.s == 1 ? 0.45f : 0.38f)), e = ComicUi.Bezier(u, 0.2f, 1.8f, 0.4f, 1);
                        b.body.localScale = Vector3.one * Mathf.Max(0, e);
                        float rot = 0;
                        if (b.s == 1) rot = u < 0.55f ? Mathf.Lerp(-14, 5, u / 0.55f) : u < 0.8f ? Mathf.Lerp(5, -2, (u - 0.55f) / 0.25f) : Mathf.Lerp(-2, 0, (u - 0.8f) / 0.2f);
                        b.body.localRotation = Quaternion.Euler(0, 0, -rot);
                        b.r.GetComponent<CanvasGroup>().alpha = b.t < 0.3f ? b.t / 0.3f : 1;
                    }
                }
                // ---- 板（この画面で遊んでいる人だけ）
                if (P.src.kind != "key" && P.src.kind != "pad") continue;
                var inp = EmoInput(P);
                EmoWheel w; emoW.TryGetValue(P, out w);
                // 救済のクレーン（4つ長押し）を押している間は、板を閉じて出さない。L2・R2 を離すまで開かない
                if (inp.rs || P.cr != null)
                {
                    if (w != null) { Object.Destroy(w.r.gameObject); emoW.Remove(P); }
                    P.emoBlock = true; P.emoOpen = false; continue;
                }
                if (P.emoBlock) { if (inp.L || inp.R) continue; P.emoBlock = false; }
                if (w == null)
                {
                    if (!play) { P.emoOpen = false; continue; }
                    int set = inp.L ? 0 : inp.R ? 1 : -1;
                    if (set < 0) { P.emoOpen = false; continue; }
                    w = EmoWheelMake(P, set);
                    w.nx = inp.nx; w.pv = inp.pv;
                    emoW[P] = w;
                    Game.SeUi("cur");
                }
                P.emoOpen = true;
                bool held = w.s == 1 ? inp.R : inp.L;
                float now = Time.unscaledTime;
                // キーボード：X で次（時計回り）、C で前。真ん中（やめる）も1つの位置として回る
                int kStep = (inp.nx && !w.nx ? 1 : 0) - (inp.pv && !w.pv ? 1 : 0);
                w.nx = inp.nx; w.pv = inp.pv;
                if (kStep != 0) { w.dir = ((w.dir + 1 + kStep + 9) % 9) - 1; w.stick = false; EmoWheelSel(w); Game.SeUi("cur"); }
                if (Mathf.Sqrt(inp.x * inp.x + inp.y * inp.y) > 0.5f)
                {
                    int d = (Mathf.RoundToInt(Mathf.Atan2(inp.x, -inp.y) / (Mathf.PI / 4)) + 8) % 8;
                    if (d != w.dir) { w.dir = d; EmoWheelSel(w); Game.SeUi("cur"); }
                    w.lastT = now; w.stick = true;
                }
                else if (w.stick && w.dir >= 0 && now - w.lastT > 0.12f) { w.dir = -1; EmoWheelSel(w); }   // 真ん中へ戻した
                if (!held || !play)
                {
                    Object.Destroy(w.r.gameObject); emoW.Remove(P); P.emoOpen = false;
                    if (!held && play && w.dir >= 0)
                    {
                        if (P.emoCd > 0) { P.emoQs = w.s; P.emoQi = w.dir; }   // 待ちの間なら、明けたら出す
                        else { P.emoQs = -1; EmoSend(P, w.s, w.dir); }
                    }
                    continue;
                }
                // ロボの上（名前・耐久の札より上）に出す。上の帯にかかるときはロボの下へ
                var up = mainCam.WorldToScreenPoint(rp + Vector3.up * 1.25f);
                bool below = (Screen.height - up.y) / Mathf.Max(0.01f, sf) - 142 < 135;
                var at = below ? mainCam.WorldToScreenPoint(rp + Vector3.down * 0.45f) : up;
                w.r.pivot = new Vector2(0.5f, below ? 1 : 0);
                w.r.position = at;
            }
        }
    }
}
