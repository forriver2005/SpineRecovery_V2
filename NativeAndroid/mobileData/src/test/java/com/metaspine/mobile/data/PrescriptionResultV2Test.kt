package com.metaspine.mobile.data

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

/**
 * P5.1 / P5.2 / P5.4：V2 结果输入边界、严格序列化与冻结摘要。
 *
 * 输入全部取自服务器 `tests/fixtures/mobile-bridge/contract.json` 的原始 `request`，
 * 不在测试里重建快照；期望值同样来自服务器生成的 canonicalUtf8 / sha256 / receipt。
 */
class PrescriptionResultV2Test {

    private val fixture: JSONObject by lazy {
        val text = javaClass.getResourceAsStream("/fixtures/contract.json")!!
            .bufferedReader(Charsets.UTF_8).use { it.readText() }
        JSONObject(text)
    }
    private val patientId: String get() = fixture.getString("patientId")
    private val cases: List<JSONObject>
        get() = fixture.getJSONArray("cases").let { array ->
            (0 until array.length()).map { array.getJSONObject(it) }
        }

    private fun snapshots(): List<Pair<JSONObject, JSONObject>> = cases.flatMap { case ->
        (0 until case.getJSONArray("results").length()).map { index ->
            case.getJSONArray("results").getJSONObject(index) to case
        }
    }

    private fun requestOf(pair: Pair<JSONObject, JSONObject>): JSONObject =
        JSONObject(pair.first.getJSONObject("request").toString())

    private fun parseError(raw: JSONObject): ResultInputException {
        try {
            PrescriptionResultV2.parse(raw, patientId)
        } catch (error: ResultInputException) {
            return error
        }
        fail("应当拒收，但解析成功")
        error("unreachable")
    }

    @Test
    fun `服务器样例全部可解析且摘要逐字节一致`() {
        val all = snapshots()
        assertEquals("3 个处方 × 3 个修订 = 9 份结果快照", 9, all.size)
        for ((snapshot, _) in all) {
            val raw = requestOf(snapshot to JSONObject())
            val parsed = PrescriptionResultV2.parse(raw, patientId)
            val frozen = parsed.freeze(patientId)
            assertEquals(
                "规范文本不一致: ${parsed.sessionId}#${parsed.resultRevision}",
                snapshot.getString("canonicalUtf8"),
                frozen.canonicalUtf8
            )
            assertEquals(
                "摘要不一致: ${parsed.sessionId}#${parsed.resultRevision}",
                snapshot.getString("sha256"),
                frozen.contentSha256
            )
            assertEquals(
                snapshot.getJSONObject("receipt").getString("resultContentSha256"),
                frozen.receipt.resultContentSha256
            )
            assertEquals(
                snapshot.getJSONObject("receipt").getLong("resultRevision"),
                frozen.receipt.resultRevision
            )
            val utf8Hex = frozen.canonicalUtf8.toByteArray(Charsets.UTF_8)
                .joinToString("") { "%02x".format(it) }
            assertEquals(snapshot.getString("utf8Hex"), utf8Hex)
        }
    }

    @Test
    fun `修订号递增且身份在重试中保持不变`() {
        val case = cases.first()
        val requests = (0 until case.getJSONArray("results").length()).map {
            case.getJSONArray("results").getJSONObject(it).getJSONObject("request")
        }
        val parsed = requests.map { PrescriptionResultV2.parse(JSONObject(it.toString()), patientId) }
        assertEquals(listOf(1L, 2L, 3L), parsed.map { it.resultRevision })
        assertEquals(1, parsed.map { it.sessionId }.distinct().size)
        assertEquals(1, parsed.map { it.requestToken }.distinct().size)
        assertEquals(1, parsed.map { it.createdAt }.distinct().size)
        assertEquals(1, parsed.map { it.prescriptionOrderId }.distinct().size)
        assertEquals(1, parsed.map { it.resourceManifestSha256 }.distinct().size)
        // 未开始的快照允许没有逐模式细节，但“开始训练但全 0 次”绝不能被当成完成。
        assertTrue(parsed.first().status == ResultSessionStatus.INTERRUPTED)
        assertTrue(parsed.last().completed)
    }

