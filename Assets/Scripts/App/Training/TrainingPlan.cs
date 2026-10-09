using System;

namespace SpineRecovery.App
{
    [Serializable]
    public sealed class TrainingAction
    {
        public string actionId;
        public string actionName;
        public string arTaskType;
        public string level = "standard";
        public int sets = 1;
        public int reps = 1;
        public string sceneName;
    }

    [Serializable]
    public sealed class TrainingPlan
    {
        public string sessionId;
        public string actionId;
        public string targetScene;
        public TrainingAction[] actions = Array.Empty<TrainingAction>();
    }
}
