using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameTutorialBuilder
{
    const string Root = "Assets/Tutorials/";
    [Serializable] sealed class Catalog { public LessonData[] lessons; }
    [Serializable] sealed class LessonData { public TutorialKind kind; public string title, defaultGame; public TutorialStep[] steps; }

    [MenuItem("Tools/Tutorials/Build Three New Tutorial Scenes")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before building tutorials.");
        if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Save current work before building tutorial copies.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Root + "Narration.zh-CN.json"));
        string[] sources = { "BirdDog_RingSkinC_Recorded", "HipThrust_RingBridge_NewScoring", "HipThrust_RingBridge_Hold30" };
        Directory.CreateDirectory(Root + "Lessons");
        AssetDatabase.Refresh();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                string path = "Assets/Scenes/" + TutorialPauseNavigation.Scenes[i] + ".unity";
                if (File.Exists(path)) throw new InvalidOperationException("Refusing to overwrite an existing tutorial: " + path);
                if (!AssetDatabase.CopyAsset("Assets/Scenes/" + sources[i] + ".unity", path)) throw new IOException("Scene copy failed.");
                var scene = EditorSceneManager.OpenScene(path);
                var roots = scene.GetRootGameObjects();
                T Find<T>() where T : Component => roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).First();
                var entry = Find<GameWorkoutScene>();
                var root = new GameObject("Automatic Tutorial");
                var player = root.AddComponent<GameTutorialPlayer>(); player.font = entry.font;
                var data = catalog.lessons[i];
                var lesson = ScriptableObject.CreateInstance<TutorialLesson>();
                lesson.kind = data.kind; lesson.title = data.title; lesson.defaultGame = data.defaultGame; lesson.steps = data.steps;
                foreach (var step in lesson.steps)
                {
                    string audioPath = Root + "Audio/" + step.id + ".wav";
                    var importer = (AudioImporter)AssetImporter.GetAtPath(audioPath);
                    var settings = importer.defaultSampleSettings; settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis; settings.quality = .85f; importer.defaultSampleSettings = settings;
                    importer.forceToMono = true; importer.SaveAndReimport();
                    step.voice = AssetDatabase.LoadAssetAtPath<AudioClip>(audioPath);
                    if (step.voice == null || step.voice.length < 1) throw new Exception("Missing narration: " + step.id);
                }
                AssetDatabase.CreateAsset(lesson, Root + "Lessons/" + TutorialPauseNavigation.Scenes[i] + ".asset"); player.lesson = lesson;
                if (i == 0)
                {
                    var skin = Find<RhythmSkinCVisuals>(); var view = Find<RingSkinCCoachView>();
                    player.artwork = skin.artwork; player.artworkCamera = skin.presentationCamera;
                    player.spriteMaterial = skin.spriteMaterial; player.displayMaterial = skin.displayMaterial; player.noteBodies = skin.noteBodies;
                    player.coachAnimator = view.previewAnimator; player.coachCamera = view.previewCamera;
                }
                else
                {
                    var view = Find<HipThrustRingBridgeView>();
                    player.drumArtwork = Find<RecordedDrumArtwork>();
                    player.artwork = view.artwork; player.artworkCamera = view.presentationCamera; player.displayMaterial = view.displayMaterial;
                    player.bridgeChart = view.chart; player.bridgeTimeline = entry.taiko.PhaseTimeline;
                    player.coachAnimator = (Animator)new SerializedObject(entry.taiko).FindProperty("coachPreviewAnimator").objectReferenceValue;
                    player.coachCamera = roots.SelectMany(r => r.GetComponentsInChildren<HipThrustPreviewFraming>(true))
                        .First(f => f.avatar == player.coachAnimator).previewCamera;
                }
                // Keep the complete copied scene for reference, but never activate its tracking,
                // calibration, training flow, audio, networking or result writers in a tutorial.
                foreach (var original in roots) original.SetActive(false);
                // The global VMC bootstrap searches inactive objects too. Removing the unused
                // rhythm controllers from this copy prevents it from starting a tracking receiver.
                foreach (var controller in roots.SelectMany(r => r.GetComponentsInChildren<RhythmModeController>(true)))
                    UnityEngine.Object.DestroyImmediate(controller);
                Rescue(player.artwork.transform, root.transform);
                Rescue(player.artworkCamera.transform, root.transform);
                Rescue(player.coachAnimator.transform, root.transform);
                Rescue(player.coachCamera.transform, root.transform);
                foreach (var behaviour in player.artwork.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(behaviour is RingBridge.RingBridgeChart)) behaviour.enabled = false;
                foreach (var behaviour in player.coachAnimator.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var source in root.GetComponentsInChildren<AudioSource>(true)) source.enabled = false;
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(listener);
                var hud = player.artwork.transform.Find("HUD"); if (hud != null) hud.gameObject.SetActive(false);
                if (player.bridgeChart != null) player.bridgeChart.autoPlay = false;
                var framing = player.coachCamera.GetComponent<HipThrustPreviewFraming>() ?? player.coachCamera.gameObject.AddComponent<HipThrustPreviewFraming>();
                framing.avatar = player.coachAnimator; framing.previewCamera = player.coachCamera; framing.enabled = true;
                player.coachCamera.clearFlags = CameraClearFlags.SolidColor; player.coachCamera.backgroundColor = Color.clear;
                player.coachCamera.targetTexture = null; player.coachCamera.cullingMask = 1 << 29;
                foreach (var node in player.coachAnimator.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 29;
                foreach (var node in player.artwork.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
                player.artworkCamera.targetTexture = null; player.artworkCamera.cullingMask = 1 << 30;
                player.artworkCamera.clearFlags = CameraClearFlags.SolidColor; player.artworkCamera.backgroundColor = new Color(.03f, .03f, .1f);
                player.artworkCamera.orthographic = true;
                player.artworkCamera.transform.rotation = Quaternion.identity;
                player.artworkCamera.transform.position = player.artwork.transform.position + new Vector3(0, 0, -10);
                player.artworkCamera.orthographicSize = player.artwork.transform.lossyScale.x * (i == 0 ? 2.15f : 1.95f);
                root.AddComponent<SkinnedMeshColorAnchorInstaller>();
                // Isolate tutorial cameras from the still-loaded, paused game at the origin.
                root.transform.position += new Vector3(10000, 0, 0);
                var archive = new GameObject("Copied original scene (inactive)"); archive.SetActive(false);
                foreach (var original in roots) if (original != null && original.transform.parent == null) original.transform.SetParent(archive.transform, true);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                Debug.Log("TUTORIAL_CREATED " + path + " from " + sources[i]);
            }
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(s => !TutorialPauseNavigation.Scenes.Any(n => s.path == "Assets/Scenes/" + n + ".unity"))
                .Concat(TutorialPauseNavigation.Scenes.Select(name => new EditorBuildSettingsScene("Assets/Scenes/" + name + ".unity", true))).ToArray();
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path) && File.Exists(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
    static void Rescue(Transform item, Transform parent)
    {
        var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(item.gameObject);
        if (instance != null) PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        item.SetParent(parent, true); item.gameObject.SetActive(true);
    }
}
