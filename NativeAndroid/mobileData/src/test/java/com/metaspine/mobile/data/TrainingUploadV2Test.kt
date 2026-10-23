package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

/**
 * P5.5 / P5.7：V2 上传体与逐条 ACK。
 *
 * 回执证据来自服务器 fixture 的 `receipt`；请求体必须与冻结快照逐字一致，
 * 任何缺失、多余、重复或矛盾的 ACK 都算「未确认」。
 */
class TrainingUploadV2Test {

    private val fixture: JSONObject by lazy {
        javaClass.getResourceAsStream("/fixtures/contract.json")!!
            .bufferedReader(Charsets.UTF_8).use { it.readText() }
            .let(::JSONObject)
    }
    private val patientId: String get() = fixture.getString("patientId")

    private fun revisionCase(caseIndex: Int): ResultRecord {
        val raw = fixture.getJSONArray("cases").getJSONObject(caseIndex)
            .getJSONArray("results").getJSONObject(2)
        val parsed = PrescriptionResultV2.parse(JSONObject(raw.getJSONObject("request").toString()), patientId)
        return ResultRevisionPolicy.next("account:bridge", parsed, parsed.freeze(patientId), null)
    }

    private fun expectedOf(vararg cases: Int): List<ResultReceipt> =
        cases.map { revisionCase(it).receipt }

    private fun successEnvelope(receipts: List<ResultReceipt>): JSONObject = JSONObject()
        .put("code", 0)
        .put("data", JSONObject()
            .put("uploaded", receipts.size)
            .put("skipped", 0)
            .put("resultReceipts", JSONArray(receipts.map { it.toJson() })))

    private fun rejection(build: (JSONObject) -> JSONObject): String {
        val expected = expectedOf(0, 1)
        val broken = build(successEnvelope(expected))
        try {
            TrainingSyncClient.verifyUploadResponse(broken, expected)
        } catch (error: RuntimeException) {
            return error.message.orEmpty()
        }
        return ""
    }

    /** 期望被拒且理由包含 [fragment]。 */
    private fun mustReject(expected: List<ResultReceipt>, fragment: String, build: (JSONObject) -> JSONObject) {
        val broken = build(successEnvelope(expected))
        val message = try {
            TrainingSyncClient.verifyUploadResponse(broken, expected)
            null
        } catch (error: RuntimeException) {
            error.message.orEmpty()
        }
        if (message == null) {
            fail("应当判定为未确认")
            return
        }
        assertTrue("应包含「$fragment」，实际: $message", message.contains(fragment))
    }

    @Test
    fun `请求体逐字使用冻结快照`() {
        val records = listOf(revisionCase(0), revisionCase(1))
        val body = TrainingSyncClient.buildV2UploadBody(records)
        val sessions = body.getJSONArray("sessions")
        assertEquals(2, sessions.length())
        val wire = TrainingSyncClient.serializeFrozenSessions(sessions)
        assertEquals("{\"sessions\":[${records.joinToString(",") { it.body }}]}", wire)
        assertEquals(records[0].body, CanonicalJson.canonicalize(sessions.getJSONObject(0)))
        assertEquals(records[1].body, CanonicalJson.canonicalize(sessions.getJSONObject(1)))
        // 不含旧 V1 包装字段，也不注入本机补写的 key。
        assertEquals(setOf("sessions"), body.keys().asSequence().toSet())
        assertEquals(2, sessions.getJSONObject(0).getInt("protocolVersion"))
    }

    @Test
    fun `完整回执按原顺序通过`() {
        val expected = expectedOf(0, 1)
        val receipts = TrainingSyncClient.verifyUploadResponse(successEnvelope(expected), expected)
        assertEquals(expected, receipts)
        assertEquals("bridge_1", receipts[0].sessionId)
        assertEquals(3L, receipts[0].resultRevision)
    }

    @Test
    fun `缺少data或逐条回执一律不确认`() {
        val expected = expectedOf(0)
        for (broken in listOf(
            JSONObject().put("code", 0),
            JSONObject().put("code", 0).put("data", JSONObject().put("uploaded", 1).put("skipped", 0)),
            successEnvelope(expected).put("data", JSONObject().put("uploaded", 1).put("skipped", 0)),
        )) {
            try {
                TrainingSyncClient.verifyUploadResponse(broken, expected)
                fail("缺少 data/resultReceipts 不得确认")
            } catch (_: RuntimeException) {
            }
        }
    }

    @Test
    fun `裸HTTP200或非零code不确认`() {
        val expected = expectedOf(0)
        try {
            TrainingSyncClient.verifyUploadResponse(JSONObject(), expected)
            fail("空响应不得确认")
        } catch (_: RuntimeException) {
        }
        assertTrue(rejection { it.put("code", 1) }.isNotEmpty())
    }

