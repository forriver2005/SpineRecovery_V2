package com.metaspine.mobile.data

import kotlinx.coroutines.runBlocking
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/**
 * P4.2 / P4.5 / P4.7：两阶段回执顺序、失联重放、能力缺口与停训回执。
 *
 * 所有网络出口都用假实现替换：这里验证的是“何时允许 ACK”，不是 HTTP 细节
 * （HTTP 细节由 [PrescriptionAssetClientTest] 覆盖）。
 */
class TrainingDirectiveCoordinatorTest {

    private val scope = BindingScope(
        origin = "https://hospital-a.example.com",
        serviceId = "svc_a",
        hospitalKey = "hospital_a",
        hospitalName = "医院A",
        patientId = "p_1001",
        accountId = "acc",
        credentialGeneration = 1
    )
    private val credential = PatientServerCredential("p_1001", "pat_test")

    private val sent = mutableListOf<Triple<String, String, Long>>()
    private val receiptFailures = mutableListOf<String>()
    private var directiveJson: JSONObject = readyDirective()
    private var gate: (TrainingPrescriptionHandoff) -> List<String> = { emptyList() }
    private var failDownload = false

    private val snapshots = object : DirectiveSnapshotStorage {
        var stored: TrainingDirectiveSnapshot? = null
        override fun read(accountId: String, patientId: String) = stored
        override fun write(accountId: String, snapshot: TrainingDirectiveSnapshot) {
            stored = snapshot
        }
        override fun clear(accountId: String) { stored = null }
        override fun clearIfPatientChanged(accountId: String, patientId: String) {
            if (stored?.patientId != patientId) stored = null
        }
    }

    private val tasks = mutableMapOf<Pair<String, Long>, PrescriptionReceiptTask>()
    private val receipts = object : PrescriptionReceiptStorage {
        override fun read(scope: BindingScope, orderId: Long) =
            tasks[scope.hospitalKey to orderId]
        override fun write(scope: BindingScope, task: PrescriptionReceiptTask) {
            tasks[scope.hospitalKey to task.orderId] = task
        }
    }

    private val assets = object : PrescriptionAssetStorage {
        override fun existing(orderId: Long, itemId: Long, sha256: String): File? = null
        override suspend fun download(
            patientToken: String,
            order: DownloadedOrder,
            item: AdmissionPrescriptionItemV2
        ): File? {
            if (failDownload) return null
            val file = File.createTempFile("asset-", ".bin")
            file.deleteOnExit()
            file.writeBytes(ByteArray(item.asset.fileSize.toInt()) { 7 })
            return file
        }
    }

    private fun coordinator() = TrainingDirectiveCoordinator(
        storage = snapshots,
        receipts = receipts,
        assets = assets,
        handoffGate = { handoff -> gate(handoff) },
        fetchDirective = { Result.success(JSONObject(directiveJson.toString())) },
        sendStopReceipt = { _, orderId ->
            sent.add(Triple("stop", "", orderId))
            Result.success(PatientDirectiveClient.ReceiptInfo(
                orderId, "", "", "2026-10-21 10:00:00", null, false))
        },
        sendPrescriptionReceipt = { _, orderId, phase, sha ->
            sent.add(Triple(phase, sha, orderId))
            if (receiptFailures.remove(phase)) {
                Result.failure(ApiHttpException(503, "服务器繁忙"))
            } else {
                Result.success(PatientDirectiveClient.ReceiptInfo(
                    orderId = orderId,
                    phase = phase,
                    resourceManifestSha256 = sha,
                    receivedAt = "2026-10-21 10:00:00",
                    assetsReadyAt = if (phase == PatientDirectiveClient.PHASE_ASSETS_READY)
                        "2026-10-21 10:00:00" else null,
                    alreadyRecorded = false
                ))
            }
        },
        nowMillis = { 1_700_000_000_000L }
    )

    private fun refresh() = runBlocking { coordinator().refresh("acc", credential, scope) }

    private fun readyDirective(itemCount: Int = 1): JSONObject {
        val text = javaClass.getResourceAsStream("/fixtures/contract.json")!!
            .bufferedReader(Charsets.UTF_8).use { it.readText() }
        val cases = JSONObject(text).getJSONArray("cases")
        val index = when (itemCount) {
            1 -> 0; 2 -> 1; 10 -> 2
            else -> error("样板只有 1/2/10 项")
        }
        return JSONObject(cases.getJSONObject(index).getJSONObject("directive").toString())
    }

    private fun manifestOf(json: JSONObject): String =
        json.getJSONObject("activePrescription").getString("resourceManifestSha256")

