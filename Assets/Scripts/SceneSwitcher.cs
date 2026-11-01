using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSwitcher : MonoBehaviour
{
    private const string GameScene = "Gamescene";
    private const string ActionScene = "ActionScene";
    private const string BirdDogScene = "BirdDog";
    private const string BirdDogGamingScene = "BirdDogGaming";
    private const string BirdDogPracticeScene = "BirdDogPractice";
    private const string DeadBugScene = "DeadBug";
    private const string DeadBugGamingScene = "DeadBugGaming";
    private const string DeadBugPracticeScene = "DeadBugPractice";
    private const string TestGamingScene = "testGaming";
    public static string TestGamingSceneName => TestGamingScene;
    private const string HipThrustScene = "HipThrust";
    private const string PlayBackScene = "PlayBack";
    private const string ActionLibraryScene = "ActionLibrary";
    private const string GamingChooseScene = "GamingChoose";

    public void ReturnToMobileApp()
    {
        if (SpineFlowTrainingSession.TryReturnToMobile())
        {
            return;
        }

        Debug.LogWarning(
            "[SpineFlow] Could not return to the mobile app. " +
            "The Home scene is intentionally not used in this release.",
            this);
    }

    public void ReturnToCurrentCoachScene()
    {
        string coachScene = SpineFlowTrainingSession.CurrentCoachScene;
        if (string.IsNullOrEmpty(coachScene))
        {
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "BirdDogMinimal")
            {
                coachScene = BirdDogPracticeScene;
            }
            else if (currentScene == "SpineV2Minimal" ||
                     currentScene == "DeadBugRhythmHapticsTest")
            {
                coachScene = DeadBugPracticeScene;
            }
        }

        if (string.IsNullOrEmpty(coachScene) ||
            !Application.CanStreamedLevelBeLoaded(coachScene))
        {
            Debug.LogWarning("No matching coach scene is available for this game.", this);
            return;
        }

        LoadSceneForCurrentMobileSession(coachScene);
    }

    public void LoadGameScene()
    {
        LoadSceneForCurrentMobileSession(GameScene);
    }

    public void LoadActionScene()
    {
        LoadSceneForCurrentMobileSession(ActionScene);
    }

    public void LoadBirdDogScene()
    {
        LoadSceneForCurrentMobileSession(BirdDogScene);
    }

    public void LoadBirdDogGamingScene()
    {
        if (IsCoachWithoutGame() && !CoachModeNavigation.HasReplayGameReturn) return;
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            CoachModeNavigation.BirdDogGameScene,
            ReplaySessionContext.ReturnGamingScene,
            true));
    }

    public void LoadBirdDogPracticeScene()
    {
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            CoachModeNavigation.BirdDogCoachScene,
            ReplaySessionContext.ReturnPracticeScene,
            false));
    }

    public void LoadDeadBugScene()
    {
        LoadSceneForCurrentMobileSession(DeadBugScene);
    }

    public void LoadDeadBugGamingScene()
    {
        if (IsCoachWithoutGame() && !CoachModeNavigation.HasReplayGameReturn) return;
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            CoachModeNavigation.DeadBugGameScene,
            ReplaySessionContext.ReturnGamingScene,
            true));
    }

    public void LoadTestGamingScene()
    {
        if (IsCoachWithoutGame()) return;
        LoadSceneForCurrentMobileSession(TestGamingScene);
    }

    public void LoadHipThrustScene()
    {
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            CoachModeNavigation.HipThrustGameScene,
            ReplaySessionContext.ReturnGamingScene,
            true));
    }

    public void LoadDeadBugPracticeScene()
    {
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            CoachModeNavigation.DeadBugCoachScene,
            ReplaySessionContext.ReturnPracticeScene,
            false));
    }

    public void LoadHipThrustPracticeScene()
    {
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            CoachModeNavigation.HipThrustCoachScene,
            ReplaySessionContext.ReturnPracticeScene,
            false));
    }

    public void LoadSideBridgePracticeScene()
    {
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            CoachModeNavigation.SideBridgeCoachScene,
            ReplaySessionContext.ReturnPracticeScene,
            false));
    }

    public void LoadPlayBackScene()
    {
        LoadSceneForCurrentMobileSession(PlayBackScene);
    }

    public void LoadActionLibraryScene()
    {
        if (!CoachingActionLibrary.OpenFromPractice())
        {
            LoadSceneForCurrentMobileSession(ActionLibraryScene);
        }
    }

    public void LoadGamingChooseScene()
    {
        LoadSceneForCurrentMobileSession(GamingChooseScene);
    }

    /// <summary>
    /// 加载统一练习场景（testPractice），传入动作 ID 进行配置。
    /// 用于从其他场景（如 ActionLibrary）跳转到数据驱动的练习场景。
    /// </summary>
    /// <param name="actionId">动作 ID，如 "deadbug", "birddog"</param>
    public void LoadUnifiedPracticeScene(string actionId)
    {
        PracticeSceneController.LoadPracticeScene(actionId, "testPractice");
    }

    /// <summary>
    /// 加载统一练习场景，使用服务器下发的动作包。
    /// 用于服务器动态下发新动作的场景。
    /// </summary>
    /// <param name="package">服务器下发的动作数据包</param>
    public void LoadUnifiedPracticeSceneWithPackage(CoachMotionPackage package)
    {
        string error = "";
        if (package == null || !package.IsValid(out error))
        {
            Debug.LogError($"[SceneSwitcher] 无效的动作包: {error}");
            return;
        }

        // 将 package 序列化为 JSON，暂存到临时文件
        string tempPath = System.IO.Path.Combine(Application.persistentDataPath, "temp_motion_package.json");
        try
        {
            string json = JsonUtility.ToJson(package, prettyPrint: false);
            System.IO.File.WriteAllText(tempPath, json);
            PracticeSceneInitializer.PendingMotionPackageUri = tempPath;
            LoadScene("testPractice");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SceneSwitcher] 保存临时动作包失败: {ex.Message}");
        }
    }

    public void LoadScene(string sceneName)
    {
        LoadSceneForCurrentMobileSession(sceneName);
    }

    private static void LoadSceneForCurrentMobileSession(string sceneName)
    {
        SceneManager.LoadScene(SpineFlowTrainingSession.ResolveSceneForMobileSession(sceneName));
    }

    private static bool IsCoachWithoutGame()
    {
        string scene = SceneManager.GetActiveScene().name;
        return LumbarCoachMode.ForScene(scene) != null ||
            (scene == PlayBackScene && LumbarCoachMode.ForScene(ReplaySessionContext.ReturnPracticeScene) != null);
    }

    private static string ResolveReplayReturnScene(
        string fallback,
        string replayTarget,
        bool gaming)
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                PlayBackScene,
                System.StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        if (CoachModeNavigation.TryConsumeReplayReturnScene(gaming, out string coachRoute))
        {
            return coachRoute;
        }

        return !string.IsNullOrWhiteSpace(replayTarget)
            ? replayTarget
            : fallback;
    }
}
