using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Connects game variants to the coaching scene for the same movement.
/// </summary>
public static class CoachModeNavigation
{
    public const string DeadBugCoachScene = "DeadBugPractice";
    public const string MaleDeadBugCoachScene = "maleDeadBugPractice";
    public const string BirdDogCoachScene = "BirdDogPractice";
    public const string MaleBirdDogCoachScene = "maleBirdDogPractice";
    public const string HipThrustCoachScene = "HipThrustPractice";
    public const string SideBridgeCoachScene = "SideBridgePractice";
    public const string SideBridgeRightCoachScene = "SideBridgeOppositePractice";
    public const string MaleSideBridgeRightCoachScene = "maleSideBridgeOppositePractice";
    private const string CrunchCoachScene = "CurlUpPractice";
    private const string LyingCatCoachScene = "CatCamelPractice";

    public const string DeadBugGameScene = "DeadBug_RingSkinC_Recorded";
    public const string BirdDogGameScene = "BirdDog_RingSkinC_Recorded";
    public const string HipThrustGameScene = "HipThrust_RingBridge_NewScoring";
    public const string SideBridgeGameScene = "SideBridge_RingBridge_Dynamic";
    public const string SideBridgeRightGameScene = "SideBridgeRight_RingBridge_Dynamic";

    private static string replayPracticeScene;
    private static string replayGameScene;
    private static string tutorialScene;
    private static string tutorialDifficulty;

    public static bool IsOpeningTutorial(string scene) => tutorialScene == scene;

    public static bool ConsumeTutorial(string scene, out string difficulty)
    {
        difficulty = tutorialDifficulty;
        if (tutorialScene != scene) return false;
        tutorialScene = null;
        tutorialDifficulty = null;
        return true;
    }

    public static bool HasReplayGameReturn => !string.IsNullOrWhiteSpace(replayGameScene);

    public static bool IsRightSideBridgeCoachScene(string scene) =>
        scene == SideBridgeRightCoachScene || scene == MaleSideBridgeRightCoachScene;

    public static IEnumerable<(string Scene, string Title)> GetGameVersionsForCoach(string coachScene)
    {
        coachScene = NormalizeSceneName(coachScene);
        for (int i = 0; i < GameTestMenu.Scenes.Length; i++)
        {
            string gameScene = GameTestMenu.Scenes[i];
            if (TryGetCoachSceneForGame(gameScene, out string match) &&
                (coachScene == match || coachScene == SpineFlowTrainingSession.ResolveSceneForGender(match, true)))
            {
                yield return (gameScene, GameTestMenu.Titles[i]);
            }
        }
    }

    private static bool TryGetDefaultGameSceneForCoach(string coachScene, out string gameScene)
    {
        var entry = TrainingActionCatalog.ForCoach(coachScene);
        if (entry != null) { gameScene = entry.Game; return true; }
        switch (NormalizeSceneName(coachScene))
        {
            case DeadBugCoachScene:
            case MaleDeadBugCoachScene:
            case "maleDeadBugPracticeOld":
                gameScene = DeadBugGameScene;
                return true;
            case BirdDogCoachScene:
            case MaleBirdDogCoachScene:
            case "maleBirdDogPracticeOld":
                gameScene = BirdDogGameScene;
                return true;
            case HipThrustCoachScene:
            case "maleHipThrustPractice":
                gameScene = HipThrustGameScene;
                return true;
            case SideBridgeCoachScene:
            case "maleSideBridgePractice":
                gameScene = SideBridgeGameScene;
                return true;
            case SideBridgeRightCoachScene:
            case MaleSideBridgeRightCoachScene:
                gameScene = SideBridgeRightGameScene;
                return true;
            default:
                gameScene = null;
                return false;
        }
    }

    public static bool TryGetCoachSceneForGame(string gameScene, out string coachScene)
    {
        var entry = TrainingActionCatalog.ForGame(gameScene);
        if (entry != null) { coachScene = entry.Coach; return true; }
        switch (NormalizeSceneName(gameScene))
        {
            case DeadBugGameScene:
            case "DeadBug_RingBridge_Hold30":
            case "SpineV2Minimal":
            case "DeadBugGaming":
                coachScene = DeadBugCoachScene;
                return true;
            case BirdDogGameScene:
            case "BirdDog_RingBridge_Hold30":
            case "BirdDogMinimal":
            case "BirdDogGaming":
                coachScene = BirdDogCoachScene;
                return true;
            case HipThrustGameScene:
            case "HipThrust_RingBridge_Hold30":
            case "HipThrust_TaikoNight_Dynamic":
            case "BridgeWave_StandardFollow":
            case "HipThrust_RingBridge_Recorded":
            case "HipThrustMinimal":
            case "HipTrustGaming":
                coachScene = HipThrustCoachScene;
                return true;
            case SideBridgeGameScene:
            case "SideBridge_RingBridge_Hold30":
                coachScene = SideBridgeCoachScene;
                return true;
            case SideBridgeRightGameScene:
            case "SideBridgeOpposite_RingBridge_Dynamic":
            case "SideBridgeOpposite_RingBridge_Hold30":
            case "SideBridgeRight_RingBridge_Hold30":
                coachScene = SideBridgeRightCoachScene;
                return true;
            case "CrunchPractice_TaikoNight":
            case "Crunch_RingBridge_Hold30":
                coachScene = CrunchCoachScene;
                return true;
            case "LyingCatPractice_TaikoNight":
            case "LyingCat_RingBridge_Hold30":
                coachScene = LyingCatCoachScene;
                return true;
            default:
                coachScene = null;
                return false;
        }
    }

