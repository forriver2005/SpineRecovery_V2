package com.metaspine.mobile.data

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import org.json.JSONArray
import org.json.JSONObject

/**
 * 康复训练数据上传客户端。
 *
 * 仅使用 Android SDK 自带的 PatientHttpTransport 与 org.json，
 * 不引入任何第三方网络依赖，与项目现有序列化风格一致。
 */
object TrainingSyncClient {

    private const val UPLOAD_PATH = "/api/training/upload"

    /** 当前是否有可用网络（需要 ACCESS_NETWORK_STATE 权限）。 */
    fun isOnline(context: Context): Boolean {
        val cm = context.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
            ?: return false
        val network = cm.activeNetwork ?: return false
        val caps = cm.getNetworkCapabilities(network) ?: return false
        return caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    }

    /**
     * P5.7：V2 结果上传。正文是冻结后的原始快照，不再拼 `{sessions:[...]}`，
     * 也不再补写 preTrainingEventId（它已在冻结前进入快照）。
     *
     * @param expected 本批冻结结果的逐条回执（scope 内的当前修订）。
     * @return 与 [expected] 一一对应的服务器回执，顺序与数量都必须严丝合缝。
     */
    suspend fun uploadV2(
        records: List<ResultRecord>,
        expected: List<ResultReceipt>,
        patientToken: String?,
    ): Result<List<ResultReceipt>> {
        if (records.size != expected.size) {
            return Result.failure(IllegalArgumentException("冻结记录与期望回执数量不一致"))
        }
        if (records.isEmpty()) {
            return Result.failure(IllegalArgumentException("没有可上传的 V2 训练结果"))
        }
        return uploadV2Sessions(JSONArray(records.map { JSONObject(it.body) }), expected, patientToken)
    }

    /**
     * P5.7：直接发送队列中已冻结的快照数组。
     *
     * 队列元素只存正文，不存 [ResultRecord]——重发时绝不重新序列化，
     * 否则键序/数字格式漂移会让服务器把同一份结果算成新内容。
     */
    suspend fun uploadV2Sessions(
        sessions: JSONArray,
        expected: List<ResultReceipt>,
        patientToken: String?,
        origin: String = SpineFlowServerConfig.BASE_URL,
    ): Result<List<ResultReceipt>> = patientRequestResult {
        require(sessions.length() > 0) { "没有可上传的 V2 训练结果" }
        require(sessions.length() == expected.size) {
            "冻结快照与期望回执数量不一致: ${sessions.length()}/${expected.size}"
        }
        val body = serializeFrozenSessions(sessions)
        // 400/409 的正文必须读：V2 里 `PRE_TRAINING_EVENT_NOT_FOUND`（等练前事件确认后重发）
        // 与 `RESULT_REVISION_CONFLICT`（必须隔离）同为 409，只靠状态码会把两者混为一谈。
        val response = PatientHttpTransport(origin = { origin }).request(
            UPLOAD_PATH, "POST", patientToken, body,
            readErrorStatuses = setOf(400, 409)
        )
        response.requireSuccess("上传失败，HTTP " + response.status)
        verifyUploadResponse(JSONObject(response.text), expected)
    }

    internal fun serializeFrozenSessions(sessions: JSONArray): String =
        CanonicalJson.canonicalize(JSONObject().put("sessions", sessions))

    /** 严格 T01 请求体：`protocolVersion` 由快照自身携带，不在这里补。 */
    internal fun buildV2UploadBody(records: List<ResultRecord>): JSONObject {
        require(records.isNotEmpty()) { "没有可上传的 V2 训练结果" }
        val sessions = JSONArray()
        for (record in records) {
            sessions.put(JSONObject(record.body))
        }
        return JSONObject().put("sessions", sessions)
    }

    /**
     * P5.5：逐条核对回执。以下任何情况都视为「未确认」，调用方必须保留任务：
     * 缺 `data`、缺少某条回执、回执数量多于本批、同一 (sessionId, revision) 出现两次、
     * 修订号或摘要与期望不一致、`skipped` 非 0。裸 HTTP 200 不构成确认。
     */
    internal fun verifyUploadResponse(
        json: JSONObject,
        expected: List<ResultReceipt>,
    ): List<ResultReceipt> {
        if (strictCount(json, "code") != 0) throw RuntimeException("上传响应状态无效: ${json.optString("message")}")
        val data = json.optJSONObject("data")
            ?: throw RuntimeException("上传响应缺少 data")
        val skipped = strictCount(data, "skipped")
        if (skipped != 0) throw RuntimeException("服务器跳过 $skipped 条记录")
        val array = data.optJSONArray("resultReceipts")
            ?: throw RuntimeException("上传响应缺少逐条回执")
        require(expected.isNotEmpty() && expected.distinctBy { it.sessionId to it.resultRevision }.size == expected.size)
        val byKey = expected.associateBy { it.sessionId to it.resultRevision }
        val seen = mutableSetOf<Pair<String, Long>>()
        val receipts = mutableListOf<ResultReceipt>()
        for (index in 0 until array.length()) {
            val receipt = array.optJSONObject(index)?.let(ResultReceipt::fromJson)
                ?: throw RuntimeException("回执格式不合法: 第 ${index + 1} 条")
            val key = receipt.sessionId to receipt.resultRevision
            if (!seen.add(key)) throw RuntimeException("回执重复: ${receipt.sessionId}#${receipt.resultRevision}")
            val want = byKey[key]
                ?: throw RuntimeException("回执与本批不符: ${receipt.sessionId}#${receipt.resultRevision}")
            if (receipt.resultContentSha256 != want.resultContentSha256) {
                throw RuntimeException("回执摘要与本批不一致: ${receipt.sessionId}#${receipt.resultRevision}")
            }
            receipts.add(receipt)
        }
        if (array.length() != expected.size) {
            throw RuntimeException("回执数量不一致: ${array.length()}/${expected.size}")
        }
        if (seen.size != byKey.size) throw RuntimeException("缺少部分逐条回执")
        val uploaded = strictCount(data, "uploaded")
        if (uploaded != expected.size) {
            throw RuntimeException("服务器只确认了 $uploaded/${expected.size} 条记录")
        }
        return receipts
    }

    private fun strictCount(json: JSONObject, key: String): Int {
        val value = json.opt(key)
        require(value is Number && value.toDouble().isFinite() && value.toDouble() >= 0 &&
            value.toDouble() <= Int.MAX_VALUE && value.toDouble() == value.toInt().toDouble()) { "回执整数无效: $key" }
        return value.toInt()
    }
}
