using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Batch-mode regression checks for the mobile/main merge, without patient data or devices.
public static class MainIntegrationVerification
{
    private static int checks;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in a batch-mode editor.");
        checks = 0;
        VerifyBuildScenes();
        VerifyTrackingScripts();
        VerifyTimelines();
        VerifyRoutes();
        VerifyFullCatalog();
        NavigationModuleVerification.Run();
        Debug.Log("MAIN_INTEGRATION_VERIFICATION_PASS checks=" + checks + " plus navigation regression checks");
    }

    private static void VerifyTrackingScripts()
    {
        const string adapter = "Assets/Scripts/SlimeVrAvatarMotionAdapter.cs";
        const string bridge = "Assets/Scripts/VmcGameModeSourceBridge.cs";
        Check(AssetDatabase.AssetPathToGUID(adapter) != AssetDatabase.AssetPathToGUID(bridge),
            "Mobile and VMC tracking scripts have independent GUIDs");
        Check(AssetDatabase.LoadAssetAtPath<MonoScript>(adapter).GetClass() == typeof(SlimeVrAvatarMotionAdapter),
            "Existing scene tracking references resolve to the mobile adapter");
        Check(AssetDatabase.LoadAssetAtPath<MonoScript>(bridge).GetClass() == typeof(VmcGameModeSourceBridge),
            "New VMC bridge resolves to its own script");
    }

    private static void VerifyBuildScenes()
    {
        var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
        Check(scenes.Length > 0 && scenes[0].path == "Assets/Scenes/AppShell.unity", "Mobile app remains the entry scene");
        Check(scenes.Select(scene => scene.path).Distinct().Count() == scenes.Length, "Build scenes are unique");
        foreach (var scene in scenes)
        {
            Check(File.Exists(scene.path), "Scene exists: " + scene.path);
            Check(new GUID(AssetDatabase.AssetPathToGUID(scene.path)) == scene.guid, "Scene GUID matches: " + scene.path);
        }
        foreach (string tutorial in TutorialPauseNavigation.Scenes)
            Check(scenes.Any(scene => Path.GetFileNameWithoutExtension(scene.path) == tutorial), "Tutorial is packaged: " + tutorial);
    }

    private static void VerifyTimelines()
    {
        var source = ScriptableObject.CreateInstance<HipThrustPhaseTimeline>();
        var clip = new AnimationClip { frameRate = 30 };
        clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 4, 0));
        source.sourceClip = clip;
        source.repeatStartFrame = 0;
        source.repeatEndFrame = 120;
        source.cycles = new[] { new HipThrustPhaseTimeline.Cycle {
            liftStartFrame = 0, holdStartFrame = 30, lowerStartFrame = 60, lowerEndFrame = 90,
            lowHeight = 0, peakHeight = 1
        } };
        source.recordedHeight = new AnimationCurve(new Keyframe(0, 0), new Keyframe(30, 1), new Keyframe(60, 1), new Keyframe(90, 0));
        try
        {
            foreach (float hold in new[] { 0f, 7f, 30f, 600f })
            {
                var timeline = new HipThrustHold30Timeline(source, 6, skipRecordedPlateauAfterHold: true,
                    holdSeconds: hold, extraRecordedHoldSeconds: 9);
                Check(timeline.Repetitions.Length == 6, "Prescription keeps total repetitions");
                float resolved = hold == 0 ? 30 : hold;
                Check(timeline.Repetitions.All(rep => Math.Abs(rep.lower - rep.top - resolved) < .00001),
                    "Main hold contract: zero defaults to 30s, explicit duration stays exact: " + hold);
                var rep = timeline.Repetitions[0];
                var sample = timeline.Evaluate(rep.lower + .1, out _, out _, out _);
                Check(sample.phase == 2, "Prescription descends after specified hold: " + hold);
                timeline.Evaluate(timeline.Duration, out _, out _, out int completed);
                Check(completed == 6, "Prescription ends after exact dose");
                if (hold > 0)
                {
                    sample = timeline.Evaluate(rep.top + hold / 2, out float frame, out _, out _);
                    Check(sample.phase == 1 && Math.Abs(frame - rep.peakFrame) < .00001, "Prescription freezes peak pose");
                }
            }
            var recorded = new HipThrustHold30Timeline(source, 3, true, false, true,
                extraRecordedHoldSeconds: 2);
            Check(recorded.UsesRecordedTiming && recorded.PreservesSourceGaps, "Taiko retains recording and wrap timing");
            Check(recorded.Repetitions.All(rep => Math.Abs(rep.lower - rep.top - 3) < .00001), "Taiko adds extension to recorded hold");
            var first = recorded.Repetitions[0];
            recorded.Evaluate(first.top + 1.5, out float midFrame, out _, out _);
            Check(Math.Abs(midFrame - 45) < .00001, "Taiko stretches recorded motion instead of freezing it");
            Check(Math.Abs(recorded.Repetitions[1].start - first.end - HipThrustHold30Timeline.WrapSeconds) < .00001,
                "Taiko preserves wrap gap");
            var defaultHold = new HipThrustHold30Timeline(source, 1);
            Check(Math.Abs(defaultHold.Repetitions[0].lower - defaultHold.Repetitions[0].top - 30) < .00001,
                "Legacy hold remains 30 seconds");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(clip);
        }
    }

    private static void VerifyRoutes()
    {
        foreach (string action in new[] { "deadbug", "birddog", "hipthrust", "hipthrust_hold30", "sidebridge",
            "sidebridge_hold30", "sidebridge_right", "sidebridge_right_hold30", "hipthrust_taiko", "crunch", "lying_cat" })
        {
            string target = GameWorkoutPlan.SceneFor(action);
            Check(EditorBuildSettings.scenes.Any(scene => scene.enabled && Path.GetFileNameWithoutExtension(scene.path) == target),
                "Workout route is packaged: " + action);
            new GameWorkoutPlan { actions = new[] { new GameWorkoutAction { action = action, difficulty = "standard", sets = 2, reps = 3 } } }.ValidatedCopy();
        }
        Check(GameWorkoutPlan.SceneFor("hipthrust") == CoachModeNavigation.HipThrustGameScene, "Mobile and main share the default bridge route");
    }

    private static void VerifyFullCatalog()
    {
        var packaged = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)).ToArray();
        Check(TrainingActionCatalog.Entries.Length == 11, "Full library has eleven game routes");
        foreach(var entry in TrainingActionCatalog.Entries)
        {
            Check(packaged.Contains(entry.Coach) && packaged.Contains(entry.Game), "Coach/game packaged: " + entry.Key);
            Check(TrainingActionCatalog.Find(entry.Key + "_coach") == entry && TrainingActionCatalog.Find(entry.Key + "_game") == entry,
                "Both prescribed identities resolve: " + entry.Key);
            Check(CoachModeNavigation.TryGetCoachSceneForGame(entry.Game, out string coach) && coach == entry.Coach,
                "Game tutorial returns to correct coach: " + entry.Key);
            Check(CoachModeNavigation.IsSupportedCoachScene(entry.Coach), "Coach can return to game: " + entry.Key);
        }
        var host = new GameObject("Scorer hold verification");
        try
        {
            ITaikoHoldScorer[] scorers = {host.AddComponent<CrunchHoldScorer>(),host.AddComponent<LyingCatHoldScorer>()};
            foreach(var scorer in scorers)
            {
                scorer.SetDifficultyMode(HipThrustDifficultyMode.Hard);
                Check(scorer.TargetHoldSeconds > 0, "Main action scorer retains its difficulty-specific hold target");
            }
            var summary = new TrainingQualitySummary();
            summary.Reset(new[] {10d,10d},new[] {7d,7d},true,false,60);
            summary.Observe(0,10,false,100,true);
            Check(!summary.HasAssessment && summary.Qualified == 0 && summary.CreditedSeconds == 0, "No sensor cannot manufacture results");
            summary.Reset(new[] {1d},new[] {0d},true,false,60);
            summary.Observe(0,1,true,100,true);
            summary.RecordDose(0,0,0,true,true);
            Check(summary.TimeCompletion == 1 && summary.Score == 100, "Zero prescribed hold has no duration penalty for a measured complete repetition");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Debug.Log("[MainIntegration] PASS " + message);
    }
}
