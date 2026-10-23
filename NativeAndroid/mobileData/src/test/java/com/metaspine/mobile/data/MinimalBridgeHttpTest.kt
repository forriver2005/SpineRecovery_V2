package com.metaspine.mobile.data

import kotlinx.coroutines.runBlocking
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import java.io.File

/** Opt-in HTTP integration against disposable hospitals, never a configured production service. */
class MinimalBridgeHttpTest {
    @Test fun doctorPrescriptionOneItemReachesMobile() = roundTrip(1)
    @Test fun doctorPrescriptionTwoItemsReachMobile() = roundTrip(2)
    @Test fun doctorPrescriptionTenItemsReachMobile() = roundTrip(10)

    private fun roundTrip(count: Int) = runBlocking {
        val fixturePath = System.getenv("SPINEFLOW_BRIDGE_FIXTURE")
        assumeTrue("Requires disposable server fixture", !fixturePath.isNullOrBlank())
        val fixture = JSONObject(File(fixturePath!!).readText())
        val cases = fixture.getJSONArray("cases")
        val case = (0 until cases.length()).map { cases.getJSONObject(it) }
            .single { it.getInt("itemCount") == count }
        val origin = fixture.getString("baseUrl")
        require(origin.startsWith("http://127.0.0.1:"))
        val previousOrigin = SpineFlowServerConfig.baseUrlOverride
        SpineFlowServerConfig.baseUrlOverride = origin
        try {
            val identity = ServiceIdentityClient.requireHospital(ServiceIdentityClient.fetch().getOrThrow())
            val request = case.getJSONObject("activationBody")
            val first = PatientActivationClient.redeem(request).getOrThrow()
            val replay = PatientActivationClient.redeem(request).getOrThrow()
            assertEquals(first, replay)
            assertEquals(count, first.prescriptionV2.items.size)
            assertTrue(first.prescriptionV2.items.all { it.asset.downloadPath.isEmpty() })
            val credential = PatientServerCredential(first.patientId, request.getString("proposedToken"))
            val binding = BindingScope(origin, identity.serviceId, identity.hospitalKey, identity.hospitalName,
                first.patientId, request.getString("accountId"), 1)
            val doctorToken = fixture.getJSONObject("doctor").getString("token")
            suspend fun doctorPost(path: String, body: JSONObject): JSONObject {
                val response = PatientHttpTransport.shared.request(path, "POST", doctorToken, body.toString(),
                    readErrorStatuses = setOf(400, 409))
                response.requireSuccess("Disposable doctor request failed")
                return JSONObject(response.text).getJSONObject("data")
            }
            // Re-running the test replaces only its own disposable patient's orders.
            val before = PatientDirectiveClient.fetch(credential.patientToken).getOrThrow()
            for (field in listOf("activeStopOrder", "activePrescription")) {
                before.optJSONObject(field)?.let { order ->
                    doctorPost("/api/doctor/orders/${order.getLong("orderId")}/revoke",
                        JSONObject().put("reason", "Isolated mobile integration refresh"))
                }
            }
            // A stopped directive hides the old prescription; read again after revoking stop.
            PatientDirectiveClient.fetch(credential.patientToken).getOrThrow()
                .optJSONObject("activePrescription")?.let { order ->
                    doctorPost("/api/doctor/orders/${order.getLong("orderId")}/revoke",
                        JSONObject().put("reason", "Isolated mobile integration replacement"))
                }
            val draft = JSONObject(case.getJSONObject("prescriptionDraft").toString()).put("orderType", "prescription")
            doctorPost("/api/doctor/patients/${first.patientId}/orders", draft)

            val storedFile = File.createTempFile("directive-http-", ".json").apply { deleteOnExit() }
            val storage = object : DirectiveSnapshotStorage {
                override fun read(accountId: String, patientId: String): TrainingDirectiveSnapshot? =
                    storedFile.takeIf { it.length() > 0 }?.readText()?.let(::directiveSnapshotFromStoreJson)
                override fun write(accountId: String, snapshot: TrainingDirectiveSnapshot) {
                    storedFile.writeText(snapshot.toStoreJson().toString())
                }
                override fun clear(accountId: String) { storedFile.writeText("") }
                override fun clearIfPatientChanged(accountId: String, patientId: String) {}
            }
            val pending = mutableMapOf<Long, PrescriptionReceiptTask>()
            val receipts = object : PrescriptionReceiptStorage {
                override fun read(scope: BindingScope, orderId: Long) = pending[orderId]
                override fun write(scope: BindingScope, task: PrescriptionReceiptTask) { pending[task.orderId] = task }
            }
            val coordinator = TrainingDirectiveCoordinator(storage = storage, receipts = receipts)
            val received = coordinator.receiveOnly(binding.accountId, credential, binding) as DirectiveRefreshResult.Received
            assertFalse(received.receiptPending)
            assertFalse(received.canStartTraining)
            val order = received.snapshot.prescription!!
            assertEquals(count, order.items.size)
            assertTrue(order.items.all { it.asset.downloadPath.startsWith("/api/patient/training-directive/") })
            assertEquals(order.resourceManifestSha256, order.prescription.computeManifestSha256())
            assertEquals(count, storage.read(binding.accountId, first.patientId)!!.prescription!!.items.size)
            val saved = PatientDirectiveClient.fetch(credential.patientToken).getOrThrow().getJSONObject("activePrescription")
            assertFalse(saved.getString("receivedAt").isBlank())
            assertTrue(saved.isNull("assetsReadyAt"))
            val receiptTime = saved.getString("receivedAt")
            coordinator.receiveOnly(binding.accountId, credential, binding)
            assertEquals(receiptTime, PatientDirectiveClient.fetch(credential.patientToken).getOrThrow()
                .getJSONObject("activePrescription").getString("receivedAt"))

            doctorPost("/api/doctor/patients/${first.patientId}/orders",
                JSONObject().put("orderType", "stop_training").put("reason", "Isolated stop delivery"))
            val stopped = coordinator.receiveOnly(binding.accountId, credential, binding) as DirectiveRefreshResult.Received
            assertEquals(TrainingDirectiveState.Stopped, stopped.snapshot.state)
            assertFalse(stopped.receiptPending)
            assertNull(stopped.snapshot.prescription)
            assertFalse(stopped.snapshot.stopOrder!!.receivedAt.isNullOrBlank())
        } finally {
            SpineFlowServerConfig.baseUrlOverride = previousOrigin
        }
    }
}
