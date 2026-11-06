using UnityEngine;

namespace Nekorobo
{
    /// <summary>救済のクレーンで運ばれている間の記録（JS版 P.cr）。</summary>
    public class RescueRun
    {
        public float t;
        public Vector3 from, to;
        public float toY, yaw0, yaw1;      // 向きは JS版と同じ（度ではなくラジアン・Unity の y 回転と同じ向き）
    }
    /// <summary>クレーンの絵（JS版 P.crFx）。画面ごとに動かす。</summary>
    public class RescueFx
    {
        public Transform g;
        public Transform[] arms;
        public float t, hy;
        public Vector3? rel;
    }

    // ================================================================ 救済（クレーン）
    // L1・L2・R1・R2 を4つとも長押し（キーボードは Q・E・X・C）すると、UFOキャッチャーのようなクレーンが
    // 上から降りてきてロボをつかみ、出現位置まで運んで下ろす（担当。ジャンプを低くしたので、川に落ちて
    // 上がれないときなどの救済）。1回 T.rescueCost（10,000円）。収支では「修理費・救急車」に入る。
    // 運んでいる間は位置で動かす（kinematic）。ほかの物には当たらない（当たり判定をトリガーにする）。
    // 動かすのはホスト（と1人のとき）。クレーンの絵は画面ごと（"cr" の便りで参加者の画面でも出す）
    public partial class Game
    {
        static class CR
        {
            public const float down = 0.55f, grab = 0.22f, up = 0.5f, move = 1.0f, lower = 0.5f, open = 0.22f, away = 0.6f;
            public const float lift = 2.6f;   // 持ち上げる高さ[m]（店の中の物より上を通る）
            public const float sky = 7;       // 降りてくる・帰っていく高さ[m]
        }
        static float CrT(string k)
        {
            float t = 0;
            foreach (var n in new[] { "down", "grab", "up", "move", "lower", "open", "away" })
            {
                if (n == k) return t;
                t += n == "down" ? CR.down : n == "grab" ? CR.grab : n == "up" ? CR.up : n == "move" ? CR.move
                   : n == "lower" ? CR.lower : n == "open" ? CR.open : CR.away;
            }
            return t;
        }
        static float CrEase(float u) { return u <= 0 ? 0 : u >= 1 ? 1 : u * u * (3 - 2 * u); }

        void RescueHoldTick(Player P, float dt)
        {
            bool can = state == "play" && !P.down && P.cr == null && P.ent != null && !NetGuest;
            P.crHold = can && P.input.rescue ? P.crHold + dt : 0;
            if (P.crHold >= T.rescueHold) { P.crHold = 0; RescueStart(P); }
        }

        void CrSensor(Player P, bool on)
        {
            foreach (var c in P.ent.GetComponentsInChildren<Collider>()) c.isTrigger = on;
        }

        public void RescueStart(Player P)
        {
            var R = P.ent; var p = R.rb.position;
            if (P.carried != null) DropDish(P, Vector3.zero);          // 運んでいた料理はその場に落とす
            var sp = RescueSpot(R, null);
            float yaw0 = R.rb.rotation.eulerAngles.y * Mathf.Deg2Rad;
            float yaw1 = Coord.RotFace(P.spawnRot).eulerAngles.y * Mathf.Deg2Rad;
            float hy = R.size.y > 0 ? R.size.y / 2 : 0.45f;
            P.cr = new RescueRun { from = p, to = new Vector3(sp.x, 0, sp.z), toY = Mathf.Max(0.6f, hy + 0.12f), yaw0 = yaw0, yaw1 = yaw1 };
            R.rb.linearVelocity = Vector3.zero; R.rb.angularVelocity = Vector3.zero;
            R.rb.isKinematic = true;
            CrSensor(P, true);
            P.crCost += T.rescueCost;
            P.slipT = 0; P.airborne = false;
            if (P.npc != null) { P.npc.wantCr = false; P.npc.wetT = 0; P.npc.idleT = 0; P.npc.path = null; }
            hud.Pop(p + Vector3.up * 1.3f, "−" + Yen(T.rescueCost), 0xffb300, true);
            Say(P, "たすけてにゃ～！", 1.6f);
            NetEv("cr", P.idx);
            CraneShow(P);
        }

