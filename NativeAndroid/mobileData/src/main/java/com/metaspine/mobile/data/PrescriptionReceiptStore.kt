package com.metaspine.mobile.data

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

/**
 * 两阶段回执（P4 §4.2）。只有本机确实完成了对应前置条件才推进：
 * - [Saved]：处方、清单与 scope 已落盘；可以提交 `directive_saved`。
 * - [Ready]：全部固定文件已下载并核对、消费者确认能力后才能提交 `assets_ready`。
 */
enum class ReceiptPhase { NotSubmitted, Saved, AssetsReady }

data class PrescriptionReceiptTask(
    val orderId: Long,
    val resourceManifestSha256: String,
    val phase: ReceiptPhase,
    val savedReceivedAt: String?,
    val assetsReadyReceivedAt: String?,
    val failedAssetItemIds: List<Long>,
    val blockingReasons: List<String>,
    val readyPending: Boolean = false
) {
    internal fun toJson(): JSONObject = JSONObject()
        .put("orderId", orderId)
        .put("resourceManifestSha256", resourceManifestSha256)
        .put("phase", phase.name)
        .put("savedReceivedAt", savedReceivedAt)
        .put("assetsReadyReceivedAt", assetsReadyReceivedAt)
        .put("failedAssetItemIds", JSONArray(failedAssetItemIds))
        .put("blockingReasons", JSONArray(blockingReasons))
        .put("readyPending", readyPending)

    companion object {
        internal fun fromJson(json: JSONObject): PrescriptionReceiptTask =
            PrescriptionReceiptTask(
                orderId = json.getLong("orderId"),
                resourceManifestSha256 = json.getString("resourceManifestSha256"),
                phase = runCatching { ReceiptPhase.valueOf(json.getString("phase")) }
                    .getOrDefault(ReceiptPhase.NotSubmitted),
                savedReceivedAt = (json.opt("savedReceivedAt") as? String)?.takeIf(String::isNotBlank),
                assetsReadyReceivedAt = (json.opt("assetsReadyReceivedAt") as? String)
                    ?.takeIf(String::isNotBlank),
                failedAssetItemIds = json.optJSONArray("failedAssetItemIds")?.let { array ->
                    (0 until array.length()).map { array.getLong(it) }
                }.orEmpty(),
                blockingReasons = json.optJSONArray("blockingReasons")?.let { array ->
                    (0 until array.length()).map { array.getString(it) }
                }.orEmpty(),
                readyPending = json.optBoolean("readyPending", false)
            )
    }
}

/**
 * 按完整绑定 scope 保存回执任务；换医院/患者/代际后旧任务不可见，也不会被新 PAT 提交。
 *
 * 同一 scope 内每个订单只保留一条记录：manifest 变化即视为新任务（服务器会以
 * `MANIFEST_MISMATCH` 拒绝旧摘要，本地不应覆盖式伪造已确认状态）。
 */
class PrescriptionReceiptStore(private val prefs: android.content.SharedPreferences) :
    PrescriptionReceiptStorage {

    constructor(context: Context) : this(context.applicationContext.getSharedPreferences(
        PREFERENCES_NAME, Context.MODE_PRIVATE))

    private fun scopePrefix(scope: BindingScope): String = PREFIX + scopeKey(scope) + "_"

    private fun key(scope: BindingScope, orderId: Long): String =
        scopePrefix(scope) + orderId

    override fun read(scope: BindingScope, orderId: Long): PrescriptionReceiptTask? {
        val storageKey = key(scope, orderId)
        val raw = SecureLocalData.read(prefs, storageKey) ?: return null
        val task = runCatching { PrescriptionReceiptTask.fromJson(JSONObject(raw)) }.getOrNull()
            ?: return null
        // 同一订单号在不同 scope 下互不认可：key 已按 scope 隔离，这里再核一次订单归属。
        return task.takeIf { it.orderId == orderId && it.resourceManifestSha256.isNotBlank() }
    }

    /** 落盘失败必须抛出：调用方据此放弃本次 ACK，绝不“先回执后保存”。 */
    override fun write(scope: BindingScope, task: PrescriptionReceiptTask) {
        val storageKey = key(scope, task.orderId)
        check(prefs.edit()
            .putString(storageKey, SecureLocalData.seal(storageKey, task.toJson().toString()))
            .commit()) { "回执任务未能安全保存" }
    }

    /** 切换绑定后清理该 scope 的全部回执任务，避免旧绑定继续重试。 */
    fun clearScope(scope: BindingScope) {
        val prefix = scopePrefix(scope)
        val editor = prefs.edit()
        prefs.all.keys.filter { it.startsWith(prefix) }.forEach { editor.remove(it) }
        editor.commit()
    }

    private fun scopeKey(scope: BindingScope): String = listOf(
        scope.origin, scope.serviceId, scope.hospitalKey, scope.patientId,
        scope.accountId, scope.credentialGeneration.toString()
    ).joinToString("|").hashCode().toUInt().toString(16)

    companion object {
        private const val PREFERENCES_NAME = "spine_prescription_receipts"
        private const val PREFIX = "receipt_"
    }
}
