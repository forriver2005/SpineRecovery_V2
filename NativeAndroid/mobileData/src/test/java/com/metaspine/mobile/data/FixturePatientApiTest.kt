package com.metaspine.mobile.data

import com.sun.net.httpserver.HttpServer
import kotlinx.coroutines.runBlocking
import org.json.JSONArray
import org.json.JSONObject
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import java.net.InetSocketAddress
import java.util.concurrent.CopyOnWriteArrayList

/** Executes the opt-in fixture adapter without requiring an external hospital fixture. */
class FixturePatientApiTest {
    private data class Request(val method: String, val path: String, val body: String,
        val authorization: String?, val directiveVersion: String?)
    private lateinit var server: HttpServer
    private lateinit var api: FixturePatientApi
    private val received = CopyOnWriteArrayList<Request>()
    private var reply: (Request) -> Pair<Int, JSONObject> = { 500 to JSONObject() }

    @Before fun start() {
        server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.createContext("/") { exchange ->
            val request = Request(exchange.requestMethod, exchange.requestURI.path,
                exchange.requestBody.use { it.readBytes().toString(Charsets.UTF_8) },
                exchange.requestHeaders.getFirst("Authorization"),
                exchange.requestHeaders.getFirst(PatientHttpTransport.DIRECTIVE_VERSION_HEADER))
            received += request
            val (status, json) = reply(request)
            val bytes = json.toString().toByteArray(Charsets.UTF_8)
            exchange.sendResponseHeaders(status, bytes.size.toLong())
            exchange.responseBody.use { it.write(bytes) }
            exchange.close()
        }
        server.start()
        api = FixturePatientApi("http://127.0.0.1:${server.address.port}")
    }

    @After fun stop() { server.stop(0) }

    private fun envelope(data: JSONObject) = JSONObject().put("code", 0).put("data", data)
    private fun resource(name: String): JSONObject = javaClass.getResourceAsStream("/fixtures/$name")!!
        .bufferedReader(Charsets.UTF_8).use { JSONObject(it.readText()) }

    @Test fun fixtureTransportIsExplicitLoopbackOnlyAndIndependentOfApplicationConfiguration() = runBlocking {
        for (origin in listOf("https://api.metaspine.cn", "http://192.168.1.2:3001",
            "http://localhost:3001", "http://127.0.0.1:3001/path", "http://user@127.0.0.1:3001")) {
            assertThrows(IllegalArgumentException::class.java) { FixturePatientApi(origin) }
        }
        val identity = envelope(JSONObject().put("role", "hospital").put("serviceId", "fixture-a")
            .put("hospitalKey", "fixture-a").put("hospitalName", "Synthetic hospital")
            .put("audience", "spineflow:medical:fixture-a").put("contractVersion", 1))
        reply = { 200 to identity }
        val previous = SpineFlowServerConfig.baseUrlOverride
        try {
            SpineFlowServerConfig.baseUrlOverride = "https://not-an-allowed-origin.example"
            assertEquals("fixture-a", ServiceIdentityClient.requireHospital(api.identity().getOrThrow()).hospitalKey)
            assertEquals(ServiceIdentityClient.PATH, received.single().path)
            assertNull(received.single().authorization)
        } finally { SpineFlowServerConfig.baseUrlOverride = previous }
    }

