using System;
using System.IO;
using System.Reflection;
using SpineRecovery.App;
using UnityEngine;

// Run only in an isolated batch editor. Restore the pre-existing local session afterwards.
public static class PostActionFeedbackVerification
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static int checks;
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use batch mode.");
        string path = JsonFileStore.PathFor("unified-training-session.json");
        byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var host = new GameObject("Feedback verification");
        host.SetActive(false);
        var service = host.AddComponent<TrainingSessionService>();
        try
        {
            var plan = new TrainingPlan { sessionId = "survey-test", accountId = "survey-test",
                prescriptionOrderId = 12, prescriptionContractVersion = 2, actions = new[] {
                    Action(0, "deadbug", "死虫"), Action(0, "deadbug", "死虫"),
                    Action(1, "deadbug", "死虫"), Action(1, "deadbug", "死虫") } };
            Set(service, "CurrentPlan", plan);
            Set(service, "CurrentResult", new TrainingHistoryRecord { sessionId = plan.sessionId,
                completed = true, feedbackPending = true, feedbackActionIndex = 1, endReason = "COMPLETED" });
            Check(service.FeedbackActionCount == 2 && service.FeedbackActionIndex == 0, "Coach/game pair shares one action questionnaire");
            Check(!service.SubmitFeedback("pain 1", "intensity 2") && service.HasPendingFeedback,
                "First completed action does not discard the second questionnaire");
            Check(service.FeedbackActionIndex == 1 && service.CurrentResult.actionFeedbacks[0].prescriptionItemId == 101,
                "Answer is associated with its prescription position");
            service.DeferFeedback();
            typeof(TrainingSessionService).GetMethod("Awake", Private).Invoke(service, null);
            Check(service.FeedbackActionIndex == 1 && service.CurrentResult.actionFeedbacks[0].feeling == "pain 1",
                "Deferred questionnaire survives reload with earlier answer intact");
            var result = service.CurrentResult;
            service.SubmitFeedback("pain 3", "intensity 4");
            Check(result.actionFeedbacks.Length == 2 && result.actionFeedbacks[1].prescriptionItemId == 102 &&
                result.actionFeedbacks[1].actionId == result.actionFeedbacks[0].actionId,
                "Repeated movement in different prescription positions gets separate answers");
            Check(!result.feedbackPending && service.CurrentPlan == null && !File.Exists(path),
                "Final submission completes the questionnaire queue");
            service.SubmitFeedback("duplicate", "duplicate");
            Check(result.actionFeedbacks.Length == 2, "Repeated final submission is harmless");

            Set(service, "CurrentPlan", plan);
            Set(service, "CurrentResult", new TrainingHistoryRecord { sessionId = plan.sessionId,
                feedbackActionIndex = 0, actionFeedbacks = new[] { result.actionFeedbacks[0] } });
            typeof(TrainingSessionService).GetField("actionOffset", Private).SetValue(service, 2);
            service.SaveCheckpoint("{\"completed\":false,\"createdAt\":\"2026-11-27T10:00:00Z\"}");
            Check(service.CurrentResult.actionFeedbacks.Length == 1 && service.CurrentResult.feedbackActionIndex == 1 &&
                !service.HasPendingFeedback, "Next action checkpoint preserves earlier feedback without reopening it");
            typeof(TrainingSessionService).GetMethod("Awake", Private).Invoke(service, null);
            Check(service.CurrentResult.endReason == "PROCESS_RESTART" && service.HasPendingFeedback &&
                service.FeedbackActionIndex == 1 && !service.CanContinueAfterFeedback,
                "Interrupted session resumes only the unfinished action questionnaire");
            service.SubmitFeedback("pain 0", "intensity 1");
            Check(service.CurrentPlan == null, "Interrupted final questionnaire clears the durable session");
            var app = host.AddComponent<AppBootstrap>();
            Set(app, "Training", service);
            typeof(AppBootstrap).GetField("<Instance>k__BackingField", Private).SetValue(null, app);
            var live = host.AddComponent<SpineFlowTrainingSession>();
            typeof(SpineFlowTrainingSession).GetField("instance", Private).SetValue(null, live);
            Set(service, "CurrentPlan", plan);
            for (int i = 0; i < plan.actions.Length; i++) {
                plan.actions[i].arTaskType = i % 2 == 0 ? "coach_training" : "game_training";
                plan.actions[i].sceneName = i % 2 == 0 ? "DeadBugPractice" : "DeadBugGaming";
            }
            Check(SpineFlowTrainingSession.BeginUnifiedTraining(plan), "Live multi-action prescription starts");
            Check(!(bool)Call(service, "PrepareActionFeedback"), "Unfinished action cannot open a completion questionnaire");
            for (int index = 0; index < 2; index++) {
                typeof(SpineFlowTrainingSession).GetField("prescriptionGameCompleted", Private).SetValue(live, true);
                var measurement = typeof(SpineFlowTrainingSession).GetMethod("CreateUnavailableMeasurement", Private)
                    .Invoke(null, new object[] { "coach" });
                typeof(SpineFlowTrainingSession).GetField("coachMeasurement", Private).SetValue(live, measurement);
                Check((bool)Call(service, "PrepareActionFeedback") && service.FeedbackActionIndex == index,
                    "Completed action opens its own questionnaire: " + index);
                Check(!(bool)Call(service, "PrepareActionFeedback"), "Repeated result click does not reopen questionnaire");
                var liveResult = service.CurrentResult;
                string next = (string)Call(service, "RecordFeedback", "pain " + index, "intensity " + index);
                if (index == 0) Check(next == "DeadBugPractice" && !service.HasPendingFeedback &&
                    !SpineFlowTrainingSession.CurrentPrescriptionGameCompleted && service.CurrentResult.actionFeedbacks.Length == 1,
                    "First answer advances the live prescription and preserves its feedback");
                else Check(next == null && service.CurrentPlan == null && liveResult.actionFeedbacks.Length == 2,
                    "Last action finishes without an extra aggregate questionnaire");
            }
            Debug.Log("[PostActionFeedbackVerification] PASS: " + checks + " checks");
        }
        finally
        {
            typeof(AppBootstrap).GetField("<Instance>k__BackingField", Private).SetValue(null, null);
            typeof(SpineFlowTrainingSession).GetField("instance", Private).SetValue(null, null);
            UnityEngine.Object.DestroyImmediate(host);
            if (original != null) File.WriteAllBytes(path, original);
            else if (File.Exists(path)) File.Delete(path);
        }
    }
    private static TrainingAction Action(int index, string id, string name) => new TrainingAction {
        actionId = id, actionName = name, prescriptionPosition = index, prescriptionItemId = 101 + index };
    private static void Set(object target, string property, object value) => target.GetType()
        .GetField("<" + property + ">k__BackingField", Private).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
        Debug.Log("[PostActionFeedbackVerification] " + message);
    }
}
