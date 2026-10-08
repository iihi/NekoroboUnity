using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>置いた物1つ。i,j は占有範囲の左上マス、rot は度。</summary>
    public class StageObj
    {
        public string t, des, gk, v, gm;   // gm … 客のモデルの名前（エディタで選んだとき）
        public int i, j, rot, pri;
        public float? speed;
        public JObject raw;          // ルートなど、まだ使っていない項目もここに残っている

        public bool HasRoute
        {
            get
            {
                var r = raw != null ? raw["route"] as JObject : null;
                var pts = r != null ? r["pts"] as JArray : null;
                return pts != null && pts.Count >= 2;
            }
        }
    }

    /// <summary>
    /// ステージ1面ぶん。**HTML版の stages/*.json をそのまま読む。**
    /// 使っていない項目も raw に残してあるので、あとから足すときはここへ取り出す。
    /// </summary>
    public class StageCfg
    {
        public string file, n, desc;
        public Vector2Int origin;
        public List<string> terrain = new List<string>();
        public List<StageObj> objects = new List<StageObj>();
        public int? orders, ordersPer;
        public string floorDes, wallDes, menu, stallName;
        public float? wallHigh, camDist;
        public bool stall, camFix;
        public List<string> dishes;
        public JArray zones, hints;
        public JObject tune, raw;

        public static StageCfg Parse(JObject o, string file)
        {
            var c = new StageCfg { raw = o, file = file };
            c.n = J.S(o, "n", file);
            c.desc = J.S(o, "desc");
            var og = o["origin"] as JArray;
            c.origin = og != null && og.Count >= 2 ? new Vector2Int((int)og[0], (int)og[1]) : Vector2Int.zero;
            var tr = o["terrain"] as JArray;
            if (tr != null) foreach (var r in tr) c.terrain.Add((string)r ?? "");
            var objs = o["objects"] as JArray;
            if (objs != null)
                foreach (var t in objs)
                {
                    var jo = t as JObject; if (jo == null) continue;
                    c.objects.Add(new StageObj
                    {
                        raw = jo, t = J.S(jo, "t"), i = J.I(jo, "i", 0), j = J.I(jo, "j", 0),
                        rot = J.I(jo, "rot", 0), des = J.S(jo, "des"), gk = J.S(jo, "gk"), v = J.S(jo, "v"), gm = J.S(jo, "gm"),
                        pri = J.I(jo, "pri", 0), speed = J.FN(jo, "speed"),
                    });
                }
            var oc = J.FN(o, "orders"); if (oc != null) c.orders = Mathf.RoundToInt(oc.Value);
            var op = J.FN(o, "ordersPer"); if (op != null) c.ordersPer = Mathf.RoundToInt(op.Value);
            c.floorDes = J.S(o, "floorDes"); c.wallDes = J.S(o, "wallDes");
            c.menu = J.S(o, "menu"); c.stallName = J.S(o, "stallName");
            c.wallHigh = J.FN(o, "wallHigh"); c.camDist = J.FN(o, "camDist");
            c.stall = J.B(o, "stall"); c.camFix = J.B(o, "camFix");
            var ds = o["dishes"] as JArray;
            if (ds != null) { c.dishes = new List<string>(); foreach (var d in ds) c.dishes.Add((string)d); }
            c.zones = o["zones"] as JArray;
            c.hints = o["hints"] as JArray;
            c.tune = o["tune"] as JObject;
            return c;
        }

        /// <summary>stages/名前.json を読む。</summary>
        public static StageCfg Load(string name)
        {
            var o = DataRoot.ReadJson("stages/" + name + ".json");
            if (o == null) { Debug.LogError("[Nekorobo] ステージが読めません: " + name); return null; }
            return Parse(o, name);
        }

        // ---- 内蔵の面（HTML版 configs.js の CONFIGS）
        // configs.js はキーに引用符の無い JS の配列。Newtonsoft はこの書き方も読めるので、
        // `export const CONFIGS = [` から最後の `];` までを切り出してそのまま読む（JS の式は入っていない）。
        static JArray builtins;
        public static JArray Builtins()
        {
            if (builtins != null) return builtins;
            builtins = new JArray();
            try
            {
                var path = DataRoot.File_("configs.js");
                if (!File.Exists(path)) return builtins;
                var s = File.ReadAllText(path);
                int a = s.IndexOf("export const CONFIGS");
                if (a < 0) return builtins;
                a = s.IndexOf('[', a);
                int b = s.LastIndexOf("];");
                if (a < 0 || b < a) return builtins;
                builtins = JArray.Parse(s.Substring(a, b - a + 1));
            }
            catch (System.Exception e) { Debug.LogWarning("[Nekorobo] configs.js が読めません: " + e.Message); }
            return builtins;
        }

        /// <summary>内蔵の面を名前で引く（"通常フロア" など）。</summary>
        public static StageCfg Builtin(string name)
        {
            foreach (var t in Builtins())
            {
                var o = t as JObject;
                if (o != null && J.S(o, "n") == name) return Parse(o, null);
            }
            return null;
        }

        /// <summary>
        /// 並び（course.json / free.json）の cfg から引く。JS版 freeEntry と同じ書き方を受ける：
        /// "file:名前" はファイル、"builtin:名前" は内蔵、ただの名前はファイルがあればファイル、無ければ内蔵。
        /// </summary>
        public static StageCfg Ref(string v, bool quiet = false)
        {
            if (string.IsNullOrEmpty(v)) return null;
            if (v.StartsWith("file:")) return quiet ? LoadQuiet(v.Substring(5)) : Load(v.Substring(5));
            if (v.StartsWith("builtin:")) return Builtin(v.Substring(8));
            if (File.Exists(DataRoot.File_("stages/" + v + ".json"))) return quiet ? LoadQuiet(v) : Load(v);
            return Builtin(v);
        }

        public static StageCfg LoadQuiet(string name)
        {
            var o = DataRoot.ReadJson("stages/" + name + ".json");
            return o != null ? Parse(o, name) : null;
        }

        /// <summary>stages フォルダのステージ一覧（並びとコースのファイルは除く）。</summary>
        public static List<string> List()
        {
            var outL = new List<string>();
            var dir = DataRoot.File_("stages");
            if (!Directory.Exists(dir)) return outL;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                var nm = System.IO.Path.GetFileNameWithoutExtension(f);
                if (nm == "course" || nm == "free") continue;
                outL.Add(nm);
            }
            outL.Sort(System.StringComparer.Ordinal);
            return outL;
        }
    }

    /// <summary>コース（ストーリー／フリープレイの並び）の1面ぶん。</summary>
    public class CourseEntry
    {
        public string shop, cfg, title, desc;
        public int bonus;
        public bool noShop;
        public JObject raw;          // 会話（before / after / shopTalk）・ライバルなど

        /// <summary>"file:tut01" → "tut01"。内蔵の構成名（configs.js）はまだ読めないので null。</summary>
        public string FileName { get { return cfg != null && cfg.StartsWith("file:") ? cfg.Substring(5) : null; } }
    }

    public class Course
    {
        public string n;
        public List<CourseEntry> stages = new List<CourseEntry>();

        /// <summary>stages/course.json（ストーリー）や free.json（フリープレイ）を読む。</summary>
        public static Course Load(string name)
        {
            var o = DataRoot.ReadJson("stages/" + name + ".json");
            var c = new Course();
            if (o == null) return c;
            c.n = J.S(o, "n", name);
            var arr = o["stages"] as JArray;
            if (arr != null)
                foreach (var t in arr)
                {
                    var jo = t as JObject; if (jo == null) continue;
                    var cf = jo["cfg"];
                    c.stages.Add(new CourseEntry
                    {
                        raw = jo, shop = J.S(jo, "shop", "カフェ"),
                        cfg = cf != null && cf.Type == JTokenType.String ? (string)cf : null,
                        title = J.S(jo, "title"), desc = J.S(jo, "desc"),
                        bonus = J.I(jo, "bonus", 0), noShop = J.B(jo, "noShop"),
                    });
                }
            return c;
        }
    }
}
