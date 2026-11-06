using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    // ================================================================ マンガの効果（ぶつかった・爆発）
    // 担当から「エフェクトはもっと派手に。ぶつかった時とか、ミサイルの爆発もコミック寄りに」（JS版と同じ作り）。
    //   バースト … ギザギザの星形。カメラを向く板で、ぱっと広がって消える
    //   描き文字 … 「ドカッ！」「ガシャーン！」など。太い縁取りの字を少し傾けて（画面の上に重ねる）
    //   衝撃の輪 … 爆発のとき、地面を這って広がる輪
    // 見た目だけなので乱数は Random.value。衝突の分はホストが "hx" で配り、爆発の分は "bl"（ExplodeFx）で参加者も出す
    public partial class Game
    {
        static readonly Dictionary<string, Texture2D> burstTex = new Dictionary<string, Texture2D>();
        static Texture2D BurstTex(string kind, int v)
        {
            string key = kind + v;
            Texture2D t;
            if (burstTex.TryGetValue(key, out t) && t != null) return t;
            const int S = 256;
            int p0, p1, p2;
            switch (kind)
            {
                case "boom": p0 = 0xfff3a0; p1 = 0xff7a1a; p2 = 0x3a0d00; break;
                case "zap": p0 = 0xffffff; p1 = 0x7fe8ff; p2 = 0x063a5a; break;
                case "dmg": p0 = 0xffffff; p1 = 0xff5050; p2 = 0x3a0000; break;
                default: p0 = 0xfff7c2; p1 = 0xffd400; p2 = 0x2a1600; break;
            }
            int n = kind == "boom" ? 14 : 11;
            System.Func<float, float, float, Vector2[]> star = (r0, r1, jit) =>
            {
                var pts = new Vector2[n * 2];
                for (int i = 0; i < n * 2; i++)
                {
                    float a = (i / (float)(n * 2)) * Mathf.PI * 2 + (Random.value - 0.5f) * 0.12f;
                    float r = i % 2 == 1 ? r0 * (1 - Random.value * jit) : r1 * (1 - Random.value * jit);
                    pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                }
                return pts;
            };
            var outer = star(70, 122, 0.22f); var inner = star(44, 80, 0.25f);
            var px = new Color32[S * S];
            Color c0 = Mats.Hex(p0), c1 = Mats.Hex(p1), c2 = Mats.Hex(p2);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2(x - S / 2 + 0.5f, y - S / 2 + 0.5f);
                    if (p.magnitude > 128) { px[y * S + x] = new Color32(0, 0, 0, 0); continue; }
                    Color c = new Color(0, 0, 0, 0);
                    bool inO = Inside(outer, p);
                    float dOut = EdgeDist(outer, p);
                    if (inO || dOut < 4.5f)
                    {
                        c = inO && dOut > 4.5f ? c1 : c2;
                        if (Inside(inner, p)) c = c0;
                        if (!inO) c.a = Mathf.Clamp01(5f - dOut);
                    }
                    px[y * S + x] = c;
                }
            t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px); t.Apply(false);
            burstTex[key] = t;
            return t;
        }
        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)) c = !c;
            return c;
        }
        static float EdgeDist(Vector2[] poly, Vector2 p)
        {
            float best = 1e9f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[j]; var b = poly[i]; var ab = b - a;
                float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * u - p).magnitude);
            }
            return best;
        }

        class Burst { public Transform m; public Material mat; public float t, life, size, spin, rot; public bool ring; }
        readonly List<Burst> bursts = new List<Burst>();

        /// <summary>バースト。size … 直径[m]、life … 秒</summary>
        public void SpawnBurst(Vector3 at, string kind, float size, float life)
        {
            var mat = Mats.BasicTex(BurstTex(kind, Random.Range(0, 3)), true, true);
            if (mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            mat.renderQueue = 3900;
            var m = Part.Add(fxRoot, MeshGen.Plane(1, 1), mat, at, false, "Burst").transform;
            m.localScale = Vector3.one * size * 0.2f;
            bursts.Add(new Burst { m = m, mat = mat, life = life, size = size, spin = (Random.value - 0.5f) * 2, rot = Random.value * 360 });
        }
        /// <summary>地面を這って広がる輪（爆発）</summary>
        public void SpawnShock(Vector3 at, float size, int col)
        {
            var c = Mats.Hex(col); c.a = 0.9f;
            var mat = Mats.NewBasic(c, true, true, false);
            var m = Part.Add(fxRoot, MeshGen.Ring(0.8f, 1, 40), mat, new Vector3(at.x, 0.06f, at.z), false, "Shock").transform;
            m.localScale = Vector3.one * 0.2f;
            bursts.Add(new Burst { m = m, mat = mat, life = 0.45f, size = size, ring = true });
        }

        void UpdateBursts(float dt)
        {
            var cam = mainCam;
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var b = bursts[i]; b.t += dt;
                float k = b.t / b.life;
                if (b.m == null) { bursts.RemoveAt(i); continue; }
                if (b.ring)
                {
                    b.m.localScale = Vector3.one * (0.2f + b.size * Mathf.Sqrt(Mathf.Min(1, k)));
                    var c = b.mat.GetColor("_BaseColor"); c.a = 0.9f * (1 - k); b.mat.SetColor("_BaseColor", c);
                }
                else
                {
                    // 一瞬で大きく（少し行き過ぎて戻る）→ そのまま薄くなる
                    float s = k < 0.18f ? (k / 0.18f) * 1.15f : 1.15f - 0.15f * Mathf.Min(1, (k - 0.18f) / 0.2f);
                    b.rot += b.spin * dt * Mathf.Rad2Deg;
                    // 板（Plane は上向き）をカメラへ向ける
                    if (cam != null) b.m.rotation = Quaternion.LookRotation(cam.transform.up, -cam.transform.forward) * Quaternion.Euler(0, b.rot, 0);
                    b.m.localScale = Vector3.one * b.size * Mathf.Max(0.2f, s);
                    var c = b.mat.GetColor("_BaseColor"); c.a = k < 0.45f ? 1 : Mathf.Max(0, 1 - (k - 0.45f) / 0.55f); b.mat.SetColor("_BaseColor", c);
                }
                if (k >= 1) { Destroy(b.m.gameObject); Destroy(b.mat); bursts.RemoveAt(i); }
            }
        }

        // ぶつかったときの効果。kind は効果音と同じ（clash ロボどうし／guest 客／crash 家具／bump 壁など）。k … 強さ 0〜1
        static readonly Dictionary<string, string[]> HIT_WORDS = new Dictionary<string, string[]>
        {
            { "inv", new[] { "バコーン！", "ドッカーン！" } }, { "clash", new[] { "ガツン！", "ドーン！" } },
            { "guest", new[] { "ドカッ！", "バキッ！", "ボコッ！" } }, { "crash", new[] { "ガシャーン！", "ガシャン！", "バリーン！" } },
            { "bump", new[] { "ゴン！", "ドスッ！" } },
        };
        static readonly Dictionary<string, int> HIT_COLS = new Dictionary<string, int>
        { { "inv", 0xff3bd8 }, { "clash", 0x2f6fff }, { "guest", 0xff3b30 }, { "crash", 0xff8a00 }, { "bump", 0x7a5cff } };

        void HitFxLocal(string kind, Vector3 at, float k)
        {
            SpawnBurst(at, kind == "guest" ? "dmg" : "hit", (kind == "inv" ? 1.4f : 0.7f) + 0.9f * k, 0.34f);
            string[] ws;
            if (HIT_WORDS.TryGetValue(kind, out ws))
                hud.Comic(at + Vector3.up * 0.35f, ws[Random.Range(0, ws.Length)], Mathf.RoundToInt(20 + 18 * k), HIT_COLS[kind]);
        }
        float hitFxT = -9;
        void HitFx(string kind, Vector3 at, float k)
        {
            // 続けて鳴る連鎖で画面が埋まらないよう、間を空ける
            if (t - hitFxT < 0.09f) return;
            hitFxT = t;
            NetEv("hx", kind, R2(at.x), R2(at.y), R2(-at.z), R2(k));
            HitFxLocal(kind, at, k);
        }
    }
}
