package com.metaspine.mobile.data

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

/**
 * P5.3 / P5.6：修订号、冻结身份与依赖恢复。
 *
 * 输入仍是服务器 fixture 的原始 request；同一 sessionId 的三次修订对应
 * rev1 未开始快照 → rev2 首模式完成 → rev3 全部完成。
 */
class ResultRevisionPolicyTest {

    private val fixture: JSONObject by lazy {
        javaClass.getResourceAsStream("/fixtures/contract.json")!!
            .bufferedReader(Charsets.UTF_8).use { it.readText() }
            .let(::JSONObject)
    }
    private val patientId: String get() = fixture.getString("patientId")
    private val scope = "account:bridge"

    /** 单个 case 的三次修订。 */
    private fun revisions(caseIndex: Int = 0): List<PrescriptionResultV2> {
        val results = fixture.getJSONArray("cases").getJSONObject(caseIndex)
            .getJSONArray("results")
        return (0 until results.length()).map { index ->
            PrescriptionResultV2.parse(
                JSONObject(results.getJSONObject(index).getJSONObject("request").toString()),
                patientId
            )
        }
    }

    private fun freeze(result: PrescriptionResultV2) =
        ResultRevisionPolicy.next(scope, result, result.freeze(patientId), null)

    private fun conflict(block: () -> Unit): RevisionConflict {
        try {
            block()
        } catch (error: RevisionConflict) {
            return error
        }
        fail("应当判定为修订冲突")
        error("unreachable")
    }

    @Test
    fun `首次冻结保留服务器修订号与摘要`() {
        val result = revisions().first()
        val record = freeze(result)
        assertEquals(1L, record.resultRevision)
        assertEquals(patientId, fixture.getString("patientId"))
        assertEquals(result.contentSha256(patientId), record.contentSha256)
        assertEquals(AckState.Pending, record.ackState)
        assertNull(record.blockedBy)
        assertNull(record.acknowledgedRevision)
        assertEquals(listOf(1001L), record.itemIds)
        assertEquals(listOf("1001:coach", "1001:game"), record.modeKeys)
    }

    @Test
    fun `同一份内容重试不递增修订号`() {
        val result = revisions().first()
        val first = freeze(result)
        val again = ResultRevisionPolicy.next(scope, result, result.freeze(patientId), first)
        assertSame("同一份事实必须原样返回", first, again)
        assertEquals(1L, again.resultRevision)
        assertEquals(first.contentSha256, again.contentSha256)
        assertEquals(first.body, again.body)
    }

    @Test
    fun `内容变化必须严格递增修订号`() {
        val all = revisions()
        val rev1 = freeze(all[0])
        val rev2 = ResultRevisionPolicy.next(scope, all[1], all[1].freeze(patientId), rev1)
        val rev3 = ResultRevisionPolicy.next(scope, all[2], all[2].freeze(patientId), rev2)
        assertEquals(listOf(1L, 2L, 3L), listOf(rev1.resultRevision, rev2.resultRevision, rev3.resultRevision))
        assertNotEquals(rev1.contentSha256, rev2.contentSha256)
        assertNotEquals(rev2.contentSha256, rev3.contentSha256)
        assertEquals(rev1.requestToken, rev3.requestToken)
        assertEquals(rev1.createdAt, rev3.createdAt)
    }

    @Test
    fun `允许跳跃但不允许回退或重复`() {
        val all = revisions()
        val rev2 = ResultRevisionPolicy.next(scope, all[1], all[1].freeze(patientId), freeze(all[0]))
        val jumped = PrescriptionResultV2.parse(
            JSONObject(all[2].toJson().toString()).put("resultRevision", 7),
            patientId
        )
        val rev7 = ResultRevisionPolicy.next(scope, jumped, jumped.freeze(patientId), rev2)
        assertEquals(7L, rev7.resultRevision)

        // 内容已变但修订号没变：拒绝（否则服务器会判定为同修订内容冲突）。
        conflict {
            ResultRevisionPolicy.next(scope, all[2], all[2].freeze(patientId), rev7)
        }
        // 修订号回退：拒绝。
        conflict {
            val regressed = PrescriptionResultV2.parse(
                JSONObject(all[2].toJson().toString()).put("resultRevision", 3),
                patientId
            )
            ResultRevisionPolicy.next(scope, regressed, regressed.freeze(patientId), rev7)
        }
    }

