using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 物の頭の上に出す表示。JS版 buildMarkers / updateMarkers と同じ中身と見た目。
    ///   客   … HP バー（緑 → 橙 → 赤）
    ///   ロボ … 名前と残りの耐久（「1P 100%」の札）・耐久バー・故障の印（⚠左 / ⚠右 / ⚠駆）
    ///   セリフ … 白い吹き出し（枠はロボの色）
    ///   配膳先 … 「▼ Target!」（2人以上なら名前）
    /// </summary>
    public class Overhead
    {
        readonly RectTransform layer;
        readonly Game g;
        readonly Dictionary<Ent, Bar> guestBars = new Dictionary<Ent, Bar>();
        readonly Dictionary<Player, RobotMark> robots = new Dictionary<Player, RobotMark>();
        readonly Dictionary<Game.Dropped, Text> drops = new Dictionary<Game.Dropped, Text>();

        class Bar { public RectTransform r; public Image fill; }
        class RobotMark
        {
            public RectTransform r; public Text nm; public Bar hp; public Text brk;
            public RectTransform pill; public RawImage crown; public Image brkBg, rsBg; public Text rs;
            public RectTransform itp; public RawImage itpIc; public string itpK; public float itpT;
            public RectTransform msg; public Text msgText;
            public RectTransform tgt; public Text tgtText;
            public RectTransform ds; public Image dsFill;      // ダッシュのクールタイムの輪
        }

        static readonly Color GREEN = Mats.Hex(0x4caf50), ORANGE = Mats.Hex(0xffa726), RED = Mats.Hex(0xe53935);
        static readonly Color PILL = new Color(24 / 255f, 28 / 255f, 36 / 255f, 0.82f);

        public Overhead(RectTransform parent, Game game)
        {
            g = game;
            var go = new GameObject("Overhead", typeof(RectTransform));
            layer = go.GetComponent<RectTransform>();
            layer.SetParent(parent, false);
            layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            layer.SetAsFirstSibling();               // 帯やダイアログの下に出す
        }

        public void Clear()
        {
            foreach (var b in guestBars.Values) Object.Destroy(b.r.gameObject);
            foreach (var m in robots.Values)
            {
                Object.Destroy(m.r.gameObject); Object.Destroy(m.msg.gameObject); Object.Destroy(m.tgt.gameObject); Object.Destroy(m.itp.gameObject);
                if (m.ds != null) Object.Destroy(m.ds.gameObject);
            }
            guestBars.Clear(); robots.Clear();
            foreach (var t in drops.Values) Object.Destroy(t.gameObject);
            drops.Clear();
        }

        // ------------------------------------------------------------ 部品
        static Sprite disc, ring;
        /// <summary>丸（ring なら輪）。ダッシュのクールタイムに使う。</summary>
        static Sprite Circle(bool isRing)
        {
            if (isRing ? ring != null : disc != null) return isRing ? ring : disc;
            const int N = 64; const float R = 31.5f, W = 9f;      // 輪の太さは JS版（直径30に線4）と同じ割合
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - N / 2f) * (x + 0.5f - N / 2f) + (y + 0.5f - N / 2f) * (y + 0.5f - N / 2f));
                    float a = Mathf.Clamp01(R - d + 0.5f);
                    if (isRing) a *= Mathf.Clamp01(d - (R - W) + 0.5f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply(false);
            var s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100);
            if (isRing) ring = s; else disc = s;
            return s;
        }
        /// <summary>ダッシュのクールタイムの輪（JS版 .dsG）。暗い丸に白い縁、水色の輪が溜まっていく。中に » 。</summary>
        void MakeDashRing(RobotMark m)
        {
            var go = new GameObject("Dash", typeof(RectTransform));
            m.ds = go.GetComponent<RectTransform>();
            m.ds.SetParent(layer, false);
            m.ds.sizeDelta = new Vector2(26, 26);
            m.ds.pivot = new Vector2(0.5f, 0);
            System.Func<Sprite, Color, float, Image> add = (sp, c, sz) =>
            {
                var o = new GameObject("I", typeof(RectTransform), typeof(Image));
                var r = o.GetComponent<RectTransform>(); r.SetParent(m.ds, false); r.sizeDelta = new Vector2(sz, sz);
                var im = o.GetComponent<Image>(); im.sprite = sp; im.color = c; im.raycastTarget = false;
                return im;
            };
            add(Circle(false), Color.white, 26);                                       // 白い縁
            add(Circle(false), new Color(14 / 255f, 18 / 255f, 26 / 255f, 0.92f), 21);  // 暗い地
            m.dsFill = add(Circle(true), Mats.Hex(0x4fd2ff), 21);
            m.dsFill.type = Image.Type.Filled; m.dsFill.fillMethod = Image.FillMethod.Radial360;
            m.dsFill.fillOrigin = (int)Image.Origin360.Top; m.dsFill.fillClockwise = true;
            var t = Label(m.ds, "»", 13, Color.white);
            t.rectTransform.sizeDelta = new Vector2(26, 26);
            go.SetActive(false);
        }

        static Sprite round;
        /// <summary>角の丸い板（9分割）。CSS の border-radius:999px の代わり。</summary>
        public static Sprite Round
        {
            get
            {
                if (round != null) return round;
                const int N = 32; const float R = 15.5f;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, R, N - R), cy = Mathf.Clamp(y + 0.5f, R, N - R);
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        byte a = (byte)(Mathf.Clamp01(R - d + 0.5f) * 255);
                        px[y * N + x] = new Color32(255, 255, 255, a);
                    }
                tex.SetPixels32(px); tex.Apply(false);
                round = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100, 0,
                                      SpriteMeshType.FullRect, new Vector4(15, 15, 15, 15));
                return round;
            }
        }

        static Image Img(Transform parent, Color c, Vector2 size, bool rounded = true)
        {
            var go = new GameObject("Img", typeof(RectTransform), typeof(Image));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.sizeDelta = size;
            var im = go.GetComponent<Image>();
            im.color = c; im.raycastTarget = false;
            if (rounded) { im.sprite = Round; im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = 4f; }
            return im;
        }

        static Text Label(Transform parent, string s, int size, Color c, FontStyle st = FontStyle.Bold)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Fonts.UI; t.fontSize = size; t.fontStyle = st; t.color = c; t.text = Lang.Tr(s);
            t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>まわりを枠で囲んだバー（外枠 → 地 → 中身）。</summary>
        static Bar MakeBar(Transform parent, Vector2 size, Color bg, Color border)
        {
            var b = new Bar();
            var outer = Img(parent, border, size + new Vector2(2, 2));
            b.r = outer.rectTransform;
            var inner = Img(outer.transform, bg, size);
            var fill = Img(inner.transform, GREEN, size);
            var fr = fill.rectTransform;
            fr.anchorMin = new Vector2(0, 0); fr.anchorMax = new Vector2(1, 1);
            fr.pivot = new Vector2(0, 0.5f); fr.offsetMin = fr.offsetMax = Vector2.zero;
            b.fill = fill;
            return b;
        }

        static void SetFill(Bar b, float k, Color c)
        {
            var r = b.fill.rectTransform;
            k = Mathf.Clamp01(k);
            r.anchorMax = new Vector2(k, 1);
            b.fill.enabled = k > 0.001f;
            b.fill.color = c;
        }

        /// <summary>そこが画面に映っているか（カメラの後ろでないか）と、画面の位置。</summary>
        bool Project(Vector3 w, RectTransform r)
        {
            var sp = g.mainCam.WorldToScreenPoint(w);
            bool vis = sp.z > 0;
            r.gameObject.SetActive(vis);
            if (vis) r.position = sp;
            return vis;
        }

        // ------------------------------------------------------------ 毎フレーム
        public void Tick()
        {
            if (g.mainCam == null) return;
            // ---- 客の HP
            foreach (var gu in g.guests)
            {
                Bar b;
                if (!guestBars.TryGetValue(gu, out b))
                {
                    b = MakeBar(layer, new Vector2(46, 6), Mats.Hex(0xe8e2d4), new Color(0, 0, 0, 0.45f));
                    guestBars[gu] = b;
                }
                if (gu.sunk) { b.r.gameObject.SetActive(false); continue; }
                // 札は頭の少し上。モデルの客は背が当たり判定（1.04m）より高いので、そのぶん上げる
                Project(gu.transform.position + Vector3.up * Mathf.Max(0.62f, gu.modelH - 0.52f + 0.22f), b.r);
                SetFill(b, gu.hp / 100f, gu.hp > 50 ? GREEN : (gu.hp > 0 ? ORANGE : RED));
            }
            // ---- 落ちている料理（誰でも拾えるので、居場所が分からないと取り合いにならない）
            var gone = new List<Game.Dropped>();
            foreach (var kv in drops) if (!g.dropped.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var d in gone) { Object.Destroy(drops[d].gameObject); drops.Remove(d); }
            foreach (var d in g.dropped)
            {
                Text t;
                if (!drops.TryGetValue(d, out t))
                {
                    t = Label(layer, "▼ " + d.dish.n, 13, Mats.Hex(0xff9500));
                    t.gameObject.AddComponent<Outline>().effectColor = Color.black;
                    drops[d] = t;
                }
                if (d.ent == null) continue;
                Project(d.ent.transform.position + Vector3.up * (0.55f + Mathf.Sin(g.t * 7) * 0.05f), t.rectTransform);
            }
            bool many = g.players.Count > 1;
            // 1位の王冠。全員が同じ順位のとき（始まったばかり）は出さない
            Dictionary<Player, int> crownRK = null;
            if (many && g.state != "ready")
            {
                var rk = Ranks();
                foreach (var v in rk.Values) if (v != 1) { crownRK = rk; break; }
            }
            foreach (var P in g.players)
            {
                RobotMark m;
                if (!robots.TryGetValue(P, out m)) robots[P] = m = MakeRobotMark(P, many);
                var rp = P.ent.transform.position;
                // ---- 名前と耐久（客と同じ「残りが減る」向き）
                float left = Mathf.Max(0, 100 - P.botDmg);
                Project(rp + Vector3.up * 0.82f, m.r);
                SetFill(m.hp, left / 100f, P.down ? Mats.Hex(0x5f6672) : left > 50 ? GREEN : (left > 25 ? ORANGE : RED));
                m.nm.text = P.down ? (P.revT > 0 ? P.Label + " " + Lang.Tr("復帰まで {0}", Mathf.CeilToInt(P.revT)) : P.Label + " " + Lang.Tr("リタイア"))
                                   : P.Label + " " + Mathf.RoundToInt(left) + "%";
                FitPill(m.nm);
                // 王冠は名前の左、故障は名前の右に、はみ出して付ける（担当）
                float pw = m.pill.sizeDelta.x;
                int rk1; bool crown = crownRK != null && crownRK.TryGetValue(P, out rk1) && rk1 == 1 && !P.down;
                m.crown.gameObject.SetActive(crown);
                m.crown.rectTransform.anchoredPosition = new Vector2(-pw / 2 - 2 - 10, 9);
                // 故障は対戦で読み合いの材料になるので、名前の右に札で並べる（担当）
                var bad = new List<string>();
                if (P.broken != null) bad.Add(Lang.Tr(P.broken == "left" ? "⚠左" : "⚠右"));
                if (P.brokenDrive) bad.Add(Lang.Tr("⚠駆動"));
                string bk = string.Join(" ", bad.ToArray());
                m.brkBg.gameObject.SetActive(bk != "");
                if (m.brk.text != bk) { m.brk.text = bk; m.brkBg.rectTransform.sizeDelta = new Vector2(m.brk.preferredWidth + 8, 14); }
                m.brkBg.rectTransform.anchoredPosition = new Vector2(pw / 2 + 3, 9);
                // 速達ボーナスのいまの倍率。耐久バーの右に。下がりきったら消す
                {
                    float r = P.carried != null && !P.down ? g.CarriedRush(P) : 1;
                    string rk = r > 1.005f ? Lang.Tr("速達×{0}", r.ToString("0.0")) : "";
                    m.rsBg.gameObject.SetActive(rk != "");
                    if (m.rs.text != rk) { m.rs.text = rk; m.rsBg.rectTransform.sizeDelta = new Vector2(m.rs.preferredWidth + 8, 13); }
                    m.rsBg.color = r >= 1 + g.T.rushBonus * 0.75f ? Mats.Hex(0xff4f9a) : Mats.Hex(0x8a5a78);
                }
                // ---- 持ち替えたアイテムを、頭の上に少しのあいだ出す（白い札に金の枠）
                {
                    bool on = !string.IsNullOrEmpty(P.slot) && g.t < P.itemPopUntil && !P.down && g.state == "play";
                    if (!on) m.itp.gameObject.SetActive(false);
                    else
                    {
                        if (m.itpK != P.slot)
                        {
                            m.itpK = P.slot; m.itpT = 0;
                            var tx = Resources.Load<Texture2D>("Ui/" + P.slot);
                            m.itpIc.texture = tx; m.itpIc.enabled = tx != null;
                            if (tx != null) { float k = Mathf.Min(44f / tx.width, 30f / tx.height); m.itpIc.rectTransform.sizeDelta = new Vector2(tx.width * k, tx.height * k); }
                        }
                        if (!m.itp.gameObject.activeSelf) m.itpT = 0;
                        Project(rp + Vector3.up * 1.45f, m.itp);
                        m.itpT += Time.unscaledDeltaTime;
                        float u = Mathf.Clamp01(m.itpT / 0.22f);
                        m.itp.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1, ComicUi.Bezier(u, 0.2f, 1.6f, 0.4f, 1));
                        var cg = m.itp.GetComponent<CanvasGroup>();
                        cg.alpha = Mathf.Min(1, (P.itemPopUntil - g.t) / 0.25f);
                    }
                }
                // ---- ダッシュのクールタイム（人が操作している機体だけ。使ったあと、次に使えるまで）
                {
                    bool on = P.dashCd > 0 && P.src.kind != "npc" && !P.down && P.cr == null && g.state == "play";
                    if (m.ds == null && on) MakeDashRing(m);
                    if (m.ds != null)
                    {
                        if (!on) m.ds.gameObject.SetActive(false);
                        else if (Project(rp + Vector3.up * 1.0f, m.ds))
                        {
                            m.ds.position += new Vector3(30, 0, 0) * layer.lossyScale.x;   // 頭の上の右寄り
                            m.dsFill.fillAmount = 1 - P.dashCd / Mathf.Max(0.01f, g.T.dashCd);
                        }
                    }
                }
                // ---- セリフ
                if (P.msgT > 0 && !string.IsNullOrEmpty(P.msg))
                {
                    Project(rp + Vector3.up * 1.28f, m.msg);
                    var said = Lang.Tr(P.msg);
                    if (m.msgText.text != said) { m.msgText.text = said; FitBubble(m); }
                }
                else m.msg.gameObject.SetActive(false);
                // ---- 配膳先
                if (P.carried == null) m.tgt.gameObject.SetActive(false);
                else
                {
                    float up = P == g.me ? 1.15f : 1.44f;
                    var p = P.carried.order.guest.transform.position;
                    Project(p + Vector3.up * (up + Mathf.Sin(g.t * 6) * 0.06f), m.tgt);
                }
            }
        }

        Dictionary<Player, int> Ranks() { return g.H2Ranks(); }

        RobotMark MakeRobotMark(Player P, bool many)
        {
            var m = new RobotMark();
            var go = new GameObject("RobotMark_" + P.name, typeof(RectTransform));
            m.r = go.GetComponent<RectTransform>();
            m.r.SetParent(layer, false);
            // 名前の札（丸い札に載せる。影だけだと明るい床の上で読めない）
            var pill = Img(m.r, PILL, new Vector2(60, 16));
            pill.rectTransform.anchoredPosition = new Vector2(0, 9);
            m.nm = Label(pill.transform, P.Label, 12, Mats.Hex(P.col));
            m.hp = MakeBar(m.r, new Vector2(44, 7), Mats.Hex(0x2b313c), new Color(0, 0, 0, 0.55f));
            m.hp.r.anchoredPosition = new Vector2(0, -4);
            m.pill = pill.rectTransform;
            // 王冠（名前の左）
            m.crown = new GameObject("Crown", typeof(RectTransform)).AddComponent<RawImage>();
            m.crown.rectTransform.SetParent(m.r, false);
            m.crown.texture = Resources.Load<Texture2D>("Ui/crown"); m.crown.raycastTarget = false;
            m.crown.rectTransform.sizeDelta = new Vector2(20, 14);
            var cs = m.crown.gameObject.AddComponent<Shadow>(); cs.effectColor = new Color(0, 0, 0, 0.6f); cs.effectDistance = new Vector2(0, -1);
            m.crown.gameObject.SetActive(false);
            // 故障の札（名前の右。赤）
            m.brkBg = Img(m.r, Mats.Hex(0xe53935), new Vector2(30, 14));
            m.brkBg.rectTransform.pivot = new Vector2(0, 0.5f);
            m.brkBg.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.45f);
            m.brk = Label(m.brkBg.transform, "", 10, Color.white);
            m.brkBg.gameObject.SetActive(false);
            // 速達の倍率（耐久バーの右）
            m.rsBg = Img(m.r, Mats.Hex(0x8a5a78), new Vector2(40, 13));
            m.rsBg.rectTransform.pivot = new Vector2(0, 0.5f);
            m.rsBg.rectTransform.anchoredPosition = new Vector2(26, -4);
            m.rs = Label(m.rsBg.transform, "", 10, Color.white);
            m.rsBg.gameObject.SetActive(false);
            // 持ち替えたアイテムの札（頭の上。白い札に金の枠、まわりに黒い細い縁）
            {
                var edge = Img(layer, Mats.Hex(0x1d1d1d), new Vector2(56, 56));
                m.itp = edge.rectTransform; m.itp.pivot = new Vector2(0.5f, 0);
                m.itp.gameObject.AddComponent<CanvasGroup>();
                var gold = Img(edge.transform, Mats.Hex(0xffc400), new Vector2(52, 52));
                var white = Img(gold.transform, Color.white, new Vector2(44, 44));
                m.itpIc = new GameObject("Ic", typeof(RectTransform)).AddComponent<RawImage>();
                m.itpIc.rectTransform.SetParent(white.transform, false); m.itpIc.raycastTarget = false;
                var sh = edge.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.4f); sh.effectDistance = new Vector2(0, -4);
                m.itp.gameObject.SetActive(false);
            }
            // セリフの吹き出し（枠の色でどの機体か分かる）。下の端を頭の上に合わせる
            var border = Img(layer, Mats.Hex(P.col), new Vector2(80, 30));
            m.msg = border.rectTransform;
            m.msg.pivot = new Vector2(0.5f, 0);
            var inner = Img(border.transform, Mats.Hex(0xfffdf6), new Vector2(74, 24));
            m.msgText = Label(inner.transform, "", 14, Mats.Hex(0x2b2f38));
            m.msg.gameObject.SetActive(false);
            // 配膳先の印
            var tg = Img(layer, PILL, new Vector2(96, 22));
            m.tgt = tg.rectTransform;
            m.tgtText = Label(tg.transform, many ? "▼ " + P.Label : "▼ Target!", P == g.me ? 17 : 13,
                              many ? Mats.Hex(P.col) : Mats.Hex(0xffe07a));
            FitPill(m.tgtText);
            m.tgt.gameObject.SetActive(false);
            return m;
        }

        static void FitPill(Text t)
        {
            var r = (RectTransform)t.transform.parent;
            r.sizeDelta = new Vector2(t.preferredWidth + 18, t.preferredHeight + 2);
        }

        static void FitBubble(RobotMark m)
        {
            float w = m.msgText.preferredWidth + 24, h = m.msgText.preferredHeight + 8;
            m.msg.sizeDelta = new Vector2(w + 6, h + 6);
            ((RectTransform)m.msgText.transform.parent).sizeDelta = new Vector2(w, h);
        }
    }
}
