package com.metaspine.mobile.data

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * P4.1：1/2/10 项 V2 处方解析与固定清单摘要，样例来自服务器 `contract.json`。
 */
class PrescriptionV2ContractTest {

    private fun cases(): List<JSONObject> {
        val text = javaClass.getResourceAsStream("/fixtures/contract.json")!!
            .bufferedReader(Charsets.UTF_8).use { it.readText() }
        val list = JSONObject(text).getJSONArray("cases")
        return (0 until list.length()).map { list.getJSONObject(it) }
    }

    private fun prescriptionOf(case: JSONObject) =
        case.getJSONObject("directive").getJSONObject("activePrescription")

    @Test
    fun `样例覆盖 1 项 2 项和 10 项`() {
        assertEquals(listOf("1-items", "2-items", "10-items"), cases().map { it.getString("name") })
    }

    @Test
    fun `全量条目按 position 有序解析`() {
        for (case in cases()) {
            val json = prescriptionOf(case)
            val parsed = AdmissionPrescriptionV2.fromJson(json, itemIdKey = "itemId")
            assertEquals(case.getString("name"), json.getJSONArray("items").length(), parsed.items.size)
            assertEquals("position 必须连续递增", parsed.items.indices.toList(), parsed.items.map { it.position })
            assertEquals(json.getLong("orderId"), parsed.orderId)
            assertEquals(2, parsed.prescriptionContractVersion)
        }
    }

    @Test
    fun `每项保留冻结资源身份`() {
        for (case in cases()) {
            val json = prescriptionOf(case)
            val items = json.getJSONArray("items")
            val parsed = AdmissionPrescriptionV2.fromJson(json, itemIdKey = "itemId")
            parsed.items.forEachIndexed { index, item ->
                val raw = items.getJSONObject(index)
                val asset = raw.getJSONObject("asset")
                assertEquals(raw.getLong("itemId"), item.itemId)
                assertEquals(raw.getString("actionId"), item.actionId)
                assertEquals(asset.getLong("actionVersionId"), item.asset.actionVersionId)
                assertEquals(asset.getString("sha256"), item.asset.sha256)
                assertEquals(asset.getString("downloadPath"), item.asset.downloadPath)
                assertEquals(asset.getLong("fileSize"), item.asset.fileSize)
                assertEquals(listOf("coach", "game"), item.asset.supportedModes)
                assertEquals("sequence-coach-game-v1", item.asset.doseProfile)
            }
        }
    }

    @Test
    fun `清单摘要与服务器 manifestCanonicalUtf8 的 SHA-256 一致`() {
        for (case in cases()) {
            val parsed = AdmissionPrescriptionV2.fromJson(prescriptionOf(case), itemIdKey = "itemId")
            val expected = ResultDigest.sha256Hex(case.getString("manifestCanonicalUtf8"))
            assertEquals(case.getString("name"), expected, parsed.computeManifestSha256())
            assertEquals("清单摘要必须与处方声明的 resourceManifestSha256 相同",
                parsed.resourceManifestSha256, parsed.computeManifestSha256())
        }
    }

    @Test
    fun `首方以id映射全部条目且不要求D01下载路径`() {
        for (case in cases()) {
            val asActivation = JSONObject(prescriptionOf(case).toString())
            val items = asActivation.getJSONArray("items")
            for (index in 0 until items.length()) {
                val item = items.getJSONObject(index)
                item.put("id", item.getLong("itemId")).remove("itemId")
                item.getJSONObject("asset").remove("downloadPath")
            }
            val parsed = PatientActivationClient.parsePrescriptionV2(asActivation)
            assertEquals(items.length(), parsed.items.size)
            assertTrue(parsed.items.all { it.asset.downloadPath.isEmpty() })
            assertEquals(parsed.resourceManifestSha256, parsed.computeManifestSha256())
            assertEquals(parsed, AdmissionPrescriptionV2.fromJson(parsed.toJson()))
        }
    }

