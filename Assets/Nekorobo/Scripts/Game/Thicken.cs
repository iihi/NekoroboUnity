using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 字を太らせる（同じ色の写しを左右に少しずらして重ねる）。CSS の font-weight:900 の代わり。
    /// Unity の字は OS のフォントを太字で引いても、HTML版の黒い太さ（900）まで太くならないため。
    /// 頂点の色をそのまま写すので、あとから字の色を変えても写しが付いてくる（Outline と違う所）。
    /// </summary>
    public class Thicken : BaseMeshEffect
    {
        public float px = 0.45f;
        static readonly System.Collections.Generic.List<UIVertex> buf = new System.Collections.Generic.List<UIVertex>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            buf.Clear();
            vh.GetUIVertexStream(buf);
            int n = buf.Count;
            var outv = new System.Collections.Generic.List<UIVertex>(n * 3);
            foreach (var dx in new[] { -px, px })
                for (int i = 0; i < n; i++) { var v = buf[i]; v.position.x += dx; outv.Add(v); }
            outv.AddRange(buf);
            vh.Clear();
            vh.AddUIVertexTriangleStream(outv);
        }
    }
}