    @Test
    fun `序列化白名单不含反馈与旧指标`() {
        val parsed = PrescriptionResultV2.parse(
            JSONObject(cases.first().getJSONArray("results").getJSONObject(1)
                .getJSONObject("request").toString()),
            patientId
        )
        val body = parsed.toJson()
        val allowed = setOf(
            "protocolVersion", "sessionId", "requestToken", "userId", "createdAt",
            "completed", "status", "durationSeconds", "prescriptionOrderId",
            "resourceManifestSha256", "resultRevision", "source", "itemResults",
            "preTrainingEventId"
        )
        val keys = body.keys().asSequence().toSet()
        assertTrue("出现多余字段: ${keys - allowed}", allowed.containsAll(keys))
        assertTrue(!body.has("feedback"))
        assertTrue(!body.has("actionResults"))
        assertTrue(!body.has("gameResults"))
        assertTrue(!body.has("overallScore"))
        assertEquals(2, body.getInt("protocolVersion"))
        assertEquals(JSONObject.NULL, body.optJSONArray("itemResults")!!
            .getJSONObject(0).getJSONArray("modeResults")
            .getJSONObject(1).get("score"))
    }

    @Test
    fun `显式userId必须与PAT一致`() {
        val raw = requestOf(snapshots().first())
        raw.put("userId", "someone_else")
        assertTrue(parseError(raw) is ResultInputException.PatientMismatch)

        raw.put("userId", patientId)
        assertTrue(PrescriptionResultV2.parse(raw, patientId).userId == patientId)
    }

    @Test
    fun `协议版本必须是数字2`() {
        val raw = requestOf(snapshots().first())
        raw.put("protocolVersion", "2")
        assertTrue(parseError(raw) is ResultInputException.ProtocolUnsupported)

        raw.put("protocolVersion", 1)
        assertTrue(parseError(raw) is ResultInputException.ProtocolUnsupported)

        raw.remove("protocolVersion")
        assertTrue(parseError(raw) is ResultInputException.ProtocolUnsupported)
    }

    @Test
    fun `UTC时间拒绝归一化的非法值与多余精度`() {
        for (bad in listOf(
            "2026-02-30T01:02:03Z",
            "2026-10-21T24:00:00Z",
            "2026-10-21T00:01:00+08:00",
            "2026-10-21T00:01:00.1Z",
            "2026-10-21T00:01:00.12Z",
            "2026-10-21T00:01:00.1234567Z",
            "2026-10-21 00:01:00Z"
        )) {
            val raw = requestOf(snapshots().first()).put("createdAt", bad)
            assertTrue("应拒收 $bad", parseError(raw) is ResultInputException.ResultInvalid)
        }
        assertTrue(
            PrescriptionResultV2.parse(
                requestOf(snapshots().first()).put("createdAt", "2026-10-21T00:01:00Z"),
                patientId
            ).createdAt == "2026-10-21T00:01:00Z"
        )
    }

    @Test
    fun `字段白名单之外的内容整体拒收`() {
        val raw = requestOf(snapshots().first()).put("feedback", JSONObject().put("feeling", 2))
        assertTrue(parseError(raw) is ResultInputException.ResultInvalid)

        val itemRaw = requestOf(snapshots().first())
        itemRaw.getJSONArray("itemResults").getJSONObject(0)
            .put("actionName", "夹带动作名")
        assertTrue(parseError(itemRaw) is ResultInputException.ResultInvalid)

        val modeRaw = requestOf(snapshots().first())
        modeRaw.getJSONArray("itemResults").getJSONObject(0)
            .getJSONArray("modeResults").getJSONObject(0).put("metrics", JSONObject())
        assertTrue(parseError(modeRaw) is ResultInputException.ResultInvalid)
    }

    @Test
    fun `条目顺序变体与模式顺序必须严格匹配`() {
        val position = requestOf(snapshots().last())
        position.getJSONArray("itemResults").getJSONObject(1).put("position", 5)
        assertTrue(parseError(position) is ResultInputException.ResultInvalid)

        val variant = requestOf(snapshots().first())
        variant.getJSONArray("itemResults").getJSONObject(0).put("variantKey", "alt")
        assertTrue(parseError(variant) is ResultInputException.ResultInvalid)

        val order = requestOf(snapshots().first())
        val modes = order.getJSONArray("itemResults").getJSONObject(0).getJSONArray("modeResults")
        val first = JSONObject(modes.getJSONObject(0).toString())
        modes.put(0, modes.getJSONObject(1))
        modes.put(1, first)
        assertTrue(parseError(order) is ResultInputException.ResultInvalid)

        val missing = requestOf(snapshots().first())
        missing.getJSONArray("itemResults").getJSONObject(0)
            .put("modeResults", org.json.JSONArray().put(missing.getJSONArray("itemResults")
                .getJSONObject(0).getJSONArray("modeResults").getJSONObject(0)))
        assertTrue(parseError(missing) is ResultInputException.ResultInvalid)
    }

