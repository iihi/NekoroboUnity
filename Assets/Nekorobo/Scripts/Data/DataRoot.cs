using System.IO;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Nekorobo
{
    /// <summary>
    /// HTML版（nekorobo3d フォルダ）の置き場所。**ステージ・数値・絵は HTML版のファイルをそのまま読む。**
    /// HTML版のエディタで作ったステージや、右パネルで保存した tune.json が、そのまま Unity でも効く。
    ///
    /// 探す順：
    ///   1. 環境変数 NEKOROBO_DATA
    ///   2. StreamingAssets/nekorobo3d（ビルドに同梱したとき）
    ///   3. このプロジェクトから見た ../../AI/ClaudeCode/Serving/nekorobo3d（いまの置き方）
    ///   4. H:\Source\AI\ClaudeCode\Serving\nekorobo3d
    /// </summary>
    public static class DataRoot
    {
        public const string Fallback = @"H:\Source\AI\ClaudeCode\Serving\nekorobo3d";
        static string path;

        public static string Path
        {
            get
            {
                if (path != null) return path;
                var cands = new System.Collections.Generic.List<string>();
                var env = System.Environment.GetEnvironmentVariable("NEKOROBO_DATA");
                if (!string.IsNullOrEmpty(env)) cands.Add(env);
                cands.Add(System.IO.Path.Combine(Application.streamingAssetsPath, "nekorobo3d"));
                cands.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
                          "..", "..", "..", "AI", "ClaudeCode", "Serving", "nekorobo3d")));
                cands.Add(Fallback);
                foreach (var c in cands)
                    if (File.Exists(System.IO.Path.Combine(c, "tiles.js"))
                        || Directory.Exists(System.IO.Path.Combine(c, "stages")))
                    { path = c; break; }
                if (path == null)
                {
                    Debug.LogError("[Nekorobo] HTML版のデータ（nekorobo3d）が見つかりません。"
                                   + "環境変数 NEKOROBO_DATA に場所を入れてください。");
                    path = Fallback;
                }
                return path;
            }
        }

        public static string File_(string rel) { return System.IO.Path.Combine(Path, rel.Replace('/', System.IO.Path.DirectorySeparatorChar)); }

        public static JObject ReadJson(string rel)
        {
            var f = File_(rel);
            if (!File.Exists(f)) return null;
            try { return JObject.Parse(File.ReadAllText(f, System.Text.Encoding.UTF8)); }
            catch (System.Exception e)
            {
                Debug.LogError("[Nekorobo] JSON が読めません: " + f + "\n" + e.Message);
                return null;
            }
        }

        static readonly System.Collections.Generic.Dictionary<string, Texture2D> texCache =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>assets/ からの相対パスで画像を読む。無ければ null。</summary>
        public static Texture2D Texture(string relUnderAssets)
        {
            if (string.IsNullOrEmpty(relUnderAssets)) return null;
            Texture2D t;
            if (texCache.TryGetValue(relUnderAssets, out t)) return t;
            var f = File_("assets/" + relUnderAssets);
            t = null;
            if (File.Exists(f))
            {
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                t.LoadImage(File.ReadAllBytes(f));
                t.wrapMode = TextureWrapMode.Repeat;
                t.anisoLevel = 4;
                t.name = relUnderAssets;
            }
            texCache[relUnderAssets] = t;
            return t;
        }
    }

    /// <summary>JObject を読みやすくする小道具。無い・型違いは既定値。</summary>
    public static class J
    {
        public static float F(JToken o, string k, float def)
        {
            var t = o == null ? null : o[k];
            if (t == null || t.Type == JTokenType.Null) return def;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) return t.Value<float>();
            float v; return float.TryParse(t.ToString(), out v) ? v : def;
        }
        public static float? FN(JToken o, string k)
        {
            var t = o == null ? null : o[k];
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) return t.Value<float>();
            return null;
        }
        public static int I(JToken o, string k, int def) { return Mathf.RoundToInt(F(o, k, def)); }
        public static string S(JToken o, string k, string def = null)
        {
            var t = o == null ? null : o[k];
            if (t == null || t.Type == JTokenType.Null) return def;
            return t.Type == JTokenType.String ? (string)t : t.ToString();
        }
        public static bool B(JToken o, string k, bool def = false)
        {
            var t = o == null ? null : o[k];
            if (t == null || t.Type == JTokenType.Null) return def;
            if (t.Type == JTokenType.Boolean) return (bool)t;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) return t.Value<float>() != 0f;
            return def;
        }
    }
}
