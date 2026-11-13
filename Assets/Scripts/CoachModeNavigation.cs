using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the four game/coaching routes in one place so buttons do not fall back
/// to the legacy DeadBug/BirdDog scene names.
/// </summary>
public static class CoachModeNavigation
{
    public const string DeadBugCoachScene = "DeadBugPractice";
    public const string BirdDogCoachScene = "BirdDogPractice";
    public const string HipThrustCoachScene = "HipThrustPractice";
    public const string SideBridgeCoachScene = "SideBridgePractice";

    public const string DeadBugGameScene = "DeadBug_RingSkinC_Recorded";
    public const string BirdDogGameScene = "BirdDog_RingSkinC_Recorded";
    public const string HipThrustGameScene = "HipThrust_RingBridge_NewScoring";
    public const string SideBridgeGameScene = "SideBridge_RingBridge_Dynamic";

    private static string replayPracticeScene;
    private static string replayGameScene;
    private static string preferredGameScene;
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

    public static bool TryGetGameSceneForCoach(string coachScene, out string gameScene)
    {
        switch (NormalizeSceneName(coachScene))
        {
            case DeadBugCoachScene:
            case "maleDeadBugPracticeOld":
                gameScene = DeadBugGameScene;
                return true;
            case BirdDogCoachScene:
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
            default:
                gameScene = null;
                return false;
        }
    }

    public static bool TryGetCoachSceneForGame(string gameScene, out string coachScene)
    {
        switch (NormalizeSceneName(gameScene))
        {
            case DeadBugGameScene:
            case "SpineV2Minimal":
            case "DeadBugGaming":
                coachScene = DeadBugCoachScene;
                return true;
            case BirdDogGameScene:
            case "BirdDogMinimal":
            case "BirdDogGaming":
                coachScene = BirdDogCoachScene;
                return true;
            case HipThrustGameScene:
            case "HipThrust_RingBridge_Hold30":
            case "HipThrust_RingBridge_Recorded":
            case "HipThrustMinimal":
            case "HipTrustGaming":
                coachScene = HipThrustCoachScene;
                return true;
            case SideBridgeGameScene:
            case "SideBridge_RingBridge_Hold30":
                coachScene = SideBridgeCoachScene;
                return true;
            default:
                coachScene = null;
                return false;
        }
    }

    public static bool IsSupportedCoachScene(string sceneName)
    {
        return TryGetGameSceneForCoach(sceneName, out _);
    }

    public static bool IsSupportedGameScene(string sceneName)
    {
        return TryGetCoachSceneForGame(sceneName, out _);
    }

    public static void OpenCoachFromGame(string gameScene = null)
    {
        string source = string.IsNullOrWhiteSpace(gameScene)
            ? SceneManager.GetActiveScene().name
            : gameScene;
        if (!TryGetCoachSceneForGame(source, out string coachScene))
        {
            Debug.LogWarning($"[CoachModeNavigation] No coach scene for game '{source}'.");
            return;
        }

        preferredGameScene = IsConcreteGameScene(source)
            ? source
            : ResolveDefaultGameScene(coachScene);
        tutorialDifficulty = UnityEngine.Object.FindObjectOfType<DeadBugStartMenuController>(true)?.SelectedDifficultyKey ?? "standard";
        tutorialScene = SpineFlowTrainingSession.ResolveSceneForMobileSession(coachScene);
        UnityEngine.Object.FindObjectOfType<GameWorkoutSession>(true)?.Cancel();
        LoadScene(coachScene);
    }

    public static void OpenGameFromCoach(string coachScene = null)
    {
        string source = string.IsNullOrWhiteSpace(coachScene)
            ? SceneManager.GetActiveScene().name
            : coachScene;
        if (!TryGetGameSceneForCoach(source, out string gameScene))
        {
            Debug.LogWarning($"[CoachModeNavigation] No game scene for coach '{source}'.");
            return;
        }

        gameScene = ResolvePreferredGameScene(source, gameScene);
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
        StopCurrentPractice();
        if (!CoachingActionLibrary.OpenFromPractice())
        {
            LoadScene("ActionLibrary");
        }
    }

    public static void OpenReplayFromCoach()
    {
        string coachScene = SceneManager.GetActiveScene().name;
        if (!TryGetGameSceneForCoach(coachScene, out string gameScene))
        {
            Debug.LogWarning($"[CoachModeNavigation] Replay return is unavailable for '{coachScene}'.");
            return;
        }

        gameScene = ResolvePreferredGameScene(coachScene, gameScene);
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

        SceneManager.LoadScene(resolved);
    }

    private static string ResolvePreferredGameScene(string coachScene, string fallback)
    {
        return IsConcreteGameScene(preferredGameScene) &&
            TryGetCoachSceneForGame(preferredGameScene, out string preferredCoach) &&
            string.Equals(
                NormalizeSceneName(preferredCoach),
                NormalizeSceneName(coachScene),
                StringComparison.Ordinal)
            ? preferredGameScene
            : fallback;
    }

    private static string ResolveDefaultGameScene(string coachScene)
    {
        return TryGetGameSceneForCoach(coachScene, out string gameScene)
            ? gameScene
            : null;
    }

    private static bool IsConcreteGameScene(string sceneName)
    {
        return string.Equals(sceneName, DeadBugGameScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, BirdDogGameScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, HipThrustGameScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, SideBridgeGameScene, StringComparison.Ordinal) ||
            string.Equals(sceneName, "HipThrust_RingBridge_Hold30", StringComparison.Ordinal) ||
            string.Equals(sceneName, "SideBridge_RingBridge_Hold30", StringComparison.Ordinal);
    }

    private static string NormalizeSceneName(string sceneName)
    {
        return sceneName?.Trim() ?? string.Empty;
    }
}