    @Test
    fun `未开始模式的四元组不可伪造`() {
        val withReps = requestOf(snapshots().first())
        withReps.getJSONArray("itemResults").getJSONObject(0)
            .getJSONArray("modeResults").getJSONObject(0).put("completedReps", 3)
        assertTrue(parseError(withReps) is ResultInputException.ResultInvalid)

        val withScore = requestOf(snapshots().first())
        withScore.getJSONArray("itemResults").getJSONObject(0)
            .getJSONArray("modeResults").getJSONObject(0).put("score", 50)
        assertTrue(parseError(withScore) is ResultInputException.ResultInvalid)

        val wrongSource = requestOf(snapshots().first())
        wrongSource.getJSONArray("itemResults").getJSONObject(0)
            .getJSONArray("modeResults").getJSONObject(0).put("source", "ar_measured")
        assertTrue(parseError(wrongSource) is ResultInputException.ResultInvalid)

        val measuredAsNotStarted = requestOf(snapshots().first())
        measuredAsNotStarted.getJSONArray("itemResults").getJSONObject(0)
            .getJSONArray("modeResults").getJSONObject(1).put("source", "not_started")
            .put("status", "COMPLETED").put("completedReps", 1)
        assertTrue(parseError(measuredAsNotStarted) is ResultInputException.ResultInvalid)
    }

    @Test
    fun `首个未完成之后不能再出现已完成模式`() {
        // 全部完成的快照：把第一项 coach 改成未开始，后面已完成的模式就落到了它之后。
        val raw = requestOf(snapshots().last())
        raw.getJSONArray("itemResults").getJSONObject(0)
            .getJSONArray("modeResults").getJSONObject(0)
            .put("status", "NOT_STARTED")
            .put("completedReps", 0)
            .put("durationSeconds", 0)
            .put("score", JSONObject.NULL)
            .put("source", "not_started")
        raw.getJSONArray("itemResults").getJSONObject(0).put("status", "INTERRUPTED")
        raw.put("status", "INTERRUPTED").put("completed", false)
        assertTrue(parseError(raw) is ResultInputException.ResultInvalid)
    }

    @Test
    fun `条目状态必须由逐模式推导`() {
        val raw = requestOf(snapshots().first())
        raw.getJSONArray("itemResults").getJSONObject(0).put("status", "COMPLETED")
        assertTrue(parseError(raw) is ResultInputException.ResultInvalid)
    }

    @Test
    fun `总时长不得小于逐模式之和`() {
        // rev3 总时长 30 = 30，压到 29 才违反。
        val raw = requestOf(snapshots().last()).put("durationSeconds", 29)
        assertTrue(parseError(raw) is ResultInputException.ResultInvalid)
    }

    @Test
    fun `mobile_no_details只能表达全部未开始`() {
        // rev2 已测得 coach 完成，不能改写成没有细节的快照。
        val raw = requestOf(snapshots()[1])
        raw.put("source", "mobile_no_details")
        assertTrue(parseError(raw) is ResultInputException.ResultInvalid)
    }

    @Test
    fun `标识与摘要格式严格`() {
        val revision = requestOf(snapshots().first()).put("resultRevision", 0)
        assertTrue(parseError(revision) is ResultInputException.ResultInvalid)

        val shortHash = requestOf(snapshots().first())
            .put("resourceManifestSha256", "abc")
        assertTrue(parseError(shortHash) is ResultInputException.ResultInvalid)

        val upperHash = requestOf(snapshots().first())
            .put("resourceManifestSha256", "A".repeat(64))
        assertTrue(parseError(upperHash) is ResultInputException.ResultInvalid)

        val badToken = requestOf(snapshots().first()).put("requestToken", "has space")
        assertTrue(parseError(badToken) is ResultInputException.ResultInvalid)

        val longToken = requestOf(snapshots().first()).put("sessionId", "x".repeat(129))
        assertTrue(parseError(longToken) is ResultInputException.ResultInvalid)
    }
}
