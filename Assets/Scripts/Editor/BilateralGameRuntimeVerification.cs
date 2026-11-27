using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SpineRecovery.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Real game scenes and button callbacks, synthetic calibration/telemetry. No patient/device data.
[InitializeOnLoad]
public static class BilateralGameRuntimeVerification
{
    const string Flag = "BilateralGameRuntimeVerification";
    const string Folder = "output/bilateral-runtime/";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    static int stage, variant, checks;
    static double started;
    static TrainingSessionService service;
    static GameObject host;
    static GameWorkoutScene game;
    static AvatarMotionSource motion;
    static string Key => variant == 0 ? "side_bridge" : "side_bridge_hold30";
    static string RightKey => variant == 0 ? "side_bridge_right" : "side_bridge_right_hold30";

    static BilateralGameRuntimeVerification()
    {
        if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick;
    }
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        string session = JsonFileStore.PathFor("unified-training-session.json");
        SessionState.SetBool(Flag + ".hadSession", File.Exists(session));
        if (File.Exists(session)) File.Copy(session, Folder + "session.backup", true);
        File.WriteAllText(Folder + "verification.txt", "Real right-side game scenes; synthetic calibration/telemetry; no physical sensor validation.\n");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Flag, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (stage == 0)
            {
                host = new GameObject("Bilateral runtime fixture"); host.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(host);
                var app = host.AddComponent<AppBootstrap>();
                service = host.AddComponent<TrainingSessionService>();
                SetAuto(app, "Training", service);
                typeof(AppBootstrap).GetField("<Instance>k__BackingField", Private).SetValue(null, app);
                var live = host.AddComponent<SpineFlowTrainingSession>();
                typeof(SpineFlowTrainingSession).GetField("instance", Private).SetValue(null, live);
                var plan = new TrainingPlan { accountId = "runtime-fixture", patientId = "runtime-fixture", sessionId = "runtime-fixture",
                    startedAt = "2026-11-28T00:00:00.000Z", prescriptionOrderId = 1, prescriptionContractVersion = 2,
                    gender = "female", actions = new[] { Action(Key, true), Action(RightKey, true), Action(Key, false), Action(RightKey, false) } };
                Check(TrainingSessionService.PreparePrescriptionPlan(plan), "Runtime plan validates");
                SetAuto(service, "CurrentPlan", plan);
                Check(SpineFlowTrainingSession.BeginUnifiedTraining(plan), "Runtime session starts");
                Check(service.SkipRemainingPrescriptionCoaches() == TrainingActionCatalog.Find(Key).Game, "Pause skip skips both coaches and opens left game");
                SpineFlowTrainingSession.MarkPrescriptionGameCompleted(3);
                string right = service.AdvanceWithinPrescriptionItem();
                Check(right == TrainingActionCatalog.Find(RightKey).Game, "Left game leads to official right game");
                SceneManager.LoadScene(right);
                stage = 1; started = EditorApplication.timeSinceStartup; return;
            }
            if (EditorApplication.timeSinceStartup - started >= 35) throw new Exception("Runtime timeout at stage " + stage);
            if (stage == 1)
            {
                game = UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();
                if (game == null || game.gameObject.scene.name != TrainingActionCatalog.Find(RightKey).Game || !game.menu.IsTrainingMenuVisible) return;
                var calibration = game.scorer.GetComponent<SupineGameCalibration>();
                Check(calibration.rightSideBridge && !calibration.skipCalibrationForPreview, "Right support and real calibration configured");
                Check(game.taiko.PrescriptionDose.Total == 3, "Right game receives one set of three");
                Check(variant == 0 || game.taiko.PrescriptionDose.HoldSeconds == 5, "Right hold game receives prescribed duration");
                game.scorer.UserAnimator.gameObject.SetActive(true);
                motion = game.scorer.UserAnimator.GetComponent<AvatarMotionSource>() ?? game.scorer.UserAnimator.gameObject.AddComponent<AvatarMotionSource>();
                motion.enabled = true; motion.Configure(game.scorer.UserAnimator);
                typeof(DeadBugGamingPoseScorer).GetField("assessmentMotion", Private).SetValue(game.scorer, motion);
                typeof(DeadBugGamingPoseScorer).GetField("calibrated", Private).SetValue(game.scorer, true);
                motion.SetAvailable(false);
                game.menu.StartTraining();
                Check(!game.IsPlaying && game.menu.TrainingStartBlockedReason().Contains("传感器"), "Missing input blocks start with a reason");
                motion.ReportFrame(1, 20);
                Check(game.menu.TrainingStartAllowed(), "Calibrated fresh input allows right game");
                ((Button)typeof(DeadBugStartMenuController).GetField("startButton", Private).GetValue(game.menu)).onClick.Invoke();
                Check(game.IsPlaying && !game.menu.IsTrainingMenuVisible, "Actual start button starts right game");
                stage = 2; return;
            }
            motion.ReportFrame(1, 20);
            if (!game.taiko.IsRunning) return;
            Check(game.taiko.Hold30Timeline.Repetitions.Length == 3, "Running right game has exact prescribed dose");
            typeof(HipThrustTaikoController).GetField("elapsed", Private).SetValue(game.taiko, game.taiko.Duration + .1);
            typeof(HipThrustTaikoController).GetMethod("RenderTimeline", Private).Invoke(game.taiko, null);
            typeof(HipThrustTaikoController).GetMethod("LateUpdate", Private).Invoke(game.taiko, null);
            Check(SpineFlowTrainingSession.CurrentPrescriptionGameCompleted, "Actual completion callback settles right game");
            Check((bool)typeof(TrainingSessionService).GetMethod("PrepareActionFeedback", Private).Invoke(service, null) &&
                service.FeedbackActionCount == 1, "Completed left/right games produce one questionnaire");
            File.AppendAllText(Folder + "verification.txt", "PASS " + game.gameObject.scene.name + ": button, countdown, three repetitions, completion and one survey.\n");
            service.ClearCurrentPlan();
            UnityEngine.Object.Destroy(host);
            typeof(AppBootstrap).GetField("<Instance>k__BackingField", Private).SetValue(null, null);
            typeof(SpineFlowTrainingSession).GetField("instance", Private).SetValue(null, null);
            if (++variant < 2) { stage = 0; return; }
            File.AppendAllText(Folder + "verification.txt", "PASS checks=" + checks + "\n");
            Finish(0);
        }
        catch (Exception error)
        {
            File.AppendAllText(Folder + "verification.txt", "FAIL " + error + "\n");
            Debug.LogException(error); Finish(1);
        }
    }
    static TrainingAction Action(string key, bool coach)
    {
        var entry = TrainingActionCatalog.Find(key);
        return new TrainingAction { actionId = key + (coach ? "_coach" : "_game"), actionName = "侧桥",
            arTaskType = coach ? "coach_training" : "game_training", sceneName = coach ? entry.Coach : entry.Game,
            sets = 1, reps = coach ? 2 : 3, holdSeconds = !coach && variant == 1 ? 5 : 0,
            prescriptionItemId = 1, prescriptionPosition = 0, level = "standard" };
    }
    static void SetAuto(object target, string name, object value) => target.GetType().GetField("<" + name + ">k__BackingField", Private).SetValue(target, value);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static void Finish(int code)
    {
        string session = JsonFileStore.PathFor("unified-training-session.json");
        if (SessionState.GetBool(Flag + ".hadSession", false)) File.Copy(Folder + "session.backup", session, true);
        else if (File.Exists(session)) File.Delete(session);
        SessionState.SetBool(Flag, false); EditorApplication.update -= Tick; EditorApplication.Exit(code);
    }
}
