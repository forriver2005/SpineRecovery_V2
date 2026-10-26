package com.metaspine.mobile.data

import kotlinx.coroutines.runBlocking
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

class RoutineQuestionnaireCoordinatorTest {
    private val scope = RoutineScope("patient", "patient_1", "fingerprint", "http://localhost")
    private val credential = PatientServerCredential("patient_1", "patient_token")
    private val timestamp = "2026-10-26T00:00:00.000Z"
    private var elapsed = 1_000L

    private class MemoryStore : RoutineQuestionnaireStorage {
        private var binding: RoutineScope? = null
        private var invalid = false
        private val rows = linkedMapOf<Long, String>()
        var writes = 0
        override fun bind(scope: RoutineScope) { binding = scope }
        override fun matches(scope: RoutineScope) = binding == scope
        override fun rejected(scope: RoutineScope) = !matches(scope) || invalid
        override fun reject(scope: RoutineScope) { invalid = true }
        override fun read(scope: RoutineScope) = if (matches(scope)) rows.values.map(LocalRoutineTask::decode) else emptyList()
        override fun put(scope: RoutineScope, rows: List<LocalRoutineTask>): Boolean {
            if (rejected(scope)) return false
            rows.forEach { this.rows[it.task.taskId] = it.encode() }
            writes++
            return true
        }
    }

    private fun task(id: Long, sfmpq: Boolean): JSONObject {
        val ids = if (sfmpq) (0..10).map { "sfmpq_sensory_$it" } + (0..3).map { "sfmpq_affective_$it" }
            else listOf("back_pain", "leg_pain", "walking") + (0..6).map { "activity_limits_$it" } + "urination"
        val questions = JSONArray()
        ids.forEach { questionId ->
            val optionCount = if (questionId.startsWith("activity_limits_") || questionId == "urination") 3 else 4
            val options = JSONArray()
            repeat(optionCount) { index -> options.put(JSONObject().put("value", "option_$index").put("label", "选项$index")) }
            questions.put(JSONObject().put("id", questionId).put("type", "single_choice")
                .put("prompt", questionId).put("required", true).put("options", options))
        }
        val code = if (sfmpq) "sf_mpq" else "joa_self_assessment"
        return JSONObject().put("taskId", id).put("status", "available")
            .put("availableAt", timestamp).put("dueAt", JSONObject.NULL).put("deliveredAt", JSONObject.NULL)
            .put("template", JSONObject().put("code", code).put("version", 1).put("name", code)
                .put("contentStatus", "ready").put("schema", JSONObject().put("schemaVersion", 1)
                    .put("title", code).put("questions", questions)))
    }

    private inner class Api(private val tasks: List<JSONObject>) : RoutineQuestionnaireApi {
        val sent = mutableListOf<String>()
        val events = mutableListOf<String>()
        val completed = mutableSetOf<Long>()
        var loseNextReply = false
        override suspend fun open(token: String): JSONObject {
            events += "open"
            val items = JSONArray()
            tasks.filter { it.getLong("taskId") !in completed }.forEach {
                val summary = JSONObject(it.toString())
                summary.getJSONObject("template").remove("schema")
                items.put(summary)
            }
            return JSONObject().put("serverTime", timestamp).put("items", items)
        }
        override suspend fun detail(token: String, taskId: Long) = JSONObject()
            .put("task", JSONObject(tasks.single { it.getLong("taskId") == taskId }.toString()))
        override suspend fun receipt(token: String, taskId: Long, requestId: String) = JSONObject()
            .put("taskId", taskId).put("deliveredAt", timestamp)
        override suspend fun submit(token: String, taskId: Long, body: JSONObject): JSONObject {
            events += "submit"
            sent += body.toString()
            completed += taskId
            if (loseNextReply) { loseNextReply = false; throw IOException("response lost") }
            return JSONObject().put("taskId", taskId).put("recordId", taskId + 100)
                .put("status", "completed").put("completedAt", timestamp)
        }
    }

