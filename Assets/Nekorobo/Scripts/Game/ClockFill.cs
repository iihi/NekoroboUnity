using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 残り時間の棒（ショップ・面選び。HTML版 .s2-clock）。x, y, w は枠の絵 ui/shop/bar.png（1547×50 を半分の 25 の高さ）を置く所。
    /// 枠は左（時計と斜めの口）90・右（丸い端）60 を伸ばさず、間だけを伸ばす（面選びは短いので、前は絵ごと縮めて右の丸みがつぶれていた。担当）。
    /// 中身は PSD どおり時計の灰色の板の下から始め、板と時計（bar_clk.png）を中身の上に重ねる
    /// （前は枠を1枚の絵にして中身をその上に描いたので、黄色が時計に重なっていた。担当。HTML版 tools/psd/cutclock.py）
    /// </summary>
    public static class ClockBar
    {
        public static ClockFill Add(RectTransform parent, float x, float y, float w)
        {
            const float L = 90, R = 60;                       // 絵の上の端の幅（px）。置くのは半分
            var t = DataRoot.Texture("ui/shop/bar.png");
            float tw = t != null ? t.width : 1547;
            Part(parent, t, x, y, L / 2, new Rect(0, 0, L / tw, 1));
            Part(parent, t, x + L / 2, y, w - (L + R) / 2, new Rect(L / tw, 0, 1 - (L + R) / tw, 1));
            Part(parent, t, x + w - R / 2, y, R / 2, new Rect(1 - R / tw, 0, R / tw, 1));
            var fr = UiBoard.Box(parent, x + 21, y + 7.5f, w - 31, 10, "Fill");
            fr.gameObject.AddComponent<CanvasRenderer>();      // 自前の Graphic には自動で付かなかった
            var f = fr.gameObject.AddComponent<ClockFill>();
            f.tex = DataRoot.Texture("ui/shop/fill.png");
            f.raycastTarget = false;
            UiBoard.Img(parent, "ui/shop/bar_clk.png", x, y, 37.5f, 25);   // 時計の板（中身の上）
            return f;
        }
        static void Part(RectTransform parent, Texture t, float x, float y, float w, Rect uv)
        {
            var im = UiBoard.Box(parent, x, y, w, 25, "Bar").gameObject.AddComponent<RawImage>();
            if (t != null) { t.wrapMode = TextureWrapMode.Clamp; im.texture = t; im.uvRect = uv; } else im.color = new Color(0, 0, 0, 0);
            im.raycastTarget = false;
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
