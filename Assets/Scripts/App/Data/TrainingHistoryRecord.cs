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
        // Only populated when this form collected a separate score for each point.
        public PainPointSnapshot[] points = Array.Empty<PainPointSnapshot>();
        public bool pointsCaptured;
        public bool painFree;
    }

    [Serializable]
    public sealed class PainPointSnapshot
    {
        public string bodyPart;
        public string bodyPartLabel;
        public string view;
        public int painLevel;
    }

    [Serializable]
    public sealed class HealthEventRecord
    {
        public string eventId;
        public string type;
        public string recordedAt;
        // Frozen at submission. Later edits of the user's current state must not alter this JSON.
        public string answersJson;
        public string[] rawRegions = Array.Empty<string>();
        public string rawLeg;

        public static void Append(UserSession user, string type, string recordedAt, object answers,
            string eventId = null, string[] rawRegions = null, string rawLeg = null)
        {
            var records = user.healthEvents ?? Array.Empty<HealthEventRecord>();
            var updated = new HealthEventRecord[records.Length + 1];
            Array.Copy(records, updated, records.Length);
            updated[records.Length] = new HealthEventRecord
            {
                eventId = eventId ?? type + ":" + Guid.NewGuid().ToString("N"),
                type = type, recordedAt = recordedAt,
                answersJson = UnityEngine.JsonUtility.ToJson(answers),
                rawRegions = rawRegions == null ? Array.Empty<string>() : (string[])rawRegions.Clone(),
                rawLeg = rawLeg,
            };
            user.healthEvents = updated;
        }
    }

    [Serializable]
    public sealed class AdmissionIntakeSnapshot
    {
        public string situation;
        public string symptom;
        public string[] goals;
    }

    [Serializable]
    public sealed class AdmissionSurveySnapshot
    {
        public int painLevel;
        public bool requiresClinicalReview;
    }

    [Serializable]
    public sealed class AdmissionMovementSnapshot
    {
        public string movementType;
        public int completionCount;
        public string painResponse;
        public string firstPainPhase;
        public int painLevel;
    }

    [Serializable]
    public sealed class AdmissionAssessmentSnapshot
    {
        public AdmissionMovementSnapshot flexion;
        public AdmissionMovementSnapshot extension;
        public string completedAt;
    }

    [Serializable]
    public sealed class PreTrainingSnapshot
    {
        public int prePainLevel;
        public bool allowedToTrain;
        public string recordedAt;
    }

    [Serializable]
    public sealed class TrainingHistoryRecord
    {
        public string sessionId;
        public string completedAt;
        public string actionId;
        public string resultJson;
        public string networkResultJson;
        public string networkResultError;
        public string preTrainingEventId;
        public string feedback;
        public string feedbackFeeling;
        public string feedbackDifficulty;
        public string feedbackRecordedAt;
        public bool completed;
        public bool feedbackPending;
        public string endReason;
        public string planJson;
        public string replaySessionId;
    }

    /// <summary>
    /// One completed monthly assessment (JOA self-assessment + SF-MPQ). Every
    /// answer is the 0-based index of the chosen option, in the order the
    /// options appear on screen, where 0 is always the mildest option.
    /// </summary>
    [Serializable]
    public sealed class MonthlyQuestionnaireRecord
    {
        public string id;
        public string completedAt;
        /// <summary>Assignment this submission answers; empty for legacy local entries.</summary>
        public string assignmentId;
        /// <summary>Bit mask of the assigned pages (1 = JOA page 1, 2 = SF-MPQ page 2).</summary>
        public int modules;
        /// <summary>Lower back pain: 0 不疼 … 3 经常或一直很疼.</summary>
        public int backPain;
        /// <summary>Leg pain / numbness: 0 没有 … 3 经常或一直很严重.</summary>
        public int legPain;
        /// <summary>Walking: 0 正常 … 3 走不到 100 米.</summary>
        public int walking;
        /// <summary>
        /// 0 不受限, 1 有点受限, 2 很受限 for: 平躺时翻身, 站立, 弯腰洗漱,
        /// 向前弯腰, 坐着约 1 小时, 提或拿重物, 走路.
        /// </summary>
        public int[] activityLimits = Array.Empty<int>();
        /// <summary>Urination: 0 正常, 1 有点排尿困难, 2 排尿明显困难.</summary>
        public int urination;
        /// <summary>SF-MPQ sensory words (11), 0 无, 1 轻, 2 中, 3 重.</summary>
        public int[] sfmpqSensory = Array.Empty<int>();
        /// <summary>SF-MPQ affective words (4), 0 无, 1 轻, 2 中, 3 重.</summary>
        public int[] sfmpqAffective = Array.Empty<int>();
        // Retained verbatim for a future, explicitly supported frozen server schema.
        public long taskId;
        public string templateVersion;
        public string requestId;
        public string schemaJson;
        public string answersJson;
        public string submissionState;
    }
}