    @Test
    fun `缺一条或多一条都不确认`() {
        val two = expectedOf(0, 1)
        mustReject(two, "数量") { envelope ->
            envelope.getJSONObject("data").getJSONArray("resultReceipts").remove(0)
            envelope
        }
        mustReject(two, "不符") { envelope ->
            envelope.getJSONObject("data").getJSONArray("resultReceipts")
                .put(two[0].copy(sessionId = "bridge_extra").toJson())
            envelope
        }
        mustReject(two, "不符") { envelope ->
            // 数量对齐，但第二条换成了本批之外的结果。
            envelope.getJSONObject("data").getJSONArray("resultReceipts")
                .put(1, two[0].copy(sessionId = "bridge_extra").toJson())
            envelope
        }
    }

    @Test
    fun `重复回执不确认`() {
        // 单条批次：数量对齐，只有重复键可以被发现。
        val expected = expectedOf(0)
        mustReject(expected, "重复") { envelope ->
            val data = envelope.getJSONObject("data")
            data.put("uploaded", 2)
            val receipts = data.getJSONArray("resultReceipts")
            receipts.put(JSONObject(receipts.getJSONObject(0).toString()))
            envelope
        }
    }

    @Test
    fun `修订号或摘要不符不确认`() {
        val expected = expectedOf(0, 1)
        mustReject(expected, "不符") { envelope ->
            envelope.getJSONObject("data").getJSONArray("resultReceipts")
                .getJSONObject(0).put("resultRevision", 4)
            envelope
        }
        mustReject(expected, "摘要") { envelope ->
            envelope.getJSONObject("data").getJSONArray("resultReceipts")
                .getJSONObject(0).put("resultContentSha256", "b".repeat(64))
            envelope
        }
    }

    @Test
    fun `skipped非零或uploaded不足不确认`() {
        val expected = expectedOf(0, 1)
        mustReject(expected, "跳过") { envelope ->
            envelope.getJSONObject("data").put("skipped", 1)
            envelope
        }
        mustReject(expected, "只确认") { envelope ->
            // 回执数量仍是 2，但 uploaded 只承认 1 条 → 只确认了 1/2。
            envelope.getJSONObject("data").put("uploaded", 1)
            envelope
        }
    }

    @Test
    fun `队列游标保留scope与逐条回执`() {
        val record = revisionCase(0)
        val cursor = QueueCursor(
            resultScope = record.scope,
            resultReceipts = listOf(record.receipt),
        )
        val restored = QueueCursor.fromJson(JSONObject(cursor.toJson().toString()))
        assertEquals(record.scope, restored.resultScope)
        assertEquals(listOf(record.receipt), restored.resultReceipts)
        // 旧队列元素没有这两个键：必须退化为「无 V2 任务」，而不是抛异常。
        val legacy = QueueCursor.fromJson(JSONObject().put("sessionIds", JSONArray().put("s")))
        assertNull(legacy.resultScope)
        assertTrue(legacy.resultReceipts.isEmpty())
    }

    @Test
    fun `快照与回执数量不符时不发请求`() {
        val record = revisionCase(0)
        val short = kotlinx.coroutines.runBlocking {
            TrainingSyncClient.uploadV2(listOf(record), emptyList(), "pat")
        }
        assertTrue(short.isFailure)
        assertTrue(short.exceptionOrNull()!!.message!!.contains("数量不一致"))

        val empty = kotlinx.coroutines.runBlocking {
            TrainingSyncClient.uploadV2Sessions(JSONArray(), listOf(record.receipt), "pat")
        }
        assertTrue(empty.isFailure)
        assertTrue(empty.exceptionOrNull()!!.message!!.contains("没有可上传"))

        val mismatched = kotlinx.coroutines.runBlocking {
            TrainingSyncClient.uploadV2Sessions(
                JSONArray().put(JSONObject(record.body)),
                listOf(record.receipt, record.receipt),
                "pat",
            )
        }
        assertTrue(mismatched.isFailure)
        assertTrue(mismatched.exceptionOrNull()!!.message!!.contains("数量不一致"))
    }

    @Test
    fun `同一结果重发不改变回执`() {
        val record = revisionCase(2)
        val body = TrainingSyncClient.buildV2UploadBody(listOf(record))
        assertEquals(body.toString(), TrainingSyncClient.buildV2UploadBody(listOf(record)).toString())
        val receipt = TrainingSyncClient.verifyUploadResponse(
            successEnvelope(listOf(record.receipt)), listOf(record.receipt)
        ).single()
        assertEquals(record.receipt, receipt)
    }
}
