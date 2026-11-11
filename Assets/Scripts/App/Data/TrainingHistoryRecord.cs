using System;

namespace SpineRecovery.App
{
    [Serializable]
    public sealed class PainHistoryRecord
    {
        public string id;
        public string recordedAt;
        public int painLevel;
        public string source;
        public string[] painParts = Array.Empty<string>();
    }

    [Serializable]
    public sealed class TrainingHistoryRecord
    {
        public string sessionId;
        public string completedAt;
        public string actionId;
        public string resultJson;
        public string preTrainingEventId;
        public string feedback;
        public bool completed;
        public bool feedbackPending;
        public string endReason;
        public string planJson;
        public string replaySessionId;
    }
}
