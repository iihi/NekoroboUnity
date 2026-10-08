using UnityEditor;
using UnityEditorInternal;

namespace Nekorobo.EditorTools
{
    /// <summary>
    /// **確かめる用。** Unity が前に出ていない（ほかのアプリを触っている）と、遊んでいる途中でも
    /// 1フレームも進まなくなる。外からコマンドで動かして確かめるときに困るので、
    /// 入にしてあるときだけ、裏に回ったら1フレームずつ送って進める（実時間より遅くなる）。
    /// Unity を前に戻したら、ふつうの再生に戻す。
    ///
    /// 既定は切。メニューの「Nekorobo/裏でも遊びを進める（確かめる用）」で切り替える。
    /// 自分で一時停止したときは送らない（送るのは、ここで止めたときだけ）。
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
            bool active = InternalEditorUtility.isApplicationActive;
            if (!On || active)
            {
                if (stepping) { EditorApplication.isPaused = false; stepping = false; }
                return;
            }
            if (!EditorApplication.isPaused) { EditorApplication.isPaused = true; stepping = true; }
            if (stepping) EditorApplication.Step();
        }
    }
}
