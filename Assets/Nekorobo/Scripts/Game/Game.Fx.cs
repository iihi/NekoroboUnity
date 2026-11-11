using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 演出と、落ちた料理。JS版の spawnSplash / spawnPuffs / spawnFlash / respawnMark / respawnArrow /
    /// 客の虹（syncMeshes）と、dropDish / takeDropped / reissueOrder / updateDropped を写したもの。
    /// 演出は見た目だけ（当たり判定なし）。
    /// </summary>
    public partial class Game
    {
        // ================================================================ 飛沫
        class Splash { public Transform t; public Material m; public Vector3 v; public float age, life; }
        readonly List<Splash> splash = new List<Splash>();
        static readonly int[] RAINBOW_COLS = { 0xff5a5a, 0xff9c3d, 0xffe049, 0x5ed06a, 0x4fb3f0, 0x7b6fd8, 0xc86fd0 };

        /// <summary>板が飛んで消えるだけの飛沫（料理がこぼれた・着地・池）。速いほど伸びる。</summary>
        public void SpawnSplash(Vector3 pos, int col, int n)
        {
            NetEv("sp", R2(pos.x), R2(pos.y), R2(-pos.z), col, n);
            SpawnSplashLocal(pos, col, n, false);
        }
        // 水滴の形（虹のしぶき。前は長方形だった）
        static Texture2D dropTex;
        static Texture2D DropTex()
        {
            if (dropTex != null) return dropTex;
            const int W = 32, H = 48;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * 2 - 1, v = (y + 0.5f) / H;     // v=0 下、1 上
                    // 下は丸く、上へとがる
                    float r = v < 0.5f ? Mathf.Sqrt(Mathf.Max(0, 1 - Mathf.Pow((0.5f - v) / 0.5f, 2))) : (1 - v) * 2 * 0.95f + 0.05f * (1 - v);
                    bool on = Mathf.Abs(u) <= r;
                    px[y * W + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            dropTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            dropTex.SetPixels32(px); dropTex.Apply(false);
            return dropTex;
        }
        void SpawnSplashLocal(Vector3 pos, int col, int n, bool drop)
        {
            for (int i = 0; i < n && splash.Count < 140; i++)
            {
                Material m;
                if (drop) { m = Mats.BasicTex(DropTex(), true, true); var cc = Mats.Hex(col); m.SetColor("_BaseColor", cc); }
                else m = Mats.NewBasic(Mats.Hex(col), true, true, false);
                var go = Part.Add(fxRoot, MeshGen.Plane(drop ? 0.07f : 0.05f, drop ? 0.1f : 0.09f), m, pos, false, "Splash");
                float a = Random.value * Mathf.PI * 2, sp = 1.0f + Random.value * 2.2f;
                splash.Add(new Splash
                {
                    t = go.transform, m = m, life = 0.45f + Random.value * 0.35f,
                    v = new Vector3(Mathf.Cos(a) * sp, 1.8f + Random.value * 2.2f, Mathf.Sin(a) * sp),
                });
            }
        }

        // ================================================================ モクモク（ミサイル）・閃光（レーザー）
        class Puff { public Transform t; public Material m; public Vector3 v; public float age, life, grow; }
        readonly List<Puff> puffs = new List<Puff>();
        public void SpawnPuffs(Vector3 p)
        {
            for (int i = 0; i < 7; i++)
            {
                float a = (i / 7f) * Mathf.PI * 2 + Random.value * 0.7f, r = 0.15f + Random.value * 0.5f;
                int c = i % 3 == 0 ? 0xfff0d2 : (i % 3 == 1 ? 0xd8cfc2 : 0x9a9188);
                var m = Mats.NewBasic(new Color(Mats.Hex(c).r, Mats.Hex(c).g, Mats.Hex(c).b, 0.95f), true, false, false);
                var go = Part.Add(fxRoot, MeshGen.Sphere(1, 10, 8), m, p + new Vector3(Mathf.Cos(a) * r, 0.1f + Random.value * 0.3f, Mathf.Sin(a) * r), false, "Puff");
                go.transform.localScale = Vector3.one * 0.12f;
                puffs.Add(new Puff
                {
                    t = go.transform, m = m, life = 0.75f + Random.value * 0.5f, grow = 0.55f + Random.value * 0.5f,
                    v = new Vector3(Mathf.Cos(a) * (0.5f + Random.value * 0.9f), 0.9f + Random.value * 0.8f, Mathf.Sin(a) * (0.5f + Random.value * 0.9f)),
                });
            }
        }

        class Flash { public Transform g; public Transform flat, face; public Material m0, m1; public float age; }
        readonly List<Flash> flashes = new List<Flash>();
        public void SpawnFlash(Vector3 p)
        {
            var g = new GameObject("Flash").transform; g.SetParent(fxRoot, false); g.position = p;
            var m0 = Mats.NewBasic(Mats.Hex(0x8fe8ff), true, true, false);
            var m1 = Mats.NewBasic(Color.white, true, true, false);
            var flat = Part.Add(g, MeshGen.Ring(0.25f, 0.42f, 24), m0, Vector3.zero, false).transform;
            flat.localRotation = Part.Euler3(-Mathf.PI / 2, 0, 0);       // 水平
            var face = Part.Add(g, MeshGen.Ring(0.25f, 0.42f, 24), m1, Vector3.zero, false).transform;   // カメラ向き
            flashes.Add(new Flash { g = g, flat = flat, face = face, m0 = m0, m1 = m1 });
        }

        // ================================================================ 戻ってきた印（光の柱）と矢印
        const float BEAM_H = 14, BEAM_R = 0.78f, BEAM_DN = 0.13f, BEAM_LAND = 0.33f, MARK_SEC = 1.45f;
        class Mark { public Transform g, beam; public Material bm; public List<Transform> rings = new List<Transform>(); public List<Material> rm = new List<Material>(); public float age, k; }
        readonly List<Mark> marks = new List<Mark>();
        static Texture2D beamTex;

        static Texture2D BeamTex()
        {
            if (beamTex != null) return beamTex;
            // 下が濃く、上へ行くほど消える（一様だと「筒」に見える）
            beamTex = new Texture2D(4, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[4 * 128];
            for (int y = 0; y < 128; y++)
            {
                float t = 1 - y / 127f;      // テクスチャの下（y=0）が柱の下
                float a = t < 0.30f ? t / 0.30f * 0.16f : t < 0.68f ? 0.16f + (t - 0.30f) / 0.38f * 0.46f : 0.62f + (t - 0.68f) / 0.32f * 0.38f;
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1, 1, 1, Mathf.Clamp01(a));
            }
            beamTex.SetPixels(px); beamTex.Apply(false);
            return beamTex;
        }

        /// <summary>画面の上から光の柱が下りてきて、その中に戻る（JS版 respawnMark）。soft は客用の控えめな柱。</summary>
        public void RespawnMark(Vector3 at, int col, bool soft = false)
        {
            NetEv("rs", R2(at.x), R2(-at.z), col, soft ? 1 : 0);
            var g = new GameObject("RespawnMark").transform; g.SetParent(fxRoot, false); g.position = new Vector3(at.x, 0, at.z);
            float k = soft ? 0.45f : 1, R = BEAM_R * (soft ? 0.62f : 1);
            var c = Mats.Hex(col);
            var bm = Mats.BasicTex(BeamTex(), true, true);
            bm.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.55f * k));
            var beam = Part.Add(g, MeshGen.Cylinder(R, R, BEAM_H, 24, true), bm, Vector3.zero, false, "Beam").transform;
            var M = new Mark { g = g, beam = beam, bm = bm, k = k };
            for (int i = 0; i < 3; i++)
            {
                var m = Mats.NewBasic(c, true, false, false);
                var r = Part.Add(g, MeshGen.Torus(R * 1.12f, 0.022f, 6, 44), m, Vector3.zero, false, "Ring").transform;
                r.localRotation = Part.Euler3(-Mathf.PI / 2, 0, 0);
                r.gameObject.SetActive(false);
                M.rings.Add(r); M.rm.Add(m);
            }
            marks.Add(M);
        }

        class Arrow { public Transform g; public Material cone, edge; public Ent ent; public float age; }
        readonly List<Arrow> arrows = new List<Arrow>();
        const float ARROW_SEC = 2.6f;

        /// <summary>戻ってきた物の上に差す矢印（物に付いて動く。JS版 respawnArrow）。</summary>
        public void RespawnArrow(Ent e, int col)
        {
            if (e == null) return;
            NetEv("ar", e.nid, col);
            var g = new GameObject("Arrow").transform; g.SetParent(fxRoot, false);
            var em = Mats.NewBasic(Mats.Hex(0x2f2a22), true, false, false);
            var cm = Mats.NewBasic(Mats.Hex(col), true, false, false);
            var edge = Part.Add(g, MeshGen.Cone(0.28f, 0.56f, 4), em, Vector3.zero, false).transform;
            edge.localRotation = Part.Euler3(Mathf.PI, Mathf.PI / 4, 0); edge.localScale = new Vector3(1, 1, 1);
            var cone = Part.Add(g, MeshGen.Cone(0.23f, 0.46f, 4), cm, Vector3.zero, false).transform;
            cone.localRotation = Part.Euler3(Mathf.PI, Mathf.PI / 4, 0);
            arrows.Add(new Arrow { g = g, cone = cm, edge = em, ent = e });
        }

        /// <summary>戻した所の印（柱と矢印）。ロボと客で使う。</summary>
        void Respawned(Ent e, Vector3 at, int col, bool soft)
        {
            RespawnMark(at, col, soft);
            if (!soft) RespawnArrow(e, col);
        }

        // ================================================================ 客の虹（HP が減るほど量が増える）
        static Texture2D rainbowTex;
        static Texture2D RainbowTex()
        {
            if (rainbowTex != null) return rainbowTex;
            // JS版の CanvasTexture は色の空間を指定していない（＝絵の色をリニアのまま使う）ので、画面では明るく出る。同じに見せるためリニアで作る
            // 口から流れ出て見えるよう、縦にくり返して流す（offset を動かす）
            rainbowTex = new Texture2D(8, 70, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color[8 * 70];
            for (int y = 0; y < 70; y++)
                for (int x = 0; x < 8; x++)
                    px[(69 - y) * 8 + x] = Mats.Hex(RAINBOW_COLS[Mathf.Min(6, y * 7 / 70)]);
            rainbowTex.SetPixels(px); rainbowTex.Apply(false);
            return rainbowTex;
        }

        static Material rainbowMat;
        /// <summary>
        /// 客の口から出る虹（見た目の入れ物に付ける。モデルに差し替えても残す）。
        /// **とがった先（口）を原点にする。**真ん中が原点だと、大きくするほど先が上へずれて鼻や目から出て見えた（担当）
        /// </summary>
        public GameObject Vomit(Transform guest, float hR, float headY)
        {
            if (rainbowMat == null)
            {
                rainbowMat = Mats.BasicTex(RainbowTex(), true, true);
                rainbowMat.SetColor("_BaseColor", new Color(1, 1, 1, 0.94f));
            }
            var v = new GameObject("Vomit");
            v.transform.SetParent(guest, false);
            v.transform.localPosition = Coord.W(0, headY - hR * 0.65f, -hR * 1.10f);
            v.transform.localRotation = Part.Euler3(0.55f, 0, 0);
            var cone = Part.Add(v.transform, MeshGen.Cone(hR * 0.80f, hR * 2.0f, 12, true), rainbowMat, new Vector3(0, -hR, 0), false, "Cone");
            cone.transform.localScale = new Vector3(1, 1, 0.22f);         // 奥行きを潰して平面的な吐き方に
            v.SetActive(false);
            return v;
        }

        // ---- 虹の水たまり。外から赤→紫の輪を、ふちを波打たせて重ねる（真上からでも「虹がこぼれた」と分かるように）
        static Texture2D puddleTex;
        static Texture2D PuddleTex()
        {
            if (puddleTex != null) return puddleTex;
            const int S = 128;
            var wob = new[] { 0.0f, 1.3f, 2.1f, 0.7f, 2.9f, 1.8f, 0.4f };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    Color32 c = new Color32(0, 0, 0, 0);
                    for (int i = 0; i < 7; i++)
                    {
                        float r0 = S * 0.48f * (1 - i / 7f * 0.86f);
                        float rr = r0 * (1 + 0.10f * Mathf.Sin(a * 5 + wob[i]) + 0.06f * Mathf.Sin(a * 3 + wob[i] * 2));
                        if (r <= rr) { var cc = Mats.Hex(RAINBOW_COLS[i]); cc.a = 0.92f; c = cc; }
                    }
                    px[y * S + x] = c;
                }
            puddleTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            puddleTex.SetPixels32(px); puddleTex.Apply(false);
            return puddleTex;
        }
        class Puddle { public Transform m; public Material mat; public float size, t, age; }
        readonly List<Puddle> puddles = new List<Puddle>();
        const float PUDDLE_LIFE = 10, PUDDLE_FADE = 2;
        /// <summary>客の足もと（顔の向きの少し前）に虹の水たまりを置く。床（動かない物）の上だけ。広がりながら出る</summary>
        void GuestPuddle(Ent gu, float r)
        {
            var p = gu.transform.position;
            RaycastHit hit; bool found = false; float y = 0;
            foreach (var h in Physics.RaycastAll(p, Vector3.down, 3, ~0, QueryTriggerInteraction.Ignore))
            {
                var e = h.collider.GetComponentInParent<Ent>();
                if (e == gu) continue;
                var rb = h.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                if (!found || h.point.y > y) { y = h.point.y; found = true; hit = h; }
            }
            if (!found) return;
            var fw = gu.transform.rotation * Vector3.forward; fw.y = 0; if (fw.sqrMagnitude > 1e-4f) fw.Normalize();
            var mat = Mats.BasicTex(PuddleTex(), true, false);
            mat.renderQueue = 3001;
            var m = Part.Add(fxRoot, MeshGen.FlatUp(1, 1), mat, new Vector3(p.x + fw.x * 0.3f, y + 0.014f + 0.002f * puddles.Count, p.z + fw.z * 0.3f), false, "Puddle").transform;
            m.rotation = Quaternion.Euler(0, Random.value * 360, 0);
            m.localScale = Vector3.one * 0.01f;
            puddles.Add(new Puddle { m = m, mat = mat, size = r * 2 });
            if (puddles.Count > 48) { var o = puddles[0]; puddles.RemoveAt(0); if (o.m != null) Destroy(o.m.gameObject); Destroy(o.mat); }
        }
        // 水たまりが広がる（0.35秒）。しばらく（10秒）したら薄くなって消える（担当）
        void PuddleTick(float dt)
        {
            for (int i = puddles.Count - 1; i >= 0; i--)
            {
                var d = puddles[i];
                if (d.m == null) { puddles.RemoveAt(i); continue; }
                d.age += dt;
                if (d.t < 1) { d.t = Mathf.Min(1, d.t + dt / 0.35f); float k = 1 - Mathf.Pow(1 - d.t, 3); d.m.localScale = new Vector3(d.size * k, 1, d.size * k); }
                float left = PUDDLE_LIFE - d.age;
                if (left <= 0) { Destroy(d.m.gameObject); Destroy(d.mat); puddles.RemoveAt(i); }
                else if (left < PUDDLE_FADE) { var c = d.mat.GetColor("_BaseColor"); c.a = left / PUDDLE_FADE; d.mat.SetColor("_BaseColor", c); }
            }
        }

        /// <summary>
        /// 体力が尽きた：大きな虹の破裂と「昇天！」、頭の上に天使の輪（金の輪）。救急車が乗せていくまで付いている。
        /// 輪は**体の向きに合わせて頭の先**に付ける（横に倒れていたら、倒れた頭の先）。
        /// </summary>
        void GuestHalo(Ent gu)
        {
            var p = gu.transform.position;
            for (int k = 0; k < 21; k++) SpawnSplashLocal(p + Vector3.up * 0.3f, RAINBOW_COLS[k % RAINBOW_COLS.Length], 2, true);
            if (state == "play") hud.Comic(p + Vector3.up * 1.1f, "昇天！", 34, 0xff5fae);
            GuestPuddle(gu, 0.75f);
            gu.ring = Part.Add(stageRoot, MeshGen.Torus(0.2f, 0.035f, 8, 20), Mats.Get(0xe8b400), p, shadow: false, name: "Halo");
            // 高さは頭のてっぺんの少し上。立っている向きで測る
            var q = gu.transform.rotation; gu.transform.rotation = Quaternion.identity;
            float top = 0.55f; bool any = false; Bounds b = new Bounds();
            foreach (var r in gu.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.name == "Cone") continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any) top = b.max.y - gu.transform.position.y;
            gu.transform.rotation = q;
            gu.haloY = top + 0.1f;
            HaloMove(gu);
        }
        void HaloMove(Ent gu)
        {
            var q = gu.transform.rotation;
            gu.ring.transform.position = gu.transform.position + q * new Vector3(0, gu.haloY > 0 ? gu.haloY : 0.65f, 0);
            gu.ring.transform.rotation = q * Quaternion.Euler(90, 0, 0);
        }

        // 客の傷み（虹）。体力の段階ごとに、口から吐く虹を太く・勢いよくし、足もとに虹の水たまりを残す。
        // 傷んでいる間は虹のしぶきを少しずつ撒き続ける。体力が尽きたら頭に金の輪（GuestHalo）。
        // 画面ごとに描く（体力はオンラインでも配られるので、参加者の画面でも同じになる）
        void TickGuestVomit()
        {
            float now = Time.time;
            if (rainbowMat != null) rainbowMat.SetTextureOffset("_BaseMap", new Vector2(0, -(now * 1.6f) % 1));
            foreach (var gu in guests)
            {
                if (gu.vomit == null) continue;
                float hp = gu.hp;
                int tier = hp <= 0 ? 4 : hp >= 80 ? 0 : hp >= 55 ? 1 : hp >= 30 ? 2 : 3;
                float s = (tier == 0 || tier == 4) ? 0 : new[] { 0f, 0.9f, 1.3f, 1.75f }[tier];
                gu.vomit.SetActive(s > 0 && !gu.taken && !gu.sunk);
                // 吐く勢い。段階が上がった直後は大きく、そのあとも脈打つ
                float gush = gu.gushT > 0 ? 1 + 0.6f * gu.gushT : 1;
                if (gu.ph == 0) gu.ph = 0.01f + Random.value * 6.3f;
                if (s > 0) gu.vomit.transform.localScale = new Vector3(s * gush, s * gush * (1 + 0.12f * Mathf.Sin(now * 14 + gu.ph)), s * gush);
                if (gu.gushT > 0) gu.gushT = Mathf.Max(0, gu.gushT - Time.deltaTime);
                bool live = state == "play" && !gu.taken && !gu.sunk;
                var p = gu.transform.position;
                if (tier > gu.vomitTier && tier < 4 && live)
                {
                    for (int k = 0; k < 12; k++) SpawnSplashLocal(p + Vector3.up * 0.34f, RAINBOW_COLS[k % RAINBOW_COLS.Length], 2, true);
                    gu.gushT = 1;
                    GuestPuddle(gu, new[] { 0f, 0.32f, 0.46f, 0.62f }[tier]);
                }
                // 傷んでいる間は、ときどき虹を吐く（段階が上なほど多く）
                if (tier >= 1 && tier < 4 && live && (gu.dripT -= Time.deltaTime) <= 0)
                {
                    gu.dripT = new[] { 0f, 1.2f, 0.6f, 0.3f }[tier];
                    SpawnSplashLocal(p + Vector3.up * 0.3f, RAINBOW_COLS[Random.Range(0, 7)], 1 + (tier >> 1), true);
                }
                if (tier == 4 && gu.ring == null && !gu.taken && !gu.sunk) GuestHalo(gu);
                if (gu.ring != null) HaloMove(gu);
                gu.vomitTier = tier;
            }
            PuddleTick(Time.deltaTime);
        }

        // ================================================================ 毎フレーム（見た目だけ）
        void TickFx(float dt)
        {
            var camPos = mainCam != null ? mainCam.transform.position : Vector3.zero;
            for (int i = splash.Count - 1; i >= 0; i--)
            {
                var s = splash[i]; s.age += dt;
                s.v.y -= 13 * dt;
                s.t.position += s.v * dt;
                s.t.rotation = Quaternion.LookRotation(s.t.position - camPos);     // いつもカメラを向く板
                s.t.localScale = new Vector3(1, 1 + Mathf.Min(2.4f, s.v.magnitude * 0.30f), 1);
                var c = s.m.GetColor("_BaseColor"); c.a = Mathf.Max(0, 1 - s.age / s.life); s.m.SetColor("_BaseColor", c);
                if (s.age >= s.life || s.t.position.y < -0.4f) { Destroy(s.t.gameObject); Destroy(s.m); splash.RemoveAt(i); }
            }
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                var s = puffs[i]; s.age += dt;
                float k = s.age / s.life;
                s.v *= 1 - 1.8f * dt;
                s.t.position += s.v * dt;
                s.t.localScale = Vector3.one * (0.12f + s.grow * Mathf.Min(1, k * 1.6f));
                var c = s.m.GetColor("_BaseColor"); c.a = Mathf.Max(0, 0.95f * (1 - k * k)); s.m.SetColor("_BaseColor", c);
                if (k >= 1) { Destroy(s.t.gameObject); Destroy(s.m); puffs.RemoveAt(i); }
            }
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var F = flashes[i]; F.age += dt;
                float k = F.age / 0.28f;
                if (mainCam != null) F.face.rotation = mainCam.transform.rotation * Quaternion.Euler(0, 180, 0);
                F.flat.localScale = Vector3.one * (0.4f + k * 5.2f);
                F.face.localScale = Vector3.one * (0.4f + k * 3.6f);
                SetA(F.m0, Mathf.Max(0, 0.75f * (1 - k))); SetA(F.m1, Mathf.Max(0, 0.9f * (1 - k)));
                if (k >= 1) { Destroy(F.g.gameObject); Destroy(F.m0); Destroy(F.m1); flashes.RemoveAt(i); }
            }
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var M = marks[i]; M.age += dt; float t = M.age;
                float d = Mathf.Min(1, t / BEAM_DN);
                float bot = BEAM_H * (1 - d) * (1 - d), len = BEAM_H - bot;
                M.beam.localScale = new Vector3(1, Mathf.Max(0.001f, len / BEAM_H), 1);
                M.beam.localPosition = new Vector3(0, bot + len / 2, 0);
                float e = Mathf.Max(0, t - BEAM_LAND);
                float a = Mathf.Max(0, 1 - e / (MARK_SEC - BEAM_LAND));
                SetA(M.bm, 0.55f * a * M.k);
                for (int n = 0; n < M.rings.Count; n++)
                {
                    var r = M.rings[n];
                    r.gameObject.SetActive(t >= BEAM_LAND);
                    if (!r.gameObject.activeSelf) continue;
                    r.localPosition = new Vector3(0, Mathf.Max(0.03f, 0.16f + n * 0.38f - e * 0.55f), 0);
                    r.localScale = Vector3.one * (1 + e * 0.22f);
                    SetA(M.rm[n], Mathf.Max(0, 0.85f * a * M.k));
                }
                if (t >= MARK_SEC) { Destroy(M.g.gameObject); Destroy(M.bm); foreach (var m in M.rm) Destroy(m); marks.RemoveAt(i); }
            }
            for (int i = arrows.Count - 1; i >= 0; i--)
            {
                var A = arrows[i]; A.age += dt;
                float k = A.age / ARROW_SEC;
                if (k >= 1 || A.ent == null) { Destroy(A.g.gameObject); Destroy(A.cone); Destroy(A.edge); arrows.RemoveAt(i); continue; }
                var p = A.ent.transform.position;
                float top = A.ent.size.y > 0 ? A.ent.size.y / 2 : 0.5f;
                float bob = Mathf.Abs(Mathf.Sin(A.age * 5.2f)) * 0.18f;
                A.g.position = new Vector3(p.x, p.y + top + 0.50f + bob, p.z);
                float a = k > 0.75f ? 1 - (k - 0.75f) / 0.25f : 1;
                SetA(A.cone, a); SetA(A.edge, a * 0.9f);
            }
            TickGuestVomit();
        }

        static void SetA(Material m, float a) { var c = m.GetColor("_BaseColor"); c.a = a; m.SetColor("_BaseColor", c); }

        void ClearFx()
        {
            splash.Clear(); puffs.Clear(); flashes.Clear(); marks.Clear(); arrows.Clear(); dropped.Clear();
            bursts.Clear(); vipLook = null; puddles.Clear();
        }

        // ================================================================ 落ちた料理
        public class Dropped { public Ent ent; public Order order; public Dish dish; public float integ, t; public Player owner; }
        public readonly List<Dropped> dropped = new List<Dropped>();

        /// <summary>運んでいる料理を落とす（その場に皿ごと転がる）。kick … 弾く向き。</summary>
        public Dropped DropDish(Player P, Vector3 kick)
        {
            if (P.carried == null) return null;
            var c = P.carried;
            var p = P.ent.rb.position; var v = P.ent.rb.linearVelocity;
            P.carried = null; P.look.ShowDish(null);
            var d = SpillDish(c, p, v, kick, P);
            Say(P, "落としたにゃ〜！", 1.2f);
            SetFace(P, "hit", 0.8f);
            return d;
        }

        /// <summary>
        /// 自分で落とす（ドロップ。R3 / V。JS版 dropByHand）。後ろへそっと置く（前へ転がすと、進む先をふさぐ）。
        /// ぶつかって落としたときと同じく、崩れ（dropDmg）は付き、落とした本人は dropArm 秒のあいだ拾えない。
        /// </summary>
        Dropped DropByHand(Player P, Vector3 fwd)
        {
            if (P.carried == null) return null;
            var c = P.carried;
            var p = P.ent.rb.position; var v = P.ent.rb.linearVelocity;
            P.carried = null; P.look.ShowDish(null);
            var d = SpillDish(c, p, v, new Vector3(-fwd.x * 1.2f, 0, -fwd.z * 1.2f), P);
            Say(P, "置いていくにゃ！", 1.0f);
            return d;
        }

        Dropped SpillDish(Carried c, Vector3 p, Vector3 v, Vector3 kick, Player P)
        {
            var e = MakeBody("dish", p + Vector3.up * 0.22f, new Vector3(0.19f, 0.06f, 0.19f), Quaternion.identity, false, 1.4f, 0.6f, 0.2f, 1.4f);
            var pl = Part.Add(e.transform, MeshGen.Cylinder(0.17f, 0.15f, 0.03f, 16), Mats.Get(0xfaf7f0, 0.9f), Vector3.zero);
            DishLook.Build(e.transform, c.dish, 0.1f, pl);
            e.rb.linearVelocity = new Vector3(v.x * 0.5f + kick.x, 2.0f, v.z * 0.5f + kick.z);
            e.rb.angularVelocity = new Vector3((Random.value - 0.5f) * 8, (Random.value - 0.5f) * 6, (Random.value - 0.5f) * 8);
            var d = new Dropped { ent = e, order = c.order, dish = c.dish, integ = Mathf.Max(0, c.integ - T.dropDmg), owner = P };
            dropped.Add(d);
            NetEv("dd", e.nid, Dishes.All.IndexOf(c.dish), orders.IndexOf(c.order), R2(p.x), R2(p.y + 0.22f), R2(-p.z), Mathf.RoundToInt(d.integ));
            SpawnSplash(p + Vector3.up * 0.15f, c.dish.col, 10);
            Se("drop", p, P);
            return d;
        }

        /// <summary>落ちた料理を、届いた番号のまま作る（オンラインのゲスト。位置は配られてくる）。</summary>
        void MakeDropped(int nid, Dish dish, Order order, Vector3 at, float integ)
        {
            var e = MakeBody("dish", at, new Vector3(0.19f, 0.06f, 0.19f), Quaternion.identity, false, 1.4f, 0.6f, 0.2f, 1.4f);
            byNid.Remove(e.nid); NidAdd(e, nid);
            var pl = Part.Add(e.transform, MeshGen.Cylinder(0.17f, 0.15f, 0.03f, 16), Mats.Get(0xfaf7f0, 0.9f), Vector3.zero);
            DishLook.Build(e.transform, dish, 0.1f, pl);
            GuestFreeze(e);
            dropped.Add(new Dropped { ent = e, order = order, dish = dish, integ = integ });
        }

        void KillDropped(Dropped d)
        {
            if (d.ent != null) { NetEv("ddx", d.ent.nid); byNid.Remove(d.ent.nid); }
            dropped.Remove(d);
            ents.Remove(d.ent);
            if (d.ent != null) Destroy(d.ent.gameObject);
        }

        void TakeDropped(Player P, Dropped d)
        {
            P.carried = new Carried { dish = d.dish, integ = d.integ, order = d.order };
            d.order.taker = P;
            P.look.ShowDish(d.dish);
            Say(P, d.owner == P ? "拾い直したにゃ" : "いただきにゃ！", 1.2f);
            Ach(P, "pickup");
            Se("pick", P.ent.rb.position, P);
            KillDropped(d);
        }

        /// <summary>注文をカウンターへ出し直す（並びの長さは変えないので、クリア条件は変わらない）。</summary>
        void ReissueOrder(Order order)
        {
            if (order.vip) { VipFail(order); VipClose(order); return; }   // VIP は作り直さない（なくなったら失敗）
            int i = orders.IndexOf(order);
            if (i < 0 || order.done) return;
            orders.RemoveAt(i);
            oi = Mathf.Max(0, oi - 1);
            orders.Insert(oi, order);
            order.taker = null;
            hud.Pop(PickupPoint() + Vector3.up * 1.2f, "作り直し " + order.dish.n, 0xff9500, true);
            SyncCounterPlates();
        }

        /// <summary>池・溶岩・海・穴・場外へ入ったら失われる（時間切れも同じ）。カウンターで作り直す。</summary>
        void UpdateDropped(float dt)
        {
            for (int i = dropped.Count - 1; i >= 0; i--)
            {
                var d = dropped[i];
                d.t += dt;
                var p = d.ent.rb.position;
                char ch = Tiles.At(under ?? map, p);
                bool lost = p.y < -0.9f || (Tiles.IsLiquid(ch) && p.y < 0.1f && !OverRaftNow(p)) || d.t > T.dishTimeout;
                if (lost) { KillDropped(d); ReissueOrder(d.order); }
            }
        }

        /// <summary>落ちている料理を拾う（落とした本人は少しのあいだ拾えない）。</summary>
        void PickDropped(Player P)
        {
            if (P.carried != null) return;
            var p = P.ent.rb.position;
            foreach (var d in dropped)
            {
                if (d.t < T.dropSettle) continue;
                if (d.owner == P && d.t < T.dropArm) continue;
                var dp = d.ent.rb.position;
                if (Mathf.Abs(dp.y - p.y) > 1.2f) continue;
                if (new Vector2(dp.x - p.x, dp.z - p.z).magnitude > T.pickupR) continue;
                TakeDropped(P, d);
                break;
            }
        }
    }
}
