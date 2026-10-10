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
        check(prefs.edit().putString(key, SecureLocalData.seal(key, value)).commit()) {
            "同步队列未能安全落盘"
        }
    }

    private fun queueKey(accountId: String) = "sync.queue.$accountId"
    private fun snapshotKey(accountId: String) = "sync.snapshot.$accountId"

    fun clearAccount(accountId: String) {
        check(prefs.edit().remove(queueKey(accountId)).remove(snapshotKey(accountId))
            .remove("sync.quarantine.$accountId").commit()) { "同步缓存未能清理" }
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
        return runCatching { raw.toSyncSnapshot() }.getOrElse { PatientSyncClient.SyncSnapshot() }
    }

    fun saveSnapshot(accountId: String, snapshot: PatientSyncClient.SyncSnapshot) {
        write(snapshotKey(accountId), snapshot.toJson().toString())
    }

    private fun readArray(key: String): JSONArray =
        SecureLocalData.read(prefs, key)
            ?.let { runCatching { JSONArray(it) }.getOrNull() }
            ?: JSONArray()

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
}

// ---- 快照 <-> JSON ----

internal fun PatientSyncClient.SyncSnapshot.toJson(): JSONObject = JSONObject()
    .put("sessionIds", JSONArray(syncedSessionIds.sorted()))
    .put("painIds", JSONArray(syncedPainIds.sorted()))
    .put("qHashes", JSONObject(questionnaireHashes))
    .put("profileHash", profileHash ?: JSONObject.NULL)
    .put("lifecycle", lifecycle ?: JSONObject.NULL)
    .put("imuCalibrationState", imuCalibrationState ?: JSONObject.NULL)

internal fun String.toSyncSnapshot(): PatientSyncClient.SyncSnapshot {
    val json = JSONObject(this)
    return PatientSyncClient.SyncSnapshot(
        syncedSessionIds = json.optJSONArray("sessionIds").toStringSet(),
        syncedPainIds = json.optJSONArray("painIds").toStringSet(),
        questionnaireHashes = json.optJSONObject("qHashes")?.toMapOfStrings() ?: emptyMap(),
        profileHash = json.opt("profileHash")?.takeIf { it !== JSONObject.NULL } as? String,
        lifecycle = json.opt("lifecycle")?.takeIf { it !== JSONObject.NULL } as? String,
        imuCalibrationState = json.opt("imuCalibrationState")?.takeIf { it !== JSONObject.NULL } as? String,
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
) {
    fun toJson(): JSONObject = JSONObject()
        .put("sessionIds", JSONArray(sessionIds.sorted()))
        .put("painIds", JSONArray(painIds.sorted()))
        .put("qHashes", JSONObject(qHashes))
        .put("profileHash", profileHash ?: JSONObject.NULL)
        .put("lifecycle", lifecycle ?: JSONObject.NULL)
        .put("imuCalibrationState", imuCalibrationState ?: JSONObject.NULL)

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
