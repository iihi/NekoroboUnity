using UnityEngine;

namespace Nekorobo
{
    /// <summary>入力5つ＋α。人間でも NPC でも、ロボはこれだけを見る（JS版と同じ考え方）。</summary>
    public struct BotInput
    {
        public bool up, down, left, right, jump, use, cycle, cycleBack;
        // 照準・カーソル用（十字とスティックだけ。パッドの A は加速と兼用なので入れない）
        public bool navUp, navDown, navLeft, navRight;
    }

    /// <summary>運んでいる料理。</summary>
    public class Carried
    {
        public Dish dish;
        public float integ;
        public Order order;
    }

    public class Order
    {
        public Dish dish;
        public Ent guest;
        public bool done;
        public Player taker;
    }

    /// <summary>
    /// 操作の枠。ロボは5つのフラグしか見ないので、その出どころを枠ごとに決める（JS版 SLOTS の1つ）。
    ///   key … キーボード（パッドを枠に割り当てていなければ、つないである全部のパッドも）
    ///   pad … index 台目のパッド
    ///   npc … NPC（lv は強さ。serious / normal / wild / easy）
    /// </summary>
    public class PlayerSrc
    {
        public string kind = "key";
        public int index;
        public string lv;
        public int? team;                // チーム戦の青(0)・赤(1)。無ければ交互に分ける

        public string Name
        {
            get
            {
                return kind == "pad" ? "パッド" + (index + 1)
                     : kind == "npc" ? "NPC・" + NpcLevels.Get(lv).name : "キーボード";
            }
        }

        /// <summary>"key,pad0,npc-normal,off" を枠の並びに直す（JS版 parseSlots）。使わない枠と重なったパッドは落とす。</summary>
        public static System.Collections.Generic.List<PlayerSrc> Parse(string str)
        {
            var o = new System.Collections.Generic.List<PlayerSrc>();
            var usedPad = new System.Collections.Generic.HashSet<int>();
            bool key = false;
            foreach (var raw in (str ?? "").Split(','))
            {
                var v = raw.Trim().ToLowerInvariant();
                if (v.Length == 0 || v == "off") continue;
                if (v == "key") { if (key) continue; key = true; o.Add(new PlayerSrc()); continue; }
                if (v.Length == 4 && v.StartsWith("pad") && char.IsDigit(v[3]))
                {
                    int i = v[3] - '0';
                    if (!usedPad.Add(i)) continue;
                    o.Add(new PlayerSrc { kind = "pad", index = i }); continue;
                }
                if (v.StartsWith("npc-") && NpcLevels.Known(v.Substring(4)))
                    o.Add(new PlayerSrc { kind = "npc", lv = NpcLevels.Canon(v.Substring(4)) });
            }
            if (o.Count > 4) o.RemoveRange(4, o.Count - 4);
            return o;
        }
    }

    /// <summary>
    /// ロボ1台ぶんの記録。JS版 newPlayer と同じ中身（まだ使わない項目は省いてある）。
    /// </summary>
    public class Player
    {
        public static readonly int[] PCOL = { 0x2f9bff, 0xff6b6b, 0x5fd08a, 0xffc44d };
        public static readonly string[] PNAME = { "1P", "2P", "3P", "4P" };

        public int idx;
        public string name;
        public int col;
        public Ent ent;
        public Carried carried;
        public float botDmg, sales, shopDmg;
        public int delivered, hurt;
        public bool down;
        public float downT, revT;
        public int combo, bestCombo, dcombo;
        public float comboT, comboDmg, comboPaid, wreck, dcomboT;
        public string broken;            // null / "left" / "right"
        public bool brokenDrive, misfire;
        public float misT;
        public float? steerHold;
        public float lastJump = -9f, prevVy;
        public bool airborne;
        public float invT, slipT, slipSpin;
        public Vector3 spawn;
        public float spawnRot;           // 出現位置の rot（度）
        public BotInput input;
        public bool prevUse, prevCycle, prevBack;
        public string slot;              // 選んでいるアイテム
        public Vector3? aim;             // 弾道ミサイルの照準（出している間だけ）
        public Transform aimMesh;
        public PlayerSrc src = new PlayerSrc();
        public int team;                 // チーム戦の青(0)・赤(1)
        public NpcBrain npc;             // NPC のときだけ入る

