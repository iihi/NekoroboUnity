using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 選択枠（HTML版 selFrame。担当の画面案。清書はあとで来るかもしれない）。
    /// 選んでいるボタンを黄色い2重の枠で囲む。キー・パッドでもマウスでも同じ（マウスは乗ったボタンが選ばれる）。
    /// 枠の形はボタンの形に合わせる。
    ///   斜めのボタン … 平行四辺形（tan … 高さ1あたりの横のずれ。マイナスは上が右へずれる形。このゲームの斜めのボタンはみなこれ）
    ///   それ以外     … 角を丸めた四角（box）
    /// 毎フレーム、選んでいる画面が Show を呼ぶ。そのフレームに誰も呼ばなければ消える（画面を閉じたら自然に消える）
    /// </summary>
    public static class SelFrame
    {
        static RectTransform fr;
        static UiRing glow, outer, inner;
        static CanvasGroup cg;
        static int shownAt = -10;

        /// <summary>target を囲む。sub … target の中の範囲だけ囲む（target の左上からの px。HTML版 data-fr）。
        /// widen … target の箱はまっすぐで、絵がその箱を真ん中を軸に傾けた形（タイトルのボタン）。傾けたぶん箱を横へ広げてから囲む
        /// （HTML版は傾けた要素の外接の箱を測るので同じになる）。板の絵のボタン（箱はまっすぐで絵が斜め）は広げない</summary>
        public static void Show(RectTransform target, float tan, bool box, Rect? sub = null, bool widen = false)
        {
            if (target == null || !target.gameObject.activeInHierarchy) { Hide(); return; }
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null) { Hide(); return; }
            var root = (RectTransform)canvas.rootCanvas.transform;
            if (fr == null || fr.parent != root) Build(root);
            // 画面の上での箱（root の中の座標。y は上向き）
            var wc = new Vector3[4];
            target.GetWorldCorners(wc);
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in wc) { Vector2 p = root.InverseTransformPoint(c); mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
            float left = mn.x, top = mx.y, w = mx.x - mn.x, h = mx.y - mn.y;
            if (w <= 0 || h <= 0) { Hide(); return; }
            if (sub.HasValue)
            {
                float k = h / Mathf.Max(1, target.rect.height);
                var s = sub.Value;
                left += s.x * k; top -= s.y * k; w = s.width * k; h = s.height * k;
            }
            if (widen && !box) { float ex = Mathf.Abs(tan) * h; left -= ex / 2; w += ex; }
            float pad = Mathf.Clamp(h * 0.08f, 4, 9);                       // ボタンから少し離す
            float W = w + pad * 2, H = h + pad * 2;
            fr.sizeDelta = new Vector2(W, H);
            fr.localPosition = new Vector3(left - pad + W / 2, top + pad - H / 2, 0);
            float sw = Mathf.Clamp(h * 0.07f, 4, 7), e = sw / 2 + 1;
            Vector2[] pts;
            if (!box && Mathf.Abs(tan) > 0.001f)
            {
                float sl = Mathf.Min(W * 0.4f, Mathf.Abs(tan) * H);         // 斜めのずれ
                pts = tan < 0 ? new[] { new Vector2(sl + e, e), new Vector2(W - e, e), new Vector2(W - sl - e, H - e), new Vector2(e, H - e) }
                              : new[] { new Vector2(e, e), new Vector2(W - sl - e, e), new Vector2(W - e, H - e), new Vector2(sl + e, H - e) };
            }
            else
            {
                float rr = Mathf.Min(14, H * 0.18f), x0 = e, y0 = e, x1 = W - e, y1 = H - e;
                pts = new[] { new Vector2(x0 + rr, y0), new Vector2(x1 - rr, y0), new Vector2(x1 - rr * 0.3f, y0 + rr * 0.3f), new Vector2(x1, y0 + rr),
                              new Vector2(x1, y1 - rr), new Vector2(x1 - rr * 0.3f, y1 - rr * 0.3f), new Vector2(x1 - rr, y1), new Vector2(x0 + rr, y1),
                              new Vector2(x0 + rr * 0.3f, y1 - rr * 0.3f), new Vector2(x0, y1 - rr), new Vector2(x0, y0 + rr), new Vector2(x0 + rr * 0.3f, y0 + rr * 0.3f) };
            }
            glow.Set(pts, sw + 6); outer.Set(pts, sw); inner.Set(pts, Mathf.Max(1.5f, sw * 0.3f));
            fr.gameObject.SetActive(true);
            fr.SetAsLastSibling();
            cg.alpha = 0.86f + 0.14f * Mathf.Cos(Time.unscaledTime * Mathf.PI);      // 1 秒かけて少し薄くなって戻る
            shownAt = Time.frameCount;
        }

        public static void Hide() { if (fr != null) fr.gameObject.SetActive(false); }

        static void Build(RectTransform root)
        {
            if (fr != null) Object.Destroy(fr.gameObject);
            var go = new GameObject("SelFrame", typeof(RectTransform), typeof(CanvasGroup), typeof(Keeper));
            fr = (RectTransform)go.transform;
            fr.SetParent(root, false);
            fr.anchorMin = fr.anchorMax = fr.pivot = new Vector2(0.5f, 0.5f);
            cg = go.GetComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false;
            glow = UiRing.Add(fr, new Color(1, 0.86f, 0.24f, 0.35f));
            outer = UiRing.Add(fr, Mats.Hex(0xffe23f));
            inner = UiRing.Add(fr, Mats.Hex(0xfff7b8));
        }

        // 呼ばれなくなったら消す
        class Keeper : MonoBehaviour
        {
            void LateUpdate() { if (Time.frameCount - shownAt > 1 && fr != null) fr.gameObject.SetActive(false); }
        }
    }

    /// <summary>閉じた折れ線（太さ w）。点は箱の左上からの px（y は下向き）。</summary>
    public class UiRing : MaskableGraphic
    {
        Vector2[] pts = new Vector2[0]; float w = 4;
        public void Set(Vector2[] p, float width) { pts = p; w = width; SetVerticesDirty(); }
        public static UiRing Add(RectTransform parent, Color c)
        {
            var go = new GameObject("Ring", typeof(RectTransform), typeof(CanvasRenderer));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var g = go.AddComponent<UiRing>();
            g.color = c; g.raycastTarget = false;
            return g;
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (pts == null || pts.Length < 2) return;
            var r = rectTransform.rect;
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = new Vector2(r.xMin + pts[i].x, r.yMax - pts[i].y), b = new Vector2(r.xMin + pts[(i + 1) % n].x, r.yMax - pts[(i + 1) % n].y);
                var d = (b - a); if (d.sqrMagnitude < 1e-4f) continue; d.Normalize();
                var nn = new Vector2(-d.y, d.x) * (w / 2);
                a -= d * (w / 2); b += d * (w / 2);                       // 角が欠けないよう、少し伸ばす
                int k = vh.currentVertCount;
                vh.AddVert(a + nn, color, Vector2.zero); vh.AddVert(b + nn, color, Vector2.zero);
                vh.AddVert(b - nn, color, Vector2.zero); vh.AddVert(a - nn, color, Vector2.zero);
                vh.AddTriangle(k, k + 1, k + 2); vh.AddTriangle(k, k + 2, k + 3);
            }
        }
    }
}
