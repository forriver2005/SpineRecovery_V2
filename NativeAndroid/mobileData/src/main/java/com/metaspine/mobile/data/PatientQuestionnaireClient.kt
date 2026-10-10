package com.metaspine.mobile.data

import org.json.JSONObject

interface RoutineQuestionnaireApi {
    suspend fun open(token: String): JSONObject
    suspend fun detail(token: String, taskId: Long): JSONObject
    suspend fun receipt(token: String, taskId: Long, requestId: String): JSONObject
    suspend fun submit(token: String, taskId: Long, body: JSONObject): JSONObject
}

object PatientQuestionnaireClient : RoutineQuestionnaireApi {
    private const val PATH = "/api/patient/questionnaire-tasks"
    override suspend fun open(token: String) = request(token, "GET", "$PATH?scope=open")
    override suspend fun detail(token: String, taskId: Long) = request(token, "GET", "$PATH/$taskId")
    override suspend fun receipt(token: String, taskId: Long, requestId: String) =
        request(token, "POST", "$PATH/$taskId/receipts", JSONObject().put("requestId", requestId))
    override suspend fun submit(token: String, taskId: Long, body: JSONObject) =
        request(token, "POST", "$PATH/$taskId/submissions", body)

    private suspend fun request(token: String, method: String, path: String, body: JSONObject? = null): JSONObject {
        val response = PatientHttpTransport.shared.request(path, method, token, body?.toString())
        response.requireSuccess("问卷请求失败")
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
