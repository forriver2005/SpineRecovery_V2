using System;

namespace SpineRecovery.App
{
    public enum UserRole
    {
        Patient,
        Administrator,
    }

    public enum UserLifecycle
    {
        AdmissionPending,
        ReturningUser,
    }

    [Serializable]
    public sealed class UserProfile
    {
        public string accountId;
        public string password;
        public string activationCode;
        public string patientId;
        public bool activationBound;
        public string activationBoundAt;
        public string gender;
        public int ageYears;
        public int heightCm;
        public int weightKg;
    }

    [Serializable]
    public sealed class UserSession
    {
        public string accountId;
        public UserRole role = UserRole.Patient;
        public UserLifecycle lifecycle;
        public UserProfile profile = new UserProfile();
        public string[] userTags = Array.Empty<string>();
        public string basicSituation;
        public string[] symptoms = Array.Empty<string>();
        public string[] goals = Array.Empty<string>();
        public string[] painParts = Array.Empty<string>();
        public int[] painLevels = Array.Empty<int>();
        public bool painFree;
        public int latestPainLevel;
        public string symptomChange;
        public bool intakeCompleted;
        public bool intakeReviewRequired;
        public bool admissionCompleted;
        public bool preTrainingCheckPassed;
        public int flexionPainLevel;
        public int extensionPainLevel;
        public string flexionResponse;
        public string extensionResponse;
        public string flexionPainPhase;
        public string extensionPainPhase;
        public int flexionCompletions;
        public int extensionCompletions;
        public string admissionCompletedAt;
        public bool admissionReviewRequired;
        public PainHistoryRecord[] painHistory = Array.Empty<PainHistoryRecord>();
        public TrainingHistoryRecord[] trainingHistory = Array.Empty<TrainingHistoryRecord>();
        public MonthlyQuestionnaireRecord[] monthlyHistory = Array.Empty<MonthlyQuestionnaireRecord>();
        public string routineQuestionnaireCompletedAt;
        public int routineQuestionnairePainLevel;
        public string routineQuestionnaireSymptomChange;
        public string routineQuestionnaireAnswers;

        public bool IsReturningUser => lifecycle == UserLifecycle.ReturningUser;
        public bool IsAdministrator => role == UserRole.Administrator;
    }
}
