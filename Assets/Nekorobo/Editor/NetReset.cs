using UnityEditor;

namespace Nekorobo.EditorTools
{
    /// <summary>
    /// **再生を止めるときと、コンパイルし直す前に、オンラインの通信を畳む。**
    /// 畳まずに残すと、受けている途中の WebSocket をつかんだまま、エディタが
    /// 「Reloading Domain」で止まって動かなくなった（実際に起きた）。
    /// </summary>
    [InitializeOnLoad]
    public static class NetReset
    {
        static NetReset()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Net.Reset;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingPlayMode) Net.Reset();
            };
        }
    }
}
