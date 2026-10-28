using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Registers the original recorded-coach games and their new bridge variants.</summary>
public static class GameSceneOrganizer
{
    [MenuItem("Tools/Ring Skin C/Organize Game Scenes")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before organizing scenes.");

        var paths = new[] { RecordedCoachSceneTools.Recorded("BirdDog"),
            RecordedCoachSceneTools.Recorded("DeadBug"), RingBridgeGameBuilder.ScenePath,
            HipThrustNewRulesBuilder.NewScore, HipThrustNewRulesBuilder.Hold30 };
        foreach (string path in paths)
            if (!File.Exists(path)) throw new FileNotFoundException("Create the recorded-coach scenes first.", path);
        EditorBuildSettings.scenes = paths.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
        Debug.Log("GAME_SCENES_ORGANIZED: " + paths.Length + " games; existing scene files preserved.");
    }
}
