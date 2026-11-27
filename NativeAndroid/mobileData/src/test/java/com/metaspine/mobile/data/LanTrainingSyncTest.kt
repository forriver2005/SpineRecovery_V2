package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class LanTrainingSyncTest {
    private val patient = "patient1"
    private val saved = TrainingHistoryRecord("session1", "2026-11-18T12:00:00.000Z",
        """{"userId":"patient1","sessionId":"session1"}""", null,
        planJson = "{}", feedbackPending = true, endReason = "COMPLETED")
    private fun user(record: TrainingHistoryRecord) = DemoUserData("account", UserLifecycle.ReturningUser,
        null, null, latestPreTrainingCheck = null, latestPainPoints = emptyList(),
        imuCalibrationState = ImuCalibrationState.Calibrated, history = listOf(record))

    @Test fun savedResultsAreReadyBeforeFeedbackForTheirBoundPatient() {
        for (reason in listOf("COMPLETED", "INTERRUPTED")) {
            val record = saved.copy(endReason = reason)
            assertTrue(record.isReadyForUpload(patient))
            assertFalse(record.isReadyForUpload("different"))
            assertFalse(record.isReadyForUpload(null))
            val diff = buildPendingDiff(user(record), PatientSyncClient.SyncSnapshot(), patient)
            assertEquals(listOf(record), diff.uploadRecords)
            assertNull(diff.syncBody?.optJSONArray("postTrainingFeedbacks"))
        }
        assertFalse(saved.copy(endReason = "").isReadyForUpload(patient))
        assertFalse(saved.copy(endReason = "LEGACY_IMPORT").isReadyForUpload(patient))
    }

    @Test fun feedbackAfterResultReceiptIsSentOnceWithoutUploadingTheSessionAgain() {
        val record = saved.copy(feedbackPending = false, feedback = PostTrainingFeedback("舒适", "适中"))
        val key = "post:${record.sessionId}"
        val accepted = PatientSyncClient.SyncSnapshot(syncedSessionIds = setOf(record.sessionId),
            questionnaireHashes = mapOf(key to "pending"))
        val diff = buildPendingDiff(user(record), accepted, patient)
        assertTrue(diff.uploadRecords.isEmpty())
        assertEquals(key, diff.syncBody!!.getJSONArray("postTrainingFeedbacks").getJSONObject(0).getString("eventId"))
        assertEquals("sent", diff.newQuestionnaireHashes[key])
        val acknowledged = accepted.copy(questionnaireHashes = diff.newQuestionnaireHashes)
        assertNull(buildPendingDiff(user(record), acknowledged, patient).syncBody?.optJSONArray("postTrainingFeedbacks"))
    }

    @Test fun reconnectSendsTheReferencedQuestionnaireBeforeQueuedTrainingBatches() {
        val first = JSONObject().put("kind", "upload:1")
        val second = JSONObject().put("kind", "upload:2")
        val latestPreCheck = JSONObject().put("kind", "sync")
        val queued = JSONArray().put(first).put(second).put(latestPreCheck)
        assertSame(latestPreCheck, nextPatientRequest(queued))
        queued.remove(2)
        assertSame(first, nextPatientRequest(queued))
    }

    @Test fun eachActionHasAnIndependentReceiptEvenForTheSameMovement() {
        val first = ActionTrainingFeedback(0, "deadbug", "死虫", 11,
            "2026-11-27T10:00:00Z", "疼痛 1", "强度 3")
        val second = first.copy(actionIndex = 1, prescriptionItemId = 12, feeling = "疼痛 2")
        val record = saved.copy(actionFeedbacks = listOf(first, second),
            feedback = PostTrainingFeedback("legacy", "legacy"))
        val diff = buildPendingDiff(user(record), PatientSyncClient.SyncSnapshot(), patient)
        val answers = diff.syncBody!!.getJSONArray("postTrainingFeedbacks")
        assertEquals(2, answers.length())
        assertNotEquals(answers.getJSONObject(0).getString("eventId"), answers.getJSONObject(1).getString("eventId"))
        assertEquals(12L, answers.getJSONObject(1).getLong("prescriptionItemId"))
        assertEquals("疼痛 2", answers.getJSONObject(1).getString("feeling"))
        assertEquals(listOf(first, second), ActionTrainingFeedback.fromJson(record.actionFeedbacks.toFeedbackJson()))
        val accepted = PatientSyncClient.SyncSnapshot(syncedSessionIds = setOf(record.sessionId),
            questionnaireHashes = diff.newQuestionnaireHashes)
        assertNull(buildPendingDiff(user(record), accepted, patient).syncBody?.optJSONArray("postTrainingFeedbacks"))
    }

    @Test fun laterActionAnswerIsSyncedAfterSessionReceiptWithoutResendingEarlierAnswer() {
        val first = ActionTrainingFeedback(0, "deadbug", "死虫", 11,
            "2026-11-27T10:00:00Z", "疼痛 1", "强度 3")
        val second = first.copy(actionIndex = 1, actionId = "birddog", actionName = "鸟狗", prescriptionItemId = 12)
        val accepted = PatientSyncClient.SyncSnapshot(syncedSessionIds = setOf(saved.sessionId),
            questionnaireHashes = mapOf(first.eventId(saved.sessionId) to "sent"))
        val diff = buildPendingDiff(user(saved.copy(actionFeedbacks = listOf(first, second))), accepted, patient)
        assertTrue(diff.uploadRecords.isEmpty())
        val answers = diff.syncBody!!.getJSONArray("postTrainingFeedbacks")
        assertEquals(1, answers.length())
        assertEquals(second.eventId(saved.sessionId), answers.getJSONObject(0).getString("eventId"))
        assertEquals("鸟狗", answers.getJSONObject(0).getString("actionName"))
    }

    @Test fun discoveryRequiresAMatchingReplyAndVerifiedHospitalIdentity() {
        val cert = "public certificate".toByteArray()
        val trust = LocalServerTrust(LocalServerProtocol.sha256(cert), "hospital-a", "hospital-a")
        val nonce = "a".repeat(32)
        val host = "192.168.3.16"
        val reply = JSONObject(String(LocalServerProtocol.request(nonce))).put("host", host)
            .put("port", 3001).put("certificateSha256", trust.certificateSha256)
            .put("serviceId", trust.serviceId).put("hospitalKey", trust.hospitalKey).toString().toByteArray()
        fun candidate(n: String, local: String) = LocalServerProtocol.parseCandidate(reply, n, host, local, 24, trust)
        assertEquals(LocalServerCandidate(host), candidate(nonce, "192.168.3.80"))
        assertNull(candidate(nonce, "192.168.31.89"))
        assertNull(candidate("b".repeat(32), "192.168.3.80"))
        assertTrue(trust.acceptsCertificate(cert))
        assertFalse(trust.acceptsCertificate("another certificate".toByteArray()))
        val data = JSONObject().put("role", "hospital").put("serviceId", "hospital-a").put("hospitalKey", "hospital-a")
            .put("audience", "spineflow:medical:hospital-a").put("contractVersion", 1)
        val identity = JSONObject().put("code", 0).put("data", data)
        assertTrue(trust.matchesIdentity(identity.toString().toByteArray()))
        data.put("hospitalKey", "hospital-b")
        assertFalse(trust.matchesIdentity(identity.toString().toByteArray()))
    }
}