    @Test fun activationDirectiveAndReceiptsUseRealHttpAndProductionParsers() = runBlocking {
        val activation = resource("real-a02.json")
        val directive = resource("contract.json").getJSONArray("cases").getJSONObject(0).getJSONObject("directive")
        val sha = directive.getJSONObject("activePrescription").getString("resourceManifestSha256")
        val receivedAt = "2026-10-31 12:00:00"
        reply = { request ->
            when (request.path) {
                PatientActivationClient.REDEEM_PATH -> 201 to activation
                PatientDirectiveClient.DIRECTIVE_PATH -> 200 to envelope(directive)
                PatientDirectiveClient.RECEIPT_PATH -> {
                    val sent = JSONObject(request.body)
                    val data = JSONObject().put("orderId", sent.getLong("orderId"))
                        .put("receivedAt", receivedAt).put("created", true)
                    if (sent.has("phase")) data.put("directiveVersion", 2)
                        .put("phase", sent.getString("phase"))
                        .put("resourceManifestSha256", sent.getString("resourceManifestSha256"))
                        .put("assetsReadyAt", JSONObject.NULL)
                    201 to envelope(data)
                }
                else -> 404 to JSONObject()
            }
        }
        val body = JSONObject().put("requestId", "fixture-request")
        val activated = api.redeem(body).getOrThrow()
        assertEquals(PatientActivationClient.parseRedeem(activation.getJSONObject("data")), activated)
        assertEquals(body.toString(), received.first().body)
        val fetched = api.directive("fixture-pat").getOrThrow()
        assertEquals(directive.toString(), fetched.toString())
        assertEquals("2", received[1].directiveVersion)
        assertEquals("Bearer fixture-pat", received[1].authorization)
        assertEquals(receivedAt, api.stopReceipt("fixture-pat", 17).getOrThrow().receivedAt)
        val receipt = api.prescriptionReceipt("fixture-pat", 19,
            PatientDirectiveClient.PHASE_SAVED, sha).getOrThrow()
        assertEquals(19L, receipt.orderId)
        assertEquals(sha, receipt.resourceManifestSha256)
        assertEquals(setOf("orderId", "directiveVersion", "phase", "resourceManifestSha256"),
            JSONObject(received.last().body).keys().asSequence().toSet())
        assertEquals(listOf("POST", "GET", "POST", "POST"), received.map { it.method })
    }

    @Test fun syncAndTrainingStillRejectMismatchedPatientOrReceiptOverRealHttp() = runBlocking {
        val expected = ResultReceipt("fixture-session", 3, "a".repeat(64))
        val counts = JSONObject().put("profile", 1).put("questionnaires", 0).put("painRecords", 0).put("lifecycle", 0)
        val syncResponse = envelope(JSONObject().put("userId", "fixture-patient").put("synced", counts))
        val trainingResponse = envelope(JSONObject().put("uploaded", 1).put("skipped", 0)
            .put("resultReceipts", JSONArray().put(expected.toJson())))
        reply = { request -> 200 to if (request.path == "/api/patient/sync") syncResponse else trainingResponse }
        val body = JSONObject().put("userId", "fixture-patient")
        assertTrue(api.sync(body, "fixture-pat").isSuccess)
        syncResponse.getJSONObject("data").put("userId", "another-patient")
        assertTrue(api.sync(body, "fixture-pat").isFailure)
        val sessions = JSONArray().put(JSONObject().put("sessionId", "fixture-session"))
        assertEquals(listOf(expected), api.uploadV2(sessions, listOf(expected), "fixture-pat").getOrThrow())
        assertEquals(TrainingSyncClient.serializeFrozenSessions(sessions), received.last().body)
        trainingResponse.getJSONObject("data").getJSONArray("resultReceipts").getJSONObject(0)
            .put("resultContentSha256", "b".repeat(64))
        assertTrue(api.uploadV2(sessions, listOf(expected), "fixture-pat").isFailure)
        assertTrue(received.all { it.authorization == "Bearer fixture-pat" })
    }

    @Test fun questionnaireAdapterPreservesFieldErrorsAndIdempotencyBody() = runBlocking {
        val body = JSONObject().put("requestId", "fixture-submission").put("templateVersion", 1)
            .put("answers", JSONObject().put("pain_index", 11))
        reply = { 400 to JSONObject().put("code", 400).put("details", JSONObject()
            .put("code", "QUESTIONNAIRE_ANSWERS_INVALID")
            .put("errors", JSONArray().put(JSONObject().put("field", "pain_index")))) }
        val error = runCatching { api.submit("fixture-pat", 7, body) }.exceptionOrNull() as ApiHttpException
        assertEquals(400, error.httpStatus)
        assertEquals("QUESTIONNAIRE_ANSWERS_INVALID", error.errorCode)
        assertEquals(1, JSONArray(error.fieldErrors!!).length())
        assertEquals(body.toString(), received.single().body)
        assertEquals("/api/patient/questionnaire-tasks/7/submissions", received.single().path)
        reply = { 200 to envelope(JSONObject().put("recordId", 9).put("completedAt", "2026-10-31 12:00:00")) }
        assertEquals(9L, api.submit("fixture-pat", 7, body).getLong("recordId"))
        assertEquals(received.first().body, received.last().body)
    }
}
