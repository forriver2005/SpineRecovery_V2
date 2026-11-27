using System;
using System.IO;
using System.Collections.Generic;
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
        public int FeedbackActionCount => IsV2Prescription ? CurrentPlan.actions.Length / 2 : 1;
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
            CurrentPlan.actions[Mathf.Clamp(IsV2Prescription ? FeedbackActionIndex * 2 : 0, 0, CurrentPlan.actions.Length - 1)] : null;
        public string FeedbackActionName => !string.IsNullOrEmpty(FeedbackAction?.actionName) ? FeedbackAction.actionName :
            GameWorkoutPlan.ActionName(FeedbackAction?.actionId ?? CurrentPlan?.actionId);
        public bool CanContinueAfterFeedback => IsV2Prescription && HasNextPrescriptionItem &&
            string.IsNullOrEmpty(CurrentResult?.endReason) && SpineFlowTrainingSession.CurrentPrescriptionGameCompleted;
        public bool IsV2Prescription => IsPrescribedTraining && CurrentPlan.prescriptionContractVersion == 2;
        // The active item advances by a coach/game pair; both modes use the same prescribed difficulty.
        public string CurrentPrescriptionDifficulty => IsV2Prescription && CurrentPlan.actions != null &&
            actionOffset < CurrentPlan.actions.Length ? CurrentPlan.actions[actionOffset].level : null;
        public bool HasNextPrescriptionItem => IsV2Prescription && actionOffset + 2 < CurrentPlan.actions.Length;
        public string CurrentPrescriptionGameScene => IsV2Prescription ? CurrentPlan.actions[actionOffset + 1].sceneName : null;
        public bool AllowsPrescriptionCoach(string scene) => !IsV2Prescription ||
            SameCoachScene(CurrentPlan.actions[actionOffset].sceneName, scene);

        private static bool SameCoachScene(string planned, string actual) => planned == actual ||
            (TrainingActionCatalog.ForCoach(planned) != null &&
             TrainingActionCatalog.ForCoach(planned) == TrainingActionCatalog.ForCoach(actual));

        public bool AllowsPrescriptionGame(string scene) => !IsV2Prescription ||
            (CurrentPlan.actions[actionOffset + 1].sceneName == scene &&
                SpineFlowTrainingSession.CurrentPrescriptionCoachCompleted);

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
            if (plan.prescriptionOrderId <= 0 || plan.actions.Length > 20 || plan.actions.Length % 2 != 0 ||
                (HasPendingFeedback && CurrentPlan.accountId == plan.accountId)) return false;
            plan = JsonUtility.FromJson<TrainingPlan>(JsonUtility.ToJson(plan));
            for (int offset = 0; offset < plan.actions.Length; offset += 2)
            {
                TrainingAction coach = plan.actions[offset];
                TrainingAction game = plan.actions[offset + 1];
                var entry = TrainingActionCatalog.Find(coach?.actionId);
                if (entry == null || TrainingActionCatalog.Find(game?.actionId) != entry ||
                    !SameCoachScene(entry.Coach, coach.sceneName) || game.sceneName != entry.Game) return false;
                coach.sceneName = SpineFlowTrainingSession.ResolveSceneForGender(entry.Coach, plan.gender == "male");
                if (coach == null || game == null || coach.arTaskType != "coach_training" ||
                    game.arTaskType != "game_training" || coach.prescriptionItemId <= 0 ||
                    game.prescriptionItemId != coach.prescriptionItemId ||
                    coach.prescriptionPosition != offset / 2 || game.prescriptionPosition != offset / 2 ||
                    coach.sets < 1 || coach.sets > 20 || coach.reps < 1 || coach.reps > 100 ||
                    coach.holdSeconds < 0 || coach.holdSeconds > 600 ||
                    (coach.level != "simple" && coach.level != "standard" && coach.level != "hard") ||
                    coach.level != game.level ||
                    coach.sets != game.sets || coach.reps != game.reps || coach.holdSeconds != game.holdSeconds ||
                    !Application.CanStreamedLevelBeLoaded(coach.sceneName) ||
                    !Application.CanStreamedLevelBeLoaded(game.sceneName)) return false;
            }
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

        public void ContinuePrescription()
        {
            if (!PrepareActionFeedback()) return;
            ScreenOrientationService.SetPortrait();
            SceneManager.LoadScene("AppShell", LoadSceneMode.Single);
        }

        private bool PrepareActionFeedback()
        {
            if (!IsV2Prescription || !SpineFlowTrainingSession.CurrentPrescriptionGameCompleted || openingSurvey) return false;
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
                feedbackActionIndex = IsV2Prescription ? actionOffset / 2 : 0,
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
                    actionOffset = row.feedbackActionIndex * 2;
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
                actionOffset += 2;
                SpineFlowTrainingSession.AdvanceUnifiedPrescription(actionOffset);
                return CurrentPlan.actions[actionOffset].sceneName;
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
