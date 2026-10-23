using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SpineRecovery.App
{
    public sealed class TrainingModeFacts
    {
        public string mode;
        public bool started;
        public bool measurementAvailable;
        public bool completionObserved;
        public int completedReps;
        public int durationSeconds;
        public bool scoreAvailable;
        public int score;
    }

    [Serializable]
    public sealed class NetworkModeResult
    {
        public string mode;
        public string status;
        public int completedReps;
        public int durationSeconds;
        // -1 is only an internal JsonUtility surrogate for the JSON null score.
        public int score = -1;
        public string source;
    }

    [Serializable]
    public sealed class NetworkItemResult
    {
        public long itemId;
        public int position;
        public string actionId;
        public long actionVersionId;
        public string variantKey;
        public string sha256;
        public string status;
        public NetworkModeResult[] modeResults;
    }

    [Serializable]
    public sealed class TrainingNetworkResult
    {
        public int protocolVersion = 2;
        public string sessionId;
        public string requestToken;
        public string userId;
        public string createdAt;
        public bool completed;
        public string status;
        public int durationSeconds;
        public long prescriptionOrderId;
        public string resourceManifestSha256;
        public long resultRevision;
        public string source;
        public string preTrainingEventId;
        public NetworkItemResult[] itemResults;

        private string ToWireJson()
        {
            return JsonUtility.ToJson(this).Replace("\"score\":-1", "\"score\":null");
        }

        public static bool TryCreate(TrainingPlan plan, TrainingModeFacts[] facts, string previousJson,
            out string json, out string error)
        {
            json = previousJson;
            error = null;
            try
            {
                Require(plan != null && plan.TryReadPrescription(out _), "PRESCRIPTION_IDENTITY_MISSING");
                plan.TryReadPrescription(out var prescription);
                Require(prescription.HasSupportedIdentity(), "PRESCRIPTION_CONTRACT_UNSUPPORTED");
                Require(IsToken(plan.sessionId) && IsToken(plan.requestToken) &&
                    !string.IsNullOrWhiteSpace(plan.patientId) && !string.IsNullOrWhiteSpace(plan.preTrainingEventId),
                    "SESSION_IDENTITY_MISSING");
                Require(!string.IsNullOrEmpty(plan.startedAt) &&
                    Regex.IsMatch(plan.startedAt, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{3})?Z$") &&
                    DateTimeOffset.TryParse(plan.startedAt, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out _), "SESSION_TIME_MISSING");
                Require(facts != null && facts.Length == 2 && facts[0]?.mode == "coach" && facts[1]?.mode == "game",
                    "MEASURED_FACTS_MISSING");
                var modes = new[] { Mode(facts[0]), Mode(facts[1]) };
                Require(modes[0].status == "COMPLETED" || modes[1].status == "NOT_STARTED", "MODE_SEQUENCE_UNSUPPORTED");
                var item = prescription.items[0];
                bool completed = modes[0].status == "COMPLETED" && modes[1].status == "COMPLETED";
                bool notStarted = modes[0].status == "NOT_STARTED" && modes[1].status == "NOT_STARTED";
                int duration = checked(modes[0].durationSeconds + modes[1].durationSeconds);
                Require(duration <= 86400, "MEASURED_DURATION_OUT_OF_RANGE");
                var result = new TrainingNetworkResult
                {
                    sessionId = plan.sessionId, requestToken = plan.requestToken, userId = plan.patientId,
                    createdAt = plan.startedAt, completed = completed, status = completed ? "COMPLETED" : "INTERRUPTED",
                    durationSeconds = duration, prescriptionOrderId = prescription.orderId,
                    resourceManifestSha256 = prescription.resourceManifestSha256, resultRevision = 1,
                    source = notStarted ? "mobile_no_details" : "ar_measured", preTrainingEventId = plan.preTrainingEventId,
                    itemResults = new[] { new NetworkItemResult
                    {
                        itemId = item.itemId, position = item.position, actionId = item.actionId,
                        actionVersionId = item.actionVersionId, variantKey = item.variantKey, sha256 = item.sha256,
                        status = completed ? "COMPLETED" : notStarted ? "NOT_STARTED" : "INTERRUPTED", modeResults = modes,
                    } },
                };
                if (!string.IsNullOrWhiteSpace(previousJson))
                {
                    // Only our own fixed projection is restored here. The native parser
                    // independently validates the complete wire contract before enqueue.
                    var previous = JsonUtility.FromJson<TrainingNetworkResult>(
                        Regex.Replace(previousJson, "\"score\"\\s*:\\s*null", "\"score\":-1"));
                    Require(previous != null && previous.resultRevision > 0 && previous.resultRevision <= 9007199254740991L &&
                        previous.sessionId == result.sessionId && previous.requestToken == result.requestToken &&
                        previous.userId == result.userId && previous.createdAt == result.createdAt &&
                        previous.prescriptionOrderId == result.prescriptionOrderId &&
                        previous.resourceManifestSha256 == result.resourceManifestSha256 &&
                        previous.preTrainingEventId == result.preTrainingEventId &&
                        previous.itemResults?.Length == 1 && previous.itemResults[0]?.modeResults?.Length == 2,
                        "FROZEN_RESULT_IDENTITY_MISMATCH");
                    var oldItem = previous.itemResults[0];
                    Require(oldItem.itemId == item.itemId && oldItem.position == item.position &&
                        oldItem.actionId == item.actionId && oldItem.actionVersionId == item.actionVersionId &&
                        oldItem.variantKey == item.variantKey && oldItem.sha256 == item.sha256,
                        "FROZEN_ITEM_IDENTITY_MISMATCH");
                    long revision = previous.resultRevision;
                    previous.resultRevision = 1;
                    if (previous.ToWireJson() == result.ToWireJson())
                    {
                        json = previousJson;
                        return true;
                    }
                    Require(!previous.completed, "COMPLETED_RESULT_IMMUTABLE");
                    for (int i = 0; i < modes.Length; i++)
                    {
                        var oldMode = previous.itemResults[0].modeResults[i];
                        if (oldMode.status == "COMPLETED")
                            Require(JsonUtility.ToJson(oldMode) == JsonUtility.ToJson(modes[i]), "COMPLETED_MODE_IMMUTABLE");
                        Require(modes[i].completedReps >= oldMode.completedReps &&
                            modes[i].durationSeconds >= oldMode.durationSeconds, "RESULT_PROGRESS_REGRESSION");
                    }
                    Require(revision < 9007199254740991L, "RESULT_REVISION_EXHAUSTED");
                    result.resultRevision = revision + 1;
                }
                json = result.ToWireJson();
                return true;
            }
            catch (Exception failure)
            {
                error = failure is InvalidOperationException ? failure.Message : "RESULT_PROJECTION_INVALID";
                return false;
            }
        }

        private static NetworkModeResult Mode(TrainingModeFacts facts)
        {
            if (!facts.started)
            {
                Require(!facts.measurementAvailable && !facts.completionObserved, "MODE_FACTS_INCONSISTENT");
                return new NetworkModeResult { mode = facts.mode, status = "NOT_STARTED", source = "not_started" };
            }
            Require(facts.measurementAvailable, "IN_PROGRESS_MEASUREMENT_NOT_AVAILABLE");
            Require(facts.completedReps >= 0 && facts.completedReps <= 2000 &&
                facts.durationSeconds >= 0 && facts.durationSeconds <= 86400 &&
                (!facts.scoreAvailable || (facts.score >= 0 && facts.score <= 100)), "MEASURED_VALUES_OUT_OF_RANGE");
            Require(!facts.completionObserved || facts.completedReps > 0, "COMPLETED_REPETITIONS_MISSING");
            return new NetworkModeResult
            {
                mode = facts.mode, status = facts.completionObserved ? "COMPLETED" : "INTERRUPTED",
                completedReps = facts.completedReps, durationSeconds = facts.durationSeconds,
                score = facts.scoreAvailable ? facts.score : -1, source = "ar_measured",
            };
        }

        private static bool IsToken(string value)
        {
            return value != null && Regex.IsMatch(value, "^[A-Za-z0-9._:-]{1,128}$");
        }

        private static void Require(bool condition, string code)
        {
            if (!condition) throw new InvalidOperationException(code);
        }
    }
}
