using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadBugDelivery.Editor
{
    public static class DeadBugDeliverySceneBuilder
    {
        [MenuItem("Tools/Dead Bug Delivery/Create Independent Game Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (Enumerable.Range(0,SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty)) throw new InvalidOperationException("Save open scenes first.");
            string path = "Assets/Scenes/" + DeadBugDeliveryGame.SceneName + ".unity";
            if (File.Exists(path)) throw new InvalidOperationException("The new scene already exists; it will not be overwritten: " + path);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                if (!AssetDatabase.CopyAsset("Assets/Scenes/" + DeadBugDeliveryGame.OriginalSceneName + ".unity",path)) throw new IOException("Could not copy original dead bug scene.");
                var scene = EditorSceneManager.OpenScene(path); var roots = scene.GetRootGameObjects();
                T Find<T>() where T : Component => roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).First();
                var entry = Find<GameWorkoutScene>(); var skin = Find<RhythmSkinCVisuals>(); var view = Find<RingSkinCCoachView>();
                var root = new GameObject("Dead Bug Delivery Game"); var game = root.AddComponent<DeadBugDeliveryGame>();
                game.font = entry.font; game.artwork = skin.artwork; game.artworkCamera = skin.presentationCamera; game.displayMaterial = skin.displayMaterial;
                game.coachAnimator = view.previewAnimator; game.coachCamera = view.previewCamera; game.userAnimator = entry.scorer.UserAnimator;
                if (game.userAnimator == null || !game.userAnimator.isHuman || game.userAnimator == game.coachAnimator) throw new Exception("Separate valid user/coach rigs required.");
                foreach (var original in roots) original.SetActive(false);
                // The host's global bootstrap searches inactive rhythm controllers.
                // Removing them from the COPY prevents an extra receiver/scoring system.
                foreach (var controller in roots.SelectMany(r => r.GetComponentsInChildren<RhythmModeController>(true))) UnityEngine.Object.DestroyImmediate(controller);
                Rescue(game.artwork.transform,root.transform); Rescue(game.artworkCamera.transform,root.transform);
                Rescue(game.coachAnimator.transform,root.transform); Rescue(game.coachCamera.transform,root.transform);
                Rescue(game.userAnimator.transform,root.transform);
                foreach (var behaviour in game.artwork.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var rig in new[] { game.userAnimator,game.coachAnimator })
                {
                    foreach (var behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                    rig.runtimeAnimatorController = null; rig.enabled = false; rig.applyRootMotion = false;
                }
                foreach (var source in root.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(listener);
                var hud = game.artwork.transform.Find("HUD"); if (hud != null) hud.gameObject.SetActive(false);
                game.userCamera = UnityEngine.Object.Instantiate(game.coachCamera,root.transform); game.userCamera.name = "User pose camera";
                Frame(game.coachCamera,game.coachAnimator,29); Frame(game.userCamera,game.userAnimator,28);
                foreach (var node in game.artwork.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
                game.artworkCamera.targetTexture = null; game.artworkCamera.cullingMask = 1 << 30; game.artworkCamera.orthographic = true;
                game.artworkCamera.clearFlags = CameraClearFlags.SolidColor; game.artworkCamera.backgroundColor = new Color(.03f,.03f,.1f);
                game.artworkCamera.transform.rotation = Quaternion.identity;
                game.artworkCamera.transform.position = game.artwork.transform.position + new Vector3(0,0,-10);
                game.artworkCamera.orthographicSize = game.artwork.transform.lossyScale.x * 2.4f;
                game.ring = root.AddComponent<DeadBugRingPresentation>(); game.ring.artwork = game.artwork; game.ring.font = game.font;
                game.ring.spriteMaterial = skin.spriteMaterial; game.ring.noteBodies = skin.noteBodies;
                var archive = new GameObject("Original copied contents (inactive)"); archive.SetActive(false);
                foreach (var original in roots) if (original != null && original.transform.parent == null) original.transform.SetParent(archive.transform,true);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                if (!EditorBuildSettings.scenes.Any(s => s.path == path))
                    EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(path,true) }).ToArray();
                AssetDatabase.SaveAssets(); Debug.Log("DEADBUG_DELIVERY_CREATED " + path);
            }
            finally
            {
                if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path) && File.Exists(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }
        static void Frame(Camera camera,Animator avatar,int layer)
        {
            foreach (var node in avatar.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
            foreach (var behaviour in camera.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
            var framing = camera.GetComponent<DeadBugPreviewFraming>() ?? camera.gameObject.AddComponent<DeadBugPreviewFraming>();
            framing.avatar = avatar; framing.previewCamera = camera; framing.enabled = true;
            framing.followBodyHeading = layer == 28;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.targetTexture = null; camera.cullingMask = 1 << layer;
        }
        public static void FinalizeNewScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + DeadBugDeliveryGame.SceneName + ".unity");
            var game = UnityEngine.Object.FindObjectOfType<DeadBugDeliveryGame>();
            Frame(game.coachCamera,game.coachAnimator,29); Frame(game.userCamera,game.userAnimator,28);
            var installer = game.GetComponent<SkinnedMeshColorAnchorInstaller>();
            if (installer != null) UnityEngine.Object.DestroyImmediate(installer);
            game.artworkCamera.orthographicSize = game.artwork.transform.lossyScale.x * 2.4f;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        static void Rescue(Transform item,Transform parent)
        {
            var prefab = PrefabUtility.GetOutermostPrefabInstanceRoot(item.gameObject);
            if (prefab != null) PrefabUtility.UnpackPrefabInstance(prefab,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            item.SetParent(parent,true); item.gameObject.SetActive(true);
        }
    }
}