    @Test
    fun `先保存再提交saved全部文件核对后才提交ready`() {
        directiveJson = readyDirective(1)
        val result = refresh()
        val ready = result as DirectiveRefreshResult.Ready
        assertEquals(
            listOf(PatientDirectiveClient.PHASE_SAVED, PatientDirectiveClient.PHASE_ASSETS_READY),
            sent.map { it.first })
        assertEquals(manifestOf(directiveJson), sent[0].second)
        assertEquals(manifestOf(directiveJson), ready.handoff!!.manifestSha256)
        assertEquals(1, ready.handoff!!.assets.size)
        assertTrue(ready.handoff!!.complete)
        assertTrue(ready.canStartTraining)
        assertEquals(ReceiptPhase.AssetsReady, tasks["hospital_a" to ready.handoff!!.order.orderId]!!.phase)
    }

    @Test
    fun `多动作处方完整交接但能力缺口保持saved`() {
        directiveJson = readyDirective(10)
        gate = { TrainingStartPayload.capabilityGaps(it.order) }
        val result = refresh()
        assertTrue(result is DirectiveRefreshResult.NotTrainable)
        // 只提交了保存阶段，绝不能在能力缺口下达 assets_ready。
        assertEquals(listOf(PatientDirectiveClient.PHASE_SAVED), sent.map { it.first })
        val stored = tasks.values.single()
        assertEquals(5010L, stored.orderId)
        assertEquals(ReceiptPhase.Saved, stored.phase)
        assertTrue(stored.blockingReasons.isNotEmpty())
    }

    @Test
    fun `资源下载失败保持saved并记录失败条目`() {
        directiveJson = readyDirective(2)
        failDownload = true
        val result = refresh()
        assertEquals(listOf(PatientDirectiveClient.PHASE_SAVED), sent.map { it.first })
        assertTrue(result is DirectiveRefreshResult.NotTrainable)
        val stored = tasks.values.single()
        assertEquals(ReceiptPhase.Saved, stored.phase)
        assertEquals(2, stored.failedAssetItemIds.size)
    }

    @Test
    fun `assets_ready丢响应后保持saved且重放同一清单`() {
        directiveJson = readyDirective(1)
        receiptFailures.add(PatientDirectiveClient.PHASE_ASSETS_READY)
        assertTrue(refresh() is DirectiveRefreshResult.NotTrainable)
        assertEquals(ReceiptPhase.Saved, tasks.values.single().phase)

        val second = refresh()
        assertTrue(second is DirectiveRefreshResult.Ready)
        assertEquals(
            listOf(PatientDirectiveClient.PHASE_SAVED,
                PatientDirectiveClient.PHASE_ASSETS_READY,
                PatientDirectiveClient.PHASE_ASSETS_READY),
            sent.map { it.first })
        // saved 不重复提交，只重放 ready；订单与清单保持一致。
        assertEquals(1, sent.count { it.first == PatientDirectiveClient.PHASE_SAVED })
        assertEquals(1, sent.map { it.second }.distinct().size)
    }

    @Test
    fun `清单摘要不一致时不提交任何回执`() {
        directiveJson = readyDirective(1)
        directiveJson.getJSONObject("activePrescription")
            .put("resourceManifestSha256", "b".repeat(64))
        val result = refresh()
        assertTrue(result is DirectiveRefreshResult.NotTrainable)
        assertTrue(sent.isEmpty())
    }

    @Test
    fun `停训只提交简式回执`() {
        directiveJson = JSONObject()
            .put("directiveVersion", 2)
            .put("serverTime", "2026-10-21T00:00:00.000Z")
            .put("trainingState", "stopped")
            .put("message", "医生已暂停训练")
            .put("activePrescription", JSONObject.NULL)
            .put("activeStopOrder", JSONObject()
                .put("orderId", 9001).put("reason", "复查")
                .put("createdBy", "doctor_a").put("createdAt", "2026-10-20T08:00:00.000Z"))
        val result = refresh()
        assertEquals(listOf(Triple("stop", "", 9001L)), sent)
        assertTrue(result is DirectiveRefreshResult.NotTrainable)
    }

    @Test
    fun `回执任务按完整scope隔离`() {
        directiveJson = readyDirective(1)
        refresh()
        val key = tasks.keys.single()
        assertTrue(tasks[key]!!.phase == ReceiptPhase.AssetsReady)
        // 换医院缓存键不同：旧任务不会被新绑定看到。
        assertFalse(tasks.containsKey("hospital_b" to key.second))
        assertEquals(ReceiptPhase.AssetsReady, receipts.read(scope, key.second)!!.phase)
        assertNull(receipts.read(scope.copy(hospitalKey = "hospital_b"), key.second))
    }
}
