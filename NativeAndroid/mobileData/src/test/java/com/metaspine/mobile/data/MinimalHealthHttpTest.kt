package com.metaspine.mobile.data

import kotlinx.coroutines.runBlocking
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.time.LocalDate
import java.time.ZoneId
import java.util.UUID

/** Loopback business-contract HTTP only; does not validate Android LAN discovery or the Unity form. */
class MinimalHealthHttpTest {
    @Test fun immutableHealthEventsReachDoctorAndReplayWithoutDuplicates() = runBlocking {
        val fixture = fixture()
        val origin = fixture.getString("baseUrl")
        val api = FixturePatientApi(origin)
        val item = fixture.getJSONObject("healthCase")
        val credential = credential(item)
        val body = JSONObject(item.getJSONObject("syncBody").toString())
        val previousOrigin = SpineFlowServerConfig.baseUrlOverride
        // The explicit frozen origin must survive a concurrent UI hospital change.
        SpineFlowServerConfig.baseUrlOverride = fixture.getString("otherHospitalBaseUrl")
        try {
            assertFalse(body.getJSONObject("preTrainingQuestionnaire").has("symptomChange"))
            assertFalse(body.getJSONObject("preTrainingQuestionnaire").has("painLocationChanged"))
            api.sync(body, credential.patientToken).getOrThrow()
            val first = doctor(fixture, "/api/doctor/patients/${credential.patientId}/questionnaires?size=100")
            api.sync(body, credential.patientToken).getOrThrow()
            val replay = doctor(fixture, "/api/doctor/patients/${credential.patientId}/questionnaires?size=100")
            assertEquals(first.getInt("total"), replay.getInt("total"))
            val payloads = replay.getJSONArray("items").objects().map { JSONObject(it.getString("payloadJson")) }
            for (field in listOf("admissionSurvey", "admissionIntake", "mobileAdmissionAssessment", "preTrainingQuestionnaire")) {
                val expected = body.getJSONObject(field)
                val stored = payloads.single { it.optString("eventId") == expected.getString("eventId") }
                assertEquals("Frozen $field must be preserved", CanonicalJson.canonicalize(expected), CanonicalJson.canonicalize(stored))
            }
            val profile = doctor(fixture, "/api/doctor/patients/${credential.patientId}").getJSONObject("latestProfile")
            assertEquals(172.5, profile.getDouble("height_cm"), 0.0)
            assertEquals(63.5, profile.getDouble("weight_kg"), 0.0)
            val pain = doctor(fixture, "/api/doctor/patients/${credential.patientId}/pain-trend?days=30")
                .getJSONArray("items").objects().filter { it.getString("bodyPart") == "left_waist" }
            assertEquals(listOf(3.0, 7.0), pain.map { it.getDouble("avgLevel") })
            assertTrue(pain.all { it.getInt("count") == 1 })
        } finally { SpineFlowServerConfig.baseUrlOverride = previousOrigin }
    }

