package com.metaspine.mobile.data

import android.content.Context
import org.json.JSONObject

/**
 * P5.3：训练结果的修订号与出站持久化。
 *
 * 一条结果的出站身份是 `scope + sessionId + revision + 内容摘要` 四元组：
 * - scope（账号绑定）不同即不同患者，永远不跨患者复用修订号；
 * - 同一 sessionId 的同一份内容（摘要相同）重试时**不得**递增修订号；
 * - 内容变化才递增，且必须严格大于已发出的最大修订号（允许跳跃，如 1→2→7）；
 * - sessionId / requestToken / createdAt / 处方号 / 清单摘要一经冻结不得改写。
 *
 * 落盘使用单次 `commit()`：冻结正文、修订号、摘要、依赖与 ACK 状态一起写入，
 * 进程在「入队后、发送前」或「发送后、ACK 前」被杀都能恢复到同一份事实重发。
 */
class ResultRevisionStore(context: Context) {

    private val prefs = context.getSharedPreferences("spine_results", Context.MODE_PRIVATE)

    /** 某 scope + sessionId 的持久状态；没有记录时返回 null。 */
    fun load(scope: String, sessionId: String): ResultRecord? =
        SecureLocalData.read(prefs, key(scope, sessionId))?.let { raw ->
            runCatching { ResultRecord.fromJson(JSONObject(raw)) }.getOrElse {
                android.util.Log.e(TAG, "结果修订记录损坏，无法解析: ${key(scope, sessionId)}")
                null
            }
        }

    /**
     * 冻结一份已通过 P5.1 校验的结果并分配修订号。
     *
     * @param scope 账号/患者绑定的隔离键（PAT 对应 patientId）。
     * @param result 已解析的 V2 快照。
     * @param patientId 当前 PAT 对应的 patientId，用于计算内容摘要。
     * @throws RevisionConflict 内容变化但修订号回退、或冻结身份被改写。
     * @throws ResultInputException.ResultInvalid 修订号/依赖字段非法。
     */
    fun freeze(scope: String, result: PrescriptionResultV2, patientId: String): ResultRecord {
        val frozen = result.freeze(patientId)
        val existing = load(scope, result.sessionId)
        val record = ResultRevisionPolicy.next(scope, result, frozen, existing)
        // 同一份内容命中已有记录时不必重复落盘。
        if (record !== existing) save(record)
        return record
    }

    /**
     * 发送前标记为在途。崩溃后重新读到 InFlight 也按待发处理（服务器按
     * revision + 摘要幂等，重发不会生成新内容）。
     */
    fun markInFlight(scope: String, sessionId: String, revision: Long): ResultRecord? {
        val record = load(scope, sessionId) ?: return null
        if (record.resultRevision != revision) return null
        if (record.ackState == AckState.Acked && record.acknowledgedRevision == revision) {
            return record
        }
        val next = record.copy(ackState = AckState.InFlight)
        save(next)
        return next
    }

    /**
     * P5.5：按四元组确认。只有 scope/sessionId/revision/摘要全部匹配当前待发记录，
     * 或是对已确认修订的幂等重复回包，才把记录标记为已确认。
     *
     * @return 是否真正推进了 ACK 状态；false 表示回包与待发事实矛盾，调用方必须保留任务。
     */
    fun acknowledge(
        scope: String,
        sessionId: String,
        revision: Long,
        contentSha256: String,
    ): Boolean {
        val record = load(scope, sessionId) ?: return false
        if (record.resultRevision == revision && record.contentSha256 == contentSha256) {
            if (record.ackState == AckState.Acked) return true
            save(record.copy(ackState = AckState.Acked, acknowledgedRevision = revision))
            return true
        }
        // 已确认过的旧修订被重放：幂等成功，不改变当前待发的新修订。
        if (record.acknowledgedRevision == revision) return true
        return false
    }

    /** P5.6：练前事件未确认——记录依赖，重发时仍指向原事件，不换事件也不丢任务。 */
    fun blockOnDependency(
        scope: String,
        sessionId: String,
        revision: Long,
        eventId: String,
    ): ResultRecord? {
        val record = load(scope, sessionId) ?: return null
        if (record.resultRevision != revision) return null
        if (record.blockedBy == eventId) return record
        val next = record.copy(blockedBy = eventId, ackState = AckState.Pending)
        save(next)
        return next
    }

    /** 依赖事件确认后解除阻塞；正文与摘要不变，因此不会产生新修订。 */
    fun clearDependency(scope: String, sessionId: String, eventId: String): ResultRecord? {
        val record = load(scope, sessionId) ?: return null
        if (record.blockedBy != eventId) return record
        val next = record.copy(blockedBy = null, ackState = AckState.Pending)
        save(next)
        return next
    }

    /** 该 scope 下仍需要出站的结果（有依赖阻塞的也要返回，由调用方判断依赖）。 */
    fun pending(scope: String): List<ResultRecord> = sessionIds(scope)
        .mapNotNull { load(scope, it) }
        .filter { it.ackState != AckState.Acked }
        // 按真实时刻排序：字符串比较会把 "00:01:00Z" 排在 "00:01:00.123Z" 之后。
        .sortedWith(
            compareBy<ResultRecord> { runCatching { java.time.Instant.parse(it.createdAt) }.getOrNull() }
                .thenBy { it.resultRevision }
        )

