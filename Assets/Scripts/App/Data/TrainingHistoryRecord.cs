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
    }
}
