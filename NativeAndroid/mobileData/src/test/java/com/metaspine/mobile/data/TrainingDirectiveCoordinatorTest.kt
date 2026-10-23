package com.metaspine.mobile.data

import kotlinx.coroutines.runBlocking
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
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
    private var assetCalls = 0
    private var gateCalls = 0
    private var failSnapshotWrite = false
    private var scopeCurrent = true
    private var afterFetch: () -> Unit = {}
    private var afterReceipt: () -> Unit = {}
    private var elapsed = 1_000L
    private var statusCalls = 0
    private var statusOverride: PrescriptionResourceStatus? = null

    private val snapshots = object : DirectiveSnapshotStorage {
        var stored: TrainingDirectiveSnapshot? = null
        override fun read(accountId: String, patientId: String) = stored
        override fun write(accountId: String, snapshot: TrainingDirectiveSnapshot) {
            check(!failSnapshotWrite) { "模拟保存失败" }
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
        override fun existing(orderId: Long, itemId: Long, sha256: String): File? {
            assetCalls++
            return null
        }
        override suspend fun download(
            patientToken: String,
            order: DownloadedOrder,
            item: AdmissionPrescriptionItemV2,
            expectedLease: TrainingResourceLease
        ): PrescriptionAssetClient.VerifiedAsset? {
            assetCalls++
            if (failDownload) return null
            val file = File.createTempFile("asset-", ".bin")
            file.deleteOnExit()
            file.writeBytes(ByteArray(item.asset.fileSize.toInt()) { 7 })
            return PrescriptionAssetClient.VerifiedAsset(file, file.length(), item.asset.sha256,
                ObservedResourceLease(expectedLease, elapsed))
        }
    }

    private fun coordinator() = TrainingDirectiveCoordinator(
        storage = snapshots,
        receipts = receipts,
        assets = assets,
        handoffGate = { handoff -> gateCalls++; gate(handoff) },
        isCurrent = { scopeCurrent },
        fetchDirective = {
            afterFetch()
            Result.success(JSONObject(directiveJson.toString()))
        },
        sendStopReceipt = { _, orderId ->
            assertEquals(TrainingDirectiveState.Stopped, snapshots.stored!!.state)
            sent.add(Triple("stop", "", orderId))
            afterReceipt()
            if (receiptFailures.remove("stop")) Result.failure(ApiHttpException(503, "服务器繁忙"))
            else Result.success(PatientDirectiveClient.ReceiptInfo(
                orderId, "", "", "2026-10-21 10:00:00", null, false))
        },
        sendPrescriptionReceipt = { _, orderId, phase, sha ->
            assertEquals(orderId, snapshots.stored!!.prescription!!.orderId)
            assertTrue("发送前回执任务必须持久保存", tasks.containsKey(scope.hospitalKey to orderId))
            sent.add(Triple(phase, sha, orderId))
            afterReceipt()
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
        fetchResourceStatus = {
            statusCalls++
            val order = directiveJson.getJSONObject("activePrescription")
            val items = order.getJSONArray("items")
            Result.success(statusOverride ?: PrescriptionResourceStatus(
                TrainingResourceLease.fromJson(directiveJson.getJSONObject("resourceLease")),
                (0 until items.length()).associate {
                    items.getJSONObject(it).getJSONObject("asset").getLong("actionVersionId") to "active"
                }))
        },
        elapsedMillis = { elapsed },
        nowMillis = { 1_700_000_000_000L }
    )

    private fun refresh() = runBlocking {
        val coordinator = coordinator()
        val prepared = coordinator.prepare("acc", credential, scope)
        if (prepared is DirectiveRefreshResult.Prepared) coordinator.confirmReady(credential, prepared, true)
        else prepared
    }
    private fun prepareOnly() = runBlocking { coordinator().prepare("acc", credential, scope) }
    private fun confirm(prepared: DirectiveRefreshResult.Prepared, accepted: Boolean = true) = runBlocking {
        coordinator().confirmReady(credential, prepared, accepted)
    }
    private fun receiveOnly() = runBlocking { coordinator().receiveOnly("acc", credential, scope) }

    private fun readyDirective(itemCount: Int = 1): JSONObject {
        val text = javaClass.getResourceAsStream("/fixtures/contract.json")!!
            .bufferedReader(Charsets.UTF_8).use { it.readText() }
        val cases = JSONObject(text).getJSONArray("cases")
        val index = when (itemCount) {
            1 -> 0; 2 -> 1; 10 -> 2
            else -> error("样板只有 1/2/10 项")
        }
        val result = JSONObject(cases.getJSONObject(index).getJSONObject("directive").toString())
        val order = result.getJSONObject("activePrescription")
        val items = order.getJSONArray("items")
        for (itemIndex in 0 until items.length()) {
            val asset = items.getJSONObject(itemIndex).getJSONObject("asset")
            val bytes = ByteArray(asset.getInt("fileSize")) { 7 }
            val sha = java.security.MessageDigest.getInstance("SHA-256").digest(bytes)
                .joinToString("") { "%02x".format(it) }
            asset.put("sha256", sha)
        }
        order.put("resourceManifestSha256", AdmissionPrescriptionV2.fromDirectiveJson(order).computeManifestSha256())
        return result.put("resourceLease", TrainingResourceLease(1, "generation-a", 7, 100_000, 160_000).toJson())
    }

    private fun manifestOf(json: JSONObject): String =
        json.getJSONObject("activePrescription").getString("resourceManifestSha256")

    private fun stoppedDirective(): JSONObject = JSONObject()
        .put("directiveVersion", 2)
        .put("serverTime", "2026-10-21T00:00:00.000Z")
        .put("trainingState", "stopped")
        .put("message", "医生已暂停训练")
        .put("activePrescription", JSONObject.NULL)
        .put("activeStopOrder", JSONObject()
            .put("orderId", 9001).put("reason", "复查")
            .put("createdBy", "doctor_a").put("createdAt", "2026-10-20T08:00:00.000Z")
            .put("receivedAt", JSONObject.NULL))

    @Test
    fun `仅接收完整保存1和2和10项只发saved不触发消费者或下载`() {
        gate = { error("仅接收不应查询执行能力") }
        failDownload = true
        for (count in listOf(1, 2, 10)) {
            sent.clear()
            directiveJson = readyDirective(count)
            val result = receiveOnly() as DirectiveRefreshResult.Received
            assertFalse(result.canStartTraining)
            assertFalse(result.receiptPending)
            assertEquals(count, result.snapshot.prescription!!.items.size)
            assertEquals(result.snapshot, directiveSnapshotFromStoreJson(result.snapshot.toStoreJson().toString()))
            assertEquals(result.snapshot, snapshots.stored)
            assertEquals(listOf(PatientDirectiveClient.PHASE_SAVED), sent.map { it.first })
            assertEquals(ReceiptPhase.Saved, tasks[scope.hospitalKey to result.snapshot.prescription!!.orderId]!!.phase)
        }
        assertEquals(0, assetCalls)
        assertEquals(0, gateCalls)
    }

    @Test
    fun `仅接收saved丢响应保留任务下次按原清单重试`() {
        receiptFailures.add(PatientDirectiveClient.PHASE_SAVED)
        val first = receiveOnly() as DirectiveRefreshResult.Received
        assertTrue(first.receiptPending)
        assertEquals(ReceiptPhase.NotSubmitted, tasks.values.single().phase)
        assertEquals(first.snapshot, snapshots.stored)
        val second = receiveOnly() as DirectiveRefreshResult.Received
        assertFalse(second.receiptPending)
        assertEquals(ReceiptPhase.Saved, tasks.values.single().phase)
        assertEquals(2, sent.size)
        assertEquals(sent.first(), sent.last())
        assertEquals(0, assetCalls)
    }

    @Test
    fun `仅接收停训覆盖旧处方并持久保存回执时间`() {
        receiveOnly()
        sent.clear()
        directiveJson = stoppedDirective()
        val result = receiveOnly() as DirectiveRefreshResult.Received
        assertFalse(result.canStartTraining)
        assertFalse(result.receiptPending)
        assertNull(result.snapshot.prescription)
        assertEquals(TrainingDirectiveState.Stopped, result.snapshot.state)
        assertEquals("2026-10-21 10:00:00", result.snapshot.stopOrder!!.receivedAt)
        assertEquals(result.snapshot, directiveSnapshotFromStoreJson(snapshots.stored!!.toStoreJson().toString()))
        assertEquals(listOf(Triple("stop", "", 9001L)), sent)
    }

    @Test
    fun `仅接收停训回执失败保留快照下次刷新重试`() {
        directiveJson = stoppedDirective()
        receiptFailures.add("stop")
        val first = receiveOnly() as DirectiveRefreshResult.Received
        assertTrue(first.receiptPending)
        assertNull(snapshots.stored!!.stopOrder!!.receivedAt)
        val second = receiveOnly() as DirectiveRefreshResult.Received
        assertFalse(second.receiptPending)
        assertEquals(listOf(Triple("stop", "", 9001L), Triple("stop", "", 9001L)), sent)
    }

    @Test
    fun `仅接收等待处方状态保存但不发回执`() {
        directiveJson = JSONObject().put("directiveVersion", 2)
            .put("serverTime", "2026-10-21T00:00:00.000Z")
            .put("trainingState", "waiting_prescription")
        val result = receiveOnly() as DirectiveRefreshResult.Received
        assertEquals(TrainingDirectiveState.WaitingPrescription, result.snapshot.state)
        assertEquals(result.snapshot, snapshots.stored)
        assertFalse(result.receiptPending)
        assertTrue(sent.isEmpty())
    }

    @Test
    fun `快照保存失败不得发接收回执`() {
        failSnapshotWrite = true
        assertThrows(IllegalStateException::class.java) { receiveOnly() }
        assertTrue(sent.isEmpty())
        assertTrue(tasks.isEmpty())
    }

    @Test
    fun `D01在途切换绑定不保存旧响应也不发回执`() {
        afterFetch = { scopeCurrent = false }
        assertTrue(receiveOnly() is DirectiveRefreshResult.Unavailable)
        assertNull(snapshots.stored)
        assertTrue(tasks.isEmpty())
        assertTrue(sent.isEmpty())
    }

    @Test
    fun `接收回执在途切换绑定不持久确认旧任务`() {
        afterReceipt = { scopeCurrent = false }
        assertTrue(receiveOnly() is DirectiveRefreshResult.Unavailable)
        assertEquals(ReceiptPhase.NotSubmitted, tasks.values.single().phase)
        assertEquals(listOf(PatientDirectiveClient.PHASE_SAVED), sent.map { it.first })
    }

    @Test
    fun `准备文件后只保持saved等待消费者确认`() {
        val prepared = prepareOnly() as DirectiveRefreshResult.Prepared
        assertFalse(prepared.canStartTraining)
        assertTrue(prepared.handoff.hasVerifiedFiles())
        assertEquals(listOf(PatientDirectiveClient.PHASE_SAVED), sent.map { it.first })
        assertTrue(confirm(prepared, false) is DirectiveRefreshResult.NotTrainable)
        assertEquals(0, statusCalls)
        assertEquals(ReceiptPhase.Saved, tasks.values.single().phase)
    }

    @Test
    fun `准备后租约到期或单调时间回退不能确认ready`() {
        val prepared = prepareOnly() as DirectiveRefreshResult.Prepared
        elapsed += 60_000
        assertTrue(confirm(prepared) is DirectiveRefreshResult.NotTrainable)
        elapsed = 0
        assertTrue(confirm(prepared) is DirectiveRefreshResult.NotTrainable)
        assertEquals(listOf(PatientDirectiveClient.PHASE_SAVED), sent.map { it.first })
        assertEquals(0, statusCalls)
    }

    @Test
    fun `D04正常递增及unlisted仍允许固定处方`() {
        val prepared = prepareOnly() as DirectiveRefreshResult.Prepared
        statusOverride = PrescriptionResourceStatus(prepared.leaseAnchor.lease.copy(revision = 8),
            prepared.handoff.order.items.associate { it.asset.actionVersionId to "unlisted" })
        assertTrue(confirm(prepared) is DirectiveRefreshResult.Ready)
        assertEquals(1, statusCalls)
    }

    @Test
    fun `D04代际异常修订回退或withdrawn均阻断`() {
        val prepared = prepareOnly() as DirectiveRefreshResult.Prepared
        val lease = prepared.leaseAnchor.lease
        val active = prepared.handoff.order.items.associate { it.asset.actionVersionId to "active" }
        for (status in listOf(
            PrescriptionResourceStatus(lease.copy(generation = "other"), active),
            PrescriptionResourceStatus(lease.copy(revision = 6), active),
            PrescriptionResourceStatus(lease, active.mapValues { "withdrawn" })
        )) {
            statusOverride = status
            assertTrue(confirm(prepared) is DirectiveRefreshResult.NotTrainable)
        }
        assertEquals(listOf(PatientDirectiveClient.PHASE_SAVED), sent.map { it.first })
    }

    @Test
    fun `消费者确认后文件被篡改仍不得发送ready`() {
        val prepared = prepareOnly() as DirectiveRefreshResult.Prepared
        val file = prepared.handoff.assets.single().file
        file.writeBytes(ByteArray(file.length().toInt()) { 8 })
        assertTrue(confirm(prepared) is DirectiveRefreshResult.NotTrainable)
        assertEquals(listOf(PatientDirectiveClient.PHASE_SAVED), sent.map { it.first })
    }

    @Test
    fun `准备后换绑不能确认原计划`() {
        val prepared = prepareOnly() as DirectiveRefreshResult.Prepared
        scopeCurrent = false
        assertTrue(confirm(prepared) is DirectiveRefreshResult.Unavailable)
        assertEquals(0, statusCalls)
        assertEquals(listOf(PatientDirectiveClient.PHASE_SAVED), sent.map { it.first })
    }

    @Test
    fun `已有ready仍重新确认服务器状态且失败不交付`() {
        assertTrue(refresh() is DirectiveRefreshResult.Ready)
        receiptFailures.add(PatientDirectiveClient.PHASE_ASSETS_READY)
        val prepared = prepareOnly() as DirectiveRefreshResult.Prepared
        assertTrue(confirm(prepared) is DirectiveRefreshResult.NotTrainable)
        assertEquals(2, sent.count { it.first == PatientDirectiveClient.PHASE_ASSETS_READY })
        assertTrue(tasks.values.single().readyPending)
        assertTrue(confirm(prepared) is DirectiveRefreshResult.Ready)
        assertFalse(tasks.values.single().readyPending)
    }

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
