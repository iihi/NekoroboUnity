using UnityEditor;
using UnityEditorInternal;

namespace Nekorobo.EditorTools
{
    /// <summary>
    /// **確かめる用。** Unity が前に出ていない（ほかのアプリを触っている）と、遊んでいる途中でも
    /// 1フレームも進まなくなる。外からコマンドで動かして確かめるときに困るので、
    /// 入にしてあるときだけ、裏に回ったらゲームのループを回し続けるよう頼む（QueuePlayerLoopUpdate）。
    ///
    /// 既定は切。メニューの「Nekorobo/裏でも遊びを進める（確かめる用）」で切り替える。
    /// 自分で一時停止したときは何もしない（一時停止中はループを回さない）。
    /// </summary>
    [InitializeOnLoad]
    public static class BackgroundStep
    {
        const string Key = "nekorobo.bgstep";
        const string Menu = "Nekorobo/裏でも遊びを進める（確かめる用）";
        static bool stepping;

        static BackgroundStep() { EditorApplication.update += Tick; }

        public static bool On
        {
            get { return EditorPrefs.GetBool(Key, false); }
            set { EditorPrefs.SetBool(Key, value); }
        }

        [MenuItem(Menu)]
        static void Toggle() { On = !On; }

        [MenuItem(Menu, true)]
        static bool ToggleCheck() { UnityEditor.Menu.SetChecked(Menu, On); return true; }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { stepping = false; return; }
            // 前の版の「一時停止して1コマ送る」が残っていたら戻す
            if (stepping) { EditorApplication.isPaused = false; stepping = false; }
            if (!On || InternalEditorUtility.isApplicationActive) return;
            // **一時停止して EditorApplication.Step() で送るのはやめた。**1コマ送るたびにエディタの一時メモリが
            // 解放されずに残り（ALLOC_TEMP_MAIN has unfreed allocations）、何万回も送ったあとにコンパイルし直すと、
            // エディタが「Reloading Domain」のまま止まった（3回起きた）。
            // いまは「もう1回ゲームのループを回して」と頼むだけ（一時停止はしない）。
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
