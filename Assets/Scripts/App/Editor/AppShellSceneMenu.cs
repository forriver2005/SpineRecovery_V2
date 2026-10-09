#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpineRecovery.App.Editor
{
    public static class AppShellSceneMenu
    {
        private const string ScenePath = "Assets/Scenes/AppShell.unity";

        [MenuItem("Spine Recovery/Create AppShell Scene")]
        public static void CreateScene()
        {
            EnsureFolder("Assets/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject shell = new GameObject("AppShell");
            shell.AddComponent<AppBootstrap>();
            shell.AddComponent<AppShellUi>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].path == ScenePath)
                {
                    Selection.activeGameObject = shell;
                    return;
                }
            }

            EditorBuildSettingsScene[] updated = new EditorBuildSettingsScene[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[updated.Length - 1] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = updated;
            Selection.activeGameObject = shell;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }
        }
    }
}
#endif
