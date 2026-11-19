using System;
using System.Reflection;
using SpineRecovery.App;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Run in an isolated batch-mode editor. No login, native bridge or user-file writes.
public static class NavigationModuleVerification
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int checks;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode editor.");
        const string temporaryScenes = "Assets/__NavigationVerification";
        if (System.IO.Directory.Exists(temporaryScenes)) throw new InvalidOperationException("Temporary scene directory already exists.");
        System.IO.Directory.CreateDirectory(temporaryScenes);
        Scene original = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(original, temporaryScenes + "/AppShell.unity");
        GameObject host = new GameObject("Navigation verification");
        host.SetActive(false);
        var app = host.AddComponent<AppBootstrap>();
        var shell = host.AddComponent<AppShellUi>();
        var training = host.AddComponent<TrainingSessionService>();
        var router = host.AddComponent<AppRouter>();
        var state = new AppState();
        state.SetSession(new UserSession { accountId = "navigation-verification" });
        router.Initialize(state);
        SetAuto(typeof(AppBootstrap), null, "Instance", app);
        SetAuto(app.GetType(), app, "State", state);
        SetAuto(app.GetType(), app, "Router", router);
        SetAuto(app.GetType(), app, "Training", training);
        Set(shell, "bootstrap", app);
        var plan = new TrainingPlan { accountId = "navigation-verification", sessionId = "existing" };
        var result = new TrainingHistoryRecord { sessionId = "existing", resultJson = "preserved-progress" };
        SetAuto(training.GetType(), training, "CurrentPlan", plan);
        SetAuto(training.GetType(), training, "CurrentResult", result);
        try
        {
            foreach (AppRoute route in new[] { AppRoute.Home, AppRoute.TrainingPreparation, AppRoute.History })
            {
                SetAuxiliary(shell, route == AppRoute.History ? "PlayBack" : "ActionLibrary", route);
                Call(shell, "HandleSceneLoaded", original, LoadSceneMode.Single);
                Check(state.Route == route, "Auxiliary return restores " + route);
                Check(ReferenceEquals(plan, training.CurrentPlan) && ReferenceEquals(result, training.CurrentResult) &&
                    !result.feedbackPending && result.endReason == null && result.resultJson == "preserved-progress",
                    "Browsing does not finish or replace an existing training session");
            }

            Scene replay = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(replay, temporaryScenes + "/PlayBack.unity");
            SceneManager.SetActiveScene(replay);
            var switcher = new GameObject("Switcher").AddComponent<SceneSwitcher>();
            SetAuxiliary(shell, "PlayBack", AppRoute.History);
            // A queued return must consume every repeated navigation click without loading a training scene.
            Set(shell, "auxiliaryReturnInProgress", true);
            CoachModeNavigation.SetReplayReturnScenes("CatCamelPractice", null);
            Check(!(bool)Call(typeof(SceneSwitcher), "IsCoachWithoutGame"), "History replay is not blocked by stale coach context");
            switcher.LoadDeadBugPracticeScene();
            switcher.LoadDeadBugGamingScene();
            switcher.LoadGameScene();
            switcher.ReturnToCurrentCoachScene();
            Check(SceneManager.GetActiveScene() == replay, "Repeated replay returns do not load a practice scene");
            Check(CoachModeNavigation.TryConsumeReplayReturnScene(false, out string preserved) && preserved == "CatCamelPractice",
                "History replay does not consume a training replay return route");

            Button home = ButtonInScene("Home", switcher);
            Button unrelated = ButtonInScene("Home", switcher);
            UnityEventTools.RemovePersistentListener(unrelated.onClick, 0);
            UnityEventTools.AddPersistentListener(unrelated.onClick, switcher.LoadPlayBackScene);
            Call(switcher, "BindAppHomeButtons");
            Check(home.onClick.GetPersistentEventCount() == 0, "Known replay Home is rebound");
            Check(unrelated.onClick.GetPersistentMethodName(0) == "LoadPlayBackScene", "Unrelated Home binding is preserved");
            home.onClick.Invoke();
            var otherSwitcher = new GameObject("Other switcher").AddComponent<SceneSwitcher>();
            otherSwitcher.ReturnToMobileApp();
            Check(!result.feedbackPending, "Multiple Home callbacks leave browsing session unchanged");

            Scene practice = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(practice, temporaryScenes + "/DeadBugPractice.unity");
            SceneManager.SetActiveScene(practice);
            var practiceSwitcher = new GameObject("Practice switcher").AddComponent<SceneSwitcher>();
            Button practiceHome = ButtonInScene("Home", practiceSwitcher);
            Call(practiceSwitcher, "BindAppHomeButtons");
            Check(practiceHome.onClick.GetPersistentEventCount() == 1, "Practice controller retains ownership of Home callbacks");
            Call(shell, "HandleSceneLoaded", practice, LoadSceneMode.Single);
            Check(!(bool)Get(shell, "auxiliaryPageOpen"), "Starting practice clears auxiliary browsing context");

            GameObject progress = new GameObject("Retained progress");
            progress.transform.position = new Vector3(3, 7, 11);
            GameObject originallyInactive = new GameObject("Inactive before browsing");
            originallyInactive.SetActive(false);
            Scene library = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(library, temporaryScenes + "/ActionLibrary.unity");
            Set(typeof(CoachingActionLibrary), "retainedPracticeScene", practice);
            Call(typeof(CoachingActionLibrary), "OnSceneLoaded", library, LoadSceneMode.Additive);
            Check(!progress.activeSelf, "Library suspends retained practice roots");
            Call(typeof(CoachingActionLibrary), "RestorePracticeAfterLibraryUnload", new object[] { null });
            Check(progress.activeSelf && progress.transform.position == new Vector3(3, 7, 11) && !originallyInactive.activeSelf,
                "Library restores the same objects, progress and original active states");
            Debug.Log("[NavigationVerification] PASS: " + checks + " checks. Scene loads and native persistence require device smoke testing.");
        }
        finally
        {
            SetAuto(typeof(AppBootstrap), null, "Instance", null);
            CoachModeNavigation.SetReplayReturnScenes(null, null);
            Call(typeof(SceneSwitcher), "ResetReturnState");
            UnityEngine.Object.DestroyImmediate(host);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(temporaryScenes);
        }
    }

    private static Button ButtonInScene(string name, SceneSwitcher owner)
    {
        var button = new GameObject(name, typeof(RectTransform), typeof(Button)).GetComponent<Button>();
        SceneManager.MoveGameObjectToScene(button.gameObject, owner.gameObject.scene);
        UnityEventTools.AddPersistentListener(button.onClick, owner.ReturnToMobileApp);
        return button;
    }

    private static void SetAuxiliary(AppShellUi shell, string scene, AppRoute route)
    {
        Set(shell, "auxiliaryPageOpen", true);
        Set(shell, "auxiliarySceneName", scene);
        Set(shell, "auxiliaryReturnRoute", route);
        Set(shell, "auxiliaryReturnInProgress", false);
    }

    private static object Call(object target, string method, params object[] args) =>
        (target as Type ?? target.GetType()).GetMethod(method, Private).Invoke(target is Type ? null : target, args);
    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value) =>
        (target as Type ?? target.GetType()).GetField(field, Private).SetValue(target is Type ? null : target, value);
    private static void SetAuto(Type type, object target, string property, object value) =>
        type.GetField("<" + property + ">k__BackingField", Private).SetValue(target, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Debug.Log("[NavigationVerification] " + message);
    }
}
