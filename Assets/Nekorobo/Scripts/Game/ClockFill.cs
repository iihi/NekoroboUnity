using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 残り時間の棒（ショップ・面選び。HTML版 .s2-clock）。src は枠の絵（ショップは ui/shop/bar.png、面選びは bar_ss.png）、x, y, w はそれを置く所（高さ 25）。
    /// 面選びは短いので、左（時計の所）と右（丸い端）は縮めず間だけを縮めた別の絵 bar_ss.png（前は絵ごと縮めて右の丸みがつぶれていた。担当。
    /// 3つに分けて並べると、拡大・縮小でつなぎ目が縦の線に見えた）。
    /// 中身は PSD どおり時計の灰色の板の下から始め、板と時計（bar_clk.png）を中身の上に重ねる
    /// （前は枠を1枚の絵にして中身をその上に描いたので、黄色が時計に重なっていた。担当。HTML版 tools/psd/cutclock.py）
    /// </summary>
    public static class ClockBar
    {
        public static ClockFill Add(RectTransform parent, string src, float x, float y, float w)
        {
            UiBoard.Img(parent, src, x, y, w, 25);
            var fr = UiBoard.Box(parent, x + 21, y + 7.5f, w - 31, 10, "Fill");
            fr.gameObject.AddComponent<CanvasRenderer>();      // 自前の Graphic には自動で付かなかった
            var f = fr.gameObject.AddComponent<ClockFill>();
            f.tex = DataRoot.Texture("ui/shop/fill.png");
            f.raycastTarget = false;
            UiBoard.Img(parent, "ui/shop/bar_clk.png", x, y, 37.5f, 25);   // 時計の板（中身の上）
            return f;
        }
    }

    /// <summary>残り時間の棒の中身。減るときは絵を縮めずに右を切る（HTML版の幅だけ変わる i と同じ）。左の端は時計の板の下に隠れる</summary>
    public class ClockFill : MaskableGraphic
    {
        public Texture tex;
        float ratio = 1;
        public override Texture mainTexture => tex != null ? tex : s_WhiteTexture;
        public float Ratio
        {
            get => ratio;
            set { value = Mathf.Clamp01(value); if (Mathf.Abs(value - ratio) < 1e-5f) return; ratio = value; SetVerticesDirty(); }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float W = r.width, w = W * ratio;
            if (w <= 0 || W <= 0) return;
            Color32 c = color;
            vh.AddVert(new Vector3(r.xMin, r.yMin), c, new Vector2(0, 0));
            vh.AddVert(new Vector3(r.xMin, r.yMax), c, new Vector2(0, 1));
            vh.AddVert(new Vector3(r.xMin + w, r.yMax), c, new Vector2(w / W, 1));
            vh.AddVert(new Vector3(r.xMin + w, r.yMin), c, new Vector2(w / W, 0));
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }
    }
}
