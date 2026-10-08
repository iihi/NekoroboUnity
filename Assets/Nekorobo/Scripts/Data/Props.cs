using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>置き物1つぶん（カタログの1行）。JS版 props.js の PROPS の中身と同じ項目。</summary>
    public class PropDef
    {
        public string k, kind, kn, cat, look = "tower", n, v = "A", sz;
        public int w = 1, d = 1, vi;
        public float h = 2, drive, maxScale = float.PositiveInfinity;
        public bool solid = true, hide, keepY, skinned;
        public string col = "#cccccc", win, roof, sign;
        // 届いたモデル
        public List<string> model;
        public string scaleMode;            // "fitIn" など。数値は scaleNum
        public float scaleNum = -1;
        public Vector3 rotDeg, offset;
    }

    /// <summary>
    /// 置ける「大きい物」のカタログ（ビル・木・街の小物・乗り物）。JS版 props.js の表をそのまま写したもの。
    /// 表のキー（"crate_1x1_A" など）はステージの JSON が持つので、**一度出したキーは変えない**。
    /// 届いたモデルの品物は assets/catalog.json と models.json の "props" から足す（registerProps）。
    /// </summary>
    public static class Props
    {
        public static readonly Dictionary<string, PropDef> All = new Dictionary<string, PropDef>();
        static readonly Dictionary<string, string> Alias = new Dictionary<string, string>();
        static bool built;

        class Var { public string v, col, win, roof, sign; public Var(string v, string col, string win = null, string roof = null, string sign = null) { this.v = v; this.col = col; this.win = win; this.roof = roof; this.sign = sign; } }
        class Spec { public string kind, n, cat, look; public float drive; public bool solid = true; public float[][] sizes; public Var[] vars; }

        static Spec S(string kind, string n, string cat, string look, float[][] sizes, Var[] vars, float drive = 0, bool solid = true)
        { return new Spec { kind = kind, n = n, cat = cat, look = look, sizes = sizes, vars = vars, drive = drive, solid = solid }; }
        static float[] Z(float w, float d, float h) { return new[] { w, d, h }; }
        static Var V(string v, string col, string win = null, string roof = null, string sign = null) { return new Var(v, col, win, roof, sign); }

        // ---- JS版 SPEC と同じ並び・同じ値
        static readonly Spec[] SPEC =
        {
            S("bldg", "ビル", "建物", "tower", new[]{Z(4,4,9.0f),Z(4,3,7.5f),Z(3,3,6.5f),Z(3,2,6.0f),Z(2,2,4.5f)},
              new[]{V("A","#c9ccd4","#7fc7e8"),V("B","#b6c2d4","#ffe9a8"),V("C","#d8cfc2","#9fd8e8"),V("D","#c8d4c4","#8fd4ff")}),
            S("high", "高層ビル", "建物", "tower", new[]{Z(3,3,18.0f),Z(3,3,14.0f),Z(2,2,11.0f)},
              new[]{V("A","#dfe3e8","#8fd4ff"),V("B","#c2c8d0","#ffe0a0"),V("C","#aeb6c0","#bfe6f2")}),
            S("apart", "マンション", "建物", "apart", new[]{Z(4,3,10.0f),Z(3,3,8.0f),Z(3,2,7.0f),Z(2,2,6.0f)},
              new[]{V("A","#d8d0b8","#8fb8d8"),V("B","#d4907e","#8fb8d8"),V("C","#8fa8a4","#ffe9a8"),V("D","#e0b85a","#9fd8e8")}),
            S("house", "家", "建物", "house", new[]{Z(3,3,4.2f),Z(3,2,3.8f),Z(2,2,3.4f)},
              new[]{V("A","#e0d3bd",roof:"#8a4a3a"),V("B","#cfe0d2",roof:"#4a6a5a"),V("C","#e8d0c4",roof:"#6a5a8a"),V("D","#c8b89a",roof:"#3a4a5a")}),
            S("hotel", "ホテル", "建物", "apart", new[]{Z(4,4,11.0f),Z(4,3,9.0f)},
              new[]{V("A","#7fb87a","#ffe9a8"),V("B","#d8d2c4","#9fd8e8")}),
            S("park", "立体駐車場", "建物", "park", new[]{Z(4,4,7.0f),Z(4,3,6.0f)}, new[]{V("A","#c4c8cc"),V("B","#b4b8bc")}),
            S("round", "円形ビル", "建物", "round", new[]{Z(3,3,9.0f),Z(2,2,6.0f)}, new[]{V("A","#dfe3e8"),V("B","#c8ccd2")}),
            S("shop", "店（1階）", "商業", "shop", new[]{Z(3,2,3.6f),Z(2,2,3.2f),Z(2,1,3.0f)},
              new[]{V("A","#d98b45"),V("B","#4f8ad0"),V("C","#5fa855"),V("D","#c0483a")}),
            S("store", "商店（平屋）", "商業", "flat", new[]{Z(4,2,3.2f),Z(3,2,3.0f)},
              new[]{V("A","#4f8ad0",sign:"#e8e6e0"),V("B","#c0483a",sign:"#ffe9a8"),V("C","#5fa855",sign:"#ffffff")}),
            S("super", "スーパー", "商業", "flat", new[]{Z(5,4,4.0f),Z(4,3,3.8f)},
              new[]{V("A","#8fbf6a",sign:"#ffffff"),V("B","#c8b48a",sign:"#c0483a")}),
            S("gas", "ガソリンスタンド", "商業", "gas", new[]{Z(3,3,3.4f),Z(3,2,3.2f)}, new[]{V("A","#d94f4f"),V("B","#3f7fb8")}),
            S("resto", "飲食店", "商業", "shop", new[]{Z(3,2,3.4f),Z(2,2,3.0f)}, new[]{V("A","#e0a463"),V("B","#a8546a"),V("C","#5a8a8a")}),
            S("school", "学校", "公共", "civic", new[]{Z(5,3,5.0f),Z(4,3,4.6f)},
              new[]{V("A","#6fae82",roof:"#5a6068"),V("B","#c8b48a",roof:"#6a5a4a")}),
            S("hosp", "病院", "公共", "hosp", new[]{Z(4,3,7.0f),Z(3,3,6.0f)}, new[]{V("A","#f0f0ee"),V("B","#e0e8ee")}),
            S("police", "警察署", "公共", "civic", new[]{Z(3,2,4.2f)}, new[]{V("A","#3f6ab8",roof:"#3a4048")}),
            S("fire", "消防署", "公共", "fire", new[]{Z(4,2,4.4f)}, new[]{V("A","#d02f2f")}),
            S("hall", "市役所", "公共", "civic", new[]{Z(4,3,5.2f)}, new[]{V("A","#3f6ab8",roof:"#4a4a52"),V("B","#b03a3a",roof:"#4a4a52")}),
            S("bank", "銀行", "公共", "civic", new[]{Z(3,2,4.4f)}, new[]{V("A","#ddd6c0",roof:"#8a8478")}),
            S("church", "教会", "公共", "church", new[]{Z(3,3,7.5f),Z(2,2,6.0f)}, new[]{V("A","#f0f0ea",roof:"#3f5a8a"),V("B","#e8e0d0",roof:"#6a4a3a")}),
            S("dome", "寺院（ドーム）", "公共", "dome", new[]{Z(3,3,7.0f)}, new[]{V("A","#f0f0ea",roof:"#4a9a72")}),
            S("stat", "駅", "公共", "stat", new[]{Z(5,3,4.6f),Z(4,2,4.0f)}, new[]{V("A","#d6d2c8"),V("B","#c0c6cc")}),
            S("stadium", "スタジアム", "公共", "stadium", new[]{Z(8,6,7.0f),Z(6,5,6.0f)},
              new[]{V("A","#d02f2f",roof:"#e8c020"),V("B","#3f6ab8",roof:"#e8e6e0")}),
            S("fact", "工場", "工場", "fact", new[]{Z(5,3,5.5f),Z(4,3,5.0f),Z(3,3,4.6f)}, new[]{V("A","#b8bec6"),V("B","#c6b8a8"),V("C","#a8b4bc")}),
            S("ware", "倉庫", "工場", "ware", new[]{Z(5,3,4.6f),Z(4,3,4.2f),Z(3,2,3.8f)}, new[]{V("A","#c8ccd0"),V("B","#b0b8c0")}),
            S("chim", "煙突", "工場", "chim", new[]{Z(1,1,10.0f)}, new[]{V("A","#c8c4bc"),V("B","#c0483a")}),
            S("tank", "タンク", "工場", "tank", new[]{Z(2,2,4.5f),Z(1,1,3.0f)}, new[]{V("A","#d8d8d0"),V("B","#9fd0d8")}),
            S("gantry", "港のクレーン", "工場", "gantry", new[]{Z(3,2,9.0f)}, new[]{V("A","#e8b400"),V("B","#3f7fb8")}),
            S("crane", "クレーン", "工場", "crane", new[]{Z(2,2,12.0f),Z(2,2,9.5f)}, new[]{V("A","#e8b400"),V("B","#d02f2f")}),
            S("cont", "コンテナ", "工場", "cont", new[]{Z(2,1,2.4f),Z(1,1,1.4f)},
              new[]{V("A","#c0483a"),V("B","#3f7fb8"),V("C","#d8a83a"),V("D","#4a9a72")}),
            S("crate", "資材", "工場", "crate", new[]{Z(1,1,1.2f)}, new[]{V("A","#b08a5a"),V("B","#8b93a0")}),
            S("scaf", "足場", "工場", "scaf", new[]{Z(2,2,5.0f)}, new[]{V("A","#c8c0a8")}),
            S("barn", "納屋", "農場", "barn", new[]{Z(3,3,4.6f),Z(2,2,3.6f)}, new[]{V("A","#c0483a"),V("B","#8a6a4a")}),
            S("farm", "農家", "農場", "house", new[]{Z(3,2,3.8f)}, new[]{V("A","#e8b46a",roof:"#6a4a3a"),V("B","#d8c8a8",roof:"#4a5a4a")}),
            S("silo", "サイロ", "農場", "silo", new[]{Z(2,2,6.0f),Z(1,1,4.0f)}, new[]{V("A","#d8d8d0"),V("B","#bfc4b8")}),
            S("green", "温室", "農場", "green", new[]{Z(3,2,2.6f),Z(2,2,2.4f)}, new[]{V("A","#bfe6f2")}),
            S("field", "畑", "農場", "field", new[]{Z(4,4,0.2f),Z(3,3,0.2f),Z(2,2,0.2f)}, new[]{V("A","#c8a86a"),V("B","#8fbf5a"),V("C","#d8c84a")}, 0, false),
            S("hay", "干し草", "農場", "hay", new[]{Z(1,1,1.1f)}, new[]{V("A","#d8b85a")}),
            S("wind", "風車", "農場", "wind", new[]{Z(2,2,12.0f)}, new[]{V("A","#f0f0ee")}),
            S("tree", "広葉樹", "自然", "tree", new[]{Z(2,2,6.5f),Z(1,1,4.0f)},
              new[]{V("A","#5fa855"),V("B","#4f9a48"),V("C","#7ab84f"),V("D","#c8823a")}),
            S("pine", "針葉樹", "自然", "pine", new[]{Z(2,2,7.0f),Z(1,1,4.6f)}, new[]{V("A","#3f8a55"),V("B","#356f4a")}),
            S("bush", "茂み", "自然", "bush", new[]{Z(1,1,0.8f)}, new[]{V("A","#6fbf5a"),V("B","#8ac96a")}, 0, false),
            S("stump", "切り株", "自然", "stump", new[]{Z(1,1,0.5f)}, new[]{V("A","#8a6a44")}, 0, false),
            S("rock", "岩", "自然", "rock", new[]{Z(2,1,1.4f),Z(1,1,0.9f)}, new[]{V("A","#9aa2ad"),V("B","#a89a86")}),
            S("flower", "花壇", "自然", "pot", new[]{Z(1,1,0.6f)}, new[]{V("A","#d8607a"),V("B","#e8b400")}, 0, false),
            S("lamp", "街灯", "街", "lamp", new[]{Z(1,1,3.2f)}, new[]{V("A","#8b93a0"),V("B","#5f6672")}),
            S("signal", "信号", "街", "signal", new[]{Z(1,1,3.0f)}, new[]{V("A","#4a4a52")}),
            S("sign", "看板", "街", "sign", new[]{Z(1,1,2.6f)}, new[]{V("A","#e8b400"),V("B","#d94f4f")}),
            S("board", "広告板", "街", "board", new[]{Z(2,1,4.0f)}, new[]{V("A","#e8e6e0"),V("B","#3f7fb8")}),
            S("bench", "ベンチ", "街", "bench", new[]{Z(2,1,0.8f)}, new[]{V("A","#a5763f"),V("B","#6b7f8a")}),
            S("stop", "バス停", "街", "stop", new[]{Z(2,1,2.8f)}, new[]{V("A","#4f8ad0"),V("B","#8b93a0")}),
            S("fence", "柵", "街", "fence", new[]{Z(1,1,1.0f)}, new[]{V("A","#cfd6de"),V("B","#8a6a4a")}),
            S("pot", "植え込み", "街", "pot", new[]{Z(1,1,1.0f)}, new[]{V("A","#c9a27a"),V("B","#9aa2ad")}, 0, false),
            S("bin", "ゴミ箱", "街", "bin", new[]{Z(1,1,0.9f)}, new[]{V("A","#5a7a5a"),V("B","#6a6a72")}, 0, false),
            S("phone", "電話ボックス", "街", "phone", new[]{Z(1,1,2.2f)}, new[]{V("A","#c0483a"),V("B","#3f7fb8")}),
            S("hydrant", "消火栓", "街", "hydrant", new[]{Z(1,1,0.8f)}, new[]{V("A","#d02f2f")}, 0, false),
            S("balloon", "気球", "街", "balloon", new[]{Z(2,2,7.0f)}, new[]{V("A","#d94f4f"),V("B","#3f9fd0"),V("C","#e8b400")}, 0, false),
            S("car", "乗用車", "乗り物", "car", new[]{Z(2,1,1.5f)}, new[]{V("A","#d94f4f"),V("B","#3f7fb8"),V("C","#e8e6e0"),V("D","#e8b400")}, 8),
            S("wagon", "ワゴン", "乗り物", "wagon", new[]{Z(2,1,1.9f)}, new[]{V("A","#7a5aa8"),V("B","#5a8a6a")}, 7),
            S("truck", "トラック", "乗り物", "truck", new[]{Z(3,1,2.6f),Z(2,1,2.2f)}, new[]{V("A","#4f6ad0"),V("B","#c0483a"),V("C","#e8e6e0")}, 6),
            S("bus", "バス", "乗り物", "bus", new[]{Z(3,1,2.6f)}, new[]{V("A","#3f9fd0"),V("B","#d02f2f")}, 6),
            S("dump", "ダンプ", "乗り物", "dump", new[]{Z(2,1,2.4f)}, new[]{V("A","#e8b400")}, 5),
            S("tanker", "タンクローリー", "乗り物", "tanker", new[]{Z(3,1,2.4f)}, new[]{V("A","#d8d8d0"),V("B","#c0483a")}, 5),
            S("tractor", "トラクター", "乗り物", "tractor", new[]{Z(2,1,2.2f)}, new[]{V("A","#e8b400"),V("B","#4a9a4a")}, 3),
            S("train", "電車", "乗り物", "train", new[]{Z(4,1,3.0f),Z(3,1,2.8f)}, new[]{V("A","#3f7fb8"),V("B","#4a9a72"),V("C","#d02f2f")}, 13),
            S("ship", "船", "乗り物", "ship", new[]{Z(5,2,3.4f),Z(4,2,3.0f)}, new[]{V("A","#c8ccd0"),V("B","#3f6ab8")}, 3),
        };

        static readonly Dictionary<string, string> OLD = new Dictionary<string, string>
        {
            {"bldg44","bldg_4x4_A"},{"bldg43","bldg_4x3_A"},{"bldg33","bldg_3x3_A"},{"bldg22","bldg_2x2_A"},
            {"house33","house_3x3_A"},{"house22","house_2x2_A"},{"shop32","shop_3x2_A"},
            {"barn33","barn_3x3_A"},{"silo22","silo_2x2_A"},
            {"tree11","tree_1x1_A"},{"tree22","tree_2x2_A"},{"pine11","pine_1x1_A"},
            {"bush11","bush_1x1_A"},{"rock11","rock_1x1_A"},{"rock21","rock_2x1_A"},
            {"lamp11","lamp_1x1_A"},{"sign11","sign_1x1_A"},{"bench21","bench_2x1_A"},
            {"stop21","stop_2x1_A"},{"fence11","fence_1x1_A"},{"planter11","pot_1x1_A"},
            {"crane22","crane_2x2_A"},{"cont21","cont_2x1_A"},{"stack11","crate_1x1_A"},
            {"truck21","truck_2x1_A"},
        };

        static string Num(float h) { return h.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }

        static void Build()
        {
            if (built) return;
            built = true;
            var dupAlias = new Dictionary<string, string>();
            foreach (var sp in SPEC)
            {
                var times = new Dictionary<string, int>();
                foreach (var s in sp.sizes) { var wd0 = (int)s[0] + "x" + (int)s[1]; int c; times.TryGetValue(wd0, out c); times[wd0] = c + 1; }
                for (int si = 0; si < sp.sizes.Length; si++)
                {
                    var s = sp.sizes[si];
                    int w = (int)s[0], d = (int)s[1]; float h = s[2];
                    string wd = w + "x" + d;
                    bool dup = times[wd] > 1;
                    string hTag = dup ? "h" + Num(h).Replace(".", "") : "";
                    bool last = dup;
                    for (int sj = si + 1; sj < sp.sizes.Length && last; sj++)
                        if ((int)sp.sizes[sj][0] + "x" + (int)sp.sizes[sj][1] == wd) last = false;
                    for (int vi = 0; vi < sp.vars.Length; vi++)
                    {
                        var va = sp.vars[vi];
                        string key = sp.kind + "_" + wd + (hTag.Length > 0 ? "_" + hTag : "") + "_" + va.v;
                        if (last) dupAlias[sp.kind + "_" + wd + "_" + va.v] = key;
                        All[key] = new PropDef
                        {
                            k = key, kind = sp.kind, kn = sp.n, cat = sp.cat, look = sp.look, w = w, d = d, h = h,
                            v = va.v, vi = vi, drive = sp.drive, solid = sp.solid, col = va.col, win = va.win,
                            roof = va.roof, sign = va.sign, n = sp.n + " " + w + "×" + d + " " + va.v,
                        };
                    }
                }
            }
            foreach (var kv in OLD) Alias[kv.Key] = kv.Value;
            foreach (var kv in dupAlias) Alias[kv.Key] = kv.Value;
            FlattenAlias();
        }

        static void FlattenAlias()
        {
            foreach (var k in new List<string>(Alias.Keys))
            {
                string t = Alias[k];
                for (int i = 0; i < 8 && !All.ContainsKey(t) && Alias.ContainsKey(t); i++) t = Alias[t];
                Alias[k] = t;
            }
        }

        /// <summary>
        /// 外から足す・差し替える（assets/catalog.json の props と、models.json の "props"）。
        /// 既にあるキーなら上書き、新しいキーなら追加（JS版 registerProps）。
        /// </summary>
        public static int Register(JObject list)
        {
            Build();
            if (list == null) return 0;
            int n = 0;
            foreach (var p in list.Properties())
            {
                if (p.Name.StartsWith("_")) continue;
                var o = p.Value as JObject; if (o == null) continue;
                PropDef P;
                if (!All.TryGetValue(p.Name, out P)) { P = new PropDef { k = p.Name, kind = "etc", kn = "その他", cat = "建物", sz = "1×1", n = p.Name }; All[p.Name] = P; }
                if (o["n"] != null) P.n = (string)o["n"];
                if (o["kind"] != null) P.kind = (string)o["kind"];
                if (o["kn"] != null) P.kn = (string)o["kn"];
                if (o["cat"] != null) P.cat = (string)o["cat"];
                if (o["look"] != null) P.look = (string)o["look"];
                if (o["col"] != null) P.col = (string)o["col"];
                if (o["win"] != null) P.win = (string)o["win"];
                if (o["roof"] != null) P.roof = (string)o["roof"];
                if (o["sign"] != null) P.sign = (string)o["sign"];
                if (o["v"] != null) P.v = (string)o["v"];
                P.w = J.I(o, "w", P.w); P.d = J.I(o, "d", P.d); P.h = J.F(o, "h", P.h);
                P.vi = J.I(o, "vi", P.vi); P.drive = J.F(o, "drive", P.drive);
                if (o["solid"] != null) P.solid = J.B(o, "solid", true);
                if (o["hide"] != null) P.hide = J.B(o, "hide");
                var m = o["model"];
                if (m is JArray) { P.model = new List<string>(); foreach (var s in (JArray)m) P.model.Add((string)s); }
                else if (m != null && m.Type == JTokenType.String && (string)m != "") P.model = new List<string> { (string)m };
                var sc = o["scale"];
                if (sc != null && (sc.Type == JTokenType.Float || sc.Type == JTokenType.Integer)) { P.scaleNum = sc.Value<float>(); P.scaleMode = null; }
                else if (sc != null) P.scaleMode = (string)sc;
                var ms = J.FN(o, "maxScale"); if (ms != null) P.maxScale = ms.Value;
                if (o["rotDeg"] is JArray) { var a = (JArray)o["rotDeg"]; P.rotDeg = new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>()); }
                if (o["offset"] is JArray) { var a = (JArray)o["offset"]; P.offset = new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>()); }
                P.keepY = J.B(o, "keepY", P.keepY); P.skinned = J.B(o, "skinned", P.skinned);
                n++;
            }
            return n;
        }

        /// <summary>起動時に1回。カタログ（catalog.json）と models.json の props を取り込む。</summary>
        public static void LoadCatalog()
        {
            Build();
            var cat = DataRoot.ReadJson("assets/catalog.json");
            if (cat != null) Register(cat["props"] as JObject);
            var man = DataRoot.ReadJson("assets/models.json");
            if (man != null) Register(man["props"] as JObject);
        }

        /// <summary>古いキーを今のキーへ（JS版 resolveProp）。</summary>
        public static string Resolve(string t)
        {
            Build();
            if (t == null) return null;
            if (All.ContainsKey(t)) return t;
            string a;
            return Alias.TryGetValue(t, out a) ? a : t;
        }

        public static PropDef Get(string t)
        {
            Build();
            PropDef p;
            return t != null && All.TryGetValue(Resolve(t), out p) ? p : null;
        }

        /// <summary>大きさ（マス）。tiles.js の OBJ_EXTRA の代わり。</summary>
        public static bool Size(string t, out Vector2Int s)
        {
            var p = Get(t);
            s = p != null ? new Vector2Int(p.w, p.d) : new Vector2Int(1, 1);
            return p != null;
        }

        /// <summary>当たり判定の半分の大きさ（見た目より少し小さく）。JS版 propHalf。</summary>
        public static Vector3 Half(PropDef p, float W, float D) { return new Vector3(W * 0.44f, p.h / 2, D * 0.44f); }
    }
}
