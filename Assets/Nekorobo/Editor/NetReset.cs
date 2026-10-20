using UnityEditor;

namespace Nekorobo.EditorTools
{
    /// <summary>
    /// **再生を止めるときと、コンパイルし直す前に、オンラインの通信を畳む。**
    /// 畳まずに残すと、受けている途中の WebSocket をつかんだまま、エディタが
    /// 「Reloading Domain」で止まって動かなくなった（実際に起きた）。
    /// EOS も畳んで DLL ごと外す（SDK は DLL を読んでから1回しか始められないため。EosHost）。
    /// 再生を止めるときは、マウスのカーソルも元に戻す（指カーソル Ptr）。
    /// </summary>
    [InitializeOnLoad]
    public static class NetReset
    {
        static NetReset()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => { Net.Reset(); EosHost.Shutdown(); };
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingPlayMode)
                {
                    Net.Reset();
                    EosHost.Shutdown();                  // EOS は DLL ごと外す（次の再生でまた始められるように）
                    // 指カーソルのまま・消したままで止めると、エディタのカーソルもそのまま残る
                    UnityEngine.Cursor.SetCursor(null, UnityEngine.Vector2.zero, UnityEngine.CursorMode.Auto);
                    UnityEngine.Cursor.visible = true;
                }
            };
        }
    }
}
