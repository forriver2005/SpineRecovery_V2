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

        private const string SessionFile = "unified-training-session.json";
        public TrainingPlan CurrentPlan { get; private set; }
        public TrainingHistoryRecord CurrentResult { get; private set; }
        public event Action<TrainingPlan> TrainingStarted;
        public bool HasPendingFeedback => CurrentResult != null && CurrentResult.feedbackPending;

        private void Awake()
        {
            if (!JsonFileStore.TryLoad(SessionFile, out SavedSession saved)) return;
            CurrentPlan = saved.plan;
            CurrentResult = saved.result;
            // A restarted process cannot continue an in-memory tracking attempt.
            if (CurrentResult != null && !CurrentResult.completed)
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
            if (plan == null || plan.actions == null || plan.actions.Length != 2 ||
                !Application.CanStreamedLevelBeLoaded(plan.targetScene)) return false;
            if (HasPendingFeedback && CurrentPlan.accountId == plan.accountId) return false;
            // Clone once so later UI/config edits cannot change the active prescription.
            plan = JsonUtility.FromJson<TrainingPlan>(JsonUtility.ToJson(plan));
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