        void RescueStep(float dt)
        {
            foreach (var P in players)
            {
                var C = P.cr;
                if (C == null) continue;
                C.t += dt;
                float tt = C.t, top = Mathf.Max(C.from.y, C.toY) + CR.lift;
                float x = C.from.x, y = C.from.y, z = C.from.z, yaw = C.yaw0;
                if (tt >= CrT("up") && tt < CrT("move")) y = C.from.y + (top - C.from.y) * CrEase((tt - CrT("up")) / CR.up);
                else if (tt >= CrT("move") && tt < CrT("lower"))
                {
                    float u = CrEase((tt - CrT("move")) / CR.move);
                    x += (C.to.x - x) * u; z += (C.to.z - z) * u; y = top;
                    yaw = C.yaw0 + Mathf.DeltaAngle(C.yaw0 * Mathf.Rad2Deg, C.yaw1 * Mathf.Rad2Deg) * Mathf.Deg2Rad * u;
                }
                else if (tt >= CrT("lower"))
                {
                    x = C.to.x; z = C.to.z; yaw = C.yaw1;
                    y = top + (C.toY - top) * CrEase((tt - CrT("lower")) / CR.lower);
                }
                P.ent.rb.MovePosition(new Vector3(x, y, z));
                P.ent.rb.MoveRotation(Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0));
                if (tt >= CrT("away")) RescueEnd(P);
            }
        }

        void RescueEnd(Player P)
        {
            var R = P.ent;
            P.cr = null;
            R.rb.isKinematic = false;
            CrSensor(P, false);
            R.rb.useGravity = true;
            R.rb.linearVelocity = new Vector3(0, -0.5f, 0); R.rb.angularVelocity = Vector3.zero;
            R.rb.WakeUp();
            SetFace(P, "norm", 99);
            Say(P, "もどったにゃ！", 1.6f);
        }

