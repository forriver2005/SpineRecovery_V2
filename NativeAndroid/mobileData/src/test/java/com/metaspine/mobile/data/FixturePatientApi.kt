package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import java.net.URI

/**
 * Business-contract integration adapter for disposable loopback hospitals only.
 * Reuses production serialization/parsers and explicit transport injection; this is not an
 * Android LAN-discovery test and is never compiled into an application build.
 */
internal class FixturePatientApi(val origin: String) : RoutineQuestionnaireApi {
    init {
        val uri = URI(origin)
        require(uri.scheme == "http" && uri.host == "127.0.0.1" && uri.port in 1..65535 &&
            uri.rawUserInfo == null && uri.rawQuery == null && uri.rawFragment == null &&
            uri.rawPath.isNullOrEmpty() && origin == "http://127.0.0.1:${uri.port}") {
            "Fixture API requires a literal loopback HTTP origin with an explicit port"
        }
    }

    val transport = PatientHttpTransport(origin = { origin }, openConnection = ::openLoopbackTestConnection)

    suspend fun identity(): Result<ServiceIdentity> = patientRequestResult {
        val response = transport.request(ServiceIdentityClient.PATH)
        response.requireSuccess("Fixture identity request failed")
        ServiceIdentityClient.parse(response.text)
    }

    suspend fun redeem(body: JSONObject): Result<ActivationRedeemResult> = patientRequestResult {
        val response = transport.request(PatientActivationClient.REDEEM_PATH, "POST", body = body.toString())
        response.requireSuccess("Fixture activation request failed")
        PatientActivationClient.parseRedeem(PatientQuestionnaireClient.decodeEnvelope(response.bytes))
    }

    suspend fun directive(token: String): Result<JSONObject> = directiveRequest(
        "GET", PatientDirectiveClient.DIRECTIVE_PATH, token, null,
        negotiationHeaders = PatientHttpTransport.DIRECTIVE_VERSION_HEADERS,
        readErrorStatuses = setOf(400))

    suspend fun stopReceipt(token: String, orderId: Long): Result<PatientDirectiveClient.ReceiptInfo> =
        directiveRequest("POST", PatientDirectiveClient.RECEIPT_PATH, token,
            JSONObject().put("orderId", orderId))
            .mapCatching { PatientDirectiveClient.parseStopReceipt(it, orderId) }

    suspend fun prescriptionReceipt(token: String, orderId: Long, phase: String,
        sha: String): Result<PatientDirectiveClient.ReceiptInfo> =
        directiveRequest("POST", PatientDirectiveClient.RECEIPT_PATH, token,
            JSONObject().put("orderId", orderId).put("directiveVersion", PatientDirectiveClient.DIRECTIVE_VERSION)
                .put("phase", phase).put("resourceManifestSha256", sha),
            readErrorStatuses = setOf(409, 503))
            .mapCatching { PatientDirectiveClient.parsePrescriptionReceipt(it, orderId, phase, sha) }

    private suspend fun directiveRequest(method: String, path: String, token: String, body: JSONObject?,
        negotiationHeaders: Map<String, String> = emptyMap(),
        readErrorStatuses: Set<Int> = emptySet()): Result<JSONObject> = patientRequestResult {
        val response = transport.request(path, method, token, body?.toString(),
            readErrorStatuses = readErrorStatuses, negotiationHeaders = negotiationHeaders)
        if (response.status !in 200..299 && response.status in readErrorStatuses) {
            val code = runCatching { PatientDirectiveClient.errorCodeOf(JSONObject(response.text)) }.getOrNull()
            if (code != null) throw DirectiveProtocolException(code)
        }
        response.requireSuccess("Fixture directive request failed")
        PatientQuestionnaireClient.decodeEnvelope(response.bytes)
    }

    suspend fun sync(body: JSONObject, token: String?): Result<Unit> = patientRequestResult {
        val response = transport.request("/api/patient/sync", "POST", token, body.toString())
        response.requireSuccess("Fixture patient sync failed")
        PatientSyncClient.verifyResponse(JSONObject(response.text), body.getString("userId"))
    }

    suspend fun uploadV2(sessions: JSONArray, expected: List<ResultReceipt>, token: String?): Result<List<ResultReceipt>> =
        patientRequestResult {
            require(sessions.length() > 0 && sessions.length() == expected.size)
            val response = transport.request("/api/training/upload", "POST", token,
                TrainingSyncClient.serializeFrozenSessions(sessions), readErrorStatuses = setOf(400, 409))
            response.requireSuccess("Fixture training upload failed")
            TrainingSyncClient.verifyUploadResponse(JSONObject(response.text), expected)
        }

    override fun atOrigin(origin: String): FixturePatientApi = FixturePatientApi(origin)
    override suspend fun open(token: String): JSONObject = questionnaire(token, "GET", "$TASKS?scope=open")
    override suspend fun detail(token: String, taskId: Long): JSONObject = questionnaire(token, "GET", "$TASKS/$taskId")
    override suspend fun receipt(token: String, taskId: Long, requestId: String): JSONObject =
        questionnaire(token, "POST", "$TASKS/$taskId/receipts", JSONObject().put("requestId", requestId))
    override suspend fun submit(token: String, taskId: Long, body: JSONObject): JSONObject =
        questionnaire(token, "POST", "$TASKS/$taskId/submissions", body)

    private suspend fun questionnaire(token: String, method: String, path: String, body: JSONObject? = null): JSONObject {
        val response = transport.request(path, method, token, body?.toString(),
            readErrorStatuses = setOf(400, 409, 410, 413, 422))
        if (response.status !in 200..299) {
            val details = runCatching { JSONObject(response.text).optJSONObject("details") }.getOrNull()
            throw ApiHttpException(response.status, "Fixture questionnaire request failed", response.retryAfterSeconds,
                (details?.opt("code") as? String)?.takeIf(String::isNotBlank),
                details?.optJSONArray("errors")?.toString())
        }
        return PatientQuestionnaireClient.decodeEnvelope(response.bytes)
    }

    companion object { private const val TASKS = "/api/patient/questionnaire-tasks" }
}
