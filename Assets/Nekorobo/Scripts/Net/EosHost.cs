// EOS の SDK を入れたときだけ（Tools/eos_setup.ps1 が Assets/csc.rsp で NEKOROBO_EOS を立てる）
#if NEKOROBO_EOS
using System;
using System.IO;
using System.Runtime.InteropServices;
using Epic.OnlineServices;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Platform;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nekorobo
{
    /// <summary>
    /// EOS（Epic Online Services）の**立ち上げと後片付け**だけを持つ。通信の中身は NetEos。
    ///
    /// **鍵（eos.json）**：productId / sandboxId / deploymentId / clientId / clientSecret。
    ///   探す順：StreamingAssets/eos.json（EXE に同梱した物）→ プロジェクトの直下の eos.json（エディタ用）。
    ///   GitHub には上げない（.gitignore 済み）。書き出すときに BuildWin が EXE へ写す。
    ///
    /// **エディタでの決まり**（SDK の決まりで、Initialize は DLL を読んでから1回しかできない）
    ///   再生のたびに DLL を LoadLibrary で読み、Bindings.Hook で関数をつなぐ。再生を止めるとき・コンパイルし直す前に
    ///   Shutdown → Unhook → FreeLibrary で DLL ごと外す（Editor/NetReset.cs）。外さないと次の再生で始められない。
    ///   EXE では DLL は Unity が普通に読む（Plugins/x86_64）。
    ///
    /// **同じ PC で2人を試す**：端末IDは Windows のユーザーごとに1つなので、そのままでは同じ人になる。
    /// 2つ目の方を起動の引数 -eosnewid で起こすと、端末IDを作り直して別人になる（NetEos.Connect）。
    /// </summary>
    public static class EosHost
    {
        public static PlatformInterface P { get; private set; }
        /// <summary>始められなかった理由（画面に出す）。</summary>
        public static string Why = "";

#if UNITY_EDITOR
        static IntPtr lib = IntPtr.Zero;
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32", SetLastError = true)] static extern bool FreeLibrary(IntPtr h);
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string name);
        const string DLL = "Assets/Plugins/EOS/Bin/EOSSDK-Win64-Shipping.dll";
#endif

        /// <summary>鍵を読む。無ければ null。</summary>
        public static JObject Keys()
        {
            var cands = new[]
            {
                Path.Combine(Application.streamingAssetsPath, "eos.json"),
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "eos.json")),
            };
            foreach (var f in cands)
                if (File.Exists(f))
                    try { return JObject.Parse(File.ReadAllText(f)); }
                    catch (Exception e) { Debug.LogError("[EOS] eos.json が読めません: " + f + "\n" + e.Message); }
            return null;
        }

        /// <summary>始める（もう始まっていれば何もしない）。うまくいかなければ false と Why。</summary>
        public static bool Init()
        {
            if (P != null) return true;
            Why = "";
            var k = Keys();
            if (k == null) { Why = "eos.json（EOS の鍵）がありません"; return false; }
            string S(string key) { return ((string)k[key] ?? "").Trim(); }
            if (S("productId").Length == 0 || S("clientId").Length == 0 || S("clientSecret").Length == 0)
            { Why = "eos.json に足りない値があります"; return false; }

#if UNITY_EDITOR
            if (lib == IntPtr.Zero)
            {
                lib = LoadLibrary(Path.GetFullPath(DLL));
                if (lib == IntPtr.Zero) { Why = "EOS の DLL が読めません（" + Marshal.GetLastWin32Error() + "）"; return false; }
                Bindings.Hook(lib, (h, name) => GetProcAddress(h, name));
            }
#endif
            var io = new InitializeOptions { ProductName = "Nekorobo", ProductVersion = Application.version };
            var r = PlatformInterface.Initialize(ref io);
            if (r != Result.Success && r != Result.AlreadyConfigured)
            { Why = "EOS を始められません（Initialize: " + r + "）"; Teardown(false); return false; }

            LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Warning);
            LoggingInterface.SetCallback((ref LogMessage m) =>
            {
                string s = "[EOS] " + m.Category + ": " + m.Message;
                if (m.Level <= LogLevel.Error) Debug.LogError(s); else Debug.LogWarning(s);
            });

            // 置き場（SDK の控え）。端末IDはここではなく Windows のユーザーごとに持つので、ここを分けても別人にはならない
            string cache = Path.Combine(Application.temporaryCachePath, "eos");
            Directory.CreateDirectory(cache);

            var flags = PlatformFlags.DisableOverlay | PlatformFlags.DisableSocialOverlay;
#if UNITY_EDITOR
            flags |= PlatformFlags.LoadingInEditor;
#endif
            var po = new Options
            {
                ProductId = S("productId"),
                SandboxId = S("sandboxId"),
                DeploymentId = S("deploymentId"),
                ClientCredentials = new ClientCredentials { ClientId = S("clientId"), ClientSecret = S("clientSecret") },
                IsServer = false,
                Flags = flags,
                CacheDirectory = cache,
                TickBudgetInMilliseconds = 0,
            };
            P = PlatformInterface.Create(ref po);
            if (P == null) { Why = "EOS を始められません（鍵が違うかもしれません）"; Teardown(false); return false; }
            return true;
        }

        /// <summary>EXE を閉じるときに畳む（エディタでは再生を止めるとき NetReset も畳む。2回呼んでもよい）。</summary>
        [RuntimeInitializeOnLoadMethod]
        static void HookQuit() { Application.quitting -= OnQuit; Application.quitting += OnQuit; }
        static void OnQuit() { Net.Close(); Shutdown(); }

        /// <summary>毎フレーム回す（Net.Pump から）。</summary>
        public static void Tick() { if (P != null) P.Tick(); }

        /// <summary>
        /// 畳む。EXE では終わるとき、エディタでは再生を止めるときとコンパイルし直す前に呼ぶ。
        /// 止める直前にもう少しだけ回して、抜けた知らせ（部屋から抜けるなど）を出し切る。
        /// </summary>
        public static void Shutdown()
        {
            if (P != null)
            {
                for (int i = 0; i < 20; i++) { P.Tick(); System.Threading.Thread.Sleep(10); }
                P.Release();
                P = null;
            }
            Teardown(true);
        }

        /// <summary>
        /// SDK を閉じる。**EXE では Shutdown のあと二度と始められない**ので、終わるとき（final）だけ閉じる。
        /// エディタは DLL ごと外すので、次に読んだときにまた始められる。
        /// </summary>
        static void Teardown(bool final)
        {
#if UNITY_EDITOR
            if (lib == IntPtr.Zero) return;
            PlatformInterface.Shutdown();
            Bindings.Unhook();
            FreeLibrary(lib);
            lib = IntPtr.Zero;
#else
            if (final) PlatformInterface.Shutdown();
#endif
        }
    }
}
#else
namespace Nekorobo
{
    /// <summary>EOS の SDK が無いとき（NEKOROBO_EOS が無い）の空の物。畳む口だけある。</summary>
    public static class EosHost
    {
        public static void Shutdown() { }
    }
}
#endif