    public static bool IsSupportedCoachScene(string sceneName)
    {
        using (var games = GetGameVersionsForCoach(sceneName).GetEnumerator())
            return games.MoveNext();
    }

    public static bool IsSupportedGameScene(string sceneName)
    {
        return TryGetCoachSceneForGame(sceneName, out _);
    }

    public static void OpenCoachFromGame(string gameScene = null, string difficulty = null)
    {
        string source = string.IsNullOrWhiteSpace(gameScene)
            ? SceneManager.GetActiveScene().name
            : gameScene;
        if (!TryGetCoachSceneForGame(source, out string coachScene))
        {
            Debug.LogWarning($"[CoachModeNavigation] No coach scene for game '{source}'.");
            return;
        }

        tutorialDifficulty = difficulty ??
            UnityEngine.Object.FindObjectOfType<DeadBugStartMenuController>(true)?.SelectedDifficultyKey ?? "standard";
        tutorialScene = SpineFlowTrainingSession.ResolveSceneForMobileSession(coachScene);
        UnityEngine.Object.FindObjectOfType<GameWorkoutSession>(true)?.Cancel();
        LoadScene(coachScene);
    }

    public static void SkipCoachAndOpenGame()
    {
        var training = SpineRecovery.App.AppBootstrap.Instance?.Training;
        if (training?.IsV2Prescription == true)
        {
            UnityEngine.Object.FindObjectOfType<PracticeSessionController>(true)?.SkipToPrescriptionGame();
            string next = training.SkipRemainingPrescriptionCoaches();
            if (next != null) { StopCurrentPractice(); LoadScene(next); }
            return;
        }
        OpenGameFromCoach();
    }

    public static void OpenGameFromCoach(string coachScene = null)
    {
        var training = SpineRecovery.App.AppBootstrap.Instance?.Training;
        string gameScene;
        if (training?.IsV2Prescription == true)
        {
            gameScene = training.AdvancePrescriptionCoach();
            if (gameScene == null) return;
        }
        else
        {
            string source = string.IsNullOrWhiteSpace(coachScene) ? SceneManager.GetActiveScene().name : coachScene;
            if (!TryGetDefaultGameSceneForCoach(source, out gameScene))
            {
                Debug.LogWarning($"[CoachModeNavigation] No game scene for coach '{source}'.");
                return;
            }
        }
        StopCurrentPractice();
        LoadScene(gameScene);
    }

    public static void OpenSelectedGameFromCoach(string gameScene)
    {
        if (SpineRecovery.App.AppBootstrap.Instance?.Training.IsPrescribedTraining == true) return;
        StopCurrentPractice();
        LoadScene(gameScene);
    }

    public static void ReturnToCoachStart()
    {
        StopCurrentPractice();
        DeadBugStartMenuController presentation =
            UnityEngine.Object.FindObjectOfType<DeadBugStartMenuController>(true);
        if (presentation != null && IsSupportedCoachScene(SceneManager.GetActiveScene().name))
        {
            presentation.ShowMenu();
            return;
        }

        string current = SceneManager.GetActiveScene().name;
        if (IsSupportedCoachScene(current))
        {
            DeadBugStartMenuController.ShowPracticeStartOnNextLoad();
            LoadScene(current);
        }
    }

    public static void OpenActionLibraryFromCoach()
    {
        UnityEngine.Object.FindObjectOfType<DeadBugStartMenuController>(true)
            ?.HidePracticeToolbarForActionLibrary();
        StopCurrentPractice();
        if (!CoachingActionLibrary.OpenFromPractice())
        {
            LoadScene("ActionLibrary");
        }
    }

    public static void OpenReplayFromCoach()
    {
        string coachScene = SceneManager.GetActiveScene().name;
        if (!TryGetDefaultGameSceneForCoach(coachScene, out string gameScene))
        {
            Debug.LogWarning($"[CoachModeNavigation] Replay return is unavailable for '{coachScene}'.");
            return;
        }

        StopCurrentPractice();
        replayPracticeScene = coachScene;
        replayGameScene = gameScene;
        LoadScene("PlayBack");
    }

    public static void SetReplayReturnScenes(string practiceScene, string gameScene)
    {
        replayPracticeScene = practiceScene;
        replayGameScene = gameScene;
    }

    public static bool TryConsumeReplayReturnScene(
        bool gaming,
        out string destination)
    {
        destination = gaming
            ? replayGameScene
            : replayPracticeScene;
        if (string.IsNullOrWhiteSpace(destination))
        {
            return false;
        }

        replayPracticeScene = null;
        replayGameScene = null;
        return true;
    }

    private static void StopCurrentPractice()
    {
        MobileCoachControls controls =
            UnityEngine.Object.FindObjectOfType<MobileCoachControls>(true);
        controls?.SetPaused(false);
        UnityEngine.Object.FindObjectOfType<PracticeSessionController>(true)?.StopPractice();
    }

    private static void LoadScene(string sceneName)
    {
        string resolved = SpineFlowTrainingSession.ResolveSceneForMobileSession(sceneName);
        if (!Application.CanStreamedLevelBeLoaded(resolved))
        {
            Debug.LogError($"[CoachModeNavigation] Scene is not in build settings: {resolved}");
            return;
        }

        SceneLoadingOverlay.Load(resolved);
    }

    private static string NormalizeSceneName(string sceneName)
    {
        return sceneName?.Trim() ?? string.Empty;
    }
}
