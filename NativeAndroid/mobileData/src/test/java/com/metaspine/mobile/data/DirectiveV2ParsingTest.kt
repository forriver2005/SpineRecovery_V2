package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * P4.1：D01 外层版本、1/2/10 项完整解析、unsupported 空处方与本地快照往返。
 *
 * 样例来自服务器 `contract.json`；不支持/停训/等待等分支在此按 P4 §4 的响应形状构造，
 * 因为它们不在样板文件里。
 */
class DirectiveV2ParsingTest {

    private fun cases(): List<JSONObject> {
        val text = javaClass.getResourceAsStream("/fixtures/contract.json")!!
            .bufferedReader(Charsets.UTF_8).use { it.readText() }
        val list = JSONObject(text).getJSONArray("cases")
        return (0 until list.length()).map { list.getJSONObject(it) }
    }

    private fun directiveOf(case: JSONObject) = JSONObject(case.getJSONObject("directive").toString())

    private fun parse(case: JSONObject, patientId: String = "p_1001") =
        TrainingDirectiveParser.parse(patientId, directiveOf(case), 1_700_000_000_000L)

    private fun assertDirectiveProtocol(block: () -> Unit): String {
        val error = assertThrows(DirectiveProtocolException::class.java) { block() }
        return error.message.orEmpty()
    }

    @Test
    fun `1项2项10项都完整解析并保持顺序`() {
        for (case in cases()) {
            val snapshot = parse(case)
            assertEquals(TrainingDirectiveState.Ready, snapshot.state)
            assertEquals(2, snapshot.directiveVersion)
            val order = requireNotNull(snapshot.prescription)
            val rawItems = case.getJSONObject("directive")
                .getJSONObject("activePrescription").getJSONArray("items")
            assertEquals(case.getString("name"), rawItems.length(), order.items.size)
            assertEquals(order.items.indices.toList(), order.items.map { it.position })
            for (index in 0 until rawItems.length()) {
                val raw = rawItems.getJSONObject(index)
                val item = order.items[index]
                assertEquals(raw.getLong("itemId"), item.itemId)
                assertEquals(raw.getString("actionId"), item.actionId)
                assertEquals(raw.getJSONObject("asset").getLong("actionVersionId"),
                    item.asset.actionVersionId)
                assertEquals(raw.getJSONObject("asset").getString("sha256"), item.asset.sha256)
            }
            assertEquals(2, order.prescription.prescriptionContractVersion)
        }
    }

    @Test
    fun `清单摘要与样板 manifestCanonicalUtf8 一致`() {
        for (case in cases()) {
            val order = requireNotNull(parse(case).prescription)
            val expected = ResultDigest.sha256Hex(case.getString("manifestCanonicalUtf8"))
            assertEquals(case.getString("name"), expected, order.resourceManifestSha256)
            assertEquals(expected, order.prescription.computeManifestSha256())
        }
    }

    @Test
    fun `缺少外层版本号时拒绝`() {
        val case = cases()[0]
        val directive = directiveOf(case)
        directive.remove("directiveVersion")
        val message = assertDirectiveProtocol {
            TrainingDirectiveParser.parse("p_1001", directive, 0L)
        }
        assertTrue(message.contains("directiveVersion"))
    }

    @Test
    fun `外层版本为1或字符串时拒绝`() {
        for (replacement in listOf<Any>(1, "2", 3)) {
            val directive = directiveOf(cases()[0])
            directive.put("directiveVersion", replacement)
            assertDirectiveProtocol { TrainingDirectiveParser.parse("p_1001", directive, 0L) }
        }
    }

    @Test
    fun `内层处方版本为1时整份快照失败`() {
        val directive = directiveOf(cases()[0])
        directive.getJSONObject("activePrescription").put("prescriptionContractVersion", 1)
        val message = assertDirectiveProtocol {
            TrainingDirectiveParser.parse("p_1001", directive, 0L)
        }
        assertTrue(message.contains("合同版本"))
    }

    @Test
    fun `ready携带多动作不再是协议错误`() {
        val snapshot = parse(cases()[2])
        val order = requireNotNull(snapshot.prescription)
        assertEquals(10, order.items.size)
        // 执行层仍只支持单动作：能力缺口交给交接层，不在这里截项。
        assertEquals(10, snapshot.trainableOrder()?.items?.size)
    }

    @Test
    fun `unsupported携带空处方时仍保留不可执行状态`() {
        val directive = JSONObject()
            .put("directiveVersion", 2)
            .put("serverTime", "2026-10-21T00:00:00.000Z")
            .put("trainingState", "unsupported_prescription")
            .put("message", "处方暂不可执行，请联系医生")
            .put("activeStopOrder", JSONObject.NULL)
            .put("activePrescription", JSONObject.NULL)
            .put("compatibility", JSONObject()
                .put("code", "prescription_v2_required")
                .put("message", "处方暂不可执行，请联系医生"))
        val snapshot = TrainingDirectiveParser.parse("p_1001", directive, 0L)
        assertEquals(TrainingDirectiveState.UnsupportedPrescription, snapshot.state)
        assertNull(snapshot.prescription)
        assertEquals("prescription_v2_required", snapshot.compatibilityCode)
        assertNull(snapshot.trainableOrder())
    }