    private fun answers(row: LocalRoutineTask): JSONObject = JSONObject().apply {
        row.task.schema!!.questions.forEachIndexed { index, question ->
            put(question.id, question.options[index % question.options.size].value)
        }
    }

    private fun answerValues(json: JSONObject) = json.keys().asSequence().associateWith { json.getString(it) }

    @Test fun `complete 11 and 15 answer drafts each persist once before submission`() = runBlocking {
        val store = MemoryStore()
        val api = Api(listOf(task(1, false), task(2, true)))
        val coordinator = RoutineQuestionnaireCoordinator(store, api) { elapsed }
        assertTrue(coordinator.refresh(scope, credential) { true }.available)
        assertEquals(listOf(11, 15), store.read(scope).map { it.task.schema!!.questions.size })
        for (row in store.read(scope)) {
            val draft = answers(row)
            val beforeWrites = store.writes
            coordinator.saveDraft(scope, row.task.taskId, draft.toString()) { true }
            assertEquals(beforeWrites + 1, store.writes)
            val saved = store.read(scope).single { it.task.taskId == row.task.taskId }
            assertEquals(answerValues(draft), answerValues(JSONObject(saved.answers)))
            assertNull(saved.pendingBody)
            coordinator.submit(scope, credential, row.task.taskId) { true }
            val submitted = JSONObject(api.sent.last())
            assertEquals(answerValues(draft), answerValues(submitted.getJSONObject("answers")))
            assertEquals(1, submitted.getInt("templateVersion"))
            assertTrue(submitted.getString("requestId").isNotBlank())
            assertEquals(timestamp, store.read(scope).single { it.task.taskId == row.task.taskId }.completedAt)
        }
        assertEquals(2, api.sent.size)
    }

    @Test fun `incomplete drafts stay local and unknown submission retries the frozen complete body before listing`() = runBlocking {
        val store = MemoryStore()
        val api = Api(listOf(task(1, false)))
        val coordinator = RoutineQuestionnaireCoordinator(store, api) { elapsed }
        coordinator.refresh(scope, credential) { true }
        val complete = answers(store.read(scope).single())
        val incomplete = JSONObject(complete.toString()).apply { remove("urination") }
        coordinator.saveDraft(scope, 1, incomplete.toString()) { true }
        coordinator.submit(scope, credential, 1) { true }
        assertTrue(api.sent.isEmpty())
        assertNull(store.read(scope).single().pendingBody)

        coordinator.saveDraft(scope, 1, complete.toString()) { true }
        api.loseNextReply = true
        coordinator.submit(scope, credential, 1) { true }
        val frozen = store.read(scope).single()
        assertNotNull(frozen.pendingBody)
        assertNull(frozen.completedAt)
        val changed = JSONObject(complete.toString()).put("back_pain", "option_3")
        val writes = store.writes
        coordinator.saveDraft(scope, 1, changed.toString()) { true }
        assertEquals(writes, store.writes)
        assertEquals(frozen.pendingBody, store.read(scope).single().pendingBody)
        assertEquals(answerValues(complete), answerValues(JSONObject(store.read(scope).single().answers)))

        coordinator.submit(scope, credential, 1) { true }
        assertEquals(1, api.sent.size)
        elapsed += 10_000
        api.events.clear()
        coordinator.refresh(scope, credential) { true }
        assertEquals(listOf("submit", "open"), api.events)
        assertEquals(2, api.sent.size)
        assertEquals(api.sent.first(), api.sent.last())
        val completed = store.read(scope).single()
        assertEquals(timestamp, completed.completedAt)
        assertEquals(frozen.pendingBody, completed.confirmedSubmissionBody)
        assertNull(completed.pendingBody)
        assertTrue(pendingLoginQuestionnaires(store.read(scope)).isEmpty())
    }
}
