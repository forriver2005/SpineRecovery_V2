package com.metaspine.mobile.data

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

/**
 * M3.6 Step 4：待传队列与游标快照的持久化（SharedPreferences，按账号分键）。
 *
 * 防回归：所有写入用 commit() 同步落盘——进程被杀丢队列 = 训练记录永远上不了服务器；
 * 旧明文会先加密迁移；加密数据损坏时中断同步，避免静默丢弃待上传内容。
 */
class PendingSyncStore(context: Context) {

    private val prefs = context.getSharedPreferences("spine_sync", Context.MODE_PRIVATE)

    private fun write(key: String, value: String) {
        val editor = prefs.edit().putString(key, SecureLocalData.seal(key, value))
        // 一次成功的有效写入即消解此前的损坏标记（P2.4：损坏必须可诊断，但可被新写入恢复）。
        if (key.startsWith(QUEUE_PREFIX)) editor.putBoolean(CORRUPT_PREFIX + key.removePrefix(QUEUE_PREFIX), false)
        check(editor.commit()) { "同步队列未能安全落盘" }
    }

    private fun queueKey(accountId: String) = QUEUE_PREFIX + accountId
    private fun snapshotKey(accountId: String) = "sync.snapshot.$accountId"
    private fun dirtyKey(accountId: String) = "sync.dirty.$accountId"

    fun clearAccount(accountId: String) {
        check(prefs.edit().remove(queueKey(accountId)).remove(snapshotKey(accountId))
            .remove("sync.quarantine.$accountId").remove(dirtyKey(accountId))
            .remove(CORRUPT_PREFIX + accountId).commit()) { "同步缓存未能清理" }
    }

    // ---------- P2.4：持久 dirty 标记 + 损坏可诊断 ----------

    /**
     * 用户状态落盘后立即同步打标记（在 5 秒防抖入队之前）。
     * 防回归：若进程在防抖窗口内被强杀，diff 从未入队、队列为空；重启后靠该标记
     * 重算待发，恢复「保存后 0 秒 / 2 秒强杀仍能补发」的语义。
     */
    fun markDirty(accountId: String) {
        check(prefs.edit().putBoolean(dirtyKey(accountId), true).commit()) { "待发标记未能落盘" }
    }

    fun clearDirty(accountId: String) {
        prefs.edit().putBoolean(dirtyKey(accountId), false).commit()
    }

    fun dirtyAccountIds(): Set<String> = buildSet {
        for (key in prefs.all.keys) {
            if (key.startsWith("sync.dirty.") && prefs.getBoolean(key, false)) {
                add(key.removePrefix("sync.dirty."))
            }
        }
    }

    /** 队列密文存在但无法解析——必须人工诊断，绝不当作「没有待发」静默丢弃。 */
    fun isQueueCorrupted(accountId: String): Boolean =
        prefs.getBoolean(CORRUPT_PREFIX + accountId, false)

    // ---------- P2.5：ACK 原子提交 ----------

    /**
     * 成功后在单次 commit() 内同时「移除队列元素」并「推进游标快照」。
     * 防回归：旧实现先 remove 再 applyCursor 是两次独立写入，中间被杀会出现
     * 「已移除但游标未推进」（重启重算重发）或反向窗口。这里合并为一次落盘。
     * @return 队列中确实存在该元素并完成提交返回 true；元素已不存在返回 false（不推进游标）。
     */
    fun commitAck(accountId: String, id: String, cursorJson: JSONObject?): Boolean {
        val key = queueKey(accountId)
        val queue = readArray(key)
        val next = JSONArray()
        var found = false
        for (index in 0 until queue.length()) {
            val element = queue.optJSONObject(index) ?: continue
            if (element.optString("id") == id) found = true else next.put(element)
        }
        if (!found) return false
        val cursor = QueueCursor.fromJson(cursorJson)
        val editor = prefs.edit()
            .putString(key, SecureLocalData.seal(key, next.toString()))
            .putBoolean(CORRUPT_PREFIX + accountId, false)
        // V2 结果任务的 ACK 由 ResultRevisionStore 逐条推进（四元组核对），
        // 不并入训练游标——否则回执与游标会各记一半。
        if (cursor.sessionIds.isNotEmpty() || cursor.painIds.isNotEmpty() ||
            cursor.qHashes.isNotEmpty() || cursor.profileHash != null ||
            cursor.lifecycle != null || cursor.imuCalibrationState != null ||
            cursor.feedbackEventIds.isNotEmpty()
        ) {
            val updated = snapshot(accountId)
                .afterUpload(cursor.sessionIds)
                .afterSync(
                    cursor.painIds, cursor.qHashes, cursor.profileHash,
                    cursor.lifecycle, cursor.imuCalibrationState, cursor.feedbackEventIds,
                )
            val sKey = snapshotKey(accountId)
            editor.putString(sKey, SecureLocalData.seal(sKey, updated.toJson().toString()))
        }
        check(editor.commit()) { "ACK 未能原子落盘" }
        return true
    }