    @Test fun assignedQuestionnairePersistsReceiptAndRecoversLostSubmissionResponse() = runBlocking {
        val fixture = fixture()
        val origin = fixture.getString("baseUrl")
        val item = fixture.getJSONObject("healthCase")
        val credential = credential(item)
        val taskId = assignFreshTask(fixture, item)
        val identity = JSONObject(http(origin, "/api/service-identity")).getJSONObject("data")
        val binding = BindingScope(origin, identity.getString("serviceId"), identity.getString("hospitalKey"),
            identity.getString("hospitalName"), credential.patientId, item.getJSONObject("activationBody").getString("accountId"), 1)
        val scope = RoutineScope.of(binding.accountId, credential, origin, binding)
        val previousOrigin = SpineFlowServerConfig.baseUrlOverride
        SpineFlowServerConfig.baseUrlOverride = fixture.getString("otherHospitalBaseUrl")
        val file = File.createTempFile("routine-http-", ".json").apply { deleteOnExit() }
        val store = FileRoutineStore(file)
        val real = FixturePatientApi(origin)
        var firstSubmission: String? = null
        var serverReceipt: JSONObject? = null
        val loseResponse = object : RoutineQuestionnaireApi by real {
            override fun atOrigin(origin: String): RoutineQuestionnaireApi {
                require(origin == scope.origin)
                return this
            }
            override suspend fun submit(token: String, taskId: Long, body: JSONObject): JSONObject {
                firstSubmission = body.toString()
                serverReceipt = real.submit(token, taskId, body)
                throw IOException("Simulated response loss after the disposable server committed")
            }
        }
        try {
            val coordinator = RoutineQuestionnaireCoordinator(store, loseResponse)
            val fetched = coordinator.refresh(scope, credential) { true }
            assertTrue(fetched.message, fetched.available)
            val downloaded = store.read(scope).single { it.task.taskId == taskId }
            assertNotNull(downloaded.receiptAt)
            assertEquals("placeholder", downloaded.task.contentStatus)
            assertEquals("pain_index", downloaded.task.schema!!.questions.single().id)

            // Preserve the real field-level error contract; never turn it into an account deletion.
            try {
                real.submit(credential.patientToken, taskId, JSONObject().put("requestId", UUID.randomUUID())
                    .put("templateVersion", downloaded.task.version).put("answers", JSONObject().put("pain_index", 11)))
                fail("Out-of-range answer must be rejected")
            } catch (error: ApiHttpException) {
                assertEquals(400, error.httpStatus)
                assertEquals("QUESTIONNAIRE_ANSWERS_INVALID", error.errorCode)
                assertTrue(JSONArray(error.fieldErrors!!).length() > 0)
                assertFalse(store.rejected(scope))
            }
            coordinator.saveDraft(scope, taskId, JSONObject().put("pain_index", 4).toString()) { true }
            coordinator.submit(scope, credential, taskId) { true }
            val pending = store.read(scope).single { it.task.taskId == taskId }
            assertNotNull(pending.pendingBody)
            assertNull(pending.completedAt)
            assertEquals(firstSubmission, pending.pendingBody)

            // Re-open the serialized cache and replay exactly the same request UUID/content.
            val reopened = FileRoutineStore(file)
            val retriedBodies = mutableListOf<String>()
            val replayApi = object : RoutineQuestionnaireApi by real {
                override fun atOrigin(origin: String): RoutineQuestionnaireApi { require(origin == scope.origin); return this }
                override suspend fun submit(token: String, taskId: Long, body: JSONObject): JSONObject {
                    retriedBodies += body.toString()
                    return real.submit(token, taskId, body)
                }
            }
            val recovered = RoutineQuestionnaireCoordinator(reopened, replayApi) { System.currentTimeMillis() + 3_600_000 }
                .refresh(scope, credential) { true }
            assertTrue(recovered.message, recovered.available)
            assertEquals(listOf(pending.pendingBody), retriedBodies)
            val completed = FileRoutineStore(file).read(scope).single { it.task.taskId == taskId }
            assertNull(completed.pendingBody)
            assertEquals(pending.pendingBody, completed.confirmedSubmissionBody)
            assertEquals(serverReceipt!!.getLong("recordId"), completed.recordId)
            assertEquals(serverReceipt!!.getString("completedAt"), completed.completedAt)
            assertEquals("completed", completed.submissionStatus)
            assertEquals(downloaded.receiptAt, completed.receiptAt)
            val rows = doctor(fixture, "/api/doctor/patients/${credential.patientId}/questionnaires?size=100").getJSONArray("items").objects()
            assertEquals(1, rows.count { it.getLong("id") == completed.recordId })
        } finally { SpineFlowServerConfig.baseUrlOverride = previousOrigin; file.delete() }
    }

    @Test fun routineScopeSeparatesCredentialGenerationsAndHospitals() {
        val credential = PatientServerCredential("patient", "token")
        val binding = BindingScope("http://127.0.0.1:1", "hospital-a", "a", "A", "patient", "account", 1)
        val first = RoutineScope.of("account", credential, binding.origin, binding)
        assertNotEquals(first.fingerprint, RoutineScope.of("account", credential, binding.origin, binding.copy(credentialGeneration = 2)).fingerprint)
        assertNotEquals(first.fingerprint, RoutineScope.of("account", credential, binding.origin, binding.copy(serviceId = "hospital-b", hospitalKey = "b")).fingerprint)
        assertEquals(binding.origin, first.origin)
    }

    private fun fixture(): JSONObject {
        val path = System.getenv("SPINEFLOW_BRIDGE_FIXTURE")
        assumeTrue("Requires disposable server fixture", !path.isNullOrBlank())
        return JSONObject(File(path!!).readText()).also {
            require(it.getString("baseUrl").startsWith("http://127.0.0.1:"))
            require(it.getJSONObject("healthCase").getBoolean("syntheticTransportFixture"))
        }
    }
    private fun credential(item: JSONObject) = PatientServerCredential(item.getJSONObject("credential").getString("patientId"),
        item.getJSONObject("credential").getString("patientToken"))

