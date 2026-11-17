using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nekorobo.EditorTools
{
    /// <summary>
    /// 中国語・韓国語の書体（HTML版の assets/fonts の Noto Sans SC・TC・KR Bold。ゲームで使う字だけに削った物）を、
    /// Resources/Fonts へ写す。HTML版で辞書を直して書体を作り直したら（tools/lang/subset_fonts.py）、
    /// エディタを開いた時・メニュー「Nekorobo/言語の書体を写し直す」で中身が違えば写し直す。
    /// 削った書体に無い字（部屋の名前など）は、PC の書体（Microsoft YaHei など）で出す（書体の fontNames。先頭は書体自身の名前でないと効かない）。
    /// </summary>
    [InitializeOnLoad]
    public static class LangFontSync
    {
        const string Dst = "Assets/Nekorobo/Resources/Fonts";
        static readonly string[][] FONTS = {
            new[] { "NotoSansSC-Bold.otf", "Noto Sans SC", "Microsoft YaHei", "PingFang SC" },
            new[] { "NotoSansTC-Bold.otf", "Noto Sans TC", "Microsoft JhengHei", "PingFang TC" },
            new[] { "NotoSansKR-Bold.otf", "Noto Sans KR", "Malgun Gothic", "Apple SD Gothic Neo" },
        };

        static LangFontSync()
        {
            EditorApplication.delayCall += () => { if (!Application.isBatchMode) Sync(false); };
        }

        [MenuItem("Nekorobo/言語の書体を写し直す")]
        static void SyncMenu() { Sync(true); }

        static void Sync(bool log)
        {
            string src;
            try { src = Path.Combine(DataRoot.Path, "assets", "fonts"); } catch { return; }
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(Dst);
            int n = 0;
            foreach (var f in FONTS)
            {
                var a = Path.Combine(src, f[0]); var b = Dst + "/" + f[0];
                if (!File.Exists(a)) continue;
                bool same = File.Exists(b) && new FileInfo(a).Length == new FileInfo(b).Length
                            && System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(a), File.ReadAllBytes(b));
                if (!same) { File.Copy(a, b, true); AssetDatabase.ImportAsset(b); n++; }
                var imp = AssetImporter.GetAtPath(b) as TrueTypeFontImporter;
                if (imp != null && (imp.fontNames == null || imp.fontNames.Length != f.Length - 1))
                {
                    var names = new string[f.Length - 1];
                    System.Array.Copy(f, 1, names, 0, names.Length);
                    imp.fontNames = names;
                    EditorUtility.SetDirty(imp);
                    imp.SaveAndReimport();
                }
            }
            if (n > 0 || log) Debug.Log("[Nekorobo] 言語の書体を写しました: " + n + " 個");
        }
    }
}
