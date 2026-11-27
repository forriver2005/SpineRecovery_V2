using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor.SceneManagement;
using SpineRecovery.App;
using UnityEngine;

public static class ClinicalPrescriptionVerification
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int checks;

    public static void Run()
    {
        MainIntegrationVerification.Run();
        PostActionFeedbackVerification.Run();
        string path = JsonFileStore.PathFor("unified-training-session.json");
        byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var host = new GameObject("Clinical prescription verification");
        host.SetActive(false);
        try
        {
            var service = host.AddComponent<TrainingSessionService>();
            var app = host.AddComponent<AppBootstrap>();
            Set(app, "Training", service);
            typeof(AppBootstrap).GetField("<Instance>k__BackingField", Private).SetValue(null, app);
            var live = host.AddComponent<SpineFlowTrainingSession>();
            typeof(SpineFlowTrainingSession).GetField("instance", Private).SetValue(null, live);
            var practice = host.AddComponent<PracticeSessionController>();
            foreach (bool hold in new[] { false, true })
            foreach (int sets in new[] { 1, 2, 20 })
            {
                string left = hold ? "side_bridge_hold30" : "side_bridge";
                string right = hold ? "side_bridge_right_hold30" : "side_bridge_right";
                var steps = new System.Collections.Generic.List<TrainingAction>();
                foreach (string key in new[] { left, right }) { var a = Action(key, true); a.reps = 2; a.actionName = "侧桥"; steps.Add(a); }
                for (int group = 0; group < sets; group++) foreach (string key in new[] { left, right })
                { var a = Action(key, false); a.sets = 1; a.holdSeconds = hold ? 45 : 0; steps.Add(a); }
                var plan = new TrainingPlan { accountId = "clinical-test", patientId = "clinical-test", sessionId = "bilateral-test",
                    prescriptionOrderId = 19, prescriptionContractVersion = 2, gender = "female", actions = steps.ToArray() };
                Check(TrainingSessionService.PreparePrescriptionPlan(plan), "Bilateral sequence validates: " + sets + " sets, hold=" + hold);
                Set(service, "CurrentPlan", plan);
                Check(service.FeedbackActionCount == 1, "Both sides and every set own one questionnaire");
                Check(SpineFlowTrainingSession.BeginUnifiedTraining(plan), "Bilateral session starts");
                Check(service.AdvancePrescriptionCoach() == null, "Unfinished coach cannot advance");
                for (int side = 0; side < 2; side++)
                {
                    Check(SpineFlowTrainingSession.GetPlannedTrainingVolume("coach", 9, 9).TotalRepetitions == 2, "Each side coach has exactly two repetitions");
                    // Exercise both successful coach completion and explicit skipping.
                    if (sets == 1) SpineFlowTrainingSession.EndCoachTraining(null, 0, 0, 2, CoachTimelineStatus.Completed, CoachEndReason.NormalCompletion);
                    else practice.SkipToPrescriptionGame();
                    string next = service.AdvancePrescriptionCoach();
                    Check(next == (side == 0 ? "SideBridgeOppositePractice" : TrainingActionCatalog.Find(left).Game), "Coach order is left then right then left game");
                }
                for (int group = 0; group < sets * 2; group++)
                {
                    string scene = TrainingActionCatalog.Find(group % 2 == 0 ? left : right).Game;
                    Check(service.AllowsPrescriptionGame(scene), "Current left/right game is startable after both coaches");
                    Check(!service.AllowsPrescriptionGame(TrainingActionCatalog.Find(group % 2 == 0 ? right : left).Game), "Wrong side is blocked");
                    Check(SpineFlowTrainingSession.GetPlannedTrainingVolume("game", 9, 9).TotalRepetitions == 3, "Only one prescribed set runs on each side");
                    SpineFlowTrainingSession.MarkPrescriptionGameCompleted(2);
                    Check(!SpineFlowTrainingSession.CurrentPrescriptionGameCompleted, "Partial set cannot advance");
                    SpineFlowTrainingSession.MarkPrescriptionGameCompleted(3);
                    if (group + 1 < sets * 2)
                    {
                        Check(!(bool)Call(service, "PrepareActionFeedback"), "No questionnaire between sides or sets");
                        Check(service.AdvanceWithinPrescriptionItem() == TrainingActionCatalog.Find(group % 2 == 0 ? right : left).Game, "Game sets alternate left and right");
                    }
                }
                Check(service.AdvanceWithinPrescriptionItem() == null && (bool)Call(service, "PrepareActionFeedback"), "Only final right set opens questionnaire");
                var result = service.CurrentResult;
                Check(result.completed, "All stages complete the prescription");
                Check((string)Call(service, "RecordFeedback", "test feeling", "test difficulty") == null && service.CurrentPlan == null &&
                    result.actionFeedbacks.Length == 1 && result.actionFeedbacks[0].actionName == "侧桥", "One bilateral answer finishes this prescription");
            }
            VerifyRepeatedBilateralItems(service);
            VerifyAllGamesAndRepeats(service, practice);
            Debug.Log("CLINICAL_PRESCRIPTION_VERIFICATION_PASS checks=" + checks);
        }
        finally
        {
            typeof(AppBootstrap).GetField("<Instance>k__BackingField", Private).SetValue(null, null);
            typeof(SpineFlowTrainingSession).GetField("instance", Private).SetValue(null, null);
            UnityEngine.Object.DestroyImmediate(host);
            if (original == null) { if (File.Exists(path)) File.Delete(path); }
            else File.WriteAllBytes(path, original);
        }
    }

    private static void VerifyRepeatedBilateralItems(TrainingSessionService service)
    {
        var steps = Enumerable.Range(0, 2).SelectMany(position => new[] { true, false }.SelectMany(coach =>
            new[] { "side_bridge", "side_bridge_right" }.Select(key => {
                var action = Action(key, coach); action.prescriptionItemId = 70 + position;
                action.prescriptionPosition = position; action.sets = 1; action.reps = coach ? 2 : 3;
                action.holdSeconds = 0; action.actionName = "侧桥"; return action;
            }))).ToArray();
        var plan = new TrainingPlan { accountId = "clinical-test", patientId = "clinical-test", sessionId = "repeated-bilateral",
            prescriptionOrderId = 21, prescriptionContractVersion = 2, gender = "male", actions = steps };
        Check(TrainingSessionService.PreparePrescriptionPlan(plan), "Repeated bilateral items accept distinct prescription IDs");
        Set(service, "CurrentPlan", plan);
        Check(SpineFlowTrainingSession.BeginUnifiedTraining(plan), "Repeated bilateral session starts");
        Check(service.FeedbackActionCount == 2, "Two prescribed side bridges need two surveys, not four");
        for (int item = 0; item < 2; item++)
        {
            Check(service.SkipRemainingPrescriptionCoaches() == "SideBridge_RingBridge_Dynamic", "Skip both coaches opens left game");
            SpineFlowTrainingSession.MarkPrescriptionGameCompleted(3);
            Check(service.AdvanceWithinPrescriptionItem() == "SideBridgeRight_RingBridge_Dynamic", "Repeated item advances to right game");
            SpineFlowTrainingSession.MarkPrescriptionGameCompleted(3);
            Check((bool)Call(service, "PrepareActionFeedback"), "Repeated item's final side opens survey");
            var result = service.CurrentResult;
            string next = (string)Call(service, "RecordFeedback", "item " + item, "difficulty");
            Check(result.actionFeedbacks.Length == item + 1 && result.actionFeedbacks[item].prescriptionItemId == 70 + item,
                "Repeated side bridges preserve separate item answers");
            Check(item == 0 ? next == plan.actions[4].sceneName : next == null && service.CurrentPlan == null,
                "Survey advances only after the complete bilateral item");
        }
    }

    private static void VerifyAllGamesAndRepeats(TrainingSessionService service, PracticeSessionController practice)
    {
        string[] keys = { "dead_bug_hold30", "bird_dog_hold30", "curl_up_hold30", "cat_camel_hold30", "hip_thrust_curve", "hip_thrust_curve" };
        var actions = keys.SelectMany((key, index) => new[] { true, false }.Select(coach => {
            var action = Action(key, coach);
            action.prescriptionItemId = 100 + index;
            action.prescriptionPosition = index;
            if (key == "hip_thrust_curve") action.holdSeconds = 0;
            return action;
        })).ToArray();
        var plan = new TrainingPlan { accountId = "clinical-test", patientId = "clinical-test", sessionId = "all-games",
            prescriptionOrderId = 20, prescriptionContractVersion = 2, gender = "female", actions = actions };
        Check(TrainingSessionService.PreparePrescriptionPlan(plan), "Four holds, curve and repeated curve form one valid plan");
        Set(service, "CurrentPlan", plan);
        typeof(TrainingSessionService).GetField("actionOffset", Private).SetValue(service, 0);
        Check(SpineFlowTrainingSession.BeginUnifiedTraining(plan), "Expanded plan starts");
        for (int index = 0; index < keys.Length; index++)
        {
            practice.SkipToPrescriptionGame();
            Check(service.AdvancePrescriptionCoach() == TrainingActionCatalog.Find(keys[index]).Game, "Normal coach advances to prescribed game");
            var entry = TrainingActionCatalog.Find(keys[index]);
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + entry.Game + ".unity", OpenSceneMode.Additive);
            try
            {
                var workout = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameWorkoutScene>(true)).Single();
                if (workout.bridgeWave != null)
                {
                    var curve = workout.bridgeWave;
                    curve.skipCalibrationForFlowTest = true;
                    curve.ApplyPrescriptionDose();
                    Check(!curve.skipCalibrationForFlowTest && curve.game.totalCount == 6, "Curve uses full prescription dose and real sensors");
                    curve.ResetSession();
                    Set(curve.Session, "Elapsed", curve.game.standardTrack.Duration(6));
                    Check(curve.CompletedRepetitions == 6, "Curve reports actual completed chart repetitions");
                    Call(curve, "FinishFinalScore");
                    SpineFlowTrainingSession.SaveUnifiedFinalGameAssessment("hip_thrust_curve", "standard", curve.FinalScore, 0, 60, 6);
                    Check(SpineFlowTrainingSession.CaptureUnifiedResult().Contains("\"scoreAvailable\":false"), "Missing curve sensor evidence never fabricates a score");
                    typeof(GameWorkoutScene).GetField("session", Private).SetValue(workout, GameWorkoutSession.Instance);
                    Call(workout, "OnCompleted");
                }
                else
                {
                    var dose = workout.taiko.PrescriptionDose;
                    Call(dose, "Awake");
                    Check(dose.Total == 6 && dose.HoldSeconds == 45, "Recorded hold scene applies prescribed count and seconds: " + keys[index]);
                    SpineFlowTrainingSession.MarkPrescriptionGameCompleted(6);
                }
                Check(SpineFlowTrainingSession.CurrentPrescriptionGameCompleted && (bool)Call(service, "PrepareActionFeedback"), "Game opens its action questionnaire: " + keys[index]);
                var result = service.CurrentResult;
                string next = (string)Call(service, "RecordFeedback", "feeling " + index, "difficulty " + index);
                Check(result.actionFeedbacks.Length == index + 1 && result.actionFeedbacks[index].prescriptionItemId == 100 + index,
                    "Each occurrence owns its answer, including repeated games");
                Check(index == keys.Length - 1 ? next == null : next == TrainingActionCatalog.Find(keys[index + 1]).Coach,
                    "Questionnaire advances to the correct next coach");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }

    private static TrainingAction Action(string key, bool coach)
    {
        var entry = TrainingActionCatalog.Find(key);
        return new TrainingAction { actionId = key + (coach ? "_coach" : "_game"), actionName = entry.Name,
            arTaskType = coach ? "coach_training" : "game_training", sceneName = coach ? entry.Coach : entry.Game,
            prescriptionItemId = 7, prescriptionPosition = 0, sets = coach ? 1 : 2, reps = coach ? 4 : 3,
            holdSeconds = coach ? 0 : 45, level = "standard" };
    }
    private static void Set(object target, string property, object value) => target.GetType()
        .GetField("<" + property + ">k__BackingField", Private).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, Private).Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Debug.Log("[ClinicalPrescription] " + message);
    }
}

