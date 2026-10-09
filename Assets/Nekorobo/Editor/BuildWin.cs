using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Nekorobo.EditorTools
{
    /// <summary>
    /// Windows の EXE に書き出す。メニューは2つ。
    ///
    ///   「Windows で書き出す」          … このPC用。Builds/Win/Nekorobo.exe。データは**同梱しない**
    ///                                    （HTML版のフォルダ nekorobo3d を直接読むので、面や数値を直せば EXE にもすぐ効く）
    ///   「Windows で書き出す（配る用）」 … ほかの人に渡す用。Builds/WinShare に書き出し、
    ///                                    **HTML版のデータを EXE の中（StreamingAssets/nekorobo3d）へ写して**、
    ///                                    Builds/Nekorobo_Win_日付.zip にまとめる。zip を渡して、展開して Nekorobo.exe を起動するだけ
    ///
    /// DataRoot は StreamingAssets/nekorobo3d を HTML版のフォルダより先に探すので、同梱した物があればそれを読む。
    /// 結果は書き出し先の build_result.txt にも書く（外から待つ用）。
    /// </summary>
    public static class BuildWin
    {
        public const string Dir = "Builds/Win";
        public const string ShareDir = "Builds/WinShare";

        // 同梱する物（HTML版のフォルダから）。Unity が読むのはこれだけ（DataRoot を使っている所）
        static readonly string[] DataDirs = { "stages", "assets" };
        static readonly string[] DataFiles = { "tune.json", "configs.js", "tiles.js", "net.json" };

        [MenuItem("Nekorobo/Windows で書き出す")]
        public static void Build() { Run(Dir, false); }

        [MenuItem("Nekorobo/Windows で書き出す（配る用）")]
        public static void BuildShare() { Run(ShareDir, true); }

        static void Run(string dir, bool share)
        {
            Directory.CreateDirectory(dir);
            var res = Path.Combine(dir, "build_result.txt");
            if (File.Exists(res)) File.Delete(res);
            var opt = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = Path.Combine(dir, "Nekorobo.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var rep = BuildPipeline.BuildPlayer(opt);
            var s = rep.summary;
            string line = s.result + " " + s.totalTime.TotalSeconds.ToString("0") + "s " + (s.totalSize / (1024 * 1024)) + "MB errors=" + s.totalErrors + " warnings=" + s.totalWarnings;
            if (s.result == BuildResult.Succeeded && share)
            {
                try { line += " | " + Pack(dir); }
                catch (System.Exception e) { line = "Failed（同梱・zip）: " + e.Message; Debug.LogException(e); }
            }
            File.WriteAllText(res, line);
            if (line.StartsWith("Succeeded")) Debug.Log("[Build] " + line + " → " + Path.GetFullPath(opt.locationPathName));
            else Debug.LogError("[Build] " + line);
        }

        /// <summary>データを同梱して zip にする。返すのは zip の場所と大きさ。</summary>
        static string Pack(string dir)
        {
            // ---- データを EXE の中へ写す（前の分は消してから）
            var dst = Path.Combine(dir, "Nekorobo_Data", "StreamingAssets", "nekorobo3d");
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            Directory.CreateDirectory(dst);
            string src = DataRoot.Path;
            int n = 0;
            foreach (var d in DataDirs) n += CopyDir(Path.Combine(src, d), Path.Combine(dst, d));
            foreach (var f in DataFiles)
            {
                var a = Path.Combine(src, f);
                if (File.Exists(a)) { File.Copy(a, Path.Combine(dst, f), true); n++; }
            }

            // ---- zip にまとめる。デバッグ用の書き出し（DoNotShip）と結果のメモは入れない
            var zip = Path.Combine("Builds", "Nekorobo_Win_" + System.DateTime.Now.ToString("yyyyMMdd_HHmm") + ".zip");
            if (File.Exists(zip)) File.Delete(zip);
            using (var za = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                string root = Path.GetFullPath(dir);
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(root.Length + 1).Replace('\\', '/');
                    if (rel.Contains("_DoNotShip") || rel == "build_result.txt") continue;
                    za.CreateEntryFromFile(f, "Nekorobo/" + rel, System.IO.Compression.CompressionLevel.Optimal);
                }
            }
            long mb = new FileInfo(zip).Length / (1024 * 1024);
            return "データ " + n + " ファイルを同梱 → " + Path.GetFullPath(zip) + "（" + mb + "MB）";
        }

        static int CopyDir(string from, string to)
        {
            if (!Directory.Exists(from)) return 0;
            int n = 0;
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) { File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true); n++; }
            foreach (var d in Directory.GetDirectories(from)) n += CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
            return n;
        }
    }
}
