using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 救急車（JS版 ambulanceRun / ambulanceTick）。**クリアしたとき、昇天した客（体力0）の人数ぶん駆けつけて、乗せて去っていく。**
    /// STAGE CLEAR! のあと、結果の画面の前に出す（数秒）。
    /// 近い側の画面の外から来て、客の横に止まり、乗せたら反対側へ抜ける。物理には乗せない（見た目だけ）。
    /// 救急車のモデルが無いので、箱を組んだ仮の形。オンラインの参加者も、配られた客の体力から同じものを出す。
    /// </summary>
    public partial class Game
    {
        class Amb
        {
            public Transform m; public Material lamp; public Ent gu;
            public float dir, stopX, endX, z, t, w, v; public int ph; public bool siren;
        }
        readonly List<Amb> ambs = new List<Amb>();
        System.Action ambDone; StageCfg ambStage;

        Transform AmbulanceMesh()
        {
            var g = new GameObject("Ambulance").transform;
            g.SetParent(stageRoot, false);
            Material white = Mats.Get(0xf4f6f8, 0.6f), red = Mats.Get(0xe53935, 0.6f), dark = Mats.Get(0x2a3e55, 0.3f), tire = Mats.Get(0x22262c);
            System.Action<float, float, float, Material, float, float, float> box = (w, h, d, m, x, y, z) =>
                Part.Add(g, MeshGen.Box(w, h, d), m, new Vector3(x, y, z));
            box(1.35f, 0.86f, 0.95f, white, -0.3f, 0.63f, 0);     // 荷台（+X が前）
            box(0.62f, 0.62f, 0.92f, white, 0.68f, 0.51f, 0);     // 運転席
            box(0.05f, 0.28f, 0.80f, dark, 0.99f, 0.63f, 0);      // 前の窓
            box(0.40f, 0.24f, 0.94f, dark, 0.66f, 0.66f, 0);      // 横の窓
            box(2.02f, 0.09f, 0.97f, red, 0.02f, 0.46f, 0);       // 赤い帯
            box(0.12f, 0.36f, 0.97f, red, -0.30f, 0.80f, 0);      // 赤十字（縦）
            box(0.36f, 0.12f, 0.97f, red, -0.30f, 0.80f, 0);      // 赤十字（横）
            var lamp = Mats.NewBasic(Mats.Hex(0xff2a2a));
            Part.Add(g, MeshGen.Box(0.28f, 0.10f, 0.62f), lamp, new Vector3(0.66f, 0.87f, 0), false, "Lamp");
            var wm = MeshGen.Cylinder(0.18f, 0.18f, 0.12f, 14);
            foreach (float x in new[] { -0.62f, 0.62f })
                foreach (float z in new[] { -0.46f, 0.46f })
                    Part.Add(g, wm, tire, new Vector3(x, 0.18f, z)).transform.localRotation = Quaternion.Euler(90, 0, 0);
            return g;
        }

        /// <summary>昇天した客が居れば救急車を走らせ、みんな去ってから done。居なければすぐ done。</summary>
        void AmbulanceRun(System.Action done)
        {
            AmbulanceClear();
            var down = guests.FindAll(gu => gu.hp <= 0 && !gu.taken && !gu.sunk);
            if (down.Count == 0) { done(); return; }
            var B = bounds; float cx = (B.x0 + B.x1) / 2;
            float lag = Mathf.Min(0.4f, 1.6f / down.Count);       // 大勢でも、出そろうまで2秒かからないように
            for (int i = 0; i < down.Count; i++)
            {
                var gu = down[i];
                var p = gu.rb.position;
                bool fromLeft = p.x < cx;                           // 近い側から来て、反対側へ抜ける
                float dir = fromLeft ? 1 : -1;
                var m = AmbulanceMesh();
                m.position = new Vector3(fromLeft ? B.x0 - 8 : B.x1 + 8, 0, p.z);
                m.rotation = Quaternion.Euler(0, fromLeft ? 0 : 180, 0);
                m.gameObject.SetActive(false);
                ambs.Add(new Amb
                {
                    m = m, lamp = m.Find("Lamp").GetComponent<MeshRenderer>().sharedMaterial, gu = gu, dir = dir,
                    stopX = p.x - dir * 1.35f, endX = fromLeft ? B.x1 + 8 : B.x0 - 8, z = p.z, t = -i * lag,
                });
            }
            ambDone = done; ambStage = stage;
        }

        void AmbulanceClear()
        {
            foreach (var a in ambs) if (a.m != null) Destroy(a.m.gameObject);
            ambs.Clear(); ambDone = null;
        }

        void AmbulanceTick(float dt)
        {
            if (ambs.Count == 0) return;
            // 途中で面が変わった（やり直し・タイトルへ）ら、黙って片付ける
            if (ambStage != stage) { AmbulanceClear(); return; }
            const float SP = 11;                                    // 走る速さ[m/s]
            bool all = true;
            foreach (var a in ambs)
            {
                a.t += dt;
                if (a.ph == 3) continue;
                all = false;
                if (a.t < 0 || a.m == null) continue;
                a.m.gameObject.SetActive(true);
                a.lamp.color = Mats.Hex(Mathf.FloorToInt(Time.time * 7) % 2 == 1 ? 0xff2a2a : 0xffffff);
                var pos = a.m.position;
                if (a.ph == 0)
                {
                    // 駆けつける（止まる前は少しゆるめる）
                    if (!a.siren) { a.siren = true; Se("siren", new Vector3(a.stopX, 0.5f, a.z), null, 1, 1, true); }
                    float left = (a.stopX - pos.x) * a.dir;
                    pos.x += a.dir * Mathf.Min(SP * dt, Mathf.Max(0.02f, left * 6 * dt));
                    if (left <= 0.03f) { pos.x = a.stopX; a.ph = 1; a.w = 0.75f; }
                }
                else if (a.ph == 1)
                {
                    // 乗せる
                    a.w -= dt;
                    if (a.w < 0.45f && !a.gu.taken)
                    {
                        a.gu.taken = true;
                        var p = a.gu.rb.position;
                        SpawnSplash(new Vector3(p.x, 0.5f, p.z), 0xffffff, 6);
                        SinkOut(a.gu);                              // 見た目・輪・札をしまって、体は盤面の下へ
                    }
                    if (a.w <= 0) a.ph = 2;
                }
                else
                {
                    // 去っていく（だんだん速く）
                    a.v = Mathf.Min(SP, a.v + 18 * dt);
                    pos.x += a.dir * a.v * dt;
                    if ((pos.x - a.endX) * a.dir >= 0) { a.ph = 3; Destroy(a.m.gameObject); a.m = null; }
                }
                if (a.m != null) a.m.position = pos;
            }
            if (all || ambs.TrueForAll(a => a.ph == 3))
            {
                var d = ambDone;
                ambs.Clear(); ambDone = null;
                if (d != null) d();
            }
        }
    }
}
