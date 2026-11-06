using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// ロボの胴の画面（メッセージエリア）に字を描く（JS版 drawMsgPanel）。
    /// 黒い地に水色の太字。日本語は6字ずつ、英語は10字前後で折り返す（2行を超えたら小さく）。
    /// 画面の外に置いた字と専用のカメラで、セリフが変わったときだけ絵（RenderTexture）へ描く。
    /// </summary>
    public static class MsgRender
    {
        static Camera cam;
        static TextMesh tm;
        static readonly Color BG = new Color(0.067f, 0.067f, 0.067f, 1), FG = Mats.Hex(0x7fe3ff);
        static readonly System.Text.RegularExpressions.Regex JP = new System.Text.RegularExpressions.Regex("[぀-ヿ一-鿿]");

        static void Setup()
        {
            if (cam != null) return;
            var go = new GameObject("MsgRender");
            Object.DontDestroyOnLoad(go);
            go.transform.position = new Vector3(0, -2000, 0);        // ゲームのカメラには写らない所
            cam = go.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 0.75f;  // 高さ 1.5（256×192 の板なので幅は 2）
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = BG;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 5;
            cam.enabled = false;
            var tg = new GameObject("Text");
            tg.transform.SetParent(go.transform, false);
            tg.transform.localPosition = new Vector3(0, 0, 1);
            tm = tg.AddComponent<TextMesh>();
            tm.font = Fonts.UI;
            tg.GetComponent<MeshRenderer>().sharedMaterial = Fonts.UI.material;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold; tm.color = FG;
            tm.fontSize = 80;
        }

        public static void Draw(RenderTexture rt, string text)
        {
            Setup();
            var lines = new System.Collections.Generic.List<string>();
            string cur = "";
            if (!string.IsNullOrEmpty(text))
            {
                if (JP.IsMatch(text))
                {
                    foreach (var ch in text) { cur += ch; if (cur.Length >= 6) { lines.Add(cur); cur = ""; } }
                }
                else
                {
                    foreach (var w in text.Split(' '))
                    {
                        if (cur.Length > 0 && (cur + " " + w).Length > 10) { lines.Add(cur); cur = w; }
                        else cur = cur.Length > 0 ? cur + " " + w : w;
                    }
                }
                if (cur.Length > 0) lines.Add(cur);
            }
            tm.text = string.Join("\n", lines.ToArray());
            // 1行の高さ：44px（3行以上は 36px）を 192px の板の高さ 1.5 に直す
            float px = lines.Count > 2 ? 36 : 44;
            tm.characterSize = px / 192f * 1.5f / (tm.fontSize * 0.1f) * 1.4f;
            tm.lineSpacing = 1.0f;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
        }
    }
}