    @Test
    fun `真实A02首次成功及原样重放响应可被完整激活解析器接受`() {
        // 隔离服务器真实 HTTP 201 及同请求 HTTP 200 的响应正文相同，不含 PAT。
        val envelope = javaClass.getResourceAsStream("/fixtures/real-a02.json")!!
            .bufferedReader(Charsets.UTF_8).use { JSONObject(it.readText()) }
        val data = envelope.getJSONObject("data")
        val initial = PatientActivationClient.parseRedeem(data)
        val replay = PatientActivationClient.parseRedeem(JSONObject(data.toString()))
        assertEquals(initial, replay)
        assertEquals(data.getString("patientId"), initial.patientId)
        val prescription = requireNotNull(initial.prescriptionV2)
        assertEquals(1L, prescription.orderId)
        assertEquals(1L, prescription.items.single().itemId)
        assertEquals(2, prescription.items.single().sets)
        assertEquals(6, prescription.items.single().reps)
        assertEquals(5, prescription.items.single().holdSeconds)
        assertEquals("", prescription.items.single().asset.downloadPath)
        assertEquals(prescription.resourceManifestSha256, prescription.computeManifestSha256())
    }

    @Test
    fun `D01仍要求下载路径不受A02缺省映射影响`() {
        val json = JSONObject(prescriptionOf(cases()[0]).toString())
        json.getJSONArray("items").getJSONObject(0).getJSONObject("asset").remove("downloadPath")
        assertThrows(Exception::class.java) {
            AdmissionPrescriptionV2.fromDirectiveJson(json)
        }
    }

    @Test
    fun `ready回执必须提供就绪时间而saved允许空`() {
        val json = JSONObject(cases()[0].getJSONObject("savedReceipt").toString())
        val orderId = json.getLong("orderId")
        val manifest = json.getString("resourceManifestSha256")
        json.put("receivedAt", "2026-10-23 10:00:00").put("assetsReadyAt", JSONObject.NULL)
        PatientDirectiveClient.parsePrescriptionReceipt(json, orderId, "directive_saved", manifest)
        json.put("phase", "assets_ready")
        assertThrows(IllegalArgumentException::class.java) {
            PatientDirectiveClient.parsePrescriptionReceipt(json, orderId, "assets_ready", manifest)
        }
    }

    @Test
    fun `停训回执缺少保存时间不得确认`() {
        assertThrows(IllegalArgumentException::class.java) {
            PatientDirectiveClient.parseStopReceipt(JSONObject().put("orderId", 1), 1)
        }
    }

    @Test
    fun `旧版本处方直接失败而不是截项`() {
        val json = JSONObject(prescriptionOf(cases()[0]).toString())
        json.put("prescriptionContractVersion", 1)
        val error = assertThrows(IllegalArgumentException::class.java) {
            AdmissionPrescriptionV2.fromJson(json, itemIdKey = "itemId")
        }
        assertTrue(error.message!!.contains("合同版本不支持"))
    }

    @Test
    fun `缺少资源身份时失败而不是补默认值`() {
        val json = JSONObject(prescriptionOf(cases()[0]).toString())
        json.getJSONArray("items").getJSONObject(0).getJSONObject("asset").remove("sha256")
        assertThrows(Exception::class.java) {
            AdmissionPrescriptionV2.fromJson(json, itemIdKey = "itemId")
        }
    }

    @Test
    fun `position 与顺序不符时拒绝`() {
        val json = JSONObject(prescriptionOf(cases()[1]).toString())
        json.getJSONArray("items").getJSONObject(1).put("position", 5)
        assertThrows(IllegalArgumentException::class.java) {
            AdmissionPrescriptionV2.fromJson(json, itemIdKey = "itemId")
        }
    }

    @Test
    fun `回执阶段样例携带正确清单摘要`() {
        for (case in cases()) {
            val parsed = AdmissionPrescriptionV2.fromJson(prescriptionOf(case), itemIdKey = "itemId")
            for (phase in listOf("savedReceipt", "readyReceipt")) {
                val receipt = case.getJSONObject(phase)
                assertEquals("$phase 的 orderId", parsed.orderId, receipt.getLong("orderId"))
                assertEquals("$phase 的版本", 2, receipt.getInt("directiveVersion"))
                assertEquals("$phase 的清单摘要", parsed.computeManifestSha256(),
                    receipt.getString("resourceManifestSha256"))
            }
            assertEquals("directive_saved", case.getJSONObject("savedReceipt").getString("phase"))
            assertEquals("assets_ready", case.getJSONObject("readyReceipt").getString("phase"))
        }
    }
}
