using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// NPC の強さ。JS版 NPC_SKILL / NPC_LV と同じ値。
    ///
    /// react … 判断の間隔[秒]。aim … これ以上ずれたら曲がる[rad]。
    /// wade  … 水・溶岩を突っ切るか。jump … 池から跳んで上がる・詰まったら跳ぶか。
    /// ram   … 体当たりを狙う率。item … アイテムを使う率。greed … 落ちた料理へ寄り道する気の強さ。
    /// gas   … 向きのずれがこれより小さいときだけアクセル[rad]。小さいと「向き直してから走る」。
    /// care  … 届ける直前に落とす速さ[m/秒]。小さいほど丁寧に寄せる。
    /// </summary>
    public class NpcLv
    {
        public string k, name, ds;
        public float react = 0.18f, aim = 0.30f, ram, item, greed, gas = 1.1f, care = 1.8f;
        public bool wade = true, jump = true;
    }

    public static class NpcLevels
    {
        public static readonly NpcLv[] All =
        {
            new NpcLv { k = "serious", name = "まじめ", ram = 0.00f, item = 0.35f, greed = 0.35f, gas = 0.85f, care = 1.2f,
                        ds = "向き直してから走るので店を壊さない。ぶつけに来ない" },
            new NpcLv { k = "normal", name = "普通", ram = 0.25f, item = 0.70f, greed = 1.00f, gas = 1.10f,
                        ds = "配膳しつつ、隙があれば妨害する" },
            new NpcLv { k = "wild", name = "暴走", ram = 0.90f, item = 1.00f, greed = 1.80f, gas = 1.50f, care = 3.6f,
                        ds = "曲がりきれずに膨らみ、そこら中にぶつかる。妨害もしたがる" },
            new NpcLv { k = "easy", name = "かんたん", react = 0.34f, aim = 0.55f, wade = false, jump = false,
                        ram = 0.00f, item = 0.25f, greed = 0.50f, gas = 0.90f, care = 1.5f,
                        ds = "反応が遅く狙いも粗い。練習用" },
        };

        /// <summary>前の名前で来ても動くように（npc-hard は暴走）。</summary>
        public static string Canon(string k) { return k == "hard" ? "wild" : k; }
        public static bool Known(string k) { k = Canon(k); foreach (var l in All) if (l.k == k) return true; return false; }
        public static NpcLv Get(string k)
        {
            k = Canon(k);
            foreach (var l in All) if (l.k == k) return l;
            return All[1];
        }
    }

    /// <summary>NPC 1台ぶんの頭の中（JS版 newNpc）。</summary>
    public class NpcBrain
    {
        public NpcLv lv;
        public List<Vector2Int> path; public int node;
        public float replan, think, stuckT, backT, ramT, ramHold, itemT;
        public Vector3 lastPos;
        public bool turnBack, waiting;
        public Vector2Int? goalCell;
        public Vector3? lastAim;
        public Player ramTarget;

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

        // ================================================================ 道を引く
        /// <summary>
        /// マスの通りやすさ。通れないマスは null（JS版 tileCost）。
        /// 道路は車、線路は電車が来るので嫌がる。氷は制御が効かないので避けたがる。
        /// 水と溶岩は通れるが高い＝急ぐときだけ突っ切る（wade が false なら避ける）。
        /// </summary>
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

        /// <summary>
        /// A*（JS版 findPath）。マス数がせいぜい数百なので、線形に最小を取るだけで足りる。
        /// 動く床の帯は待てば渡れるので高いコスト（15）で通す。ほかに道があればそちらを選ぶくらい高い。
        /// 置き物のマスは通れない（行き先そのものは除く。客が木箱の横に立っていても届けられるように）。
        /// </summary>
        List<Vector2Int> FindPath(Vector2Int from, Vector2Int to, bool wade)
        {
            if (from == to) return new List<Vector2Int> { to };
            System.Func<Vector2Int, float> h = c => Mathf.Abs(c.x - to.x) + Mathf.Abs(c.y - to.y);
            var open = new List<KeyValuePair<Vector2Int, float>>();      // マスと f
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
                    float ng = cg + c.Value;
                    float old;
                    if (gOf.TryGetValue(nk, out old) && old <= ng) continue;
                    gOf[nk] = ng; came[nk] = cur;
                    open.Add(new KeyValuePair<Vector2Int, float>(nk, ng + h(nk)));
                }
            }
            return null;
        }

        /// <summary>そこに乗れるか。動く床の上も「乗れる」として数える（JS版 npcCanStand）。</summary>
        bool NpcCanStand(Vector3 p, NpcLv lv)
        {
            var t = Tiles.Def(Tiles.At(map, p));
            if (t != null && (t.floor || (lv.wade && t.liquid && !t.deadly))) return true;
            foreach (var mv in movers)
            {
                var q = mv.ent.rb.position;
                if (Mathf.Abs(p.x - q.x) < 1.5f && Mathf.Abs(p.z - q.z) < 0.5f) return true;
            }
            return false;
        }

        // ---- 道探しに教えるマス（組み立てのときに呼ぶ）
        /// <summary>動く床が通るマス。床の長さ（3マス）ぶんを、動く向きに沿って数える。</summary>
        void AddMoverLanes(Vector3 b, int axis, float range)
        {
            for (float t = -range; t <= range; t += 0.5f)
            {
                float mx = b.x + (axis == 0 ? t : 0), mz = b.z + (axis == 2 ? t : 0);
                for (int w = -1; w <= 1; w++)
                    moverLanes.Add(CellOf(new Vector3(mx + (axis == 0 ? w : 0), 0, mz + (axis == 2 ? w : 0))));
            }
        }

        /// <summary>いかだの通り道（置いた所のマスだけだと、動いた先はただの水なので道が見つからない）。JS版 sweepRaftLanes。</summary>
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
                    float t = s / (float)steps;
                    add(a.x + (b2.x - a.x) * t - rf.cx, a.z + (b2.z - a.z) * t - rf.cz);
                }
            }
        }

        // ================================================================ 1台ぶんの判断
        /// <summary>
        /// NPC 1台ぶんの判断（JS版 driveNpc）。毎刻み呼んで、人間と同じ5つのボタンだけを P.input に入れる。
        /// </summary>
        void DriveNpc(Player P, float dt)
        {
            var N = P.npc; var lv = N.lv;
            var K = new BotInput();
            P.input = K;
            if (state != "play") return;

            var R = P.ent; var p = R.rb.position;

            // ---- 行き先を決める
            //   持っている → 配膳先
            //   手ぶら     → 近くに落ちている料理があればそれ、無ければカウンター
            Vector3 goalPos;
            if (P.carried != null) goalPos = P.carried.order.guest.rb.position;
            else
            {
                // 受取口へ向かう先は機体ごとに少しずらす（全員が同じ一点を目指すと団子になる）
                float ang0 = (P.idx / (float)Mathf.Max(1, players.Count)) * Mathf.PI * 2;
                var pk = PickupPoint();
                float cx = pk.x + Mathf.Cos(ang0) * 0.7f, cz = pk.z + Mathf.Sin(ang0) * 0.7f;
                Vector3? best = null;
                float bd = new Vector2(cx - p.x, cz - p.z).magnitude * lv.greed;
                foreach (var d in dropped)
                {
                    if (d.t < T.dropSettle) continue;
                    if (d.owner == P && d.t < T.dropArm) continue;
                    var dp = d.ent.rb.position;
                    float dist = new Vector2(dp.x - p.x, dp.z - p.z).magnitude;
                    if (dist < bd) { bd = dist; best = dp; }
                }
                // 自分より受取口に近い「手ぶらの人」が残りの注文の数以上いるなら、行っても取れない。
                // 押し合いにならないよう、その場で少し離れて待つ
                if (best == null)
                {
                    int remain0 = orders.Count - oi, ahead = 0;
                    float myD = new Vector2(pk.x - p.x, pk.z - p.z).magnitude;
                    foreach (var Q in players)
                    {
                        if (Q == P || Q.down || Q.carried != null) continue;
                        var qp = Q.ent.rb.position;
                        if (new Vector2(pk.x - qp.x, pk.z - qp.z).magnitude < myD) ahead++;
                    }
                    N.waiting = ahead >= Mathf.Max(1, remain0);
                    if (N.waiting && myD < 4.5f)
                    {
                        K.down = true;
                        if (P.idx % 2 == 1) K.left = true; else K.right = true;
                        P.input = K;
                        return;
                    }
                }
                else N.waiting = false;
                goalPos = best ?? new Vector3(cx, 0, cz);
            }

            // ---- 引っかかり検知。進んでいなければ少し下がって引き直す
            float moved = new Vector2(p.x - N.lastPos.x, p.z - N.lastPos.z).magnitude;
            N.lastPos = p;
            if (N.backT > 0)
            {
                N.backT -= dt;
                K.down = true;
                if (N.turnBack) K.left = true; else K.right = true;
                P.input = K;
                return;
            }
            if (moved < 0.006f && p.y > -1.0f) N.stuckT += dt; else N.stuckT = 0;
            if (N.stuckT > 0.7f)
            {
                N.stuckT = 0; N.backT = 0.55f; N.turnBack = Rnd() < 0.5f; N.path = null;
                return;
            }

            // ---- 経路を引き直す。家具が吹っ飛んで古くなるので定期的に
            N.replan -= dt;
            var gCell = CellOf(goalPos);
            if (N.path == null || N.replan <= 0 || N.node >= N.path.Count || N.goalCell == null || N.goalCell.Value != gCell)
            {
                var from = CellOf(p);
                var to = gCell;
                // 行き先が床でないとき（客が池に落ちている等）は、近くの走れるマスへ
                if (TileCost(to, true) == null)
                {
                    var s = Tiles.NearestFloor(map, goalPos);
                    if (s != null) to = CellOf(s.Value);
                }
                N.path = FindPath(from, to, lv.wade);
                N.node = 0; N.replan = 0.5f; N.goalCell = gCell;
            }

            // ---- 次に向かう点。経路が無ければ目的地へ直に向かう
            var aim = goalPos;
            if (N.path != null && N.path.Count > 0)
            {
                while (N.node < N.path.Count)
                {
                    var c = Coord.Cell(N.path[N.node].x, N.path[N.node].y);
                    if (new Vector2(c.x - p.x, c.z - p.z).magnitude < 0.75f) N.node++; else break;
                }
                if (N.node < N.path.Count) aim = Coord.Cell(N.path[N.node].x, N.path[N.node].y);
            }
            float distGoal = new Vector2(goalPos.x - p.x, goalPos.z - p.z).magnitude;
            if (distGoal < 2.0f) aim = goalPos;            // 近くまで来たら直接狙う

            // ---- 体当たり。決めるのは 0.6秒に1回だけ。決めたら少しの間そこへ向かう。
            // まず配膳を優先する（運んでいる間・受取口に注文が残っている間は狙わない。暴走だけは例外）
            int remain = orders.Count - oi;
            bool wild = lv.ram >= 0.6f;
            bool mayRam = lv.ram > 0 && P.carried == null && !(remain > 0 && !wild);
            N.ramT -= dt;
            if (mayRam && N.ramT <= 0)
            {
                N.ramT = 0.6f;
                N.ramTarget = null;
                foreach (var Q in players)
                {
                    if (Q == P || Q.down) continue;
                    var q = Q.ent.rb.position;
                    float d = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                    if (d >= 3.2f) continue;
                    if (remain > 0 && Q.carried == null) continue;     // 注文が残っているのに狙うのは、運んでいる相手だけ
                    if (Rnd() < lv.ram * (Q.carried != null ? 1.0f : 0.30f)) { N.ramTarget = Q; N.ramHold = 1.2f; break; }
                }
            }
            if (N.ramHold > 0)
            {
                N.ramHold -= dt;
                var Q = N.ramTarget;
                if (mayRam && Q != null && !Q.down) aim = Q.ent.rb.position;
                else { N.ramHold = 0; N.ramTarget = null; }
            }

            // ---- 反応の遅れ。強さの調整つまみ
            N.think -= dt;
            if (N.think > 0 && N.lastAim != null) aim = N.lastAim.Value;
            else { N.lastAim = aim; N.think = lv.react; }

            // ---- ここから先は人間と同じ5ボタンだけ。
            // 角度は JS版の向き（左 = +）で数える。Unity は z が逆なので符号を返す
            var fwd = R.rb.rotation * Vector3.forward; fwd.y = 0; fwd.Normalize();
            float ang = -(Mathf.Atan2(aim.x - p.x, aim.z - p.z) - Mathf.Atan2(fwd.x, fwd.z));
            while (ang > Mathf.PI) ang -= 2 * Mathf.PI;
            while (ang < -Mathf.PI) ang += 2 * Mathf.PI;
            if (ang > lv.aim) K.left = true;
            if (ang < -lv.aim) K.right = true;
            bool back = Mathf.Abs(ang) > 2.2f;             // ほぼ真後ろならバックで寄る
            if (back) K.down = true;
            else if (Mathf.Abs(ang) < lv.gas) K.up = true;

            var vel = R.rb.linearVelocity; float spd = new Vector2(vel.x, vel.z).magnitude;
            bool onRoad = Tiles.At(map, p) == 'r';

            // ---- 届ける直前・受け取る直前は速度を落とす（ちゃんと運ぶほうが儲かる）。
            // JS版は `!N.ramHold` で見ていて、一度体当たりを狙うと ramHold が負の小さな値で残り、
            // それ以降この減速が効かなくなっていた。ここは「狙っている間だけ外す」にしてある
            if (K.up && N.ramHold <= 0)
            {
                float near = P.carried != null ? 2.4f : 1.9f;
                float cap = P.carried != null ? lv.care : lv.care * 1.5f;
                if (distGoal < near && spd > cap) K.up = false;
            }
            // ---- 落ちる前に止まる。進行方向の少し先を見て、乗れる所が無ければ後退
            if (spd > 0.5f && !onRoad)
            {
                float la = 0.45f + spd * 0.34f;            // 速いほど遠くを見る
                var a = new Vector3(p.x + vel.x / spd * la, 0, p.z + vel.z / spd * la);
                if (!NpcCanStand(a, lv))
                {
                    K.up = false;
                    // 動く床の帯なら、下がらずに縁で待つ（下がると寄っては戻るを繰り返す）
                    if (!moverLanes.Contains(CellOf(a))) K.down = true;
                    N.stuckT = 0;                          // 待っている間は「詰まった」と数えない
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
                        if (new Vector2(dx, dz).magnitude < 7.5f && (dx * c.dir.x + dz * c.dir.z) < 0) { K.up = false; break; }
                    }
            }

            // ---- アイテム。強いほどよく使う
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
                bool done = false;
                // 壊れているなら真っ先に直す。傷んできたら無敵。運んでいて遠いならドローン
                if (P.brokenDrive || P.broken != null || P.botDmg >= 60) done = pick("repair");
                if (!done && !InvOn(P) && P.botDmg >= 40) done = pick("star");
                if (!done && P.carried != null)
                {
                    var tg = P.carried.order.guest.rb.position;
                    if (new Vector2(tg.x - p.x, tg.z - p.z).magnitude > 5.5f) done = pick("drone");
                }
                foreach (var Q in players)
                {
                    if (Q == P) continue;
                    if (done) break;
                    var q = Q.ent.rb.position;
                    float dx = q.x - p.x, dz = q.z - p.z, d = new Vector2(dx, dz).magnitude;
                    if (d < 1e-6f) d = 1;
                    float dot = (dx / d) * fwd.x + (dz / d) * fwd.z;   // 正面なら +1、真後ろなら -1
                    if (d < 15 && dot > 0.2f) done = pick("missile");  // 追尾は向きが多少ずれていても当たる
                    if (!done && d < 13 && dot > 0.93f) done = pick("laser");
                    else if (d < 6 && dot < -0.5f) done = pick("banana");
                    // 手ぶらで、正面の相手が料理を運んでいたらブーメランで奪う
                    if (!done && P.carried == null && Q.carried != null && d < 6.5f && dot > 0.85f
                        && !(mode == "team" && Q.team == P.team)
                        && !booms.Exists(b => b.owner == P)) done = pick("boomerang");
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
            P.input = K;
        }
    }
}
