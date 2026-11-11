using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    // ================================================================ 高額オーダー・VIP オーダー・速達ボーナス（JS版と同じ決まり）
    //   高額オーダー … 受取口に新しい皿が並んだとき richRate の確率で高額に（1回に1つ。RichTick）。値段は richMin〜richMax。
    //     受取口に並んだら、列の先頭でなくても**先に取る**（CheckPickupDelivery）。料理はその店の特別な料理（shop.rich）
    //   VIP オーダー … **個人戦で2人以上**のとき、vipEvery 秒ごとに**その時の1位限定**の注文が受取口の横の VIP 台に出る。
    //     誰でも取れる。vipTime 秒以内に届けたら**届けた人**に vipPrice（崩れ具合の掛け目は掛ける）。
    //     届けられなかったら失敗で、同じ額を1位以外で山分け。VIP の注文は vips に持つ（orders には入れない）
    //   速達 … 受取口で受け取ったときに客までの目安の時間を決め、それより早ければ 1+rushBonus 倍、
    //     過ぎると rushFade 秒かけて1倍まで下がる（VIP には付けない）
    public partial class Game
    {
        [System.NonSerialized] public readonly List<Order> vips = new List<Order>();
        [System.NonSerialized] public Order vipCur;
        [System.NonSerialized] public Vector3? vipPos;
        float vipT;
        int richOi;
        bool richShown;

        /// <summary>「どの注文か」の番号。VIP は orders の続きとして番号を振る（オンラインで配るとき）。</summary>
        public int OrdIdx(Order o) { return o == null ? -1 : o.vip ? orders.Count + vips.IndexOf(o) : orders.IndexOf(o); }
        public Order OrdAt(int i) { return i < 0 ? null : i < orders.Count ? orders[i] : (i - orders.Count < vips.Count ? vips[i - orders.Count] : null); }

        void RichReset()
        {
            vips.Clear(); vipCur = null; vipPos = null;
            vipT = T.vipEvery; richOi = 0; richShown = false;
            hud.VipReset();
        }

        // ストーリーで、高額（k="rich"）・VIP（k="vip"）を出してよいか。コースで "richStart" / "vipStart" を書いた面**から**出す
        bool StoryAllows(string k)
        {
            if (!IsStory || course == null) return true;
            for (int i = 0; i <= courseIndex && i < course.stages.Count; i++)
            {
                var raw = course.stages[i].raw;
                if (raw != null && raw[k + "Start"] != null && (bool)raw[k + "Start"]) return true;
            }
            return false;
        }
        // いまの面が、その解禁の面か（初めて出す面では、確実に出るようにする）
        bool StoryFirst(string k)
        {
            if (!IsStory || entry == null || entry.raw == null) return false;
            return entry.raw[k + "Start"] != null && (bool)entry.raw[k + "Start"];
        }

        // 高額オーダーの抽選。**受取口に新しい皿が並んだとき**（列が進んだとき）だけ引く。
        //   面の始まりに並んでいる皿は高額にしない／高額がまだ残っている間は出さない（1回に1つ）
        void RichTick()
        {
            bool adv = oi > richOi;
            richOi = oi;
            if (!adv || !StoryAllows("rich")) return;
            foreach (var x in orders) if (x.rich && !x.done) return;
            int nVis = Mathf.Min(counterPlates.Count > 0 ? counterPlates.Count : 3, orders.Count - oi);
            if (nVis <= 0) return;
            var o = orders[oi + nVis - 1];                         // いま並んだ、いちばん後ろの皿
            if (o == null || o.done) return;
            bool first = StoryFirst("rich") && !richShown;
            if (!first && Rnd() >= T.richRate) return;
            o.rich = true; richShown = true;
            var rd = Dishes.RichOf(stage, shop); if (rd != null) o.dish = rd;
            float lo = Mathf.Min(T.richMin, T.richMax), hi = Mathf.Max(T.richMin, T.richMax);
            o.richAmt = Mathf.Round((lo + Rnd() * (hi - lo)) / 500) * 500;
            SyncCounterPlates();
        }

        /// <summary>満点のときの値段（配膳コンボは別）。高額・VIP は料理によらず決まった額。</summary>
        public float OrdPrice(Order o)
        {
            if (o.vip) return T.vipPrice;
            if (o.rich) return o.richAmt > 0 ? o.richAmt : T.richMin;
            return o.dish.price * shop.price;
        }

        bool VipOn() { return T.vipOn && mode == "versus" && players.Count >= 2 && StoryAllows("vip"); }

        // その時の1位（同点なら抽選）。動けない人（リタイア）は外す
        Player VipLeader()
        {
            var live = players.FindAll(P => !(P.down && P.revT <= 0));
            if (live.Count == 0) return null;
            var tot = new Dictionary<Player, float>();
            foreach (var P in live) tot[P] = LedgerOf(P).total;
            float mx = float.MinValue; foreach (var v in tot.Values) mx = Mathf.Max(mx, v);
            var top = live.FindAll(P => tot[P] >= mx - 0.5f);
            return top[Mathf.Min(top.Count - 1, (int)(Rnd() * top.Count))];
        }

        // VIP 台の場所。受取口の列の端の先（カウンターの端の前のマス）。床でなければ前へずらす
        Vector3 VipSpot()
        {
            float e = CounterD / 2 + 0.9f;
            var tries = new[] { new Vector2(PickupOffset, e), new Vector2(PickupOffset, -e), new Vector2(PickupOffset + 1.0f, e - 1.0f),
                                new Vector2(PickupOffset + 1.0f, -(e - 1.0f)), new Vector2(PickupOffset + 1.5f, 0) };
            foreach (var t2 in tries)
            {
                var w = counterPos + counterRot * Coord.W(t2.x, 0, t2.y);
                var td = Tiles.Def(Tiles.At(map, w));
                if (td != null && td.floor && !propBlock.Contains(new Vector2Int(Coord.ToI(w.x), Coord.ToJ(w.z)))) return new Vector3(w.x, 0, w.z);
            }
            var d = counterPos + counterRot * Coord.W(PickupOffset + 1.5f, 0, 0);
            return new Vector3(d.x, 0, d.z);
        }

        void VipSpawn()
        {
            var L = VipLeader();
            var menu = Dishes.MenuOf(stage, shop);
            var gs = guests.FindAll(g => g.hp > 0 && !g.sunk && !g.taken);
            if (L == null || gs.Count == 0 || menu.Count == 0) return;
            var pick = menu[Mathf.Min(menu.Count - 1, (int)(Rnd() * menu.Count))];   // 乱数の並びを変えないよう、引くだけは引く
            var o = new Order { dish = Dishes.Find("vip_cloche") ?? pick, guest = gs[Mathf.Min(gs.Count - 1, (int)(Rnd() * gs.Count))],
                                vip = true, leader = L, left = T.vipTakeTime, at = "stand" };   // 台から取るまでの制限（担当）
            vips.Add(o); vipCur = o;
            vipPos = VipSpot();
            NetEv("vip", 0, players.IndexOf(L), 0);
            hud.VipNotice(0, L, 0);
            Se("go", vipPos.Value + Vector3.up * 0.5f, null, 1, 0.7f);
        }

        // 台から取る（誰でも取れる）
        void VipTake(Player P)
        {
            var o = vipCur;
            if (o == null || o.at != "stand") return;
            o.at = "held";
            o.left = T.vipTime;                               // 取ったら、届けるまでの時間を数え直す（担当）
            P.carried = new Carried { dish = o.dish, integ = 100, order = o };
            o.taker = P;
            P.look.ShowDish(o.dish);
            Say(P, P == o.leader ? "VIPにゃ！急ぐにゃ！" : "横取りにゃ！", 1.3f);
            Se("pick", P.ent.rb.position, P);
        }

        // 届けた。**届けた人が受け取る**（1位でも、横取りした人でも）
        void VipDeliver(Player P, Order o, float integ, Vector3 at)
        {
            if (o.done) return;
            P.delivered++;
            o.done = true; o.res = "ok";
            float tier = Dishes.PriceTier(integ);
            float amt = OrdPrice(o) * tier;
            P.sales += amt;
            bool steal = P != o.leader;
            hud.SalePop(at + Vector3.up * 0.9f, "VIP", OrdPrice(o), new[] { tier }, new[] { "状態" }, amt);
            Say(P, steal ? "いただきにゃ！" : "VIP成功にゃ！", 1.5f);
            Se("tut_ok", at, P);
            NetEv("vip", 1, players.IndexOf(P), Mathf.RoundToInt(amt), steal ? 1 : 0);
            hud.VipNotice(1, P, amt);
            VipClose(o);
        }

        // 失敗。同じ額を1位以外で山分けする
        void VipFail(Order o)
        {
            if (o.res != null) return;
            o.done = true; o.res = "ng";
            float amt = OrdPrice(o);
            var rest = players.FindAll(q => q != o.leader);
            float each = rest.Count > 0 ? amt / rest.Count : 0;
            foreach (var q in rest)
            {
                q.sales += each;
                hud.Pop(q.ent.rb.position + Vector3.up * 1.1f, "+" + Yen(each), 0xe6a800, false);
            }
            Se("ng", null, null, 1, 0.8f);
            NetEv("vip", 2, players.IndexOf(o.leader), Mathf.RoundToInt(amt));
            hud.VipNotice(2, o.leader, amt);
        }

        // 手に持っている・床に落ちている VIP の料理を片付ける
        void VipClose(Order o)
        {
            foreach (var P in players)
                if (P.carried != null && P.carried.order == o) { P.carried = null; P.look.ShowDish(null); }
            foreach (var d in dropped.ToArray()) if (d.order == o) KillDropped(d);
            if (vipCur == o) vipCur = null;
        }

        // 毎ステップ（ホストだけ）。時間切れと、次の VIP を出す
        void VipTick(float dt)
        {
            if (state != "play" || !VipOn()) return;
            var o = vipCur;
            if (o != null)
            {
                o.left -= dt;
                if (o.left <= 0) { VipFail(o); VipClose(o); }
            }
            vipT -= dt;
            if (vipT <= 0) { vipT = T.vipEvery; if (vipCur == null) VipSpawn(); }
        }

        // NPC の行き先（手ぶらのとき）。1位は VIP 台へ。ほかは1位より台に近ければ横取りしに行き、
        // 1位が VIP を運んでいる間は、近ければ追いかけてぶつかりに行く。無ければ null
        Vector3? NpcVipGoal(Player P, Vector3 p)
        {
            var o = vipCur;
            if (o == null || o.leader == null) return null;
            var L = o.leader;
            if (o.at == "stand" && vipPos != null)
            {
                var v = vipPos.Value;
                if (P == L) return v;
                var lp = L.ent.rb.position;
                float mine = new Vector2(v.x - p.x, v.z - p.z).magnitude, his = new Vector2(v.x - lp.x, v.z - lp.z).magnitude;
                if (L.carried != null || L.down || mine < his) return v;
                return null;
            }
            if (P != L && L.carried != null && L.carried.order == o && !L.down)
            {
                var lp = L.ent.rb.position;
                if (new Vector2(lp.x - p.x, lp.z - p.z).magnitude < 6) return new Vector3(lp.x, 0, lp.z);
            }
            return null;
        }

        // ---- 速達ボーナス。受取口で受け取ったときに、その客までの目安の時間を決めておく。
        // 道のりは NPC と同じ道探し（マス目）で測る。マス目の道は斜めに進めないぶん長く出るので少し縮め、
        // 真っすぐの距離より短くはしない。道が見つからなければ真っすぐの距離
        void RushStart(Order ord, Vector3 from)
        {
            ord.pickF = frames;
            var gp = ord.guest.rb.position;
            float straight = new Vector2(gp.x - from.x, gp.z - from.z).magnitude;
            var to = new Vector2Int(Coord.ToI(gp.x), Coord.ToJ(gp.z));
            var nf = Tiles.NearestFloor(map, gp);
            char ch; if (!map.TryGetValue(to, out ch) || !Tiles.IsFloor(ch)) { if (nf != null) to = new Vector2Int(Coord.ToI(nf.Value.x), Coord.ToJ(nf.Value.z)); }
            var path = FindPath(new Vector2Int(Coord.ToI(from.x), Coord.ToJ(from.z)), to, true);
            float dist = Mathf.Max(straight, path != null ? path.Count * Coord.Tile * 0.85f : 0);
            ord.rushPar = dist / Mathf.Max(0.5f, T.rushRef) + T.rushGrace;
        }
        // いまの倍率（1〜1+rushBonus）。ゲストの画面はホストから届いた値（carried.rush）を使う
        public float RushMult(Order ord)
        {
            if (!T.rushOn || ord == null || ord.vip || ord.pickF < 0) return 1;
            float tt = (frames - ord.pickF) / 60f;
            float k = Mathf.Clamp01(1 - (tt - ord.rushPar) / Mathf.Max(0.1f, T.rushFade));
            return 1 + T.rushBonus * k;
        }
        public float CarriedRush(Player P)
        {
            if (P.carried == null) return 1;
            return NetGuest ? (P.carried.rush > 0 ? P.carried.rush : 1) : RushMult(P.carried.order);
        }

        // ---- VIP 台（見た目だけ。当たり判定なし）。金の台座・柱・天板に赤い布。料理は天板の上でゆっくり回る
        const float VIP_TOP = 0.78f;
        class VipLook { public Transform g, dish, ring, beam, wave; public Material beamM, waveM; public string key = ""; }
        VipLook vipLook;
        void VipStandMesh()
        {
            var V = vipLook = new VipLook();
            V.g = new GameObject("VipStand").transform; V.g.SetParent(stageRoot, false);
            var gold = Mats.Get(0xffc400, 0.35f, 0.6f);
            Part.Add(V.g, MeshGen.Cylinder(0.42f, 0.48f, 0.07f, 28), gold, new Vector3(0, 0.035f, 0), false, "Base");
            Part.Add(V.g, MeshGen.Cylinder(0.2f, 0.3f, 0.12f, 24), gold, new Vector3(0, 0.13f, 0), true, "Foot");
            Part.Add(V.g, MeshGen.Cylinder(0.075f, 0.1f, VIP_TOP - 0.28f, 18), gold, new Vector3(0, 0.19f + (VIP_TOP - 0.28f) / 2, 0), true, "Pole");
            var knob = Part.Add(V.g, MeshGen.Sphere(0.11f, 18, 12), gold, new Vector3(0, VIP_TOP - 0.11f, 0), true, "Knob");
            knob.transform.localScale = new Vector3(1, 0.6f, 1);
            Part.Add(V.g, MeshGen.Cylinder(0.3f, 0.24f, 0.05f, 32), gold, new Vector3(0, VIP_TOP - 0.025f, 0), true, "Top");
            Part.Add(V.g, MeshGen.Cylinder(0.27f, 0.27f, 0.012f, 32), Mats.Get(0xb0122a, 0.8f), new Vector3(0, VIP_TOP + 0.006f, 0), false, "Cloth");
            var ringM = Mats.NewBasic(new Color(1f, 0.88f, 0.4f, 0.9f), true, false, false);
            V.ring = Part.Add(V.g, MeshGen.Torus(0.55f, 0.035f, 8, 36), ringM, new Vector3(0, 0.05f, 0), false, "Ring").transform;
            V.ring.localRotation = Part.Euler3(Mathf.PI / 2, 0, 0);
            V.beamM = Mats.NewBasic(new Color(1f, 0.85f, 0.29f, 0.3f), true, true, false);
            V.beam = Part.Add(V.g, MeshGen.Cylinder(0.36f, 0.46f, 4.0f, 24, true), V.beamM, new Vector3(0, 2.0f, 0), false, "Beam").transform;
            V.waveM = Mats.NewBasic(new Color(1f, 0.88f, 0.4f, 0.8f), true, false, false);
            V.wave = Part.Add(V.g, MeshGen.Torus(0.56f, 0.03f, 6, 40), V.waveM, new Vector3(0, 0.04f, 0), false, "Wave").transform;
            V.wave.localRotation = Part.Euler3(Mathf.PI / 2, 0, 0);
            V.dish = new GameObject("Dish").transform; V.dish.SetParent(V.g, false);
            V.dish.localPosition = new Vector3(0, VIP_TOP + 0.012f, 0);
        }

        // 毎フレーム（ホストもオンラインの参加者も）。VIP 台を出し入れする
        void VipRender(float dt)
        {
            var o = vipCur;
            bool onStand = o != null && o.at == "stand" && vipPos != null;
            if (onStand && (vipLook == null || vipLook.g == null)) VipStandMesh();
            if (vipLook == null || vipLook.g == null) return;
            vipLook.g.gameObject.SetActive(onStand);
            if (!onStand) return;
            vipLook.g.position = vipPos.Value;
            if (vipLook.key != o.dish.k)
            {
                vipLook.key = o.dish.k;
                foreach (Transform c in vipLook.dish) Destroy(c.gameObject);
                var pl = Part.Add(vipLook.dish, MeshGen.Cylinder(0.17f, 0.15f, 0.03f, 16), Mats.Get(0xfaf7f0, 0.9f), Vector3.zero);
                DishLook.Build(vipLook.dish, o.dish, 0.09f, pl, 0.4f);
            }
            vipLook.dish.Rotate(0, -dt * 0.8f * Mathf.Rad2Deg, 0);
            float pu = 1 + 0.12f * Mathf.Sin(t * 6);
            vipLook.ring.localScale = new Vector3(pu, pu, 1);
            var bc = vipLook.beamM.GetColor("_BaseColor"); bc.a = 0.22f + 0.14f * (0.5f + 0.5f * Mathf.Sin(t * 5)); vipLook.beamM.SetColor("_BaseColor", bc);
            float wk = (t * 0.9f) % 1;
            vipLook.wave.localScale = Vector3.one * (1 + wk * 2.2f);
            var wc = vipLook.waveM.GetColor("_BaseColor"); wc.a = 0.8f * (1 - wk); vipLook.waveM.SetColor("_BaseColor", wc);
        }

        // 制限時間の残り10秒から、1秒ごとに鳴らす
        int tickS = -1;
        void TimeTick()
        {
            if (state != "play") return;
            int tl = TimeLeftFrames();
            if (tl < 0) return;
            int s = Mathf.CeilToInt(tl / 60f);
            if (s != tickS) { if (tickS >= 0 && s <= 10 && s > 0) SeUi("tick"); tickS = s; }
        }
    }
}
