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
    fun storageKey(): String = toJson().put("hospitalName", "").toString()

    fun toJson(): JSONObject = JSONObject()
        .put("origin", origin).put("serviceId", serviceId)
        .put("hospitalKey", hospitalKey).put("hospitalName", hospitalName)
        .put("patientId", patientId).put("accountId", accountId)
        .put("credentialGeneration", credentialGeneration)

    fun matchesOriginAndPatient(origin: String, patientId: String): Boolean =
        this.origin == origin && this.patientId == patientId

    fun matchesHospital(identity: ServiceIdentity): Boolean =
        identity.isHospital && serviceId == identity.serviceId && hospitalKey == identity.hospitalKey

    companion object {
        fun fromJson(json: JSONObject) = BindingScope(
            json.getString("origin"), json.getString("serviceId"), json.getString("hospitalKey"),
            json.optString("hospitalName"), json.getString("patientId"), json.getString("accountId"),
            json.getLong("credentialGeneration"))
    }

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
        return PatientCredentials.saveBindingScope(context, scope)
    }

    fun load(context: Context, accountId: String): BindingScope? =
        PatientCredentials.bindingScope(context, accountId) ?:
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
        PatientCredentials.clearBindingScope(context, accountId)
        check(prefs(context).edit().remove(key(accountId)).commit())
    }

    /** 新绑定或重新激活时推进代际，使旧 scope 的在途回包与队列失效。 */
    fun nextGeneration(context: Context, accountId: String): Long =
        (load(context, accountId)?.credentialGeneration ?: 0L) + 1L
}
