using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    // ================================================================ 厨房とシェフ（受取カウンターの後ろ。JS版 buildChef / chefTick）
    // 担当「受取カウンターで、後ろ側に厨房設備を作り、シェフを一人置きたい（動いてる、料理してる感じで）」。
    // **受け渡し口（窓）の奥の厨房の中**にコンロと調理台を置いて、シェフを1人立たせる（窓からのぞくと作業している）。
    // 座標はカウンターと同じ（ローカル +X が受取面）。three.js の向きで書いて、置くときに Coord.W で直す。
    //   コンロ … 奥の左。フライパンと鍋、火がゆらぐ。鍋から湯気
    //   調理台 … 奥の右。まな板・包丁・野菜
    //   シェフ … 窓のすぐ奥。コンロであおる → 歩く → 調理台で切る → … をくり返し、受取口の列が進んだら窓まで次の皿を出しに来る
    // 厨房のモデルがあって、**カウンターの後ろが壁（高い壁でない）の面だけ**作る。見た目だけ（当たり判定なし）
    public partial class Game
    {
        class ChefS
        {
            public Transform g, chef, body, pan, knife; public Transform[] arms;
            public List<Transform> bits = new List<Transform>(), fires = new List<Transform>();
            public List<Transform> steam = new List<Transform>(); public List<Material> steamM = new List<Material>();
            public List<float> steamT = new List<float>(); public List<Vector3> bitO = new List<Vector3>();
            public string st = "cook", next; public float t, dur; public int oi;
            public Vector3 from, to; public float toRy;          // three.js の向きの (x, 0, z) と向き
            public float X0;
        }
        ChefS chefS;
        static readonly Vector3 CHEF_COOK = new Vector3(-1.12f, 0, -0.85f), CHEF_CHOP = new Vector3(-1.12f, 0, 0.85f);

        void BuildChef()
        {
            chefS = null;
            if (stage == null || stage.stall || kitchenModel == null || kitchen == null) return;
            var bw = kitchen.TransformPoint(Coord.W(-1, 0, 0));
            var td = Tiles.Def(Tiles.At(map, bw));
            if (td == null || !td.wall || td.high) return;
            var S = new ChefS { X0 = -1.62f, oi = oi };
            var g = new GameObject("Chef").transform; g.SetParent(kitchen, false); S.g = g;
            var steel = Mats.Get(0xc9ced6, 0.35f, 0.55f);
            var dark = Mats.Get(0x2b2d31, 0.6f, 0.3f);
            System.Func<float, float, float, Material, float, float, float, Transform> box = (w, h, d, m, x, y, z) =>
                Part.Add(g, MeshGen.Box(w, h, d), m, Coord.W(x, y, z), true, "Box").transform;
            float X0 = S.X0;
            foreach (var zc in new[] { -0.85f, 0.85f }) { box(0.4f, 0.86f, 0.7f, steel, X0, 0.43f, zc); box(0.42f, 0.04f, 0.72f, steel, X0, 0.88f, zc); }
            box(0.38f, 0.03f, 0.68f, dark, X0, 0.915f, -0.85f);
            foreach (var z in new[] { -1.05f, -0.65f })
            {
                var r = Part.Add(g, MeshGen.Torus(0.1f, 0.015f, 6, 18), dark, Coord.W(X0, 0.935f, z), false, "Burner");
                r.transform.localRotation = Part.Euler3(Mathf.PI / 2, 0, 0);
            }
            // 火（光る円すい。ゆらす）
            var fireM = Mats.NewBasic(new Color(1f, 0.48f, 0.1f, 0.85f), true, false, false);
            foreach (var z in new[] { -1.05f, -0.65f })
                for (int k = 0; k < 5; k++)
                {
                    float a = k / 5f * Mathf.PI * 2;
                    S.fires.Add(Part.Add(g, MeshGen.Cone(0.025f, 0.08f, 6), fireM, Coord.W(X0 + Mathf.Cos(a) * 0.08f, 0.97f, z + Mathf.Sin(a) * 0.08f), false, "Fire").transform);
                }
            // フライパン（取っ手は手前＝シェフの側）と、中の料理
            S.pan = new GameObject("Pan").transform; S.pan.SetParent(g, false); S.pan.localPosition = Coord.W(X0, 0.99f, -1.05f);
            Part.Add(S.pan, MeshGen.Cylinder(0.14f, 0.11f, 0.05f, 22), dark, Vector3.zero, true, "PanBody");
            var hd = Part.Add(S.pan, MeshGen.Cylinder(0.015f, 0.015f, 0.26f, 8), Mats.Get(0x3a2a20), Coord.W(0.24f, 0.02f, 0), false, "Handle");
            hd.transform.localRotation = Part.Euler3(0, 0, Mathf.PI / 2);
            var bc = new[] { 0xe8a040, 0x6fbf46, 0xd85030 };
            for (int k = 0; k < 6; k++)
            {
                var o = Coord.W((Random.value - 0.5f) * 0.14f, 0.04f, (Random.value - 0.5f) * 0.14f);
                S.bits.Add(Part.Add(S.pan, MeshGen.Sphere(0.03f, 8, 6), Mats.Get(bc[k % 3]), o, false, "Bit").transform);
                S.bitO.Add(o);
            }
            // 寸胴鍋と湯気
            Part.Add(g, MeshGen.Cylinder(0.12f, 0.12f, 0.22f, 20), steel, Coord.W(X0, 1.04f, -0.65f), true, "Pot");
            Part.Add(g, MeshGen.Cylinder(0.11f, 0.11f, 0.01f, 18), Mats.Get(0xd8a050), Coord.W(X0, 1.14f, -0.65f), false, "Soup");
            for (int k = 0; k < 5; k++)
            {
                var m = Mats.NewBasic(new Color(1, 1, 1, 0.4f), true, false, false);
                S.steam.Add(Part.Add(g, MeshGen.Sphere(0.06f, 8, 6), m, Vector3.zero, false, "Steam").transform);
                S.steamM.Add(m); S.steamT.Add(k / 5f);
            }
            // 調理台（まな板・野菜・包丁）
            box(0.34f, 0.03f, 0.5f, Mats.Get(0xd9b07a), X0 + 0.02f, 0.915f, 0.85f);
            foreach (var v in new[] { new Vector3(-0.12f, 0xe8742a, 0.04f), new Vector3(0.02f, 0x6fbf46, 0.05f), new Vector3(0.15f, 0xd83a2a, 0.045f) })
                Part.Add(g, MeshGen.Sphere(v.z, 10, 8), Mats.Get((int)v.y), Coord.W(X0 - 0.06f, 0.95f, 0.85f + v.x), true, "Veg");
            S.knife = new GameObject("Knife").transform; S.knife.SetParent(g, false); S.knife.localPosition = Coord.W(X0 + 0.08f, 0.95f, 0.9f);
            Part.Add(S.knife, MeshGen.Box(0.2f, 0.05f, 0.008f), steel, Coord.W(-0.06f, 0, 0), false, "Blade");
            Part.Add(S.knife, MeshGen.Box(0.1f, 0.03f, 0.02f), Mats.Get(0x2a2220), Coord.W(0.09f, 0, 0), false, "Grip");
            S.knife.localRotation = Part.Euler3(0, Mathf.PI / 2, 0);
            // シェフ。客の「コック」の見た目を、少し大きく
            var K0 = GuestKinds.Find("chefguest") ?? GuestKinds.All[0];
            var K = new GuestKind { k = K0.k, n = K0.n, hair = K0.hair, wear = K0.wear, hat = K0.hat, bag = K0.bag, h = K0.h, w = 1.15f, heads = 0.2f,
                                    tie = K0.tie, glass = K0.glass, beard = K0.beard, cap = K0.cap, pal = K0.pal };
            S.chef = new GameObject("ChefBody").transform; S.chef.SetParent(g, false);
            var look = Looks.Guest(S.chef, new Lcg(11), K, TF.heads);
            S.body = look.transform;
            S.body.localScale = Vector3.one * 1.2f;
            S.body.localPosition = new Vector3(0, 0.52f * 1.2f, 0);
            // 腕（左右のカプセル）を見つけて、肩を軸に回せるように入れ物へ移す
            var arms = new List<Transform>();
            foreach (var mf in S.body.GetComponentsInChildren<MeshFilter>(true))
            {
                var o = mf.transform;
                if (mf.sharedMesh == null || !mf.sharedMesh.name.StartsWith("Capsule") || Mathf.Abs(o.localPosition.x) < 0.01f) continue;
                var par = o.parent;
                var sh = new GameObject("Shoulder").transform; sh.SetParent(par, false);
                float top = o.localPosition.y + 0.08f;
                sh.localPosition = new Vector3(o.localPosition.x, top, o.localPosition.z);
                o.SetParent(sh, true);
                arms.Add(sh);
            }
            arms.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x));   // [右手（-X）, 左手（+X）]
            S.arms = arms.ToArray();
            S.chef.localPosition = Coord.W(CHEF_COOK.x, 0, CHEF_COOK.z); S.chef.localRotation = Part.Euler3(0, Mathf.PI / 2, 0);
            S.dur = 4 + Random.value * 2;
            chefS = S;
        }

        static float AngDiff(float a, float b) { float d = b - a; while (d > Mathf.PI) d -= 2 * Mathf.PI; while (d < -Mathf.PI) d += 2 * Mathf.PI; return d; }
        void ArmX(Transform a, float rx) { if (a != null) a.localRotation = Part.Euler3(rx, 0, 0); }
        void ChefSet(ChefS S, string st)
        {
            S.st = st; S.t = 0; S.dur = st == "cook" ? 4 + Random.value * 3 : st == "chop" ? 3 + Random.value * 2 : 0.8f;
            S.pan.localPosition = Coord.W(S.X0, 0.99f, -1.05f); S.pan.localRotation = Quaternion.identity;
            var kp = S.knife.localPosition; S.knife.localPosition = new Vector3(kp.x, 0.95f, kp.z);
        }
        void ChefGo(ChefS S, string st, Vector3 to, float ry, string next)
        {
            var p = S.chef.localPosition;
            S.from = new Vector3(p.x, 0, -p.z); S.to = to; S.toRy = ry; S.next = next;
            S.st = st; S.t = 0; S.dur = Mathf.Max(0.5f, new Vector2(to.x - S.from.x, to.z - S.from.z).magnitude / 1.6f);
            S.pan.localPosition = Coord.W(S.X0, 0.99f, -1.05f); S.pan.localRotation = Quaternion.identity;
            var kp = S.knife.localPosition; S.knife.localPosition = new Vector3(kp.x, 0.95f, kp.z);
            for (int i = 0; i < S.bits.Count; i++) S.bits[i].localPosition = S.bitO[i];
        }

        // 毎フレーム（ホストもオンラインの参加者も。見た目だけ）
        void ChefTick(float dt)
        {
            var S = chefS;
            if (S == null || S.g == null) return;
            S.t += dt;
            float T0 = t;
            for (int k = 0; k < S.fires.Count; k++)
            {
                float s = 0.8f + 0.5f * Mathf.Abs(Mathf.Sin(T0 * 13 + k * 1.7f)) + (S.st == "cook" ? 0.4f : 0);
                S.fires[k].localScale = new Vector3(1, s, 1);
            }
            for (int k = 0; k < S.steam.Count; k++)
            {
                float u = S.steamT[k] = (S.steamT[k] + dt * 0.45f) % 1;
                S.steam[k].localPosition = Coord.W(S.X0 + Mathf.Sin(u * 6 + k) * 0.03f, 1.18f + u * 0.6f, -0.65f);
                S.steam[k].localScale = Vector3.one * (0.6f + u * 1.4f);
                var c = S.steamM[k].GetColor("_BaseColor"); c.a = 0.35f * (1 - u); S.steamM[k].SetColor("_BaseColor", c);
            }
            // 料理が受け取られた（受取口の列が進んだ）ら、次の皿を出しに行く
            if (oi != S.oi)
            {
                if (oi > S.oi && S.st != "serve" && S.st != "toServe") ChefGo(S, "toServe", new Vector3(-1.08f, 0, 0.9f), -Mathf.PI / 2, null);
                S.oi = oi;
            }
            var rA = S.arms.Length > 0 ? S.arms[0] : null; var lA = S.arms.Length > 1 ? S.arms[1] : null;
            var C = S.chef;
            float by = 0.52f * 1.2f;
            if (S.st == "walk" || S.st == "toServe")
            {
                float k = Mathf.Min(1, S.t / S.dur);
                float e = k < 0.5f ? 2 * k * k : 1 - Mathf.Pow(-2 * k + 2, 2) / 2;
                float x = S.from.x + (S.to.x - S.from.x) * e, z = S.from.z + (S.to.z - S.from.z) * e;
                C.localPosition = Coord.W(x, 0, z);
                float dirY = Mathf.Atan2(-(S.to.x - S.from.x), -(S.to.z - S.from.z));
                float ry = k < 0.8f ? dirY : dirY + AngDiff(dirY, S.toRy) * ((k - 0.8f) / 0.2f);
                C.localRotation = Part.Euler3(0, ry, 0);
                S.body.localPosition = new Vector3(0, by + Mathf.Abs(Mathf.Sin(S.t * 12)) * 0.03f, 0);
                ArmX(rA, Mathf.Sin(S.t * 12) * 0.5f); ArmX(lA, -Mathf.Sin(S.t * 12) * 0.5f);
                if (k >= 1) { C.localRotation = Part.Euler3(0, S.toRy, 0); ChefSet(S, S.st == "toServe" ? "serve" : S.next ?? "cook"); }
                return;
            }
            S.body.localPosition = new Vector3(0, by, 0);
            if (S.st == "cook")
            {
                // フライパンをあおる。0.9秒ごとに、引いて・跳ね上げる。具が宙を舞う
                float ph = (S.t % 0.9f) / 0.9f;
                float lift = ph < 0.35f ? Mathf.Sin(ph / 0.35f * Mathf.PI) : 0;
                S.pan.localPosition = Coord.W(S.X0, 0.99f + lift * 0.06f, -1.05f);
                S.pan.localRotation = Part.Euler3(0, 0, -lift * 0.35f);
                float hop = ph > 0.15f && ph < 0.75f ? Mathf.Sin((ph - 0.15f) / 0.6f * Mathf.PI) : 0;
                for (int i = 0; i < S.bits.Count; i++) S.bits[i].localPosition = S.bitO[i] + new Vector3(0, hop * 0.22f, 0);
                ArmX(rA, -0.9f - lift * 0.6f);
                ArmX(lA, -0.2f + Mathf.Sin(S.t * 4) * 0.15f);
                S.body.localRotation = Part.Euler3(0, 0, Mathf.Sin(S.t * 7) * 0.03f);
                if (S.t > S.dur) ChefGo(S, "walk", CHEF_CHOP, Mathf.PI / 2, "chop");
            }
            else if (S.st == "chop")
            {
                float c = Mathf.Abs(Mathf.Sin(S.t * 14));
                var kp = S.knife.localPosition; S.knife.localPosition = new Vector3(kp.x, 0.95f + c * 0.06f, kp.z);
                ArmX(rA, -0.8f - c * 0.4f); ArmX(lA, -0.6f);
                S.body.localPosition = new Vector3(0, by - c * 0.01f, 0);
                if (S.t > S.dur) ChefGo(S, "walk", CHEF_COOK, Mathf.PI / 2, "cook");
            }
            else if (S.st == "serve")
            {
                float k = Mathf.Min(1, S.t / 0.7f), reach = Mathf.Sin(k * Mathf.PI);
                ArmX(rA, -1.4f * reach); ArmX(lA, -1.4f * reach);
                S.body.localRotation = Part.Euler3(-0.12f * reach, 0, 0);
                if (S.t > 0.8f) { S.body.localRotation = Quaternion.identity; ChefGo(S, "walk", CHEF_COOK, Mathf.PI / 2, "cook"); }
            }
        }
    }
}
