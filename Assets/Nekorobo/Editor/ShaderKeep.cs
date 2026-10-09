using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nekorobo.EditorTools
{
    /// <summary>
    /// **EXE に書き出したときにシェーダーが抜けないようにする。**
    ///
    /// Unity は「どの素材からも使われていないシェーダー（と、その組み合わせ）」を書き出しから外す。
    /// ネコ配は素材を全部コードで作る（Mats）ので、そのままだと URP のシェーダーが外れて、EXE では面が何も描けなかった
    /// （Shader.Find が null を返す）。モデル（glb）を読む glTFast のシェーダーも同じ。
    ///
    ///   1. 見本の素材（Resources/Mats）… Mats が使う形をひと通り（Lit／半透明の Lit／光る Lit／Unlit／半透明の Unlit）。
    ///      Resources に置いた物は必ず書き出しに入るので、URP のシェーダーとその組み合わせが残る。Mats はここからシェーダーを取る
    ///   2. 使った組み合わせの控え（Settings/NkShaders.shadervariants）… 再生中に全部の面を読んでから
    ///      メニュー「Nekorobo/シェーダーの控えを取る」で取る。Graphics 設定の Preloaded Shaders に入れるので書き出しに入る（glTFast 用）
    /// </summary>
    public static class ShaderKeep
    {
        const string MatDir = "Assets/Nekorobo/Resources/Mats";
        public const string SvcPath = "Assets/Nekorobo/Settings/NkShaders.shadervariants";

        [MenuItem("Nekorobo/シェーダーの見本の素材を作る")]
        public static void MakeTemplates()
        {
            Directory.CreateDirectory(MatDir);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Save(new Material(lit), "NkLit");
            var lt = new Material(lit); Mats.MakeTransparent(lt); Save(lt, "NkLitClear");
            var le = new Material(lit); le.EnableKeyword("_EMISSION"); le.SetColor("_EmissionColor", Color.white); Save(le, "NkLitGlow");
            Save(new Material(unlit), "NkUnlit");
            var ut = new Material(unlit); Mats.MakeTransparent(ut); Save(ut, "NkUnlitClear");
            AssetDatabase.SaveAssets();
            Debug.Log("[ShaderKeep] 見本の素材を作りました: " + MatDir);
        }

        static void Save(Material m, string name)
        {
            var p = MatDir + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(p) != null) AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(m, p);
        }

        /// <summary>控えを空にする（このあと面を全部読んでから TakeSvc）。</summary>
        [MenuItem("Nekorobo/シェーダーの控えを空にする")]
        public static void ClearSvc()
        {
            typeof(ShaderUtil).GetMethod("ClearCurrentShaderVariantCollection", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(null, null);
        }

        /// <summary>いままでに使った組み合わせを保存して、Preloaded Shaders に入れる。</summary>
        [MenuItem("Nekorobo/シェーダーの控えを取る")]
        public static string TakeSvc()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SvcPath));
            typeof(ShaderUtil).GetMethod("SaveCurrentShaderVariantCollection", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(null, new object[] { SvcPath });
            AssetDatabase.ImportAsset(SvcPath);
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(SvcPath);
            if (svc == null) return "控えが作れませんでした";
            // Graphics 設定の Preloaded Shaders に入れる（まだ入っていなければ）
            // Graphics 設定そのもの（ファイルから読み直した物に書いても効かなかった）
            var gs = GraphicsSettings.GetGraphicsSettings();
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_PreloadedShaders");
            bool has = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == svc) has = true;
            if (!has)
            {
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = svc;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(gs);
                AssetDatabase.SaveAssets();
            }
            string msg = "シェーダー " + svc.shaderCount + " 個・組み合わせ " + svc.variantCount + " 通りを控えました";
            Debug.Log("[ShaderKeep] " + msg + ": " + SvcPath);
            return msg;
        }
    }
}
