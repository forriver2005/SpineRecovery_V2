package com.metaspine.mobile.data

import android.content.Context
import org.json.JSONObject

/**
 * 医院绑定作用域：规范 Origin、服务身份、患者与本地账号、凭据代际作为一个整体保存。
 *
 * 凭据、缓存、出站队列和在途回包都必须校验同一 scope；切换医院或重新激活会推进
 * [credentialGeneration]，使旧 scope 的在途回包失效（P2.1/P2.7）。
 */
data class BindingScope(
    val origin: String,
    val serviceId: String,
    val hospitalKey: String,
    val hospitalName: String,
    val patientId: String,
    val accountId: String,
    val credentialGeneration: Long
) {
    fun matches(other: BindingScope?): Boolean =
        other != null &&
            origin == other.origin &&
            serviceId == other.serviceId &&
            hospitalKey == other.hospitalKey &&
            patientId == other.patientId &&
            accountId == other.accountId &&
            credentialGeneration == other.credentialGeneration
}

object BindingScopeStore {
    private const val PREFERENCES_NAME = "spineflow_binding_scope"
    private const val KEY_PREFIX = "binding_scope_"

    private fun prefs(context: Context) =
        context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    private fun key(accountId: String) = KEY_PREFIX + accountId.trim().lowercase()

    fun save(context: Context, scope: BindingScope): Boolean {
        val json = JSONObject()
            .put("origin", scope.origin)
            .put("serviceId", scope.serviceId)
            .put("hospitalKey", scope.hospitalKey)
            .put("hospitalName", scope.hospitalName)
            .put("patientId", scope.patientId)
            .put("accountId", scope.accountId)
            .put("credentialGeneration", scope.credentialGeneration)
        val key = key(scope.accountId)
        return prefs(context).edit()
            .putString(key, SecureLocalData.seal(key, json.toString()))
            .commit()
    }

    fun load(context: Context, accountId: String): BindingScope? =
        SecureLocalData.read(prefs(context), key(accountId))?.let { raw ->
            runCatching {
                val json = JSONObject(raw)
                BindingScope(
                    origin = json.getString("origin"),
                    serviceId = json.getString("serviceId"),
                    hospitalKey = json.getString("hospitalKey"),
                    hospitalName = json.optString("hospitalName"),
                    patientId = json.getString("patientId"),
                    accountId = json.getString("accountId"),
                    credentialGeneration = json.optLong("credentialGeneration", 0L)
                )
            }.getOrNull()
        }

    fun clear(context: Context, accountId: String) {
        prefs(context).edit().remove(key(accountId)).apply()
    }

    /** 新绑定或重新激活时推进代际，使旧 scope 的在途回包与队列失效。 */
    fun nextGeneration(context: Context, accountId: String): Long =
        (load(context, accountId)?.credentialGeneration ?: 0L) + 1L
}
