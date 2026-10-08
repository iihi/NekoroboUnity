using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// ステージの組み立て。JS版 buildStage を写したもの。
    ///
    /// **移してある物**：床・壁（低い／高い／見えない）・板の道・道路・線路・橋・凍りの床・
    ///   水・溶岩・海・穴・受取カウンター・屋台・テーブル・イス・ベンチ・客・開始位置・
    ///   ジャンプ台・場所ごとの床（zones）
    /// **まだの物**：車・動く床・いかだ・ルート（歩く客）・置き物カタログ・壁の飾り・
    ///   まわりの飾り・道の白線。読み込んだときに「未対応」と一度だけログに出す。
    /// </summary>
    public partial class Game
    {
        // ---- 受取カウンター
        public const float CounterW = 0.95f, CounterD = 2.95f, CounterH = 0.92f;
        public const float PickupW = 1.2f, PickupD = 2.9f, PickupOffset = 1.0f;
        public Vector3 counterPos;
        public Quaternion counterRot = Quaternion.identity;
        public Ent counterEnt;
        Transform kitchen;
        readonly List<Transform> counterPlates = new List<Transform>();
        readonly List<Dish> counterPlateDish = new List<Dish>();

        // ---- 盤面の広がり（Unity 座標の xz。z は手前が小さい）
        public struct Box2 { public float x0, x1, z0, z1; }
        public Box2 bounds, fitBox;
        public float wallTop;

        readonly Dictionary<string, PhysicsMaterial> pmats = new Dictionary<string, PhysicsMaterial>();
        readonly HashSet<string> warned = new HashSet<string>();

        PhysicsMaterial PMat(float fric, float rest, bool minFric = false)
        {
            string key = fric + "|" + rest + "|" + minFric;
            PhysicsMaterial m;
            if (pmats.TryGetValue(key, out m)) return m;
            m = new PhysicsMaterial(key)
            {
                dynamicFriction = fric,
                staticFriction = fric,
                bounciness = rest,
                frictionCombine = minFric ? PhysicsMaterialCombine.Minimum : PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Average,
            };
            pmats[key] = m;
            return m;
        }

        /// <summary>
        /// 当たり判定を1つ作る（JS版 makeBody）。half は箱の半分の大きさ。
        /// capsule のときは half.x が半径、half.y が筒の半分の長さ。
        /// </summary>
        /// <summary>models.json にその種類のモデルがあれば、手続きの見た目と差し替える（JS版 applyModel）。</summary>
        void ApplyModel(Ent e, GameObject look, string kind, string want = null)
        {
            float h;
            if (ModelStore.Apply(e.transform, look, kind, shop.n, e.size, out h, want) != null) e.modelH = h;
        }

        Ent MakeBody(string kind, Vector3 pos, Vector3 half, Quaternion rot, bool fix,
                     float mass = 0, float fric = 0.7f, float rest = 0.15f, float angDamp = 0f,
                     bool capsule = false, bool minFric = false, bool kinematic = false)
        {
            var go = new GameObject(kind);
            go.transform.SetParent(stageRoot, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var e = go.AddComponent<Ent>();
            e.kind = kind;
            e.isFixed = fix;
            e.infMass = fix || kinematic;
            Collider col;
            if (capsule)
            {
                var cc = go.AddComponent<CapsuleCollider>();
                cc.radius = half.x; cc.height = (half.y + half.x) * 2f; cc.direction = 1;
                col = cc;
                e.size = new Vector3(half.x * 2, (half.y + half.x) * 2, half.x * 2);
            }
            else
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.size = half * 2f;
                col = bc;
                e.size = half * 2f;
            }
            col.sharedMaterial = PMat(fric, rest, minFric);
            if (!fix)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = mass > 0 ? mass : 1f;
                rb.linearDamping = 0f;
                rb.angularDamping = angDamp;
                rb.isKinematic = kinematic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = kinematic ? CollisionDetectionMode.ContinuousSpeculative
                                                      : CollisionDetectionMode.ContinuousDynamic;
                e.rb = rb;
                e.hasHome = true;
                e.home = pos;
            }
            ents.Add(e);
            return e;
        }

        GameObject Vis(Transform parent, string name = "Vis")
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g;
        }

        void Warn(string what)
        {
            if (warned.Add(what)) Debug.LogWarning("[Nekorobo] まだ移していない物があります（飛ばしました）: " + what);
        }

        bool FloorAt(Vector3 p) { return Tiles.IsFloor(Tiles.At(map, p)); }

        // ================================================================ 組み立て
        void BuildStage()
        {
            var C = stage;
            // ---- 地形
            map = Tiles.ParseTerrain(C);
            int bi0 = int.MaxValue, bi1 = int.MinValue, bj0 = int.MaxValue, bj1 = int.MinValue;
            foreach (var k in map.Keys)
            {
                bi0 = Mathf.Min(bi0, k.x); bi1 = Mathf.Max(bi1, k.x);
                bj0 = Mathf.Min(bj0, k.y); bj1 = Mathf.Max(bj1, k.y);
            }
            if (map.Count == 0) { bi0 = bj0 = -5; bi1 = bj1 = 5; }
            // Unity の z は -j。手前（j が大きい）ほど z が小さい
            bounds = new Box2 { x0 = bi0, x1 = bi1 + 1, z0 = -(bj1 + 1), z1 = -bj0 };

            // カメラに収める範囲：走れる床（道路と線路は除く）。床が無ければ壁も
            {
                int ci0 = int.MaxValue, ci1 = int.MinValue, cj0 = int.MaxValue, cj1 = int.MinValue;
                foreach (bool useWall in cam.fitRoomWalls ? new[] { true } : new[] { false, true })
                {
                    foreach (var kv in map)
                    {
                        var t2 = Tiles.Def(kv.Value);
                        if (t2 == null || !(t2.floor || (useWall && t2.wall && !t2.hidden))) continue;
                        if (t2.conn == "road" || t2.conn == "rail") continue;
                        ci0 = Mathf.Min(ci0, kv.Key.x); ci1 = Mathf.Max(ci1, kv.Key.x);
                        cj0 = Mathf.Min(cj0, kv.Key.y); cj1 = Mathf.Max(cj1, kv.Key.y);
                    }
                    if (ci0 != int.MaxValue) break;
                }
                fitBox = ci0 != int.MaxValue ? new Box2 { x0 = ci0, x1 = ci1 + 1, z0 = -(cj1 + 1), z1 = -cj0 } : bounds;
            }

            float fr = shop.fric;
            var objs = C.objects;

            // ---- 受取カウンター（位置と向きはマップに置いた物から）
            StageObj co = objs.Find(o => o.t == "counter");
            if (co != null)
            {
                float w, d; Vector3 c;
                Tiles.ObjBox(co, out w, out d, out c);
                counterPos = c;
                counterRot = Coord.RotX(co.rot);
            }
            else { counterPos = Coord.W(-7.5f, 0, -2.5f); counterRot = Quaternion.identity; }

            // ---- 出現位置。置いた順に 1P, 2P...
            spawnObjs = objs.FindAll(o => o.t == "spawn");

            // ---- 地形を種類ごとに矩形へまとめて置く
            // いかだのマスはここでは作らない（動くので、まとめて1つのかたまりにする）。
            // いかだの「下」には、すぐ隣の地形を敷く（under）
            BuildUnder();
            var groups = new Dictionary<char, HashSet<Vector2Int>>();
            foreach (var kv in under)
            {
                char ch = kv.Value;
                HashSet<Vector2Int> set;
                if (!groups.TryGetValue(ch, out set)) groups[ch] = set = new HashSet<Vector2Int>();
                set.Add(kv.Key);
            }
            string fDes = C.floorDes, wDes = C.wallDes;
            var floorBase = Mats.Get(0xf3f1ea);
            var wallBase = Mats.Get(0xcfd6de);
            var floorRects = new List<Tiles.Rect>();
            wallTop = 0;
            foreach (var g in groups)
            {
                char ch = g.Key;
                var t = Tiles.Def(ch);
                foreach (var r in Tiles.MergeCells(g.Value))
                {
                    Vector3 c; float w, d;
                    Tiles.RectWorld(r, out c, out w, out d);
                    if (ch == '.')
                    {
                        Slab(c, w, d, 0f, fDes != null ? Designs.Surf(fDes, floorBase, "floor", w, d) : floorBase,
                             "floor", 0.9f * fr, 0.05f);
                        floorRects.Add(r);
                    }
                    else if (ch == '=') Slab(c, w, d, 0f, Mats.Get(0xd98b45, 0.75f), "floor", 0.9f * fr, 0.05f);
                    else if (ch == 'i')
                    {
                        // 凍りの床。当たり判定の摩擦も落とす（ぶつかった家具も一緒に滑る）
                        Slab(c, w, d, 0f, Mats.Get(0xbfe6f2, 0.15f), "floor", 0.05f, 0.05f);
                        var gl = Part.Add(stageRoot, MeshGen.FlatUp(w, d),
                                          Mats.LitTransparent(new Color(0.87f, 0.96f, 1f, 0.35f), 0.05f),
                                          c + new Vector3(0, 0.012f, 0), shadow: false);
                        gl.name = "IceGloss";
                    }
                    else if (ch == 'r') Slab(c, w, d, 0f, Mats.Get(0x4a4f55), "floor", 0.9f * fr, 0.05f);
                    else if (ch == 'l') Slab(c, w, d, 0f, Mats.Get(0x3a3228), "floor", 0.9f * fr, 0.05f);
                    else if (ch == 'b')
                    {
                        // 橋：下に水を敷いてから板を渡す
                        Slab(c, w, d, -Tiles.PondDepth, Mats.Get(0x123c52), "pit", 0.5f, 0.02f);
                        Water(c, w, d, "water", 0.8f, -0.10f);
                        Slab(c, w, d, 0f, Mats.Get(0xa5763f), "floor", 0.9f * fr, 0.05f);
                    }
                    else if (t != null && t.wall)
                    {
                        float wh = t.high ? (C.wallHigh ?? T.wallHigh) : T.wallLow;
                        float wPhys = Mathf.Max(T.wallPhys, wh);
                        MakeBody(t.hidden ? "fence" : "wall", c + new Vector3(0, wPhys / 2f, 0),
                                 new Vector3(w / 2f, wPhys / 2f, d / 2f), Quaternion.identity, true, 0, 0.5f, 0.45f);
                        if (t.hidden) continue;
                        if (wh > wallTop) wallTop = wh;
                        // 見た目は半分の薄さ。床のある側の面へ寄せる
                        Vector3 vc; float vw, vd;
                        ThinWall(r, c, w, d, out vc, out vw, out vd);
                        var wm = wDes != null ? Designs.Surf(wDes, wallBase, "wall", Mathf.Max(w, d), wh) : wallBase;
                        Part.Add(stageRoot, MeshGen.Box(vw, wh, vd), wm, vc + new Vector3(0, wh / 2f, 0), name: "WallVis");
                        if (t.high)
                            Part.Add(stageRoot, MeshGen.Box(vw + 0.12f, 0.09f, vd + 0.12f), Mats.Get(0x9aa4b2),
                                     vc + new Vector3(0, wh + 0.03f, 0), name: "WallCap");
                    }
                    else if (t != null && t.liquid)
                    {
                        float dep = Tiles.DepthOf(ch);
                        Slab(c, w, d, -dep, Mats.Get(ch == '^' ? 0x3a1408 : ch == '@' ? 0x08243a : 0x0e2a3d), "pit", 0.5f, 0.02f);
                        Water(c, w, d, ch == '^' ? "lava" : ch == '@' ? "sea" : "water", 0.82f, Tiles.WaterTop);
                    }
                    // 'o'（穴）は当たり判定を置かない。落ちた物は下の受け皿へ
                }
            }
            BuildPitLook();
            BuildFloorGrid(floorRects);

            // ---- 盤面のまわりを、見えない壁でぐるりと囲う（マスは使わない）
            {
                var B = bounds; float h = T.wallPhys, th = 3f;
                float cx = (B.x0 + B.x1) / 2f, cz = (B.z0 + B.z1) / 2f, hw = (B.x1 - B.x0) / 2f, hd = (B.z1 - B.z0) / 2f;
                System.Action<float, float, float, float> put = (x, z, ex, ez) =>
                    MakeBody("fence", new Vector3(x, h / 2f, z), new Vector3(ex, h / 2f, ez), Quaternion.identity, true, 0, 0.3f, 0.1f);
                put(B.x0 - th / 2, cz, th / 2, hd + th);
                put(B.x1 + th / 2, cz, th / 2, hd + th);
                put(cx, B.z0 - th / 2, hw + th, th / 2);
                put(cx, B.z1 + th / 2, hw + th, th / 2);
            }
            // ---- 落ちたものの受け皿
            {
                float pad = 16;
                MakeBody("pit", new Vector3((bounds.x0 + bounds.x1) / 2f, -4.2f, (bounds.z0 + bounds.z1) / 2f),
                         new Vector3((bounds.x1 - bounds.x0) / 2f + pad, 0.3f, (bounds.z1 - bounds.z0) / 2f + pad),
                         Quaternion.identity, true, 0, 0.4f, 0f);
            }

            BuildCounter(C);
            BuildRafts(fr);

            // ---- 置いた物
            var rr = new Lcg(20251010);
            foreach (var o in objs)
            {
                float bw, bd; Vector3 b;
                Tiles.ObjBox(o, out bw, out bd, out b);
                switch (o.t)
                {
                    case "counter": case "spawn": case "raft": break;
                    case "table":
                    case "table2":
                        {
                            bool lng = o.t == "table2";
                            float hw = (lng ? 1.95f : 0.95f) / 2, hd = 0.95f / 2, th = 0.38f;
                            var tb = MakeBody("table", b + new Vector3(0, th, 0), new Vector3(hw, th, hd), Coord.RotX(o.rot),
                                              false, lng ? 45 : 30, 0.6f * fr, 0.2f, 0.6f);
                            var look = Looks.Table(tb.transform, lng, o.des);
                            // 長テーブルのモデルが無ければテーブルのモデル（JS版と同じ）
                            ApplyModel(tb, look, ModelStore.For(o.t, shop.n) != null ? o.t : "table");
                            furni.Add(tb); objEnt[o] = tb;
                            break;
                        }
                    case "chair":
                        {
                            var chr = MakeBody("chair", b + new Vector3(0, 0.45f, 0), new Vector3(0.22f, 0.45f, 0.22f),
                                               Coord.RotFace(o.rot), false, 8, 0.5f * fr, 0.25f, 0.5f);
                            ApplyModel(chr, Looks.Chair(chr.transform, o.des), "chair");
                            furni.Add(chr); objEnt[o] = chr;
                            break;
                        }
                    case "bench":
                        {
                            // ベンチ（家具）。イスと同じく、ぶつけると飛んで店の損壊になる
                            float W = bw, D = bd, h = 0.8f;
                            bool along = W >= D;
                            var half = along ? new Vector3(W * 0.43f, h * 0.32f, D * 0.3f) : new Vector3(W * 0.3f, h * 0.32f, D * 0.43f);
                            var bn = MakeBody("chair", b + new Vector3(0, half.y, 0), half, Quaternion.identity,
                                              false, 22, 0.5f * fr, 0.25f, 0.5f);
                            BenchLook(bn.transform, half, along);
                            bn.bench = true;
                            furni.Add(bn); objEnt[o] = bn;
                            break;
                        }
                    case "guest":
                        {
                            var gu = MakeBody("guest", b + new Vector3(0, 0.55f, 0), new Vector3(0.26f, 0.26f, 0),
                                              Coord.RotFace(o.rot), false, 55, 0.7f * fr, 0.3f, 0.4f, capsule: true);
                            gu.pri = o.pri;
                            var K = GuestKinds.Find(o.gk) ?? GuestKinds.All[rr.Pick(GuestKinds.All.Count)];
                            var look = Looks.Guest(gu.transform, rr, K, TF.heads);
                            {
                                float hR = 1.04f / (2 * Mathf.Max(2.2f, TF.heads + K.heads));
                                gu.vomit = Vomit(gu.transform, hR, 1.04f / 2 - hR);   // モデルに差し替えても残す
                            }
                            ApplyModel(gu, look, "guest", o.gm);    // gm … エディタで選んだ客のモデル
                            guests.Add(gu); objEnt[o] = gu;
                            break;
                        }
                    case "ramp":
                        BuildRamp(o, b, bw, bd, fr);
                        break;
                    case "car": BuildCar(o, b); break;
                    case "mover": BuildMover(o, b); break;
                    case "deco": BuildDeco(o, b); break;
                    default: if (!BuildProp(o, b, bw, bd)) Warn("置き物「" + o.t + "」"); break;
                }
            }
            BuildRoutes();
            BuildMarks();
            FindTails();

            // ---- ロボ（オーダー数が人数で変わるので、先に作る）
            BuildPlayers(fr);

            // ---- オーダー
            var pool = new List<Ent>(guests);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = (int)System.Math.Floor(rr.Next() * (i + 1));
                var tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }
            // pri の大きい客から（同じ pri の中は混ぜたまま）。JS の sort は安定
            pool = StableSortByPri(pool);
            int oBase = C.orders ?? Mathf.RoundToInt(T.orderCount);
            int oPer = C.ordersPer ?? Mathf.RoundToInt(T.orderAddPer);
            int want = oBase + oPer * (players.Count - 1);
            int n = Mathf.Clamp(want, 1, Mathf.Max(1, guests.Count * 2));
            var menu = Dishes.MenuOf(C);
            orders.Clear();
            if (pool.Count > 0)
                for (int i = 0; i < n; i++)
                    orders.Add(new Order { dish = menu[rr.Pick(menu.Count)], guest = pool[i % pool.Count] });

            if (C.stall) BuildStall(C);
            BuildZones(C);
            BuildScenery(C);
        }

        /// <summary>
        /// まわりの飾り（JS版 buildStageScenery）。水のステージはまわりも水にする。
        /// ステージに scenery:false があれば、地面と背景だけ（置き物を手で並べた面用）。
        /// </summary>
        void BuildScenery(StageCfg C)
        {
            bool sea = false; int liq = 0, all = 0;
            foreach (var ch in map.Values)
            {
                all++;
                var td = Tiles.Def(ch);
                if (td != null && td.deadly) sea = true;
                if (td != null && td.liquid) liq++;
            }
            if (all > 0 && (float)liq / all > 0.5f) sea = true;
            const float depth = 11;
            // 盤面の外へ伸ばした道と線路の帯（three.js の向き）。その上には置き物を並べない
            var clear = new List<Rect>();
            foreach (var t in tails)
            {
                float ax = t.ci + 0.5f, az = t.cj + 0.5f, len = depth + 6, half = 0.75f;
                float x0 = Mathf.Min(ax, ax + t.di * len) - (t.di != 0 ? 0 : half), x1 = Mathf.Max(ax, ax + t.di * len) + (t.di != 0 ? 0 : half);
                float z0 = Mathf.Min(az, az + t.dj * len) - (t.dj != 0 ? 0 : half), z1 = Mathf.Max(az, az + t.dj * len) + (t.dj != 0 ? 0 : half);
                clear.Add(Rect.MinMaxRect(x0, z0, x1, z1));
            }
            bool props = !(C.raw["scenery"] != null && C.raw["scenery"].Type == Newtonsoft.Json.Linq.JTokenType.Boolean && !(bool)C.raw["scenery"]);
            int seed = 90210;
            foreach (var ch in (C.n ?? "")) seed = (seed * 31 + ch) & 0x7fffff;
            seed += Shops.All.IndexOf(shop) * 13;
            var bg = Scenery.Build(stageRoot, bounds.x0, bounds.x1, -bounds.z1, -bounds.z0, depth, seed, shop.n, sea, props, clear);
            if (mainCam != null) mainCam.backgroundColor = bg;
        }

        static List<Ent> StableSortByPri(List<Ent> src)
        {
            var idx = new List<KeyValuePair<int, Ent>>();
            for (int i = 0; i < src.Count; i++) idx.Add(new KeyValuePair<int, Ent>(i, src[i]));
            idx.Sort((a, b) => a.Value.pri != b.Value.pri ? b.Value.pri.CompareTo(a.Value.pri) : a.Key.CompareTo(b.Key));
            var outL = new List<Ent>();
            foreach (var kv in idx) outL.Add(kv.Value);
            return outL;
        }

        /// <summary>上面が topY に来る厚い板。側面がそのまま池や穴の壁になる。</summary>
        void Slab(Vector3 c, float w, float d, float topY, Material m, string kind, float fric, float rest)
        {
            var e = MakeBody(kind, new Vector3(c.x, topY - Tiles.FloorH, c.z), new Vector3(w / 2f, Tiles.FloorH, d / 2f),
                             Quaternion.identity, true, 0, fric, rest);
            var mr = Part.Add(e.transform, MeshGen.Box(w, Tiles.FloorH * 2, d), m, Vector3.zero, shadow: false);
            mr.name = "SlabVis";
        }

        /// <summary>水面（流れの筋の絵がゆっくり流れる）。溶岩は光の影響を受けない色で、海と水は少し透ける。</summary>
        void Water(Vector3 c, float w, float d, string kind, float alpha, float y)
        {
            Texture2D tex;
            if (!flowTex.TryGetValue(kind, out tex)) flowTex[kind] = tex = SurfaceArt.Flow(kind, kind.Length * 7919 + 13);
            var rep = new Vector2(Mathf.Max(1, w / 3.2f), Mathf.Max(1, d / 3.2f));
            Material m;
            if (kind == "lava") { m = Mats.BasicTex(tex); m.SetTextureScale("_BaseMap", rep); }
            else { m = Mats.Textured(tex, new Color(1, 1, 1, alpha), 0.25f, rep); Mats.MakeTransparent(m); }
            flowMats.Add(m);
            var s = Part.Add(stageRoot, MeshGen.FlatUp(w, d), m, new Vector3(c.x, y, c.z), shadow: false);
            s.name = "WaterSurf";
        }
        readonly Dictionary<string, Texture2D> flowTex = new Dictionary<string, Texture2D>();
        readonly List<Material> flowMats = new List<Material>();

        /// <summary>流れを動かす（JS版 updateFlow：offset.x を 0.06/秒ずつ）。</summary>
        void UpdateFlow(float dt)
        {
            foreach (var m in flowMats)
            {
                var o = m.GetTextureOffset("_BaseMap"); o.x -= 0.06f * dt;
                m.SetTextureOffset("_BaseMap", o);
            }
        }

        /// <summary>壁の飾り。壁のマスに置き、向きの三角の側へ半マスぶん寄せて立てる（当たり判定は作らない）。</summary>
        void BuildDeco(StageObj o, Vector3 b)
        {
            var D = SurfaceArt.Deco(o.v);
            var g = SurfaceArt.DecoMesh(stageRoot, D);
            float r = o.rot * Mathf.Deg2Rad;
            float d3x = Mathf.Cos(r), d3z = -Mathf.Sin(r);                 // three.js の向き（dirOf）
            g.transform.position = new Vector3(b.x + d3x * 0.53f, D.y, b.z - d3z * 0.53f);
            g.transform.rotation = Part.Euler3(0, Mathf.Atan2(d3x, d3z), 0);
        }

        /// <summary>壁の見た目を半分の薄さにして、床のある側へ寄せる（JS版 thinWall）。</summary>
        void ThinWall(Tiles.Rect r, Vector3 c, float w, float d, out Vector3 vc, out float vw, out float vd)
        {
            vc = c; vw = w; vd = d;
            int nx = Mathf.Max(1, Mathf.RoundToInt(w)), nz = Mathf.Max(1, Mathf.RoundToInt(d));
            int px = 0, mx = 0, pz = 0, mz = 0;     // ここは three.js の向き（-z＝奥）で数える
            for (int i = 0; i < nx; i++)
            {
                int ii = r.i0 + i;
                if (Tiles.IsFloor(Get(ii, r.j0 - 1))) mz++;
                if (Tiles.IsFloor(Get(ii, r.j1 + 1))) pz++;
            }
            for (int j = 0; j < nz; j++)
            {
                int jj = r.j0 + j;
                if (Tiles.IsFloor(Get(r.i0 - 1, jj))) mx++;
                if (Tiles.IsFloor(Get(r.i1 + 1, jj))) px++;
            }
            bool canZ = d <= 1.01f, canX = w <= 1.01f;
            bool useZ = canZ && (!canX || (pz + mz) >= (px + mx));
            const float THIN = 0.5f;
            if (useZ)
            {
                if (mz > 0 && pz > 0) return;
                vd = d * THIN;
                // three.js の -z（奥＝j が小さい側）は Unity の +z
                if (mz > 0) vc.z = c.z + (d - vd) / 2;
                else if (pz > 0) vc.z = c.z - (d - vd) / 2;
            }
            else if (canX)
            {
                if (mx > 0 && px > 0) return;
                vw = w * THIN;
                if (mx > 0) vc.x = c.x - (w - vw) / 2;
                else if (px > 0) vc.x = c.x + (w - vw) / 2;
            }
        }

        char Get(int i, int j) { char ch; return map.TryGetValue(new Vector2Int(i, j), out ch) ? ch : ' '; }

        /// <summary>穴の中を暗くする（底と、穴でない側の縁の壁）。見た目だけ。</summary>
        void BuildPitLook()
        {
            var m = Mats.Get(0x2e3138, 1f);
            const float depth = 0.86f, top = -0.02f;
            foreach (var kv in map)
            {
                if (kv.Value != 'o') continue;
                var c = Coord.Cell(kv.Key.x, kv.Key.y);
                // 底と4つの縁。箱1つで代用（穴どうしの境目にも面ができるが、上からは見えない）
                Part.Add(stageRoot, MeshGen.Box(0.98f, depth, 0.98f), m, new Vector3(c.x, top - depth / 2f, c.z), shadow: false, name: "PitVis");
            }
        }

        /// <summary>床のマス目の線。床の上にだけ、マスの境目に1本ずつ。</summary>
        void BuildFloorGrid(List<Tiles.Rect> rects)
        {
            if (rects.Count == 0) return;
            var verts = new List<Vector3>(); var tris = new List<int>();
            const float hw = 0.035f / 2f, y = 0.004f;
            System.Action<float, float, float, float> strip = (x0, z0, x1, z1) =>
            {
                int o = verts.Count;
                verts.Add(new Vector3(x0, y, z0)); verts.Add(new Vector3(x0, y, z1));
                verts.Add(new Vector3(x1, y, z1)); verts.Add(new Vector3(x1, y, z0));
                tris.Add(o); tris.Add(o + 1); tris.Add(o + 2); tris.Add(o); tris.Add(o + 2); tris.Add(o + 3);
            };
            foreach (var r in rects)
            {
                Vector3 c; float w, d;
                Tiles.RectWorld(r, out c, out w, out d);
                float x0 = c.x - w / 2, x1 = c.x + w / 2, z0 = c.z - d / 2, z1 = c.z + d / 2;
                for (int i = 0; i <= Mathf.RoundToInt(w); i++) { float x = x0 + i; strip(x - hw, z0, x + hw, z1); }
                for (int j = 0; j <= Mathf.RoundToInt(d); j++) { float z = z0 + j; strip(x0, z - hw, x1, z + hw); }
            }
            var mesh = new Mesh { name = "FloorGrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Part.Add(stageRoot, mesh, Mats.Basic(new Color(0.886f, 0.886f, 0.855f, 0.85f), true, true, false), Vector3.zero, shadow: false, name: "FloorGrid");
        }

        // ---------------------------------------------------------------- 受取カウンター
        void BuildCounter(StageCfg C)
        {
            counterEnt = MakeBody("counter", counterPos + new Vector3(0, CounterH / 2f, 0),
                                  new Vector3(CounterW / 2f, CounterH / 2f, CounterD / 2f), counterRot, true, 0, 0.5f, 0.2f);
            string des = null;
            var co = C.objects.Find(o => o.t == "counter");
            if (co != null) des = co.des;
            var cg = Vis(counterEnt.transform, "Look").transform;
            Part.Add(cg, MeshGen.Box(CounterW, CounterH - 0.1f, CounterD), Designs.Mat(des, Mats.Get(0x6b4230), "counter", "leg"),
                     new Vector3(0, -0.05f, 0));
            Part.Add(cg, MeshGen.Box(CounterW + 0.22f, 0.1f, CounterD + 0.18f), Designs.Mat(des, Mats.Get(0xe9e4d8, 0.55f), "counter", "top", 3),
                     new Vector3(0, CounterH / 2f - 0.05f, 0));
            Part.Add(cg, MeshGen.Box(0.04f, 0.12f, CounterD * 0.94f), Mats.Get(0xb9c0c8, 0.35f), new Vector3(CounterW / 2f + 0.01f, 0.12f, 0));
            ApplyModel(counterEnt, cg.gameObject, "counter");

            // 厨房まわり（カウンターと一緒に回す。ローカル +X が受取面の向き）
            kitchen = new GameObject("Kitchen").transform;
            kitchen.SetParent(stageRoot, false);
            kitchen.SetPositionAndRotation(new Vector3(counterPos.x, 0, counterPos.z), counterRot);
            if (!C.stall)
            {
                var wm = C.wallDes != null ? Designs.Surf(C.wallDes, Mats.Get(0xcfd6de), "wall", 5.0f, 2.6f) : Mats.Get(0xcfd6de);
                Part.Add(kitchen, MeshGen.Box(0.24f, 2.6f, 5.0f), wm, Coord.W(-0.71f, 1.3f, 0), name: "KitchenWall");
                var hatch = Vis(kitchen, "Hatch").transform;
                hatch.localPosition = Coord.W(-0.51f, 1.0f, 0);
                Part.Add(hatch, MeshGen.Box(0.12f, 1.05f, CounterD * 0.8f), Mats.Get(0x181818), Vector3.zero);
                Part.Add(hatch, MeshGen.Box(0.1f, 0.06f, CounterD * 0.7f), Mats.Get(0xb9c0c8, 0.35f), new Vector3(0, 0.52f, 0));
                foreach (float lz in new[] { -1.0f, 0f, 1.0f })
                    Part.Add(hatch, MeshGen.Sphere(0.07f, 10, 8), Mats.Basic(Mats.Hex(0xffb64d)), Coord.W(0, 0.44f, lz), shadow: false);
                // 「受取口 PICK UP」の看板（受取側＝ローカル +X へ向ける）
                var sign = Looks.Sign(kitchen, "受取口  PICK UP", 2.6f, 0.65f, Mats.Hex(0x1b1b1b), Mats.Hex(0xffb64d));
                sign.transform.localPosition = Coord.W(-0.43f, 1.85f, 0);
                sign.transform.localRotation = Part.Euler3(0, Mathf.PI / 2, 0);
            }
            // 天板に並ぶ料理（NEXT の3品）
            counterPlates.Clear(); counterPlateDish.Clear();
            for (int i = 0; i < 3; i++)
            {
                var pg = Vis(kitchen, "Plate" + i).transform;
                pg.localPosition = Coord.W(0.1f, CounterH + 0.03f, -1.1f + i * 1.1f);
                Part.Add(pg, MeshGen.Cylinder(0.17f, 0.15f, 0.03f, 16), Mats.Get(0xfaf7f0, 0.9f), Vector3.zero);
                counterPlates.Add(pg);
                counterPlateDish.Add(null);
            }
            // 受け取り位置を示す床のマーク
            var zone = Part.Add(kitchen, MeshGen.FlatUp(PickupW, PickupD), Mats.Basic(new Color(0.24f, 0.61f, 1f, 0.16f), true, false, false),
                                Coord.W(PickupOffset, 0.008f, 0), shadow: false, name: "PickupZone");
        }

        /// <summary>カウンターに並ぶ料理を NEXT の中身に合わせる。</summary>
        void SyncCounterPlates()
        {
            for (int i = 0; i < counterPlates.Count; i++)
            {
                var o = oi + i < orders.Count ? orders[oi + i] : null;
                var pg = counterPlates[i];
                pg.gameObject.SetActive(o != null);
                if (o != null && counterPlateDish[i] != o.dish)
                {
                    counterPlateDish[i] = o.dish;
                    for (int c = pg.childCount - 1; c >= 1; c--) Destroy(pg.GetChild(c).gameObject);
                    DishLook.Build(pg, o.dish, 0.1f);
                }
            }
        }

        // ---------------------------------------------------------------- 屋台（ステージの stall）
        void BuildStall(StageCfg C)
        {
            var g = Vis(kitchen, "Stall").transform;
            var pm = Mats.Get(0xd9d4ca);
            foreach (float x in new[] { -1.4f, 0.0f })
                foreach (float z in new[] { -1.55f, 1.55f })
                    Part.Add(g, MeshGen.Cylinder(0.05f, 0.05f, 2.25f, 8), pm, Coord.W(x, 1.12f, z));
            // しま模様の日よけ（受取台の上にはかけない＝後ろの半分だけ）
            var tex = new Texture2D(16, 128, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[16 * 128];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 16; x++)
                    px[y * 16 + x] = (y / 16) % 2 == 1 ? new Color32(0xff, 0xf6, 0xea, 255) : new Color32(0xe8, 0x68, 0x2c, 255);
            tex.SetPixels32(px); tex.Apply(false);
            var am = Mats.Textured(tex, Color.white, 0.85f, Vector2.one);
            var roof = Part.Add(g, MeshGen.Box(1.5f, 0.06f, 3.5f), am, Coord.W(-0.75f, 2.3f, 0));
            roof.transform.localRotation = Part.Euler3(0, 0, -0.22f);
            Part.Add(g, MeshGen.Box(0.04f, 0.24f, 3.5f), am, Coord.W(-0.02f, 2.02f, 0));
            // 看板（手前の端に、カメラへ向けて）
            var sign = Looks.Sign(g, C.stallName ?? "クレープ", 1.5f, 0.38f, Mats.Hex(0xfff6ea), Mats.Hex(0xc2410c), Mats.Hex(0xe8682c));
            sign.transform.localPosition = Coord.W(-0.7f, 1.95f, 1.58f);
            // 店主。コックの見た目にひげを足して少し大きく
            var K0 = GuestKinds.Find("chefguest");
            var K = new GuestKind { k = "owner", n = "店主", h = K0.h, w = 1.2f, heads = 0.3f, hair = K0.hair, wear = K0.wear, hat = K0.hat, beard = true, pal = K0.pal };
            var ow = Vis(kitchen, "Owner").transform;
            ow.localPosition = Coord.W(-1.0f, 0.52f, 2.0f);
            ow.localRotation = Part.Euler3(0, -Mathf.PI * 0.75f, 0);
            Looks.Guest(ow, new Lcg(7), K, TF.heads);
            var wp = kitchen.TransformPoint(Coord.W(-1.0f, 0, 2.0f));
            MakeBody("prop", new Vector3(wp.x, 0.55f, wp.z), new Vector3(0.28f, 0.55f, 0.28f), Quaternion.identity, true, 0, 0.5f, 0.2f);
        }

        // ---------------------------------------------------------------- ベンチの見た目（仮）
        void BenchLook(Transform t, Vector3 half, bool along)
        {
            var wood = Mats.Get(0xa5763f, 0.8f);
            var leg = Mats.Get(0x5a4030);
            float L = along ? half.x * 2 / 0.86f : half.z * 2 / 0.86f;
            // 座面と背もたれ（向きは長い方に沿わせる）
            var seat = along ? new Vector3(L, 0.06f, 0.42f) : new Vector3(0.42f, 0.06f, L);
            Part.Add(t, MeshGen.Box(seat.x, seat.y, seat.z), wood, new Vector3(0, -half.y + 0.45f, 0));
            var back = along ? new Vector3(L, 0.36f, 0.05f) : new Vector3(0.05f, 0.36f, L);
            Part.Add(t, MeshGen.Box(back.x, back.y, back.z), wood,
                     along ? new Vector3(0, -half.y + 0.68f, 0.2f) : new Vector3(-0.2f, -half.y + 0.68f, 0));
            foreach (float s in new[] { -0.4f, 0.4f })
            {
                var lp = along ? new Vector3(L * s, -half.y + 0.21f, 0) : new Vector3(0, -half.y + 0.21f, L * s);
                Part.Add(t, MeshGen.Box(0.06f, 0.42f, 0.36f), leg, lp);
            }
        }

        // ---------------------------------------------------------------- ジャンプ台
        void BuildRamp(StageObj o, Vector3 b, float bw, float bd, float fr)
        {
            float L = bw >= bd ? bw : bd;
            float Wd = bw >= bd ? bd : bw;
            float rise = T.rampRise, th = Tiles.RampThick;
            float ang = Mathf.Atan2(rise, L);
            // three.js: qy(yaw=rot) * qz(ang)。ローカル +X が上り
            var q = Coord.RotX(o.rot) * Part.Euler3(0, 0, ang);
            var nrm = q * Vector3.up;
            var c = new Vector3(b.x - nrm.x * th / 2, rise / 2 - nrm.y * th / 2, b.z - nrm.z * th / 2);
            float slope = Mathf.Sqrt(L * L + rise * rise);
            var e = MakeBody("ramp", c, new Vector3(slope / 2, th / 2, Wd / 2), q, true, 0, 0.9f * fr, 0f);
            Part.Add(e.transform, MeshGen.Box(slope, th, Wd), Mats.Get(0x9aa7b8), Vector3.zero);
            var bar = Mats.Get(0xf0c020, 0.7f);
            for (float t2 = -slope / 2 + 0.28f; t2 < slope / 2 - 0.1f; t2 += 0.42f)
                Part.Add(e.transform, MeshGen.Box(0.2f, 0.02f, Wd * 0.9f), bar, new Vector3(t2, th / 2, 0), shadow: false);
        }

        // ---------------------------------------------------------------- 場所ごとの床（zones）
        public class Zone { public int i0, i1, j0, j1; public float fric, drag; }
        public List<Zone> zones = new List<Zone>();

        void BuildZones(StageCfg C)
        {
            zones.Clear();
            if (C.zones == null) return;
            foreach (var t in C.zones)
            {
                var Z = t as Newtonsoft.Json.Linq.JObject; if (Z == null) continue;
                var z = new Zone { i0 = J.I(Z, "i0", 0), i1 = J.I(Z, "i1", 0), j0 = J.I(Z, "j0", 0), j1 = J.I(Z, "j1", 0) };
                var fr = J.FN(Z, "fric");
                if (fr != null) z.fric = fr.Value;
                else { var s = Shops.Find(J.S(Z, "shop")); z.fric = s != null ? s.fric : 1f; }
                z.drag = J.F(Z, "drag", 1f);
                zones.Add(z);
                int w = z.i1 - z.i0 + 1, d = z.j1 - z.j0 + 1;
                string des = J.S(Z, "floorDes");
                var baseM = Mats.NewLit(Mats.Hex(J.S(Z, "col", "#c9b79a")), 0.7f);
                var m = des != null ? Designs.Surf(des, baseM, "floor", w, d) : baseM;
                var c = Coord.W(z.i0 + w / 2f, 0.003f, z.j0 + d / 2f);
                Part.Add(stageRoot, MeshGen.FlatUp(w, d), m, c, shadow: false, name: "Zone");
            }
        }

        Zone ZoneAt(Vector3 p)
        {
            float jz = -p.z;                           // three.js の z（＝マスの j の向き）
            foreach (var Z in zones)
                if (p.x >= Z.i0 && p.x < Z.i1 + 1 && jz >= Z.j0 && jz < Z.j1 + 1) return Z;
            return null;
        }
        public float FricAt(Vector3 p, float baseF) { var Z = ZoneAt(p); return Z != null ? Z.fric : baseF; }
        public float DragAt(Vector3 p) { var Z = ZoneAt(p); return Z != null ? Z.drag : 1f; }
    }
}
