using UnityEngine;
using UnityEngine.UI;

namespace Nekorobo
{
    /// <summary>
    /// 選択枠（HTML版 selFrame。清書1111 の「選択」の絵）。
    /// 選んでいるボタンを黄色い枠で囲む。キー・パッドでもマウスでも同じ（マウスは乗ったボタンが選ばれる）。
    /// 枠は PSD の絵（HTML版 assets/ui/sel。tools/psd/cutsel.py）を、ボタンの大きさに合わせて伸ばして描く（担当「添付の枠を使って、良い感じにサイズを調整して」）。
    ///   斜めのボタン … skew.png（「コントローラー」の札の枠）。角と斜めの辺は伸ばさず、まっすぐな上下の辺だけ伸ばす。
    ///                  傾きが違うボタン（タイトルの板・最終結果の札）は、絵をずらして同じ傾きに作り直す（tan … 高さ1あたりの横のずれ）。
    ///                  板の上の絵のボタン（img。設定・実績・リザルト・最終結果・ストーリークリア。箱はまっすぐで絵が斜め）は、
    ///                  PSD の「札と枠」の位置の差を同じ割合で当てて置く（ぴったり重なる）
    ///   それ以外     … box.png（実績の仕分けの札の枠）を、角を残して伸ばす
    /// 線の太さは PSD のまま（1280×720 の板では半分）。HTML版は絵を行ごとにずらして canvas に描く。ここは同じ形を
    /// 四角を並べたメッシュで描く（行ごとのずれは高さに比例するので、四角の上下の辺をずらすだけで同じになる）。
    /// 毎フレーム、選んでいる画面が Show を呼ぶ。そのフレームに誰も呼ばなければ消える（画面を閉じたら自然に消える）
    /// </summary>
    public static class SelFrame
    {
        // 斜めの枠の絵の決まり（cutsel.py で測った値。PSD の px。HTML版 SEL_SKEW と同じ）。S0 … 絵の傾き、ES … 左右の伸ばさない幅、
        // BTN … 「コントローラー」の札の箱（高さ BH）に対する枠の箱の差（左・上・右・下）
        const float S0 = 0.338f, ES = 90, BH = 138;
        static readonly float[] BTN = { -6, -8, 1, -5 };
        const float BOX_C = 40;                  // 四角い枠の角（伸ばさない幅）

        static RectTransform fr;
        static SelMesh mesh;
        static CanvasGroup cg;
        static int shownAt = -10;
        static Texture2D texSkew, texBox;

        /// <summary>target を囲む。tan … 斜めのボタンの傾き（マイナスは上が右へずれる形）、box … 四角い枠。
        /// sub … target の中の範囲だけ囲む（target の左上からの px。HTML版 data-fr。ボタンから離さない）。
        /// widen … target の箱はまっすぐで、絵がその箱を真ん中を軸に傾けた形（タイトルのボタン）。傾けたぶん箱を横へ広げてから囲む
        /// （HTML版は傾けた要素の外接の箱を測るので同じになる）。
        /// img … 板の上の絵のボタン。PSD の札と枠の位置の差で置く（傾きも絵のまま）。
        /// pad … ボタンから離す幅（target の px。HTML版 data-pad。マイナスなら高さから決める）</summary>
        public static void Show(RectTransform target, float tan, bool box, Rect? sub = null, bool widen = false, bool img = false, float pad = -1)
        {
            if (target == null || !target.gameObject.activeInHierarchy) { Hide(); return; }
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null) { Hide(); return; }
            if (texSkew == null) texSkew = Load("ui/sel/skew.png");
            if (texBox == null) texBox = Load("ui/sel/box.png");
            if (texSkew == null || texBox == null) { Hide(); return; }
            var root = (RectTransform)canvas.rootCanvas.transform;
            if (fr == null || fr.parent != root) Build(root);
            // 画面の上での箱（root の中の座標。y は上向き）
            var wc = new Vector3[4];
            target.GetWorldCorners(wc);
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in wc) { Vector2 p = root.InverseTransformPoint(c); mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
            float left = mn.x, top = mx.y, w = mx.x - mn.x, h = mx.y - mn.y;
            if (w <= 0 || h <= 0) { Hide(); return; }
            // 板の倍率（target の 1px が root でいくつか）。PSD の px → root は 0.5 × 板の倍率
            float bs = h / Mathf.Max(1, target.rect.height), k = 0.5f * bs;
            bool exact = false;
            if (sub.HasValue)
            {
                var s = sub.Value;
                left += s.x * bs; top -= s.y * bs; w = s.width * bs; h = s.height * bs;
                exact = true;
            }
            bool skew = !box && Mathf.Abs(tan) > 0.001f;
            if (widen && skew) { float ex = Mathf.Abs(tan) * h; left -= ex / 2; w += ex; }
            float a = Mathf.Abs(tan), L, T, Wd, Hd;
            if (skew && img)
            {
                float u = h / BH;
                L = left + BTN[0] * u; T = top - BTN[1] * u; Wd = w + (BTN[2] - BTN[0]) * u; Hd = h + (BTN[3] - BTN[1]) * u;
                a = S0;
            }
            else
            {
                float p = exact ? 0 : pad >= 0 ? pad * bs : Mathf.Clamp(h * 0.07f, 3, 8);     // ボタンから少し離す
                L = left - p; T = top + p; Wd = w + p * 2; Hd = h + p * 2;
            }
            // 絵の大きさ（PSD の px）で形を作って、k 倍で出す
            fr.sizeDelta = mesh.Set(skew ? texSkew : texBox, skew, Wd / k, Hd / k, a, k);
            fr.localPosition = new Vector3(L, T, 0);
            fr.gameObject.SetActive(true);
            fr.SetAsLastSibling();
            cg.alpha = 0.86f + 0.14f * Mathf.Cos(Time.unscaledTime * Mathf.PI);      // 1 秒かけて少し薄くなって戻る
            shownAt = Time.frameCount;
        }