    private suspend fun assignFreshTask(fixture: JSONObject, item: JSONObject): Long {
        val path = "/api/doctor/patients/${item.getString("patientId")}/questionnaire-plan"
        val previous = doctor(fixture, path).optJSONObject("plan")
        val currentTemplate = previous?.getJSONArray("items")?.getJSONObject(0)?.getJSONObject("template")?.getLong("templateId")
        val template = doctor(fixture, "/api/doctor/questionnaire-templates").getJSONArray("items").objects()
            .first { it.getLong("templateId") != currentTemplate && it.getString("contentStatus") == "placeholder" }
        val date = LocalDate.now(ZoneId.of("Asia/Shanghai"))
        doctor(fixture, path, "PUT", JSONObject().put("requestId", UUID.randomUUID())
            .put("expectedPlanId", previous?.getLong("planId") ?: JSONObject.NULL)
            .put("items", JSONArray().put(JSONObject().put("templateId", template.getLong("templateId"))
                .put("frequency", "monthly").put("dayOfMonth", date.dayOfMonth).put("startDate", date.toString()).put("endDate", JSONObject.NULL))))
        val listed = FixturePatientApi(fixture.getString("baseUrl")).open(credential(item).patientToken)
        return listed.getJSONArray("items").objects().single().getLong("taskId")
    }
    private fun doctor(fixture: JSONObject, path: String, method: String = "GET", body: JSONObject? = null): JSONObject =
        JSONObject(http(fixture.getString("baseUrl"), path, method, fixture.getJSONObject("doctor").getString("token"), body)).getJSONObject("data")

    private fun http(origin: String, path: String, method: String = "GET", token: String? = null, body: JSONObject? = null): String {
        require(origin.startsWith("http://127.0.0.1:"))
        val connection = URL(origin + path).openConnection() as HttpURLConnection
        try {
            connection.requestMethod = method
            connection.instanceFollowRedirects = false
            connection.connectTimeout = 10_000
            connection.readTimeout = 15_000
            token?.let { connection.setRequestProperty("Authorization", "Bearer $it") }
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            }
            val status = connection.responseCode
            check(status in 200..299) { "Disposable API $path returned HTTP $status" }
            return connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
        } finally { connection.disconnect() }
    }
    private fun JSONArray.objects() = (0 until length()).map(::getJSONObject)

    /** Uses production row serialization; SQLite/encryption on-device recovery needs an instrumented test. */
    private class FileRoutineStore(private val file: File) : RoutineQuestionnaireStorage {
        private var bound: RoutineScope? = null
        private var isRejected = false
        private val rows = linkedMapOf<Long, LocalRoutineTask>()
        init {
            if (file.length() > 0) {
                val json = JSONObject(file.readText())
                val s = json.getJSONObject("scope")
                bound = RoutineScope(s.getString("accountId"), s.getString("patientId"), s.getString("fingerprint"), s.getString("origin"))
                isRejected = json.getBoolean("rejected")
                val values = json.getJSONArray("rows")
                for (index in 0 until values.length()) LocalRoutineTask.decode(values.getString(index)).let { rows[it.task.taskId] = it }
            }
        }
        override fun bind(scope: RoutineScope) { if (bound != scope) { bound = scope; rows.clear(); isRejected = false; persist() } }
        override fun matches(scope: RoutineScope) = bound == scope
        override fun rejected(scope: RoutineScope) = !matches(scope) || isRejected
        override fun reject(scope: RoutineScope) { if (matches(scope)) { isRejected = true; persist() } }
        override fun read(scope: RoutineScope) = if (matches(scope)) rows.values.toList() else emptyList()
        override fun put(scope: RoutineScope, rows: List<LocalRoutineTask>): Boolean {
            if (rejected(scope)) return false
            rows.forEach { this.rows[it.task.taskId] = it }
            persist()
            return true
        }
        private fun persist() {
            val scope = bound ?: return
            file.writeText(JSONObject().put("scope", JSONObject().put("accountId", scope.accountId).put("patientId", scope.patientId)
                .put("fingerprint", scope.fingerprint).put("origin", scope.origin)).put("rejected", isRejected)
                .put("rows", JSONArray(rows.values.map { it.encode() })).toString())
        }
    }
}
