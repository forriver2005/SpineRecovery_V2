using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TaikoNight.EditorTools
{
    public static class TaikoNightPracticeBuilder
    {
        private const string CrunchSource = "Assets/Scenes/CrunchPractice.unity";
        private const string CrunchDestination = "Assets/Scenes/CrunchPractice_TaikoNight.unity";
        private const string CatSource = "Assets/Scenes/LyingCatPractice.unity";
        private const string CatDestination = "Assets/Scenes/LyingCatPractice_TaikoNight.unity";
        private const string PrefabPath = "Assets/TaikoNight/Prefabs/TaikoNight.prefab";
        private const int TaikoLayer = 30;

        [MenuItem("Tools/Taiko Night/Build Crunch Practice")]
        public static void BuildCrunch() => Build(CrunchSource, CrunchDestination, "动态卷腹 · 星轨敲鼓", TaikoNightActionKind.Crunch);

        [MenuItem("Tools/Taiko Night/Build Lying Cat Practice")]
        public static void BuildLyingCat() => Build(CatSource, CatDestination, "动态卧位猫 · 星轨敲鼓", TaikoNightActionKind.LyingCat);

        [MenuItem("Tools/Taiko Night/Add Crunch And Lying Cat To Build Settings")]
        public static void AddToBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            Add(scenes, CrunchDestination);
            Add(scenes, CatDestination);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void Add(System.Collections.Generic.List<EditorBuildSettingsScene> scenes, string path)
        {
            for (int i = 0; i < scenes.Count; i++) if (scenes[i].path == path) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        private static void Build(string source, string destination, string title, TaikoNightActionKind actionKind)
        {
            if (!File.Exists(source)) throw new FileNotFoundException(source);
            if (!File.Exists(PrefabPath)) throw new FileNotFoundException(PrefabPath);
            if (File.Exists(destination))
            {
                Debug.Log("Taiko Night practice scene already exists. Delete it before rebuilding: " + destination);
                return;
            }
            File.Copy(source, destination);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(destination, OpenSceneMode.Single);
            var menu = Object.FindObjectOfType<DeadBugStartMenuController>(true);
            if (menu == null) throw new MissingReferenceException("Practice scene is missing its start menu.");

            foreach (var root in scene.GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(node.gameObject);
            var legacyScorer = Object.FindObjectOfType<PoseScorer>(true);
            if (legacyScorer != null) legacyScorer.enabled = false;
            var practice = Object.FindObjectOfType<PracticeSessionController>();
            if (practice != null) practice.enabled = false;

            var taikoRoot = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene) as GameObject;
            taikoRoot.name = title + " Art";
            SetLayerRecursively(taikoRoot, TaikoLayer);
            var cameraObject = new GameObject(title + " Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.layer = TaikoLayer;
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.04f, 0.15f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = 2.7f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.depth = 100f;
            camera.cullingMask = 1 << TaikoLayer;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            var presentation = cameraObject.AddComponent<TaikoNightActionRuntime>();
            presentation.actionKind = actionKind;
            presentation.menu = menu;
            presentation.coach = Object.FindObjectOfType<SegmentedCoachController>(true);
            presentation.recorder = Object.FindObjectOfType<MotionRecorder>(true);
            presentation.taikoRoot = taikoRoot;
            presentation.presentationCamera = camera;
            presentation.title = title;
            presentation.font = FindFont(practice);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Built " + destination);
        }

        private static TMPro.TMP_FontAsset FindFont(PracticeSessionController practice)
        {
            var progress = Object.FindObjectOfType<CoachSetProgressUI>(true);
            if (progress != null && progress.ProgressText != null) return progress.ProgressText.font;
            return AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                "Assets/Fonts/SourceHanSans/SourceHanSansSC-Heavy SDF.asset");
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
