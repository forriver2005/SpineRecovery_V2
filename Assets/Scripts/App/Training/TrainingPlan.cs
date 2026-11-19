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
        public string motionPackageUri;
        public int holdSeconds = -1;
        public string prescriptionAssetPath;
        public long prescriptionItemId;
        public int prescriptionPosition;
    }

    [Serializable]
    public sealed class TrainingPlan
    {
        public string sessionId;
        public string requestToken;
        public string planToken;
        public string accountId;
        public string patientId;
        public string gender;
        public string startedAt;
        public string preTrainingEventId;
        public long prescriptionOrderId;
        public string prescriptionJson;
        public int prescriptionContractVersion;
        public string actionId;
        public string targetScene;
        public TrainingAction[] actions = Array.Empty<TrainingAction>();

        public bool TryReadPrescription(out PrescriptionSnapshot prescription)
        {
            prescription = null;
            if (string.IsNullOrWhiteSpace(prescriptionJson)) return false;
            try { prescription = UnityEngine.JsonUtility.FromJson<PrescriptionEnvelope>(prescriptionJson)?.prescription; }
            catch (ArgumentException) { return false; }
            return prescription != null && prescription.orderId == prescriptionOrderId;
        }

        public bool CanConsumePrescription()
        {
            if (string.IsNullOrWhiteSpace(planToken) || string.IsNullOrWhiteSpace(sessionId) ||
                string.IsNullOrWhiteSpace(requestToken) || string.IsNullOrWhiteSpace(startedAt) ||
                string.IsNullOrWhiteSpace(preTrainingEventId) || string.IsNullOrWhiteSpace(patientId) ||
                !TryReadPrescription(out var prescription) || !prescription.HasSupportedIdentity() ||
                actions == null || actions.Length != prescription.items.Length * 2) return false;
            for (int index = 0; index < prescription.items.Length; index++)
            {
                var item = prescription.items[index];
                var coach = actions[index * 2];
                var game = actions[index * 2 + 1];
                if (item == null || coach == null || game == null ||
                    coach.actionId != item.actionId + "_coach" || game.actionId != item.actionId + "_game" ||
                    coach.arTaskType != "coach_training" || game.arTaskType != "game_training" ||
                    coach.sets != item.sets || coach.reps != item.reps || coach.holdSeconds != item.holdSeconds ||
                    game.sets != item.sets || game.reps != item.reps || game.holdSeconds != item.holdSeconds ||
                    coach.prescriptionItemId != item.itemId || game.prescriptionItemId != item.itemId ||
                    coach.prescriptionPosition != index || game.prescriptionPosition != index ||
                    !UnityEngine.Application.CanStreamedLevelBeLoaded(coach.sceneName) ||
                    !UnityEngine.Application.CanStreamedLevelBeLoaded(game.sceneName) ||
                    coach.motionPackageUri != game.motionPackageUri ||
                    (string.IsNullOrWhiteSpace(coach.motionPackageUri) && !LocalActionCatalog.IsSupported(item.actionId))) return false;
                // Built-in doctor actions execute from the APK scenes and only receive
                // order/dose metadata. Remote motion packages remain supported for future actions.
                if (string.IsNullOrWhiteSpace(coach.motionPackageUri)) continue;
                try
                {
                    string json = PracticeSceneInitializer.ReadJsonFromUri(coach.motionPackageUri);
                    if (string.IsNullOrWhiteSpace(json)) return false;
                    var package = UnityEngine.JsonUtility.FromJson<CoachMotionPackage>(json);
                    if (package == null || package.actionId != item.actionId || !package.IsValid(out _)) return false;
                }
                catch (Exception) { return false; }
            }
            return actionId == prescription.items[0].actionId && targetScene == actions[0].sceneName;
        }
    }

    [Serializable]
    public sealed class PrescriptionEnvelope
    {
        public PrescriptionSnapshot prescription;
    }

    [Serializable]
    public sealed class PrescriptionSnapshot
    {
        public long orderId;
        public int prescriptionContractVersion;
        public string resourceManifestSha256;
        public PrescriptionScope localScope;
        public PrescriptionItemSnapshot[] items;

        public bool HasSupportedIdentity()
        {
            if (orderId <= 0 || orderId > 9007199254740991L || prescriptionContractVersion != 2 ||
                !IsSha256(resourceManifestSha256) || localScope == null ||
                string.IsNullOrWhiteSpace(localScope.origin) || string.IsNullOrWhiteSpace(localScope.serviceId) ||
                string.IsNullOrWhiteSpace(localScope.hospitalKey) || items == null || items.Length < 1 || items.Length > 10) return false;
            for (int index = 0; index < items.Length; index++)
            {
                var item = items[index];
                if (item == null || item.itemId <= 0 || item.itemId > 9007199254740991L || item.position != index ||
                    item.actionVersionId <= 0 || item.actionVersionId > 9007199254740991L ||
                    (item.actionId != "dead_bug" && item.actionId != "bird_dog" &&
                     item.actionId != "hip_thrust" && item.actionId != "side_bridge") || item.variantKey != "single" ||
                    !IsSha256(item.sha256) || item.fileSize <= 0 || item.formatVersion != 1 ||
                    item.runtimeContractVersion != 1 || item.doseProfile != "sequence-coach-game-v1" ||
                    item.supportedModes == null || item.supportedModes.Length != 2 ||
                    item.supportedModes[0] != "coach" || item.supportedModes[1] != "game" ||
                    item.sets < 1 || item.sets > 20 || item.reps < 1 || item.reps > 100 ||
                    item.holdSeconds < 0 || item.holdSeconds > 600) return false;
            }
            return true;
        }

        private static bool IsSha256(string text)
        {
            return text != null && System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9a-f]{64}$");
        }
    }

    [Serializable]
    public sealed class PrescriptionScope
    {
        public string origin;
        public string serviceId;
        public string hospitalKey;
    }

    [Serializable]
    public sealed class PrescriptionItemSnapshot
    {
        public long itemId;
        public int position;
        public string actionId;
        public string actionName;
        public int sets;
        public int reps;
        public int holdSeconds;
        public long actionVersionId;
        public string variantKey;
        public int formatVersion;
        public int runtimeContractVersion;
        public string doseProfile;
        public string[] supportedModes;
        public string sha256;
        public long fileSize;
        public string localFileName;
    }
}
