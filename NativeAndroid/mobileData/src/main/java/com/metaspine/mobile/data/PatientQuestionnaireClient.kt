package com.metaspine.mobile.data

import org.json.JSONObject

interface RoutineQuestionnaireApi {
    /** Production clients freeze the hospital before entering the IO dispatcher; test APIs may ignore it. */
    fun atOrigin(origin: String): RoutineQuestionnaireApi = this
    suspend fun open(token: String): JSONObject
    suspend fun detail(token: String, taskId: Long): JSONObject
    suspend fun receipt(token: String, taskId: Long, requestId: String): JSONObject
    suspend fun submit(token: String, taskId: Long, body: JSONObject): JSONObject
}

object PatientQuestionnaireClient : RoutineQuestionnaireApi {
    private const val PATH = "/api/patient/questionnaire-tasks"
    override suspend fun open(token: String) = atOrigin(SpineFlowServerConfig.BASE_URL).open(token)
    override suspend fun detail(token: String, taskId: Long) = atOrigin(SpineFlowServerConfig.BASE_URL).detail(token, taskId)
    override suspend fun receipt(token: String, taskId: Long, requestId: String) =
        atOrigin(SpineFlowServerConfig.BASE_URL).receipt(token, taskId, requestId)
    override suspend fun submit(token: String, taskId: Long, body: JSONObject) =
        atOrigin(SpineFlowServerConfig.BASE_URL).submit(token, taskId, body)

    override fun atOrigin(origin: String): RoutineQuestionnaireApi = object : RoutineQuestionnaireApi {
        override fun atOrigin(origin: String): RoutineQuestionnaireApi = PatientQuestionnaireClient.atOrigin(origin)
        override suspend fun open(token: String) = request(origin, token, "GET", "$PATH?scope=open")
        override suspend fun detail(token: String, taskId: Long) = request(origin, token, "GET", "$PATH/$taskId")
        override suspend fun receipt(token: String, taskId: Long, requestId: String) =
            request(origin, token, "POST", "$PATH/$taskId/receipts", JSONObject().put("requestId", requestId))
        override suspend fun submit(token: String, taskId: Long, body: JSONObject) =
            request(origin, token, "POST", "$PATH/$taskId/submissions", body)
    }

    private suspend fun request(origin: String, token: String, method: String, path: String, body: JSONObject? = null): JSONObject {
        val response = PatientHttpTransport(origin = { origin }).request(path, method, token, body?.toString(),
            readErrorStatuses = setOf(400, 409, 410, 413, 422))
        if (response.status !in 200..299) {
            val details = runCatching { JSONObject(response.text).optJSONObject("details") }.getOrNull()
            throw ApiHttpException(response.status, "问卷请求失败", response.retryAfterSeconds,
                (details?.opt("code") as? String)?.takeIf(String::isNotBlank),
                details?.optJSONArray("errors")?.toString())
        }
        return decodeEnvelope(response.bytes)
    }

    /** Bound nesting before JSONObject's recursive parser, including unknown or damaged fields. */
    internal fun decodeEnvelope(bytes: ByteArray): JSONObject {
        require(bytes.size <= 1024 * 1024)
        val raw = PatientHttpTransport.decodeUtf8(bytes)
        var depth = 0
        var inString = false
        var escaped = false
        raw.forEach { char ->
            if (inString) {
                if (escaped) escaped = false
                else if (char == '\\') escaped = true
                else if (char == '"') inString = false
            } else when (char) {
                '"' -> inString = true
                '{', '[' -> { depth++; require(depth <= 16) { "问卷响应嵌套过深" } }
                '}', ']' -> { depth--; require(depth >= 0) }
            }
        }
        require(!inString && depth == 0)
        val json = JSONObject(raw)
        require(json.get("code") == 0)
        return json.getJSONObject("data")
    }
}
