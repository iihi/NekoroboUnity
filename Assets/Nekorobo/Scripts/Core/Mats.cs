using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nekorobo
{
    /// <summary>
    /// 素材。JS版の mat(色, 粗さ) と同じ使い方にしてある（同じ色は使い回す）。
    ///
    /// 色は JS版と同じ16進（sRGB）で渡す。Unity は素材の色を sRGB として受け取り、
    /// リニア空間へは自分で直すので、そのまま入れてよい。
    ///
    /// ※ ビルドしたときに URP のシェーダーが抜けないよう、あとで
    ///   Graphics 設定の「Always Included Shaders」に入れる必要がある（エディタでは不要）。
    /// </summary>
    public static class Mats
    {
        static Shader lit, unlit;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        static Shader Lit
        {
            get
            {
                if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null) lit = Shader.Find("Standard");
                return lit;
            }
        }
        static Shader Unlit
        {
            get
            {
                if (unlit == null) unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit == null) unlit = Shader.Find("Unlit/Color");
                return unlit;
            }
        }

        public static Color Hex(int hex)
        {
            return new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f);
        }

        public static Color Hex(string css)
        {
            Color c;
            if (!string.IsNullOrEmpty(css) && ColorUtility.TryParseHtmlString(css, out c)) return c;
            return Color.magenta;
        }

        /// <summary>JS版の mat(c, rough)。粗さ 0.95 が既定（つや消し）。</summary>
        public static Material Get(int hex, float rough = 0.95f, float metal = 0f)
        {
            string key = "lit|" + hex + "|" + rough + "|" + metal;
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = NewLit(Hex(hex), rough, metal);
            cache[key] = m;
            return m;
        }

        public static Material NewLit(Color c, float rough = 0.95f, float metal = 0f)
        {
            var m = new Material(Lit);
            m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f - rough);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>貼り絵つきの素材。repeat は貼り絵の繰り返し回数。</summary>
        public static Material Textured(Texture2D tex, Color tint, float rough, Vector2 repeat)
        {
            var m = NewLit(tint, rough);
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetTextureScale("_BaseMap", repeat);
            m.mainTextureScale = repeat;
            return m;
        }

        /// <summary>光の影響を受けない色（MeshBasicMaterial）。alpha &lt; 1 なら半透明。</summary>
        public static Material Basic(Color c, bool transparent = false, bool doubleSided = false, bool depthWrite = true)
        {
            string key = "basic|" + c + "|" + transparent + "|" + doubleSided + "|" + depthWrite;
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = NewBasic(c, transparent, doubleSided, depthWrite);
            cache[key] = m;
            return m;
        }

        public static Material NewBasic(Color c, bool transparent = false, bool doubleSided = false, bool depthWrite = true)
        {
            var m = new Material(Unlit);
            m.SetColor("_BaseColor", c);
            m.color = c;
            if (transparent) MakeTransparent(m, depthWrite);
            if (doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);
            return m;
        }

        /// <summary>貼り絵だけを出す素材（看板・顔パネル）。</summary>
        public static Material BasicTex(Texture tex, bool transparent = false, bool doubleSided = false)
        {
            var m = new Material(Unlit);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            if (transparent) MakeTransparent(m, false);
            if (doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);
            return m;
        }

        /// <summary>URP の素材を半透明にする（実行中に切り替えるときの決まった手順）。</summary>
        public static void MakeTransparent(Material m, bool depthWrite = false)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", depthWrite ? 1f : 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>半透明の Lit（氷のつや・水面）。</summary>
        public static Material LitTransparent(Color c, float rough)
        {
            var m = NewLit(c, rough);
            MakeTransparent(m, false);
            return m;
        }
    }

    /// <summary>
    /// 日本語の字。OS のフォントをその場で使う（仮。本番のUIはデザインの絵が来てから作り直す前提）。
    /// </summary>
    public static class Fonts
    {
        static Font ui;
        public static Font UI
        {
            get
            {
                if (ui == null)
                    ui = Font.CreateDynamicFontFromOSFont(
                        new[] { "Meiryo UI", "Meiryo", "Yu Gothic UI", "MS Gothic", "Arial" }, 32);
                return ui;
            }
        }
    }
}
