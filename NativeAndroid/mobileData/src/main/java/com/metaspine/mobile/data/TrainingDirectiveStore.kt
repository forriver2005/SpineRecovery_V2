package com.metaspine.mobile.data

import android.content.Context

/**
 * M7.1 §8.2：当前训练指令快照的本地存储端口。
 *
 * 抽成接口是为了让「拉取 → 校验 → 保存 → 回执」的编排逻辑可以在 JVM 单元测试里
 * 用内存实现验证，不必启动 Android 运行时。
 */
interface DirectiveSnapshotStorage {
    /** 读取该账号的快照；[patientId] 与快照内保存的归属不一致时必须返回 null。 */
    fun read(accountId: String, patientId: String): TrainingDirectiveSnapshot?

    /** 原子写入完整快照（含 waiting/stopped 等状态），不逐字段更新。 */
    fun write(accountId: String, snapshot: TrainingDirectiveSnapshot)

    fun clear(accountId: String)

    /** PAT 重新绑定到不同 patientId 时清除原快照，避免把别人的处方展示成本人的。 */
    fun clearIfPatientChanged(accountId: String, patientId: String)
}

/**
 * 独立 SharedPreferences 文件（`spine_training_directive`），key 按规范化 accountId 隔离。
 *
 * 刻意不放进 `PendingSyncStore`：那是待上传 FIFO 与上传游标，方向相反、失败策略不同。
 */
class TrainingDirectiveStore(context: Context) : DirectiveSnapshotStorage {
    private val prefs = context.applicationContext
        .getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    override fun read(accountId: String, patientId: String): TrainingDirectiveSnapshot? {
        if (patientId.isBlank()) return null
        val raw = SecureLocalData.read(prefs, key(accountId)) ?: return null
        val snapshot = directiveSnapshotFromStoreJson(raw) ?: return null
        // 归属校验：PAT 换绑到别的患者后，旧快照立即不可用。
        return snapshot.takeIf { it.patientId == patientId }
    }

    override fun write(accountId: String, snapshot: TrainingDirectiveSnapshot) {
        val storageKey = key(accountId)
        check(prefs.edit().putString(storageKey,
            SecureLocalData.seal(storageKey, snapshot.toStoreJson().toString())).commit()) {
            "训练指令未能安全保存"
        }
    }

    override fun clear(accountId: String) {
        check(prefs.edit().remove(key(accountId)).commit()) { "训练指令未能清理" }
    }

    override fun clearIfPatientChanged(accountId: String, patientId: String) {
        val raw = SecureLocalData.read(prefs, key(accountId)) ?: return
        val stored = directiveSnapshotFromStoreJson(raw) ?: run {
            clear(accountId)
            return
        }
        if (stored.patientId != patientId) clear(accountId)
    }

    private fun key(accountId: String) = KEY_PREFIX + accountId.trim().lowercase()

    companion object {
        const val PREFERENCES_NAME = "spine_training_directive"
        private const val KEY_PREFIX = "directive_"
    }
}
