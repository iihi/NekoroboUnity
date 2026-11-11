using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 煽り（エモート）。JS版 emoIn / emoWheel / emoWheelSel / emoShow / emoTick を写したもの（担当「煽り文句システム」）。
    /// L2（キーボードは Q）を押している間は応援、R2（E）は挑発の丸い板が自分のロボの所に出て、
    /// 右スティックで8方向から選び（キーボードは X / C で次・前）、離すと自分の頭の上に出す（2.6 秒）。
    /// 担当の案（11/09）：板の8つの区画のうち、いちばん上は「やめる」（❎）。残り7つに、上の右隣から時計回りに絵文字を並べる。
    /// 開いたときは ❎。右スティックを戻しても、最後に倒した向きのまま。❎ のまま離すと何も出さずに閉じる。
    /// 板の見た目：紺の太い縁（外に白い細い縁）・うっすら丸みのある白地・細い灰色の区切り・選んでいる区画に色を敷いて縁のその所を光らせる・
    /// まん中は小さな色の丸だけ。応援（LT）は緑・挑発（RT）は赤。出るとき・選ぶときの動きは付けない（パッと出す）。
    /// 頭の上は白い真円に絵文字だけ（前はマンガのフキダシ）。**絵文字は絵**（HTML版の assets/ui/emo/<コードポイント>.png）。
    /// 出したあと 1 秒は次を出せない（待ちの間に離したら、明けたところで出す）。
    /// オンラインは、参加者は自分の板を手元で出して、選んだもの（セットと 0〜6 の番号。JS版と同じ）をホストへ送る（p:"emo"）。ホストが全員に配る（出来事 "emo"）。
    /// </summary>
    public partial class Game
    {
        public const float EMO_SHOW = 2.6f, EMO_CD = 1.0f;
        /// <summary>確かめる用：1P が L2（0）・R2（1）を押して、右スティックを debugEmoDir の向き（0 が上の ❎、時計回り）に倒していることにする（-1 で切る）</summary>
        [System.NonSerialized] public int debugEmo = -1, debugEmoDir = -1;
        // 絵文字（JS版 EMOS と同じ並び）。後ろは意味のメモ（画面には出ない）
        static readonly string[][] EMOS =
        {
            new[] { "1f607", "1f44d", "1f605", "1f973", "1f97a", "1f64f", "1f970" },   // 😇天使 👍ナイス 😅やっちゃった 🥳やったー 🥺お願い 🙏ありがとう 🥰大好き
            new[] { "1f923", "1f621", "1f60f", "1f4a9", "1f644", "1f608", "1f62d" },   // 🤣ざまぁ 😡ふざけんな 😏どうだ 💩クソッ 🙄うんざり 😈覚悟しろ 😭ひどいよ
        };
        const string EMO_CANCEL = "274e";        // ❎
        static readonly Color EMO_GOOD = Mats.Hex(0x2fbf5b), EMO_GOOD_S = Mats.Hex(0xd2f4dc), EMO_BAD = Mats.Hex(0xff5a5a), EMO_BAD_S = Mats.Hex(0xffdcd8);

        class EmoWheel
        {
            public RectTransform r; public int s, dir; public bool nx, pv;
            public UiFan hl, rim; public RawImage[] it = new RawImage[8];
        }
        class EmoBubble { public RectTransform r, body; public float t; }
        readonly Dictionary<Player, EmoWheel> emoW = new Dictionary<Player, EmoWheel>();
        readonly Dictionary<Player, EmoBubble> emoB = new Dictionary<Player, EmoBubble>();
        RectTransform emoLayer, emoTop;

        static Texture2D EmoTex(string cp)
        {
            var t = DataRoot.Texture("ui/emo/" + cp + ".png");
            if (t != null) t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        void EmoLayers()
        {
            if (emoLayer != null) return;
            // 頭の上の絵文字は上の帯より奥（JS版 z-index 7）、板は帯より手前（30）
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
            if (PauseOn || pauseBlock) { e.L = e.R = false; }               // ポーズメニューの間は開かない
            return e;
        }

        // ---------------------------------------------------------------- 板
        static Sprite emoDisc;
        /// <summary>丸（256px。板は 160px あるので、小さい丸を引き伸ばすと縁がぼやける）</summary>
        static Sprite EmoDisc()
        {
            if (emoDisc != null) return emoDisc;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - N / 2f) * (x + 0.5f - N / 2f) + (y + 0.5f - N / 2f) * (y + 0.5f - N / 2f));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(N / 2f - d + 0.5f) * 255));
                }
            tex.SetPixels32(px); tex.Apply(false);
            emoDisc = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100);
            return emoDisc;
        }
        static Sprite emoGrad;
        /// <summary>うっすら丸みのある白地（JS版 radialGradient：まん中やや上が白、縁へ #f4f6fa → #dde3ec）</summary>
        static Sprite EmoGrad()
        {
            if (emoGrad != null) return emoGrad;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            Color c0 = Color.white, c1 = Mats.Hex(0xf4f6fa), c2 = Mats.Hex(0xdde3ec);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = (x + 0.5f) / N, fy = 1 - (y + 0.5f) / N;           // fy は上から
                    float edge = Mathf.Clamp01(N / 2f - Mathf.Sqrt((x + 0.5f - N / 2f) * (x + 0.5f - N / 2f) + (y + 0.5f - N / 2f) * (y + 0.5f - N / 2f)) + 0.5f);
                    float g = Mathf.Sqrt((fx - 0.5f) * (fx - 0.5f) + (fy - 0.38f) * (fy - 0.38f)) / 0.62f;
                    var c = g < 0.7f ? Color.Lerp(c0, c1, g / 0.7f) : Color.Lerp(c1, c2, Mathf.Clamp01((g - 0.7f) / 0.3f));
                    c.a = edge;
                    px[y * N + x] = c;
                }
            tex.SetPixels32(px); tex.Apply(false);
            emoGrad = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100);
            return emoGrad;
        }

        // 扇（中心から a0〜a1。上から時計回りの角度[rad]、半径 r）。UiFan の点は y 下向きで渡す
        static List<Vector2> EmoSector(float a0, float a1, float r)
        {
            var pts = new List<Vector2>();
            const int n = 10;
            for (int k = 0; k <= n; k++) { float a = Mathf.Lerp(a0, a1, k / (float)n); pts.Add(new Vector2(Mathf.Sin(a) * r, -Mathf.Cos(a) * r)); }
            return pts;
        }

        EmoWheel EmoWheelMake(Player P, int set)
        {
            EmoLayers();
            var w = new EmoWheel { s = set, dir = 0 };
            bool bad = set == 1;
            var c = bad ? EMO_BAD : EMO_GOOD; var cs = bad ? EMO_BAD_S : EMO_GOOD_S;
            w.r = UiKit.Rect(emoTop, "EmoWheel");
            w.r.sizeDelta = new Vector2(160, 160);
            System.Func<float, Color, Vector2, Image> disk = (d, col, off) =>
            {
                var im = UiKit.Img(w.r, col, 0, "Disk");
                im.sprite = EmoDisc(); im.type = Image.Type.Simple;
                var rr = im.rectTransform; rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f); rr.sizeDelta = new Vector2(d, d); rr.anchoredPosition = off;
                return im;
            };
            // 影（drop-shadow 0 5px 0 .28 と 0 8px 12px .4 を、ずらした暗い丸2つで）
            disk(166, new Color(0, 0, 0, 0.22f), new Vector2(0, -8));
            disk(158, new Color(0, 0, 0, 0.28f), new Vector2(0, -5));
            disk(160, Color.white, Vector2.zero);                         // 外の白い細い縁
            disk(156, Mats.Hex(0x1d2433), Vector2.zero);                  // 紺の太い縁
            // 縁の光（選んでいる区画の所。白地の下に敷くと、縁の幅だけ見える）
            w.rim = ComicUi.Fan(w.r, "Rim", new List<Vector2> { Vector2.zero, Vector2.up, Vector2.right }, c, Vector2.zero);
            var bg = disk(144, Color.white, Vector2.zero); bg.sprite = EmoGrad();   // 白地（r72）
            // 選んでいる区画に色を敷く
            w.hl = ComicUi.Fan(w.r, "Hl", new List<Vector2> { Vector2.zero, Vector2.up, Vector2.right }, cs, Vector2.zero);
            // 区切り：r12 から r70 へ、22.5° から 45° ごと（細い灰色の線）
            for (int k = 0; k < 8; k++)
            {
                float a = (k * 45 + 22.5f) * Mathf.Deg2Rad;
                var ln = UiKit.Img(w.r, Mats.Hex(0xc5cdd8), 0, "Sp");
                var lr = ln.rectTransform; lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f); lr.pivot = new Vector2(0.5f, 0);
                lr.sizeDelta = new Vector2(1.6f, 58); lr.anchoredPosition = new Vector2(Mathf.Sin(a) * 12, Mathf.Cos(a) * 12);
                lr.localRotation = Quaternion.Euler(0, 0, -k * 45 - 22.5f);
            }
            // まん中の小さな色の丸（白い縁）
            disk(21, Color.white, Vector2.zero);
            disk(16, c, Vector2.zero);
            // 絵文字（k=0 が上の ❎、そこから時計回りに7つ）
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4;
                var im = new GameObject("It", typeof(RectTransform)).AddComponent<RawImage>();
                im.rectTransform.SetParent(w.r, false);
                im.texture = EmoTex(k == 0 ? EMO_CANCEL : EMOS[set][k - 1]); im.raycastTarget = false;
                im.rectTransform.sizeDelta = new Vector2(28, 28);
                im.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(a) * 47, Mathf.Cos(a) * 47);
                var ds = im.gameObject.AddComponent<Shadow>(); ds.effectColor = new Color(0, 0, 0, 0.25f); ds.effectDistance = new Vector2(0, -1);
                w.it[k] = im;
            }
            EmoWheelSel(w);
            return w;
        }

        void EmoWheelSel(EmoWheel w)
        {
            // 選んでいる区画（dir の向きを真ん中に 45°）に色を敷き、縁のその所を色で光らせる
            float a0 = (w.dir * 45 - 22.5f) * Mathf.Deg2Rad, a1 = a0 + Mathf.PI / 4;
            var hl = new List<Vector2>(); foreach (var v in EmoSector(a0, a1, 72)) hl.Add(new Vector2(v.x, -v.y));
            w.hl.pts = hl; w.hl.SetVerticesDirty();
            var rim = new List<Vector2>(); foreach (var v in EmoSector(a0 + 0.06f, a1 - 0.06f, 78)) rim.Add(new Vector2(v.x, -v.y));
            w.rim.pts = rim; w.rim.SetVerticesDirty();
            for (int k = 0; k < 8; k++) w.it[k].rectTransform.localScale = Vector3.one * (k == w.dir && w.dir > 0 ? 1.3f : 1);
        }

        // ---------------------------------------------------------------- 出す
        /// <summary>出す。参加者はホストへ送り、ホストが配る（自分の画面にも、配られてから出る）。i は EMOS の 0〜6</summary>
        void EmoSend(Player P, int set, int i)
        {
            P.emoCd = EMO_CD;
            if (NetGuest) Net.Send(new JObject { ["p"] = "emo", ["s"] = set, ["i"] = i });
            else EmoFire(P, set, i);
        }
        public void EmoFire(Player P, int set, int i)
        {
            if (set < 0 || set > 1 || i < 0 || i >= EMOS[set].Length) return;
            NetEv("emo", P.idx, set, i);
            EmoShow(P, set, i);
        }

        /// <summary>頭の上の絵文字。下地は白い真円だけ（52px）。絵文字の絵は余白があるので、丸より大きく（56px）置く</summary>
        public void EmoShow(Player P, int set, int i)
        {
            if (set < 0 || set > 1 || i < 0 || i >= EMOS[set].Length) return;
            EmoLayers();
            EmoBubble old;
            if (emoB.TryGetValue(P, out old) && old.r != null) Object.Destroy(old.r.gameObject);
            var b = new EmoBubble { t = EMO_SHOW };
            b.r = UiKit.Rect(emoLayer, "EmoB");
            b.r.sizeDelta = new Vector2(52, 52); b.r.pivot = new Vector2(0.5f, 0);
            b.body = UiKit.Rect(b.r, "Body"); UiKit.Stretch(b.body);
            b.r.gameObject.AddComponent<CanvasGroup>();
            var disc = UiKit.Img(b.body, Color.white, 0, "Disc"); disc.sprite = EmoDisc(); disc.type = Image.Type.Simple;
            UiKit.Stretch(disc.rectTransform);
            var im = new GameObject("E", typeof(RectTransform)).AddComponent<RawImage>();
            im.rectTransform.SetParent(b.body, false); im.raycastTarget = false;
            im.texture = EmoTex(EMOS[set][i]);
            im.rectTransform.sizeDelta = new Vector2(56, 56);
            // 出したフレームから頭の上に（位置を入れる前に描かれると、1コマだけ画面のまん中に出る）
            b.body.localScale = Vector3.zero;
            if (P.ent != null && mainCam != null) b.r.position = mainCam.WorldToScreenPoint(P.ent.transform.position + Vector3.up * 1.28f);
            emoB[P] = b;
            Game.SeUi("cur", 0.8f, set == 1 ? 0.8f : 1.5f);
        }

        // ---------------------------------------------------------------- 毎フレーム
        void EmoTick(float dt)
        {
            if (players == null || mainCam == null) return;
            bool play = state == "play" && !hud.MenuOpen && !TalkOn && !PauseOn;
            float sf = hud.Root != null ? hud.Root.lossyScale.y : 1;
            foreach (var P in players)
            {
                if (P.ent == null) continue;
                if (P.emoCd > 0) P.emoCd -= dt;
                if (P.emoQs >= 0 && !(P.emoCd > 0)) { int qs = P.emoQs; P.emoQs = -1; if (play) EmoSend(P, qs, P.emoQi); }
                var rp = P.ent.transform.position;
                // 頭の上の絵文字（セリフと同じ高さ。かぶせて出す）
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
                        float u = Mathf.Clamp01((EMO_SHOW - b.t) / 0.38f);
                        b.body.localScale = Vector3.one * Mathf.Max(0, ComicUi.Bezier(u, 0.2f, 1.8f, 0.4f, 1));
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
                    Game.SeUi("cur", 0.5f);
                }
                P.emoOpen = true;
                bool held = w.s == 1 ? inp.R : inp.L;
                // キーボード：X で次（時計回り）、C で前。上の ❎ も1つの区画として回る
                int kStep = (inp.nx && !w.nx ? 1 : 0) - (inp.pv && !w.pv ? 1 : 0);
                w.nx = inp.nx; w.pv = inp.pv;
                if (kStep != 0) { w.dir = (w.dir + kStep + 8) % 8; EmoWheelSel(w); Game.SeUi("cur", 0.6f, 1.2f); }
                // 右スティック：倒した向き。戻しても最後に倒した向きのまま（担当）
                if (Mathf.Sqrt(inp.x * inp.x + inp.y * inp.y) > 0.5f)
                {
                    int d = (Mathf.RoundToInt(Mathf.Atan2(inp.x, -inp.y) / (Mathf.PI / 4)) + 8) % 8;
                    if (d != w.dir) { w.dir = d; EmoWheelSel(w); Game.SeUi("cur", 0.6f, 1.2f); }
                }
                if (!held || !play)
                {
                    Object.Destroy(w.r.gameObject); emoW.Remove(P); P.emoOpen = false;
                    // ❎（dir 0）のまま離したら何も出さない。絵文字は区画 1〜7 ＝ EMOS の 0〜6
                    if (!held && play && w.dir > 0)
                    {
                        if (P.emoCd > 0) { P.emoQs = w.s; P.emoQi = w.dir - 1; }   // 待ちの間なら、明けたら出す
                        else { P.emoQs = -1; EmoSend(P, w.s, w.dir - 1); }
                    }
                    continue;
                }
                // ロボの上（名前・耐久の札より上）に出す。上の帯にかかるときはロボの下へ
                var up = mainCam.WorldToScreenPoint(rp + Vector3.up * 1.25f);
                bool below = (Screen.height - up.y) / Mathf.Max(0.01f, sf) - 166 < 135;
                var at = below ? mainCam.WorldToScreenPoint(rp + Vector3.down * 0.45f) : up;
                w.r.pivot = new Vector2(0.5f, below ? 1 : 0);
                w.r.position = at;
            }
        }
    }
}
