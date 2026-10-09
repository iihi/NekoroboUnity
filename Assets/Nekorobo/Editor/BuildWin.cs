using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Nekorobo.EditorTools
{
    /// <summary>
    /// Windows の EXE に書き出す（確かめる用）。メニュー「Nekorobo/Windows で書き出す」。
    /// 書き出し先は Builds/Win/Nekorobo.exe（git には入れない）。
    ///
    /// データ（ステージ・数値・絵）は**同梱しない**。EXE も HTML版のフォルダ（nekorobo3d）を直接読む
    /// （DataRoot の探す順。このPCの置き方なら見つかる。ほかのPCでは環境変数 NEKOROBO_DATA で場所を教える）。
    /// 結果は Builds/Win/build_result.txt にも書く（外から待つ用）。
    /// </summary>
    public static class BuildWin
    {
        public const string Dir = "Builds/Win";

        [MenuItem("Nekorobo/Windows で書き出す")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var res = Path.Combine(Dir, "build_result.txt");
            if (File.Exists(res)) File.Delete(res);
            var opt = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = Path.Combine(Dir, "Nekorobo.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var rep = BuildPipeline.BuildPlayer(opt);
            var s = rep.summary;
            string line = s.result + " " + s.totalTime.TotalSeconds.ToString("0") + "s " + (s.totalSize / (1024 * 1024)) + "MB errors=" + s.totalErrors + " warnings=" + s.totalWarnings;
            File.WriteAllText(res, line);
            if (s.result == BuildResult.Succeeded) Debug.Log("[Build] " + line + " → " + Path.GetFullPath(opt.locationPathName));
            else Debug.LogError("[Build] " + line);
        }
    }
}
