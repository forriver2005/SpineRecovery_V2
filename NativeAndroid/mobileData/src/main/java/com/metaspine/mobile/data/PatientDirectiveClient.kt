package com.metaspine.mobile.data

import org.json.JSONObject

/**
 * M7.1 §8.3 / P4：训练指令下行客户端（D01 快照 + D02 回执）。
 *
 * D01 固定携带 `X-SpineFlow-Directive-Version: 2`：服务端缺头或非 2 返回
 * 400 `DIRECTIVE_VERSION_UNSUPPORTED`，不存在降级路径。
 */
object PatientDirectiveClient {
    const val DIRECTIVE_PATH = "/api/patient/training-directive"
    const val RECEIPT_PATH = "/api/patient/training-directive/receipts"
    const val RESOURCE_STATUS_PATH = "/api/patient/resource-status"

    /** 处方回执阶段（D02 四字段之一）；停训仍用简式 `{orderId}`。 */
    const val PHASE_SAVED = "directive_saved"
    const val PHASE_ASSETS_READY = "assets_ready"

    /** D01 协商版本；与处方内 `prescriptionContractVersion` 分别校验。 */
    const val DIRECTIVE_VERSION = 2

    data class ReceiptInfo(
        val orderId: Long,
        val phase: String,
        val resourceManifestSha256: String,
        val receivedAt: String,
        val assetsReadyAt: String?,
        val alreadyRecorded: Boolean
    )

    /** GET 只返回 `data`：状态语义全在 JSON 内，HTTP 层只区分 2xx / 非 2xx。 */
    suspend fun fetch(patientToken: String, origin: String = SpineFlowServerConfig.BASE_URL): Result<JSONObject> =
        request("GET", DIRECTIVE_PATH, patientToken, null,
            negotiationHeaders = PatientHttpTransport.DIRECTIVE_VERSION_HEADERS,
            readErrorStatuses = setOf(400), origin = origin)

    suspend fun resourceStatus(patientToken: String, origin: String = SpineFlowServerConfig.BASE_URL): Result<PrescriptionResourceStatus> =
        request("GET", RESOURCE_STATUS_PATH, patientToken, null, readErrorStatuses = setOf(409, 503), origin = origin)
            .mapCatching(PrescriptionResourceStatus::fromJson)

    /** 停训简式回执：首次 201、重复 200，服务器保留首次时间。 */
    suspend fun postStopReceipt(patientToken: String, orderId: Long, origin: String = SpineFlowServerConfig.BASE_URL): Result<ReceiptInfo> =
        request("POST", RECEIPT_PATH, patientToken, JSONObject().put("orderId", orderId), origin = origin)
            .mapCatching { parseStopReceipt(it, orderId) }

    /** 处方回执：严格四字段（P4 §4）；缺字段或多字段服务端一律 400。 */
    suspend fun postPrescriptionReceipt(
        patientToken: String,
        orderId: Long,
        phase: String,
        resourceManifestSha256: String,
        origin: String = SpineFlowServerConfig.BASE_URL
    ): Result<ReceiptInfo> {
        require(phase == PHASE_SAVED || phase == PHASE_ASSETS_READY) { "处方回执阶段不合法: $phase" }
        require(Regex("^[a-f0-9]{64}$").matches(resourceManifestSha256)) { "清单摘要格式不合法" }
        val body = JSONObject()
            .put("orderId", orderId)
            .put("directiveVersion", DIRECTIVE_VERSION)
            .put("phase", phase)
            .put("resourceManifestSha256", resourceManifestSha256)
        return request("POST", RECEIPT_PATH, patientToken, body, readErrorStatuses = setOf(409, 503), origin = origin)
            .mapCatching { parsePrescriptionReceipt(it, orderId, phase, resourceManifestSha256) }
    }

    internal fun parseStopReceipt(data: JSONObject, orderId: Long): ReceiptInfo {
        val respondedOrderId = data.optLong("orderId", 0L)
        require(respondedOrderId == orderId) { "回执响应缺少有效指令编号" }
        val receivedAt = data.opt("receivedAt") as? String
        require(!receivedAt.isNullOrBlank()) { "回执响应缺少保存时间" }
        return ReceiptInfo(
            orderId = respondedOrderId,
            phase = "",
            resourceManifestSha256 = "",
            receivedAt = receivedAt,
            assetsReadyAt = null,
            alreadyRecorded = data.optBoolean("created", false).not()
        )
    }

    /**
     * P4 §4.2：核对 scope 之外的订单、阶段与清单摘要；服务器可能返回已有 ready 时间，
     * 因此首次 saved 的 `assetsReadyAt` 只要求“可空”，不强制为空。
     */
    internal fun parsePrescriptionReceipt(
        data: JSONObject,
        orderId: Long,
        phase: String,
        resourceManifestSha256: String
    ): ReceiptInfo {
        require(data.optLong("orderId", 0L) == orderId) { "回执响应的指令编号不一致" }
        require(data.optInt("directiveVersion", -1) == DIRECTIVE_VERSION) { "回执响应的指令版本不一致" }
        require(data.optString("phase") == phase) { "回执响应的阶段不一致" }
        require(data.optString("resourceManifestSha256") == resourceManifestSha256) {
            "回执响应的清单摘要不一致"
        }
        val receivedAt = data.opt("receivedAt") as? String
        require(!receivedAt.isNullOrBlank()) { "回执响应缺少保存时间" }
        val assetsReadyAt = (data.opt("assetsReadyAt") as? String)?.takeIf(String::isNotBlank)
        require(phase != PHASE_ASSETS_READY || assetsReadyAt != null) { "资源就绪回执缺少就绪时间" }
        return ReceiptInfo(
            orderId = orderId,
            phase = phase,
            resourceManifestSha256 = resourceManifestSha256,
            receivedAt = receivedAt,
            assetsReadyAt = assetsReadyAt,
            alreadyRecorded = false
        )
    }

    private suspend fun request(
        method: String,
        path: String,
        patientToken: String,
        body: JSONObject?,
        negotiationHeaders: Map<String, String> = emptyMap(),
        readErrorStatuses: Set<Int> = emptySet(),
        origin: String
    ): Result<JSONObject> = patientRequestResult {
        val response = PatientHttpTransport(origin = { origin }).request(
            path, method, patientToken, body?.toString(),
            readErrorStatuses = readErrorStatuses,
            negotiationHeaders = negotiationHeaders
        )
        // 只有显式要求读取错误正文的状态才解析 JSON；401/403 等仍保持 HTTP 语义，
        // 否则协调器会把凭据失效误当成普通协议错误。
        if (response.status !in 200..299) {
            if (response.status in readErrorStatuses) {
                val code = runCatching { errorCodeOf(JSONObject(response.text)) }.getOrNull()
                if (code != null) throw DirectiveProtocolException(code)
            }
            response.requireSuccess("训练指令请求失败，HTTP " + response.status)
        }
        val json = runCatching { JSONObject(response.text) }.getOrNull()
        if (json == null || json.optInt("code", -1) != 0) {
            throw DirectiveProtocolException(
                errorCodeOf(json) ?: "服务器返回的训练指令无法解析"
            )
        }
        json.getJSONObject("data")
    }

    /** `details.code` 是唯一细码位置；缺失时退回消息文本。 */
    internal fun errorCodeOf(json: JSONObject?): String? =
        json?.optJSONObject("details")?.optString("code")?.takeIf(String::isNotBlank)
}
