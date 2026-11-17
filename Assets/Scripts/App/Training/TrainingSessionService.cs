using System;
using System.IO;
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
        }

        [Serializable]
        private sealed class ResultState
        {
            public bool completed;
            public string createdAt;
            public string endReason;
        }

        private int actionOffset;
        public bool IsV2Prescription => IsPrescribedTraining && CurrentPlan.prescriptionContractVersion == 2;
        public bool HasNextPrescriptionItem => IsV2Prescription && actionOffset + 2 < CurrentPlan.actions.Length;
        public bool AllowsPrescriptionCoach(string scene) => !IsV2Prescription ||
            CurrentPlan.actions[actionOffset].sceneName == scene;

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
            string gameScene = plan.actionId == "bird_dog" ? CoachModeNavigation.BirdDogGameScene :
                plan.actionId == "hip_thrust" ? CoachModeNavigation.HipThrustGameScene :
                plan.actionId == "side_bridge" ? CoachModeNavigation.SideBridgeGameScene :
                CoachModeNavigation.DeadBugGameScene;
            string targetScene = gameScene;
            if (plan.prescriptionOrderId > 0)
            {
                TrainingAction coach = Array.Find(plan.actions, action => action?.arTaskType == "coach_training");
                if (coach == null || coach.holdSeconds < 0 || coach.holdSeconds > 600) return false;
                switch (plan.actionId)
                {
                    case "dead_bug": targetScene = CoachModeNavigation.DeadBugCoachScene; break;
                    case "bird_dog": targetScene = CoachModeNavigation.BirdDogCoachScene; break;
                    default: return false;
                }
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
                if (coach == null || game == null || coach.arTaskType != "coach_training" ||
                    game.arTaskType != "game_training" || coach.prescriptionItemId <= 0 ||
                    game.prescriptionItemId != coach.prescriptionItemId ||
                    coach.prescriptionPosition != offset / 2 || game.prescriptionPosition != offset / 2 ||
                    coach.sets < 1 || coach.sets > 20 || coach.reps < 1 || coach.reps > 100 ||
                    coach.holdSeconds < 0 || coach.holdSeconds > 600 ||
                    coach.sets != game.sets || coach.reps != game.reps || coach.holdSeconds != game.holdSeconds ||
                    !File.Exists(coach.prescriptionAssetPath) ||
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
            if (!IsV2Prescription || !SpineFlowTrainingSession.CurrentPrescriptionGameCompleted) return;
            if (!HasNextPrescriptionItem)
            {
                FinishAndReturn();
                return;
            }
            actionOffset += 2;
            SpineFlowTrainingSession.AdvanceUnifiedPrescription(actionOffset);
            SceneManager.LoadScene(CurrentPlan.actions[actionOffset].sceneName);
        }

        public void SaveCheckpoint(string resultJson)
        {
            if (CurrentPlan == null || string.IsNullOrEmpty(resultJson)) return;
            ResultState state = JsonUtility.FromJson<ResultState>(resultJson);
            CurrentResult = new TrainingHistoryRecord
            {
                sessionId = CurrentPlan.sessionId, actionId = CurrentPlan.actionId,
                completedAt = state.createdAt, completed = state.completed,
                resultJson = resultJson, preTrainingEventId = CurrentPlan.preTrainingEventId,
                endReason = state.completed ? state.endReason : "", feedbackPending = state.completed,
                planJson = JsonUtility.ToJson(CurrentPlan),
                replaySessionId = ReplaySessionContext.SessionId ?? CurrentResult?.replaySessionId
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
                }
            }
            if (CurrentPlan == null) return;
            SaveToUser();
            if (HasPendingFeedback) AppBootstrap.Instance.GetComponent<AppShellUi>()?.ShowPostTrainingSurvey();
        }

        public void SubmitFeedback(string feeling, string difficulty)
        {
            if (CurrentResult == null) return;
            CurrentResult.feedback = feeling + " / " + difficulty;
            CurrentResult.feedbackPending = false;
            Persist();
            SaveToUser();
            ClearCurrentPlan();
        }

        public void DeferFeedback()
        {
            // Results remain durable; the next login can resume this questionnaire.
            Persist();
            SaveToUser();
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
            JsonFileStore.Save(SessionFile, new SavedSession { plan = CurrentPlan, result = CurrentResult });
        }
    }
}
