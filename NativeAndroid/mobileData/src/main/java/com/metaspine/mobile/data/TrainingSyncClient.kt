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

    /**
     * SF-06：客户端拆批阈值。服务器 express.json limit 为 200KB（超过即 413），
     * 这里按 UTF-8 字节数与条数双上限提前拆批，留出 HTTP 头/JSON 转义余量；
     * 单条记录超过字节上限视为「无法进入任何合规批次」的终态（由协调器隔离）。
     */
    const val MAX_UPLOAD_BATCH_UTF8_BYTES = 180 * 1024
    const val MAX_UPLOAD_BATCH_ITEMS = 40

    /** 一条可上传记录是否超过单批字节上限（无法拆细 → 协调器终态隔离，不无限重试）。 */
    fun isOversizedForBatch(record: TrainingHistoryRecord): Boolean =
        runCatching {
            sessionJsonOf(record).toString().toByteArray(Charsets.UTF_8).size >
                MAX_UPLOAD_BATCH_UTF8_BYTES
        }.getOrDefault(true)

    /** 拆批产物：合规批次列表 + 必须隔离的超大单条（协调器据此终态处理）。 */
    data class UploadChunkPlan(
        val chunks: List<List<TrainingHistoryRecord>>,
        val oversizedSessionIds: Set<String>,
    )

    /**
     * SF-06：把待上传历史按「单批 UTF-8 字节 ≤ MAX_UPLOAD_BATCH_UTF8_BYTES 且
     * 条数 ≤ MAX_UPLOAD_BATCH_ITEMS」拆成多个批次，供协调器逐批入队、逐批 ACK。
     * 保持 [buildUploadBody] 兼容（单批用例不变）；贪心装填，保证结果确定可测。
     */
    fun planUploadChunks(records: List<TrainingHistoryRecord>): UploadChunkPlan {
        val oversized = mutableSetOf<String>()
        val chunks = mutableListOf<List<TrainingHistoryRecord>>()
        var current = mutableListOf<TrainingHistoryRecord>()
        var currentBytes = 0
        var currentItems = 0
        for (record in records) {
            if (!isUploadable(record)) continue
            if (isOversizedForBatch(record)) {
                oversized.add(record.sessionId)
                continue
            }
            val sessionBytes = runCatching {
                sessionJsonOf(record).toString().toByteArray(Charsets.UTF_8).size
            }.getOrDefault(Int.MAX_VALUE)
            val wouldExceedBytes = currentBytes + sessionBytes > MAX_UPLOAD_BATCH_UTF8_BYTES
            val wouldExceedItems = currentItems + 1 > MAX_UPLOAD_BATCH_ITEMS
            if (current.isNotEmpty() && (wouldExceedBytes || wouldExceedItems)) {
                chunks.add(current.toList())
                current = mutableListOf()
                currentBytes = 0
                currentItems = 0
            }
            current.add(record)
            currentBytes += sessionBytes
            currentItems += 1
        }
        if (current.isNotEmpty()) chunks.add(current.toList())
        return UploadChunkPlan(chunks = chunks, oversizedSessionIds = oversized)
    }

    /** 当前是否有可用网络（需要 ACCESS_NETWORK_STATE 权限）。 */
    fun isOnline(context: Context): Boolean {
        val cm = context.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
            ?: return false
        val network = cm.activeNetwork ?: return false
        val caps = cm.getNetworkCapabilities(network) ?: return false
        return caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    }

    /**
     * 记录是否能被服务器 extractSession 接受：
     * resultJson 必须可解析，且 sessionId/userId（含 snake_case 兼容键）为非空字符串。
     * 规则与 server/routes/training.js 的 extractSession 保持一致，
     * 防止「客户端以为成功、服务器静默跳过」造成丢数据（评审 P4）。
     * Step 4 协调器据此把不可上传的记录标记为终态拒绝（不再重试），其余记录正常游标推进。
     */
    fun isUploadable(record: TrainingHistoryRecord): Boolean {
        val session = runCatching { JSONObject(record.resultJson) }.getOrNull() ?: return false
        return firstString(session, "sessionId", "session_id") != null &&
            firstString(session, "userId", "user_id") != null
    }

    private fun firstString(json: JSONObject, vararg keys: String): String? {
        for (key in keys) {
            val value = json.opt(key)
            if (value is String && value.isNotEmpty()) return value
        }
        return null
    }

    /**
     * 纯函数：历史记录 → upload 请求体（{sessions:[...]}）。
     *
     * 每条记录取 [TrainingHistoryRecord.resultJson] 原样解析后放入 sessions 数组；
     * 有训练后反馈时以 feedback 对象附加进对应 session。
     * 不可上传的记录（坏 JSON / 缺 sessionId / 缺 userId）跳过不崩；
     * 全部无效时 sessions 为空数组（调用方据此不发送请求）。
     */
    fun buildUploadBody(records: List<TrainingHistoryRecord>): JSONObject {
        val sessions = JSONArray()
        for (record in records) {
            if (!isUploadable(record)) continue
            sessions.put(sessionJsonOf(record))
        }
        return JSONObject().put("sessions", sessions)
    }

    /**
     * 单条记录的请求体 JSON（resultJson 原样 + SF-08 pre 关联），供 buildUploadBody/拆批共用。
     *
     * P3.5：训练后反馈不再拼接进 T01 上传体——反馈作为独立 S01 事件（post:<sessionId>）
     * 出站并有独立游标，避免「训练上传后补填的晚反馈」随 session 游标推进而永久丢失，
     * 也避免同一反馈在 T01 与 S01 两条通道重复。
     */
    internal fun sessionJsonOf(record: TrainingHistoryRecord): JSONObject {
        val session = JSONObject(record.resultJson)
        // SF-08：随训练上传显式声明 pre 问卷事件（pre:<recordedAt>，与 sync 通道同一身份）。
        // 服务器校验该问卷存在且属于该患者并写入 pre_training_event_id；空串 = 不声明。
        record.preTrainingEventId.takeIf(String::isNotBlank)?.let {
            session.put("preTrainingEventId", it)
        }
        return session
    }

    /**
     * 上传训练数据到服务器；body 必须由 [buildUploadBody] 产出。
     *
     * SF-01：patientToken 为该账号的 PAT（管理员签发），请求头携带；缺失时请求将被
     * 服务器 401 拒绝，协调器据此暂停冲刷并提示用户配置。
     *
     * @return 成功时返回服务器确认已入库的条数；失败返回 [Result.failure]。
     */
    suspend fun upload(body: JSONObject, patientToken: String?): Result<Int> = patientRequestResult {
        require((body.optJSONArray("sessions")?.length() ?: 0) > 0) { "没有可上传的有效训练数据" }
        val response = PatientHttpTransport.shared.request(UPLOAD_PATH, "POST", patientToken, body.toString())
        response.requireSuccess("上传失败，HTTP "+response.status)
        val json = JSONObject(response.text)
        check(json.optInt("code", -1) == 0) { "上传响应状态无效" }
        val data = json.optJSONObject("data")
        val uploaded = data?.optInt("uploaded", 0) ?: 0
        val skipped = data?.optInt("skipped", 0) ?: 0
        // Skipped records remain failures: the coordinator must not silently discard them.
        if (skipped > 0) throw RuntimeException("服务器跳过 $skipped 条记录（uploaded=$uploaded）")
        uploaded
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
    ): Result<List<ResultReceipt>> = patientRequestResult {
        require(sessions.length() > 0) { "没有可上传的 V2 训练结果" }
        require(sessions.length() == expected.size) {
            "冻结快照与期望回执数量不一致: ${sessions.length()}/${expected.size}"
        }
        val body = JSONObject().put("sessions", sessions)
        // 400/409 的正文必须读：V2 里 `PRE_TRAINING_EVENT_NOT_FOUND`（等练前事件确认后重发）
        // 与 `RESULT_REVISION_CONFLICT`（必须隔离）同为 409，只靠状态码会把两者混为一谈。
        val response = PatientHttpTransport.shared.request(
            UPLOAD_PATH, "POST", patientToken, body.toString(),
            readErrorStatuses = setOf(400, 409)
        )
        response.requireSuccess("上传失败，HTTP " + response.status)
        verifyUploadResponse(JSONObject(response.text), expected)
    }

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
        if (json.optInt("code", -1) != 0) throw RuntimeException("上传响应状态无效: ${json.optString("message")}")
        val data = json.optJSONObject("data")
            ?: throw RuntimeException("上传响应缺少 data")
        val skipped = data.optInt("skipped", -1)
        if (skipped != 0) throw RuntimeException("服务器跳过 $skipped 条记录")
        val array = data.optJSONArray("resultReceipts")
            ?: throw RuntimeException("上传响应缺少逐条回执")
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
        val uploaded = data.optInt("uploaded", -1)
        if (uploaded < expected.size) {
            throw RuntimeException("服务器只确认了 $uploaded/${expected.size} 条记录")
        }
        return receipts
    }

    /** P5.7：V1 面已退役——不再接受 snake_case 别名或补写练前事件。 */
    @Deprecated("V2 结果请使用 PrescriptionResultV2 + ResultRevisionStore")
    internal fun sessionJsonOfLegacy(record: TrainingHistoryRecord): JSONObject =
        JSONObject(record.resultJson)
}
