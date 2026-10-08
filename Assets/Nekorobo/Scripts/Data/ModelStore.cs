using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>models.json の1項目（"guest" など）。src を配列で書くと人違い・色違いを置くたびに配る。</summary>
    public class ModelEntry
    {
        public string key;
        public List<GameObject> roots = new List<GameObject>();
        public List<string> names = new List<string>();     // ファイル名（拡張子なし）。エディタで「この客」と選ぶ名前
        public string scaleMode;                             // "fit" / "fitLen" / "fitIn"（数値のときは null）
        public float scaleNum = -1, maxScale = float.PositiveInfinity;
        public Vector3 rotDeg, offset;
        public bool keepY, skinned;
    }

    /// <summary>
    /// 外から届いたモデル（glb）。HTML版 loadAssets / applyModel を写したもの。
    /// **HTML版の assets/models.json と assets/ の glb をそのまま読む。**
    ///
    /// glb は Unity の glTF 読み込み（glTFast）で読む。glTFast は glTF を Unity へ写すとき **x を反転する**。
    /// このプロジェクトの決まり（three.js の z を反転、Coord.cs）とは y軸まわりに 180度ずれるので、
    /// 置くときに 180度回してそろえる（Place）。
    /// </summary>
    public static class ModelStore
    {
        public static readonly Dictionary<string, ModelEntry> Models = new Dictionary<string, ModelEntry>();
        public static readonly Dictionary<string, ModelEntry> DishModels = new Dictionary<string, ModelEntry>();
        public static readonly Dictionary<string, Dictionary<string, ModelEntry>> ShopModels =
            new Dictionary<string, Dictionary<string, ModelEntry>>();
        public static readonly List<string> Log = new List<string>();

        static readonly Dictionary<string, Task<GameObject>> glb = new Dictionary<string, Task<GameObject>>();
        static Transform cache;
        public static bool Loaded;

        // ---- 置くたびに配る順番（JS版 MODEL_SEQ / MODEL_SEED）
        static readonly Dictionary<string, int> seq = new Dictionary<string, int>();
        static int seed;

        /// <summary>面を組む前に呼ぶ。面の名前から配り方を決める（同じ面なら毎回同じ並び）。</summary>
        public static void ResetSeq(string stageName)
        {
            seq.Clear();
            // JS: [...name].reduce((a, c) => (a*31 + c.charCodeAt(0)) >>> 0, 7) % 997
            ulong a = 7;
            foreach (var ch in stageName ?? "") a = (a * 31 + ch) & 0xffffffffUL;
            seed = (int)(a % 997);
        }

        /// <summary>assets/ からの相対パスの glb を1つ読む（同じファイルは1回だけ）。読めなければ null。</summary>
        public static Task<GameObject> LoadGlb(string src)
        {
            Task<GameObject> t;
            if (glb.TryGetValue(src, out t)) return t;
            t = LoadGlbNow(src);
            glb[src] = t;
            return t;
        }

        static async Task<GameObject> LoadGlbNow(string src)
        {
            if (cache == null)
            {
                var c = new GameObject("ModelCache");
                c.SetActive(false);                      // 型紙なので画面には出さない
                Object.DontDestroyOnLoad(c);
                cache = c.transform;
            }
            var path = DataRoot.File_("assets/" + src);
            if (!System.IO.File.Exists(path)) { Log.Add("× 見つからない " + src); return null; }
            try
            {
                var imp = new GLTFast.GltfImport();      // 型紙が使うので、捨てずに持っておく
                if (!await imp.LoadFile(path, new System.Uri(path))) { Log.Add("× 読込失敗 " + src); return null; }
                var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(src));
                root.transform.SetParent(cache, false);
                if (!await imp.InstantiateMainSceneAsync(root.transform)) { Log.Add("× 読込失敗 " + src); return null; }
                return root;
            }
            catch (System.Exception e)
            {
                Log.Add("× 読込失敗 " + src + " " + e.Message);
                Debug.LogWarning("[Nekorobo] モデルが読めません: " + src + "\n" + e);
                return null;
            }
        }

        static ModelEntry Entry(string key, JToken cfg)
        {
            var o = cfg as JObject;
            if (o == null) return null;
            var src = o["src"];
            var list = new List<string>();
            if (src is JArray) foreach (var s in (JArray)src) { var v = (string)s; if (!string.IsNullOrEmpty(v)) list.Add(v); }
            else if (src != null && src.Type == JTokenType.String && (string)src != "") list.Add((string)src);
            if (list.Count == 0) return null;
            var M = new ModelEntry { key = key };
            var sc = o["scale"];
            if (sc != null && (sc.Type == JTokenType.Float || sc.Type == JTokenType.Integer)) M.scaleNum = sc.Value<float>();
            else M.scaleMode = sc != null ? (string)sc : "fit";
            var ms = J.FN(o, "maxScale"); if (ms != null) M.maxScale = ms.Value;
            M.rotDeg = V3(o["rotDeg"]); M.offset = V3(o["offset"]);
            M.keepY = J.B(o, "keepY"); M.skinned = J.B(o, "skinned");
            M.names = list;          // いったんファイル名を入れておき、読めた物だけ残す
            return M;
        }

        static Vector3 V3(JToken t)
        {
            var a = t as JArray;
            return a != null && a.Count >= 3 ? new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>()) : Vector3.zero;
        }

        static async Task Fill(ModelEntry M, string label)
        {
            var srcs = M.names;
            M.names = new List<string>();
            var tasks = new List<Task<GameObject>>();
            foreach (var s in srcs) tasks.Add(LoadGlb(s));
            await Task.WhenAll(tasks);
            for (int i = 0; i < srcs.Count; i++)
            {
                if (tasks[i].Result == null) continue;
                M.roots.Add(tasks[i].Result);
                M.names.Add(System.IO.Path.GetFileNameWithoutExtension(srcs[i]));
            }
            Log.Add(label + "：" + M.roots.Count + "/" + srcs.Count);
        }

        /// <summary>models.json を読んで、書いてあるモデルを全部読む（JS版 loadAssets と同じく、遊ぶ前に済ませる）。</summary>
        public static async Task LoadAll()
        {
            Models.Clear(); DishModels.Clear(); ShopModels.Clear(); Log.Clear();
            var man = DataRoot.ReadJson("assets/models.json");
            if (man == null) { Log.Add("models.json なし（内蔵の見た目）"); Loaded = true; return; }
            var jobs = new List<Task>();
            var models = man["models"] as JObject;
            if (models != null)
                foreach (var p in models.Properties())
                {
                    var M = Entry(p.Name, p.Value); if (M == null) continue;
                    Models[p.Name] = M; jobs.Add(Fill(M, p.Name));
                }
            var dishes = man["dishes"] as JObject;
            if (dishes != null)
                foreach (var p in dishes.Properties())
                {
                    var M = Entry(p.Name, p.Value); if (M == null) continue;
                    DishModels[p.Name] = M; jobs.Add(Fill(M, "料理:" + p.Name));
                }
            var shops = man["shops"] as JObject;
            if (shops != null)
                foreach (var sp in shops.Properties())
                {
                    var set = sp.Value as JObject; if (set == null) continue;
                    var d = new Dictionary<string, ModelEntry>();
                    foreach (var p in set.Properties())
                    {
                        var M = Entry(p.Name, p.Value); if (M == null) continue;
                        d[p.Name] = M; jobs.Add(Fill(M, sp.Name + "/" + p.Name));
                    }
                    if (d.Count > 0) ShopModels[sp.Name] = d;
                }
            await Task.WhenAll(jobs);
            Loaded = true;
        }

        /// <summary>その店ぶんの指定があればそちら（JS版 modelFor）。</summary>
        public static ModelEntry For(string kind, string shopName)
        {
            Dictionary<string, ModelEntry> sm;
            ModelEntry M;
            if (shopName != null && ShopModels.TryGetValue(shopName, out sm) && sm.TryGetValue(kind, out M) && M.roots.Count > 0) return M;
            return Models.TryGetValue(kind, out M) && M.roots.Count > 0 ? M : null;
        }

        /// <summary>配るモデルを1つ選ぶ（JS版 pickModelRoot）。名前を指定した物はそれ。</summary>
        static GameObject Pick(ModelEntry M, string key, string want)
        {
            if (!string.IsNullOrEmpty(want))
            {
                int i = M.names.IndexOf(want);
                if (i >= 0) return M.roots[i];
            }
            int n = M.roots.Count;
            if (n <= 1) return M.roots[0];
            int k; seq.TryGetValue(key, out k);
            seq[key] = k + 1;
            int step = n % 7 != 0 ? 7 : 5;           // 隣どうしがいつも同じ組み合わせにならないよう、飛ばしながら配る
            return M.roots[(k * step + seed) % n];
        }

        /// <summary>
        /// 手続きで組んだ見た目をモデルへ差し替える（JS版 applyModel）。
        /// look … 隠す手続きの見た目。size … 当たり判定の外寸。footY … 足元の高さ（null なら -size.y/2）。
        /// 戻り値は置いたモデル（無ければ null）。modelH に見た目の高さを返す。
        /// </summary>
        public static Transform Apply(Transform holder, GameObject look, string kind, string shopName, Vector3 size,
                                      out float modelH, string want = null, float? footY = null, Vector2? foot = null)
        {
            modelH = 0;
            var M = For(kind, shopName);
            if (M == null) return null;
            var tmpl = Pick(M, kind, want);
            if (look != null) look.SetActive(false);

            // 外の箱：rotDeg（three.js の向き）と縮尺。中：glTFast の向きを 180度回してそろえる
            var outer = new GameObject("Model_" + tmpl.name).transform;
            outer.SetParent(holder, false);
            outer.localRotation = Part.Euler3(M.rotDeg.x * Mathf.Deg2Rad, M.rotDeg.y * Mathf.Deg2Rad, M.rotDeg.z * Mathf.Deg2Rad);
            var inst = Object.Instantiate(tmpl, outer, false);
            inst.name = tmpl.name;
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.Euler(0, 180, 0);
            inst.transform.localScale = Vector3.one;
            inst.SetActive(true);

            // **回してから測る**（寝た向きで書き出されたモデルは rotDeg で起こすので）
            var b = LocalBounds(holder, outer);
            float h = b.size.y;
            float sc;
            if (M.scaleNum > 0) sc = M.scaleNum;
            else if (M.scaleMode == "fitIn")
            {
                float fw = (foot != null ? foot.Value.x : size.x) * 0.96f, fd = (foot != null ? foot.Value.y : size.z) * 0.96f;
                sc = Mathf.Min(M.maxScale, b.size.x > 1e-4f ? fw / b.size.x : 1, b.size.z > 1e-4f ? fd / b.size.z : 1);
            }
            else if (M.scaleMode == "fitLen" && size.x > 0 && size.z > 0)
            {
                float L = Mathf.Max(b.size.x, b.size.z);
                sc = L > 1e-4f ? Mathf.Max(size.x, size.z) / L : 1;
            }
            else sc = h > 1e-4f ? size.y / h : 1;
            outer.localScale = Vector3.one * sc;
            modelH = h * sc;
            // 足元を当たり判定の底に、横は中心へ寄せる。offset は three.js の向き（z を反転）
            float fy = footY ?? -size.y / 2;
            outer.localPosition = new Vector3(M.offset.x - b.center.x * sc,
                                              fy - (M.keepY ? 0 : b.min.y * sc) + M.offset.y,
                                              -M.offset.z - b.center.z * sc);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            return outer;
        }

        /// <summary>物の見た目の大きさを、holder から見た向きで測る（縮尺1・原点に置いた状態で）。</summary>
        static Bounds LocalBounds(Transform holder, Transform outer)
        {
            var oldPos = outer.localPosition; var oldScale = outer.localScale;
            outer.localPosition = Vector3.zero; outer.localScale = Vector3.one;
            bool any = false;
            var bb = new Bounds();
            var w2l = holder.worldToLocalMatrix;
            foreach (var r in outer.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var m = w2l * r.transform.localToWorldMatrix;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { bb = new Bounds(p, Vector3.zero); any = true; } else bb.Encapsulate(p);
                }
            }
            outer.localPosition = oldPos; outer.localScale = oldScale;
            return bb;
        }
    }
}
