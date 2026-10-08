using System.Collections.Generic;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// ステージの見取り図（サムネイル）。JS版 thumb.js の drawThumb を写したもの。
    /// 絵を用意するのではなく、ステージのデータからその場で描く（地形を直せば絵も追従する）。
    /// 地形は1マス1色、置いた物は四角を置くだけ。
    /// </summary>
    public static class Thumb
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Of(string key, StageCfg st, int W = 192, int H = 108)
        {
            Texture2D t;
            if (key != null && cache.TryGetValue(key, out t) && t != null) return t;
            t = Draw(st, W, H);
            if (key != null) cache[key] = t;
            return t;
        }

        public static Texture2D Draw(StageCfg st, int W, int H)
        {
            var px = new Color32[W * H];
            Color32 bg = Mats.Hex(0x12161d);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            if (st == null || st.terrain.Count == 0) { tex.SetPixels32(px); tex.Apply(false); return tex; }
            var rows = st.terrain;
            int i0 = st.origin.x, j0 = st.origin.y, cols = 0;
            foreach (var r in rows) cols = Mathf.Max(cols, r.Length);
            // 盤面が縦横どちらに長くても収まる縮尺。余白は少しだけ取る
            const float pad = 3;
            float k = Mathf.Min((W - pad * 2) / cols, (H - pad * 2) / rows.Count);
            float ox = (W - cols * k) / 2, oy = (H - rows.Count * k) / 2;
            System.Action<float, float, float, float, Color32> rect = (x, y, w, h, c) =>
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(x)), x1 = Mathf.Min(W, Mathf.CeilToInt(x + w));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(y)), y1 = Mathf.Min(H, Mathf.CeilToInt(y + h));
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++) px[(H - 1 - yy) * W + xx] = c;   // テクスチャの y は下から
            };
            // ---- 地形（0.5px ずつ広げて、マスの継ぎ目に筋が出ないようにする）
            for (int r = 0; r < rows.Count; r++)
            {
                var line = rows[r];
                for (int c = 0; c < line.Length; c++)
                {
                    char ch = line[c];
                    string col;
                    if (ch == ' ' || Tiles.Def(ch) == null || !Tiles.COL.TryGetValue(ch, out col)) continue;
                    rect(ox + c * k - 0.5f, oy + r * k - 0.5f, k + 1, k + 1, (Color32)Mats.Hex(col));
                }
            }
            // ---- 置いた物（四角を置くだけ）
            foreach (var ob in st.objects)
            {
                if (ob == null || ob.t == "raft") continue;
                float bw, bd; Vector3 b;
                Tiles.ObjBox(ob, out bw, out bd, out b);
                float bx = b.x, bz = -b.z;                                   // three.js の向きへ戻す
                float w = bw * k, d = bd * k;
                float x = ox + (bx - bw / 2 - i0) * k, z = oy + (bz - bd / 2 - j0) * k;
                rect(x + 0.5f, z + 0.5f, Mathf.Max(1.5f, w - 1), Mathf.Max(1.5f, d - 1), (Color32)Mats.Hex(ObjCol(ob.t)));
            }
            tex.SetPixels32(px); tex.Apply(false);
            return tex;
        }

        static string ObjCol(string t)
        {
            string c;
            if (t != null && Tiles.OBJ_COL.TryGetValue(t, out c)) return c;
            var p = t != null ? Props.Get(t) : null;
            return p != null && !string.IsNullOrEmpty(p.col) ? p.col : "#9aa3b0";
        }
    }
}