        /// <summary>頭の上・一覧に出す名前（NPC は「NPC」）。</summary>
        public string Label { get { return src.kind == "npc" ? "NPC" : name; } }

        // 見た目
        public RobotLook look;
        public GameObject ring;
        public string msg = "";
        public float msgT, faceT;
        public string face = "norm";

        public Player(int idx)
        {
            this.idx = idx;
            name = PNAME[idx % 4];
            col = PCOL[idx % 4];
        }
    }

    /// <summary>
    /// ロボの見た目。JS版 buildRobot の箱の組み方をそのまま写したもの（z は反転）。
    /// 前は顔、後ろは開いたトレー棚。上段のトレーに運んでいる料理が乗る。
    /// </summary>
    public class RobotLook
    {
        public GameObject root;
        public Transform plate;          // 運んでいる料理（皿ごと）
        public GameObject dishVis;       // 皿の上の料理
        public Material body, bodyDmg;
        public MeshRenderer[] white;
        public Texture2D faceTex;
        public string faceNow = "";

        public static RobotLook Build(Transform parent, int col, float tint)
        {
            var L = new RobotLook();
            var rg = new GameObject("Look").transform;
            rg.SetParent(parent, false);
            L.root = rg.gameObject;
            var pc = Mats.Hex(col);
            L.body = Mats.NewLit(Color.Lerp(Mats.Hex(0xfdfdfd), pc, tint), 0.95f);
            L.bodyDmg = Mats.NewLit(Color.Lerp(Mats.Hex(0xbdbdbd), pc, tint), 0.95f);
            var whites = new System.Collections.Generic.List<MeshRenderer>();
            System.Action<float, float, float, float, float, float> wpart = (w, h, d, x, y, z) =>
            {
                var o = Part.Add(rg, MeshGen.Box(w, h, d), L.body, Coord.W(x, y, z));
                whites.Add(o.GetComponent<MeshRenderer>());
            };
            wpart(0.60f, 0.24f, 0.50f, 0, -0.40f, 0.00f);       // 台座
            wpart(0.52f, 0.52f, 0.20f, 0, -0.02f, -0.14f);      // 胴
            wpart(0.54f, 0.32f, 0.24f, 0, 0.37f, -0.12f);       // 頭
            wpart(0.54f, 0.10f, 0.28f, 0, 0.50f, 0.12f);        // 天板（ひさし）
            wpart(0.06f, 0.62f, 0.30f, -0.24f, 0.06f, 0.12f);   // 後ろの支柱
            wpart(0.06f, 0.62f, 0.30f, 0.24f, 0.06f, 0.12f);
            L.white = whites.ToArray();
            // 青いアクセントライン
            var lineM = Mats.Basic(pc);
            Part.Add(rg, MeshGen.Box(0.02f, 0.44f, 0.01f), lineM, Coord.W(-0.21f, -0.02f, -0.242f), shadow: false);
            Part.Add(rg, MeshGen.Box(0.02f, 0.44f, 0.01f), lineM, Coord.W(0.21f, -0.02f, -0.242f), shadow: false);
            Part.Add(rg, MeshGen.Box(0.46f, 0.02f, 0.01f), lineM, Coord.W(0, -0.26f, -0.242f), shadow: false);
            // 後ろのトレー棚（2段）
            var panel = Mats.Get(0x8f9aa6);
            Part.Add(rg, MeshGen.Box(0.44f, 0.03f, 0.30f), panel, Coord.W(0, 0.18f, 0.12f));
            Part.Add(rg, MeshGen.Box(0.44f, 0.03f, 0.30f), panel, Coord.W(0, -0.14f, 0.12f));
            // 顔パネル（前 = Unity の +Z）。少し上向きに傾ける
            L.faceTex = new Texture2D(192, 96, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var face = Part.Add(rg, MeshGen.Plane(0.48f, 0.25f), Mats.BasicTex(L.faceTex), Coord.W(0, 0.38f, -0.243f), shadow: false);
            // three.js: rotation.y = π（裏返して前へ向ける）, rotation.x = 0.22
            face.transform.localRotation = Part.Euler3(0.22f, Mathf.PI, 0f);
            // 横の装甲パネル
            Part.Add(rg, MeshGen.Box(0.05f, 0.46f, 0.34f), panel, Coord.W(-0.295f, 0.02f, 0.03f));
            Part.Add(rg, MeshGen.Box(0.05f, 0.46f, 0.34f), panel, Coord.W(0.295f, 0.02f, 0.03f));
            // ネコ耳
            var robotM = L.body;
            Part.Add(rg, MeshGen.Cone(0.09f, 0.17f, 4), robotM, Coord.W(-0.19f, 0.60f, -0.13f));
            Part.Add(rg, MeshGen.Cone(0.09f, 0.17f, 4), robotM, Coord.W(0.19f, 0.60f, -0.13f));
            // 運んでいる料理（上段トレー）
            var plate = new GameObject("Plate").transform;
            plate.SetParent(rg, false);
            plate.localPosition = Coord.W(0, 0.215f, 0.12f);
            Part.Add(plate, MeshGen.Cylinder(0.15f, 0.13f, 0.03f, 16), Mats.Get(0xfaf7f0, 0.9f), Vector3.zero);
            plate.gameObject.SetActive(false);
            L.plate = plate;
            L.DrawFace("norm");
            return L;
        }

        /// <summary>料理の崩れ具合（JS版 dishIntegrity）。満点で高さ 0.7、崩れるほど低く潰れる。</summary>
        public void SetInteg(float integ)
        {
            if (dishVis == null) return;
            float sy = 0.6f * (integ / 100f) + 0.1f;
            dishVis.transform.localScale = new Vector3(1, sy / 0.7f, 1);
        }

        public void SetDamaged(bool dmg)
        {
            var m = dmg ? bodyDmg : body;
            foreach (var r in white) r.sharedMaterial = m;
        }

        /// <summary>運んでいる料理を出す／消す。</summary>
        public void ShowDish(Dish d)
        {
            if (dishVis != null) Object.Destroy(dishVis);
            dishVis = null;
            plate.gameObject.SetActive(d != null);
            if (d == null) return;
            dishVis = DishLook.Build(plate, d, 0.09f);
        }

        /// <summary>
        /// 顔。黒いパネルに青く光る目と ω の口（JS版 drawFace を簡単にしたもの）。
        /// norm / fast / hit / happy / dead
        /// </summary>
        public void DrawFace(string f)
        {
            if (faceNow == f) return;
            faceNow = f;
            var px = new Color32[192 * 96];
            var bg = new Color32(13, 13, 15, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            var eye = f == "dead" ? new Color32(120, 120, 130, 255)
                    : f == "hit" ? new Color32(255, 90, 80, 255)
                    : new Color32(80, 200, 255, 255);
            System.Action<int, int, int, int> ell = (cx, cy, rx, ry) =>
            {
                for (int y = -ry; y <= ry; y++)
                    for (int x = -rx; x <= rx; x++)
                    {
                        if ((x * x) / (float)(rx * rx) + (y * y) / (float)(ry * ry) > 1f) continue;
                        int X = cx + x, Y = cy + y;
                        if (X >= 0 && X < 192 && Y >= 0 && Y < 96) px[Y * 192 + X] = eye;
                    }
            };
            System.Action<int, int, int, int, int> bar = (x0, y0, x1, y1, th) =>
            {
                int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
                for (int s = 0; s <= n; s++)
                {
                    int x = x0 + (x1 - x0) * s / Mathf.Max(1, n), y = y0 + (y1 - y0) * s / Mathf.Max(1, n);
                    for (int a = -th; a <= th; a++) for (int b = -th; b <= th; b++)
                        {
                            int X = x + a, Y = y + b;
                            if (X >= 0 && X < 192 && Y >= 0 && Y < 96) px[Y * 192 + X] = eye;
                        }
                }
            };
            // テクスチャは下が y=0
            if (f == "happy")
            {
                bar(42, 52, 56, 64, 3); bar(56, 64, 70, 52, 3);
                bar(122, 52, 136, 64, 3); bar(136, 64, 150, 52, 3);
            }
            else if (f == "dead")
            {
                bar(42, 48, 70, 70, 3); bar(42, 70, 70, 48, 3);
                bar(122, 48, 150, 70, 3); bar(122, 70, 150, 48, 3);
            }
            else if (f == "hit")
            {
                bar(42, 66, 70, 58, 3); bar(42, 50, 70, 58, 3);
                bar(150, 66, 122, 58, 3); bar(150, 50, 122, 58, 3);
            }
            else
            {
                int ry = f == "fast" ? 6 : 13;
                ell(56, 58, 11, ry); ell(136, 58, 11, ry);
            }
            // ω の口
            bar(82, 30, 89, 22, 2); bar(89, 22, 96, 30, 2); bar(96, 30, 103, 22, 2); bar(103, 22, 110, 30, 2);
            faceTex.SetPixels32(px);
            faceTex.Apply(false);
        }
    }

    /// <summary>部品を1つ置く小道具。位置は Unity の座標（Coord.W で直してから渡す）。</summary>
    public static class Part
    {
        public static GameObject Add(Transform parent, Mesh mesh, Material m, Vector3 localPos,
                                     bool shadow = true, string name = null)
        {
            var go = new GameObject(name ?? mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On
                                         : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        /// <summary>
        /// three.js の rotation(x, y, z)（ラジアン、XYZ順）を、z を反転した Unity の回転へ。
        /// x と y の符号が逆になり、z はそのまま（Coord の決まり）。
        /// </summary>
        public static Quaternion Euler3(float x, float y, float z)
        {
            return Quaternion.AngleAxis(-x * Mathf.Rad2Deg, Vector3.right)
                 * Quaternion.AngleAxis(-y * Mathf.Rad2Deg, Vector3.up)
                 * Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward);
        }
    }

    /// <summary>料理の見た目（皿の上）。モデルが来るまでは図形で組む。</summary>
    public static class DishLook
    {
        public static GameObject Build(Transform parent, Dish d, float r)
        {
            var g = new GameObject("Dish_" + d.k);
            g.transform.SetParent(parent, false);
            if (d.shape == "cup")
            {
                var root = g.transform; root.localScale = Vector3.one * 1.35f;
                Part.Add(root, MeshGen.Cylinder(0.075f, 0.058f, 0.2f, 14), Mats.Get(d.col, 0.8f), new Vector3(0, 0.1f, 0));
                Part.Add(root, MeshGen.Cylinder(0.079f, 0.079f, 0.02f, 14), Mats.Get(0xffffff, 0.8f), new Vector3(0, 0.205f, 0));
                var st = Part.Add(root, MeshGen.Cylinder(0.009f, 0.009f, 0.16f, 6), Mats.Get(0xe8504a, 0.8f), new Vector3(0.025f, 0.27f, 0));
                st.transform.localRotation = Part.Euler3(0, 0, 0.25f);
            }
            else if (d.shape == "crepe")
            {
                var root = g.transform; root.localScale = Vector3.one * 1.35f;
                var a = Part.Add(root, MeshGen.Cone(0.085f, 0.22f, 14), Mats.Get(0xe8c48a, 0.8f), new Vector3(0, 0.11f, 0));
                a.transform.localRotation = Part.Euler3(Mathf.PI, 0, 0);
                var pa = Part.Add(root, MeshGen.Cone(0.092f, 0.13f, 14, true), Mats.Get(0xf6f1e4, 0.8f), new Vector3(0, 0.07f, 0));
                pa.transform.localRotation = Part.Euler3(Mathf.PI, 0, 0);
                var cr = Part.Add(root, MeshGen.Sphere(0.07f, 12, 9), Mats.Get(0xfffaf0, 0.8f), new Vector3(0, 0.225f, 0));
                cr.transform.localScale = new Vector3(1, 0.7f, 1);
                Part.Add(root, MeshGen.Sphere(0.045f, 10, 8), Mats.Get(d.col, 0.8f), new Vector3(0.025f, 0.255f, 0));
            }
            else
            {
                var fd = Part.Add(g.transform, MeshGen.Sphere(r, 10, 8), Mats.Get(d.col), new Vector3(0, 0.05f, 0));
                fd.transform.localScale = new Vector3(1, 0.6f, 1);
            }
            return g;
        }
    }
}
