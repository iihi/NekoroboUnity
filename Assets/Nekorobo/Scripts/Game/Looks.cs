using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// 客・家具・カウンター・屋台の見た目。**JS版の組み方をそのまま写した仮の見た目。**
    /// モデル（glb）が揃ったら差し替える前提なので、作り込まない。
    /// 数値は JS版と同じ（位置の z と、x・y軸まわりの回転の符号だけ逆）。
    /// </summary>
    public static class Looks
    {
        static readonly int[] SKIN = { 0xf2d3b4, 0xe8bf9a, 0xd2a077, 0xa97250, 0xf7dcc4 };
        static readonly int[] SHIRT = { 0x4f8fd6, 0xd96f6f, 0x59b37a, 0xe0b84c, 0x8e7cc3, 0xe8a33d, 0x5c6bc0, 0xef7fa8 };
        static readonly int[] HAIR = { 0x2b2118, 0x4a3220, 0x6b4a2a, 0x1a1a1a, 0x8a6b45, 0xb8860b };

        static Texture2D faceTex;

        /// <summary>
        /// 客1人ぶん。JS版 guestMesh と**同じ順で乱数を使う**（同じステージなら JS版と同じ見た目になる）。
        /// 原点は当たり判定（カプセル）の中心。前は Unity の +Z。
        /// </summary>
        public static GameObject Guest(Transform parent, Lcg rr, GuestKind K, float tuneHeads)
        {
            int skin = SKIN[rr.Pick(SKIN.Length)];
            int shirt = K.pal != null ? K.pal[rr.Pick(K.pal.Length)] : SHIRT[rr.Pick(SHIRT.Length)];
            int hairC = K.hair == "bald" ? HAIR[3] : HAIR[rr.Pick(HAIR.Length)];

            var gm = new GameObject("GuestLook").transform;
            gm.SetParent(parent, false);
            var inner = gm;
            if (K.h != 1f)
            {
                inner = new GameObject("Inner").transform;
                inner.SetParent(gm, false);
                inner.localScale = new Vector3(1, K.h, 1);
                inner.localPosition = new Vector3(0, -1.04f / 2f * (1 - K.h), 0);
            }
            System.Func<Mesh, int, float, GameObject> put = (mesh, c, y) =>
                Part.Add(inner, mesh, Mats.Get(c), new Vector3(0, y, 0));

            const float HH = 1.04f;
            float heads = Mathf.Max(2.2f, tuneHeads + K.heads);
            float hR = HH / (2 * heads);
            float headY = HH / 2 - hR, neckY = headY - hR * 0.88f;
            float bodyBot = -HH / 2, bodyH = neckY - bodyBot;
            float lowH = bodyH * 0.56f, torH = bodyH * 0.44f;
            float torR = hR * 1.10f * K.w, lowR = hR * 1.30f * K.w;
            int legCol = K.wear == "apron" ? 0x30343a : 0x4a5158;

            if (K.wear == "skirt" || K.wear == "dress")
                put(MeshGen.Cylinder(lowR * 0.72f, lowR * 1.34f, lowH, 12), K.wear == "dress" ? shirt : legCol, bodyBot + lowH / 2);
            else
                put(MeshGen.Cylinder(lowR * 0.86f, lowR, lowH, 12), legCol, bodyBot + lowH / 2);
            put(MeshGen.Cylinder(torR * 0.86f, torR, torH, 12), shirt, bodyBot + lowH + torH / 2);
            if (K.wear == "apron")
                Part.Add(inner, MeshGen.Box(torR * 1.5f, (lowH + torH) * 0.62f, 0.03f), Mats.Get(0xf4f2ec),
                         Coord.W(0, bodyBot + lowH * 0.9f, -torR * 0.92f));
            if (K.tie)
                Part.Add(inner, MeshGen.Box(hR * 0.26f, torH * 0.66f, 0.03f), Mats.Get(0xc03a3a),
                         Coord.W(0, bodyBot + lowH + torH * 0.55f, -torR * 0.93f));
            foreach (float ax in new[] { -torR, torR })
            {
                var arm = Part.Add(inner, MeshGen.Capsule(hR * 0.28f, torH * 0.5f, 8), Mats.Get(shirt),
                                   Coord.W(ax * 1.03f, bodyBot + lowH + torH * 0.55f, 0.02f));
                arm.transform.localRotation = Part.Euler3(0, 0, ax < 0 ? 0.22f : -0.22f);
            }
            if (K.bag == "hand")
                Part.Add(inner, MeshGen.Box(hR * 0.7f, hR * 0.62f, hR * 0.3f), Mats.Get(0x8a6a4a),
                         Coord.W(torR * 1.35f, bodyBot + lowH * 0.95f, 0.02f));
            else if (K.bag == "back")
                Part.Add(inner, MeshGen.Box(torR * 1.5f, torH * 0.9f, hR * 0.55f), Mats.Get(0x3a5a7a),
                         Coord.W(0, bodyBot + lowH + torH * 0.5f, torR * 1.0f));
            put(MeshGen.Cylinder(hR * 0.34f, hR * 0.40f, hR * 0.44f, 8), skin, neckY + hR * 0.12f);
            put(MeshGen.Sphere(hR, 16, 13), skin, headY);
            if (K.hair != "bald")
                Part.Add(inner, MeshGen.Sphere(hR * 1.04f, 16, 13, 0, Mathf.PI * 2, 0, Mathf.PI * 0.28f), Mats.Get(hairC),
                         new Vector3(0, headY + hR * 0.03f, 0));
            if (K.hair == "long")
            {
                var bk = Part.Add(inner, MeshGen.Sphere(hR * 1.02f, 16, 13, 0, Mathf.PI, 0, Mathf.PI * 0.72f), Mats.Get(hairC),
                                  Coord.W(0, headY - hR * 0.12f, hR * 0.1f));
                bk.transform.localRotation = Part.Euler3(0, -Mathf.PI / 2, 0);
                bk.transform.localScale = new Vector3(1, 1, 0.72f);
            }
            if (K.hair == "bun")
                Part.Add(inner, MeshGen.Sphere(hR * 0.42f, 10, 8), Mats.Get(hairC), Coord.W(0, headY + hR * 0.62f, hR * 0.5f));
            if (K.hair == "pony")
            {
                var pn = Part.Add(inner, MeshGen.Capsule(hR * 0.24f, hR * 0.9f, 8), Mats.Get(hairC),
                                  Coord.W(0, headY - hR * 0.1f, hR * 0.85f));
                pn.transform.localRotation = Part.Euler3(0.35f, 0, 0);
            }
            if (K.hat == "cap" || K.cap)
            {
                Part.Add(inner, MeshGen.Sphere(hR * 1.08f, 14, 10, 0, Mathf.PI * 2, 0, Mathf.PI * 0.42f), Mats.Get(0xd8483a),
                         new Vector3(0, headY + hR * 0.05f, 0));
                Part.Add(inner, MeshGen.Box(hR * 1.5f, hR * 0.12f, hR * 0.9f), Mats.Get(0xb03a2e),
                         Coord.W(0, headY + hR * 0.30f, -hR * 1.0f));
            }
            else if (K.hat == "hat")
            {
                put(MeshGen.Cylinder(hR * 0.92f, hR * 0.98f, hR * 0.7f, 14), 0xe0d8b8, headY + hR * 0.62f);
                put(MeshGen.Cylinder(hR * 1.75f, hR * 1.75f, hR * 0.10f, 16), 0xd8cfa8, headY + hR * 0.32f);
            }
            else if (K.hat == "helmet")
                Part.Add(inner, MeshGen.Sphere(hR * 1.12f, 14, 10, 0, Mathf.PI * 2, 0, Mathf.PI * 0.55f), Mats.Get(0xf0c020),
                         new Vector3(0, headY, 0));
            else if (K.hat == "chef")
                put(MeshGen.Cylinder(hR * 0.98f, hR * 0.86f, hR * 1.5f, 14), 0xf6f4ee, headY + hR * 1.0f);
            if (K.glass)
                foreach (float gx in new[] { -hR * 0.42f, hR * 0.42f })
                    Part.Add(inner, MeshGen.Torus(hR * 0.26f, hR * 0.05f, 6, 14), Mats.Get(0x2a2a2e),
                             Coord.W(gx, headY + hR * 0.18f, -hR * 0.94f), shadow: false);
            if (K.beard)
                Part.Add(inner, MeshGen.Sphere(hR * 0.52f, 12, 10, 0, Mathf.PI * 2, Mathf.PI * 0.5f, Mathf.PI * 0.5f),
                         Mats.Get(0xd8d4cc), Coord.W(0, headY - hR * 0.34f, -hR * 0.62f));
            // 顔（笑顔の1枚を使い回す）。頭の上寄りで上向きに傾ける
            var fc = Part.Add(inner, MeshGen.Plane(hR * 1.45f, hR * 0.99f), FaceMat(),
                              Coord.W(0, headY + hR * 0.28f, -hR * 1.02f), shadow: false);
            fc.transform.localRotation = Part.Euler3(0.5f, 0, 0);
            return gm.gameObject;
        }

        static Material faceMat;
        static Material FaceMat()
        {
            if (faceMat != null) return faceMat;
            // 目2つと笑った口。JS版 guestFaceTex と同じ絵柄を簡単に描く
            faceTex = new Texture2D(64, 44, TextureFormat.RGBA32, false);
            var px = new Color32[64 * 44];
            var ink = new Color32(40, 30, 30, 255);
            for (int y = 0; y < 44; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool on = false;
                    foreach (int ex in new[] { 20, 44 })
                        if ((x - ex) * (x - ex) + (y - 26) * (y - 26) <= 16) on = true;
                    float dx = x - 32, dy = y - 16;
                    float rr = Mathf.Sqrt(dx * dx + dy * dy);
                    if (rr > 7f && rr < 9.5f && dy < -1f) on = true;
                    px[y * 64 + x] = on ? ink : new Color32(0, 0, 0, 0);
                }
            faceTex.SetPixels32(px); faceTex.Apply(false);
            faceMat = Mats.BasicTex(faceTex, true, true);
            return faceMat;
        }

        /// <summary>テーブル（JS版の組み方）。原点は当たり判定の中心（高さ 0.38）。</summary>
        public static GameObject Table(Transform holder, bool lng, string des)
        {
            var parent = new GameObject("Look").transform;
            parent.SetParent(holder, false);
            float hw = (lng ? 1.95f : 0.95f) / 2, hd = 0.95f / 2;
            string kind = lng ? "table2" : "table";
            var top = Designs.Mat(des, Mats.Get(0xd98b45, 0.75f), kind, "top", lng ? 2 : 1);
            var leg = Designs.Mat(des, Mats.Get(0x8a5730), kind, "leg");
            Part.Add(parent, MeshGen.Box(hw * 2, 0.07f, hd * 2), top, new Vector3(0, 0.34f, 0));
            foreach (float lx in new[] { -hw + 0.1f, hw - 0.1f })
                foreach (float lz in new[] { -hd + 0.1f, hd - 0.1f })
                    Part.Add(parent, MeshGen.Box(0.08f, 0.68f, 0.08f), leg, Coord.W(lx, -0.02f, lz));
            var cloth = Mats.Get(0xfaf7f0, 0.9f);
            Part.Add(parent, MeshGen.Box(hw * 2 + 0.04f, 0.015f, hd * 0.9f), cloth, new Vector3(0, 0.379f, 0));
            Part.Add(parent, MeshGen.Cylinder(0.12f, 0.11f, 0.02f, 14), cloth, Coord.W(-hw * 0.4f, 0.388f, 0));
            var glass = Mats.LitTransparent(new Color(0.81f, 0.90f, 0.96f, 0.5f), 0.1f);
            Part.Add(parent, MeshGen.Cylinder(0.045f, 0.038f, 0.14f, 12), glass, Coord.W(hw * 0.4f, 0.445f, 0.16f), shadow: false);
            return parent.gameObject;
        }

        /// <summary>イス（前 = +Z）。原点は当たり判定の中心（高さ 0.45）。</summary>
        public static GameObject Chair(Transform holder, string des)
        {
            var parent = new GameObject("Look").transform;
            parent.SetParent(holder, false);
            var cm = Designs.Mat(des, Mats.Get(0xe0a463, 0.8f), "chair", "top");
            var leg = Designs.Mat(des, Mats.Get(0x8a5730), "chair", "leg");
            Part.Add(parent, MeshGen.Box(0.44f, 0.06f, 0.44f), cm, Vector3.zero);
            Part.Add(parent, MeshGen.Box(0.44f, 0.42f, 0.06f), cm, Coord.W(0, 0.24f, 0.19f));
            foreach (float lx in new[] { -0.18f, 0.18f })
                foreach (float lz in new[] { -0.18f, 0.18f })
                    Part.Add(parent, MeshGen.Box(0.05f, 0.44f, 0.05f), leg, Coord.W(lx, -0.23f, lz));
            return parent.gameObject;
        }

        /// <summary>
        /// 文字の板（看板）。TextMesh で書く（仮。本番は絵に差し替える）。
        /// 表はローカルの -Z（three.js の Plane と同じ向き＝カメラの側）。
        /// </summary>
        public static GameObject Sign(Transform parent, string text, float w, float h, Color bg, Color fg, Color? border = null)
        {
            var g = new GameObject("Sign_" + text);
            g.transform.SetParent(parent, false);
            Part.Add(g.transform, MeshGen.Plane(w, h), Mats.Basic(bg), Vector3.zero, shadow: false);
            if (border != null)
            {
                var bm = Mats.Basic(border.Value);
                float t = h * 0.08f;
                Part.Add(g.transform, MeshGen.Plane(w, t), bm, new Vector3(0, h / 2 - t / 2, -0.002f), false);
                Part.Add(g.transform, MeshGen.Plane(w, t), bm, new Vector3(0, -h / 2 + t / 2, -0.002f), false);
                Part.Add(g.transform, MeshGen.Plane(t, h), bm, new Vector3(-w / 2 + t / 2, 0, -0.002f), false);
                Part.Add(g.transform, MeshGen.Plane(t, h), bm, new Vector3(w / 2 - t / 2, 0, -0.002f), false);
            }
            var tg = new GameObject("Text");
            tg.transform.SetParent(g.transform, false);
            tg.transform.localPosition = new Vector3(0, 0, -0.004f);
            var tm = tg.AddComponent<TextMesh>();
            tm.font = Fonts.UI;
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.color = fg;
            tg.GetComponent<MeshRenderer>().sharedMaterial = Fonts.UI.material;
            // 文字の高さを板の 55% に合わせる（characterSize は fontSize に掛かる）
            tm.characterSize = h * 0.55f / 6.4f;
            // 板からはみ出すときは縮める
            var r = tg.GetComponent<MeshRenderer>();
            r.enabled = true;
            float tw = r.bounds.size.x;
            if (tw > w * 0.92f && tw > 0f) tm.characterSize *= w * 0.92f / tw;
            return g;
        }
    }
}
