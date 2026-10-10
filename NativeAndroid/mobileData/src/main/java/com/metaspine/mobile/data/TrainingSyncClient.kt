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

    /** 单条记录的请求体 JSON（resultJson 原样 + feedback 附加 + SF-08 pre 关联），供 buildUploadBody/拆批共用。 */
    internal fun sessionJsonOf(record: TrainingHistoryRecord): JSONObject {
        val session = JSONObject(record.resultJson)
        record.feedback?.let { feedback ->
            session.put(
                "feedback",
                JSONObject()
                    .put("feeling", feedback.feeling)
                    .put("difficulty", feedback.difficulty)
            )
        }
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
}
