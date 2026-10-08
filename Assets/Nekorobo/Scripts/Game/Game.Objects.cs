using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>
    /// 盤面に置く「動く物・大きい物」。JS版 buildStage の後半と updateHazards の前半を写したもの。
    ///   置き物カタログ（ビル・木・街の小物・乗り物）／車／動く床／ルート（走る物・歩く客）／いかだ
    ///   道路の白線・線路・橋の欄干・盤面の外へ伸ばす道
    /// </summary>
    public partial class Game
    {
        // ---- 走る物（車・走る置き物）
        public class Car { public Ent ent; public Vector3 dir; public float speed; public Vector3 home; public float blown; public string vk; }
        public readonly List<Car> cars = new List<Car>();
        // ---- 動く床（1軸の往復）
        public class Mover { public Ent ent; public float speed, range; public int dir; public int axis; public Vector3 home; public bool routed; public float v; }
        public readonly List<Mover> movers = new List<Mover>();
        // ---- ルートで動く物（いかだも）
        public class Route
        {
            public Ent ent; public Transform mesh; public List<Vector3> pts = new List<Vector3>();
            public float y, meshY, speed, wait; public string mode; public bool turn, ease;
            public float bob, bobW, phase;
            public float mx, mz, lastX, lastZ, vx, vz;
            public int i, dir = 1; public float waitT, delayT;
            public Vector3 pos, legFrom;
            public Raft raft;
            public List<Rider> riders;
        }
        public class Rider { public Ent ent; public float ox, oz, y; public bool counter; }
        public readonly List<Route> routed = new List<Route>();
        // ---- 歩いて回る客
        public class Walker { public Ent ent; public List<Vector3> pts; public float speed, wait; public string mode; public int i, dir = 1; public float waitT, delayT; public bool done; public Raft raft; }
        public readonly List<Walker> walkers = new List<Walker>();
        // ---- いかだ
        public class Raft { public int n; public Ent ent; public Transform mesh; public List<Tiles.Rect> rects; public float cx, cz; public List<Vector2Int> cells; public Route R; }
        public readonly List<Raft> rafts = new List<Raft>();
        /// <summary>いかだのマスを「その下の地形」に置き換えた地図（水かどうかを見るとき使う）。</summary>
        public Dictionary<Vector2Int, char> under;
        readonly Dictionary<StageObj, Ent> objEnt = new Dictionary<StageObj, Ent>();
        readonly Dictionary<StageObj, Transform> routeMesh = new Dictionary<StageObj, Transform>();
        float routeT;

        static bool HasRoute(StageObj o) { return o != null && o.HasRoute; }

        // ================================================================ いかだの下の地形
        /// <summary>
        /// いかだのマスの「下」をどうするか。いかだの**すぐ隣の地形**で埋める（隣が床と水なら水）。
        /// JS版 buildStage の under と同じ決め方。
        /// </summary>
        void BuildUnder()
        {
            under = new Dictionary<Vector2Int, char>(map);
            var byN = new Dictionary<int, Dictionary<char, int>>();
            var whole = new Dictionary<char, int>();
            foreach (var kv in map)
            {
                var td = Tiles.Def(kv.Value);
                int n = td != null ? td.raft : 0;
                if (n == 0) { int c; whole.TryGetValue(kv.Value, out c); whole[kv.Value] = c + 1; continue; }
                Dictionary<char, int> cnt;
                if (!byN.TryGetValue(n, out cnt)) byN[n] = cnt = new Dictionary<char, int>();
                foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                {
                    char nb;
                    if (!map.TryGetValue(kv.Key + d, out nb)) continue;
                    var nd = Tiles.Def(nb);
                    if ((nd != null && nd.raft > 0) || Tiles.IsWall(nb)) continue;
                    int c; cnt.TryGetValue(nb, out c); cnt[nb] = c + 1;
                }
            }
            System.Func<Dictionary<char, int>, System.Func<char, bool>, char?> most = (cnt, ok) =>
            {
                char? best = null; int bn = 0;
                if (cnt != null) foreach (var kv in cnt) if (ok(kv.Key) && kv.Value > bn) { bn = kv.Value; best = kv.Key; }
                return best;
            };
            char fall = most(whole, ch => !Tiles.IsFloor(ch)) ?? most(whole, ch => true) ?? '~';
            foreach (var kv in map)
            {
                var td = Tiles.Def(kv.Value);
                if (td == null || td.raft == 0) continue;
                Dictionary<char, int> cnt; byN.TryGetValue(td.raft, out cnt);
                under[kv.Key] = most(cnt, ch => !Tiles.IsFloor(ch)) ?? most(cnt, ch => true) ?? fall;
            }
        }

        // ================================================================ いかだ
        const float RaftLogR = 0.19f, RaftLogStep = 0.30f;

        void BuildRafts(float fr)
        {
            var byRaft = new Dictionary<int, HashSet<Vector2Int>>();
            foreach (var kv in map)
            {
                var td = Tiles.Def(kv.Value); if (td == null || td.raft == 0) continue;
                HashSet<Vector2Int> s;
                if (!byRaft.TryGetValue(td.raft, out s)) byRaft[td.raft] = s = new HashSet<Vector2Int>();
                s.Add(kv.Key);
            }
            foreach (var kv in byRaft)
            {
                var rects = Tiles.MergeCells(kv.Value);
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                foreach (var r in rects)
                {
                    Vector3 c; float w, d; Tiles.RectWorld(r, out c, out w, out d);
                    x0 = Mathf.Min(x0, c.x - w / 2); x1 = Mathf.Max(x1, c.x + w / 2);
                    z0 = Mathf.Min(z0, c.z - d / 2); z1 = Mathf.Max(z1, c.z + d / 2);
                }
                float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
                // かたまり1つ＝キネマティックな剛体1つ。板の数だけ当たり判定を付ける
                var go = new GameObject("Raft" + kv.Key);
                go.transform.SetParent(stageRoot, false);
                go.transform.position = new Vector3(cx, 0, cz);
                var e = go.AddComponent<Ent>();
                e.kind = "floor"; e.infMass = true;
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
                e.rb = rb; e.size = new Vector3(0, Tiles.FloorH, 0);
                foreach (var r in rects)
                {
                    Vector3 c; float w, d; Tiles.RectWorld(r, out c, out w, out d);
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = new Vector3(c.x - cx, -0.9f + Tiles.FloorH / 2, c.z - cz);
                    bc.size = new Vector3(w, Tiles.FloorH, d);
                    bc.sharedMaterial = PMat(0.9f * fr, 0.05f);
                }
                ents.Add(e);
                var mesh = RaftMesh(rects, cx, cz);
                mesh.SetParent(stageRoot, false);
                mesh.position = new Vector3(cx, 0, cz);
                rafts.Add(new Raft { n = kv.Key, ent = e, mesh = mesh, rects = rects, cx = cx, cz = cz, cells = new List<Vector2Int>(kv.Value) });
            }
        }

        /// <summary>いかだの見た目。丸太を並べ、縁に丸太を回して、縄で締めた形（JS版 raftMesh）。</summary>
        Transform RaftMesh(List<Tiles.Rect> rects, float cx, float cz)
        {
            var g = new GameObject("RaftLook").transform;
            var m0 = Mats.Get(0xc98b45); var m1 = Mats.Get(0xb0763a);
            var rail = Mats.Get(0x8a5a2a); var lash = Mats.Get(0x6a4a2c);
            foreach (var r in rects)
            {
                Vector3 c; float w, d; Tiles.RectWorld(r, out c, out w, out d);
                bool alongX = w >= d;
                float span = alongX ? d : w, len = alongX ? w : d;
                int n = Mathf.Max(1, Mathf.RoundToInt(span / RaftLogStep));
                float step = span / n;
                for (int i = 0; i < n; i++)
                {
                    float off = -span / 2 + step * (i + 0.5f);
                    var lg = Part.Add(g, MeshGen.Cylinder(RaftLogR, RaftLogR, len, 10), i % 2 == 0 ? m0 : m1,
                                      new Vector3(c.x - cx + (alongX ? 0 : off), -RaftLogR, c.z - cz + (alongX ? off : 0)));
                    // 立った円柱を寝かせる（長いほうへ流す）
                    lg.transform.localRotation = alongX ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
                }
                foreach (var a in new[] { new Vector2(0, -1), new Vector2(0, 1), new Vector2(-1, 0), new Vector2(1, 0) })
                {
                    float rl = a.x != 0 ? d : w;
                    // a は three.js の向き（z が手前）。Unity では z を反転
                    var rg = Part.Add(g, MeshGen.Cylinder(0.11f, 0.11f, rl, 8), rail,
                                      new Vector3(c.x - cx + a.x * (w / 2 - 0.09f), -0.02f, c.z - cz - a.y * (d / 2 - 0.09f)));
                    rg.transform.localRotation = a.x != 0 ? Quaternion.Euler(90, 0, 0) : Quaternion.Euler(0, 0, 90);
                }
                foreach (float t in new[] { 0.22f, 0.78f })
                {
                    float o2 = (t - 0.5f) * len;
                    Part.Add(g, MeshGen.Box(alongX ? 0.13f : span - 0.1f, 0.05f, alongX ? span - 0.1f : 0.13f), lash,
                             new Vector3(c.x - cx + (alongX ? o2 : 0), 0.012f, c.z - cz + (alongX ? 0 : -o2)), shadow: false);
                }
            }
            return g;
        }

        Raft RaftAt(Vector3 p)
        {
            foreach (var rf in rafts)
                foreach (var r in rf.rects)
                {
                    Vector3 c; float w, d; Tiles.RectWorld(r, out c, out w, out d);
                    if (Mathf.Abs(p.x - c.x) <= w / 2 && Mathf.Abs(p.z - c.z) <= d / 2) return rf;
                }
            return null;
        }

        /// <summary>**いま**いかだが浮いている所か（いかだは動くので、地形の文字では分からない）。</summary>
        bool OverRaftNow(Vector3 p)
        {
            foreach (var R in routed)
            {
                var rf = R.raft; if (rf == null) continue;
                float ox = R.pos.x - rf.cx, oz = R.pos.z - rf.cz;
                foreach (var r in rf.rects)
                {
                    Vector3 c; float w, d; Tiles.RectWorld(r, out c, out w, out d);
                    if (Mathf.Abs(p.x - (c.x + ox)) <= w / 2 && Mathf.Abs(p.z - (c.z + oz)) <= d / 2) return true;
                }
            }
            return false;
        }

        /// <summary>戻し先がいかだのマスなら、いまいかだが居る所へずらす（JS版 raftShift）。</summary>
        Vector3 RaftShift(Vector3 p)
        {
            foreach (var rf in rafts)
            {
                if (rf.R == null) continue;
                if (rf.cells.Contains(new Vector2Int(Coord.ToI(p.x), Coord.ToJ(p.z))))
                    return new Vector3(p.x + (rf.R.pos.x - rf.cx), p.y, p.z + (rf.R.pos.z - rf.cz));
            }
            return p;
        }

        // ================================================================ 置いた物（車・動く床・置き物）
        /// <summary>カタログの置き物（ビル・木・街の小物・乗り物）。JS版 buildStage の「カタログの置き物」。</summary>
        bool BuildProp(StageObj o, Vector3 b, float W, float D)
        {
            var pr = Props.Get(o.t);
            if (pr == null) return false;
            // 床の上に置いた物だけ当たり判定を作る（まわりの飾りには行けないので要らない）
            bool onFloor = false;
            for (int di = 0; di < W && !onFloor; di++)
                for (int dj = 0; dj < D && !onFloor; dj++)
                    if (Tiles.IsFloor(Tiles.At(map, new Vector3(b.x - W / 2 + di + 0.5f, 0, b.z + D / 2 - dj - 0.5f)))) onFloor = true;
            bool rt = HasRoute(o);
            float drive = (!rt && o.speed > 0) ? o.speed.Value : 0;
            if (pr.solid && drive == 0 && (onFloor || rt))
            {
                var half = Props.Half(pr, W, D);
                var e = MakeBody("prop", new Vector3(b.x, half.y, b.z), half, Quaternion.identity, true, 0, 0.7f, 0.1f);
                objEnt[o] = e;
            }
            // 見た目は回す前の大きさ（カタログの w×d）で作って、あとから回す
            var holder = new GameObject("PropHolder_" + pr.k).transform;
            holder.SetParent(stageRoot, false);
            var look = PropLooks.Build(holder, pr, pr.w, pr.d);
            PropModel(pr, holder, look);
            if (drive > 0)
            {
                // 走る乗り物。当たり判定は運動学ボディ（押されない）で、見た目はその中へ
                var half = Props.Half(pr, W, D);
                var car = MakeBody("car", new Vector3(b.x, half.y, b.z), half, Quaternion.identity, false, 1000, 0.4f, 0.3f, kinematic: true);
                holder.SetParent(car.transform, false);
                holder.localPosition = new Vector3(0, -half.y, 0);
                holder.localRotation = Coord.RotX(o.rot);
                cars.Add(new Car { ent = car, speed = drive, dir = Coord.Dir(o.rot), home = car.transform.position, vk = pr.kind });
                return true;
            }
            holder.position = b;
            holder.rotation = Coord.RotX(o.rot);
            if (rt) routeMesh[o] = holder;
            else { var rf = RaftAt(b); if (rf != null) holder.SetParent(rf.mesh, true); }
            return true;
        }

        /// <summary>置き物のモデル。読めていなければ読みに行き、読めたら差し替える（JS版 needPropModel）。</summary>
        void PropModel(PropDef pr, Transform holder, GameObject look)
        {
            if (pr.model == null || pr.model.Count == 0) return;
            int gen = buildGen;
            ModelStore.EnsureProp(pr, () =>
            {
                if (holder == null || gen != buildGen) return;    // 読み終わる前に面を組み直した
                float h;
                ModelStore.Apply(holder, look, pr.k, null, new Vector3(pr.w * 0.88f, pr.h, pr.d * 0.88f), out h,
                                 footY: 0, foot: new Vector2(pr.w, pr.d));
            });
        }

        /// <summary>車（o.t = "car"）。置いた向きへ一定の速さで走り、画面の外へ出たら反対側から戻る。</summary>
        void BuildCar(StageObj o, Vector3 b)
        {
            var car = MakeBody("car", new Vector3(b.x, 0.42f, b.z), new Vector3(0.9f, 0.35f, 0.42f), Coord.RotX(o.rot),
                               false, 1000, 0.4f, 0.3f, kinematic: true);
            var look = Vis(car.transform, "Look");
            int col = CARCOL[cars.Count % CARCOL.Length];
            Part.Add(look.transform, MeshGen.Box(1.8f, 0.5f, 0.84f), Mats.Get(col), Vector3.zero);
            Part.Add(look.transform, MeshGen.Box(0.95f, 0.34f, 0.78f), Mats.Get(col), new Vector3(-0.1f, 0.4f, 0));
            foreach (float wx in new[] { -0.58f, 0.58f })
                foreach (float wz in new[] { -0.44f, 0.44f })
                {
                    var wl = Part.Add(look.transform, MeshGen.Cylinder(0.19f, 0.19f, 0.12f, 10), Mats.Get(0x181818), new Vector3(wx, -0.24f, wz));
                    wl.transform.localRotation = Quaternion.Euler(90, 0, 0);
                }
            ApplyModel(car, look, "car");
            cars.Add(new Car { ent = car, speed = o.speed ?? 8f, dir = Coord.Dir(o.rot), home = car.transform.position });
        }
        static readonly int[] CARCOL = { 0xd94f4f, 0x4f7fd9, 0x54b06a, 0xe0a53c, 0x8c6fd0 };

        /// <summary>動く床（1軸の往復）。乗っている物は「床が動いたぶん」だけ一緒に動かす。</summary>
        void BuildMover(StageObj o, Vector3 b)
        {
            var e = MakeBody("mover", new Vector3(b.x, -0.1f, b.z), new Vector3(1.45f, 0.1f, 0.45f), Coord.RotX(o.rot),
                             false, 1000, 0.9f, 0f, kinematic: true);
            Part.Add(e.transform, MeshGen.Box(2.9f, 0.2f, 0.9f), Mats.Get(0xb08a5a), Vector3.zero);
            var d = Coord.Dir(o.rot);
            var raw = o.raw;
            movers.Add(new Mover
            {
                ent = e, speed = o.speed ?? 2.4f, dir = J.I(raw, "dir", 1), range = J.F(raw, "range", 4.0f),
                axis = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? 0 : 2, home = e.transform.position, routed = HasRoute(o),
            });
            objEnt[o] = e;
        }

        // ================================================================ ルート
        void AddRoute(StageObj o, Ent ent, Transform mesh, Vector3 home, bool relative)
        {
            var r = o.raw["route"] as JObject;
            var arr = r != null ? r["pts"] as JArray : null;
            if (arr == null) return;
            var pts = new List<Vector3>();
            foreach (var t in arr) { var a = t as JArray; if (a != null && a.Count >= 2) pts.Add(Coord.Cell((int)a[0], (int)a[1])); }
            if (relative && pts.Count > 0)
            {
                var p0 = pts[0];
                for (int i = 0; i < pts.Count; i++) pts[i] = new Vector3(home.x + (pts[i].x - p0.x), 0, home.z + (pts[i].z - p0.z));
            }
            if (pts.Count < 2) return;
            routed.Add(new Route
            {
                ent = ent, mesh = mesh, pts = pts,
                y = ent != null ? ent.transform.position.y : 0, meshY = mesh != null ? mesh.position.y : 0,
                speed = J.F(r, "speed", 3.0f), mode = J.S(r, "mode", "pingpong"), wait = J.F(r, "wait", 0),
                turn = J.B(r, "turn"), ease = J.B(r, "ease"), lastX = home.x, lastZ = home.z,
                delayT = J.F(r, "delay", 0), pos = new Vector3(home.x, 0, home.z), legFrom = new Vector3(home.x, 0, home.z),
            });
        }

        void AddWalker(StageObj o, Ent ent, Vector3 home)
        {
            var r = o.raw["route"] as JObject;
            var arr = r != null ? r["pts"] as JArray : null;
            if (arr == null) return;
            var pts = new List<Vector3>();
            foreach (var t in arr) { var a = t as JArray; if (a != null && a.Count >= 2) pts.Add(Coord.Cell((int)a[0], (int)a[1])); }
            if (pts.Count < 2) return;
            walkers.Add(new Walker
            {
                ent = ent, pts = pts, speed = J.F(r, "speed", 1.0f), mode = J.S(r, "mode", "pingpong"), wait = J.F(r, "wait", 0),
                delayT = J.F(r, "delay", 0), raft = RaftAt(home),
            });
        }

        /// <summary>ルートを付ける（物を作り終わってから）。JS版 buildStage の「ルート」と「いかだに乗せた物」。</summary>
        void BuildRoutes()
        {
            foreach (var o in stage.objects)
            {
                if (!HasRoute(o)) continue;
                if (o.t == "raft")
                {
                    int grp = J.I(o.raw, "group", 1);
                    var rf = rafts.Find(x => x.n == grp);
                    if (rf != null)
                    {
                        AddRoute(o, rf.ent, rf.mesh, new Vector3(rf.cx, 0, rf.cz), true);
                        if (routed.Count > 0 && routed[routed.Count - 1].ent == rf.ent) { var R = routed[routed.Count - 1]; R.raft = rf; R.mesh = rf.mesh; }
                    }
                    continue;
                }
                Ent ent; objEnt.TryGetValue(o, out ent);
                float bw, bd; Vector3 b; Tiles.ObjBox(o, out bw, out bd, out b);
                var r = o.raw["route"] as JObject;
                bool walk = r != null && r["walk"] != null ? J.B(r, "walk") : o.t == "guest";
                if (walk && ent != null) { AddWalker(o, ent, b); continue; }
                if (ent != null && ent.rb != null) { ent.rb.isKinematic = true; ent.infMass = true; }
                else if (ent != null && ent.rb == null)
                {
                    // 動かない置き物の当たり判定にルートを付けたとき：キネマティックにして動かす
                    var rb = ent.gameObject.AddComponent<Rigidbody>();
                    rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
                    ent.rb = rb; ent.isFixed = false; ent.infMass = true;
                }
                Transform rm; routeMesh.TryGetValue(o, out rm);
                AddRoute(o, ent, rm, b, false);
            }
            // いかだに乗せた「押しても動かない物」（受取カウンター・置き物）を一緒に動かす
            foreach (var rf in rafts)
            {
                var R = routed.Find(x => x.raft == rf);
                if (R == null) continue;
                R.riders = new List<Rider>(); rf.R = R;
                R.bob = 0.05f; R.bobW = 2.3f + rf.n * 0.37f; R.phase = rf.n * 2.1f;
                foreach (var e in ents)
                {
                    if (!e.isFixed) continue;
                    if (e.kind == "floor" || e.kind == "pit" || e.kind == "wall" || e.kind == "fence") continue;
                    var q = e.transform.position;
                    if (q.y < -0.5f || q.y > 3.5f) continue;
                    bool on = false;
                    foreach (var r in rf.rects)
                    {
                        Vector3 c; float w, d; Tiles.RectWorld(r, out c, out w, out d);
                        if (Mathf.Abs(q.x - c.x) <= w / 2 && Mathf.Abs(q.z - c.z) <= d / 2) { on = true; break; }
                    }
                    if (!on) continue;
                    var rb = e.gameObject.AddComponent<Rigidbody>();
                    rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
                    e.rb = rb; e.isFixed = false; e.infMass = true;
                    R.riders.Add(new Rider { ent = e, ox = q.x - rf.cx, oz = q.z - rf.cz, y = q.y, counter = e == counterEnt });
                }
            }
        }

        // ================================================================ 毎フレーム（JS版 updateHazards の前半）
        void UpdateMoving(float dt)
        {
            // ---- 車：置いた向きへ一定速で走り、画面から出きったら反対側から入れ直す
            foreach (var car in cars)
            {
                if (car.blown > 0) continue;
                var rb = car.ent.rb;
                var p = rb.position + car.dir * car.speed * dt;
                car.ent.prevV = car.dir * car.speed;
                int ax = Mathf.Abs(car.dir.x) > 0.5f ? 0 : 1;
                float dir = ax == 0 ? Mathf.Sign(car.dir.x) : Mathf.Sign(car.dir.z);
                if (dir != 0)
                {
                    var B = bounds;
                    float cur = ax == 0 ? p.x : p.z;
                    float edge = ax == 0 ? (dir > 0 ? B.x1 : B.x0) : (dir > 0 ? B.z1 : B.z0);
                    float outN = (cur - edge) * dir;
                    if (outN >= 0 && (outN >= 80 || !VehInView(car, p)))
                    {
                        float v = ax == 0 ? (dir > 0 ? B.x0 : B.x1) : (dir > 0 ? B.z0 : B.z1);
                        var q = p;
                        for (int n2 = 0; n2 < 80; n2++)
                        {
                            if (ax == 0) q.x = v; else q.z = v;
                            if (!VehInView(car, q)) break;
                            v -= dir;
                        }
                        if (ax == 0) p.x = v; else p.z = v;
                        rb.position = p; car.ent.transform.position = p;
                        continue;
                    }
                }
                rb.MovePosition(p);
            }
            UpdateRoutes(dt);
            CarryRiders();
            UpdateWalkers(dt);
            // ---- 動く床
            if (movers.Count > 0)
            {
                foreach (var mv in movers)
                {
                    if (mv.routed) continue;
                    var p = mv.ent.rb.position;
                    float rel = p[mv.axis] - mv.home[mv.axis];
                    if (rel > mv.range && mv.dir > 0) mv.dir = -1;
                    if (rel < -mv.range && mv.dir < 0) mv.dir = 1;
                    mv.v = mv.speed * mv.dir;
                    var np = p; np[mv.axis] += mv.v * dt;
                    var vel = Vector3.zero; vel[mv.axis] = mv.v;
                    mv.ent.prevV = vel;
                    mv.ent.rb.MovePosition(np);
                }
                foreach (var e in ents)
                {
                    if (e.isFixed || e.infMass || e.rb == null || e.kind == "floor" || e.kind == "pit") continue;
                    var q = e.rb.position;
                    foreach (var mv in movers)
                    {
                        if (mv.routed) continue;
                        var p = mv.ent.rb.position;
                        int cross = mv.axis == 0 ? 2 : 0;
                        if (Mathf.Abs(q[mv.axis] - p[mv.axis]) < 1.6f && Mathf.Abs(q[cross] - p[cross]) < 0.58f && q.y > p.y && q.y < p.y + 0.9f)
                        {
                            q[mv.axis] += mv.v * dt;
                            e.rb.position = q;
                            break;
                        }
                    }
                }
            }
        }

        void UpdateRoutes(float dt)
        {
            if (routed.Count == 0) return;
            routeT += dt;
            foreach (var R in routed)
            {
                R.mx = R.mz = 0;
                if (R.delayT > 0) { R.delayT -= dt; continue; }
                if (R.waitT > 0) { R.waitT -= dt; R.vx = R.vz = 0; SetRoutePos(R, 0, 0); continue; }
                var tgt = R.pts[R.i];
                float dx = tgt.x - R.pos.x, dz = tgt.z - R.pos.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float sp = R.speed;
                if (R.ease)
                {
                    float df = Mathf.Sqrt((R.pos.x - R.legFrom.x) * (R.pos.x - R.legFrom.x) + (R.pos.z - R.legFrom.z) * (R.pos.z - R.legFrom.z));
                    float tt = (df + d) > 1e-4f ? df / (df + d) : 0;
                    sp *= 0.30f + 0.70f * Mathf.Sin(Mathf.PI * tt);
                }
                float step = sp * dt;
                if (d <= step || d < 1e-4f)
                {
                    R.pos.x = tgt.x; R.pos.z = tgt.z;
                    int last = R.pts.Count - 1;
                    if (R.mode == "loop") R.i = (R.i + 1) % R.pts.Count;
                    else if (R.mode == "once") { if (R.i < last) R.i++; else { SetRoutePos(R, dx, dz); continue; } }
                    else { if (R.i + R.dir > last || R.i + R.dir < 0) R.dir *= -1; R.i += R.dir; }
                    R.waitT = R.wait;
                    R.legFrom = R.pos;
                    SetRoutePos(R, dx, dz);
                    continue;
                }
                dx /= d; dz /= d;
                R.pos.x += dx * step; R.pos.z += dz * step;
                SetRoutePos(R, dx, dz);
            }
        }

        void SetRoutePos(Route R, float dx, float dz)
        {
            R.mx = R.pos.x - R.lastX; R.mz = R.pos.z - R.lastZ;
            R.lastX = R.pos.x; R.lastZ = R.pos.z;
            R.vx = dx * R.speed; R.vz = dz * R.speed;
            // 向き：JS版 atan2(dx, dz) - π/2 は three.js の z。Unity の z は反転しているので dz の符号を戻す
            float yawDeg = Mathf.Atan2(dx, -dz) * Mathf.Rad2Deg - 90f;
            if (R.ent != null && R.ent.rb != null)
            {
                R.ent.prevV = new Vector3(R.vx, 0, R.vz);
                R.ent.rb.MovePosition(new Vector3(R.pos.x, R.y, R.pos.z));
                if (R.turn && (dx != 0 || dz != 0)) R.ent.rb.MoveRotation(Quaternion.Euler(0, -yawDeg, 0));
            }
            if (R.mesh != null)
            {
                float by = 0;
                var rot = Quaternion.identity;
                if (R.bob > 0)
                {
                    by = Mathf.Sin(routeT * R.bobW + R.phase) * R.bob;
                    rot = Part.Euler3(Mathf.Cos(routeT * R.bobW * 0.52f + R.phase * 1.7f) * 0.013f, 0,
                                      Mathf.Sin(routeT * R.bobW * 0.73f + R.phase) * 0.017f);
                }
                R.mesh.position = new Vector3(R.pos.x, R.meshY + by, R.pos.z);
                if (R.turn && (dx != 0 || dz != 0)) R.mesh.rotation = Quaternion.Euler(0, -yawDeg, 0);
                else if (R.bob > 0) R.mesh.rotation = rot;
            }
            if (R.riders != null)
                foreach (var m in R.riders)
                {
                    var x = R.pos.x + m.ox; var z = R.pos.z + m.oz;
                    m.ent.rb.MovePosition(new Vector3(x, m.y, z));
                    if (m.counter)
                    {
                        // 受取口はあちこちから位置を見るので、動いたら教える
                        counterPos = new Vector3(x, 0, z);
                        if (kitchen != null) kitchen.position = new Vector3(x, kitchen.position.y, z);
                    }
                }
        }

        /// <summary>乗っている物を「床が動いたぶん」だけ動かす（速度には触らないので、上を自分で歩ける）。</summary>
        void CarryRiders()
        {
            foreach (var R in routed)
            {
                if (R.ent == null) continue;
                if (R.mx == 0 && R.mz == 0) continue;
                var p = R.ent.transform.position;
                if (R.ent.rb != null) p = new Vector3(R.lastX, R.y, R.lastZ);
                foreach (var e in ents)
                {
                    if (e == R.ent || e.isFixed || e.infMass || e.rb == null || e.kind == "floor" || e.kind == "pit") continue;
                    var q = e.rb.position;
                    if (R.raft != null)
                    {
                        bool on = false;
                        foreach (var r in R.raft.rects)
                        {
                            Vector3 c; float w, d; Tiles.RectWorld(r, out c, out w, out d);
                            float rx = c.x - R.raft.cx + R.pos.x - R.mx, rz = c.z - R.raft.cz + R.pos.z - R.mz;
                            if (Mathf.Abs(q.x - rx) <= w / 2 + 0.2f && Mathf.Abs(q.z - rz) <= d / 2 + 0.2f && q.y > -0.2f && q.y < 1.4f) { on = true; break; }
                        }
                        if (!on) continue;
                    }
                    else
                    {
                        float hw = R.ent.size.x / 2 + 0.2f, hd = R.ent.size.z / 2 + 0.2f;
                        var pp = new Vector3(R.pos.x - R.mx, R.y, R.pos.z - R.mz);
                        if (Mathf.Abs(q.x - pp.x) > hw || Mathf.Abs(q.z - pp.z) > hd) continue;
                        if (q.y < pp.y || q.y > pp.y + R.ent.size.y + 0.9f) continue;
                    }
                    e.rb.position = new Vector3(q.x + R.mx, q.y, q.z + R.mz);
                }
            }
        }

        void UpdateWalkers(float dt)
        {
            foreach (var W in walkers)
            {
                var e = W.ent;
                if (W.done || e.hp <= 0 || e.walkStop || e.sunk) continue;
                if (W.delayT > 0) { W.delayT -= dt; continue; }
                if (e.wet > 0) continue;
                if (W.waitT > 0) { W.waitT -= dt; WalkStand(e, W); continue; }
                var p = e.rb.position;
                float ox = 0, oz = 0;
                if (W.raft != null && W.raft.R != null) { ox = W.raft.R.pos.x - W.raft.cx; oz = W.raft.R.pos.z - W.raft.cz; }
                var tgt = W.pts[W.i];
                float dx = (tgt.x + ox) - p.x, dz = (tgt.z + oz) - p.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < 0.25f)
                {
                    int last = W.pts.Count - 1;
                    if (W.mode == "loop") W.i = (W.i + 1) % W.pts.Count;
                    else if (W.mode == "once") { if (W.i < last) W.i++; else W.done = true; }
                    else { if (W.i + W.dir > last || W.i + W.dir < 0) W.dir *= -1; W.i += W.dir; }
                    W.waitT = W.wait;
                    WalkStand(e, W);
                    continue;
                }
                dx /= d; dz /= d;
                var v = e.rb.linearVelocity;
                // 勢いよく押されている間は物理にまかせる（毎フレーム上書きすると、体当たりしても動かない）
                if (new Vector2(v.x, v.z).magnitude <= W.speed * 1.6f)
                    e.rb.linearVelocity = new Vector3(dx * W.speed, v.y, dz * W.speed);
                // 進む向きへ体を向ける（客の前は Unity の +Z）
                e.rb.rotation = Quaternion.LookRotation(new Vector3(dx, 0, dz));
                e.rb.angularVelocity = Vector3.zero;
            }
        }

        static void WalkStand(Ent e, Walker W)
        {
            var v = e.rb.linearVelocity;
            if (new Vector2(v.x, v.z).magnitude <= W.speed * 1.6f) e.rb.linearVelocity = new Vector3(0, v.y, 0);
        }

        // ================================================================ 画面に映っているか
        const float ViewMargin = 0.35f;

        bool InView(Vector3 p, float m)
        {
            if (mainCam == null) return false;
            var vp = mainCam.WorldToViewportPoint(p);
            if (vp.z < 0) return false;
            float nx = vp.x * 2 - 1, ny = vp.y * 2 - 1;
            return Mathf.Abs(nx) <= 1 + m && Mathf.Abs(ny) <= 1 + m;
        }

        /// <summary>乗り物が画面に映っているか。前後の端も見る（電車は4マスある）。</summary>
        bool VehInView(Car car, Vector3 p)
        {
            float half = (Mathf.Abs(car.dir.x) > 0.5f ? car.ent.size.x : car.ent.size.z) / 2 + 0.6f;
            foreach (float t in new[] { -half, 0, half })
                if (InView(p + car.dir * t, ViewMargin)) return true;
            return false;
        }

        // ================================================================ 道路・線路・橋の欄干
        static readonly Vector3Int[] DIRS = { new Vector3Int(0, -1, 1), new Vector3Int(1, 0, 2), new Vector3Int(0, 1, 4), new Vector3Int(-1, 0, 8) };

        int ConnMask(int i, int j, string conn)
        {
            int m = 0;
            foreach (var d in DIRS)
            {
                char ch; var td = map.TryGetValue(new Vector2Int(i + d.x, j + d.y), out ch) ? Tiles.Def(ch) : null;
                if (td != null && td.conn == conn) m |= d.z;
            }
            return m;
        }
        static int ConnCount(int m) { return ((m & 1) != 0 ? 1 : 0) + ((m & 2) != 0 ? 1 : 0) + ((m & 4) != 0 ? 1 : 0) + ((m & 8) != 0 ? 1 : 0); }

        /// <summary>つながる地形の飾り（白線・枕木とレール・橋の欄干）。つながっている向きにだけ描く。</summary>
        void BuildMarks()
        {
            var markRoot = new GameObject("Marks").transform; markRoot.SetParent(stageRoot, false);
            foreach (var kv in map)
            {
                var td = Tiles.Def(kv.Value);
                if (td == null || td.conn == null) continue;
                int ci = kv.Key.x, cj = kv.Key.y;
                var c = Coord.Cell(ci, cj);
                int m = ConnMask(ci, cj, td.conn), n = ConnCount(m);
                if (td.conn == "road") RoadMark(markRoot, c, m, n);
                if (td.conn == "rail") RailMark(markRoot, c, m, n);
                if (td.conn == "bridge") BridgeRail(markRoot, ci, cj, c, m);
            }
        }

        // 位置は three.js の向き（dj が + なら手前）で計算して、z だけ反転して置く
        static void Mk(Transform root, float w, float h, float d, Material m, Vector3 c, float dx, float y, float dz, bool shadow = false)
        {
            Part.Add(root, MeshGen.Box(w, h, d), m, new Vector3(c.x + dx, y, c.z - dz), shadow);
        }

        void RoadMark(Transform root, Vector3 c, int m, int n)
        {
            var mm = Mats.Get(0xe8e8e0);
            bool cross = n >= 3;
            foreach (var d in DIRS)
            {
                if ((m & d.z) == 0) continue;
                bool ax = d.x != 0;
                if (!cross)
                    for (float t = 0.14f; t < 0.5f; t += 0.22f)
                        Mk(root, ax ? 0.14f : 0.09f, 0.01f, ax ? 0.09f : 0.14f, mm, c, d.x * t, 0.012f, d.y * t);
                else
                    for (float s = -0.3f; s <= 0.31f; s += 0.15f)
                        Mk(root, ax ? 0.16f : 0.10f, 0.01f, ax ? 0.10f : 0.16f, mm, c, d.x * 0.38f + (ax ? 0 : s), 0.012f, d.y * 0.38f + (ax ? s : 0));
            }
            if (n == 1)
                foreach (var d in DIRS)
                {
                    if ((m & d.z) != 0) continue;
                    bool ax = d.x != 0;
                    Mk(root, ax ? 0.06f : 0.7f, 0.01f, ax ? 0.7f : 0.06f, mm, c, d.x * 0.44f, 0.012f, d.y * 0.44f);
                }
        }

        void RailMark(Transform root, Vector3 c, int m, int n)
        {
            var tie = Mats.Get(0x5a4a38); var rail = Mats.Get(0x8a8f96);
            var done = new List<bool>();
            foreach (var d in DIRS)
            {
                if ((m & d.z) == 0) continue;
                bool ax = d.x != 0;
                if (done.Contains(ax)) continue;
                done.Add(ax);
                for (float t = -0.36f; t <= 0.37f; t += 0.24f)
                    Mk(root, ax ? 0.12f : 0.62f, 0.05f, ax ? 0.62f : 0.12f, tie, c, ax ? t : 0, 0.028f, ax ? 0 : t);
                foreach (float s in new[] { -0.20f, 0.20f })
                    Mk(root, ax ? 1.0f : 0.06f, 0.06f, ax ? 0.06f : 1.0f, rail, c, ax ? 0 : s, 0.062f, ax ? s : 0);
            }
            if (n == 0) RailMark(root, c, 10, 2);
        }

        /// <summary>橋の欄干。水や穴に面した側にだけ立てる（当たり判定つき。高さ0.5mなのでジャンプなら越えられる）。</summary>
        void BridgeRail(Transform root, int ci, int cj, Vector3 c, int m)
        {
            const float RAIL_AT = 0.47f, RAIL_T = 0.06f;
            var post = Mats.Get(0xc9a27a);
            foreach (var d in DIRS)
            {
                if ((m & d.z) != 0) continue;
                char nb;
                if (map.TryGetValue(new Vector2Int(ci + d.x, cj + d.y), out nb) && (Tiles.IsFloor(nb) || Tiles.IsWall(nb))) continue;
                bool ax = d.x != 0;
                float x = d.x * RAIL_AT, z = d.y * RAIL_AT;
                Mk(root, ax ? RAIL_T : 1.0f, 0.10f, ax ? 1.0f : RAIL_T, post, c, x, 0.40f, z, true);
                foreach (float s in new[] { -0.44f, 0f, 0.44f })
                    Mk(root, RAIL_T, 0.45f, RAIL_T, post, c, x + (ax ? 0 : s), 0.22f, z + (ax ? s : 0), true);
                MakeBody("fence", new Vector3(c.x + x, 0.25f, c.z - z), ax ? new Vector3(RAIL_T / 2, 0.25f, 0.5f) : new Vector3(0.5f, 0.25f, RAIL_T / 2),
                         Quaternion.identity, true, 0, 0f, 0f);
            }
        }

        // ---- 盤面の外へ伸ばす道と線路（見た目だけ）。長さはカメラで決まるので、距離が決まってから作る
        struct Tail { public int ci, cj, di, dj; public string conn; public int n; }
        readonly List<Tail> tails = new List<Tail>();
        Transform tailRoot; string tailSig;

        void FindTails()
        {
            tails.Clear(); tailSig = null;
            foreach (var kv in map)
            {
                var td = Tiles.Def(kv.Value);
                if (td == null || (td.conn != "road" && td.conn != "rail")) continue;
                foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                {
                    if (map.ContainsKey(kv.Key + d)) continue;
                    char back; var bd = map.TryGetValue(kv.Key - d, out back) ? Tiles.Def(back) : null;
                    if (bd == null || bd.conn != td.conn) continue;
                    tails.Add(new Tail { ci = kv.Key.x, cj = kv.Key.y, di = d.x, dj = d.y, conn = td.conn });
                    break;
                }
            }
        }

        void RefreshTails()
        {
            if (tails.Count == 0 || mainCam == null) return;
            const int TAIL_MAX = 44;
            var sig = "";
            for (int k0 = 0; k0 < tails.Count; k0++)
            {
                var t = tails[k0]; int n = TAIL_MAX;
                for (int k = 1; k <= TAIL_MAX; k++)
                {
                    var c = Coord.Cell(t.ci + t.di * k, t.cj + t.dj * k);
                    if (!InView(c + Vector3.up * 0.3f, ViewMargin)) { n = k + 1; break; }
                }
                t.n = n; tails[k0] = t; sig += n + ",";
            }
            if (sig == tailSig) return;
            tailSig = sig;
            if (tailRoot != null) Destroy(tailRoot.gameObject);
            tailRoot = new GameObject("RoadTails").transform; tailRoot.SetParent(stageRoot, false);
            var road = Mats.Get(0x4a4f55); var rail = Mats.Get(0x3a3228);
            foreach (var t in tails)
            {
                int mask = t.di != 0 ? 10 : 5;
                for (int k = 1; k <= t.n; k++)
                {
                    var c = Coord.Cell(t.ci + t.di * k, t.cj + t.dj * k);
                    Part.Add(tailRoot, MeshGen.Box(1, Tiles.FloorH * 2, 1), t.conn == "road" ? road : rail, new Vector3(c.x, -Tiles.FloorH, c.z), false);
                    if (t.conn == "road") RoadMark(tailRoot, c, mask, 2); else RailMark(tailRoot, c, mask, 2);
                }
            }
        }
    }
}
