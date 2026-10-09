using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 字に横向きのグラデーションを塗り、光の帯を左右に走らせる（上の帯の順位の数字）。
    /// JS版の CSS（background-clip:text ＋ background-size:260% ＋ background-position を 100%→-160% に動かす）と同じ計算。
    /// 頂点の色で塗るので、1文字の中は4隅の色の間をなめらかにつなぐだけ（数字1文字なら十分）。
    /// Outline・Shadow より先に付けること（先に塗っておくと、ふちの写しはふちの色のまま残る）。
    /// </summary>
    public class ShineText : BaseMeshEffect
    {
        /// <summary>グラデーションの色と位置（0〜1）。</summary>
        public Color[] cols; public float[] at;
        /// <summary>光が1回走る秒数（0 なら止めたまま pos で塗る）。</summary>
        public float period;
        /// <summary>CSS の background-position（1＝100%）。止めるときの位置。</summary>
        public float pos = 0.5f;
        float t;

        public void Set(Color[] c, float[] a, float per, float p = 0.5f)
        {
            cols = c; at = a; period = per; pos = p; t = 0;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        void Update()
        {
            if (period <= 0 || cols == null) return;
            t = (t + Time.unscaledDeltaTime / period) % 1f;
            pos = Mathf.Lerp(1f, -1.6f, t);
            graphic.SetVerticesDirty();
        }

        Color Eval(float u)
        {
            if (u <= at[0]) return cols[0];
            for (int i = 1; i < at.Length; i++)
                if (u <= at[i]) return Color.Lerp(cols[i - 1], cols[i], (u - at[i - 1]) / Mathf.Max(1e-5f, at[i] - at[i - 1]));
            return cols[cols.Length - 1];
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || cols == null || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float x0 = float.MaxValue, x1 = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); x0 = Mathf.Min(x0, v.position.x); x1 = Mathf.Max(x1, v.position.x); }
            float w = Mathf.Max(1e-3f, x1 - x0);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                // 260% の幅のグラデーションを、(幅−2.6幅)×pos だけずらして置いたときの、この点の位置
                float u = ((v.position.x - x0) / w + 1.6f * pos) / 2.6f;
                var c = Eval(u); c.a *= v.color.a / 255f;
                v.color = c;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