    fun remove(scope: String, sessionId: String) {
        val index = readIndex(scope).toMutableSet().apply { remove(sessionId) }
        check(
            prefs.edit()
                .remove(key(scope, sessionId))
                .putString(indexKey(scope), SecureLocalData.seal(indexKey(scope), index.joinToString("\n")))
                .commit()
        ) { "结果修订记录未能清理" }
    }

    fun clearScope(scope: String) {
        val editor = prefs.edit()
        for (sessionId in sessionIds(scope)) editor.remove(key(scope, sessionId))
        editor.remove(indexKey(scope))
        check(editor.commit()) { "结果修订记录未能清理" }
    }

    private fun save(record: ResultRecord) {
        val index = readIndex(record.scope).toMutableSet().apply { add(record.sessionId) }
        check(
            prefs.edit()
                .putString(key(record.scope, record.sessionId),
                    SecureLocalData.seal(key(record.scope, record.sessionId), record.toJson().toString()))
                .putString(indexKey(record.scope),
                    SecureLocalData.seal(indexKey(record.scope), index.joinToString("\n")))
                .commit()
        ) { "结果修订记录未能安全落盘" }
    }

    private fun sessionIds(scope: String): List<String> =
        readIndex(scope).filter(String::isNotBlank)

    private fun readIndex(scope: String): List<String> =
        SecureLocalData.read(prefs, indexKey(scope))
            ?.split('\n')
            ?.filter(String::isNotBlank)
            ?: emptyList()

    private fun indexKey(scope: String) = "results.index.$scope"
    private fun key(scope: String, sessionId: String) = "results.record.$scope.$sessionId"

    private companion object {
        const val TAG = "ResultRevisionStore"
    }
}

enum class AckState(val wireValue: String) {
    Pending("pending"),
    InFlight("in_flight"),
    Acked("acked");

    companion object {
        fun fromWire(value: String): AckState? = entries.firstOrNull { it.wireValue == value }
    }
}

/** 一份结果在本地冻结后的事实；正文与摘要此后不再重算。 */
data class ResultRecord(
    val scope: String,
    val sessionId: String,
    val requestToken: String,
    val createdAt: String,
    val prescriptionOrderId: Long,
    val resourceManifestSha256: String,
    val resultRevision: Long,
    val source: ResultSnapshotSource,
    val contentSha256: String,
    /** 冻结的出站正文（不含注入的 userId），重发原样使用。 */
    val body: String,
    /** 计算摘要所用的规范文本（已注入 userId），仅供诊断与校验。 */
    val canonicalUtf8: String,
    val itemIds: List<Long>,
    val modeKeys: List<String>,
    val preTrainingEventId: String?,
    val blockedBy: String?,
    val ackState: AckState,
    val acknowledgedRevision: Long?,
) {
    val receipt: ResultReceipt
        get() = ResultReceipt(sessionId, resultRevision, contentSha256)

    fun toJson(): JSONObject = JSONObject()
        .put("scope", scope)
        .put("sessionId", sessionId)
        .put("requestToken", requestToken)
        .put("createdAt", createdAt)
        .put("prescriptionOrderId", prescriptionOrderId)
        .put("resourceManifestSha256", resourceManifestSha256)
        .put("resultRevision", resultRevision)
        .put("source", source.wireValue)
        .put("contentSha256", contentSha256)
        .put("body", body)
        .put("canonicalUtf8", canonicalUtf8)
        .put("itemIds", org.json.JSONArray(itemIds))
        .put("modeKeys", org.json.JSONArray(modeKeys))
        .put("preTrainingEventId", preTrainingEventId ?: JSONObject.NULL)
        .put("blockedBy", blockedBy ?: JSONObject.NULL)
        .put("ackState", ackState.wireValue)
        .put("acknowledgedRevision", acknowledgedRevision ?: JSONObject.NULL)

    companion object {
        fun fromJson(json: JSONObject): ResultRecord {
            val source = ResultSnapshotSource.fromWire(json.getString("source"))
                ?: throw IllegalArgumentException("未知快照来源: ${json.getString("source")}")
            val acknowledged = (json.opt("acknowledgedRevision") as? Number)?.toLong()
            return ResultRecord(
                scope = json.getString("scope"),
                sessionId = json.getString("sessionId"),
                requestToken = json.getString("requestToken"),
                createdAt = json.getString("createdAt"),
                prescriptionOrderId = json.getLong("prescriptionOrderId"),
                resourceManifestSha256 = json.getString("resourceManifestSha256"),
                resultRevision = json.getLong("resultRevision"),
                source = source,
                contentSha256 = json.getString("contentSha256"),
                body = json.getString("body"),
                canonicalUtf8 = json.getString("canonicalUtf8"),
                itemIds = json.getJSONArray("itemIds").let { array ->
                    (0 until array.length()).map { array.getLong(it) }
                },
                modeKeys = json.getJSONArray("modeKeys").let { array ->
                    (0 until array.length()).map { array.getString(it) }
                },
                preTrainingEventId = json.opt("preTrainingEventId")
                    ?.takeIf { it !== JSONObject.NULL } as? String,
                blockedBy = json.opt("blockedBy")?.takeIf { it !== JSONObject.NULL } as? String,
                ackState = AckState.fromWire(json.getString("ackState")) ?: AckState.Pending,
                acknowledgedRevision = acknowledged,
            )
        }
    }
}

/** 修订号/冻结身份冲突：不得改写已有结果，也不得回退修订号。 */
class RevisionConflict(message: String) : Exception(message)
