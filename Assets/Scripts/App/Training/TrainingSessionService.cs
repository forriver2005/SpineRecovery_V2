using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpineRecovery.App
{
    public sealed class TrainingSessionService : MonoBehaviour
    {
        public TrainingPlan CurrentPlan { get; private set; }
        public event Action<TrainingPlan> TrainingStarted;

        public void ClearCurrentPlan()
        {
            CurrentPlan = null;
        }

        public bool StartTraining(TrainingPlan plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.targetScene))
            {
                Debug.LogWarning("Training plan has no target scene.");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(plan.targetScene))
            {
                Debug.LogError("Training scene is not in Build Settings: " + plan.targetScene);
                return false;
            }

            CurrentPlan = plan;
            TrainingStarted?.Invoke(plan);
            ScreenOrientationService.SetLandscape();
            SceneManager.LoadScene(plan.targetScene, LoadSceneMode.Single);
            return true;
        }
    }
}
