using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// NPC の中身。JS版 NPC_SKILL / NPC_LV と同じ値。
    ///
    /// 性格は1つにまとめた（担当「NPCは1つ。性能は暴走で、走り方だけまじめ寄りに。しっかり走って欲しい」）。
    /// 体当たり・アイテム・寄り道（ram・item・greed）は暴走の値、アクセルの開け方と届ける直前の速さ（gas・care）はまじめの値。
    /// 前の名前（serious / wild / hard）で来ても、この1つに読み替える。かんたんはストーリー用。
    ///
    /// react … 判断の間隔[秒]（道をたどる狙いには遅れを入れない。かんたん・体当たりのときだけ）
    /// aim   … ずれを気にしない幅[rad]（回る速さで合わせる）
    /// wade  … 水・溶岩を突っ切るか。jump … 障害物を跳び越えるか（池から上がるときは、かんたんも跳ぶ）
    /// ram / item / greed … 体当たり・アイテム・寄り道の意欲。gas … 向きのずれがこれより小さいときだけアクセル[rad]
    /// care  … 届ける直前に落とす速さ[m/秒]。lag … 道をたどる狙いにも遅れを入れる（かんたん）
    /// </summary>
    public class NpcLv
    {
        public string k, name, ds;
        public float react = 0.18f, aim = 0.12f, ram, item, greed, gas = 1.1f, care = 1.8f;
        public bool wade = true, jump = true, lag;
        public NpcLv Clone() { return (NpcLv)MemberwiseClone(); }
    }

    public static class NpcLevels
    {
        public static readonly NpcLv[] All =
        {
            new NpcLv { k = "normal", name = "NPC", ram = 0.90f, item = 1.00f, greed = 1.80f, gas = 0.85f, care = 1.2f,
                        ds = "向き直してから走るので店を壊さない。隙があれば体当たりやアイテムで妨害する" },
            new NpcLv { k = "easy", name = "NPC・かんたん", react = 0.34f, aim = 0.55f, wade = false, jump = false, lag = true,
                        ram = 0.00f, item = 0.25f, greed = 0.50f, gas = 0.90f, care = 1.5f,
                        ds = "反応が遅く狙いも粗い。ストーリー用" },
        };

        /// <summary>前の名前で来ても動くように（hard / serious / wild は1つにまとめた NPC）。</summary>
        public static string Canon(string k) { return k == "hard" || k == "serious" || k == "wild" ? "normal" : k; }
        public static bool Known(string k) { k = Canon(k); foreach (var l in All) if (l.k == k) return true; return false; }
        public static NpcLv Get(string k)
        {
            k = Canon(k);
            foreach (var l in All) if (l.k == k) return l;
            return All[0];
        }
    }

    /// <summary>NPC 1台ぶんの頭の中（JS版 newNpc）。</summary>
    public class NpcBrain
    {
        public NpcLv lv, lvDry;
        public List<Vector2Int> path; public int node, aimK;
        public float replan, think, stuckT, backT, ramT, ramHold, itemT;
        public Vector3 lastPos;
        public bool turnBack, waiting;
        public Vector2Int? goalCell;
        public Vector3? lastAim;
        public Player ramTarget, hunt;
        public bool wantCr;                  // クレーンを呼びたい（水から上がれない・長く詰まった）
        public float wetT, idleT;
        public bool dry;                     // 岸へ跳び上がれないので水を避けて走っている
        public Vector3? anc; public float ancT;
        public readonly List<Vector4> jams = new List<Vector4>();          // x, z, 時刻
        public readonly Dictionary<Vector2Int, float> avoidCells = new Dictionary<Vector2Int, float>();
        public Dictionary<Vector2Int, float> extra;
        public float xte; public bool xteDrop, lined, rampRun;
        public Vector3? waitSpot; public Transform waitSpotG;
        public float pinT, yieldT, pickT; public bool yieldConf, hadCarry;
        public float hopT; public object hopTo; public string hopWhy;

        public NpcBrain(NpcLv lv) { this.lv = lv; }
    }

    public partial class Game
    {
        /// <summary>動く床・いかだが通るマス（待てば渡れる）。NPC の道探し用。</summary>
        public readonly HashSet<Vector2Int> moverLanes = new HashSet<Vector2Int>();
        /// <summary>動かない置き物のマス（木箱・柵など。地形には出てこない）。</summary>
        public readonly HashSet<Vector2Int> propBlock = new HashSet<Vector2Int>();
        System.Random npcRnd = new System.Random(20251115);
        float Rnd() { return (float)npcRnd.NextDouble(); }

        static Vector2Int CellOf(Vector3 p) { return new Vector2Int(Coord.ToI(p.x), Coord.ToJ(p.z)); }
        static Vector3 CellC(Vector2Int c) { return Coord.Cell(c.x, c.y); }
        static float Dist2(Vector3 a, Vector3 b) { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }
        static float Wrap(float a) { while (a > Mathf.PI) a -= 2 * Mathf.PI; while (a < -Mathf.PI) a += 2 * Mathf.PI; return a; }
        /// <summary>JS版の向き（左 = +）で、前から見た target の角度。</summary>
        static float AngTo(Vector3 p, Vector3 fwd, float tx, float tz) { return Wrap(-(Mathf.Atan2(tx - p.x, tz - p.z) - Mathf.Atan2(fwd.x, fwd.z))); }
        /// <summary>JS版の向き（左 = +）の回る速さ。</summary>
        static float WNow(Ent R) { return -R.rb.angularVelocity.y; }
        static Vector3 FwdOf(Ent R) { var f = R.rb.rotation * Vector3.forward; f.y = 0; return f.normalized; }

        // 角や届け先の手前で速さを落とすときに見込む減速[m/秒²]
        const float NPC_DECEL = 6;
        // クレーン（救済）を呼ぶまでの秒数。水に浸かったまま上がれない／1m も進めないまま
        const float NPC_CR_WET = 5, NPC_CR_IDLE = 15;
        // 池・川から岸へ跳び上がれるか。ジャンプの頂点がこの高さ[m]に届けば上がれる
        const float NPC_CLIMB_H = 0.3f;
        bool NpcCanClimb(Player P) { float v = EffJumpSpd(P); return v * v / (2 * 9.81f) >= NPC_CLIMB_H; }

        // ================================================================ 道を引く
        /// <summary>マスの通りやすさ。通れないマスは null（JS版 tileCost）。</summary>
        float? TileCost(Vector2Int c, bool wade)
        {
            char ch;
            if (!map.TryGetValue(c, out ch)) return null;
            var t = Tiles.Def(ch);
            if (t == null) return null;
            if (t.floor) return (ch == 'r' || ch == 'l') ? 3.0f : (t.slippery ? 2.4f : 1.0f);
            if (t.deadly) return null;                       // 海は入らない
            if (t.liquid) return wade ? (t.burn ? 14f : 7f) : (float?)null;
            return null;                                      // 壁・穴・空白
        }

        static readonly Vector2Int[] DIR4 = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        /// <summary>A*（JS版 findPath）。動く床の帯は待てば渡れるので高いコスト（15）。置き物のマスは通れない（行き先は除く）。</summary>
        List<Vector2Int> FindPath(Vector2Int from, Vector2Int to, bool wade, Dictionary<Vector2Int, float> extra = null)
        {
            if (from == to) return new List<Vector2Int> { to };
            System.Func<Vector2Int, float> h = c => Mathf.Abs(c.x - to.x) + Mathf.Abs(c.y - to.y);
            var open = new List<KeyValuePair<Vector2Int, float>>();
            var gOf = new Dictionary<Vector2Int, float>();
            var came = new Dictionary<Vector2Int, Vector2Int>();
            open.Add(new KeyValuePair<Vector2Int, float>(from, h(from)));
            gOf[from] = 0;
            int guard = 0;
            while (open.Count > 0 && guard++ < 4000)
            {
                int bi = 0;
                for (int x = 1; x < open.Count; x++) if (open[x].Value < open[bi].Value) bi = x;
                var cur = open[bi].Key;
                float cg = gOf[cur];
                open.RemoveAt(bi);
                if (cur == to)
                {
                    var path = new List<Vector2Int>();
                    var k = to;
                    while (k != from) { path.Add(k); k = came[k]; }
                    path.Reverse();
                    return path;
                }
                foreach (var d in DIR4)
                {
                    var nk = cur + d;
                    var c = TileCost(nk, wade);
                    if (c == null && moverLanes.Contains(nk)) c = 15f;
                    if (nk != to && propBlock.Contains(nk)) c = null;
                    if (c == null) continue;
                    float ex;
                    if (extra != null && nk != to && extra.TryGetValue(nk, out ex)) c += ex;
                    float ng = cg + c.Value;
                    float old;
                    if (gOf.TryGetValue(nk, out old) && old <= ng) continue;
                    gOf[nk] = ng; came[nk] = cur;
                    open.Add(new KeyValuePair<Vector2Int, float>(nk, ng + h(nk)));
                }
            }
            return null;
        }

        // ---- 面ごとに覚えておくもの（面を組み直したら作り直す）
        Transform npcCacheFor;
        HashSet<Vector2Int> npcNearDropC, rampCellsC;
        readonly Dictionary<Vector2Int, int> npcTightMemo = new Dictionary<Vector2Int, int>();
        void NpcCacheCheck()
        {
            if (npcCacheFor == stageRoot) return;
            npcCacheFor = stageRoot;
            npcNearDropC = null; rampCellsC = null; npcTightMemo.Clear();
        }

        /// <summary>ジャンプ台の載っているマス（NPC の道探しで避ける。料理を持って乗ると、当たった衝撃で料理が崩れる）。</summary>
        HashSet<Vector2Int> RampCells()
        {
            NpcCacheCheck();
            if (rampCellsC != null) return rampCellsC;
            var s = new HashSet<Vector2Int>();
            foreach (var e in ents)
            {
                if (e == null || e.kind != "ramp") continue;
                var cols = e.GetComponentsInChildren<Collider>();
                if (cols.Length == 0) continue;
                var b = cols[0].bounds; foreach (var c in cols) b.Encapsulate(c.bounds);
                for (float x = b.min.x + Coord.Tile / 2; x < b.max.x; x += Coord.Tile)
                    for (float z = b.min.z + Coord.Tile / 2; z < b.max.z; z += Coord.Tile)
                        s.Add(CellOf(new Vector3(x, 0, z)));
            }
            return rampCellsC = s;
        }

        /// <summary>
        /// NPC の道探しで「通りにくいマス」（JS版 npcAvoidMap）。家具のマスと、そのまわり1マス（3割）。
        /// 置き物（柵・木箱など）のとなりも少し（1.5）。家具は動くので 0.25秒ごとに数え直す。
        /// </summary>
        Dictionary<Vector2Int, float> avoidMap; float avoidT = -1;
        Dictionary<Vector2Int, float> NpcAvoidMap()
        {
            float k = T.npcAvoid;
            if (k <= 0) return null;
            if (avoidMap != null && t - avoidT < 0.25f && t >= avoidT) return avoidMap;
            var m = new Dictionary<Vector2Int, float>();
            float rim = k * 0.3f;
            foreach (var e in furni)
            {
                if (e == null || e.sunk || e.rb == null) continue;
                var q = e.rb.position;
                if (q.y < -0.8f) continue;                         // 池や穴の底に沈んでいる物は数えない
                float r = Mathf.Max(e.size.x > 0 ? e.size.x : 0.6f, e.size.z > 0 ? e.size.z : 0.6f) / 2;
                int i0 = Coord.ToI(q.x - r + 0.05f), i1 = Coord.ToI(q.x + r - 0.05f);
                int j0 = Coord.ToJ(q.z + r - 0.05f), j1 = Coord.ToJ(q.z - r + 0.05f);     // Unity の z は向きが逆
                for (int i = i0 - 1; i <= i1 + 1; i++)
                    for (int j = j0 - 1; j <= j1 + 1; j++)
                    {
                        bool inside = i >= i0 && i <= i1 && j >= j0 && j <= j1;
                        float v = inside ? k : rim;
                        if (v <= 0) continue;
                        var key = new Vector2Int(i, j);
                        float old;
                        if (!m.TryGetValue(key, out old) || old < v) m[key] = v;
                    }
            }
            // 置き物のとなりも少し通りにくくする（1マス幅のすき間を、きわを通って柵の角に引っかかっていた）
            foreach (var b in propBlock)
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        if (di == 0 && dj == 0) continue;
                        var kk = new Vector2Int(b.x + di, b.y + dj);
                        float old;
                        if (!m.TryGetValue(kk, out old) || old < 1.5f) m[kk] = 1.5f;
                    }
            avoidMap = m; avoidT = t;
            return m;
        }

        // ---- 動く床。t 秒後の中心（往復の折り返しも見る。ルートで動く床は今の速さのまま）
        Vector3 MoverAt(Mover m, float tt)
        {
            var q = m.ent.rb.position;
            if (m.routed) { var v0 = m.ent.prevV; return new Vector3(q.x + v0.x * tt, 0, q.z + v0.z * tt); }
            int ax = m.axis; float R = m.range;
            float s = (q[ax] - m.home[ax]) + m.v * tt;
            if (s > R) s = 2 * R - s; else if (s < -R) s = -2 * R - s;
            var c = new Vector3(q.x, 0, q.z); c[ax] = m.home[ax] + s;
            return c;
        }
        // (x, z) が t 秒後に動く床 m の上か。ma … 長い辺のふちから空ける幅、mc … 短い辺のふちから空ける幅
        bool MoverHolds(Mover m, Vector3 p, float tt, float ma, float mc)
        {
            var c = MoverAt(m, tt); int ax = m.axis, cr = ax == 0 ? 2 : 0;
            return Mathf.Abs(p[ax] - c[ax]) < 1.45f - ma && Mathf.Abs(p[cr] - c[cr]) < 0.45f - mc;
        }
        // その点を通る動く床の帯（1軸で往復するものだけ）
        Mover MoverStripOf(Vector3 p)
        {
            foreach (var m in movers)
            {
                if (m.routed) continue;
                var q = m.ent.rb.position; int ax = m.axis, cr = ax == 0 ? 2 : 0;
                if (Mathf.Abs(p[cr] - q[cr]) < 0.5f && Mathf.Abs(p[ax] - m.home[ax]) < m.range + 1.45f) return m;
            }
            return null;
        }

        /// <summary>
        /// そこに乗れるか（JS版 npcCanStand）。動く床の上も「乗れる」として数える。
        /// 溶岩（burn）は、自分がもう浸かっているとき（inLava）だけ数える（数えていたころは、溶岩の手前で止まらずに入っていた）。
        /// </summary>
        bool NpcCanStand(Vector3 p, NpcLv lv, bool inLava = false)
        {
            var t0 = Tiles.Def(Tiles.At(map, p));
            if (t0 != null && (t0.floor || (lv.wade && t0.liquid && !t0.deadly && (!t0.burn || inLava)))) return true;
            foreach (var mv in movers)
            {
                if (mv.routed) { var q = mv.ent.rb.position; if (Mathf.Abs(p.x - q.x) < 1.5f && Mathf.Abs(p.z - q.z) < 0.5f) return true; continue; }
                if (MoverHolds(mv, p, 0, 0.25f, -0.05f)) return true;
            }
            return false;
        }

        // 落ちる所（穴・海・水・溶岩・何も無い所。壁は除く）のとなりのマス。面ごとに1回だけ数える
        HashSet<Vector2Int> NpcNearDrop()
        {
            NpcCacheCheck();
            if (npcNearDropC != null) return npcNearDropC;
            var s = new HashSet<Vector2Int>();
            foreach (var kv in map)
            {
                var td = Tiles.Def(kv.Value); var c = kv.Key;
                if (td != null && (td.floor || td.wall))
                {
                    for (int di = -1; di <= 1; di++) for (int dj = -1; dj <= 1; dj++)
                            if (!map.ContainsKey(new Vector2Int(c.x + di, c.y + dj))) s.Add(c);
                    continue;
                }
                for (int di = -1; di <= 1; di++) for (int dj = -1; dj <= 1; dj++) s.Add(new Vector2Int(c.x + di, c.y + dj));
            }
            return npcNearDropC = s;
        }

        // ルートで動く置き物（迷路の扉など）がいま居るマス。1フレームに1回だけ数える
        HashSet<Vector2Int> npcMoverC; float npcMoverT = -1;
        HashSet<Vector2Int> NpcMoverCells()
        {
            if (npcMoverC != null && npcMoverT == t) return npcMoverC;
            var s = new HashSet<Vector2Int>();
            foreach (var r in routed)
            {
                var e = r.ent;
                if (e == null || e.kind != "prop" || e.rb == null) continue;
                var q = e.rb.position;
                float hx = (e.size.x > 0 ? e.size.x / 2 : 0.5f) - 0.15f, hz = (e.size.z > 0 ? e.size.z / 2 : 0.5f) - 0.15f;
                for (int i = Coord.ToI(q.x - hx); i <= Coord.ToI(q.x + hx); i++)
                    for (int j = Coord.ToJ(q.z + hz); j <= Coord.ToJ(q.z - hz); j++) s.Add(new Vector2Int(i, j));
            }
            npcMoverT = t; npcMoverC = s;
            return s;
        }
        // まわり8マスの込み具合。2 … 置き物か落ちる所、1 … 壁だけ、0 … 何も無い
        int NpcTight(Vector2Int c0)
        {
            NpcCacheCheck();
            int r;
            if (npcTightMemo.TryGetValue(c0, out r)) return r;
            r = 0;
            for (int di = -1; di <= 1 && r < 2; di++)
                for (int dj = -1; dj <= 1; dj++)
                {
                    if (di == 0 && dj == 0) continue;
                    var kk = new Vector2Int(c0.x + di, c0.y + dj);
                    char ch; var td = map.TryGetValue(kk, out ch) ? Tiles.Def(ch) : null;
                    if (propBlock.Contains(kk) || td == null || (!td.floor && !td.wall)) { r = 2; break; }
                    if (!td.floor) r = 1;
                }
            npcTightMemo[c0] = r;
            return r;
        }
        bool NpcRampNear(Vector2Int c)
        {
            var rc = RampCells();
            if (rc.Count == 0) return false;
            for (int di = -1; di <= 1; di++) for (int dj = -1; dj <= 1; dj++)
                    if (rc.Contains(new Vector2Int(c.x + di, c.y + dj))) return true;
            return false;
        }
        // 狭いマスか（左右、または上下の両側が、置き物か壁か落ちる所）
        bool NpcNarrow(Vector2Int c)
        {
            System.Func<int, int, bool> bl = (a, b) =>
            {
                var kk = new Vector2Int(a, b); char ch;
                var td = map.TryGetValue(kk, out ch) ? Tiles.Def(ch) : null;
                return propBlock.Contains(kk) || td == null || !td.floor;
            };
            return (bl(c.x + 1, c.y) && bl(c.x - 1, c.y)) || (bl(c.x, c.y + 1) && bl(c.x, c.y - 1));
        }

        // from から to まで、まっすぐ走って大丈夫か。ロボの幅（左右 0.35m）ぶんも見る
        bool NpcLineClear(Vector3 from, Vector3 to, Dictionary<Vector2Int, float> extra, NpcLv lv)
        {
            float dx = to.x - from.x, dz = to.z - from.z, L = Mathf.Sqrt(dx * dx + dz * dz);
            if (L < 0.01f) return true;
            float ux = dx / L, uz = dz / L;
            var nd = NpcNearDrop(); var mc = NpcMoverCells();
            for (float s = 0.25f; s < L; s += 0.25f)
                foreach (var o in new[] { -0.35f, 0, 0.35f })
                {
                    var q = new Vector3(from.x + ux * s - uz * o, 0, from.z + uz * s + ux * o);
                    var kk = CellOf(q);
                    var tc = TileCost(kk, false);
                    if (tc == null || tc.Value != 1.0f) return false;
                    if (propBlock.Contains(kk)) return false;
                    float ex; if (extra != null && extra.TryGetValue(kk, out ex) && ex > 0) return false;
                    if (nd.Contains(kk) || mc.Contains(kk)) return false;
                    if (!NpcCanStand(q, lv, false)) return false;
                }
            return true;
        }

        // 受取口が空くのを待つ位置。受取口から 2.6m、機体ごとの向き（ang）から順に、立てる床が見つかる向きを探す
        Vector3? NpcWaitSpot(Player P, float ang)
        {
            var N = P.npc;
            if (N.waitSpot != null && N.waitSpotG == stageRoot) return N.waitSpot;
            var pk = PickupPoint();
            for (int k = 0; k < 12; k++)
            {
                float a = ang + (k % 2 == 1 ? 1 : -1) * Mathf.Ceil(k / 2f) * (Mathf.PI / 6);
                var q = new Vector3(pk.x + Mathf.Cos(a) * 2.6f, 0, pk.z + Mathf.Sin(a) * 2.6f);
                var kk = CellOf(q); var tc = TileCost(kk, false);
                if (tc == null || tc.Value != 1.0f || propBlock.Contains(kk)) continue;
                if (!NpcCanStand(q, N.lv, false)) continue;
                N.waitSpot = q; N.waitSpotG = stageRoot;
                return q;
            }
            return null;
        }
        // その場で target のほうを向く（動いていたら止める）
        void NpcFace(Player P, Vector3 target, ref BotInput K)
        {
            var R = P.ent; var p = R.rb.position; var fwd = FwdOf(R);
            float a = AngTo(p, fwd, target.x, target.z) - WNow(R) * 0.12f;
            if (a > 0.3f) K.left = true;
            if (a < -0.3f) K.right = true;
            var v = R.rb.linearVelocity; float vf = v.x * fwd.x + v.z * fwd.z;
            if (vf > 0.4f) K.down = true; else if (vf < -0.4f) K.up = true;
            P.npc.stuckT = 0;                     // 止まって待っているのは「詰まった」ではない
            P.npc.anc = null;                     // 長く動けない（クレーン）とも数えない
        }

        // ---- 動く床の乗り継ぎ（JS版 npcLaneHop）。乗り継ぎをこちらで決めたら true
        bool NpcLaneHop(Player P, NpcBrain N, ref BotInput K, Vector3 p)
        {
            if (movers.Count == 0 || N.path == null || N.node >= N.path.Count) { N.hopT = 0; return false; }
            Mover onM = movers.Find(m => !m.routed && MoverHolds(m, p, 0, -0.05f, -0.1f));
            Vector3? c = null; Mover m2 = null;
            for (int k = N.node; k < Mathf.Min(N.path.Count, N.node + 8); k++)
            {
                var cc = CellC(N.path[k]); var sm = MoverStripOf(cc);
                if (onM != null) { if (sm == onM) continue; c = cc; m2 = sm; break; }
                if (sm != null) { c = cc; m2 = sm; break; }
            }
            if (c == null || (onM == null && m2 == null)) { N.hopT = 0; N.hopWhy = "none"; return false; }
            var R = P.ent; var fwd = FwdOf(R); var vel = R.rb.linearVelocity;
            if (onM == null)
            {
                var mq = m2.ent.rb.position; int ax = m2.axis, cr = ax == 0 ? 2 : 0;
                if (Mathf.Abs(p[cr] - mq[cr]) - 0.45f > 3.5f) { N.hopT = 0; return false; }
                // どこで横切るか：この先に続けて横切る帯が、どれも来る範囲の中で、いまの位置にいちばん近い所
                float lo = float.NegativeInfinity, hi = float.PositiveInfinity; Mover last = null;
                for (int k = N.node; k < Mathf.Min(N.path.Count, N.node + 10); k++)
                {
                    var cc = CellC(N.path[k]); var sm = MoverStripOf(cc);
                    if (sm == null) { if (last != null) break; continue; }
                    if (sm == last || sm.axis != m2.axis) continue;
                    last = sm;
                    float hh = sm.home[ax], w = sm.range + 1.45f - 0.6f;
                    lo = Mathf.Max(lo, hh - w); hi = Mathf.Min(hi, hh + w);
                }
                if (lo > hi) { float hh = m2.home[ax], w = m2.range + 1.45f - 0.6f; lo = hh - w; hi = hh + w; }
                float al = p[ax], want = Mathf.Clamp(al, lo, hi);
                if (Mathf.Abs(want - al) > 0.35f)
                {
                    // 帯の手前の床を、帯に沿って横切る所まで走る
                    float sg = Mathf.Sign(mq[cr] - p[cr]); if (sg == 0) sg = 1;
                    float wc = p[cr];
                    for (int k = 0; k < 40; k++)
                    {
                        float q = wc + sg * 0.1f; var tp = Vector3.zero; tp[ax] = want; tp[cr] = q;
                        var td = Tiles.Def(Tiles.At(map, tp));
                        if (td == null || !td.floor) break;
                        wc = q;
                    }
                    wc -= sg * 0.6f;
                    var W = Vector3.zero; W[ax] = want; W[cr] = wc;
                    float a = AngTo(p, fwd, W.x, W.z) - WNow(R) * 0.12f;
                    if (a > 0.2f && P.broken != "left") K.left = true;
                    if (a < -0.2f && P.broken != "right") K.right = true;
                    float vf = vel.x * fwd.x + vel.z * fwd.z, dW = Dist2(W, p);
                    float vd = Mathf.Min(2.8f, Mathf.Sqrt(2 * 4 * dW));
                    float vToward = vel[cr] * sg;
                    if (vToward > 0.6f && vf > 0.2f) K.down = true;
                    else if (Mathf.Abs(a) < 0.6f && vf < vd - 0.3f) K.up = true;
                    else if (vf > vd + 0.4f || (Mathf.Abs(a) > 1.2f && vf > 1.0f)) K.down = true;
                    N.stuckT = 0; N.hopT = 0; N.hopWhy = "slide";
                    return true;
                }
            }
            var refM = m2 ?? onM; int crs = refM.axis == 0 ? 2 : 0;        // 横切る軸（帯と直角）
            float tc = m2 != null ? m2.ent.rb.position[crs] : c.Value[crs];
            float d = tc - p[crs], sgn = Mathf.Sign(d); if (sgn == 0) sgn = 1;
            // 渡ってよいか。着くまでの間も、いま乗っている床に運ばれて流れるので、着く位置はそのぶん先を見る
            float ta = Mathf.Abs(d) / 2.2f + 0.25f;
            var Ta = new Vector3(p.x + vel.x * ta, 0, p.z + vel.z * ta); Ta[crs] = tc;
            bool ready;
            if (m2 != null) ready = MoverHolds(m2, Ta, ta, 0.55f, 0.05f);
            else { var td = Tiles.Def(Tiles.At(map, Ta)); ready = td != null && td.floor && !propBlock.Contains(CellOf(Ta)); }
            object tgt = m2 != null ? (object)m2 : ("f" + CellOf(c.Value));
            if (!Equals(N.hopTo, tgt)) { N.hopTo = tgt; N.hopT = 0; }
            if (N.hopT > t) ready = true;
            var dir = Vector3.zero; dir[crs] = sgn;
            float a2 = AngTo(p, fwd, p.x + dir.x, p.z + dir.z) - WNow(R) * 0.12f;
            if (a2 > 0.2f && P.broken != "left") K.left = true;
            if (a2 < -0.2f && P.broken != "right") K.right = true;
            bool facing = Mathf.Abs(a2) < 0.45f;
            float vc = vel[crs] * sgn, goal;
            bool leaving = onM != null ? Mathf.Abs(p[crs] - onM.ent.rb.position[crs]) > 0.25f : Mathf.Abs(d) - 0.45f < 0.35f;
            if (ready)
            {
                goal = Mathf.Abs(d);
                if (leaving && vc > 0.3f && Mathf.Abs(d) > 0.25f) N.hopT = t + 0.4f;   // ふちを越えたら、着くまで続ける
            }
            else
            {
                float wc;
                if (onM != null) wc = onM.ent.rb.position[crs];
                else
                {
                    float e = p[crs];
                    for (int k = 0; k < 40; k++)
                    {
                        float q = e + sgn * 0.1f; var tp = p; tp[crs] = q;
                        var td = Tiles.Def(Tiles.At(map, tp));
                        if (td == null || !td.floor) break;
                        e = q;
                    }
                    wc = e - sgn * 0.6f;
                }
                goal = (wc - p[crs]) * sgn;
                N.stuckT = 0;
                if (onM == null) N.anc = null;
            }
            float vd2 = Mathf.Sign(goal) * Mathf.Min(2.6f, Mathf.Sqrt(2 * 4 * Mathf.Abs(goal)));
            float vfw = vel.x * fwd.x + vel.z * fwd.z;
            if (facing)
            {
                if (vc < vd2 - 0.25f) K.up = true;
                else if (vc > vd2 + 0.3f) K.down = true;
            }
            else if (onM == null && vc > Mathf.Max(0, vd2) + 0.3f && vfw > 0.3f) K.down = true;
            N.hopWhy = (ready ? "go" : "wait") + (onM != null ? ":M" : ":F") + (m2 != null ? ">m" : ">f");
            return true;
        }

        // ---- 道探しに教えるマス（組み立てのときに呼ぶ）
        /// <summary>動く床が通るマス。床の長さ（3マス）ぶんを、動く向きに沿って数える。</summary>
        void AddMoverLanes(Vector3 b, int axis, float range)
        {
            for (float s = -range; s <= range; s += 0.5f)
            {
                float mx = b.x + (axis == 0 ? s : 0), mz = b.z + (axis == 2 ? s : 0);
                for (int w = -1; w <= 1; w++)
                    moverLanes.Add(CellOf(new Vector3(mx + (axis == 0 ? w : 0), 0, mz + (axis == 2 ? w : 0))));
            }
        }

        /// <summary>いかだの通り道（JS版 sweepRaftLanes）。</summary>
        void SweepRaftLanes(Raft rf, Route R)
        {
            System.Action<float, float> add = (ox, oz) =>
            {
                int di = Mathf.RoundToInt(ox / Coord.Tile), dj = Mathf.RoundToInt(-oz / Coord.Tile);
                foreach (var c in rf.cells) moverLanes.Add(new Vector2Int(c.x + di, c.y + dj));
            };
            int last = R.pts.Count - 1;
            for (int n = 0; n <= last; n++)
            {
                if (R.mode != "loop" && n == last) break;
                var a = R.pts[n]; var b2 = R.pts[(n + 1) % R.pts.Count];
                float len = new Vector2(b2.x - a.x, b2.z - a.z).magnitude;
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / (Coord.Tile * 0.5f)));
                for (int s = 0; s <= steps; s++)
                {
                    float u = s / (float)steps;
                    add(a.x + (b2.x - a.x) * u - rf.cx, a.z + (b2.z - a.z) * u - rf.cz);
                }
            }
        }

        // ================================================================ 1台ぶんの判断
        /// <summary>NPC 1台ぶんの判断（JS版 driveNpc）。毎刻み呼んで、人間と同じボタンだけを P.input に入れる。</summary>
        void DriveNpc(Player P, float dt)
        {
            var N = P.npc;
            var K = new BotInput();
            P.input = K;
            // 岸へ跳び上がれないので水を避けて走っているときは、水を「乗れる所」と数えない
            NpcLv lv = N.lv;
            if (N.dry) { if (N.lvDry == null) { N.lvDry = N.lv.Clone(); N.lvDry.wade = false; } lv = N.lvDry; }
            if (state != "play") return;
            DriveNpcIn(P, N, lv, dt, ref K);
            P.input = K;
        }

        void DriveNpcIn(Player P, NpcBrain N, NpcLv lv, float dt, ref BotInput K)
        {
            var R = P.ent; var p = R.rb.position;
            var pk = PickupPoint();

            // ---- 救済のクレーン。水から上がれない・長く動けないときは、4つ長押しを押し続けて呼ぶ
            if (N.wantCr) { K.rescue = true; return; }

            // ---- 池・川に落ちたら、まず近い岸のほうを向いて、前へ出しながら跳んで上がる（かんたんも跳ぶ）
            {
                float foot = p.y - (R.size.y > 0 ? R.size.y / 2 : 0.5f);
                var wt = Tiles.Def(Tiles.At(under ?? map, p));
                if (foot < -0.06f && wt != null && wt.liquid && !wt.deadly && !(rafts.Count > 0 && OverRaftNow(p)))
                {
                    N.wetT += dt; N.anc = null;
                    // しばらく上がれなければクレーン。跳んでも岸に届かない（ジャンプが低い）なら、すぐ呼ぶ
                    if (N.wetT > (NpcCanClimb(P) ? NPC_CR_WET : 1.2f)) { N.wantCr = true; return; }
                    var s = Tiles.NearestFloor(map, p);
                    if (s != null)
                    {
                        var fw = FwdOf(R);
                        float a = AngTo(p, fw, s.Value.x, s.Value.z) - WNow(R) * 0.12f;
                        if (a > 0.25f) K.left = true;
                        if (a < -0.25f) K.right = true;
                        if (Mathf.Abs(a) < 0.7f) { K.up = true; K.jump = true; }
                        N.stuckT = 0; N.path = null;                  // 上がったら経路を引き直す
                        return;
                    }
                }
                else N.wetT = 0;
            }

            // ---- 行き先を決める
            Vector3 goalPos;
            var vg = P.carried == null ? NpcVipGoal(P, p) : null;        // VIP 台・VIP を運ぶ1位
            if (P.carried != null) goalPos = P.carried.order.guest.rb.position;
            else if (vg != null) { N.waiting = false; goalPos = vg.Value; }
            else
            {
                // 受取口へ向かう先は機体ごとに少しずらす（全員が同じ一点を目指すと団子になる）
                float ang0 = (P.idx / (float)Mathf.Max(1, players.Count)) * Mathf.PI * 2;
                float cx = pk.x + Mathf.Cos(ang0) * 0.7f, cz = pk.z + Mathf.Sin(ang0) * 0.7f;
                Vector3? best = null;
                float bd = new Vector2(cx - p.x, cz - p.z).magnitude * lv.greed;
                foreach (var d in dropped)
                {
                    if (d.t < T.dropSettle) continue;
                    if (d.owner == P && d.t < T.dropArm) continue;
                    var dp = d.ent.rb.position;
                    float dist = Dist2(dp, p);
                    if (dist < bd) { bd = dist; best = dp; }
                }
                // **運ぶ物が無い**ときは、まだ運んでいる相手を追いかけて、ぶつけて落とさせる（担当）。協力ではしない
                N.hunt = null;
                if (best == null && orders.Count - oi == 0 && mode != "coop")
                {
                    float hd = 1e9f;
                    foreach (var Q in players)
                    {
                        if (Q == P || Q.down || Q.carried == null) continue;
                        if (mode == "team" && Q.team == P.team) continue;
                        float d = Dist2(Q.ent.rb.position, p);
                        if (d < hd) { hd = d; N.hunt = Q; }
                    }
                    if (N.hunt != null) best = N.hunt.ent.rb.position;
                }
                // 自分より受取口に近い「手ぶらの人」が、残っている注文の数以上いるなら、行っても取れない
                if (best == null)
                {
                    int remain0 = orders.Count - oi, ahead = 0;
                    float myD = Dist2(pk, p);
                    foreach (var Q in players)
                    {
                        if (Q == P || Q.down || Q.carried != null) continue;
                        if (Dist2(pk, Q.ent.rb.position) < myD) ahead++;
                    }
                    N.waiting = ahead >= Mathf.Max(1, remain0);
                }
                else N.waiting = false;
                goalPos = best ?? new Vector3(cx, 0, cz);
                // 待つときは、受取口から少し離れた**待ち位置**まで行って、受取口を向いて止まる
                if (N.waiting)
                {
                    var wp = NpcWaitSpot(P, ang0);
                    if (wp != null)
                    {
                        if (Dist2(wp.Value, p) < 0.7f) { NpcFace(P, pk, ref K); return; }
                        goalPos = wp.Value;
                    }
                }
            }

            // ---- 長く動けない（行き先が 1.5m より遠いのに、NPC_CR_IDLE 秒たっても 1m も進めない）ならクレーンを呼ぶ
            if (N.anc == null || Dist2(p, N.anc.Value) > 1.0f || Dist2(goalPos, p) < 1.5f) { N.anc = p; N.ancT = t; }
            if (t - N.ancT > NPC_CR_IDLE) { N.wantCr = true; N.anc = null; return; }

            // ---- 引っかかり検知。進んでいなければ少し下がって引き直す
            float moved = Dist2(p, N.lastPos);
            N.lastPos = p;
            if (N.backT > 0)
            {
                N.backT -= dt;
                // **後ろが落ちる所（穴・水など）なら下がらない。**下がる速さも 1.6m/秒まで
                var bw = -FwdOf(R);
                var th = Tiles.Def(Tiles.At(map, p)); bool lava = th != null && th.burn;
                if (!NpcCanStand(p + bw * 0.75f, lv, lava) || !NpcCanStand(p + bw * 0.4f, lv, lava)) N.backT = 0;
                else
                {
                    var bv = R.rb.linearVelocity;
                    if (bv.x * bw.x + bv.z * bw.z < 1.6f) K.down = true;
                    if (N.turnBack) K.left = true; else K.right = true;
                    return;
                }
            }
            if (moved < 0.006f && p.y > -1.0f && !P.misfire) N.stuckT += dt; else if (moved >= 0.006f) N.stuckT = 0;
            if (N.stuckT > 0.7f)
            {
                N.stuckT = 0; N.backT = 0.55f;
                // **同じ所で続けて詰まったら、行こうとしていたマスをしばらく（6秒）避けて、別の道を引く**
                N.jams.RemoveAll(j => t - j.z > 6);
                bool again = N.jams.Exists(j => new Vector2(j.x - p.x, j.y - p.z).magnitude < 1.2f);
                N.jams.Add(new Vector4(p.x, p.z, t, 0));
                N.turnBack = again ? !N.turnBack : Rnd() < 0.5f;
                if (again && N.path != null && N.node < N.path.Count) N.avoidCells[N.path[N.node]] = t + 6;
                N.path = null;
                return;
            }

            // ---- 経路を引き直す。家具が吹っ飛んで古くなるので定期的に
            N.replan -= dt;
            var gCell = CellOf(goalPos);
            if (N.path == null || N.replan <= 0 || N.node >= N.path.Count || N.goalCell == null || N.goalCell.Value != gCell)
            {
                var from = CellOf(p);
                var to = gCell;
                if (TileCost(to, true) == null)
                {
                    var s = Tiles.NearestFloor(map, goalPos);
                    if (s != null) to = CellOf(s.Value);
                }
                var av = NpcAvoidMap();
                var extra = av != null ? new Dictionary<Vector2Int, float>(av) : new Dictionary<Vector2Int, float>();
                System.Action<Vector2Int, float> addX = (kk, v) => { float o; extra.TryGetValue(kk, out o); extra[kk] = o + v; };
                if (N.avoidCells.Count > 0)
                {
                    var rm = new List<Vector2Int>();
                    foreach (var kv in N.avoidCells) { if (kv.Value < t) rm.Add(kv.Key); else addX(kv.Key, 25); }
                    foreach (var kk in rm) N.avoidCells.Remove(kk);
                }
                // ジャンプ台は避ける。**料理を持っているときは特に**（当たった衝撃で料理が崩れる）
                foreach (var kk in RampCells()) addX(kk, P.carried != null ? 40 : 4);
                // ルートで動く置き物がいま居るマスも、少し通りにくくする
                foreach (var kk in NpcMoverCells()) addX(kk, 8);
                // 料理を持っているときは、ほかのロボの居るマス（と、その進む先）を避ける
                if (P.carried != null)
                {
                    foreach (var Q in players)
                    {
                        if (Q == P || Q.down) continue;
                        var q = Q.ent.rb.position; var qv = Q.ent.rb.linearVelocity;
                        foreach (var tt in new[] { 0f, 0.4f }) addX(CellOf(q + qv * tt), 6);
                    }
                    // 届け先でない客のマスとそのまわりも避ける（客に当たると料理が崩れる）
                    foreach (var gu in guests)
                    {
                        if (gu.sunk || gu.taken || gu == P.carried.order.guest || gu.rb == null) continue;
                        var gc = CellOf(gu.rb.position);
                        for (int di = -1; di <= 1; di++) for (int dj = -1; dj <= 1; dj++)
                                addX(new Vector2Int(gc.x + di, gc.y + dj), (di != 0 || dj != 0) ? 1.5f : 6);
                    }
                }
                N.extra = extra;
                // 池・川は、岸へ跳び上がれる（ジャンプを1段以上強化している）ときだけ通る。ほかに道が無ければ通る
                bool wadeOK = N.lv.wade && NpcCanClimb(P);
                N.path = FindPath(from, to, wadeOK, extra);
                N.dry = N.path != null && N.lv.wade && !wadeOK;
                if (N.path == null && N.lv.wade && !wadeOK) N.path = FindPath(from, to, true, extra);
                N.node = 0; N.replan = 0.5f; N.goalCell = gCell;
            }

            // ---- 次に向かう点。経路が無ければ目的地へ直に向かう
            var aim = goalPos;
            if (N.path != null && N.path.Count > 0)
            {
                // 通り過ぎたマスは飛ばす（次のマスのほうへ出ていれば通り過ぎた）。込み合ったマスは真ん中近く（0.45m）まで寄せる
                while (N.node < N.path.Count)
                {
                    var c = CellC(N.path[N.node]);
                    int tight = NpcTight(N.path[N.node]);
                    if (Dist2(c, p) < (tight != 0 ? 0.45f : 0.75f)) { N.node++; continue; }
                    if (tight == 0 && N.node + 1 < N.path.Count)
                    {
                        var nx = CellC(N.path[N.node + 1]);
                        float ux = nx.x - c.x, uz = nx.z - c.z, ul = Mathf.Sqrt(ux * ux + uz * uz); if (ul < 1e-6f) ul = 1;
                        if (((p.x - c.x) * ux + (p.z - c.z) * uz) / ul > 0.1f && Dist2(c, p) < 1.6f) { N.node++; continue; }
                    }
                    break;
                }
                if (N.node < N.path.Count)
                {
                    // **まっすぐ行けるいちばん先のマスを狙う**（1マスずつ追うと階段のようにジグザグに走る）
                    int k = N.node;
                    for (int k2 = N.node + 1; k2 < Mathf.Min(N.path.Count, N.node + 6); k2++)
                    {
                        if (NpcLineClear(p, CellC(N.path[k2]), N.extra, lv)) k = k2; else break;
                    }
                    aim = CellC(N.path[k]);
                    N.aimK = k;
                    // **まわりに置き物・壁・落ちる所があるときは、道の線そのものを追う**（線の上の 0.7m 先を狙う）
                    N.xte = 0; N.xteDrop = false;
                    if (k == N.node && (NpcTight(N.path[k]) != 0 || (k + 1 < N.path.Count && NpcTight(N.path[k + 1]) != 0)))
                    {
                        var pl = new List<Vector3> { N.node > 0 ? CellC(N.path[N.node - 1]) : p };
                        for (int k2 = N.node; k2 < Mathf.Min(N.path.Count, N.node + 4); k2++) pl.Add(CellC(N.path[k2]));
                        int bs = 0; float bt = 0, bdd = float.PositiveInfinity;
                        for (int s2 = 0; s2 + 1 < Mathf.Min(pl.Count, 3); s2++)
                        {
                            var a = pl[s2]; var b = pl[s2 + 1]; float ux = b.x - a.x, uz = b.z - a.z, L2 = ux * ux + uz * uz; if (L2 == 0) L2 = 1;
                            float tt = Mathf.Clamp01(((p.x - a.x) * ux + (p.z - a.z) * uz) / L2);
                            float dd = new Vector2(a.x + ux * tt - p.x, a.z + uz * tt - p.z).magnitude;
                            if (dd < bdd) { bdd = dd; bs = s2; bt = tt; }
                        }
                        N.xte = bdd;
                        var nd = NpcNearDrop();
                        bool nearDrop = nd.Contains(N.path[k]) || nd.Contains(CellOf(p));
                        float left = nearDrop ? 0.5f : 0.7f; int si = bs; float tq = bt;
                        N.xteDrop = nearDrop;
                        while (si + 1 < pl.Count)
                        {
                            var a = pl[si]; var b = pl[si + 1]; float sl = Dist2(a, b); if (sl == 0) sl = 1;
                            float rest = (1 - tq) * sl;
                            if (left <= rest) { tq += left / sl; aim = new Vector3(a.x + (b.x - a.x) * tq, 0, a.z + (b.z - a.z) * tq); left = 0; break; }
                            left -= rest; si++; tq = 0;
                        }
                        if (left > 0) aim = pl[pl.Count - 1];
                    }
                }
            }
            float distGoal = Dist2(goalPos, p);
            // 近くまで来たら直接狙う（2m では早すぎた。テーブルやイスを突っ切ってしまう）
            if (distGoal < (T.npcAvoid > 0 ? 1.4f : 2.0f)) aim = goalPos;

            // ---- 動く床の乗り継ぎ（帯の手前・動く床の上）。ここで決めたら、ほかの判断はしない
            if (NpcLaneHop(P, N, ref K, p)) return;

            // ---- 体当たり。決めるのは 0.6秒に1回だけ。まず配膳を優先する
            int remain = orders.Count - oi;
            bool wild = lv.ram >= 0.6f;
            bool mayRam = (lv.ram > 0 || N.hunt != null) && P.carried == null && !(remain > 0 && !wild);
            // 追いかけている相手が近づいたら、くじを引かずにまっすぐぶつかりに行く
            if (mayRam && N.hunt != null && !N.hunt.down && Dist2(N.hunt.ent.rb.position, p) < 3.2f)
            { N.ramTarget = N.hunt; N.ramHold = Mathf.Max(N.ramHold, 0.4f); }
            N.ramT -= dt;
            if (mayRam && N.ramT <= 0)
            {
                N.ramT = 0.6f;
                N.ramTarget = null;
                foreach (var Q in players)
                {
                    if (Q == P || Q.down) continue;
                    float d = Dist2(Q.ent.rb.position, p);
                    if (d >= 3.2f) continue;
                    if (remain > 0 && Q.carried == null) continue;
                    if (Rnd() < lv.ram * (Q.carried != null ? 1.0f : 0.30f)) { N.ramTarget = Q; N.ramHold = 1.2f; break; }
                }
            }
            if (N.ramHold > 0)
            {
                N.ramHold -= dt;
                var Q = N.ramTarget;
                if (mayRam && Q != null && !Q.down)
                {
                    aim = Q.ent.rb.position;
                    // **押し付けたまま止まっていたら、いったん下がって勢いを付け直す**
                    var qv = R.rb.linearVelocity;
                    if (Dist2(aim, p) < 1.3f && new Vector2(qv.x, qv.z).magnitude < 1.0f) N.pinT += dt; else N.pinT = 0;
                    if (N.pinT > 0.8f) { N.pinT = 0; N.backT = 0.6f; N.turnBack = Rnd() < 0.5f; return; }
                }
                else { N.ramHold = 0; N.ramTarget = null; }
            }

            // ---- 反応の遅れ。道をたどる狙いには遅れを入れない（かんたん以外）。遅れるのは体当たりのときだけ
            N.think -= dt;
            if (lv.lag || N.ramHold > 0)
            {
                if (N.think > 0 && N.lastAim != null) aim = N.lastAim.Value; else { N.lastAim = aim; N.think = lv.react; }
            }
            else N.lastAim = null;

            // ---- ここから先は人間と同じボタンだけ。角度は JS版の向き（左 = +）で数える
            var fwd = FwdOf(R);
            float ang = AngTo(p, fwd, aim.x, aim.z);
            // **回る速さで合わせる**（ずれに比例した欲しい回る速さ）
            float wNow = WNow(R);
            float wWant = Mathf.Abs(ang) < lv.aim * 0.5f ? 0 : Mathf.Clamp(ang * 6, -T.steerMax, T.steerMax);
            var vel = R.rb.linearVelocity; float spd = new Vector2(vel.x, vel.z).magnitude;
            float vf = vel.x * fwd.x + vel.z * fwd.z;
            // 受け取った直後で、行き先が後ろなら、まずまっすぐ下がって受取台から離れてから向きを変える
            if (P.carried != null && !N.hadCarry) N.pickT = t;
            N.hadCarry = P.carried != null;
            if (P.carried != null && t - N.pickT < 0.35f && Mathf.Abs(ang) > 1.2f)
            {
                if (vf > -1.2f) K.down = true;
                return;
            }
            // 片側に曲がれない故障のとき：曲がれない向きへずれていたら、反対回りでぐるっと回る
            float ang2 = ang;
            if (P.broken != null)
            {
                bool bad = P.broken == "left" ? ang > 0.3f : ang < -0.3f;
                if (bad) ang2 = ang - Mathf.Sign(ang) * 2 * Mathf.PI;
                bool okDir = ang2 > 0 ? P.broken != "left" : P.broken != "right";
                if (okDir && Mathf.Abs(ang2) > Mathf.Abs(wNow) / 4.5f + 0.08f) { if (ang2 > 0) K.left = true; else K.right = true; }
            }
            else if (wNow < wWant - 0.5f) K.left = true;
            else if (wNow > wWant + 0.5f) K.right = true;

            // ほぼ真後ろで、**近いとき（2.5m 以内）だけ**バックで寄る。遠ければその場で向き直す（2.2m/秒まで）
            float aimD = Dist2(aim, p);
            bool back = Mathf.Abs(ang2) > 2.2f && aimD < 2.5f && P.broken == null;
            if (back) { if (vf > -2.2f) K.down = true; }
            else if (Mathf.Abs(ang2) < lv.gas) K.up = true;

            bool onRoad = Tiles.At(map, p) == 'r';

            // ---- 届ける直前・受け取る直前は速度を落とす。ぎりぎりまで走って、バックを踏んで止める
            if (K.up && N.ramHold <= 0)
            {
                float care = lv.care, cap0 = P.carried != null ? care : care * 1.5f;
                float near = Mathf.Max(P.carried != null ? 1.9f : 1.6f, (spd * spd - cap0 * cap0) / (2 * 12) + 0.7f);
                if (distGoal < near && spd > cap0) { K.up = false; if (spd > cap0 + 0.8f) K.down = true; }
            }
            // ---- 料理を持っているとき・持っている相手には、前のロボに突っ込まない
            if (K.up && N.ramHold <= 0)
            {
                foreach (var Q in players)
                {
                    if (Q == P || Q.down || (P.carried == null && Q.carried == null)) continue;
                    if (Q.npc != null && (Q.npc.hunt == P || (Q.npc.ramHold > 0 && Q.npc.ramTarget == P))) continue;
                    var q = Q.ent.rb.position; var qv = Q.ent.rb.linearVelocity;
                    float rx = q.x - p.x, rz = q.z - p.z, d = Mathf.Sqrt(rx * rx + rz * rz);
                    if (d > 2.2f || d < 0.01f) continue;
                    if ((rx * fwd.x + rz * fwd.z) / d < 0.57f) continue;
                    float closing = ((vel.x - qv.x) * rx + (vel.z - qv.z) * rz) / d;
                    if (!(d < 1.15f || (closing > 0.6f && d / closing < 0.7f))) continue;
                    // 向かい合っているときは、番号の小さいほうが先に行く
                    if (Q.npc != null)
                    {
                        var qf = FwdOf(Q.ent);
                        if ((-rx * qf.x - rz * qf.z) / d > 0.57f && P.idx < Q.idx) continue;
                    }
                    N.yieldConf = true;
                    if (N.yieldT > 1.2f) continue;
                    K.up = false;
                    break;
                }
                N.yieldT = N.yieldConf ? N.yieldT + dt : 0;
                if (N.yieldT > 2.0f) N.yieldT = 0;
                N.yieldConf = false;
            }
            var tHere = Tiles.Def(Tiles.At(map, p));
            bool inLava = tHere != null && tHere.burn;
            // ---- 曲がり角と、危ない所のきわでは速さを落とす（体当たりで追いかけている間は除く）
            if (N.ramHold <= 0 && N.path != null && N.node < N.path.Count)
            {
                float cap = float.PositiveInfinity;
                int kA = Mathf.Max(N.node, N.aimK);
                float dist = 0; var prevP = p;
                for (int k2 = kA; k2 + 1 < Mathf.Min(N.path.Count, kA + 4); k2++)
                {
                    var c = CellC(N.path[k2]); var nx = CellC(N.path[k2 + 1]);
                    var a = (k2 == kA && kA > N.node) || k2 == 0 ? p : CellC(N.path[k2 - 1]);
                    dist += Dist2(c, prevP); prevP = c;
                    float da = Mathf.Abs(Mathf.Atan2(nx.x - c.x, nx.z - c.z) - Mathf.Atan2(c.x - a.x, c.z - a.z));
                    if (da > Mathf.PI) da = 2 * Mathf.PI - da;
                    if (da < 0.5f) continue;
                    bool risky = false;
                    for (int di = -1; di <= 1 && !risky; di++)
                        for (int dj = -1; dj <= 1; dj++)
                            if (!NpcCanStand(new Vector3(c.x + di * Coord.Tile, 0, c.z + dj * Coord.Tile), lv, inLava)) { risky = true; break; }
                    if (!risky && P.carried == null) continue;
                    var cc = CellOf(c); int tl = NpcTight(cc);
                    float exv = 0; bool furn = N.extra != null && N.extra.TryGetValue(cc, out exv) && exv > 0;
                    float v90 = (risky || tl == 2) ? 1.2f : (tl == 1 || furn) ? 2.0f : 3.5f;
                    float vt = Mathf.Max(1.2f, v90 / Mathf.Sin(Mathf.Min(da, Mathf.PI / 2)));
                    cap = Mathf.Min(cap, Mathf.Sqrt(vt * vt + 2 * NPC_DECEL * Mathf.Max(0, dist - 0.3f)));
                }
                {
                    // 道の線にまっすぐ乗れているときは、細い橋やふちのそばでも速く走ってよい（担当「橋やジャンプ台があると弱い」）
                    bool lined = Mathf.Abs(ang) < 0.15f && N.xte < 0.12f && Mathf.Abs(wNow) < 0.6f;
                    N.lined = lined;
                    System.Func<Vector2Int, float> vOf = c =>
                    {
                        if (NpcNarrow(c)) return lined ? (P.carried != null ? 4.5f : 6.0f) : (P.carried != null ? 2.0f : 3.5f);
                        if (P.carried == null) return float.PositiveInfinity;
                        int tl = NpcTight(c);
                        if (tl == 2) return lined ? 4.5f : 2.0f;
                        float ev = 0;
                        if (tl == 1 || (N.extra != null && N.extra.TryGetValue(c, out ev) && ev >= 1.5f) || NpcRampNear(c)) return 3.2f;
                        return float.PositiveInfinity;
                    };
                    // 道がジャンプ台を通るとき（ほかに道が無い）は、勢いが要るので落とさない
                    bool rampRun = false; var rc = RampCells();
                    for (int k2 = N.node; k2 < Mathf.Min(N.path.Count, N.node + 5) && !rampRun; k2++) if (rc.Contains(N.path[k2])) rampRun = true;
                    if (rampRun) cap = float.PositiveInfinity;
                    N.rampRun = rampRun;
                    if (!rampRun)
                    {
                        cap = Mathf.Min(cap, vOf(CellOf(p)));
                        float dn = 0; var prv = p;
                        for (int k2 = N.node; k2 < Mathf.Min(N.path.Count, N.node + 4); k2++)
                        {
                            var cc = CellC(N.path[k2]);
                            dn += Dist2(cc, prv); prv = cc;
                            float vn = vOf(N.path[k2]);
                            if (vn < float.PositiveInfinity) cap = Mathf.Min(cap, Mathf.Sqrt(vn * vn + 2 * NPC_DECEL * Mathf.Max(0, dn - 0.5f)));
                        }
                    }
                    if (P.carried != null && N.xte > 0.3f) cap = Mathf.Min(cap, 1.4f);
                    if (N.xteDrop && N.xte > 0.2f) cap = Mathf.Min(cap, 1.2f);
                }
                // ルートで動く置き物（迷路の扉の木箱など）が道をふさいでいたら、手前（1m）で止まって開くのを待つ
                {
                    var mc = NpcMoverCells();
                    float dm = 0; var pv = p;
                    for (int k2 = N.node; k2 < Mathf.Min(N.path.Count, N.node + 3); k2++)
                    {
                        var cc = CellC(N.path[k2]);
                        dm += Dist2(cc, pv); pv = cc;
                        if (mc.Contains(N.path[k2]))
                        {
                            if (dm < 2.2f) { cap = Mathf.Min(cap, Mathf.Max(0, (dm - 1.0f) * 2)); N.stuckT = 0; }
                            break;
                        }
                    }
                }
                // 料理を持って向きを大きく変えている最中は、勢いを付けない
                if (P.carried != null && Mathf.Abs(ang) > 0.5f) cap = Mathf.Min(cap, Mathf.Max(1.6f, 2.5f / Mathf.Sin(Mathf.Min(Mathf.Abs(ang), Mathf.PI / 2))));
                bool edge = false;
                for (int di = -1; di <= 1 && !edge; di++)
                    for (int dj = -1; dj <= 1; dj++)
                        if ((di != 0 || dj != 0) && !NpcCanStand(new Vector3(p.x + di * Coord.Tile, 0, p.z + dj * Coord.Tile), lv, inLava)) { edge = true; break; }
                if (edge && !N.rampRun) cap = Mathf.Min(cap, N.lined ? 5.0f : 3.0f);
                if (lv.gas > 1.3f) cap *= 1.3f;
                if (vf > cap - 0.4f) { K.up = false; if (vf > cap + 1.2f) K.down = true; }
                // 横へすべっている間はアクセルを踏まない
                float vLat = Mathf.Abs(vel.x * fwd.z - vel.z * fwd.x);
                var pc = CellOf(p); float pe = 0;
                bool busy = NpcTight(pc) > 0 || (N.extra != null && N.extra.TryGetValue(pc, out pe) && pe > 0);
                if (vLat > (P.carried != null && busy ? 1.0f : 2.0f)) K.up = false;
            }
            // ---- 落ちる前に止まる。進行方向の少し先を見て、乗れる所が無ければ止まる
            if (spd > 0.5f && !onRoad)
            {
                float la = 0.45f + spd * 0.34f;
                bool bad = false; Vector3 a = p;
                float sx = -vel.z / spd * 0.25f, sz = vel.x / spd * 0.25f;   // 機体の幅（左右 0.25m）
                foreach (var f in new[] { 0.5f, 1f })
                {
                    a = new Vector3(p.x + vel.x / spd * la * f, 0, p.z + vel.z / spd * la * f);
                    if (!NpcCanStand(a, lv, inLava)) { bad = true; break; }
                    if (f == 0.5f && (!NpcCanStand(a + new Vector3(sx, 0, sz), lv, inLava) || !NpcCanStand(a - new Vector3(sx, 0, sz), lv, inLava))) { bad = true; break; }
                }
                if (bad)
                {
                    // **今の動きと逆へ踏んで止まる**
                    K.up = false; K.down = false;
                    bool waiting = moverLanes.Contains(CellOf(a));      // 動く床の帯なら、下がらずに縁で待つ
                    if (!waiting) { if (vf >= 0) K.down = true; else K.up = true; }
                    N.stuckT = 0;
                }
            }
            // ---- 車道は車をやり過ごしてから渡る。渡り始めたら止まらない
            if (!K.down && !onRoad && cars.Count > 0)
            {
                float dx0 = spd > 0.2f ? vel.x / spd : fwd.x, dz0 = spd > 0.2f ? vel.z / spd : fwd.z;
                float la = 1.0f + spd * 0.5f;
                if (Tiles.At(map, new Vector3(p.x + dx0 * la, 0, p.z + dz0 * la)) == 'r')
                    foreach (var c in cars)
                    {
                        var cp = c.ent.rb.position;
                        float dx = cp.x - p.x, dz = cp.z - p.z;
                        if (new Vector2(dx, dz).magnitude < (P.carried != null ? 10.5f : 7.5f) && (dx * c.dir.x + dz * c.dir.z) < 0) { K.up = false; break; }
                    }
            }

            // ---- アイテム
            N.itemT -= dt;
            if (N.itemT <= 0)
            {
                N.itemT = 0.35f;
                var W = WalletOf(P);
                bool used = false;
                System.Func<string, bool> pick = k =>
                {
                    if (W.Has(k) > 0 && Rnd() < lv.item) { P.slot = k; used = true; return true; }
                    return false;
                };
                bool done2 = false;
                if (P.brokenDrive || P.broken != null || P.botDmg >= 60) done2 = pick("repair");
                if (!done2 && !InvOn(P) && P.botDmg >= 40) done2 = pick("star");
                if (!done2 && P.carried != null && Dist2(P.carried.order.guest.rb.position, p) > 5.5f) done2 = pick("drone");
                foreach (var Q in players)
                {
                    if (Q == P) continue;
                    if (done2) break;
                    var q = Q.ent.rb.position;
                    float dx = q.x - p.x, dz = q.z - p.z, d = new Vector2(dx, dz).magnitude;
                    if (d < 1e-6f) d = 1;
                    float dot = (dx / d) * fwd.x + (dz / d) * fwd.z;
                    if (d < 15 && dot > 0.2f) done2 = pick("missile");
                    if (!done2 && d < 13 && dot > 0.93f) done2 = pick("laser");
                    else if (d < 6 && dot < -0.5f) done2 = pick("banana");
                    if (!done2 && P.carried == null && Q.carried != null && d < 6.5f && dot > 0.85f
                        && !(mode == "team" && Q.team == P.team) && !booms.Exists(b => b.owner == P)) done2 = pick("boomerang");
                }
                if (used) K.use = true;
            }

            // ---- ジャンプ：池に浸かっていたら岸へ上がる／障害物を跳び越える
            if (lv.jump)
            {
                float foot = p.y - R.size.y / 2;
                bool inWater = foot < -0.06f && Tiles.IsLiquid(Tiles.At(map, p));
                if (inWater) { K.up = true; K.jump = true; }
                else if (N.stuckT > 0.35f) K.jump = true;
            }
        }
    }
}
