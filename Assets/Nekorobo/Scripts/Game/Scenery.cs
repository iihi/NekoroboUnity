using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>
    /// ステージのまわりに置く飾り（地面・木・建物・植木鉢…）。JS版 scenery.js を写したもの。
    /// 当たり判定も影も持たない。カメラの自動調整にも入れない（画面の外へはみ出して切れるのが正解）。
    ///
    /// 計算は three.js の向き（z が手前）のまま行い、置くときだけ z を反転する。
    /// </summary>
    public static class Scenery
    {
        const float TAN_MIN_ELEV = 0.8390996f;          // tan(40°)
        const float NEAR_MAX = 1.5f, NEAR_DEPTH = 7f, MODEL_SCALE = 0.2f;

        class PropSpec { public string k; public float w, h0, h1; public bool near; }
        class Pal { public int trunk, leaf, wall, roof, pot, win; }
        class SetDef { public int bg, ground, rim; public Pal pal; public List<PropSpec> props; }

        static Pal P(int trunk, int leaf, int wall, int roof, int pot, int win)
        { return new Pal { trunk = trunk, leaf = leaf, wall = wall, roof = roof, pot = pot, win = win }; }
        static PropSpec S(string k, float w, float h0, float h1, bool near = false)
        { return new PropSpec { k = k, w = w, h0 = h0, h1 = h1, near = near }; }

        static readonly Dictionary<string, SetDef> SETS = new Dictionary<string, SetDef>
        {
            { "カフェ", new SetDef { bg = 0xd8e8f0, ground = 0x9fc48a, rim = 0x7fa76c,
                pal = P(0x8a6a44, 0x7cc76a, 0xd8d2c4, 0xc06a5a, 0xc9b79a, 0xffe9a8),
                props = new List<PropSpec> { S("tree",3,3.2f,5.4f), S("bush",3,0.5f,0.9f,true), S("pot",3,0.6f,1.0f,true), S("parasol",2,2.1f,2.5f), S("building",1,3.5f,5.5f) } } },
            { "定食屋", new SetDef { bg = 0xe6e0cf, ground = 0xbfae8c, rim = 0x9d8e6f,
                pal = P(0x7a5a3a, 0x6faa5a, 0xd6cbb2, 0x8a6a4a, 0xb08a5a, 0xffe0a0),
                props = new List<PropSpec> { S("crate",4,0.55f,0.85f,true), S("pot",3,0.6f,1.0f,true), S("tree",2,3.0f,4.6f), S("building",2,3.2f,5.0f), S("rock",1,0.5f,0.9f,true) } } },
            { "町中華", new SetDef { bg = 0xf0d8c8, ground = 0xb08a72, rim = 0x8c6a55,
                pal = P(0x9fbf6a, 0xd93b2b, 0xd8c0a8, 0xc23a2a, 0x8a3a2a, 0xffd24a),
                props = new List<PropSpec> { S("lantern",4,2.4f,3.2f), S("bamboo",3,3.4f,5.6f), S("building",3,3.6f,5.8f), S("crate",2,0.55f,0.8f,true), S("pot",2,0.6f,1.0f,true) } } },
            { "ファミレス", new SetDef { bg = 0xdfe6ee, ground = 0xa8b0a0, rim = 0x87907e,
                pal = P(0x7a6a52, 0x6fbf5a, 0xdfe2e6, 0x5a7fbf, 0xb9bec6, 0xffe9a8),
                props = new List<PropSpec> { S("bush",4,0.5f,0.9f,true), S("tree",3,3.0f,4.8f), S("building",2,3.4f,5.2f), S("pot",2,0.6f,0.95f,true), S("lantern",1,2.6f,3.4f) } } },
            { "カジュアルレストラン", new SetDef { bg = 0xe4e0e8, ground = 0x9fb08a, rim = 0x7f8f6c,
                pal = P(0x86643f, 0x69b45c, 0xd0c8bc, 0x9a5a4a, 0xc0a888, 0xffeeb0),
                props = new List<PropSpec> { S("tree",3,3.4f,5.2f), S("cone",2,1.8f,2.8f), S("pot",3,0.6f,1.0f,true), S("parasol",2,2.1f,2.5f), S("building",2,3.6f,5.6f) } } },
            { "高級和食", new SetDef { bg = 0xe8e6dc, ground = 0xa9ac96, rim = 0x878a76,
                pal = P(0x6a4f34, 0x5f9a52, 0xbdbdb4, 0x4a4a48, 0x9a9a92, 0xffe6b0),
                props = new List<PropSpec> { S("toro",3,1.5f,2.1f), S("cone",3,2.2f,3.4f), S("bamboo",3,3.6f,5.4f), S("rock",3,0.5f,1.0f,true), S("bush",2,0.5f,0.85f,true) } } },
            { "高級フレンチ", new SetDef { bg = 0xe6e4ee, ground = 0x9ea98f, rim = 0x7c8570,
                pal = P(0x7a6248, 0x5fa855, 0xdcd6cc, 0x7a6a8a, 0xcfc6b6, 0xfff0c0),
                props = new List<PropSpec> { S("cone",4,1.8f,3.0f), S("pot",3,0.65f,1.05f,true), S("tree",2,3.6f,5.4f), S("building",2,4.0f,6.0f), S("rock",1,0.5f,0.9f,true) } } },
            { "最高級ホテル", new SetDef { bg = 0xdfe4ee, ground = 0x94a596, rim = 0x74857a,
                pal = P(0x6a5a48, 0x4f9a68, 0xe6e2da, 0x5a6a8a, 0xd0cabe, 0xfff2c8),
                props = new List<PropSpec> { S("cone",3,2.2f,3.6f), S("tree",3,4.0f,6.2f), S("building",3,4.5f,7.0f), S("pot",3,0.7f,1.1f,true), S("toro",1,1.4f,2.0f) } } },
        };
        static SetDef SetOf(string name) { SetDef s; return name != null && SETS.TryGetValue(name, out s) ? s : SETS["ファミレス"]; }

        // ---- 飾りのモデル（models.json の sceneryModels）。遊ぶ前に読んでおく（並べたあとで差し替えるとちらつくので）
        static readonly Dictionary<string, Dictionary<string, List<GameObject>>> models =
            new Dictionary<string, Dictionary<string, List<GameObject>>>();

        public static async System.Threading.Tasks.Task LoadModels()
        {
            models.Clear();
            var man = DataRoot.ReadJson("assets/models.json");
            var sm = man != null ? man["sceneryModels"] as JObject : null;
            if (sm == null) return;
            var lists = new List<KeyValuePair<string, JObject>> { new KeyValuePair<string, JObject>("*", sm) };
            var themes = sm["themes"] as JObject;
            if (themes != null) foreach (var t in themes.Properties()) if (t.Value is JObject) lists.Add(new KeyValuePair<string, JObject>(t.Name, (JObject)t.Value));
            var jobs = new List<System.Threading.Tasks.Task>();
            foreach (var L in lists)
                foreach (var p in L.Value.Properties())
                {
                    if (p.Name == "themes" || p.Name.StartsWith("_") || !(p.Value is JArray)) continue;
                    string theme = L.Key, kind = p.Name;
                    var srcs = new List<string>();
                    foreach (var s in (JArray)p.Value) srcs.Add((string)s);
                    jobs.Add(LoadList(theme, kind, srcs));
                }
            await System.Threading.Tasks.Task.WhenAll(jobs);
        }

        /// <summary>作り直したモデル（assets/oc/…）。実寸[m]・正面が +Z なので、縮尺 1 で回さない。</summary>
        static readonly HashSet<GameObject> ocRoots = new HashSet<GameObject>();

        static async System.Threading.Tasks.Task LoadList(string theme, string kind, List<string> srcs)
        {
            var roots = new List<GameObject>();
            foreach (var s in srcs) { var g = await ModelStore.LoadGlb(s); if (g != null) { roots.Add(g); if (s.StartsWith("oc/")) ocRoots.Add(g); } }
            if (roots.Count == 0) return;
            Dictionary<string, List<GameObject>> d;
            if (!models.TryGetValue(theme, out d)) models[theme] = d = new Dictionary<string, List<GameObject>>();
            d[kind] = roots;
        }

        static List<GameObject> ModelsOf(string kind, string theme)
        {
            Dictionary<string, List<GameObject>> d; List<GameObject> l;
            if (theme != null && models.TryGetValue(theme, out d) && d.TryGetValue(kind, out l)) return l;
            if (models.TryGetValue("*", out d) && d.TryGetValue(kind, out l)) return l;
            return null;
        }

        // ---------------------------------------------------------------- 部品（three.js の向きで組む）
        static Transform root;
        static PropLooks.N Add(Mesh mesh, int col) { return new PropLooks.N(Part.Add(root, mesh, Mats.Get(col), Vector3.zero, shadow: false).transform); }

        delegate float Rnd();

        static float Tree(float h, Pal p, Rnd rnd)
        {
            float tr = h * 0.10f;
            Add(MeshGen.Cylinder(tr * 0.8f, tr, h * 0.55f, 7), p.trunk).Y(h * 0.55f / 2);
            int n = 2 + (rnd() < 0.5f ? 1 : 0);
            for (int i = 0; i < n; i++)
            {
                float r = h * 0.30f * (1 - i * 0.16f);
                float x = (rnd() - 0.5f) * h * 0.10f, y = h * 0.55f + i * h * 0.17f + r * 0.5f, z = (rnd() - 0.5f) * h * 0.10f;
                Add(MeshGen.Sphere(r, 8, 6), p.leaf).P(x, y, z).SY(0.86f);
            }
            return h;
        }
        static float Cone(float h, Pal p, Rnd rnd)
        {
            Add(MeshGen.Cylinder(h * 0.07f * 0.8f, h * 0.07f, h * 0.28f, 7), p.trunk).Y(h * 0.14f);
            Add(MeshGen.Cone(h * 0.30f, h * 0.78f, 8), p.leaf).Y(h * 0.28f + h * 0.39f);
            return h;
        }
        static float Bamboo(float h, Pal p, Rnd rnd)
        {
            for (int i = 0; i < 3; i++)
            {
                float hh = h * (0.7f + rnd() * 0.3f);
                float cx = (rnd() - 0.5f) * 0.6f, cz = (rnd() - 0.5f) * 0.6f;
                Add(MeshGen.Cylinder(0.055f, 0.07f, hh, 6), p.trunk).P(cx, hh / 2, cz).R(0, 0, (rnd() - 0.5f) * 0.10f);
                for (int k = 0; k < 2; k++)
                {
                    float lx = cx + (rnd() - 0.5f) * 0.5f, ly = hh * (0.62f + k * 0.2f), lz = cz + (rnd() - 0.5f) * 0.5f;
                    Add(MeshGen.Sphere(0.34f, 6, 4), p.leaf).P(lx, ly, lz).S(1.5f, 0.35f, 1.5f);
                }
            }
            return h;
        }
        static float Bush(float h, Pal p, Rnd rnd)
        {
            for (int i = 0; i < 2; i++)
            {
                float r = h * 0.55f * (1 - i * 0.2f);
                float x = (rnd() - 0.5f) * h * 0.5f, z = (rnd() - 0.5f) * h * 0.5f;
                Add(MeshGen.Sphere(r, 7, 5), p.leaf).P(x, r * 0.75f, z).SY(0.7f);
            }
            return h;
        }
        static float Pot(float h, Pal p, Rnd rnd)
        {
            Add(MeshGen.Cylinder(h * 0.34f, h * 0.26f, h * 0.42f, 8), p.pot).Y(h * 0.21f);
            Add(MeshGen.Sphere(h * 0.34f, 7, 5), p.leaf).Y(h * 0.62f).SY(0.8f);
            return h;
        }
        static float Building(float h, Pal p, Rnd rnd)
        {
            float w = h * (0.62f + rnd() * 0.30f), d = h * (0.58f + rnd() * 0.26f);
            Add(MeshGen.Box(w, h * 0.76f, d), p.wall).Y(h * 0.38f);
            float rr = Mathf.Max(w, d) * 0.60f;
            Add(MeshGen.Box(rr * 1.42f, h * 0.05f, rr * 1.42f), p.wall).Y(h * 0.76f + h * 0.02f).R(0, Mathf.PI / 4, 0);
            Add(MeshGen.Cone(rr, h * 0.26f, 4), p.roof).Y(h * 0.76f + h * 0.15f).R(0, Mathf.PI / 4, 0);
            for (int i = 0; i < 2; i++) Add(MeshGen.Box(w * 0.16f, h * 0.15f, 0.06f), p.win).P((i - 0.5f) * w * 0.44f, h * 0.44f, d / 2 + 0.03f);
            return h;
        }
        static float Lantern(float h, Pal p, Rnd rnd)
        {
            Add(MeshGen.Cylinder(0.20f, 0.26f, 0.18f, 8), p.pot).Y(0.09f);
            Add(MeshGen.Cylinder(0.06f, 0.07f, h, 6), p.pot).Y(h / 2);
            Add(MeshGen.Box(0.46f, 0.07f, 0.07f), p.pot).P(0.20f, h - 0.10f, 0);
            for (int i = 0; i < 2; i++)
            {
                Add(MeshGen.Sphere(0.20f, 8, 6), p.leaf).P(0.40f - i * 0.40f * 2, h - 0.34f - i * 0.06f, 0).SY(1.25f);
                if (i == 0) Add(MeshGen.Box(0.46f, 0.07f, 0.07f), p.pot).P(-0.20f, h - 0.10f, 0);
            }
            return h;
        }
        static float Parasol(float h, Pal p, Rnd rnd)
        {
            Add(MeshGen.Cylinder(0.045f, 0.05f, h, 6), p.pot).Y(h / 2);
            Add(MeshGen.Cone(h * 0.55f, h * 0.26f, 8), p.leaf).Y(h * 0.94f);
            return h;
        }
        static float Crate(float h, Pal p, Rnd rnd)
        {
            Add(MeshGen.Box(h, h, h * 0.92f), p.pot).Y(h / 2);
            if (rnd() < 0.45f)
            {
                float x = (rnd() - 0.5f) * h * 0.3f, z = (rnd() - 0.5f) * h * 0.3f;
                Add(MeshGen.Box(h * 0.8f, h * 0.8f, h * 0.72f), p.pot).P(x, h + h * 0.4f, z).R(0, (rnd() - 0.5f) * 0.6f, 0);
                return h * 1.8f;
            }
            return h;
        }
        static float Rock(float h, Pal p, Rnd rnd)
        {
            Add(MeshGen.Icosahedron(h * 0.6f), p.wall).Y(h * 0.34f).S(1, 0.62f, 1.15f).R(0, rnd() * Mathf.PI, 0);
            return h * 0.7f;
        }
        static float Toro(float h, Pal p, Rnd rnd)
        {
            Add(MeshGen.Cylinder(h * 0.20f, h * 0.24f, h * 0.16f, 6), p.wall).Y(h * 0.08f);
            Add(MeshGen.Cylinder(h * 0.09f, h * 0.10f, h * 0.44f, 6), p.wall).Y(h * 0.38f);
            Add(MeshGen.Box(h * 0.30f, h * 0.20f, h * 0.30f), p.leaf).Y(h * 0.70f);
            Add(MeshGen.Cone(h * 0.30f, h * 0.18f, 6), p.wall).Y(h * 0.89f);
            return h;
        }

        delegate float Maker(float h, Pal p, Rnd rnd);
        static readonly Dictionary<string, Maker> MAKERS = new Dictionary<string, Maker>
        {
            {"tree",Tree},{"cone",Cone},{"bamboo",Bamboo},{"bush",Bush},{"pot",Pot},{"building",Building},
            {"lantern",Lantern},{"parasol",Parasol},{"crate",Crate},{"rock",Rock},{"toro",Toro},
        };

        /// <summary>届いたモデルで飾りを作る。縮尺は置き物と同じ（5単位＝1m）。手前で高すぎるときだけ縮める。</summary>
        static Transform FromModel(List<GameObject> roots, string k, float maxH, Rnd rnd, out float h, out float r, out bool face)
        {
            h = 0; r = 0; face = k == "building";
            var src = roots[Mathf.Min(roots.Count - 1, (int)(rnd() * roots.Count))];
            var g = new GameObject("SceneryModel").transform;
            bool oc = ocRoots.Contains(src);
            var outer = new GameObject("Rot").transform; outer.SetParent(g, false);
            outer.localRotation = oc ? Quaternion.identity : Part.Euler3(0, Mathf.PI, 0);   // 正面（-Z）を手前（カメラ）へ。作り直したモデルは回さない
            var inst = Object.Instantiate(src, outer, false);
            inst.SetActive(true);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(0, 180, 0);   // glTFast の向き → このプロジェクトの向き
            // 大きさを測る
            bool any = false; var bb = new Bounds();
            var w2l = g.worldToLocalMatrix;
            foreach (var rd in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mf = rd.GetComponent<MeshFilter>(); Mesh mesh = mf != null ? mf.sharedMesh : null;
                var smr = rd as SkinnedMeshRenderer; if (smr != null) mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var m = w2l * rd.transform.localToWorldMatrix; var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { bb = new Bounds(p, Vector3.zero); any = true; } else bb.Encapsulate(p);
                }
            }
            float mh = bb.size.y;
            if (!any || mh <= 1e-4f) { Object.Destroy(g.gameObject); return null; }
            float s = Mathf.Min(oc ? 1 : MODEL_SCALE, maxH / mh);
            h = mh * s;
            if (h < 0.3f) { Object.Destroy(g.gameObject); return null; }
            outer.localScale = Vector3.one * s;
            outer.localPosition = new Vector3(-bb.center.x * s, -bb.min.y * s, -bb.center.z * s);
            r = Mathf.Max(bb.size.x, bb.size.z) * s * 0.5f;
            foreach (var rd in inst.GetComponentsInChildren<Renderer>(true))
            { rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rd.receiveShadows = false; }
            return g;
        }

        /// <summary>
        /// 並べる。bounds は three.js の向き（z0 が奥、z1 が手前）。clear は置かない帯（同じ向き）。
        /// 戻り値は背景の色。
        /// </summary>
        // ---------------------------------------------------------------- 島（JS版 buildIsland）
        // 角を丸めた厚い板（上は地面の色、横は土の層か石垣）と、まわりの海・岸の白い泡。座標は three.js の向きで数えて、z を反転して置く
        const float ISLAND_TOP = -0.94f, ISLAND_T = 2.4f, SEA_Y = -2.2f, ISL_BEV = 0.25f;

        /// <summary>角を丸めた長方形のふち（three.js の (x, z)）。角は quadraticCurveTo と同じ2次の曲線で seg 分割。</summary>
        static List<Vector2> RoundRect(float x0, float z0, float x1, float z1, float r, int seg = 10)
        {
            var pts = new List<Vector2>();
            System.Action<Vector2, Vector2, Vector2> curve = (a, c, b) =>
            {
                for (int i = 1; i <= seg; i++)
                {
                    float t = (float)i / seg;
                    pts.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b);
                }
            };
            pts.Add(new Vector2(x0 + r, z0));
            pts.Add(new Vector2(x1 - r, z0)); curve(new Vector2(x1 - r, z0), new Vector2(x1, z0), new Vector2(x1, z0 + r));
            pts.Add(new Vector2(x1, z1 - r)); curve(new Vector2(x1, z1 - r), new Vector2(x1, z1), new Vector2(x1 - r, z1));
            pts.Add(new Vector2(x0 + r, z1)); curve(new Vector2(x0 + r, z1), new Vector2(x0, z1), new Vector2(x0, z1 - r));
            pts.Add(new Vector2(x0, z0 + r)); curve(new Vector2(x0, z0 + r), new Vector2(x0, z0), new Vector2(x0 + r, z0));
            return pts;
        }

        /// <summary>島の側面の絵（256×256）。石垣（stone）か土の層（soil）。v=0（絵の上）が島の上の縁。</summary>
        static Texture2D SideTex(string kind, int gCol)
        {
            const int N = 256;
            var px = new Color[N * N];
            System.Action<int, int, int, int, Color> fill = (x, y, w, h, c) =>
            {
                for (int yy = Mathf.Max(0, y); yy < Mathf.Min(N, y + h); yy++)
                    for (int xx = Mathf.Max(0, x); xx < Mathf.Min(N, x + w); xx++)
                    {
                        int i = yy * N + xx; var d = px[i];
                        px[i] = new Color(d.r + (c.r - d.r) * c.a, d.g + (c.g - d.g) * c.a, d.b + (c.b - d.b) * c.a, 1);
                    }
            };
            System.Func<float, float, float, float, Color> rgba = (r, g, b, a) => new Color(r / 255f, g / 255f, b / 255f, a);
            if (kind == "stone")
            {
                // 石垣：段ごとにずらした石。上は笠石、下は水に濡れて濃い
                fill(0, 0, N, N, Mats.Hex(0x6e6a62));
                int rows = 7; float rh = N / (float)rows;
                for (int r = 0; r < rows; r++)
                {
                    int x = -(r % 2) * 30;
                    while (x < N)
                    {
                        int w = 48 + ((x * 7 + r * 13) % 22 + 22) % 22;
                        int v = 150 + ((x * 3 + r * 29) % 40 + 40) % 40;
                        fill(x + 3, Mathf.RoundToInt(r * rh + 3), w - 6, Mathf.RoundToInt(rh - 6), rgba(v, v - 6, v - 18, 1));
                        fill(x + 8, Mathf.RoundToInt(r * rh + 6), w - 16, 4, rgba(255, 255, 255, 0.18f));
                        x += w;
                    }
                }
                fill(0, 0, N, 18, Mats.Hex(0xc8c2b4));                       // 笠石
                fill(0, 3, N, 4, rgba(255, 255, 255, 0.35f));
                for (int y = 190; y < N; y++) fill(0, y, N, 1, rgba(40, 60, 60, 0.55f * (y - 190) / 66f));
            }
            else
            {
                // 土の層：上に草のふち、茶色の層、小石
                int[] lay = { 0x8a5a36, 0x7a4e2e, 0x946440, 0x6a4428 };
                for (int k = 0; k < 4; k++) fill(0, 30 + k * 57, N, 57, Mats.Hex(lay[k]));
                for (int i = 0; i < 70; i++)
                {
                    int x = (i * 97) % N, y = 40 + (i * 61) % 210; float r = 3 + (i % 4) * 2;
                    var c = Mats.Hex(i % 3 != 0 ? 0xa88a6a : 0x5a3a22);
                    for (int yy = (int)(y - r); yy <= (int)(y + r); yy++)
                        for (int xx = (int)(x - r * 1.4f); xx <= (int)(x + r * 1.4f); xx++)
                        {
                            float dx = (xx + 0.5f - x) / (r * 1.4f), dy = (yy + 0.5f - y) / r;
                            if (dx * dx + dy * dy <= 1) fill(xx, yy, 1, 1, c);
                        }
                }
                fill(0, 0, N, 30, Mats.Hex(gCol));
                for (int x = 0; x < N; x += 16)                                // 草のふちのギザギザ
                    for (int y = 28; y < 42; y++)
                    {
                        float half = 8 * (1 - (y - 28) / 14f);
                        fill(Mathf.RoundToInt(x + 8 - half), y, Mathf.RoundToInt(half * 2), 1, Mats.Hex(gCol));
                    }
            }
            // 配列の 0 行目（Unity の絵の下＝v=0）に canvas の上を置く（JS版の flipY:false と同じ向き）
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        /// <summary>海の絵（256×256。小さな波の線）。</summary>
        static Texture2D SeaTex()
        {
            const int N = 256;
            var px = new Color[N * N];
            var bg = Mats.Hex(0x3f9fcf);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            for (int i = 0; i < 14; i++)
            {
                int x = (i * 83) % N, y = (i * 47) % N;
                foreach (int ox in new[] { 0, -N })
                    for (float a = Mathf.PI * 1.15f; a <= Mathf.PI * 1.85f; a += 0.02f)
                    {
                        float cx = x + ox + Mathf.Cos(a) * 12, cy = y + 10 + Mathf.Sin(a) * 12;
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int X = (int)cx + dx, Y = (int)cy + dy;
                                if (X < 0 || X >= N || Y < 0 || Y >= N || dx * dx + dy * dy > 4) continue;
                                int k = (N - 1 - Y) * N + X;
                                px[k] = Color.Lerp(px[k], Color.white, 0.35f);
                            }
                    }
            }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        /// <summary>島を組む。m は [左, 奥, 右, 手前] = 盤面の縁から島の縁までのマス数。戻り値は島の範囲（three.js の向き）。</summary>
        static Rect BuildIsland(Transform group, float bx0, float bx1, float bz0, float bz1, int[] m, string side, bool withSea, int gCol)
        {
            var rect = Rect.MinMaxRect(bx0 - m[0], bz0 - m[1], bx1 + m[2], bz1 + m[3]);
            const float R = 2.5f;
            var ring = RoundRect(rect.xMin, rect.yMin, rect.xMax, rect.yMax, R);
            float top = ISLAND_TOP, bot = ISLAND_TOP - ISLAND_T - ISL_BEV * 2;
            // 上の面（扇に分ける。形は凸なので中心から）
            var tv = new List<Vector3>(); var tt = new List<int>();
            var cen = rect.center;
            tv.Add(new Vector3(cen.x, top, -cen.y));
            foreach (var p in ring) tv.Add(new Vector3(p.x, top, -p.y));
            for (int i = 1; i <= ring.Count; i++) { int a = i, b = i % ring.Count + 1; tt.Add(0); tt.Add(b); tt.Add(a); }
            var topMesh = new Mesh { name = "IslandTop" };
            topMesh.SetVertices(tv); topMesh.SetTriangles(tt, 0); topMesh.RecalculateNormals(); topMesh.RecalculateBounds();
            if (Vector3.Dot(topMesh.normals[0], Vector3.up) < 0) { tt.Reverse(); topMesh.SetTriangles(tt, 0); topMesh.RecalculateNormals(); }
            Part.Add(group, topMesh, Mats.Get(gCol), Vector3.zero, false, "IslandTop");
            // 横の面。横は 2.5m で絵1枚、縦は側面の高さで1枚
            var sv = new List<Vector3>(); var su = new List<Vector2>(); var st = new List<int>();
            float len = 0;
            for (int i = 0; i <= ring.Count; i++)
            {
                var p = ring[i % ring.Count];
                if (i > 0) len += (p - ring[i - 1]).magnitude;
                sv.Add(new Vector3(p.x, top, -p.y)); su.Add(new Vector2(len * 0.4f, 0));
                sv.Add(new Vector3(p.x, bot, -p.y)); su.Add(new Vector2(len * 0.4f, 1));
                if (i > 0) { int o = (i - 1) * 2; st.AddRange(new[] { o, o + 2, o + 1, o + 1, o + 2, o + 3 }); }
            }
            var sideMesh = new Mesh { name = "IslandSide" };
            sideMesh.SetVertices(sv); sideMesh.SetUVs(0, su); sideMesh.SetTriangles(st, 0); sideMesh.RecalculateNormals(); sideMesh.RecalculateBounds();
            // 外向きでなければ裏返す
            var n0 = sideMesh.normals[0]; var out0 = new Vector3(sv[0].x - cen.x, 0, sv[0].z + cen.y);
            if (Vector3.Dot(n0, out0) < 0) { var rev = new List<int>(); for (int i = 0; i < st.Count; i += 3) { rev.Add(st[i]); rev.Add(st[i + 2]); rev.Add(st[i + 1]); } sideMesh.SetTriangles(rev, 0); sideMesh.RecalculateNormals(); }
            Part.Add(group, sideMesh, Mats.Textured(SideTex(side == "stone" ? "stone" : "soil", gCol), Color.white, 1f, Vector2.one), Vector3.zero, false, "IslandSide");
            if (withSea)
            {
                var seaMat = Mats.Textured(SeaTex(), Color.white, 1f, new Vector2(110, 110));
                var sea = Part.Add(group, MeshGen.FlatUp(600, 600), seaMat, new Vector3(cen.x, SEA_Y, -cen.y), false, "Sea");
                sea.GetComponent<MeshRenderer>().receiveShadows = false;
                // 岸の白い泡（島の形より少し大きい輪）
                const float F = 0.45f;
                var outer = RoundRect(rect.xMin - F - cen.x, rect.yMin - F - cen.y, rect.xMax + F - cen.x, rect.yMax + F - cen.y, R + F);
                var inner = RoundRect(rect.xMin - cen.x, rect.yMin - cen.y, rect.xMax - cen.x, rect.yMax - cen.y, R);
                var fv = new List<Vector3>(); var ft = new List<int>();
                for (int i = 0; i < outer.Count; i++) { fv.Add(new Vector3(outer[i].x, 0, -outer[i].y)); fv.Add(new Vector3(inner[i].x, 0, -inner[i].y)); }
                for (int i = 0; i < outer.Count; i++)
                {
                    int a = i * 2, b = ((i + 1) % outer.Count) * 2;
                    ft.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    ft.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });          // 両面
                }
                var foamMesh = new Mesh { name = "Foam" };
                foamMesh.SetVertices(fv); foamMesh.SetTriangles(ft, 0); foamMesh.RecalculateNormals(); foamMesh.RecalculateBounds();
                var foamMat = Mats.NewBasic(new Color(1, 1, 1, 0.7f), true, true, false);
                var foam = Part.Add(group, foamMesh, foamMat, new Vector3(cen.x, SEA_Y + 0.03f, -cen.y), false, "Foam");
                Ambient.AddSea(seaMat, foam.transform, foamMat);
            }
            foreach (var r in group.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            return rect;
        }

        /// <summary>
        /// 並べる。bounds は three.js の向き（z0 が奥、z1 が手前）。clear は置かない帯（同じ向き）。戻り値は背景の色。
        /// island … ステージを島にする。[左, 奥, 右, 手前] = 盤面の縁から島の縁までのマス数。縁に土（soil）か石垣（stone）の側面を見せ、
        ///          まわりは海（islandSea が false なら空に浮かぶ島）。ジオラマのように舞台が区切られる。
        /// </summary>
        public static Color Build(Transform parent, float bx0, float bx1, float bz0, float bz1, float depth, int seed,
                                  string theme, bool sea, bool props, List<Rect> clear,
                                  int[] island = null, string islandSide = "soil", bool islandSea = true)
        {
            var set = SetOf(theme);
            var pal = set.pal;
            var group = new GameObject("Scenery").transform;
            group.SetParent(parent, false);
            var lcg = new Lcg(seed);
            Rnd rnd = () => (float)lcg.Next();
            float w = bx1 - bx0, d = bz1 - bz0, cx = (bx0 + bx1) / 2, cz = (bz0 + bz1) / 2;
            const float gap = 1.2f;
            int gCol = sea ? 0x2a5f7a : set.ground;
            // ---- 地面と、部屋のすぐ外の縁
            root = group;
            Rect? isl = null;
            if (island != null && island.Length >= 4) isl = BuildIsland(group, bx0, bx1, bz0, bz1, island, islandSide, islandSea, gCol);
            else Add(MeshGen.FlatUp(w + depth * 2 + 60, d + depth * 2 + 60), gCol).P(cx, -0.94f, cz);
            Add(MeshGen.FlatUp(w + gap * 2 + 1.8f, d + gap * 2 + 1.8f), sea ? 0x1f4c62 : set.rim).P(cx, -0.92f, cz);

            var lowProps = set.props.FindAll(p => p.near && p.h0 <= NEAR_MAX);
            System.Func<List<PropSpec>, PropSpec> pickFrom = list =>
            {
                float total = 0; foreach (var p in list) total += p.w;
                float r = rnd() * total;
                foreach (var p in list) { r -= p.w; if (r <= 0) return p; }
                return list[list.Count - 1];
            };
            float ringArea = (w + depth * 2) * (d + depth * 2) - w * d;
            int count = props ? Mathf.Min(220, Mathf.RoundToInt(ringArea * 0.05f)) : 0;
            var placed = new List<Vector3>();      // x, z, r
            for (int n = 0; n < count * 5 && placed.Count < count; n++)
            {
                float zr = rnd();
                string zn = zr < 0.42f ? "far" : (zr < 0.84f ? "side" : "near");
                float x, z; var list = set.props;
                if (zn == "far") { x = bx0 - depth + rnd() * (w + depth * 2); z = bz0 - gap - rnd() * depth; }
                else if (zn == "side")
                {
                    bool left = rnd() < 0.5f;
                    x = left ? bx0 - gap - rnd() * depth : bx1 + gap + rnd() * depth;
                    z = bz0 - depth * 0.35f + rnd() * (d + depth * 0.35f + 1.0f);
                }
                else
                {
                    x = bx0 - depth + rnd() * (w + depth * 2);
                    z = bz1 + gap + rnd() * NEAR_DEPTH;
                    list = lowProps;
                    if (list.Count == 0) continue;
                }
                var ps = pickFrom(list);
                float h = ps.h0 + rnd() * (ps.h1 - ps.h0);
                bool capped = false;
                if (z > bz1 && x > bx0 - 1.5f && x < bx1 + 1.5f)
                {
                    h = Mathf.Min(h, NEAR_MAX, (z - bz1) * TAN_MIN_ELEV);
                    capped = true;
                    if (h < 0.4f) continue;
                }
                var roots = ModelsOf(ps.k, theme);
                float mh = 0, mr = 0; bool face = false;
                Transform mm = null;
                if (roots != null && roots.Count > 0) mm = FromModel(roots, ps.k, capped ? h : float.PositiveInfinity, rnd, out mh, out mr, out face);
                float rad = mm != null ? Mathf.Max(0.55f, mr) : Mathf.Max(0.55f, h * 0.45f);
                bool hit = false;
                foreach (var q in placed) if (Mathf.Sqrt((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z)) < q.z + rad) { hit = true; break; }
                if (!hit && clear != null)
                    foreach (var c in clear) if (x > c.xMin - rad && x < c.xMax + rad && z > c.yMin - rad && z < c.yMax + rad) { hit = true; break; }
                // 島のときは、島の外（海の上）に置かない
                if (!hit && isl != null && (x < isl.Value.xMin + rad || x > isl.Value.xMax - rad || z < isl.Value.yMin + rad || z > isl.Value.yMax - rad)) hit = true;
                if (hit) { if (mm != null) Object.Destroy(mm.gameObject); continue; }
                Transform obj = mm;
                if (obj == null)
                {
                    obj = new GameObject("Scenery_" + ps.k).transform;
                    root = obj;
                    MAKERS[ps.k](h, pal, rnd);
                    root = group;
                }
                obj.SetParent(group, false);
                obj.localPosition = Coord.W(x, -0.9f, z);
                float ry = face ? (rnd() - 0.5f) * 0.5f : rnd() * Mathf.PI * 2;
                obj.localRotation = Part.Euler3(0, ry, 0);
                obj.localScale = Vector3.one * (0.9f + rnd() * 0.22f);
                placed.Add(new Vector3(x, z, rad));
            }
            root = null;
            Ambient.FxAttach(group.gameObject);           // 飾りのモデルの中の動く部品（風車・旗・煙）
            return Mats.Hex(isl != null && islandSea ? 0xbfe0ee : sea ? 0xbcd8e6 : set.bg);
        }
    }
}
