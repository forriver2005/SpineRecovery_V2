package com.metaspine.mobile.data

import org.json.JSONObject

/** 医院资源授权时间均为毫秒；与移动 V2 协议版本无关。 */
data class TrainingResourceLease(
    val contractVersion: Int,
    val generation: String,
    val revision: Long,
    val serverTime: Long,
    val expiresAt: Long
) {
    fun toJson(): JSONObject = JSONObject()
        .put("contractVersion", contractVersion).put("generation", generation)
        .put("revision", revision).put("serverTime", serverTime).put("expiresAt", expiresAt)

    fun requireSuccessorOf(previous: TrainingResourceLease) {
        require(generation == previous.generation && revision >= previous.revision) { "资源代际或修订已失效" }
        require(serverTime >= previous.serverTime) { "资源服务器时间回退" }
    }

    companion object {
        fun fromJson(json: JSONObject): TrainingResourceLease {
            fun integer(key: String): Long {
                val number = json.get(key) as? Number ?: error("资源租约 $key 必须为整数")
                val value = number.toDouble()
                require(value.isFinite() && value >= 0 && value <= 9_007_199_254_740_991.0 && value == kotlin.math.floor(value)) {
                    "资源租约 $key 不合法"
                }
                return value.toLong()
            }
            val version = integer("contractVersion")
            require(version == 1L) { "资源租约合同版本不支持" }
            val generation = json.getString("generation")
            require(generation.isNotBlank()) { "资源租约缺少代际" }
            return TrainingResourceLease(1, generation, integer("revision"), integer("serverTime"), integer("expiresAt"))
                .also { require(it.expiresAt > it.serverTime) { "资源租约已过期" } }
        }
    }
}

/** 只在本次准备/确认操作内使用；不持久化单调时钟锚点，不跨进程重启恢复授权。 */
data class ObservedResourceLease(val lease: TrainingResourceLease, val observedElapsedMillis: Long) {
    fun isValidAt(elapsedMillis: Long): Boolean {
        val elapsed = elapsedMillis - observedElapsedMillis
        return elapsed >= 0 && elapsed < lease.expiresAt - lease.serverTime
    }
}

data class PrescriptionResourceStatus(val lease: TrainingResourceLease, val states: Map<Long, String>) {
    fun requireAvailable(order: DownloadedOrder) {
        require(order.items.all { states[it.asset.actionVersionId] in setOf("active", "unlisted") }) {
            "处方固定资源已撤回或状态不可确认"
        }
    }

    companion object {
        fun fromJson(json: JSONObject): PrescriptionResourceStatus {
            val lease = TrainingResourceLease.fromJson(json.getJSONObject("resourceLease"))
            val items = json.getJSONArray("items")
            val states = (0 until items.length()).associate { index ->
                val item = items.getJSONObject(index)
                item.getLong("releaseId") to item.getString("state")
            }
            return PrescriptionResourceStatus(lease, states)
        }
    }
}