    /** 队列元素：{id, kind: "upload"|"sync", payload, attempts, enqueuedAt, cursor} */
    fun enqueue(accountId: String, element: JSONObject) {
        val key = queueKey(accountId)
        val array = readArray(key)
        array.put(element)
        write(key, array.toString())
    }

    /**
     * 评审 #4：同类替换。diff 相对「最后成功快照」是累积的，离线期间反复触发会
     * 产生同一批事件的多个队列副本（且各自携带新 UUID，服务器无法去重）；
     * 同类新元素取代旧元素，队列中每个 kind 至多保留一个最新元素。
     */
    fun replaceKind(accountId: String, kind: String, element: JSONObject) {
        val key = queueKey(accountId)
        val array = readArray(key)
        val next = JSONArray()
        for (i in 0 until array.length()) {
            val existing = array.optJSONObject(i) ?: continue
            if (existing.optString("kind") != kind) next.put(existing)
        }
        next.put(element)
        write(key, next.toString())
    }

    /** 已入队会话：保留 413 拆出的子批 FIFO 位置，后续状态变化只追加新会话。 */
    fun pendingUploadSessionIds(accountId: String): Set<String> {
        val queue = readArray(queueKey(accountId))
        return buildSet {
            for (index in 0 until queue.length()) {
                val item = queue.optJSONObject(index) ?: continue
                if (item.optString("kind").startsWith("upload")) {
                    addAll(QueueCursor.fromJson(item.optJSONObject("cursor")).sessionIds)
                }
            }
        }
    }

    /** 队首元素（不移除）；空队列返回 null。 */
    fun peek(accountId: String): JSONObject? = readArray(queueKey(accountId)).optJSONObject(0)

    fun hasElement(accountId: String, id: String): Boolean =
        element(accountId, id) != null

    /** A rejected frozen event/revision stays held when foreground recovery recomputes pending work. */
    fun isQuarantined(accountId: String, id: String): Boolean {
        val held = readArray("sync.quarantine.$accountId")
        return (0 until held.length()).any { held.optJSONObject(it)?.optString("id") == id }
    }

    fun element(accountId: String, id: String): JSONObject? {
        val queue = readArray(queueKey(accountId))
        for (index in 0 until queue.length()) {
            val item = queue.optJSONObject(index) ?: continue
            if (item.optString("id") == id) return item
        }
        return null
    }

    /** 队列中全部元素 id（供按会话清理过期修订）。 */
    fun pendingIds(accountId: String): List<String> {
        val queue = readArray(queueKey(accountId))
        return buildList {
            for (index in 0 until queue.length()) {
                queue.optJSONObject(index)?.optString("id")?.takeIf(String::isNotBlank)?.let(::add)
            }
        }
    }

    fun remove(accountId: String, id: String) {
        val key = queueKey(accountId)
        val array = readArray(key)
        val next = JSONArray()
        for (i in 0 until array.length()) {
            val element = array.optJSONObject(i) ?: continue
            if (element.optString("id") != id) next.put(element)
        }
        write(key, next.toString())
    }

    /** 失败次数 +1 并落盘；返回新的 attempts（元素不存在返回 -1）。 */
    fun bumpAttempts(accountId: String, id: String): Int {
        val key = queueKey(accountId)
        val array = readArray(key)
        var attempts = -1
        for (i in 0 until array.length()) {
            val element = array.optJSONObject(i) ?: continue
            if (element.optString("id") == id) {
                attempts = element.optInt("attempts", 0) + 1
                element.put("attempts", attempts)
            }
        }
        write(key, array.toString())
        return attempts
    }

    fun pendingCount(accountId: String): Int = readArray(queueKey(accountId)).length()

    /** 永久隔离的上传会话不因下一次用户状态变化而重新入队。原始批次仍留在加密隔离区。 */
    fun quarantinedUploadSessionIds(accountId: String): Set<String> {
        val held = readArray("sync.quarantine.$accountId")
        return buildSet {
            for (index in 0 until held.length()) {
                val item = held.optJSONObject(index) ?: continue
                if (item.optString("kind").startsWith("upload")) {
                    addAll(QueueCursor.fromJson(item.optJSONObject("cursor")).sessionIds)
                }
            }
        }
    }

