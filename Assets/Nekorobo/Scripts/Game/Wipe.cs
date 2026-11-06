using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 画面転換（JS版 screenWipe。担当が選んだ「先頭に色」）。左下から右上へ、黄色のドットが先に走り、
    /// 黒いドットが大きくなりながら追いかけて画面を覆う。真っ黒になったら fn で画面を切り替えて、
    /// 同じ向きに黒いドットが小さくなりながら抜け、次の画面が見える。
    /// 小さめの絵（480×270）に1コマずつ描いて、画面いっぱいに引き伸ばす（ドットは少しぼける）
    /// </summary>
    public class Wipe
    {
        const float ANGLE = 35, SPACING = 46, SIZE = 0.8f, GROW = 0.6f, BAND = 0.42f;
        static readonly Color32 ACC = new Color32(255, 212, 0, 255), BLACK = new Color32(0, 0, 0, 255), CLEAR = new Color32(0, 0, 0, 0);
        const int RW = 480, RH = 270;
        readonly RawImage img;
        readonly Texture2D tex;
        readonly Color32[] px = new Color32[RW * RH];
        System.Action fn;
        float t; int phase;
        public bool Busy { get; private set; }
        readonly Game g;

        public Wipe(RectTransform parent, Game game)
        {
            g = game;
            var r = UiKit.Rect(parent, "Wipe");
            UiKit.Stretch(r);
            img = r.gameObject.AddComponent<RawImage>();
            tex = new Texture2D(RW, RH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            img.texture = tex; img.raycastTarget = true;
            r.gameObject.SetActive(false);
        }

        /// <summary>覆ってから fn を呼び、抜ける。切っているとき・走っている途中はすぐ切り替える</summary>
        public void Run(System.Action f)
        {
            if (!g.T.wipe || Busy) { f(); return; }
            fn = f; t = 0; phase = 0; Busy = true;
            img.transform.SetAsLastSibling();
            img.gameObject.SetActive(true);
            Draw();
        }

        public void Tick(float dt)
        {
            if (!Busy) return;
            img.transform.SetAsLastSibling();
            t += dt / Mathf.Max(0.05f, g.T.wipeDur);
            if (phase == 0 && t >= 1)
            {
                Draw(1);
                phase = 1; t = 0;
                try { fn(); } catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (phase == 1 && t >= 1) { Busy = false; img.gameObject.SetActive(false); return; }
            Draw();
        }

        void Draw(float force = -1)
        {
            float tt = force >= 0 ? force : Mathf.Clamp01(t);
            var rr = img.rectTransform.rect;
            float W = rr.width > 0 ? rr.width : 1280, H = rr.height > 0 ? rr.height : 720;
            float a = ANGLE * Mathf.Deg2Rad, dx = Mathf.Cos(a), dy = -Mathf.Sin(a);      // 画面の y は下向き
            float lo = Mathf.Min(Mathf.Min(0, W * dx), Mathf.Min(H * dy, W * dx + H * dy)), hi = Mathf.Max(Mathf.Max(0, W * dx), Mathf.Max(H * dy, W * dx + H * dy));
            float sp = SPACING * Mathf.Sqrt(W * W + H * H) / Mathf.Sqrt(1280 * 1280 + 720 * 720), bw = BAND;
            float c; bool behind;
            if (phase == 0) { c = -bw / 2 + tt * tt * (1 + bw / 2); behind = true; }
            else { c = (1 - (1 - tt) * (1 - tt)) * (1 + bw / 2); behind = false; }
            float sx = W / RW, sy = H / RH;
            for (int py = 0; py < RH; py++)
            {
                float y = (RH - 1 - py + 0.5f) * sy;                // 絵の下の行が画面の下
                for (int qx = 0; qx < RW; qx++)
                {
                    float x = (qx + 0.5f) * sx;
                    float s = (x * dx + y * dy - lo) / (hi - lo);
                    Color32 col = CLEAR;
                    bool dark = behind ? s <= c : s >= c;
                    if (dark) col = BLACK;
                    else
                    {
                        // まわりのドット（格子の近い4つ）
                        int gx = Mathf.FloorToInt((x + sp) / sp), gy = Mathf.FloorToInt((y + sp) / sp);
                        for (int ox = -1; ox <= 1 && col.a == 0; ox++)
                            for (int oy = -1; oy <= 1; oy++)
                            {
                                float cx = (gx + ox) * sp - sp, cy = (gy + oy) * sp - sp;
                                float ds = (cx * dx + cy * dy - lo) / (hi - lo);
                                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                                // 黒のドット：覆うときは前半分で大きくなる、抜けるときは後ろ半分で小さくなる
                                float r = DotR(c, ds, sp, bw, 1, phase == 0 ? 0 : 0.5f, phase == 0 ? 0.5f : 1);
                                if (r > 0.5f && d < r) { col = BLACK; break; }
                                // 先に走る黄色（覆うときだけ）
                                if (phase == 0)
                                {
                                    float ry = DotR(c + bw * 0.22f, ds, sp, bw, 0.85f, 0, 1);
                                    if (ry > 0.5f && d < ry) { col = ACC; }
                                }
                            }
                    }
                    px[py * RW + qx] = col;
                }
            }
            tex.SetPixels32(px); tex.Apply(false);
        }

        static float DotR(float c, float s, float sp, float bw, float k, float from, float to)
        {
            float u = (c - s) / bw + 0.5f;
            if (u <= from || u >= to) return 0;
            float peak = sp * SIZE * (1 + GROW * s) * k;
            return u < 0.5f ? peak * Mathf.Pow(Mathf.Min(1, u / 0.5f), 2) : peak * (1 - Mathf.Pow((u - 0.5f) / 0.5f, 2));
        }
    }
}
