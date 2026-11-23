using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TaikoNight.EditorTools
{
    public static class TaikoNightBridgeBuilder
    {
        private const string SourceScene = "Assets/Scenes/HipThrust_RingBridge_NewScoring.unity";
        private const string DestinationScene = "Assets/Scenes/HipThrust_TaikoNight_Dynamic.unity";
        private const string PrefabPath = "Assets/TaikoNight/Prefabs/TaikoNight.prefab";
        private const int TaikoLayer = 30;

        [MenuItem("Tools/Taiko Night/Build Dynamic Hip Bridge")]
        public static void Build()
        {
            if (!File.Exists(SourceScene)) throw new FileNotFoundException(SourceScene);
            if (!File.Exists(PrefabPath)) throw new FileNotFoundException(PrefabPath);
            if (File.Exists(DestinationScene))
            {
                Debug.Log("Taiko Night dynamic bridge already exists. Delete it before rebuilding: " + DestinationScene);
                return;
            }

            File.Copy(SourceScene, DestinationScene);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(DestinationScene, OpenSceneMode.Single);
            var entry = Object.FindObjectOfType<GameWorkoutScene>();
            var transport = Object.FindObjectOfType<HipThrustTaikoController>();
            if (entry == null || transport == null) throw new MissingReferenceException("The copied hip-thrust scene is missing its game entry or controller.");

            var oldView = Object.FindObjectOfType<HipThrustRingBridgeView>();
            if (oldView != null)
            {
                oldView.enabled = false;
                if (oldView.artwork != null) oldView.artwork.SetActive(false);
            }
            if (entry.gameCanvas != null)
            {
                var group = entry.gameCanvas.GetComponent<CanvasGroup>();
                if (group == null) group = entry.gameCanvas.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            var taikoRoot = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene) as GameObject;
            taikoRoot.name = "TaikoNight Dynamic Bridge Art";
            SetLayerRecursively(taikoRoot, TaikoLayer);
            var chart = taikoRoot.GetComponentInChildren<TaikoNightChart>(true);

            var cameraObject = new GameObject("TaikoNight Presentation Camera", typeof(Camera));
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

            var presentation = cameraObject.AddComponent<TaikoNightBridgePresentation>();
            presentation.transport = transport;
            presentation.entry = entry;
            presentation.chart = chart;
            presentation.presentationCamera = camera;
            presentation.oldCanvas = entry.gameCanvas;
            presentation.font = entry.font;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Built " + DestinationScene);
        }

        [MenuItem("Tools/Taiko Night/Add Dynamic Hip Bridge To Build Settings")]
        public static void AddToBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
                if (scenes[i].path == DestinationScene) return;
            scenes.Add(new EditorBuildSettingsScene(DestinationScene, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("Added " + DestinationScene + " to build settings.");
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
