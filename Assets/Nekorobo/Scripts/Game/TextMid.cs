using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 真ん中寄せ（上下）の字を、字の見た目の真ん中で枠の真ん中に合わせる。
    /// Unity の Text は書体の上下の余白（ascent・descent）で真ん中を決めるので、ゲームの書体（Segoe UI ＋メイリオ）だと
    /// 字が 2〜3px 下へずれて、札（名前・Target・吹き出し）の真ん中に来なかった（担当「文字などを枠の中心にして欲しい」）。
    /// 書体・大きさごとに「Hあ」の字の箱を一度だけ測って、その真ん中が枠の真ん中に来るように字を上下に動かす
    /// （字ごとに測ると、出す字によって上下にぶれるため）。上・下寄せの字は動かさない。
    /// UiKit.Label・Overhead の字に付けてある。ふち（Outline・Shadow）より先に付ける（ふちも一緒に動くように）
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class TextMid : BaseMeshEffect
    {
        static readonly Dictionary<long, float> cache = new Dictionary<long, float>();
        Text text;

        /// <summary>付ける。測るのはここで先に済ませる（描き直しの途中で書体の字を足すと、Unity が描き直しの中の描き直しを嫌う）</summary>
        public static void Add(Text t)
        {
            if (t == null || t.GetComponent<TextMid>() != null) return;
            t.gameObject.AddComponent<TextMid>();
            if (t.font != null) Offset(t);
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            if (text == null) text = GetComponent<Text>();
            if (text == null || text.font == null) return;
            var al = text.alignment;
            if (al != TextAnchor.MiddleLeft && al != TextAnchor.MiddleCenter && al != TextAnchor.MiddleRight) return;
            float dy = Offset(text);
            if (Mathf.Abs(dy) < 0.05f) return;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.position.y += dy;
                vh.SetUIVertex(v, i);
            }
        }

        /// <summary>字を上へ動かす量（Text の座標）。真ん中寄せで「Hあ」を置いたときの字の箱の真ん中が、枠の真ん中からどれだけ下か</summary>
        static float Offset(Text t)
        {
            float ppu = Mathf.Max(0.01f, t.pixelsPerUnit);
            long key = ((long)t.font.GetInstanceID() << 24) ^ ((long)t.fontSize << 12) ^ ((long)t.fontStyle << 8) ^ Mathf.RoundToInt(ppu * 64);
            float dy;
            if (cache.TryGetValue(key, out dy)) return dy;
            var st = t.GetGenerationSettings(new Vector2(1000, 1000));
            st.textAnchor = TextAnchor.MiddleCenter; st.pivot = new Vector2(0.5f, 0.5f);
            st.richText = false; st.horizontalOverflow = HorizontalWrapMode.Overflow; st.verticalOverflow = VerticalWrapMode.Overflow;
            var gen = new TextGenerator();
            gen.Populate("Hあ", st);
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var v in gen.verts) { mn = Mathf.Min(mn, v.position.y); mx = Mathf.Max(mx, v.position.y); }
            dy = mn <= mx ? -(mn + mx) / 2 / ppu : 0;
            cache[key] = dy;
            return dy;
        }
    }
}
