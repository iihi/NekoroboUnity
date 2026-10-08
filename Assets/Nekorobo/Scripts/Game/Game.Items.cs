using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>アイテム1種類（JS版 ITEMS の1行）。</summary>
    public class ItemDef
    {
        public string k, n, sn, ds, shortN;
        public float price, rate = 1.5f;
        public int max, ammo, col;
        public bool aim;
    }

    /// <summary>強化1種類（JS版 UPGRADES の1行）。</summary>
    public class UpgradeDef { public string k, n, ds; public float bas, rate; public int max; }

    /// <summary>
    /// 財布（人ごと）。所持金・強化の Lv・アイテムの個数・弾の残り・何個買ったか。
    /// ステージをまたいで残る（JS版 RUN.wallets）。
    /// </summary>
    public class Wallet
    {
        public float cash;
        public readonly Dictionary<string, int> up = new Dictionary<string, int>();
        public readonly Dictionary<string, int> items = new Dictionary<string, int>();
        public readonly Dictionary<string, int> ammo = new Dictionary<string, int>();
        public readonly Dictionary<string, int> bought = new Dictionary<string, int>();

        public Wallet(float cash)
        {
            this.cash = cash;
            foreach (var u in Game.UPGRADES) up[u.k] = 0;
            foreach (var it in Game.ITEMS) { items[it.k] = 0; ammo[it.k] = 0; bought[it.k] = 0; }
        }
        public int Up(string k) { int v; return up.TryGetValue(k, out v) ? v : 0; }
        public int Has(string k) { int v; return items.TryGetValue(k, out v) ? v : 0; }

        /// <summary>デバッグ：アイテムを全部、上限まで持たせる（JS版 stockItems("all")）。</summary>
        public void StockAll()
        {
            foreach (var it in Game.ITEMS)
            {
                items[it.k] = Mathf.Max(items[it.k], it.max);
                if (it.ammo > 0) ammo[it.k] = it.ammo;
            }
        }
    }

    /// <summary>
    /// アイテム。JS版の useItem / placeBanana / launchDrone / throwBoomerang / launchShot /
    /// 弾道ミサイル（照準）/ explode / doRepair / 無敵 を写したもの。
    /// </summary>
    public partial class Game
    {
        public static readonly ItemDef[] ITEMS =
        {
            new ItemDef { k = "banana", n = "バナナの皮", shortN = "バナナ", price = 500, rate = 1.5f, max = 3, col = 0xf5d020, ds = "うしろに1マス置く。踏むと激しく滑って回る" },
            new ItemDef { k = "repair", n = "リペア", price = 1500, rate = 1.5f, max = 3, col = 0x5fd08a, ds = "体力を50%ぶん回復して、旋回・駆動の故障も直す" },
            new ItemDef { k = "drone", n = "ドローン", price = 1800, rate = 1.5f, max = 2, col = 0x8fd4ff, ds = "運搬中の料理を、乱さずターゲットへ自動で届ける（使い捨て）" },
            new ItemDef { k = "laser", n = "レーザービーム", shortN = "レーザー", sn = "エネルギーパック", price = 10000, max = 1, ammo = 20, col = 0x6be0ff, ds = "まっすぐ飛ぶ光線を20発。当たると爆発して周りが吹っ飛ぶ（車も）" },
            new ItemDef { k = "ball", n = "弾道ミサイル", shortN = "弾道", price = 20000, max = 1, col = 0xffcf3a, aim = true, ds = "上へ打ち上げて、狙った場所へ落とす。押しっぱなしで照準、離すと発射" },
            new ItemDef { k = "missile", n = "追尾ミサイル", shortN = "追尾", price = 6000, rate = 1.5f, max = 2, col = 0xff6b4a, ds = "いちばん近い相手を追いかける。高いぶん当たりやすいが、急旋回でかわされる" },
            new ItemDef { k = "boomerang", n = "ブーメラン", price = 3000, rate = 1.5f, max = 2, col = 0xe0913a, ds = "前へ投げると戻ってくる。運んでいる料理を奪う／受取口の料理を取ってくる（手ぶらのときだけ）" },
            new ItemDef { k = "star", n = "無敵", price = 8000, rate = 1.5f, max = 2, col = 0xff7ad9, ds = "10秒間ダメージを受けない。ぶつけた客にもダメージを与えず、壊しても修理費がかからない" },
        };
        public static ItemDef ItemOf(string k) { foreach (var i in ITEMS) if (i.k == k) return i; return null; }

        public static readonly UpgradeDef[] UPGRADES =
        {
            new UpgradeDef { k = "accel", n = "加速アップ", ds = "加速力・バック出力 +15% / Lv（Lv10で2.5倍・最高速10.5m/s）", bas = 700, rate = 1.34f, max = 10 },
            new UpgradeDef { k = "armor", n = "耐久度アップ", ds = "受けるダメージ −10% / Lv", bas = 1400, rate = 1.50f, max = 5 },
            new UpgradeDef { k = "jump", n = "ジャンプ性能アップ", ds = "高さ +10% / 連射間隔 −0.05秒 / Lv", bas = 1100, rate = 1.50f, max = 5 },
            new UpgradeDef { k = "light", n = "重量軽量化", ds = "ぶつけた客の吹っ飛び・客のダメージ・店の損壊 −10% / Lv", bas = 1200, rate = 1.50f, max = 5 },
        };

        // ---- 財布（人ごと。協力・チーム戦は対戦を移すときに分ける）
        public Wallet[] wallets;
        /// <summary>その人の財布。協力のときは全員1つ目、チーム戦はチームの番号（青＝0・赤＝1）を見る。</summary>
        public Wallet WalletOf(Player P)
        {
            if (P == null || mode == "coop") return wallets[0];
            return wallets[mode == "team" ? Mathf.Clamp(P.team, 0, 1) : Mathf.Clamp(P.idx, 0, 3)];
        }
        /// <summary>タイトルのデバッグ「最初からアイテムを持つ」（この端末で覚える）。</summary>
        public const string ITEMS_KEY = "nekorobo.items";

        void NewWallets()
        {
            wallets = new[] { new Wallet(T.startCash), new Wallet(T.startCash), new Wallet(T.startCash), new Wallet(T.startCash) };
            if (PlayerPrefs.GetString(ITEMS_KEY, "") == "all") foreach (var W in wallets) W.StockAll();
        }
        public void ResetWallets() { NewWallets(); runDone = 0; }

        // ================================================================ 選ぶ・使う
        void FixSlot(Player P)
        {
            var W = WalletOf(P);
            if (P.slot != null && W.Has(P.slot) > 0) return;
            P.slot = null;
            foreach (var it in ITEMS) if (W.Has(it.k) > 0) { P.slot = it.k; break; }
        }

        void CycleItem(Player P, int step)
        {
            var W = WalletOf(P);
            var have = new List<ItemDef>();
            foreach (var it in ITEMS) if (W.Has(it.k) > 0) have.Add(it);
            if (have.Count == 0) { P.slot = null; return; }
            int cur = have.FindIndex(i => i.k == P.slot), n = have.Count;
            P.slot = have[((cur + step) % n + n) % n].k;
            Say(P, ItemOf(P.slot).n, 0.8f);
        }

        bool CanUse(Player P, ItemDef it)
        {
            switch (it.k)
            {
                case "repair": return P.botDmg > 0 || P.broken != null || P.brokenDrive;
                case "drone": return P.carried != null;
                case "boomerang": return P.carried == null && !booms.Exists(b => b.owner == P);
                case "star": return !InvOn(P);
                default: return true;
            }
        }

        bool DoUse(Player P, ItemDef it)
        {
            switch (it.k)
            {
                case "banana": PlaceBanana(P); return true;
                case "repair": DoRepair(P); return true;
                case "drone": LaunchDrone(P); return true;
                case "laser": LaunchShot(P, false); return true;
                case "ball": return LaunchBallistic(P);
                case "missile": LaunchShot(P, true); return true;
                case "boomerang": ThrowBoomerang(P); return true;
                case "star": P.invT = T.invTime; Say(P, "無敵にゃ！！", 1.4f); return true;
            }
            return false;
        }

        void UseItem(Player P)
        {
            var W = WalletOf(P);
            FixSlot(P);
            if (P.slot == null) return;
            var it = ItemOf(P.slot);
            if (W.Has(it.k) <= 0) return;
            if (!CanUse(P, it)) { Say(P, "いま使えないにゃ", 0.9f); return; }
            if (DoUse(P, it))
            {
                if (it.ammo > 0)
                {
                    // 1発ぶん減らす。撃ち切ったらパックごと無くなる
                    W.ammo[it.k] = Mathf.Max(0, W.ammo[it.k] - 1);
                    if (W.ammo[it.k] <= 0) W.items[it.k] = 0;
                }
                else W.items[it.k]--;
                FixSlot(P);
            }
        }

        /// <summary>アイテムの入力（Drive から）。押しっぱなしで連発しないよう、押した瞬間だけ拾う。</summary>
        void ItemInput(Player P)
        {
            var inp = P.input;
            if (inp.use && !P.prevUse && AimItemOf(P) == null) UseItem(P);
            if (inp.cycle && !P.prevCycle) CycleItem(P, 1);
            if (inp.cycleBack && !P.prevBack) CycleItem(P, -1);
            P.prevUse = inp.use; P.prevCycle = inp.cycle; P.prevBack = inp.cycleBack;
        }

        /// <summary>デバッグ：いま全員にアイテムを全部持たせる（右パネル）。</summary>
        public void GiveAllItems()
        {
            foreach (var P in players) { WalletOf(P).StockAll(); FixSlot(P); }
            hud.Toast("アイテムを全部持たせました");
        }

        // ================================================================ リペア
        void DoRepair(Player P)
        {
            float before = P.botDmg;
            P.botDmg = Mathf.Max(0, P.botDmg - T.repairHeal);
            P.broken = null; P.brokenDrive = false; P.misfire = false;
            hud.Pop(P.ent.rb.position + Vector3.up * 1.0f, "修理 +" + Mathf.RoundToInt(before - P.botDmg) + "%", 0x5fd08a, true);
            Say(P, "直ったにゃ！", 1.4f);
            SetFace(P, "happy", 1.2f);
        }

        // ================================================================ バナナの皮
        class Banana { public Vector3 p; public Transform mesh; public Player owner; public float t; }
        readonly List<Banana> bananas = new List<Banana>();

        void PlaceBanana(Player P)
        {
            var p = P.ent.rb.position; var fwd = Fwd(P);
            var b = p - fwd;
            if (!Tiles.IsFloor(Tiles.At(map, b))) b = p;      // 床の無い所へは置かない
            var c = Coord.Cell(Coord.ToI(b.x), Coord.ToJ(b.z));
            var m = Part.Add(fxRoot, MeshGen.Sphere(0.22f, 12, 8), Mats.Get(0xf5d020, 0.6f), new Vector3(c.x, 0.07f, c.z), true, "Banana").transform;
            m.localScale = new Vector3(1, 0.34f, 1.25f);
            bananas.Add(new Banana { p = c, mesh = m, owner = P });
            Say(P, "置いたにゃ！", 0.9f);
        }

        void UpdateBananas(float dt)
        {
            for (int i = bananas.Count - 1; i >= 0; i--)
            {
                var b = bananas[i];
                b.t += dt;
                b.mesh.Rotate(0, -dt * 0.6f * Mathf.Rad2Deg, 0);
                if (b.t < 0.35f) continue;                       // 置いた本人がすぐ踏まないように
                foreach (var P in players)
                {
                    var p = P.ent.rb.position;
                    if (p.y > 0.9f) continue;                    // 跳び越えているときは踏まない
                    if (new Vector2(p.x - b.p.x, p.z - b.p.z).magnitude > 0.62f) continue;
                    Slip(P);
                    Destroy(b.mesh.gameObject);
                    bananas.RemoveAt(i);
                    break;
                }
            }
        }

        void Slip(Player P)
        {
            if (InvOn(P)) { Say(P, "効かないにゃ！", 0.9f); return; }
            P.slipT = T.bananaSlip;
            P.slipSpin = (Random.value < 0.5f ? -1 : 1) * T.bananaSpin;
            Say(P, "つるっ！！", 1.2f);
            SetFace(P, "hit", 1.0f);
            hud.Pop(P.ent.rb.position + Vector3.up * 0.9f, "つるっ！", 0xf5d020, true);
            if (P.carried != null) P.carried.integ = Mathf.Max(0, P.carried.integ - 22 * P.carried.dish.frag);
        }

        // ================================================================ ドローン
        class Drone { public Player P; public Order order; public Dish dish; public float integ, t; public Transform mesh; }
        readonly List<Drone> drones = new List<Drone>();

        void LaunchDrone(Player P)
        {
            var c = P.carried; if (c == null) return;
            var g = new GameObject("Drone").transform; g.SetParent(fxRoot, false);
            Part.Add(g, MeshGen.Box(0.34f, 0.12f, 0.34f), Mats.Get(0x8fd4ff, 0.4f), Vector3.zero);
            foreach (var d in new[] { new Vector2(-0.22f, -0.22f), new Vector2(0.22f, -0.22f), new Vector2(-0.22f, 0.22f), new Vector2(0.22f, 0.22f) })
                Part.Add(g, MeshGen.Cylinder(0.14f, 0.14f, 0.02f, 12), Mats.Basic(new Color(0.87f, 0.95f, 1f, 0.55f), true), new Vector3(d.x, 0.08f, d.y), false);
            Part.Add(g, MeshGen.Cylinder(0.14f, 0.12f, 0.03f, 14), Mats.Get(0xfaf7f0, 0.9f), new Vector3(0, -0.16f, 0));
            var fd = Part.Add(g, MeshGen.Sphere(0.09f, 10, 8), Mats.Get(c.dish.col), new Vector3(0, -0.11f, 0));
            fd.transform.localScale = new Vector3(1, 0.6f, 1);
            var p = P.ent.rb.position;
            g.position = new Vector3(p.x, 1.2f, p.z);
            drones.Add(new Drone { P = P, order = c.order, dish = c.dish, integ = c.integ, mesh = g });
            // 手放すので、すぐ次のオーダーを取りに行ける
            P.carried = null; P.look.ShowDish(null);
            Say(P, "ドローン発進にゃ！", 1.2f);
        }

        void UpdateDrones(float dt)
        {
            for (int i = drones.Count - 1; i >= 0; i--)
            {
                var d = drones[i]; d.t += dt;
                var tg = d.order.guest.rb.position;
                var m = d.mesh.position;
                float dx = tg.x - m.x, dz = tg.z - m.z, dist = Mathf.Sqrt(dx * dx + dz * dz), step = T.droneSpeed * dt;
                if (dist > step)
                {
                    m.x += dx / dist * step; m.z += dz / dist * step;
                    m.y += (1.4f - m.y) * Mathf.Min(1, 4 * dt);
                    d.mesh.Rotate(0, -dt * 7 * Mathf.Rad2Deg, 0);
                    d.mesh.position = m;
                }
                else
                {
                    m.x = tg.x; m.z = tg.z;
                    m.y += (0.9f - m.y) * Mathf.Min(1, 8 * dt);
                    d.mesh.position = m;
                    if (m.y < 1.05f)
                    {
                        DeliverDish(d.P, d.order, d.integ, tg);
                        Destroy(d.mesh.gameObject);
                        drones.RemoveAt(i);
                    }
                }
            }
        }

        // ================================================================ ブーメラン
        class Boom { public Player owner; public Transform mesh, load; public Material food; public Vector3 dir; public float t; public bool back; public Carried dish; }
        readonly List<Boom> booms = new List<Boom>();

        void ThrowBoomerang(Player P)
        {
            var p = P.ent.rb.position; var fwd = Fwd(P);
            var g = new GameObject("Boomerang").transform; g.SetParent(fxRoot, false);
            var m = Mats.Get(0xe0913a, 0.5f);
            foreach (int sgn in new[] { -1, 1 })
            {
                var arm = Part.Add(g, MeshGen.Box(0.4f, 0.05f, 0.11f), m, Coord.W(sgn * 0.13f, 0, -0.08f));
                arm.transform.localRotation = Part.Euler3(0, sgn * 0.65f, 0);
            }
            var load = new GameObject("Load").transform; load.SetParent(g, false);
            Part.Add(load, MeshGen.Cylinder(0.14f, 0.12f, 0.03f, 14), Mats.Get(0xfaf7f0, 0.9f), new Vector3(0, 0.06f, 0));
            var food = Mats.NewLit(Color.white);
            var fd = Part.Add(load, MeshGen.Sphere(0.09f, 10, 8), food, new Vector3(0, 0.11f, 0));
            fd.transform.localScale = new Vector3(1, 0.6f, 1);
            load.gameObject.SetActive(false);
            g.position = new Vector3(p.x + fwd.x * 0.5f, 0.9f, p.z + fwd.z * 0.5f);
            booms.Add(new Boom { owner = P, mesh = g, load = load, food = food, dir = fwd });
            Say(P, "それっ！", 0.9f);
        }

        void UpdateBooms(float dt)
        {
            for (int i = booms.Count - 1; i >= 0; i--)
            {
                var b = booms[i]; var P = b.owner; var m = b.mesh.position;
                b.t += dt;
                b.mesh.Rotate(0, -dt * 18 * Mathf.Rad2Deg, 0);
                if (!b.back && b.t >= T.boomOut) b.back = true;
                var op = P.ent.rb.position;
                if (!b.back) { m.x += b.dir.x * T.boomSpeed * dt; m.z += b.dir.z * T.boomSpeed * dt; b.mesh.position = m; }
                else
                {
                    float dx = op.x - m.x, dz = op.z - m.z, d = Mathf.Sqrt(dx * dx + dz * dz); if (d < 1e-4f) d = 1;
                    float st = Mathf.Min(d, T.boomSpeed * 1.1f * dt);
                    m.x += dx / d * st; m.z += dz / d * st; b.mesh.position = m;
                    if (d < 0.7f || b.t > 6 || P.down)
                    {
                        if (b.dish != null)
                        {
                            if (P.carried == null && !P.down)
                            {
                                P.carried = b.dish; b.dish.order.taker = P;
                                P.look.ShowDish(b.dish.dish);
                                Say(P, "いただきにゃ！", 1.2f);
                            }
                            else SpillDish(b.dish, new Vector3(m.x, 0.6f, m.z), Vector3.zero, Vector3.zero, P);
                        }
                        Destroy(b.mesh.gameObject); booms.RemoveAt(i);
                        continue;
                    }
                }
                if (b.dish != null) continue;
                // 料理を運んでいるロボから奪う
                foreach (var Q in players)
                {
                    if (Q == P || Q.carried == null || Q.down) continue;
                    if (mode == "team" && Q.team == P.team) continue;      // 味方からは取らない
                    var qp = Q.ent.rb.position;
                    if (Mathf.Abs(qp.y - m.y) > 1.3f || new Vector2(qp.x - m.x, qp.z - m.z).magnitude > T.boomR) continue;
                    b.dish = Q.carried;
                    Q.carried = null; Q.look.ShowDish(null);
                    Say(Q, "取られたにゃ！！", 1.3f); SetFace(Q, "hit", 0.8f);
                    hud.Pop(qp + Vector3.up * 1.1f, "うばった！", 0xe0913a, true);
                    break;
                }
                // 受取口の上を通ったら、次の料理を取る
                if (b.dish == null && oi < orders.Count)
                {
                    var lp = Quaternion.Inverse(counterRot) * (m - counterPos);
                    if (Mathf.Abs(lp.x - PickupOffset) < PickupW / 2 + 0.3f && Mathf.Abs(lp.z) < PickupD / 2 + 0.3f)
                    {
                        var ord = orders[oi]; ord.taker = P;
                        b.dish = new Carried { dish = ord.dish, integ = 100, order = ord };
                        oi++;
                        SyncCounterPlates();
                    }
                }
                if (b.dish != null) { b.back = true; b.load.gameObject.SetActive(true); b.food.SetColor("_BaseColor", Mats.Hex(b.dish.dish.col)); }
            }
        }

        // ================================================================ ビーム・追尾ミサイル
        class Shot { public Player owner, target; public Transform mesh, fire; public Vector3 dir; public float t; public bool homing; }
        readonly List<Shot> shots = new List<Shot>();

        void LaunchShot(Player P, bool homing)
        {
            var p = P.ent.rb.position; var fwd = Fwd(P);
            var g = new GameObject(homing ? "Missile" : "Beam").transform; g.SetParent(fxRoot, false);
            Transform fire;
            // 進む向きをローカル +X に作る（three.js と同じ形）
            if (homing)
            {
                Part.Add(g, MeshGen.Cylinder(0.09f, 0.09f, 0.44f, 10), Mats.Get(0xff6b4a, 0.4f), Vector3.zero).transform.localRotation = Part.Euler3(0, 0, Mathf.PI / 2);
                var nose = Part.Add(g, MeshGen.Cone(0.09f, 0.18f, 10), Mats.Get(0xffd0c0, 0.4f), new Vector3(0.31f, 0, 0));
                nose.transform.localRotation = Part.Euler3(0, 0, -Mathf.PI / 2);
                fire = Part.Add(g, MeshGen.Cone(0.11f, 0.3f, 8), Mats.Basic(new Color(1, 0.69f, 0.23f, 0.9f), true), new Vector3(-0.34f, 0, 0), false).transform;
                fire.localRotation = Part.Euler3(0, 0, Mathf.PI / 2);
            }
            else
            {
                Part.Add(g, MeshGen.Cylinder(0.045f, 0.045f, 1.5f, 8), Mats.Basic(Color.white), Vector3.zero, false).transform.localRotation = Part.Euler3(0, 0, Mathf.PI / 2);
                Part.Add(g, MeshGen.Cylinder(0.13f, 0.13f, 1.5f, 10), Mats.Basic(new Color(0.42f, 0.88f, 1f, 0.45f), true), Vector3.zero, false).transform.localRotation = Part.Euler3(0, 0, Mathf.PI / 2);
                fire = Part.Add(g, MeshGen.Cone(0.16f, 0.34f, 10), Mats.Basic(new Color(0.75f, 0.95f, 1f, 0.85f), true), new Vector3(0.85f, 0, 0), false).transform;
                fire.localRotation = Part.Euler3(0, 0, -Mathf.PI / 2);
            }
            g.position = new Vector3(p.x + fwd.x * 0.7f, 0.55f, p.z + fwd.z * 0.7f);
            g.rotation = Quaternion.FromToRotation(Vector3.right, fwd);
            Player target = null;
            if (homing)
            {
                float bd = float.MaxValue;
                foreach (var Q in players)
                {
                    if (Q == P || Q.down) continue;
                    float d = new Vector2(Q.ent.rb.position.x - p.x, Q.ent.rb.position.z - p.z).magnitude;
                    if (d < bd) { bd = d; target = Q; }
                }
            }
            shots.Add(new Shot { owner = P, mesh = g, fire = fire, dir = fwd, homing = homing, target = target });
            Say(P, homing ? (target != null ? "ロックオンにゃ！" : "撃つにゃ！！") : "ビームにゃ！！", 1.0f);
            SetFace(P, "fast", 0.8f);
        }

        void UpdateShots(float dt)
        {
            const float pad = 6;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var ms = shots[i]; ms.t += dt;
                ms.fire.localScale = Vector3.one * (0.7f + Random.value * 0.6f);
                var m = ms.mesh.position;
                if (ms.homing && ms.target != null && ms.target.ent != null)
                {
                    var tp = ms.target.ent.rb.position;
                    var to = new Vector2(tp.x - m.x, tp.z - m.z).normalized;
                    float cur = Mathf.Atan2(ms.dir.z, ms.dir.x);
                    float want = Mathf.Atan2(to.y, to.x) - cur;
                    while (want > Mathf.PI) want -= Mathf.PI * 2;
                    while (want < -Mathf.PI) want += Mathf.PI * 2;
                    float a2 = cur + Mathf.Clamp(want, -T.homingTurn * dt, T.homingTurn * dt);
                    ms.dir = new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2));
                    ms.mesh.rotation = Quaternion.FromToRotation(Vector3.right, ms.dir);
                }
                float step = (ms.homing ? T.homingSpeed : T.missileSpeed) * dt;
                // 進む先へレイを飛ばして、当たったらそこで爆発（速すぎて薄い物をすり抜けないように）
                RaycastHit hit;
                if (Physics.Raycast(m, ms.dir, out hit, step + 0.25f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var he = hit.collider.GetComponentInParent<Ent>();
                    if (ms.owner != null && he == ms.owner.ent) { ms.mesh.position = m + ms.dir * step; continue; }   // 撃った本人には当たらない
                    Explode(ms.owner, m + ms.dir * hit.distance, ms.homing, 0);
                    Destroy(ms.mesh.gameObject); shots.RemoveAt(i);
                    continue;
                }
                m += ms.dir * step;
                ms.mesh.position = m;
                float life = ms.homing ? T.homingLife : 8;
                var B = bounds;
                if (m.x < B.x0 - pad || m.x > B.x1 + pad || m.z < B.z0 - pad || m.z > B.z1 + pad || ms.t > life)
                { Destroy(ms.mesh.gameObject); shots.RemoveAt(i); }
            }
        }

        // ================================================================ 弾道ミサイル（押しっぱなしで照準、離して発射）
        public bool AimItemOfPublic(Player P) { return AimItemOf(P) != null; }

        ItemDef AimItemOf(Player P)
        {
            if (P.slot == null) return null;
            var it = ItemOf(P.slot);
            if (it == null || !it.aim) return null;
            return WalletOf(P).Has(it.k) > 0 ? it : null;
        }

        Transform AimMesh(int col)
        {
            var g = new GameObject("Aim").transform; g.SetParent(fxRoot, false);
            var c = Mats.Hex(col);
            var ring = Part.Add(g, MeshGen.Torus(1.5f, 0.07f, 8, 40), Mats.Basic(new Color(c.r, c.g, c.b, 0.9f), true, false, false), Vector3.zero, false);
            ring.transform.localRotation = Part.Euler3(-Mathf.PI / 2, 0, 0);
            var inner = Part.Add(g, MeshGen.Torus(0.42f, 0.05f, 8, 26), Mats.Basic(new Color(c.r, c.g, c.b, 0.8f), true, false, false), Vector3.zero, false);
            inner.transform.localRotation = Part.Euler3(-Mathf.PI / 2, 0, 0);
            foreach (float a in new[] { 0f, Mathf.PI / 2 })
            {
                var bar = Part.Add(g, MeshGen.Box(2.6f, 0.04f, 0.09f), Mats.Basic(new Color(c.r, c.g, c.b, 0.55f), true, false, false), Vector3.zero, false);
                bar.transform.localRotation = Part.Euler3(0, a, 0);
            }
            return g;
        }

        void StartAim(Player P)
        {
            if (P.aim != null) return;
            var p = P.ent.rb.position; var fwd = Fwd(P);
            float d = Mathf.Min(T.ballRange, 4.5f);              // 正面すこし先から（足元からだと自爆しやすい）
            P.aim = new Vector3(p.x + fwd.x * d, 0.06f, p.z + fwd.z * d);
            P.aimMesh = AimMesh(P.col);
            P.aimMesh.position = P.aim.Value;
            Say(P, "狙うにゃ…", 0.8f);
        }

        void ClearAim(Player P)
        {
            if (P.aimMesh != null) { Destroy(P.aimMesh.gameObject); P.aimMesh = null; }
            P.aim = null;
        }

        void MoveAim(Player P, float dt)
        {
            var inp = P.input;
            int vx = (inp.navRight ? 1 : 0) - (inp.navLeft ? 1 : 0);
            int vz = (inp.navUp ? 1 : 0) - (inp.navDown ? 1 : 0);        // 画面の上 = Unity の +Z
            var a = P.aim.Value;
            if (vx != 0 || vz != 0)
            {
                float k = (vx != 0 && vz != 0) ? 0.7071f : 1;
                a.x += vx * T.ballAimSpeed * k * dt; a.z += vz * T.ballAimSpeed * k * dt;
            }
            var p = P.ent.rb.position;
            var d = new Vector2(a.x - p.x, a.z - p.z);
            if (d.magnitude > T.ballRange) { d = d.normalized * T.ballRange; a.x = p.x + d.x; a.z = p.z + d.y; }
            P.aim = a;
            if (P.aimMesh != null) P.aimMesh.position = new Vector3(a.x, 0.06f, a.z);
        }

        /// <summary>毎フレーム。押している間は照準、離したら発射（JS版 tickAim）。</summary>
        void TickAim(Player P, float dt)
        {
            var it = AimItemOf(P);
            bool hold = P.input.use;
            if (it == null || P.down) { if (P.aim != null) ClearAim(P); return; }
            if (P.aim == null)
            {
                if (hold && !P.prevUse) StartAim(P);
                return;
            }
            if (hold) { MoveAim(P, dt); return; }
            UseItem(P);
            ClearAim(P);
        }

        class Ball { public Player owner; public Vector3 a, b; public Transform mesh, mark, fire; public Material fireM; public float t, puff; }
        readonly List<Ball> balls = new List<Ball>();

        bool LaunchBallistic(Player P)
        {
            if (P.aim == null) return false;
            var p = P.ent.rb.position;
            // 形：機首を +Y に作っておく（飛ぶ向きへ向けるのが楽なので）
            var g = new GameObject("Ballistic").transform; g.SetParent(fxRoot, false);
            Part.Add(g, MeshGen.Cylinder(0.11f, 0.11f, 0.72f, 12), Mats.Get(0xf2f4f7, 0.45f), Vector3.zero);
            Part.Add(g, MeshGen.Cylinder(0.115f, 0.115f, 0.16f, 12), Mats.Get(P.col, 0.4f), new Vector3(0, 0.12f, 0));
            Part.Add(g, MeshGen.Cone(0.11f, 0.34f, 12), Mats.Get(0xe53935, 0.35f), new Vector3(0, 0.53f, 0));
            for (int i = 0; i < 3; i++)
            {
                var pv = new GameObject("Fin").transform; pv.SetParent(g, false);
                pv.localRotation = Part.Euler3(0, i * Mathf.PI * 2 / 3, 0);
                Part.Add(pv, MeshGen.Box(0.03f, 0.22f, 0.20f), Mats.Get(0xd4d8de, 0.5f), Coord.W(0, -0.30f, 0.13f));
            }
            var fm = Mats.NewBasic(new Color(1, 0.69f, 0.23f, 0.9f), true);
            var fire = Part.Add(g, MeshGen.Cone(0.10f, 0.36f, 10), fm, new Vector3(0, -0.55f, 0), false).transform;
            fire.localRotation = Part.Euler3(Mathf.PI, 0, 0);
            g.localScale = Vector3.one * 1.45f;                   // 実寸だと点にしか見えない
            var mk = AimMesh(P.col); mk.localScale = Vector3.one * 0.8f; mk.position = new Vector3(P.aim.Value.x, 0.05f, P.aim.Value.z);
            balls.Add(new Ball { owner = P, a = p, b = P.aim.Value, mesh = g, mark = mk, fire = fire, fireM = fm });
            Say(P, "そこだにゃーっ！", 1.2f);
            SetFace(P, "fast", 0.9f);
            return true;
        }

        void UpdateBalls(float dt)
        {
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                var b = balls[i]; b.t += dt;
                float u = Mathf.Min(1, b.t / Mathf.Max(0.1f, T.ballFlight));
                float x = b.a.x + (b.b.x - b.a.x) * u, z = b.a.z + (b.b.z - b.a.z) * u;
                float y = 0.5f + T.ballHeight * 4 * u * (1 - u);
                b.mesh.position = new Vector3(x, y, z);
                var dir = new Vector3(b.b.x - b.a.x, T.ballHeight * 4 * (1 - 2 * u), b.b.z - b.a.z).normalized;
                b.mesh.rotation = Quaternion.FromToRotation(Vector3.up, dir);
                float k = Mathf.Max(0.25f, 1 - u) * (0.8f + 0.4f * Random.value);
                b.fire.localScale = new Vector3(1, k, 1);
                SetA(b.fireM, 0.35f + 0.55f * Mathf.Max(0, 1 - u));
                b.puff += dt;
                if (b.puff > 0.05f) { b.puff = 0; SpawnSplash(new Vector3(x, y - 0.4f, z), 0xdfe4ea, 1); }
                b.mark.gameObject.SetActive(Mathf.FloorToInt(b.t * 8) % 2 == 0);      // 落ちる所の印は点滅
                if (u < 1) continue;
                Destroy(b.mesh.gameObject); Destroy(b.mark.gameObject);
                balls.RemoveAt(i);
                Explode(b.owner, new Vector3(b.b.x, 0.35f, b.b.z), true, T.ballRadius);
            }
        }

        // ================================================================ 爆発
        class Blast { public Transform mesh; public Material m; public float t; public bool fast; }
        readonly List<Blast> blasts = new List<Blast>();

        /// <summary>
        /// 半径内のものを、距離に応じた力で吹っ飛ばす。車は押されない物なので、その場で動く物に切り替えて飛ばす。
        /// boom … true=ミサイル（モクモクと「ボカーン」）／ false=レーザー（閃光と「ジュッ」）。radius 0 でふつうの爆風。
        /// </summary>
        void Explode(Player owner, Vector3 at, bool boom, float radius)
        {
            float R = radius > 0 ? radius : T.blastRadius;
            var m = Mats.NewBasic(boom ? new Color(1, 0.82f, 0.42f, 0.85f) : new Color(0.74f, 0.95f, 1f, 0.95f), true);
            var fl = Part.Add(fxRoot, MeshGen.Sphere(1, 16, 12), m, at, false, "Blast").transform;
            fl.localScale = Vector3.one * 0.35f;
            blasts.Add(new Blast { mesh = fl, m = m, fast = !boom });
            if (boom) SpawnPuffs(at); else SpawnFlash(at);
            SpawnSplash(at, boom ? 0xff9040 : 0xdff6ff, boom ? 14 : 8);

            // 写しを回す（料理を落とすと ents に物が増えるため）
            foreach (var e in ents.ToArray())
            {
                if (e == null || e.rb == null) continue;
                if (e.kind == "floor" || e.kind == "pit" || e.kind == "wall" || e.kind == "counter" || e.kind == "fence") continue;
                var p = e.rb.position;
                var dvec = p - at; float dist = dvec.magnitude;
                if (dist > R) continue;
                float f = 1 - dist / R, len = dist > 1e-4f ? dist : 1;
                var car = e.kind == "car" ? cars.Find(c => c.ent == e) : null;
                if (car != null && car.blown <= 0)
                {
                    // 車をここで物にする。数秒後に道の端から入り直す
                    car.blown = T.carRespawn;
                    e.rb.isKinematic = false; e.infMass = false;
                }
                if (e.rb.isKinematic) continue;
                e.rb.AddForce(new Vector3(dvec.x / len * f * T.blastForce, f * T.blastForce * 0.55f + 2, dvec.z / len * f * T.blastForce), ForceMode.Impulse);
                e.rb.AddTorque(new Vector3((Random.value - 0.5f) * f * 8, (Random.value - 0.5f) * f * 6, (Random.value - 0.5f) * f * 8), ForceMode.Impulse);
                if (e.kind == "guest")
                {
                    float was = e.hp;
                    e.hp = Mathf.Max(0, e.hp - GuestHurt(f * T.blastGuestDmg));
                    e.walkStop = true;
                    if (owner != null && was > 50 && e.hp <= 50) owner.hurt++;
                    if (was > 0 && e.hp <= 0) GuestDown(e);
                }
                if (e.kind == "table" || e.kind == "chair")
                {
                    float d2 = f * T.blastShopDmg;
                    shopDmg = Mathf.Min(100, shopDmg + d2);
                    if (owner != null) owner.shopDmg = Mathf.Min(100, owner.shopDmg + d2);
                    e.blame = owner;
                }
                if (e.player != null && !InvOn(e.player))
                {
                    var P = e.player;
                    P.botDmg = Mathf.Min(100, P.botDmg + Toughed(f * T.blastBotDmg));
                    SetFace(P, "hit", 1.0f);
                    if (P != owner) Say(P, "うわーっ！", 1.2f);
                    // 近くで爆発したら、運んでいる料理を落とす（爆心の反対側へ）
                    if (T.dropBlast > 0 && f >= T.dropBlast && P.carried != null && !P.down)
                    {
                        var h = new Vector2(dvec.x, dvec.z); if (h.magnitude < 1e-4f) h = Vector2.right;
                        h = h.normalized;
                        DropDish(P, new Vector3(h.x * 2.2f, 0, h.y * 2.2f));
                    }
                    if (P.botDmg >= 100) DownPlayer(P, 0);
                }
            }
            hud.Pop(at + Vector3.up * 1.0f, boom ? "ボカーン！" : "ジュッ！", boom ? 0xff8a3d : 0x49d8ff, true);
        }

        void UpdateBlasts(float dt)
        {
            for (int i = blasts.Count - 1; i >= 0; i--)
            {
                var b = blasts[i]; b.t += dt;
                float k = b.t / (b.fast ? 0.22f : 0.45f);
                b.mesh.localScale = Vector3.one * (0.35f + k * T.blastRadius * (b.fast ? 0.75f : 1.1f));
                SetA(b.m, Mathf.Max(0, 0.85f * (1 - k)));
                if (k >= 1) { Destroy(b.mesh.gameObject); Destroy(b.m); blasts.RemoveAt(i); }
            }
            // 吹っ飛ばした車を、道の入口へ戻す
            foreach (var car in cars)
            {
                if (car.blown <= 0) continue;
                car.blown -= dt;
                if (car.blown > 0) continue;
                car.blown = 0;
                var rb = car.ent.rb;
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true; car.ent.infMass = true;
                var B = bounds; const float pad = 3;
                var t = new Vector3(car.home.x, car.home.y, car.home.z);
                if (Mathf.Abs(car.dir.x) > 0.5f) t.x = car.dir.x > 0 ? B.x0 - pad : B.x1 + pad;
                else t.z = car.dir.z > 0 ? B.z0 - pad : B.z1 + pad;
                rb.position = t; car.ent.transform.position = t;
                rb.rotation = car.homeRot; car.ent.transform.rotation = car.homeRot;
            }
        }

        // ================================================================ 毎フレーム
        void UpdateItems(float dt)
        {
            UpdateBananas(dt);
            UpdateDrones(dt);
            UpdateBooms(dt);
            UpdateShots(dt);
            UpdateBalls(dt);
            UpdateBlasts(dt);
        }

        void ClearItems()
        {
            bananas.Clear(); drones.Clear(); booms.Clear(); shots.Clear(); balls.Clear(); blasts.Clear();
        }

        Vector3 Fwd(Player P)
        {
            var f = P.ent.rb.rotation * Vector3.forward; f.y = 0; return f.normalized;
        }

        /// <summary>
        /// 無敵の見た目。虹色に光りながら点滅する（終わり3秒は、ついたり消えたり）。
        /// 光らせるのはその人の機体の素材だけ。
        /// </summary>
        void InvGlow(Player P)
        {
            bool on = P.invT > 0 && !P.down;
            foreach (var m in new[] { P.look.body, P.look.bodyDmg })
            {
                if (on)
                {
                    float t = Time.time;
                    bool lit = P.invT >= 3 || Mathf.FloorToInt(t * 10) % 2 == 0;
                    var c = Color.HSVToRGB((t * 2.2f) % 1, 1, 1) * (lit ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 14)) : 0);
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", c);
                }
                else if (m.IsKeywordEnabled("_EMISSION"))
                {
                    m.SetColor("_EmissionColor", Color.black);
                    m.DisableKeyword("_EMISSION");
                }
            }
        }
    }
}