    /** 有积压任务的账号集合（供协调器冲刷遍历）。 */
    fun pendingAccountIds(): Set<String> = buildSet {
        for (key in prefs.all.keys) {
            if (key.startsWith("sync.queue.") && readArray(key).length() > 0) {
                add(key.removePrefix("sync.queue."))
            }
        }
    }

    fun snapshot(accountId: String): PatientSyncClient.SyncSnapshot {
        val raw = SecureLocalData.read(prefs, snapshotKey(accountId))
            ?: return PatientSyncClient.SyncSnapshot()
        return runCatching { raw.toSyncSnapshot() }.getOrElse {
            // 快照损坏不等于「什么都没同步过」：记录日志供诊断，返回空快照会让重算
            // 按未同步处理（服务器对不可变事件幂等去重），但绝不静默无声。
            android.util.Log.e(TAG, "游标快照损坏，无法解析: ${snapshotKey(accountId)}（按空快照重算，服务器幂等去重）")
            PatientSyncClient.SyncSnapshot()
        }
    }

    fun saveSnapshot(accountId: String, snapshot: PatientSyncClient.SyncSnapshot) {
        write(snapshotKey(accountId), snapshot.toJson().toString())
    }

    private fun readArray(key: String): JSONArray {
        val raw = SecureLocalData.read(prefs, key) ?: return JSONArray()
        return runCatching { JSONArray(raw) }.getOrElse {
            // 防回归：密文存在但解析失败 = 数据损坏。必须留下可诊断标记，
            // 且 dirty 标记不清除，避免把「解析失败」误记成「没有待发」。
            android.util.Log.e(TAG, "本地同步队列损坏，无法解析: $key（保留损坏标记，待人工诊断）")
            if (key.startsWith(QUEUE_PREFIX)) {
                prefs.edit().putBoolean(CORRUPT_PREFIX + key.removePrefix(QUEUE_PREFIX), true).commit()
            }
            JSONArray()
        }
    }

    /** 413 或协议拒绝时保留原始队列元素供人工诊断，绝不推进上传游标。 */
    fun quarantine(accountId: String, id: String, reason: String) {
        val sourceKey = queueKey(accountId)
        val quarantineKey = "sync.quarantine.$accountId"
        val queue = readArray(sourceKey)
        val next = JSONArray()
        val held = readArray(quarantineKey)
        for (index in 0 until queue.length()) {
            val item = queue.optJSONObject(index) ?: continue
            if (item.optString("id") == id) {
                item.put("quarantineReason", reason)
                held.put(item)
            } else next.put(item)
        }
        check(prefs.edit()
            .putString(sourceKey, SecureLocalData.seal(sourceKey, next.toString()))
            .putString(quarantineKey, SecureLocalData.seal(quarantineKey, held.toString()))
            .commit()) { "隔离失败批次未能安全落盘" }
    }

    /** 保持 FIFO 位置，原子替换单个过大的上传批次。 */
    fun replaceItem(accountId: String, id: String, replacements: List<JSONObject>): Boolean {
        val key = queueKey(accountId)
        val queue = readArray(key)
        val next = JSONArray()
        var replaced = false
        for (index in 0 until queue.length()) {
            val item = queue.optJSONObject(index) ?: continue
            if (item.optString("id") == id) {
                replacements.forEach { next.put(it) }
                replaced = true
            } else next.put(item)
        }
        if (replaced) write(key, next.toString())
        return replaced
    }

    private companion object {
        const val TAG = "PendingSyncStore"
        const val QUEUE_PREFIX = "sync.queue."
        const val CORRUPT_PREFIX = "sync.corrupt."
    }
}

// ---- 快照 <-> JSON ----

internal fun PatientSyncClient.SyncSnapshot.toJson(): JSONObject = JSONObject()
    .put("sessionIds", JSONArray(syncedSessionIds.sorted()))
    .put("painIds", JSONArray(syncedPainIds.sorted()))
    .put("qHashes", JSONObject(questionnaireHashes))
    .put("profileHash", profileHash ?: JSONObject.NULL)
    .put("lifecycle", lifecycle ?: JSONObject.NULL)
    .put("imuCalibrationState", imuCalibrationState ?: JSONObject.NULL)
    .put("feedbackEventIds", JSONArray(syncedFeedbackEventIds.sorted()))