    @Test
    fun `冻结身份的任何改写都被拒绝`() {
        val rev1 = freeze(revisions().first())
        val rev3 = revisions().last()
        for (mutate in listOf<(JSONObject) -> Unit>(
            { it.put("requestToken", "another_token") },
            { it.put("createdAt", "2026-10-21T00:02:00.000Z") },
            { it.put("prescriptionOrderId", 9999) },
            { it.put("resourceManifestSha256", "0".repeat(64)) },
        )) {
            val altered = PrescriptionResultV2.parse(
                JSONObject(rev3.toJson().toString()).also(mutate),
                patientId
            )
            conflict {
                ResultRevisionPolicy.next(scope, altered, altered.freeze(patientId), rev1)
            }
        }
    }

    @Test
    fun `不同scope的修订号互不影响`() {
        val all = revisions()
        val otherScopeRev1 = ResultRevisionPolicy.next(
            "account:other", all[0], all[0].freeze(patientId), null
        )
        val thisScopeRev2 = ResultRevisionPolicy.next(scope, all[1], all[1].freeze(patientId), freeze(all[0]))
        assertEquals(1L, otherScopeRev1.resultRevision)
        assertEquals(2L, thisScopeRev2.resultRevision)
        assertEquals(scope, thisScopeRev2.scope)
    }

    @Test
    fun `依赖阻塞保留原事件且不产生新修订`() {
        val rev2 = ResultRevisionPolicy.next(scope, revisions()[1], revisions()[1].freeze(patientId), freeze(revisions()[0]))
        val eventId = rev2.preTrainingEventId
        assertTrue("fixture rev2 应携带练前事件", eventId != null)

        val blocked = rev2.copy(blockedBy = eventId, ackState = AckState.Pending)
        val retried = ResultRevisionPolicy.next(scope, revisions()[1], revisions()[1].freeze(patientId), blocked)
        assertSame(blocked, retried)
        assertEquals(eventId, blocked.blockedBy)

        // 换一个练前事件来「绕过」阻塞：拒绝。
        val swapped = PrescriptionResultV2.parse(
            JSONObject(revisions()[1].toJson().toString()).put("preTrainingEventId", "pre:other"),
            patientId
        )
        conflict {
            ResultRevisionPolicy.next(scope, swapped, swapped.freeze(patientId), blocked)
        }

        val cleared = blocked.copy(blockedBy = null, ackState = AckState.Pending)
        assertEquals(rev2.resultRevision, cleared.resultRevision)
        assertEquals(rev2.contentSha256, cleared.contentSha256)
    }

    @Test
    fun `出站正文与回执四元组一致`() {
        val result = revisions()[2]
        val record = ResultRevisionPolicy.next(scope, result, result.freeze(patientId), null)
        assertEquals(result.toJson().toString(), record.body)
        assertEquals(result.sessionId, record.receipt.sessionId)
        assertEquals(record.resultRevision, record.receipt.resultRevision)
        assertEquals(record.contentSha256, record.receipt.resultContentSha256)
        assertTrue(!record.body.contains("\"userId\""))
        assertTrue(record.canonicalUtf8.contains("\"userId\":\"$patientId\""))
    }

    @Test
    fun `记录可完整往返持久化`() {
        val result = revisions().last()
        val record = ResultRevisionPolicy.next(scope, result, result.freeze(patientId), null)
            .copy(ackState = AckState.InFlight, acknowledgedRevision = 2)
        val restored = ResultRecord.fromJson(JSONObject(record.toJson().toString()))
        assertEquals(record, restored)
        assertEquals(record.body, restored.body)
        assertEquals(AckState.InFlight, restored.ackState)
        assertEquals(2L, restored.acknowledgedRevision)
    }
}
