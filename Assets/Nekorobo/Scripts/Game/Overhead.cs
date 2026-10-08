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

        class Bar { public RectTransform r; public Image fill; }
        class RobotMark
        {
            public RectTransform r; public Text nm; public Bar hp; public Text brk;
            public RectTransform msg; public Text msgText;
            public RectTransform tgt; public Text tgtText;
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
                Object.Destroy(m.r.gameObject); Object.Destroy(m.msg.gameObject); Object.Destroy(m.tgt.gameObject);
            }
            guestBars.Clear(); robots.Clear();
        }

        // ------------------------------------------------------------ 部品
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
            t.font = Fonts.UI; t.fontSize = size; t.fontStyle = st; t.color = c; t.text = s;
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
                Project(gu.transform.position + Vector3.up * 0.62f, b.r);
                SetFill(b, gu.hp / 100f, gu.hp > 50 ? GREEN : (gu.hp > 0 ? ORANGE : RED));
            }
            bool many = g.players.Count > 1;
            foreach (var P in g.players)
            {
                RobotMark m;
                if (!robots.TryGetValue(P, out m)) robots[P] = m = MakeRobotMark(P, many);
                var rp = P.ent.transform.position;
                // ---- 名前と耐久（客と同じ「残りが減る」向き）
                float left = Mathf.Max(0, 100 - P.botDmg);
                Project(rp + Vector3.up * 0.82f, m.r);
                SetFill(m.hp, left / 100f, P.down ? Mats.Hex(0x5f6672) : left > 50 ? GREEN : (left > 25 ? ORANGE : RED));
                m.nm.text = P.down ? (P.revT > 0 ? P.name + " 復帰まで " + Mathf.CeilToInt(P.revT) : P.name + " リタイア")
                                   : P.name + " " + Mathf.RoundToInt(left) + "%";
                FitPill(m.nm);
                string bad = "";
                if (P.broken != null) bad += P.broken == "left" ? "左" : "右";
                if (P.brokenDrive) bad += "駆";
                m.brk.text = bad.Length > 0 ? "⚠" + bad : "";
                // ---- セリフ
                if (P.msgT > 0 && !string.IsNullOrEmpty(P.msg))
                {
                    Project(rp + Vector3.up * 1.28f, m.msg);
                    if (m.msgText.text != P.msg) { m.msgText.text = P.msg; FitBubble(m); }
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

        RobotMark MakeRobotMark(Player P, bool many)
        {
            var m = new RobotMark();
            var go = new GameObject("RobotMark_" + P.name, typeof(RectTransform));
            m.r = go.GetComponent<RectTransform>();
            m.r.SetParent(layer, false);
            // 名前の札（丸い札に載せる。影だけだと明るい床の上で読めない）
            var pill = Img(m.r, PILL, new Vector2(60, 16));
            pill.rectTransform.anchoredPosition = new Vector2(0, 9);
            m.nm = Label(pill.transform, P.name, 12, Mats.Hex(P.col));
            m.hp = MakeBar(m.r, new Vector2(44, 7), Mats.Hex(0x2b313c), new Color(0, 0, 0, 0.55f));
            m.hp.r.anchoredPosition = new Vector2(0, -4);
            m.brk = Label(m.r, "", 12, Mats.Hex(0xff5252));
            m.brk.rectTransform.anchoredPosition = new Vector2(0, -15);
            m.brk.gameObject.AddComponent<Outline>().effectColor = Color.black;
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
            m.tgtText = Label(tg.transform, many ? "▼ " + P.name : "▼ Target!", P == g.me ? 17 : 13,
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