        public static void Hide() { if (fr != null) fr.gameObject.SetActive(false); }

        static Texture2D Load(string rel)
        {
            var t = DataRoot.Texture(rel);
            if (t != null) { t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear; }
            return t;
        }

        static void Build(RectTransform root)
        {
            if (fr != null) Object.Destroy(fr.gameObject);
            var go = new GameObject("SelFrame", typeof(RectTransform), typeof(CanvasGroup), typeof(Keeper), typeof(CanvasRenderer));
            fr = (RectTransform)go.transform;
            fr.SetParent(root, false);
            fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 0.5f);
            fr.pivot = new Vector2(0, 1);                        // 左上で置く
            cg = go.GetComponent<CanvasGroup>(); cg.blocksRaycasts = false; cg.interactable = false;
            mesh = go.AddComponent<SelMesh>();
            mesh.raycastTarget = false;
        }

        // 呼ばれなくなったら消す
        class Keeper : MonoBehaviour
        {
            void LateUpdate() { if (Time.frameCount - shownAt > 1 && fr != null) fr.gameObject.SetActive(false); }
        }

        /// <summary>枠の絵を伸ばして描くメッシュ。点は PSD の px（左上が原点、y は下向き）で作って k 倍する</summary>
        public class SelMesh : MaskableGraphic
        {
            Texture tex; bool skew; float W, H, a, k = 1;
            string key = "";
            public override Texture mainTexture { get { return tex != null ? tex : s_WhiteTexture; } }

            static float EsOf(float tw) { return Mathf.Min(ES, Mathf.Floor(tw / 2) - 1); }
            static float WbOf(float tw, float th, float w, float h, float tilt) { return Mathf.Max(EsOf(tw) * 2 - S0 * th + 8, w - tilt * h); }   // 描く枠の、斜めを除いた幅

            /// <summary>形を決める（w・h … 描く大きさ（PSD の px）、tilt … 傾き、scale … PSD の px → root）。返り値は出す大きさ（root）</summary>
            public Vector2 Set(Texture t, bool sk, float w, float h, float tilt, float scale)
            {
                float cw, ch;
                if (sk) { cw = WbOf(t.width, t.height, w, h, tilt) + tilt * h; ch = h; }
                else { cw = Mathf.Max(BOX_C * 2 + 2, w); ch = Mathf.Max(BOX_C * 2 + 2, h); }
                string nk = (sk ? "s" : "b") + Mathf.Round(w * 4) + "x" + Mathf.Round(h * 4) + ":" + tilt.ToString("0.000") + ":" + scale.ToString("0.0000");
                if (t != tex) { tex = t; SetMaterialDirty(); }
                if (nk != key) { key = nk; skew = sk; W = w; H = h; a = tilt; k = scale; SetVerticesDirty(); }
                return new Vector2(cw * scale, ch * scale);
            }

            // 四角を1つ足す。y0・y1 … 上下の辺、xa0・xa1 … 上の辺の左右、xb0・xb1 … 下の辺の左右（PSD の px）。su・sv … 絵の範囲（px）
            void Quad(VertexHelper vh, Rect r, float y0, float y1, float xa0, float xa1, float xb0, float xb1, float su0, float su1, float sv0, float sv1)
            {
                float tw = tex.width, th = tex.height;
                int n = vh.currentVertCount;
                var col = (Color32)color;
                vh.AddVert(new Vector3(r.xMin + xa0 * k, r.yMax - y0 * k), col, new Vector2(su0 / tw, 1 - sv0 / th));
                vh.AddVert(new Vector3(r.xMin + xa1 * k, r.yMax - y0 * k), col, new Vector2(su1 / tw, 1 - sv0 / th));
                vh.AddVert(new Vector3(r.xMin + xb1 * k, r.yMax - y1 * k), col, new Vector2(su1 / tw, 1 - sv1 / th));
                vh.AddVert(new Vector3(r.xMin + xb0 * k, r.yMax - y1 * k), col, new Vector2(su0 / tw, 1 - sv1 / th));
                vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                if (tex == null || H <= 0) return;
                var r = rectTransform.rect;
                float tw = tex.width, th = tex.height;
                if (skew)
                {
                    float es = EsOf(tw), wb = tw - S0 * th, mid = WbOf(tw, th, W, H, a) - wb;   // mid … 真ん中を伸ばす長さ
                    float mw = Mathf.Max(1, tw - es * 2 + mid);
                    float top = Mathf.Floor(th / 2), bot = th - top;
                    // 上半分と下半分は絵の行をそのまま、間は真ん中の行をくり返す（縦に伸ばしても斜めの辺がまっすぐ続くように）。
                    // 行 y（絵の行 ys）のずらし off ＝ a*(H−y) − S0*(h−ys)（絵の傾きを消して、描く傾きにする）
                    float yA = Mathf.Min(top, H), yC = Mathf.Max(top, H - bot);
                    System.Action<float, float, float, float> band = (y0, y1, sv0, sv1) =>
                    {
                        if (y1 - y0 <= 0.001f) return;
                        // 間の帯は真ん中の1行（sv0 == sv1）。ずらしはその行の位置で
                        float s0 = Mathf.Abs(sv1 - sv0) < 0.01f ? top : sv0, s1 = Mathf.Abs(sv1 - sv0) < 0.01f ? top : sv1;
                        float o0 = a * (H - y0) - S0 * (th - s0), o1 = a * (H - y1) - S0 * (th - s1);
                        Quad(vh, r, y0, y1, o0, o0 + es, o1, o1 + es, 0, es, sv0, sv1);
                        Quad(vh, r, y0, y1, o0 + es, o0 + es + mw, o1 + es, o1 + es + mw, es, tw - es, sv0, sv1);
                        Quad(vh, r, y0, y1, o0 + tw - es + mid, o0 + tw + mid, o1 + tw - es + mid, o1 + tw + mid, tw - es, tw, sv0, sv1);
                    };
                    band(0, yA, 0, yA);                                     // 上（絵の行そのまま）
                    if (yC > yA) band(yA, yC, top + 0.5f, top + 0.5f);      // 間（真ん中の1行の、行の真ん中を取ってにじまないように）
                    band(yC, H, th - (H - yC), th);                         // 下（絵の下の行を下に合わせて）
                }
                else
                {
                    // 四角い枠：角を残して伸ばす（9つに切る。真ん中は空）
                    float C = BOX_C, X = Mathf.Max(C * 2 + 2, W), Y = Mathf.Max(C * 2 + 2, H);
                    float[] dx = { 0, C, X - C, X }, dy = { 0, C, Y - C, Y }, sx = { 0, C, tw - C, tw }, sy = { 0, C, th - C, th };
                    for (int j = 0; j < 3; j++)
                        for (int i = 0; i < 3; i++)
                            if (i != 1 || j != 1) Quad(vh, r, dy[j], dy[j + 1], dx[i], dx[i + 1], dx[i], dx[i + 1], sx[i], sx[i + 1], sy[j], sy[j + 1]);
                }
            }
        }
    }
}