internal fun String.toSyncSnapshot(): PatientSyncClient.SyncSnapshot {
    val json = JSONObject(this)
    return PatientSyncClient.SyncSnapshot(
        syncedSessionIds = json.optJSONArray("sessionIds").toStringSet(),
        syncedPainIds = json.optJSONArray("painIds").toStringSet(),
        questionnaireHashes = json.optJSONObject("qHashes")?.toMapOfStrings() ?: emptyMap(),
        profileHash = json.opt("profileHash")?.takeIf { it !== JSONObject.NULL } as? String,
        lifecycle = json.opt("lifecycle")?.takeIf { it !== JSONObject.NULL } as? String,
        imuCalibrationState = json.opt("imuCalibrationState")?.takeIf { it !== JSONObject.NULL } as? String,
        // 旧快照无此键 → 空集合 → 反馈按未确认重发一次，服务器按 eventId 幂等去重。
        syncedFeedbackEventIds = json.optJSONArray("feedbackEventIds").toStringSet(),
    )
}

internal fun JSONArray?.toStringSet(): Set<String> {
    if (this == null) return emptySet()
    return buildSet {
        for (i in 0 until length()) {
            optString(i).takeIf { it.isNotBlank() }?.let(::add)
        }
    }
}

internal fun JSONObject.toMapOfStrings(): Map<String, String> = buildMap {
    keys().forEach { key -> put(key, optString(key)) }
}

// ---- 队列元素与游标 ----

/** 队列元素携带的「成功推进游标所需信息」（成功发送后合并写回快照）。 */
internal data class QueueCursor(
    val sessionIds: Set<String> = emptySet(),
    val painIds: Set<String> = emptySet(),
    val qHashes: Map<String, String> = emptyMap(),
    val profileHash: String? = null,
    val lifecycle: String? = null,
    val imuCalibrationState: String? = null,
    val feedbackEventIds: Set<String> = emptySet(),
    /** P5.5：V2 结果任务必须保留 scope + 逐条期望回执，ACK 时逐项核对。 */
    val resultScope: String? = null,
    val resultReceipts: List<ResultReceipt> = emptyList(),
) {
    fun toJson(): JSONObject = JSONObject()
        .put("sessionIds", JSONArray(sessionIds.sorted()))
        .put("painIds", JSONArray(painIds.sorted()))
        .put("qHashes", JSONObject(qHashes))
        .put("profileHash", profileHash ?: JSONObject.NULL)
        .put("lifecycle", lifecycle ?: JSONObject.NULL)
        .put("imuCalibrationState", imuCalibrationState ?: JSONObject.NULL)
        .put("feedbackEventIds", JSONArray(feedbackEventIds.sorted()))
        .put("resultScope", resultScope ?: JSONObject.NULL)
        .put("resultReceipts", JSONArray(resultReceipts.map { it.toJson() }))

    companion object {
        fun fromJson(json: JSONObject?): QueueCursor {
            if (json == null) return QueueCursor()
            return QueueCursor(
                sessionIds = json.optJSONArray("sessionIds").toStringSet(),
                painIds = json.optJSONArray("painIds").toStringSet(),
                qHashes = json.optJSONObject("qHashes")?.toMapOfStrings() ?: emptyMap(),
                profileHash = json.opt("profileHash")?.takeIf { it !== JSONObject.NULL } as? String,
                lifecycle = json.opt("lifecycle")?.takeIf { it !== JSONObject.NULL } as? String,
                imuCalibrationState = json.opt("imuCalibrationState")
                    ?.takeIf { it !== JSONObject.NULL } as? String,
                feedbackEventIds = json.optJSONArray("feedbackEventIds").toStringSet(),
                resultScope = json.opt("resultScope")?.takeIf { it !== JSONObject.NULL } as? String,
                resultReceipts = json.optJSONArray("resultReceipts")?.let { array ->
                    (0 until array.length()).mapNotNull { index ->
                        array.optJSONObject(index)?.let(ResultReceipt::fromJson)
                    }
                } ?: emptyList(),
            )
        }
    }
}

internal fun queueElement(
    id: String,
    kind: String,
    payload: JSONObject,
    cursor: QueueCursor,
): JSONObject = JSONObject()
    .put("id", id)
    .put("kind", kind)
    .put("payload", payload)
    .put("attempts", 0)
    .put("enqueuedAt", System.currentTimeMillis())
    .put("cursor", cursor.toJson())
