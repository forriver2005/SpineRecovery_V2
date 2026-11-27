using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpineRecovery.App
{
    public sealed class TrainingSessionService : MonoBehaviour
    {
        [Serializable]
        private sealed class SavedSession
        {
            public TrainingPlan plan;
            public TrainingHistoryRecord result;
            public int actionOffset;
        }

        [Serializable]
        private sealed class ResultState
        {
            public bool completed;
            public string createdAt;
            public string endReason;
        }

        private int actionOffset;
        private bool openingSurvey;
        // Pending questionnaires from the former left/right pair format keep
        // their original indices. New prescriptions always use grouped stages.
        private bool LegacyBilateralPairs => CurrentPlan.actions.GroupBy(a => a.prescriptionItemId).Any(g =>
            g.Count() > 2 && g.ElementAt(1).arTaskType == "game_training" && g.Skip(2).Any(a => a.arTaskType == "coach_training"));
        private TrainingAction[] PrescriptionItems => LegacyBilateralPairs
            ? CurrentPlan.actions.Where((a, index) => index % 2 == 0).ToArray()
            : CurrentPlan.actions.GroupBy(a => a.prescriptionItemId).Select(g => g.First()).ToArray();
        private TrainingAction CurrentStep => CurrentPlan.actions[actionOffset];
        private int CurrentItemIndex => LegacyBilateralPairs ? actionOffset / 2 :
            Array.FindIndex(PrescriptionItems, a => a.prescriptionItemId == CurrentStep.prescriptionItemId);
        public int FeedbackActionCount => IsV2Prescription ? PrescriptionItems.Length : 1;
        public int FeedbackActionIndex
        {
            get
            {
                if (CurrentResult == null) return -1;
                int last = CurrentResult.completed ? FeedbackActionCount - 1 :
                    Mathf.Min(CurrentResult.feedbackActionIndex, FeedbackActionCount - 1);
                for (int index = 0; index <= last; index++)
                    if (!Array.Exists(CurrentResult.actionFeedbacks ?? Array.Empty<ActionTrainingFeedback>(), row => row.actionIndex == index)) return index;
                return -1;
            }
        }
        private TrainingAction FeedbackAction => CurrentPlan?.actions != null && CurrentPlan.actions.Length > 0 ?
            (IsV2Prescription ? PrescriptionItems[Mathf.Clamp(FeedbackActionIndex, 0, FeedbackActionCount - 1)] : CurrentPlan.actions[0]) : null;
        public string FeedbackActionName => !string.IsNullOrEmpty(FeedbackAction?.actionName) ? FeedbackAction.actionName :
            GameWorkoutPlan.ActionName(FeedbackAction?.actionId ?? CurrentPlan?.actionId);
        public bool CanContinueAfterFeedback => IsV2Prescription && HasNextPrescriptionItem &&
            string.IsNullOrEmpty(CurrentResult?.endReason) && SpineFlowTrainingSession.CurrentPrescriptionGameCompleted;
        public bool IsV2Prescription => IsPrescribedTraining && CurrentPlan.prescriptionContractVersion == 2;
        public string CurrentPrescriptionDifficulty => IsV2Prescription ? CurrentStep.level : null;
        public bool HasNextPrescriptionItem => IsV2Prescription && CurrentItemIndex + 1 < FeedbackActionCount;
        public bool HasNextStepInItem => IsV2Prescription && actionOffset + 1 < CurrentPlan.actions.Length &&
            CurrentPlan.actions[actionOffset + 1].prescriptionItemId == CurrentStep.prescriptionItemId;
        public string CoachContinueLabel => HasNextStepInItem && CurrentPlan.actions[actionOffset + 1].arTaskType == "coach_training"
            ? "继续下一侧教练" : "进入本项游戏";
        public string CurrentPrescriptionGameScene => !IsV2Prescription ? null : CurrentStep.arTaskType == "game_training"
            ? CurrentStep.sceneName : CurrentPlan.actions.First(a => a.prescriptionItemId == CurrentStep.prescriptionItemId && a.arTaskType == "game_training").sceneName;
        public bool AllowsPrescriptionCoach(string scene) => !IsV2Prescription ||
            CurrentStep.arTaskType == "coach_training" && SameCoachScene(CurrentStep.sceneName, scene);

        private static bool SameCoachScene(string planned, string actual) => planned == actual ||
            (TrainingActionCatalog.ForCoach(planned) != null &&
             TrainingActionCatalog.ForCoach(planned) == TrainingActionCatalog.ForCoach(actual));

        public bool AllowsPrescriptionGame(string scene) => !IsV2Prescription ||
            (CurrentStep.arTaskType == "game_training" && CurrentStep.sceneName == scene &&
                SpineFlowTrainingSession.CurrentPrescriptionCoachReady);

        public string AdvancePrescriptionCoach()
        {
            if (!IsV2Prescription || CurrentStep.arTaskType != "coach_training" ||
                !SpineFlowTrainingSession.CurrentPrescriptionCoachReady) return null;
            return AdvanceStep();
        }

        public string SkipRemainingPrescriptionCoaches()
        {
            string next = null;
            while (IsV2Prescription && CurrentStep.arTaskType == "coach_training")
            {
                SpineFlowTrainingSession.SkipPrescriptionCoach();
                next = AdvanceStep();
            }
            return next;
        }

        private string AdvanceStep()
        {
            actionOffset++;
            SpineFlowTrainingSession.AdvanceUnifiedPrescription(actionOffset);
            return CurrentStep.sceneName;
        }

        public string AdvanceWithinPrescriptionItem()
        {
            if (!HasNextStepInItem || CurrentStep.arTaskType != "game_training" ||
                !SpineFlowTrainingSession.CurrentPrescriptionGameCompleted) return null;
            return AdvanceStep();
        }

        private const string SessionFile = "unified-training-session.json";
        public TrainingPlan CurrentPlan { get; private set; }
        public TrainingHistoryRecord CurrentResult { get; private set; }
        public event Action<TrainingPlan> TrainingStarted;
        public bool HasPendingFeedback => CurrentResult != null && CurrentResult.feedbackPending;
        public bool IsPrescribedTraining => CurrentPlan != null && CurrentPlan.prescriptionOrderId > 0;

        private void Awake()
        {
            if (!JsonFileStore.TryLoad(SessionFile, out SavedSession saved)) return;
            CurrentPlan = saved.plan;
            CurrentResult = saved.result;
            actionOffset = saved.actionOffset;
            // A restarted process cannot continue an in-memory tracking attempt.
            if (CurrentResult != null && !CurrentResult.completed && string.IsNullOrEmpty(CurrentResult.endReason))
            {
                CurrentResult.endReason = "PROCESS_RESTART";
                CurrentResult.feedbackPending = true;
                Persist();
            }
        }

        public void ClearCurrentPlan()
        {
            CurrentPlan = null;
            CurrentResult = null;
            actionOffset = 0;
            openingSurvey = false;
            SpineFlowTrainingSession.EndUnifiedTraining();
            string path = JsonFileStore.PathFor(SessionFile);
            if (File.Exists(path)) File.Delete(path);
        }

        public bool StartTraining(TrainingPlan plan)
        {
            if (plan == null || plan.actions == null || plan.actions.Length < 2) return false;
            if (plan.prescriptionContractVersion == 2) return StartV2Training(plan);
            if (plan.actions.Length != 2) return false;
            TrainingAction game = Array.Find(plan.actions, action => action?.arTaskType == "game_training");
            if (game == null) return false;
            var entry = TrainingActionCatalog.Find(plan.actionId);
            if (entry == null) return false;
            string gameScene = entry.Game;
            string targetScene = gameScene;
            if (plan.prescriptionOrderId > 0)
            {
                TrainingAction coach = Array.Find(plan.actions, action => action?.arTaskType == "coach_training");
                if (coach == null || coach.holdSeconds < 0 || coach.holdSeconds > 600) return false;
                targetScene = SpineFlowTrainingSession.ResolveSceneForGender(entry.Coach, plan.gender == "male");
            }
            if (!Application.CanStreamedLevelBeLoaded(targetScene)) return false;
            if (HasPendingFeedback && CurrentPlan.accountId == plan.accountId) return false;
            // Clone once so later UI/config edits cannot change the active prescription.
            plan = JsonUtility.FromJson<TrainingPlan>(JsonUtility.ToJson(plan));
            if (string.IsNullOrWhiteSpace(plan.patientId)) plan.patientId = plan.accountId;
            plan.targetScene = targetScene;
            if (plan.prescriptionOrderId > 0)
                Array.Find(plan.actions, action => action?.arTaskType == "coach_training").sceneName = targetScene;
            Array.Find(plan.actions, action => action?.arTaskType == "game_training").sceneName = gameScene;
            plan.sessionId = Guid.NewGuid().ToString("N");
            plan.startedAt = DateTimeOffset.UtcNow.ToString("O");
            if (!SpineFlowTrainingSession.BeginUnifiedTraining(plan)) return false;
            ReplaySessionContext.Clear();
            CurrentPlan = plan;
            CurrentResult = null;
            SaveCheckpoint(SpineFlowTrainingSession.CaptureUnifiedResult());
            TrainingStarted?.Invoke(plan);
            ScreenOrientationService.SetLandscape();
            SceneManager.LoadScene(plan.targetScene, LoadSceneMode.Single);
            return true;
        }

        private bool StartV2Training(TrainingPlan plan)
        {
            if (HasPendingFeedback && CurrentPlan.accountId == plan.accountId) return false;
            plan = JsonUtility.FromJson<TrainingPlan>(JsonUtility.ToJson(plan));
            if (!PreparePrescriptionPlan(plan)) return false;
            if (Array.Exists(plan.actions, action => !Application.CanStreamedLevelBeLoaded(action.sceneName))) return false;
            plan.sessionId = Guid.NewGuid().ToString("N");
            plan.startedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
            plan.targetScene = plan.actions[0].sceneName;
            actionOffset = 0;
            if (!SpineFlowTrainingSession.BeginUnifiedTraining(plan)) return false;
            CurrentPlan = plan;
            CurrentResult = null;
            ReplaySessionContext.Clear();
            SaveCheckpoint(SpineFlowTrainingSession.CaptureUnifiedResult());
            TrainingStarted?.Invoke(plan);
            ScreenOrientationService.SetLandscape();
            SceneManager.LoadScene(plan.targetScene);
            return true;
        }

        public static bool PreparePrescriptionPlan(TrainingPlan plan)
        {
            if (!PrescriptionSequence.IsValid(plan)) return false;
            foreach (var action in plan.actions)
                if (action.arTaskType == "coach_training")
                    action.sceneName = SpineFlowTrainingSession.ResolveSceneForGender(action.sceneName, plan.gender == "male");
            return true;
        }

        public void ContinuePrescription()
        {
            string next = AdvanceWithinPrescriptionItem();
            if (next != null)
            {
                ScreenOrientationService.SetLandscape();
                SceneManager.LoadScene(next, LoadSceneMode.Single);
                return;
            }
            if (!PrepareActionFeedback()) return;
            ScreenOrientationService.SetPortrait();
            SceneManager.LoadScene("AppShell", LoadSceneMode.Single);
        }

        private bool PrepareActionFeedback()
        {
            if (!IsV2Prescription || !SpineFlowTrainingSession.CurrentPrescriptionGameCompleted || HasNextStepInItem || openingSurvey) return false;
            openingSurvey = true;
            SaveCheckpoint(SpineFlowTrainingSession.CaptureUnifiedResult());
            CurrentResult.feedbackPending = true;
            Persist();
            SaveToUser();
            return true;
        }

        public void SaveCheckpoint(string resultJson)
        {
            if (CurrentPlan == null || string.IsNullOrEmpty(resultJson)) return;
            ResultState state = JsonUtility.FromJson<ResultState>(resultJson);
            TrainingHistoryRecord previous = CurrentResult;
            CurrentResult = new TrainingHistoryRecord
            {
                sessionId = CurrentPlan.sessionId, actionId = CurrentPlan.actionId,
                completedAt = state.createdAt, completed = state.completed,
                resultJson = resultJson, preTrainingEventId = CurrentPlan.preTrainingEventId,
                endReason = state.completed ? state.endReason : "", feedbackPending = state.completed || previous?.feedbackPending == true,
                planJson = JsonUtility.ToJson(CurrentPlan),
                replaySessionId = ReplaySessionContext.SessionId ?? previous?.replaySessionId,
                feedbackActionIndex = IsV2Prescription ? CurrentItemIndex : 0,
                actionFeedbacks = previous?.actionFeedbacks ?? Array.Empty<ActionTrainingFeedback>()
            };
            Persist();
            SaveToUser();
        }

        public void FinishAndReturn()
        {
            if (CurrentPlan != null)
            {
                string snapshot = SpineFlowTrainingSession.CaptureUnifiedResult();
                if (!string.IsNullOrEmpty(snapshot)) SaveCheckpoint(snapshot);
                if (CurrentResult != null)
                {
                    CurrentResult.feedbackPending = true;
                    if (!CurrentResult.completed) CurrentResult.endReason = "USER_EXIT";
                    Persist();
                    SaveToUser();
                }
            }
            ScreenOrientationService.SetPortrait();
            SceneManager.LoadScene("AppShell", LoadSceneMode.Single);
        }

        public void RestoreForCurrentUser()
        {
            UserSession user = AppBootstrap.Instance.State.Session;
            if (user == null) return;
            if (CurrentPlan == null || CurrentPlan.accountId != user.accountId)
            {
                CurrentPlan = null;
                CurrentResult = null;
                foreach (var row in user.trainingHistory ?? Array.Empty<TrainingHistoryRecord>())
                {
                    if (!row.feedbackPending || string.IsNullOrEmpty(row.planJson)) continue;
                    CurrentResult = row;
                    CurrentPlan = JsonUtility.FromJson<TrainingPlan>(row.planJson);
                    actionOffset = LegacyBilateralPairs ? row.feedbackActionIndex * 2 : IsV2Prescription ? Array.FindIndex(CurrentPlan.actions,
                        a => a.prescriptionItemId == PrescriptionItems[row.feedbackActionIndex].prescriptionItemId) : 0;
                }
            }
            if (CurrentPlan == null) return;
            SaveToUser();
            if (HasPendingFeedback) AppBootstrap.Instance.GetComponent<AppShellUi>()?.ShowPostTrainingSurvey();
        }

        public bool SubmitFeedback(string feeling, string difficulty)
        {
            string nextScene = RecordFeedback(feeling, difficulty);
            if (string.IsNullOrEmpty(nextScene)) return false;
            ScreenOrientationService.SetLandscape();
            SceneManager.LoadScene(nextScene, LoadSceneMode.Single);
            return true;
        }

        private string RecordFeedback(string feeling, string difficulty)
        {
            if (!HasPendingFeedback || FeedbackActionIndex < 0) return null;
            int index = FeedbackActionIndex;
            TrainingAction action = FeedbackAction;
            var answers = new List<ActionTrainingFeedback>(CurrentResult.actionFeedbacks ?? Array.Empty<ActionTrainingFeedback>());
            answers.Add(new ActionTrainingFeedback { actionIndex = index, actionId = action?.actionId ?? CurrentPlan.actionId,
                actionName = FeedbackActionName, prescriptionItemId = action?.prescriptionItemId ?? 0,
                recordedAt = DateTimeOffset.UtcNow.ToString("O"), feeling = feeling, difficulty = difficulty });
            CurrentResult.actionFeedbacks = answers.ToArray();
            CurrentResult.feedbackPending = FeedbackActionIndex >= 0;
            Persist();
            SaveToUser();
            if (HasPendingFeedback) return null;
            openingSurvey = false;
            if (CanContinueAfterFeedback)
            {
                return AdvanceStep();
            }
            ClearCurrentPlan();
            return null;
        }

        public void DeferFeedback()
        {
            // Results remain durable; the next login can resume this questionnaire.
            Persist();
            SaveToUser();
            openingSurvey = false;
        }

        private void SaveToUser()
        {
            AppBootstrap app = AppBootstrap.Instance;
            UserSession user = app?.State?.Session;
            if (user == null || CurrentPlan == null || CurrentResult == null ||
                user.accountId != CurrentPlan.accountId) return;
            app.Users.AppendTrainingRecord(user, CurrentResult);
        }

        private void Persist()
        {
            JsonFileStore.Save(SessionFile, new SavedSession { plan = CurrentPlan, result = CurrentResult, actionOffset = actionOffset });
        }
    }
}
