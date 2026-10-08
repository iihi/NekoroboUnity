using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>地形チップ1種類ぶん。JS版 tiles.js の TT と同じ中身。</summary>
    public class TileDef
    {
        public string k, n;
        public bool floor, wall, high, hidden, liquid, burn, deadly, slippery;
        public string conn;
        public int raft;
    }

    /// <summary>
    /// マップチップの決まりごと。**JS版 tiles.js を写したもの。**
    /// 文字を増やしたり意味を変えたときは、両方を直す。
    /// </summary>
    public static class Tiles
    {
        public const float PondDepth = 0.45f, SeaDepth = 2.40f;
        public const float RampThick = 0.24f;
        public const float WaterTop = -0.20f;     // 水面の高さ（床の面から下）
        public const float FloorH = 0.9f;         // 床の板の厚みの半分

        public static readonly Dictionary<char, TileDef> TT = new Dictionary<char, TileDef>
        {
            { '.', new TileDef { k = "floor",  n = "床",         floor = true } },
            { '#', new TileDef { k = "wall",   n = "壁",         wall = true } },
            { 'H', new TileDef { k = "wallHi", n = "高い壁",     wall = true, high = true } },
            { '+', new TileDef { k = "hwall",  n = "見えない壁", wall = true, hidden = true } },
            { '=', new TileDef { k = "plank",  n = "板の道",     floor = true } },
            { 'r', new TileDef { k = "road",   n = "道路",       floor = true, conn = "road" } },
            { '~', new TileDef { k = "water",  n = "水（池）",   liquid = true } },
            { '^', new TileDef { k = "lava",   n = "溶岩",       liquid = true, burn = true } },
            { '@', new TileDef { k = "sea",    n = "海",         liquid = true, deadly = true } },
            { 'o', new TileDef { k = "hole",   n = "穴" } },
            { 'i', new TileDef { k = "ice",    n = "凍りの床",   floor = true, slippery = true } },
            { 'l', new TileDef { k = "rail",   n = "線路",       floor = true, conn = "rail" } },
            { 'b', new TileDef { k = "bridge", n = "橋（川つき）", floor = true, conn = "bridge" } },
            { '1', new TileDef { k = "raft",   n = "いかだ 1",   floor = true, raft = 1 } },
            { '2', new TileDef { k = "raft",   n = "いかだ 2",   floor = true, raft = 2 } },
            { '3', new TileDef { k = "raft",   n = "いかだ 3",   floor = true, raft = 3 } },
            { '4', new TileDef { k = "raft",   n = "いかだ 4",   floor = true, raft = 4 } },
        };

        public static TileDef Def(char ch) { TileDef t; return TT.TryGetValue(ch, out t) ? t : null; }
        public static bool IsFloor(char ch) { var t = Def(ch); return t != null && t.floor; }
        public static bool IsWall(char ch) { var t = Def(ch); return t != null && t.wall; }
        public static bool IsLiquid(char ch) { var t = Def(ch); return t != null && t.liquid; }
        public static bool IsSlippery(char ch) { var t = Def(ch); return t != null && t.slippery; }
        public static float DepthOf(char ch) { return ch == '@' ? SeaDepth : PondDepth; }

        // ---- 置ける物の大きさ（rot=0 のときの占有マス数）
        static readonly Dictionary<string, Vector2Int> OBJ = new Dictionary<string, Vector2Int>
        {
            { "spawn", new Vector2Int(1, 1) }, { "counter", new Vector2Int(1, 3) },
            { "table", new Vector2Int(1, 1) }, { "table2", new Vector2Int(2, 1) },
            { "chair", new Vector2Int(1, 1) }, { "bench", new Vector2Int(2, 1) },
            { "guest", new Vector2Int(1, 1) }, { "car", new Vector2Int(2, 1) },
            { "mover", new Vector2Int(3, 1) }, { "ramp", new Vector2Int(2, 1) },
            { "raft", new Vector2Int(1, 1) }, { "deco", new Vector2Int(1, 1) },
        };
        public static Vector2Int ObjSize(string t)
        {
            Vector2Int s;
            if (t != null && OBJ.TryGetValue(t, out s)) return s;
            if (t != null && Props.Size(t, out s)) return s;
            return new Vector2Int(1, 1);
        }

        /// <summary>置いた物の占有範囲（回転済み）と中心（Unity 座標）。JS版 objBox。</summary>
        public static void ObjBox(StageObj o, out float w, out float d, out Vector3 center)
        {
            var s = ObjSize(o.t);
            int rot = ((o.rot % 180) + 180) % 180;
            bool swap = rot == 90;
            w = swap ? s.y : s.x;
            d = swap ? s.x : s.y;
            center = Coord.W((o.i + w / 2f) * Coord.Tile, 0f, (o.j + d / 2f) * Coord.Tile);
        }

        // ---- 地形の読み書き
        /// <summary>文字列の並び → (i,j) → 文字。空白と未知の文字は置かない。</summary>
        public static Dictionary<Vector2Int, char> ParseTerrain(StageCfg c)
        {
            var map = new Dictionary<Vector2Int, char>();
            for (int k = 0; k < c.terrain.Count; k++)
            {
                string row = c.terrain[k];
                for (int x = 0; x < row.Length; x++)
                {
                    char ch = row[x];
                    if (ch == ' ' || !TT.ContainsKey(ch)) continue;
                    map[new Vector2Int(c.origin.x + x, c.origin.y + k)] = ch;
                }
            }
            return map;
        }

        public struct Rect { public int i0, i1, j0, j1; }

        /// <summary>
        /// 同じ種類の隣り合うマスを矩形にまとめる（貪欲法：右へ伸ばし、次に下へ）。JS版 mergeCells と同じ順。
        /// </summary>
        public static List<Rect> MergeCells(HashSet<Vector2Int> cells)
        {
            var left = new HashSet<Vector2Int>(cells);
            var ks = new List<Vector2Int>(cells);
            ks.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var outR = new List<Rect>();
            foreach (var p in ks)
            {
                if (!left.Contains(p)) continue;
                int i1 = p.x; while (left.Contains(new Vector2Int(i1 + 1, p.y))) i1++;
                int j1 = p.y;
                for (; ; )
                {
                    bool ok = true;
                    for (int x = p.x; x <= i1; x++) if (!left.Contains(new Vector2Int(x, j1 + 1))) { ok = false; break; }
                    if (!ok) break;
                    j1++;
                }
                for (int x = p.x; x <= i1; x++) for (int y = p.y; y <= j1; y++) left.Remove(new Vector2Int(x, y));
                outR.Add(new Rect { i0 = p.x, i1 = i1, j0 = p.y, j1 = j1 });
            }
            return outR;
        }

        /// <summary>矩形（マス）→ 中心（Unity）と幅・奥行き[m]。</summary>
        public static void RectWorld(Rect r, out Vector3 c, out float w, out float d)
        {
            c = Coord.W((r.i0 + r.i1 + 1) / 2f * Coord.Tile, 0f, (r.j0 + r.j1 + 1) / 2f * Coord.Tile);
            w = (r.i1 - r.i0 + 1) * Coord.Tile;
            d = (r.j1 - r.j0 + 1) * Coord.Tile;
        }

        public static char At(Dictionary<Vector2Int, char> map, Vector3 p)
        {
            char ch;
            return map.TryGetValue(new Vector2Int(Coord.ToI(p.x), Coord.ToJ(p.z)), out ch) ? ch : ' ';
        }

        /// <summary>いちばん近い「走れるマス」の中心。渦を巻くように外側へ探す。</summary>
        public static Vector3? NearestFloor(Dictionary<Vector2Int, char> map, Vector3 p, int maxR = 24)
        {
            int i0 = Coord.ToI(p.x), j0 = Coord.ToJ(p.z);
            char ch;
            if (map.TryGetValue(new Vector2Int(i0, j0), out ch) && IsFloor(ch)) return Coord.Cell(i0, j0);
            for (int r = 1; r <= maxR; r++)
            {
                Vector3? best = null; float bd = float.MaxValue;
                for (int di = -r; di <= r; di++)
                    for (int dj = -r; dj <= r; dj++)
                    {
                        if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != r) continue;
                        if (!map.TryGetValue(new Vector2Int(i0 + di, j0 + dj), out ch) || !IsFloor(ch)) continue;
                        var c = Coord.Cell(i0 + di, j0 + dj);
                        float dd = (c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z);
                        if (dd < bd) { bd = dd; best = c; }
                    }
                if (best != null) return best;
            }
            return null;
        }
    }

    // ------------------------------------------------------------------ デザイン（見た目）
    public class Design
    {
        public string n, src;
        public int col = 0x9aa4b2, legCol = 0x5a6472;
        public float tile = 2.0f;
        public readonly Dictionary<string, string> parts = new Dictionary<string, string>();
    }

    /// <summary>
    /// 床・壁・家具の見た目の組。JS版 tiles.js の DESIGNS と、
    /// assets/models.json の designs（あとから足した組）を合わせたもの。
    /// </summary>
    public static class Designs
    {
        public static readonly Dictionary<string, Design> All = new Dictionary<string, Design>();

        static void Def(string k, string n, int col, int legCol, string src, float tile = 2.0f)
        {
            All[k] = new Design { n = n, col = col, legCol = legCol, src = src, tile = tile };
        }

        static Designs()
        {
            Def("wood", "木目", 0xc98f52, 0x8a5730, "tex/wood.png");
            Def("woodDk", "濃い木", 0x7a4d2c, 0x3f2a1a, "tex/wood_dark.png");
            Def("white", "白", 0xefe9e0, 0xb8b2a8, "tex/paint_white.png");
            Def("marble", "大理石", 0xe8e6e0, 0x9aa0a6, "tex/marble.png");
            Def("lacquer", "朱塗り", 0xa8322c, 0x5c2419, "tex/lacquer_red.png");
            Def("metal", "メタル", 0xb9c0c8, 0x7a828c, "tex/metal.png");
            Def("tatami", "畳", 0xc7bf95, 0x6b6144, "tex/tatami.png", 1.0f);
            Def("cloth", "布張り", 0x6ea3cf, 0x4a6b8a, "tex/cloth_blue.png");
            Def("checker", "チェック", 0xd8534f, 0x7a2f2c, "tex/checker.png");
            Def("tileW", "白タイル", 0xe4e7ea, 0xa9b0b6, "tex/tile_white.png", 1.0f);
            Def("brick", "レンガ", 0x9d5a44, 0x6a3a2a, "tex/brick.png", 2.0f);
            Def("concrete", "コンクリ", 0xa8a8a4, 0x7b7b78, "tex/concrete.png", 3.0f);
            Def("carpet", "カーペット", 0x7a4b52, 0x4e3035, "tex/carpet.png", 2.0f);
        }

        /// <summary>models.json の designs を取り込む（JS版 registerDesigns と同じ）。</summary>
        public static void Register(JObject designs)
        {
            if (designs == null) return;
            foreach (var p in designs.Properties())
            {
                if (p.Name.StartsWith("_") || p.Value == null || p.Value.Type == JTokenType.Null) continue;
                if (p.Value.Type == JTokenType.String && (string)p.Value == "") continue;
                Design D;
                if (!All.TryGetValue(p.Name, out D)) { D = new Design { n = p.Name }; All[p.Name] = D; }
                if (p.Value.Type == JTokenType.String) { D.src = (string)p.Value; continue; }
                var o = p.Value as JObject; if (o == null) continue;
                if (o["n"] != null) D.n = (string)o["n"];
                if (o["col"] != null) D.col = HexInt((string)o["col"], D.col);
                if (o["legCol"] != null) D.legCol = HexInt((string)o["legCol"], D.legCol);
                if (o["tile"] != null) D.tile = o["tile"].Value<float>();
                if (o["src"] != null) D.src = (string)o["src"];
                foreach (var pt in new[] { "table", "table2", "chair", "counter", "floor", "wall", "leg" })
                    if (o[pt] != null) D.parts[pt] = (string)o[pt];
            }
        }

        static int HexInt(string css, int def)
        {
            if (string.IsNullOrEmpty(css)) return def;
            css = css.TrimStart('#');
            int v; return int.TryParse(css, System.Globalization.NumberStyles.HexNumber, null, out v) ? v : def;
        }

        public static Design Get(string k) { Design d; return k != null && All.TryGetValue(k, out d) ? d : null; }

        /// <summary>その部位の画像（部位 → 種類 → まとめて1枚 の順）。JS版 desTex。</summary>
        public static Texture2D Tex(string des, string kind, string part)
        {
            var D = Get(des); if (D == null) return null;
            string s;
            if (part != null && D.parts.TryGetValue(part, out s) && !string.IsNullOrEmpty(s)) return DataRoot.Texture(s);
            if (kind != null && D.parts.TryGetValue(kind, out s) && !string.IsNullOrEmpty(s)) return DataRoot.Texture(s);
            if (part == "leg") return null;          // 脚は脚用の画像があるときだけ貼る
            return DataRoot.Texture(D.src);
        }

        static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

        /// <summary>家具の素材（JS版 desMat）。デザインが無ければ base。</summary>
        public static Material Mat(string des, Material baseMat, string kind, string part, float rep = 1f)
        {
            var D = Get(des);
            if (D == null) return baseMat;
            string key = des + "|" + kind + "|" + (part ?? "top") + "|" + rep;
            Material m;
            if (matCache.TryGetValue(key, out m) && m != null) return m;
            var t = part == "leg" ? Tex(des, kind, "leg") : Tex(des, kind, part);
            float rough = 1f - (baseMat.HasProperty("_Smoothness") ? baseMat.GetFloat("_Smoothness") : 0.05f);
            m = t != null ? Mats.Textured(t, Color.white, rough, new Vector2(rep, rep))
                          : Mats.NewLit(Mats.Hex(part == "leg" ? D.legCol : D.col), rough);
            matCache[key] = m;
            return m;
        }

        /// <summary>床・壁のような大きな面（JS版 desSurf）。tile[m] ごとに敷き詰める。</summary>
        public static Material Surf(string des, Material baseMat, string kind, float w, float d)
        {
            var D = Get(des);
            if (D == null) return baseMat;
            var t = Tex(des, kind, kind);
            if (t == null) return Mats.NewLit(Mats.Hex(D.col), 0.9f);
            float tl = D.tile > 0 ? D.tile : 2f;
            return Mats.Textured(t, Color.white, 0.9f, new Vector2(Mathf.Max(1f, w / tl), Mathf.Max(1f, d / tl)));
        }
    }

    // ------------------------------------------------------------------ 客の種類
    public class GuestKind
    {
        public string k, n, hair = "short", wear = "pants", hat, bag;
        public float h = 1, w = 1, heads = 0;
        public bool tie, glass, beard, cap;
        public int[] pal;
    }

    public static class GuestKinds
    {
        public static readonly List<GuestKind> All = new List<GuestKind>
        {
            new GuestKind { k = "salary",  n = "サラリーマン", h = 1.00f, w = 1.00f, hair = "short", wear = "pants", tie = true, pal = new[] { 0x3a4a6a, 0x2b3550, 0x4a4a52, 0x5a6a80 } },
            new GuestKind { k = "office",  n = "OL",           h = 0.97f, w = 0.94f, hair = "bun",   wear = "skirt", pal = new[] { 0x6a5a7a, 0x8a6a7a, 0x4a5a6a, 0xa08090 } },
            new GuestKind { k = "casual",  n = "若い男",       h = 1.00f, w = 1.00f, hair = "short", wear = "pants", pal = new[] { 0x4a9a72, 0xd06a4a, 0x3f8ac0, 0xe0b040 } },
            new GuestKind { k = "girl",    n = "若い女",       h = 0.95f, w = 0.92f, hair = "long",  wear = "dress", pal = new[] { 0xe08aa0, 0xf0c060, 0x8ac0e0, 0xc0a0e0 } },
            new GuestKind { k = "student", n = "学生",         h = 0.94f, w = 0.94f, hair = "short", wear = "pants", bag = "back", pal = new[] { 0x2b3550, 0x3a4a6a, 0xe8e6e0 } },
            new GuestKind { k = "child",   n = "子ども",       h = 0.68f, w = 0.90f, heads = -1.4f, hair = "short", wear = "pants", cap = true, pal = new[] { 0xf0a040, 0x60c080, 0x6090e0, 0xe86a8a } },
            new GuestKind { k = "grandpa", n = "おじいさん",   h = 0.92f, w = 1.02f, heads = 0.4f, hair = "bald", wear = "pants", glass = true, beard = true, pal = new[] { 0x8a7a5a, 0x6a6a60, 0x7a6a7a } },
            new GuestKind { k = "grandma", n = "おばあさん",   h = 0.88f, w = 1.02f, heads = 0.4f, hair = "bun",  wear = "dress", glass = true, bag = "hand", pal = new[] { 0xa08a9a, 0x8a9a8a, 0xb0a090 } },
            new GuestKind { k = "tourist", n = "観光客",       h = 1.00f, w = 1.04f, hair = "short", wear = "pants", hat = "hat", bag = "hand", pal = new[] { 0xf0e0a0, 0xe8a060, 0x80c0d0 } },
            new GuestKind { k = "worker",  n = "作業員",       h = 1.00f, w = 1.06f, hair = "short", wear = "pants", hat = "helmet", pal = new[] { 0xd8a030, 0xc06a30, 0x5a7a4a } },
            new GuestKind { k = "chefguest", n = "コック",     h = 1.00f, w = 1.04f, hair = "short", wear = "apron", hat = "chef", pal = new[] { 0xf0f0ea } },
            new GuestKind { k = "sporty",  n = "スポーツ",     h = 1.02f, w = 0.98f, hair = "pony",  wear = "pants", pal = new[] { 0xe84a4a, 0x3f8ac0, 0x4ac07a } },
        };

        public static GuestKind Find(string k)
        {
            if (k == null) return null;
            foreach (var g in All) if (g.k == k) return g;
            return null;
        }
    }

    // ------------------------------------------------------------------ お店
    public class ShopDef { public string n, fricLabel; public float price, repair, fric; }

    /// <summary>お店の種類。JS版 shops.js と同じ。</summary>
    public static class Shops
    {
        public static readonly List<ShopDef> All = new List<ShopDef>
        {
            new ShopDef { n = "カフェ",               price = 0.6f, repair = 0.6f, fric = 1.00f, fricLabel = "普通" },
            new ShopDef { n = "定食屋",               price = 0.6f, repair = 0.6f, fric = 0.72f, fricLabel = "やや滑る" },
            new ShopDef { n = "町中華",               price = 1.0f, repair = 0.6f, fric = 0.32f, fricLabel = "超滑る" },
            new ShopDef { n = "ファミレス",           price = 1.0f, repair = 1.0f, fric = 1.00f, fricLabel = "普通" },
            new ShopDef { n = "カジュアルレストラン", price = 1.6f, repair = 1.0f, fric = 1.00f, fricLabel = "普通" },
            new ShopDef { n = "高級和食",             price = 1.6f, repair = 1.8f, fric = 1.00f, fricLabel = "普通" },
            new ShopDef { n = "高級フレンチ",         price = 2.6f, repair = 1.8f, fric = 1.35f, fricLabel = "高" },
            new ShopDef { n = "最高級ホテル",         price = 2.6f, repair = 3.0f, fric = 1.35f, fricLabel = "高" },
        };

        public static ShopDef Find(string n)
        {
            foreach (var s in All) if (s.n == n) return s;
            return null;
        }
    }

    // ------------------------------------------------------------------ 料理
    public class Dish
    {
        public string k, n, set, shape;
        public float price, frag;
        public int col;
    }

    /// <summary>料理。JS版 DISHES と同じ並び（オンラインで番号を送るので、並びは変えない）。</summary>
    public static class Dishes
    {
        public static readonly List<Dish> All = new List<Dish>
        {
            new Dish { k = "hamburg", n = "ハンバーグ", price = 1200, frag = 1.0f, col = 0x8b5a2b },
            new Dish { k = "parfait", n = "パフェ",     price = 900,  frag = 1.9f, col = 0xf0a0bd },
            new Dish { k = "ramen",   n = "ラーメン",   price = 950,  frag = 1.6f, col = 0xe5bd76 },
            new Dish { k = "steak",   n = "ステーキ",   price = 2200, frag = 0.7f, col = 0x7b3a2c },
            new Dish { k = "pilaf",   n = "ピラフ",     price = 1000, frag = 0.5f, col = 0xe2cc85 },
            new Dish { k = "soup",    n = "スープ",     price = 650,  frag = 2.3f, col = 0xc8752c },
            new Dish { k = "salad",   n = "サラダ",     price = 750,  frag = 1.2f, col = 0x5c9c46 },
            new Dish { k = "tacos",   n = "タコス",     price = 1150, frag = 1.3f, col = 0xc78737 },
            new Dish { k = "crepe_berry",   n = "いちご",       price = 700, frag = 1.6f, col = 0xe8506a, set = "crepe", shape = "crepe" },
            new Dish { k = "crepe_choco",   n = "チョコバナナ", price = 650, frag = 1.4f, col = 0x6b3e26, set = "crepe", shape = "crepe" },
            new Dish { k = "crepe_caramel", n = "キャラメル",   price = 650, frag = 1.2f, col = 0xc98a3a, set = "crepe", shape = "crepe" },
            new Dish { k = "crepe_matcha",  n = "抹茶あずき",   price = 750, frag = 1.4f, col = 0x7aa84a, set = "crepe", shape = "crepe" },
            new Dish { k = "crepe_tuna",    n = "ツナサラダ",   price = 800, frag = 1.0f, col = 0x9fd06a, set = "crepe", shape = "crepe" },
            new Dish { k = "crepe_juice",   n = "ジュース",     price = 450, frag = 2.2f, col = 0xf2b134, set = "crepe", shape = "cup" },
        };

        /// <summary>その面で出す料理（JS版 menuOf）。dishes の指定 → menu の組 → ふつうの料理。</summary>
        public static List<Dish> MenuOf(StageCfg C)
        {
            var outL = new List<Dish>();
            if (C != null && C.dishes != null && C.dishes.Count > 0)
            {
                foreach (var d in All) if (C.dishes.Contains(d.k)) outL.Add(d);
                if (outL.Count > 0) return outL;
            }
            string set = C != null ? (C.menu ?? "") : "";
            foreach (var d in All) if ((d.set ?? "") == set) outL.Add(d);
            if (outL.Count > 0) return outL;
            foreach (var d in All) if (d.set == null) outL.Add(d);
            return outL;
        }

        /// <summary>料理の完成度 → 値段の割合（JS版 priceTier）。</summary>
        public static float PriceTier(float integ)
        {
            return integ >= 75 ? 1.0f : integ >= 50 ? 0.75f : integ >= 25 ? 0.5f : 0.25f;
        }
    }
}
