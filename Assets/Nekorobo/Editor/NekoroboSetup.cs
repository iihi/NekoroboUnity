using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nekorobo.EditorTools
{
    /// <summary>
    /// 初めて開いたときに、遊ぶためのシーン（Assets/Scenes/Main.unity）を作る。
    /// シーンに置くのは「Game」1つとカメラと光だけ。床もロボも遊び始めたときに作る。
    /// メニューの「Nekorobo/シーンを作り直す」でいつでも作り直せる。
    /// </summary>
    [InitializeOnLoad]
    public static class NekoroboSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        static NekoroboSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode) return;
                if (!System.IO.File.Exists(ScenePath)) MakeScene();
            };
        }

        [MenuItem("Nekorobo/シーンを作り直す")]
        public static void MakeScene()
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            var sun = new GameObject("Sun");
            var l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            var g = new GameObject("Game");
            g.AddComponent<Game>();
            EditorSceneManager.SaveScene(sc, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[Nekorobo] シーンを作りました: " + ScenePath);
        }

        [MenuItem("Nekorobo/HTML版のフォルダを開く")]
        static void OpenData() { EditorUtility.RevealInFinder(DataRoot.Path); }
    }
}
