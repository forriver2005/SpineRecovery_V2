package com.metaspine.mobile.data

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.io.File

class LocalPrescriptionHandoffTest {
    private val binding = BindingScope("http://192.168.3.16:33101", "hospital-a", "hospital-a", "A", "patient-test", "local-test", 2)
    private val asset = AdmissionPrescriptionAsset(4, "single", 1, 1, "sequence-coach-game-v1",
        listOf("coach", "game"), 64, "a".repeat(64), "/api/patient/assets/4")
    private val item = AdmissionPrescriptionItemV2(7, 0, "dead_bug", "dead bug", 3, 8, 2, asset)
    private val prescription = AdmissionPrescriptionV2(15, 2, "b".repeat(64), listOf(item))
    private val order = DownloadedOrder(prescription, "", "test", "2026-10-28T00:00:00Z", null, null)
    private val user = DemoUserData("local-test", UserLifecycle.ReturningUser,
        UserProfile(170, 65, UserGender.Female, 30), null,
        latestPreTrainingCheck = DailyCheck(1, "stable", false, true, "2026-10-28T00:00:00Z"),
        latestPainPoints = emptyList(), imuCalibrationState = ImuCalibrationState.Calibrated, history = emptyList())

    private fun local() = TrainingStartPayload.forLocalPrescription(user, DemoFlowType.ReturningUser,
        binding.patientId, order, binding)

    private fun assertUnityIdentity(snapshot: JSONObject) {
        assertEquals(15L, snapshot.getLong("orderId"))
        assertEquals(2, snapshot.getInt("prescriptionContractVersion"))
        assertEquals(prescription.resourceManifestSha256, snapshot.getString("resourceManifestSha256"))
        val scope = snapshot.getJSONObject("localScope")
        assertEquals(binding.origin, scope.getString("origin"))
        assertEquals(binding.serviceId, scope.getString("serviceId"))
        assertEquals(binding.hospitalKey, scope.getString("hospitalKey"))
        val frozen = snapshot.getJSONArray("items").getJSONObject(0)
        assertFalse(frozen.has("asset")) // Unity consumes flat PrescriptionItemSnapshot fields.
        assertEquals(asset.actionVersionId, frozen.getLong("actionVersionId"))
        assertEquals(asset.sha256, frozen.getString("sha256"))
        assertEquals(asset.fileSize, frozen.getLong("fileSize"))
        assertEquals(3, frozen.getInt("sets"))
        assertEquals(8, frozen.getInt("reps"))
        assertEquals(2, frozen.getInt("holdSeconds"))
    }

    @Test fun builtInTrainingKeepsUnityIdentityAndDoctorDoseWithoutAFile() {
        val plan = JSONObject(local().toJson()).getJSONObject("trainingPlan")
        val snapshot = plan.getJSONObject("prescription")
        assertUnityIdentity(snapshot)
        assertEquals("", snapshot.getJSONArray("items").getJSONObject(0).getString("localFileName"))
        val actions = plan.getJSONArray("recommendedActions")
        assertEquals(2, actions.length())
        val coach = actions.getJSONObject(0)
        assertEquals(3, coach.getInt("sets"))
        assertEquals(8, coach.getInt("reps"))
        assertEquals(2, coach.getInt("holdSeconds"))
        for (index in 0 until actions.length()) assertFalse(actions.getJSONObject(index).has("motionPackageUri"))
    }

    @Test fun remoteTrainingStillSerializesTheVerifiedFileHandoff() {
        val handoff = TrainingPrescriptionHandoff(binding, order, prescription.resourceManifestSha256,
            listOf(HandedOffAsset(item, File("verified-motion.json"), asset.fileSize)), emptyList())
        val plan = JSONObject(local().copy(prescription = handoff, prescriptionSnapshot = null).toJson())
            .getJSONObject("trainingPlan").getJSONObject("prescription")
        assertUnityIdentity(plan)
        assertEquals("verified-motion.json", plan.getJSONArray("items").getJSONObject(0).getString("localFileName"))
    }

    @Test fun localExecutionCannotAttachAnotherPatientsBinding() {
        assertThrows(IllegalArgumentException::class.java) {
            TrainingStartPayload.forLocalPrescription(user, DemoFlowType.ReturningUser,
                binding.patientId, order, binding.copy(patientId = "other-patient"))
        }
    }

    @Test fun localConfirmationRejectsStopWithdrawalAndChangedPrescription() {
        val snapshot = TrainingDirectiveSnapshot(binding.patientId, 2, TrainingDirectiveState.Ready,
            "2026-10-28T00:00:00Z", 0, "", order, null, null, null)
        assertTrue(isSameLocalTrainingOrder(order, snapshot))
        assertFalse(isSameLocalTrainingOrder(order, snapshot.copy(state = TrainingDirectiveState.Stopped)))
        assertFalse(isSameLocalTrainingOrder(order, snapshot.copy(state = TrainingDirectiveState.UnsupportedPrescription)))
        assertFalse(isSameLocalTrainingOrder(order, snapshot.copy(prescription = null)))
        assertFalse(isSameLocalTrainingOrder(order, snapshot.copy(prescription = order.copy(
            prescription = prescription.copy(orderId = 16)))))
        assertFalse(isSameLocalTrainingOrder(order, snapshot.copy(prescription = order.copy(
            prescription = prescription.copy(resourceManifestSha256 = "c".repeat(64))))))
    }
}
