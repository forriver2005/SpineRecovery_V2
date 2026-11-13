package com.metaspine.mobile.data

import org.json.JSONObject

/**
 * M7.1 §8.3：训练指令下行客户端（GET 快照 + 回执 POST）。
 *
 * 沿用现有 `PatientSyncClient` / `TrainingSyncClient` 的约定：
 * base URL 取运行期 [SpineFlowServerConfig.BASE_URL]，`Authorization: Bearer <PAT>`，
 * connect 10 秒 / read 15 秒，JSON UTF-8，不引入 Retrofit/OkHttp。
 */
object PatientDirectiveClient {
    const val DIRECTIVE_PATH = "/api/patient/training-directive"
    const val RECEIPT_PATH = "/api/patient/training-directive/receipts"


    data class ReceiptInfo(
        val orderId: Long,
        val receivedAt: String,
        val alreadyRecorded: Boolean
    )

    /** GET 只返回 `data`：状态语义全在 JSON 内，HTTP 层只区分 2xx / 非 2xx。 */
    suspend fun fetch(patientToken: String): Result<JSONObject> =
        request("GET", DIRECTIVE_PATH, patientToken, null)

    suspend fun postReceipt(patientToken: String, orderId: Long): Result<ReceiptInfo> =
        request("POST", RECEIPT_PATH, patientToken, JSONObject().put("orderId", orderId))
            .mapCatching(::parseReceipt)

    internal fun parseReceipt(data: JSONObject): ReceiptInfo {
        val orderId = data.optLong("orderId", 0L)
        require(orderId > 0L) { "回执响应缺少有效指令编号" }
        return ReceiptInfo(
            orderId = orderId,
            receivedAt = data.optString("receivedAt"),
            alreadyRecorded = false
        )
    }

    suspend fun postV2Receipt(token: String, order: AdmissionPrescriptionV2, phase: String): JSONObject =
        request("POST", RECEIPT_PATH, token, JSONObject()
            .put("orderId", order.orderId)
            .put("directiveVersion", 2)
            .put("phase", phase)
            .put("resourceManifestSha256", order.resourceManifestSha256)).getOrThrow().also { receipt ->
            require(receipt.optLong("orderId") == order.orderId &&
                receipt.optInt("directiveVersion") == 2 && receipt.optString("phase") == phase &&
                receipt.optString("resourceManifestSha256") == order.resourceManifestSha256 &&
                receipt.optString("receivedAt").isNotBlank() &&
                (phase != "assets_ready" || receipt.optString("assetsReadyAt").isNotBlank())) {
                "处方回执与本次处方不一致"
            }
        }

    private suspend fun request(
        method: String,
        path: String,
        patientToken: String,
        body: JSONObject?
    ): Result<JSONObject> = patientRequestResult {
        val response = PatientHttpTransport.shared.request(path, method, patientToken, body?.toString(), directiveVersion = method == "GET")
        response.requireSuccess("训练指令请求失败，HTTP "+response.status)
        val json = runCatching { JSONObject(response.text) }.getOrNull()
        if (json == null || json.optInt("code", -1) != 0) {
            throw DirectiveProtocolException("服务器返回的训练指令无法解析")
        }
        json.getJSONObject("data")
    }
}
