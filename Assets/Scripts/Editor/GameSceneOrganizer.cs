using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Names the five game scenes without reserializing any existing scene.</summary>
public static class GameSceneOrganizer
{
    [MenuItem("Tools/Ring Skin C/Organize Games and Create BirdDog")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before organizing scenes.");

        Move("Assets/Scenes/SpineV2Minimal.unity", DeadBugRingSkinCBuilder.Source);
        Move("Assets/Scenes/BirdDogMinimal.unity", DeadBugRingSkinCBuilder.BirdDogSource);
        Move("Assets/Scenes/HipThrustMinimal.unity", "Assets/Scenes/HipThrust.unity");
        Move("Assets/Scenes/DeadBugRingSkinC.unity", DeadBugRingSkinCBuilder.Destination);
        if (!AssetDatabase.IsValidFolder("Assets/Scenes/Tests")) AssetDatabase.CreateFolder("Assets/Scenes", "Tests");
        Move("Assets/Scenes/DeadBugRhythmHapticsTest.unity", "Assets/Scenes/Tests/DeadBugRhythmHapticsTest.unity");
        Move("Assets/Scenes/DeadBugRhythmHapticsTest 1.unity", "Assets/Scenes/Tests/DeadBugRhythmHapticsTest 1.unity");

        if (!File.Exists(DeadBugRingSkinCBuilder.BirdDogDestination)) DeadBugRingSkinCBuilder.BuildBirdDog();

        // Preserve the three original game indices and the existing new DeadBug index (4).
        string[] games = {
            DeadBugRingSkinCBuilder.Source, DeadBugRingSkinCBuilder.BirdDogSource,
            "Assets/Scenes/HipThrust.unity", DeadBugRingSkinCBuilder.BirdDogDestination,
            DeadBugRingSkinCBuilder.Destination
        };
        var tests = Directory.GetFiles("Assets/Scenes/Tests", "*.unity")
            .OrderBy(p => p).Select(p => new EditorBuildSettingsScene(p.Replace('\\', '/'), false));
        EditorBuildSettings.scenes = games.Select(p => new EditorBuildSettingsScene(p, true)).Concat(tests).ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("GAME_SCENES_ORGANIZED: five enabled games; original scene bytes and GUIDs preserved.");
    }

    private static void Move(string source, string destination)
    {
        if (!File.Exists(source))
        {
            if (!File.Exists(destination)) throw new FileNotFoundException("Missing scene", source);
            return;
        }
        if (File.Exists(destination)) throw new IOException("Refusing to replace existing scene: " + destination);
        byte[] bytes = File.ReadAllBytes(source);
        string guid = AssetDatabase.AssetPathToGUID(source);
        string error = AssetDatabase.MoveAsset(source, destination);
        if (!string.IsNullOrEmpty(error)) throw new IOException(error);
        if (!bytes.SequenceEqual(File.ReadAllBytes(destination)) || AssetDatabase.AssetPathToGUID(destination) != guid)
            throw new IOException("Scene preservation check failed: " + destination);
    }
}