        // ---- クレーンの絵。ロボの見た目の大きさに合わせて作る（爪3本。真ん中の玉からワイヤーが空へ）
        public void CraneShow(Player P)
        {
            if (P.crFx != null && P.crFx.g != null) Destroy(P.crFx.g.gameObject);
            if (P.ent == null || P.look == null || P.look.root == null) return;
            // 見た目の箱（向きを戻して測る）
            var root = P.look.root.transform;
            var q = root.rotation; root.rotation = Quaternion.identity;
            Bounds b = new Bounds(root.position, Vector3.zero); bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            root.rotation = q;
            var pp = root.position;
            float top = any ? b.max.y - pp.y : 0.9f, bot = any ? b.min.y - pp.y : -0.45f;
            float rad = any ? Mathf.Max(Mathf.Abs(b.min.x - pp.x), b.max.x - pp.x, Mathf.Abs(b.min.z - pp.z), b.max.z - pp.z) : 0.45f;
            float rr = rad + 0.07f, armL = (top - bot) * 0.62f;
            var g = new GameObject("Crane").transform; g.SetParent(fxRoot, false);
            var metal = Mats.Get(0xdfe5ee, 0.28f, 0.75f);
            var pink = Mats.Get(0xff5c8a, 0.4f);
            var gold = Mats.Get(0xffd23f, 0.35f, 0.4f);
            var hub = Part.Add(g, MeshGen.Cylinder(rr * 0.55f, rr * 0.7f, 0.16f, 18), pink, new Vector3(0, 0.1f, 0), true, "Hub");
            hub.transform.localPosition = new Vector3(0, 0.1f, 0);
            var cap = Part.Add(g, MeshGen.Sphere(rr * 0.55f, 18, 8, 0, Mathf.PI * 2, 0, Mathf.PI / 2), metal, Vector3.zero, true, "Cap");
            cap.transform.localPosition = new Vector3(0, 0.18f, 0);
            var ring = Part.Add(g, MeshGen.Torus(rr * 0.66f, 0.025f, 6, 24), gold, Vector3.zero, false, "Ring");
            ring.transform.localPosition = new Vector3(0, 0.04f, 0); ring.transform.localRotation = Part.Euler3(Mathf.PI / 2, 0, 0);
            var cable = Part.Add(g, MeshGen.Cylinder(0.02f, 0.02f, 14, 6), metal, Vector3.zero, false, "Cable");
            cable.transform.localPosition = new Vector3(0, 0.2f + 7, 0);
            var arms = new Transform[3];
            for (int k = 0; k < 3; k++)
            {
                var pv = new GameObject("Arm" + k).transform; pv.SetParent(g, false);
                pv.localRotation = Quaternion.Euler(0, -(k * 120 + 30), 0);
                var hinge = new GameObject("Hinge").transform; hinge.SetParent(pv, false);
                hinge.localPosition = new Vector3(0, 0.04f, rr);        // JS版の −Z は Unity の +Z
                var up = Part.Add(hinge, MeshGen.Box(0.06f, armL, 0.045f), metal, Vector3.zero, true, "Up");
                up.transform.localPosition = new Vector3(0, -armL / 2, 0);
                var tipG = new GameObject("TipG").transform; tipG.SetParent(hinge, false);
                tipG.localPosition = new Vector3(0, -armL, 0); tipG.localRotation = Quaternion.Euler(-0.95f * Mathf.Rad2Deg, 0, 0);
                var tip = Part.Add(tipG, MeshGen.Box(0.06f, 0.16f, 0.045f), metal, Vector3.zero, true, "Tip");
                tip.transform.localPosition = new Vector3(0, -0.08f, 0);
                arms[k] = hinge;
            }
            P.crFx = new RescueFx { g = g, arms = arms, t = 0, hy = top + 0.02f };
            Se("revive", P.ent.rb.position, P, 0.75f, 0.8f);
        }

        // 毎フレーム（画面ごと）。爪の付け根をロボの頭に合わせ、時間で開け閉めする
        void CraneTick(float dt)
        {
            foreach (var P in players)
            {
                var F = P.crFx;
                if (F == null) continue;
                F.t += dt;
                float tt = F.t;
                var m = P.look != null && P.look.root != null ? P.look.root.transform : null;
                if (m == null || F.g == null || tt >= CrT("away") + CR.away)
                {
                    if (F.g != null) Destroy(F.g.gameObject);
                    P.crFx = null; continue;
                }
                float off = 0, open = 0.05f;
                if (tt < CrT("grab")) { off = CR.sky * (1 - CrEase(tt / CR.down)); open = 0.6f; }
                else if (tt < CrT("up")) open = 0.6f - 0.55f * CrEase((tt - CrT("grab")) / CR.grab);
                else if (tt >= CrT("open") && tt < CrT("away")) open = 0.05f + 0.55f * CrEase((tt - CrT("open")) / CR.open);
                else if (tt >= CrT("away")) { open = 0.6f; off = CR.sky * CrEase((tt - CrT("away")) / CR.away); }
                // 放したあと（帰っていくとき）は、放した所から上がる（ロボは下へ落ちていく）
                if (tt < CrT("away") || F.rel == null) F.rel = m.position + Vector3.up * F.hy;
                var bp = tt >= CrT("away") ? F.rel.Value : m.position + Vector3.up * F.hy;
                F.g.position = bp + Vector3.up * off;
                F.g.rotation = Quaternion.Euler(0, m.eulerAngles.y, 0);
                // 爪は外へ開く（JS版は −Z 側の付け根を x 回りに +open。Unity は +Z 側なので逆向き）
                foreach (var a in F.arms) a.localRotation = Quaternion.Euler(-open * Mathf.Rad2Deg, 0, 0);
            }
        }
    }
}