    @Test
    fun `unsupported可保留完整V2处方但绝不授予可训练`() {
        val directive = directiveOf(cases()[0])
        directive.put("trainingState", "unsupported_prescription")
        directive.put("compatibility", JSONObject()
            .put("code", "prescription_v2_unavailable")
            .put("message", "处方暂不可执行，请联系医生"))
        val snapshot = TrainingDirectiveParser.parse("p_1001", directive, 0L)
        assertEquals(1, requireNotNull(snapshot.prescription).items.size)
        assertNull(snapshot.trainableOrder())
        assertEquals("prescription_v2_unavailable", snapshot.compatibilityCode)
    }

    @Test
    fun `stop优先并且不保留处方`() {
        val directive = JSONObject()
            .put("directiveVersion", 2)
            .put("serverTime", "2026-10-21T00:00:00.000Z")
            .put("trainingState", "stopped")
            .put("message", "医生已暂停您的训练，请联系医生")
            .put("activePrescription", JSONObject.NULL)
            .put("activeStopOrder", JSONObject()
                .put("orderId", 9001)
                .put("reason", "腰疼复诊")
                .put("createdBy", "doctor_a")
                .put("createdAt", "2026-10-20T08:00:00.000Z")
                .put("receivedAt", JSONObject.NULL)
                .put("items", JSONArray()))
        val snapshot = TrainingDirectiveParser.parse("p_1001", directive, 0L)
        assertEquals(TrainingDirectiveState.Stopped, snapshot.state)
        assertEquals(9001L, requireNotNull(snapshot.stopOrder).orderId)
        assertNull(snapshot.prescription)
        assertNull(snapshot.trainableOrder())
    }

    @Test
    fun `waiting处方不得携带任何指令`() {
        val directive = directiveOf(cases()[0])
        directive.put("trainingState", "waiting_prescription")
        assertDirectiveProtocol { TrainingDirectiveParser.parse("p_1001", directive, 0L) }
    }

    @Test
    fun `本地快照往返保留清单与资源身份`() {
        for (case in cases()) {
            val original = parse(case)
            val restored = requireNotNull(
                directiveSnapshotFromStoreJson(original.toStoreJson().toString()))
            assertEquals(original.patientId, restored.patientId)
            assertEquals(original.directiveVersion, restored.directiveVersion)
            assertEquals(original.state, restored.state)
            assertEquals(original.serverTime, restored.serverTime)
            val before = requireNotNull(original.prescription)
            val after = requireNotNull(restored.prescription)
            assertEquals(before.orderId, after.orderId)
            assertEquals(before.resourceManifestSha256, after.resourceManifestSha256)
            assertEquals(before.items.size, after.items.size)
            assertEquals(before.items.map { it.asset }, after.items.map { it.asset })
            assertEquals(before.items.map { it.sets to it.reps to it.holdSeconds },
                after.items.map { it.sets to it.reps to it.holdSeconds })
        }
    }

    @Test
    fun `旧格式本地快照读不出处方`() {
        val legacy = JSONObject()
            .put("patientId", "p_1001")
            .put("state", "ready")
            .put("serverTime", "2026-01-01T00:00:00Z")
            .put("fetchedAtEpochMillis", 1L)
            .put("message", "")
            .put("prescription", JSONObject()
                .put("orderId", 1)
                .put("items", JSONArray().put(JSONObject()
                    .put("position", 0).put("actionId", "dead_bug")
                    .put("actionName", "死虫式").put("sets", 2).put("reps", 6)
                    .put("holdSeconds", 5))))
        val restored = directiveSnapshotFromStoreJson(legacy.toString())
        // 快照本身可读（用于展示最后一次结果），但不能产生可训练处方。
        assertNotNull(restored)
        assertEquals(0, restored!!.directiveVersion)
        assertNull(restored.prescription)
        assertNull(restored.trainableOrder())
    }

    @Test
    fun `传输层协商头固定为版本2且拒绝其它头`() {
        assertEquals(
            mapOf("X-SpineFlow-Directive-Version" to "2"),
            PatientHttpTransport.DIRECTIVE_VERSION_HEADERS
        )
        val transport = PatientHttpTransport(
            origin = { "https://hospital-a.example.com" },
            openConnection = { error("白名单外的头必须在建立连接前失败") })
        assertThrows(IllegalArgumentException::class.java) {
            kotlinx.coroutines.runBlocking {
                transport.request(
                    "/api/patient/training-directive",
                    negotiationHeaders = mapOf("X-Other" to "1"))
            }
        }
    }
}
