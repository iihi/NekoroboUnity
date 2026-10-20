using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nekorobo
{
    /// <summary>
    /// 床チップ（HTML版の assets/floor/floors.json）。JS版の LINE_AT / LIQ_AT / ICE_SKIN と、床・壁のデザインの絵。
    ///
    ///   designs … 床チップ（f000〜）と壁の絵（w000〜）。Designs に足す（surf：床・壁専用。画像は使うときに読む）
    ///   roads   … 道路・線路・橋・桟橋。形ごとの絵を1枚にまとめた画像（atlas）。k 番目が形の番号（つながる向きのビット）
    ///   liquids … 川（水）・海・マグマ。岸つきのまとめ画像（透明度＝岸からの距離）と、流す全面の絵（surface）
    ///   ice     … 凍りの床の岸（雪との境目）。隣の8マスを見て47通りから選ぶ（masks：形 → 何枚目）
    /// まとめ画像は 1枚 tile px の絵を pad px の余白つきで cell px おきに cols 列に並べたもの。上が北（three.js の -z）。
    /// </summary>
    public static class FloorSet
    {
        public class Atlas
        {
            public int cols, cell, pad, tile, w, h;
            public Dictionary<int, int> masks;          // 形のビット → 何枚目（無ければ形のビットのまま）
            public Texture2D tex, surf;
        }

        public static readonly Dictionary<string, Atlas> Lines = new Dictionary<string, Atlas>();
        public static readonly Dictionary<string, Atlas> Liquids = new Dictionary<string, Atlas>();
        public static Atlas Ice;
        public static string Log = "";

        /// <summary>起動時に1回（JS版 loadAssets の floors.json のところ）。</summary>
        public static void Load()
        {
            Lines.Clear(); Liquids.Clear(); Ice = null;
            var fl = DataRoot.ReadJson("assets/floor/floors.json");
            if (fl == null) { Log = "floors.json なし"; return; }
            Designs.Register(fl["designs"] as JObject);
            if (fl["roads"] is JObject ro)
                foreach (var p in ro.Properties()) { var A = Parse(p.Value as JObject, "atlas", false); if (A != null) Lines[p.Name] = A; }
            if (fl["liquids"] is JObject lq)
                foreach (var p in lq.Properties())
                {
                    var A = Parse(p.Value as JObject, "atlas", false);
                    if (A == null) continue;
                    A.surf = Tex((string)p.Value["surface"], true);
                    if (A.surf != null) Liquids[p.Name] = A;
                }
            Ice = Parse(fl["ice"] as JObject, "atlas", false);
            Log = "床チップ " + ((fl["designs"] as JObject)?.Count ?? 0) + "種類・道 " + Lines.Count + "・水 " + Liquids.Count + (Ice != null ? "・氷の岸" : "");
        }

        static Atlas Parse(JObject o, string key, bool repeat)
        {
            if (o == null) return null;
            var t = Tex((string)o[key], repeat);
            if (t == null) return null;
            var A = new Atlas
            {
                tex = t, cols = J.I(o, "cols", 1), cell = J.I(o, "cell", 144), pad = J.I(o, "pad", 8),
                tile = J.I(o, "tile", 128), w = J.I(o, "w", 1), h = J.I(o, "h", 1),
            };
            if (o["masks"] is JObject mk)
            {
                A.masks = new Dictionary<int, int>();
                foreach (var p in mk.Properties()) { int k; if (int.TryParse(p.Name, out k)) A.masks[k] = p.Value.Value<int>(); }
            }
            return A;
        }

        static Texture2D Tex(string src, bool repeat)
        {
            if (string.IsNullOrEmpty(src)) return null;
            var t = DataRoot.Texture(src);
            if (t == null) return null;
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.anisoLevel = 8;          // 斜めから見てもにじまないように
            return t;
        }

        /// <summary>まとめ画像の n 枚目の UV（u0, v0＝左上, u1, v1＝右下）。</summary>
        public static Vector4 UV(Atlas K, int n)
        {
            float u0 = ((n % K.cols) * K.cell + K.pad) / (float)K.w;
            float v0 = 1 - ((n / K.cols) * K.cell + K.pad) / (float)K.h;
            return new Vector4(u0, v0, u0 + K.tile / (float)K.w, v0 - K.tile / (float)K.h);
        }

        /// <summary>形 → 何枚目（masks に無い形は null）。masks を持たないまとめ画像は形のまま。</summary>
        public static int? Index(Atlas K, int m)
        {
            if (K.masks == null) return m;
            int n; return K.masks.TryGetValue(m, out n) ? n : (int?)null;
        }
    }

    /// <summary>1マスずつの四角を1枚の板にまとめる（何マスあっても描画は1回）。座標は three.js のマス (i, j)。</summary>
    public class CellQuads
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Vector2> uv2 = new List<Vector2>();
        readonly List<int> t = new List<int>();
        public int Count { get { return t.Count / 6; } }

        /// <summary>マス (i, j) に高さ y で四角を置く。uvs は (u0, v0, u1, v1)＝北西の角と南東の角。flow は2つめの UV（川の流れ）。</summary>
        public void Add(int i, int j, float y, Vector4 uvs, Vector2 flow = default(Vector2))
        {
            var c = Coord.Cell(i, j);
            float h = Coord.Tile / 2f;
            int o = v.Count;
            // 北西・北東・南東・南西（three.js の -z＝北は Unity の +z）
            v.Add(new Vector3(c.x - h, y, c.z + h)); v.Add(new Vector3(c.x + h, y, c.z + h));
            v.Add(new Vector3(c.x + h, y, c.z - h)); v.Add(new Vector3(c.x - h, y, c.z - h));
            uv.Add(new Vector2(uvs.x, uvs.y)); uv.Add(new Vector2(uvs.z, uvs.y));
            uv.Add(new Vector2(uvs.z, uvs.w)); uv.Add(new Vector2(uvs.x, uvs.w));
            for (int k = 0; k < 4; k++) uv2.Add(flow);
            t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 3);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }

    public partial class Game
    {
        // ================================================================ 実寸の UV
        /// <summary>
        /// 箱の UV を、面ごとに実寸[m]（盤面の座標）で付け直す（JS版 boxMeterUV）。x 向きの面は (z, y)、上下は (x, z)、z 向きは (x, y)。
        /// 盤面の座標で付けるので、隣り合う壁どうし・床の長方形どうしで模様の位置がそろう。c は箱の中心（Unity の座標）。
        /// 座標は three.js の向き（z を反転）で数える（画面に出る模様を JS版と同じにするため）。
        /// </summary>
        static Mesh MeterUV(Mesh src, Vector3 c)
        {
            var m = Object.Instantiate(src);
            m.name = src.name + "_m";
            var p = m.vertices; var n = m.normals; var uv = new Vector2[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                float x = p[i].x + c.x, y = p[i].y + c.y, z = -(p[i].z + c.z);
                if (Mathf.Abs(n[i].x) > 0.5f) uv[i] = new Vector2(z, y);
                else if (Mathf.Abs(n[i].y) > 0.5f) uv[i] = new Vector2(x, z);
                else uv[i] = new Vector2(x, y);
            }
            m.uv = uv;
            return m;
        }

        // ================================================================ 床の絵（マスごとに塗った床）
        /// <summary>
        /// マスごとに塗った床の絵（エディタの「床の絵を塗る」。ステージの floorPaint：{ デザイン: ["i,j", …] }）。
        /// 「床」のマスだけが対象。塗ったマスはデザインごとに別にまとめて板を敷く（見た目だけ。滑り方や当たり判定はふつうの床と同じ）。
        /// 戻り値はマス → デザイン。
        /// </summary>
        Dictionary<Vector2Int, string> FloorPaint(StageCfg C)
        {
            var outD = new Dictionary<Vector2Int, string>();
            if (C.floorPaint == null) return outD;
            foreach (var kv in C.floorPaint)
            {
                if (Designs.Get(kv.Key) == null) continue;
                foreach (var k in kv.Value)
                {
                    var a = k.Split(',');
                    int i, j;
                    if (a.Length != 2 || !int.TryParse(a[0], out i) || !int.TryParse(a[1], out j)) continue;
                    var key = new Vector2Int(i, j);
                    char ch;
                    if (under.TryGetValue(key, out ch) && ch == '.') outD[key] = kv.Key;
                }
            }
            return outD;
        }

        // ================================================================ 道路・線路・橋・桟橋の絵
        string ConnAt(int i, int j) { char ch; if (!map.TryGetValue(new Vector2Int(i, j), out ch)) return null; var d = Tiles.Def(ch); return d != null ? d.conn : null; }

        /// <summary>
        /// 道路・線路の向き（JS版 lineDir）。車も電車も**まっすぐしか走らない**ので、形は「並び」で決める。
        /// そのマスを通る同じ種類の並びが長いほうの向きの直線として扱う。縦横どちらにも3マス以上続くマス（角・T字・十字）だけ null。
        /// </summary>
        string LineDir(int i, int j, string conn)
        {
            System.Func<int, int, int> run = (di, dj) => { int n = 0; for (int k = 1; ConnAt(i + di * k, j + dj * k) == conn; k++) n++; return n; };
            int h = 1 + run(1, 0) + run(-1, 0), v = 1 + run(0, 1) + run(0, -1);
            if (h >= 3 && v >= 3) return null;
            return h >= v ? "h" : "v";
        }

        /// <summary>道路・線路の絵の形（JS版 lineTile）。盤面の外（マスが無い所）は道が外へ抜けているので「つながっている」。</summary>
        int LineTile(int i, int j, string conn)
        {
            var d = LineDir(i, j, conn);
            if (d == null) return ConnMask(i, j, conn);
            System.Func<int, int, bool> on = (di, dj) => !map.ContainsKey(new Vector2Int(i + di, j + dj)) || ConnAt(i + di, j + dj) == conn;
            return d == "h" ? (on(1, 0) ? 2 : 0) | (on(-1, 0) ? 8 : 0)
                            : (on(0, -1) ? 1 : 0) | (on(0, 1) ? 4 : 0);
        }

        /// <summary>
        /// 橋・桟橋の形（JS版 deckMask）。縁はつながっていない側にだけ付くので、隣が同じ橋か、歩ける床（渡り口）なら「つながる」。
        /// こうすると2マス幅の橋が、外側だけに縁のある1本の太い橋になる。
        /// </summary>
        int DeckMask(int i, int j, char ch)
        {
            int m = 0;
            foreach (var d in DIRS)                           // (di, dj, 向きのビット)。上1 右2 下4 左8
            {
                char nb;
                if (map.TryGetValue(new Vector2Int(i + d.x, j + d.y), out nb) && (nb == ch || Tiles.IsFloor(nb))) m |= d.z;
            }
            return m;
        }

        /// <summary>透明な所がある形（行き止まり・カーブ・ぽつんと1マス）。下に床を敷く。</summary>
        static bool LineOpen(int m) { return m != 5 && m != 10 && ConnCount(m) <= 2; }

        /// <summary>
        /// 形ごとの絵を貼った板（JS版 lineMesh）。cells は (i, j, 形)。under は透明な所に敷く床の素材（null なら敷かない）。
        /// </summary>
        GameObject LineMesh(Transform parent, string kind, List<Vector3Int> cells, Material underMat)
        {
            FloorSet.Atlas K;
            if (!FloorSet.Lines.TryGetValue(kind, out K) || cells.Count == 0) return null;
            var g = new GameObject("Line_" + kind);
            g.transform.SetParent(parent, false);
            if (underMat != null)
            {
                var uq = new CellQuads();
                foreach (var c in cells) if (LineOpen(c.z)) uq.Add(c.x, c.y, 0.002f, new Vector4(0, 1, 1, 0));
                if (uq.Count > 0) Part.Add(g.transform, uq.ToMesh("LineUnder"), underMat, Vector3.zero, false);
            }
            var q = new CellQuads();
            foreach (var c in cells) q.Add(c.x, c.y, 0.004f, FloorSet.UV(K, c.z));
            Part.Add(g.transform, q.ToMesh("Line"), LineMat(kind, K.tex), Vector3.zero, false);
            return g;
        }

        static readonly Dictionary<string, Material> lineMats = new Dictionary<string, Material>();
        /// <summary>まとめ画像の素材。透明な所（カーブの外側など）は切り抜く。</summary>
        static Material LineMat(string kind, Texture2D tex)
        {
            Material m;
            if (lineMats.TryGetValue(kind, out m) && m != null) return m;
            m = Mats.Textured(tex, Color.white, 0.85f, Vector2.one);
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            lineMats[kind] = m;
            return m;
        }

        /// <summary>
        /// 道路・線路・橋・桟橋に絵を貼る（JS版 buildStage の「つながる地形の飾り」の絵のぶん）。
        /// 絵が貼れた道路・線路は白線や枕木を描かない（done に入れて返す）。橋の欄干は今までどおり。
        /// </summary>
        HashSet<Vector2Int> BuildLineSkins(Material floorUnder)
        {
            var done = new HashSet<Vector2Int>();
            var lines = new Dictionary<string, List<Vector3Int>> { { "road", new List<Vector3Int>() }, { "rail", new List<Vector3Int>() } };
            var decks = new Dictionary<string, List<Vector3Int>> { { "bridge", new List<Vector3Int>() }, { "pier", new List<Vector3Int>() } };
            foreach (var kv in map)
            {
                char ch = kv.Value; int i = kv.Key.x, j = kv.Key.y;
                string dk = ch == 'b' ? "bridge" : ch == '=' ? "pier" : null;
                if (dk != null && FloorSet.Lines.ContainsKey(dk)) decks[dk].Add(new Vector3Int(i, j, DeckMask(i, j, ch)));
                var td = Tiles.Def(ch);
                if (td == null || td.conn == null || !lines.ContainsKey(td.conn) || !FloorSet.Lines.ContainsKey(td.conn)) continue;
                lines[td.conn].Add(new Vector3Int(i, j, LineTile(i, j, td.conn)));
                done.Add(kv.Key);
            }
            // カーブの外側などの透明な所には、店の床を敷く。橋・桟橋の透明な所は、下の板がそのまま見える
            foreach (var kv in lines) LineMesh(stageRoot, kv.Key, kv.Value, floorUnder);
            foreach (var kv in decks) LineMesh(stageRoot, kv.Key, kv.Value, null);
            return done;
        }

        // ================================================================ 凍りの床の岸
        /// <summary>隣の8マスのどこが同じ物か（JS版 iceMask / liqMask）。上1 右2 下4 左8、斜めは接する上下左右が両方同じときだけ。</summary>
        static int Mask8(System.Func<int, int, bool> isSame)
        {
            bool n = isSame(0, -1), e = isSame(1, 0), so = isSame(0, 1), w = isSame(-1, 0);
            int m = (n ? 1 : 0) | (e ? 2 : 0) | (so ? 4 : 0) | (w ? 8 : 0);
            if (n && e && isSame(1, -1)) m |= 16;
            if (so && e && isSame(1, 1)) m |= 32;
            if (so && w && isSame(-1, 1)) m |= 64;
            if (n && w && isSame(-1, -1)) m |= 128;
            return m;
        }

        /// <summary>凍りの床に、雪との境目つきの絵を1マスずつ貼る（JS版 buildIceSkin）。見た目だけの1枚の板。</summary>
        void BuildIceSkin()
        {
            var K = FloorSet.Ice;
            if (K == null) return;
            var q = new CellQuads();
            foreach (var kv in under)
            {
                if (kv.Value != 'i') continue;
                int i = kv.Key.x, j = kv.Key.y;
                char ch;
                int? n = FloorSet.Index(K, Mask8((di, dj) => under.TryGetValue(new Vector2Int(i + di, j + dj), out ch) && ch == 'i'));
                if (n == null) continue;
                q.Add(i, j, 0.004f, FloorSet.UV(K, n.Value));
            }
            if (q.Count == 0) return;
            Part.Add(stageRoot, q.ToMesh("IceSkin"), Mats.Textured(K.tex, Color.white, 0.18f, Vector2.one), Vector3.zero, false, "IceSkin");
        }

        // ================================================================ 川・海・マグマ
        // 届いた絵は岸（草・砂・岩場）ごと描いた止まった絵。**岸は絵のまま止めておき、水（マグマ）の所だけ全面の絵を流して描く**
        // （JS版 buildLiquidSkin / liqMaterial。描き方は Resources/Shaders/Liquid.shader）。
        //   まとめ画像の透明度が「岸からの距離」（0＝岸、16px 入ると 1）
        //   流れる向きは、川はそのマスを通る川の並びが長いほう。海はゆっくり漂うだけ、マグマはゆっくり流れて明るさが脈打つ
        //   盤面の外は同じ液体が続いている扱い。川は橋の下も川、板の道（=）はどの液体ともつながる扱い
        class LiqLook { public float speed, wob, foamK, alpha; public Color foam; }
        static readonly Dictionary<string, LiqLook> LIQ_LOOK = new Dictionary<string, LiqLook>
        {
            { "water", new LiqLook { speed = 0.22f, wob = 0.035f, foam = new Color(0.9f, 0.97f, 1.0f), foamK = 0.55f, alpha = 0.9f } },
            { "sea",   new LiqLook { speed = 0.05f, wob = 0.05f,  foam = new Color(0.95f, 0.98f, 1.0f), foamK = 0.7f, alpha = 0.92f } },
            { "lava",  new LiqLook { speed = 0.07f, wob = 0.03f,  foam = new Color(1.0f, 0.62f, 0.15f), foamK = 0.9f, alpha = 1.0f } },
        };
        static string LiqKind(char ch) { return ch == '~' || ch == 'b' ? "water" : ch == '@' ? "sea" : ch == '^' ? "lava" : null; }

        /// <summary>岸つきの絵が読めている液体か（読めていなければ今までの流れの絵で描く）。</summary>
        static bool HasLiqSkin(char ch) { var k = LiqKind(ch); return k != null && FloorSet.Liquids.ContainsKey(k); }

        static float liqTime;                                   // 流れの時計（JS版 LIQ_TIME）。毎フレーム進める
        static Shader liqShader;

        void BuildLiquidSkin()
        {
            var by = new Dictionary<string, CellQuads>();
            foreach (var kv in under)
            {
                char ch = kv.Value;
                var kind = LiqKind(ch);
                if (kind == null || ch == 'b') continue;                 // 橋の下は板で見えない
                FloorSet.Atlas K;
                if (!FloorSet.Liquids.TryGetValue(kind, out K)) continue;
                int i = kv.Key.x, j = kv.Key.y;
                System.Func<int, int, bool> same = (di, dj) =>
                {
                    char nb;
                    if (!under.TryGetValue(new Vector2Int(i + di, j + dj), out nb)) return true;
                    return nb == '=' || LiqKind(nb) == kind;
                };
                int? n = FloorSet.Index(K, Mask8(same));
                if (n == null) continue;
                CellQuads q;
                if (!by.TryGetValue(kind, out q)) by[kind] = q = new CellQuads();
                q.Add(i, j, Tiles.WaterTop, FloorSet.UV(K, n.Value), LiqFlow(i, j, kind));
            }
            foreach (var kv in by)
            {
                var go = Part.Add(stageRoot, kv.Value.ToMesh("Liquid_" + kv.Key), LiqMat(kv.Key), Vector3.zero, false, "Liquid_" + kv.Key);
                go.GetComponent<MeshRenderer>().receiveShadows = kv.Key != "lava";
            }
        }

        /// <summary>川の流れる向き（three.js の向き。並びの長いほう、同じなら横）。海とマグマは斜めにゆっくり。</summary>
        Vector2 LiqFlow(int i, int j, string kind)
        {
            var L = LIQ_LOOK[kind];
            if (kind != "water") return new Vector2(L.speed * 0.8f, L.speed * 0.6f);
            System.Func<int, int, int> run = (di, dj) =>
            {
                int n = 0; char ch;
                for (int k = 1; under.TryGetValue(new Vector2Int(i + di * k, j + dj * k), out ch) && LiqKind(ch) == kind; k++) n++;
                return n;
            };
            int h = run(1, 0) + run(-1, 0), v = run(0, 1) + run(0, -1);
            return h >= v ? new Vector2(L.speed, 0) : new Vector2(0, L.speed);
        }

        static readonly Dictionary<string, Material> liqMats = new Dictionary<string, Material>();
        static Material LiqMat(string kind)
        {
            Material m;
            if (liqMats.TryGetValue(kind, out m) && m != null) return m;
            if (liqShader == null) liqShader = Resources.Load<Shader>("Shaders/Liquid");
            if (liqShader == null) liqShader = Shader.Find("Hidden/Nekorobo/Liquid");
            var K = FloorSet.Liquids[kind]; var L = LIQ_LOOK[kind];
            m = new Material(liqShader) { name = "Liquid_" + kind };
            m.SetTexture("_MainTex", K.tex);
            m.SetTexture("_Surf", K.surf);
            m.SetFloat("_Wob", L.wob);
            m.SetColor("_Foam", L.foam * L.foamK);
            m.SetFloat("_Alpha", L.alpha);
            m.SetFloat("_Lava", kind == "lava" ? 1 : 0);
            bool lava = kind == "lava";
            m.SetFloat("_ZWrite", lava ? 1 : 0);
            m.SetFloat("_SrcBlend", (float)(lava ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(lava ? BlendMode.Zero : BlendMode.OneMinusSrcAlpha));
            m.renderQueue = lava ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
            liqMats[kind] = m;
            return m;
        }

        /// <summary>流れの時計を進める（JS版は毎フレーム LIQ_TIME に足す）。</summary>
        static void TickLiquid(float dt)
        {
            liqTime += dt;
            Shader.SetGlobalFloat("_NkLiqTime", liqTime);
        }

        // ================================================================ 穴の見た目の素材
        static Material pitWallMat, pitRimMat;
        /// <summary>穴の壁（上は土の茶色、下へ行くほど黒く）と、縁の注意の帯（黄と黒の斜めのしま）。JS版 pitMats。</summary>
        static void PitMats()
        {
            if (pitWallMat != null) return;
            // 壁：v=0 が上（縦 256px）
            var wt = new Texture2D(4, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var stops = new[] { new KeyValuePair<float, int>(0, 0x6a4a32), new KeyValuePair<float, int>(0.18f, 0x4a3426),
                                new KeyValuePair<float, int>(0.6f, 0x1c1612), new KeyValuePair<float, int>(1, 0x060505) };
            var px = new Color[4 * 256];
            for (int y = 0; y < 256; y++)
            {
                float t = (y + 0.5f) / 256f;                      // 上から
                int k = 0; while (k < stops.Length - 2 && t > stops[k + 1].Key) k++;
                float f = Mathf.InverseLerp(stops[k].Key, stops[k + 1].Key, t);
                var c = Color.Lerp(Mats.Hex(stops[k + 1 - 1].Value), Mats.Hex(stops[k + 1].Value), f);
                foreach (int s in new[] { 22, 40, 70 }) if (y >= s && y < s + 3) c *= 0.78f;   // 地層のすじ
                c.a = 1;
                for (int x = 0; x < 4; x++) px[(255 - y) * 4 + x] = c;   // 画像の上（v=1）が three.js の v=0 にあたるので上下を返す
            }
            wt.SetPixels(px); wt.Apply(false);
            pitWallMat = Mats.Textured(wt, Color.white, 1f, Vector2.one);
            pitWallMat.SetFloat("_Cull", (float)CullMode.Off);
            // 帯：横 128 × 縦 32。斜めのしまは64px おき
            var ht = new Texture2D(128, 32, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var hp = new Color[128 * 32];
            var yel = Mats.Hex(0xf2c230); var blk = Mats.Hex(0x2a2624);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 128; x++)
                {
                    // canvas の y（上から）で、(x, 32) (x+32, 32) (x+64, 0) (x+32, 0) の平行四辺形
                    float cy = 31 - y + 0.5f, cx = x + 0.5f;
                    float s = ((cx + cy) % 64 + 64) % 64;             // 斜めの位置
                    hp[y * 128 + x] = s < 32 ? blk : yel;
                }
            ht.SetPixels(hp); ht.Apply(true);
            pitRimMat = Mats.Textured(ht, Color.white, 0.7f, Vector2.one);
        }
    }
}
