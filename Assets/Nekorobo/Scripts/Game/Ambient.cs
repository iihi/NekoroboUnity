using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// まわりの動き（にぎやかし）。JS版 ambient.js を写したもの。見た目だけで、当たり判定や物理には触らない。
    /// オーバークックの周りが「生きている町」に見えるのは、旗がはためき、煙が上がり、人や鳥が動いているから。
    ///
    ///   FxAttach(root) … モデルの中の「fx_」で始まる素材の部品を動かす（tools/models/ambient.py の決まり）
    ///       fx_spinz_* / fx_spinx_* / fx_spiny_* … 回る（風車の羽）。同じ名前の頭の部品をまとめた外寸の中心が軸
    ///       fx_wave_*  … 旗。UV の u（付け根 0 → 先 1）を重みにして、先ほど大きく揺らす
    ///       fx_flapl_* / fx_flapr_* … 鳥の左右の羽。体の中心（x=0）を軸に羽ばたく
    ///       fx_smoke_* … 煙の出る所の目印。見えなくして、そこから煙を出す
    ///       smoke      … 前からある「止まった煙の玉」（煙突・工場）。これも消して、根元から本物の煙を出す
    ///   Add(type, obj) … 置き物ごとの動き
    ///       walk … 向いている方へ歩いて、dist[m] 先で引き返す（通行人）
    ///       bird … 置いた所の上をぐるぐる飛ぶ
    ///       bob  … 水に浮かんでゆらゆら（船・ボート）
    ///       sea  … 海の波の絵を流し、岸の白い泡を寄せては返す（島のまわり）
    ///   Tick(dt) … 毎フレーム（Game.Update）
    ///
    /// **向き**：glTFast で読んだモデルの中は three.js の x を反転した向き（鏡写し）なので、
    /// z と y まわりの回しは符号を逆にする（x まわりはそのまま）。ステージを作り直しても何もしなくてよい（消えた物はそのうち捨てる）。
    /// </summary>
    public static class Ambient
    {
        class Fx
        {
            public string type; public Transform obj; public float ph;
            // 部品の回し・羽
            public Quaternion r0;
            // 旗
            public Mesh mesh; public Vector3[] basePos, now; public float[] w;
            // 煙
            public List<Puff> puffs; public float next; public bool on;
            // 歩く・飛ぶ・浮かぶ
            public Vector3 p0; public Vector3 fwd; public float dist, speed, s, dir, pause, turn, step;
            public float cx, cz, y0, r, h, wv, a;
            // 海
            public Material seaMat; public Transform foam; public Material foamMat;
        }
        class Puff { public Transform m; public Material mat; public float life, max, dx, dz, k; }

        static readonly List<Fx> FX = new List<Fx>();
        static float T, sweep;
        static int smokeN;
        const int SMOKE_MAX = 30;                 // 同時に煙を出す所の上限（多すぎると重い）
        static Transform puffRoot;
        static Mesh puffMesh;

        static float Rnd() { return Random.value; }

        // ================================================================ モデルの中の動く部品
        public static void FxAttach(GameObject root)
        {
            if (root == null) return;
            var groups = new Dictionary<string, KeyValuePair<string, List<Renderer>>>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponent<FxDone>() != null || r.sharedMaterial == null) continue;
                var nm = r.sharedMaterial.name;
                string type = null;
                foreach (var t in new[] { "spinz", "spinx", "spiny", "flapl", "flapr", "wave", "smoke" })
                    if (nm.StartsWith("fx_" + t)) { type = t; break; }
                if (type == null && (nm == "smoke" || nm.StartsWith("smoke ("))) type = "puffs";
                if (type == null) continue;
                r.gameObject.AddComponent<FxDone>();
                if (type == "wave") { AddWave(r); continue; }
                if (type == "smoke" || type == "puffs") { AddSmoke(r, type == "puffs"); continue; }
                string key = r.transform.parent.GetInstanceID() + type;
                if (!groups.ContainsKey(key)) groups[key] = new KeyValuePair<string, List<Renderer>>(type, new List<Renderer>());
                groups[key].Value.Add(r);
            }
            foreach (var g in groups.Values) AddPivot(g.Key, g.Value);
        }

        /// <summary>FxAttach を2度かけないための印。</summary>
        class FxDone : MonoBehaviour { }

        /// <summary>部品の外寸（親から見た向き）。</summary>
        static Bounds LocalBox(Transform parent, List<Renderer> rs)
        {
            bool any = false; var bb = new Bounds();
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = parent.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1)));
                    if (!any) { bb = new Bounds(p, Vector3.zero); any = true; } else bb.Encapsulate(p);
                }
            }
            return bb;
        }

        /// <summary>部品をまとめて、軸の位置に置いた入れ物へ入れ替える（入れ物を回せば軸まわりに回る）。</summary>
        static void AddPivot(string type, List<Renderer> rs)
        {
            var parent = rs[0].transform.parent;
            var box = LocalBox(parent, rs);
            var c = box.center;
            if (type == "flapl" || type == "flapr") { c.x = 0; c.y = box.min.y; }    // 羽は体の中心が付け根
            var pv = new GameObject("fx_" + type).transform;
            pv.SetParent(parent, false);
            pv.localPosition = c;
            foreach (var r in rs) r.transform.SetParent(pv, true);
            // 左右の羽は同じ調子で動かす（同じ鳥なら同じ位相）
            var ph = parent.GetComponent<FxPhase>();
            if (ph == null) { ph = parent.gameObject.AddComponent<FxPhase>(); ph.v = Rnd() * 6.283f; }
            FX.Add(new Fx { type = type, obj = pv, ph = ph.v, r0 = pv.localRotation });
        }
        class FxPhase : MonoBehaviour { public float v; }

        static void AddWave(Renderer r)
        {
            // 形を書き換えるので、ほかの置き物と共有している形を自分用に複製する
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var mesh = Object.Instantiate(mf.sharedMesh);
            mf.sharedMesh = mesh;
            var pos = mesh.vertices; var uv = mesh.uv;
            if (uv == null || uv.Length != pos.Length) return;
            var w = new float[pos.Length];
            for (int i = 0; i < pos.Length; i++) w[i] = Mathf.Clamp01(uv[i].x);
            // 揺れて外寸からはみ出しても消えないように
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.one * 0.4f);
            FX.Add(new Fx { type = "wave", obj = r.transform, mesh = mesh, basePos = pos, now = (Vector3[])pos.Clone(), w = w, ph = Rnd() * 6.283f });
        }

        static void AddSmoke(Renderer r, bool legacy)
        {
            r.enabled = false;
            var mf = r.GetComponent<MeshFilter>();
            var b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds();
            var p = b.center;
            // 前からの煙の玉は、いちばん下の玉（左下）が煙突の口の上にある（three.js の左＝こちらの右）
            if (legacy) { p.x = b.max.x - 0.26f; p.y = b.min.y; }
            var em = new GameObject("fx_smoke").transform;
            em.SetParent(r.transform, false);
            em.localPosition = p;
            FX.Add(new Fx { type = "smoke", obj = em, puffs = new List<Puff>(), next = Rnd() * 0.5f });
        }

        // ================================================================ 置き物ごとの動き
        /// <summary>walk：dist[m]・speed[m/秒]。bird：置いた所の上を回る。bob：ゆらゆら。</summary>
        public static void Add(string type, Transform obj, float dist = 6, float speed = 1.1f)
        {
            if (obj == null) return;
            var e = new Fx { type = type, obj = obj, ph = Rnd() * 6.283f, r0 = obj.localRotation, p0 = obj.localPosition };
            if (type == "walk")
            {
                // 向いている方（three.js の +z ＝ こちらの -z）へ歩く
                e.fwd = obj.localRotation * Vector3.back;
                e.dist = Mathf.Max(1, dist); e.speed = speed > 0 ? speed : 1.1f;
                e.s = Rnd() * e.dist; e.dir = Rnd() < 0.5f ? 1 : -1;
                e.turn = e.dir < 0 ? Mathf.PI : 0;
            }
            else if (type == "bird")
            {
                e.cx = obj.localPosition.x; e.cz = obj.localPosition.z; e.y0 = obj.localPosition.y;
                e.r = 2 + Rnd() * 2.5f; e.h = 4 + Rnd() * 2.5f;
                e.wv = (Rnd() < 0.5f ? -1 : 1) * (0.45f + Rnd() * 0.3f);
                e.a = Rnd() * 6.283f;
                obj.localScale = obj.localScale * 1.3f;        // 遠くからでも鳥と分かる大きさに
            }
            FX.Add(e);
        }

        /// <summary>島のまわりの海（波の絵を流す）と岸の白い泡（寄せては返す）。</summary>
        public static void AddSea(Material sea, Transform foam, Material foamMat)
        {
            FX.Add(new Fx { type = "sea", obj = foam, seaMat = sea, foam = foam, foamMat = foamMat });
        }

        // ================================================================ 毎フレーム
        public static void Tick(float dt)
        {
            T += dt;
            // ときどき、消えた物（作り直す前のステージ）を捨てる
            sweep += dt;
            if (sweep > 1)
            {
                sweep = 0;
                for (int k = FX.Count - 1; k >= 0; k--)
                {
                    var e = FX[k];
                    if (e.obj != null) continue;
                    if (e.type == "smoke") { foreach (var p in e.puffs) if (p.m != null) Object.Destroy(p.m.gameObject); if (e.on) smokeN--; }
                    FX.RemoveAt(k);
                }
            }
            foreach (var e in FX) if (e.obj != null) Tick1(e, dt);
        }

        static void Tick1(Fx e, float dt)
        {
            var o = e.obj;
            switch (e.type)
            {
                // three.js の回しを鏡写しにして（z・y は逆向き）
                case "spinz": o.localRotation = e.r0 * Quaternion.Euler(0, 0, -(-T * 0.9f + e.ph) * Mathf.Rad2Deg); break;
                case "spinx": o.localRotation = e.r0 * Quaternion.Euler((T * 0.9f + e.ph) * Mathf.Rad2Deg, 0, 0); break;
                case "spiny": o.localRotation = e.r0 * Quaternion.Euler(0, -(T * 0.9f + e.ph) * Mathf.Rad2Deg, 0); break;
                case "flapl":
                case "flapr":
                    {
                        // はばたいては滑空する（ずっと羽ばたくと虫のように見える）
                        float glide = Mathf.Sin(T * 0.6f + e.ph) > 0.2f ? 0.25f : 1;
                        float a = Mathf.Sin(T * 9 + e.ph) * 0.6f * glide + 0.1f;
                        o.localRotation = e.r0 * Quaternion.Euler(0, 0, -(e.type == "flapl" ? a : -a) * Mathf.Rad2Deg);
                        break;
                    }
                case "wave":
                    {
                        var b = e.basePos; var nw = e.now; var w = e.w;
                        for (int i = 0; i < b.Length; i++)
                        {
                            float x = -b[i].x, y = b[i].y;          // three.js の x
                            float s = Mathf.Sin(T * 5.5f + x * 2.2f + y * 1.3f + e.ph) + 0.4f * Mathf.Sin(T * 8.3f + x * 4.1f);
                            nw[i] = new Vector3(b[i].x, y - Mathf.Abs(s) * 0.015f * w[i], b[i].z + s * 0.07f * w[i]);
                        }
                        e.mesh.vertices = nw;
                        break;
                    }
                case "smoke": TickSmoke(e, dt); break;
                case "walk": TickWalk(e, dt); break;
                case "bird":
                    {
                        e.a += e.wv * dt;
                        // three.js の (x, z) で回して、z を反転して置く
                        float x = e.cx + Mathf.Cos(e.a) * e.r, z3 = -e.cz + Mathf.Sin(e.a) * e.r;
                        float vx = -Mathf.Sin(e.a) * Mathf.Sign(e.wv), vz = Mathf.Cos(e.a) * Mathf.Sign(e.wv);
                        o.localPosition = new Vector3(x, e.y0 + e.h + Mathf.Sin(T * 0.8f + e.ph) * 0.35f, -z3);
                        o.localRotation = Part.Euler3(0, Mathf.Atan2(vx, vz), -Mathf.Sign(e.wv) * 0.35f);
                        break;
                    }
                case "bob":
                    o.localPosition = e.p0 + new Vector3(0, Mathf.Sin(T * 1.4f + e.ph) * 0.05f, 0);
                    o.localRotation = e.r0 * Part.Euler3(Mathf.Sin(T * 0.9f + e.ph * 1.3f) * 0.02f, 0, Mathf.Sin(T * 1.1f + e.ph) * 0.03f);
                    break;
                case "sea":
                    if (e.seaMat != null) e.seaMat.SetTextureOffset("_BaseMap", new Vector2((T * 0.012f) % 1, (T * 0.02f) % 1));
                    if (e.foam != null)
                    {
                        float k = Mathf.Sin(T * 1.2f);
                        var c = e.foamMat.GetColor("_BaseColor"); c.a = 0.45f + 0.2f * k; e.foamMat.SetColor("_BaseColor", c);
                        e.foam.localScale = Vector3.one * (1 + 0.003f * (k + 1));
                    }
                    break;
            }
        }

        static void TickWalk(Fx e, float dt)
        {
            var o = e.obj;
            if (e.pause > 0) e.pause -= dt;
            else
            {
                e.s += e.dir * e.speed * dt;
                if (e.s > e.dist || e.s < 0)
                {
                    e.s = Mathf.Clamp(e.s, 0, e.dist);
                    e.dir = -e.dir;
                    e.pause = 0.6f + Rnd() * 2.2f;          // 立ち止まって、振り返ってから戻る
                }
                e.step += dt * e.speed * 7.5f;
            }
            // 向きはなめらかに変える（その場でくるっと回る）
            float want = e.dir < 0 ? Mathf.PI : 0;
            float d = Mathf.Atan2(Mathf.Sin(want - e.turn), Mathf.Cos(want - e.turn));
            e.turn += Mathf.Sign(d) * Mathf.Min(Mathf.Abs(d), dt * 5);
            bool moving = e.pause <= 0;
            o.localPosition = e.p0 + e.fwd * e.s + new Vector3(0, moving ? Mathf.Abs(Mathf.Sin(e.step)) * 0.05f : 0, 0);
            o.localRotation = Quaternion.AngleAxis(-e.turn * Mathf.Rad2Deg, Vector3.up) * e.r0
                            * Quaternion.Euler(0, 0, (moving ? Mathf.Sin(e.step) * 0.06f : 0) * Mathf.Rad2Deg);
        }

        static void TickSmoke(Fx e, float dt)
        {
            if (!e.on)
            {
                if (smokeN >= SMOKE_MAX) return;
                e.on = true; smokeN++;
            }
            if (puffRoot == null) { puffRoot = new GameObject("SmokePuffs").transform; }
            if (puffMesh == null) puffMesh = MeshGen.Sphere(0.5f, 10, 7);
            e.next -= dt;
            if (e.next <= 0)
            {
                e.next = 0.42f + Rnd() * 0.2f;
                Puff p = e.puffs.Find(q => q.life <= 0);
                if (p == null && e.puffs.Count < 9)
                {
                    var mat = Mats.LitTransparent(Mats.Hex(0xf2f2f4), 0.95f);
                    var go = Part.Add(puffRoot, puffMesh, mat, Vector3.zero, false, "Puff");
                    go.GetComponent<MeshRenderer>().receiveShadows = false;
                    p = new Puff { m = go.transform, mat = mat };
                    e.puffs.Add(p);
                }
                if (p != null)
                {
                    p.m.position = e.obj.position;
                    p.k = Mathf.Max(0.5f, e.obj.lossyScale.x);
                    p.life = p.max = 3.2f + Rnd() * 0.8f;
                    p.dx = 0.25f + Rnd() * 0.15f; p.dz = (Rnd() - 0.5f) * 0.12f;
                    p.m.gameObject.SetActive(true);
                }
            }
            foreach (var p in e.puffs)
            {
                if (p.life <= 0) { p.m.gameObject.SetActive(false); continue; }
                p.life -= dt;
                float t = 1 - p.life / p.max;                 // 0 → 1
                p.m.position += new Vector3(p.dx * dt * p.k, (0.9f - 0.3f * t) * dt * p.k, -p.dz * dt * p.k);
                p.m.localScale = Vector3.one * ((0.28f + 0.75f * Mathf.Sqrt(t)) * p.k);
                var c = p.mat.GetColor("_BaseColor"); c.a = 0.9f * (t < 0.15f ? t / 0.15f : (1 - t) / 0.85f); p.mat.SetColor("_BaseColor", c);
            }
        }
    }
}
