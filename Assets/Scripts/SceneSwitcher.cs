using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
    private const string CoachingChooseScene = "CoachingChoose";
    private const string GamingChooseScene = "GamingChoose";
    private bool returnInProgress;

    private IEnumerator Start()
    {
        // Wait until scene/menu scripts have finished their Awake/Start setup,
        // then replace any stale legacy callback on the visible Home buttons.
        yield return null;
        int boundButtons = 0;
        // The legacy menu controller can clear button listeners during its Awake.
        // Rebind the visible Home/back buttons afterwards so the integrated Unity
        // app always returns to AppShell instead of trying to launch another app.
        Button[] buttons = FindObjectsOfType<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || button.gameObject.scene != gameObject.scene ||
                button.gameObject.name != "Home")
            {
                continue;
            }

            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(ReturnToMobileApp);
            button.interactable = true;
            HomeArrowReturnHandler handler = button.GetComponent<HomeArrowReturnHandler>();
            if (handler == null) handler = button.gameObject.AddComponent<HomeArrowReturnHandler>();
            handler.Initialize(this);
            boundButtons++;
        }
        Debug.Log($"[SpineFlow] Bound {boundButtons} Home/back button(s) to AppShell.", this);
    }

    public void ReturnToMobileApp()
    {
        if (returnInProgress) return;
        returnInProgress = true;
        Debug.Log("[SpineFlow] Home/back arrow clicked.", this);
        SpineRecovery.App.AppBootstrap app = SpineRecovery.App.AppBootstrap.Instance;
        if (app != null)
        {
            string current = SceneManager.GetActiveScene().name;
            if (current == "CoachingChoose" || current == "PlayBack")
                SceneManager.LoadScene("AppShell", LoadSceneMode.Single);
            else
                app.Training.FinishAndReturn();
            return;
        }
        SpineFlowTrainingSession.TryReturnToMobile();
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
        if (Application.CanStreamedLevelBeLoaded(GameScene))
        {
            LoadSceneForCurrentMobileSession(GameScene);
            return;
        }

        // The historical V2 repository references Gamescene, but that scene
        // asset is not present in any available branch.  Keep the button
        // functional on Android by falling back to the restored, data-driven
        // Dead Bug practice scene until the original game scene is supplied.
        Debug.LogWarning(
            "[SpineFlow] Gamescene is unavailable in this build; " +
            "falling back to DeadBugPractice.");
        LoadDeadBugPracticeScene();
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
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            BirdDogGamingScene,
            ReplaySessionContext.ReturnGamingScene));
    }

    public void LoadBirdDogPracticeScene()
    {
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            BirdDogPracticeScene,
            ReplaySessionContext.ReturnPracticeScene));
    }

    public void LoadDeadBugScene()
    {
        LoadSceneForCurrentMobileSession(DeadBugScene);
    }

    public void LoadDeadBugGamingScene()
    {
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            DeadBugGamingScene,
            ReplaySessionContext.ReturnGamingScene));
    }

    public void LoadTestGamingScene()
    {
        LoadSceneForCurrentMobileSession(TestGamingScene);
    }

    public void LoadHipThrustScene()
    {
        LoadSceneForCurrentMobileSession(HipThrustScene);
    }

    public void LoadDeadBugPracticeScene()
    {
        LoadSceneForCurrentMobileSession(ResolveReplayReturnScene(
            DeadBugPracticeScene,
            ReplaySessionContext.ReturnPracticeScene));
    }

    public void LoadPlayBackScene()
    {
        LoadSceneForCurrentMobileSession(PlayBackScene);
    }

    public void LoadCoachingChooseScene()
    {
        if (!CoachingActionLibrary.OpenFromPractice())
        {
            LoadSceneForCurrentMobileSession(CoachingChooseScene);
        }
    }

    public void LoadGamingChooseScene()
    {
        LoadSceneForCurrentMobileSession(GamingChooseScene);
    }

    /// <summary>
    /// 加载统一练习场景（testPractice），传入动作 ID 进行配置。
    /// 用于从其他场景（如 CoachingChoose）跳转到数据驱动的练习场景。
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
        if (SceneManager.GetActiveScene().name == PlayBackScene &&
            SpineRecovery.App.AppBootstrap.Instance != null &&
            SpineRecovery.App.AppBootstrap.Instance.Training.CurrentPlan == null)
        {
            SceneManager.LoadScene("AppShell", LoadSceneMode.Single);
            return;
        }
        SceneManager.LoadScene(SpineFlowTrainingSession.ResolveSceneForMobileSession(sceneName));
    }

    private static string ResolveReplayReturnScene(string fallback, string replayTarget)
    {
        return string.Equals(
                   SceneManager.GetActiveScene().name,
                   PlayBackScene,
                   System.StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(replayTarget)
            ? replayTarget
            : fallback;
    }
}

public sealed class HomeArrowReturnHandler : MonoBehaviour, IPointerClickHandler
{
    private SceneSwitcher owner;

    public void Initialize(SceneSwitcher sceneSwitcher)
    {
        owner = sceneSwitcher;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button == PointerEventData.InputButton.Left)
        {
            owner?.ReturnToMobileApp();
        }
    }
}
