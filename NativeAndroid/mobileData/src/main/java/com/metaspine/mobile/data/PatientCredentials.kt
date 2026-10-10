package com.metaspine.mobile.data

import android.content.Context
import com.metaspine.mobile.unified.BuildConfig
import org.json.JSONObject
import java.util.UUID

data class PatientServerCredential(
    val patientId: String,
    val patientToken: String
)

/**
 * 阶段 C：已提交但尚未确认结果的激活请求。
 *
 * 明文激活码只为「响应丢失后重试同一请求」而暂存：Server 依据
 * `requestId + requestHash` 识别重放，重试不会消耗第二个码、也不会创建第二个 PAT；
 * 核销成功后立即清除（设计 §4.3、§11.10）。
 */
data class PendingActivationBinding(
    val requestId: String,
    val activationCode: String,
    val proposedToken: String,
    val installationId: String,
    val accountId: String
)

/** 本地账号激活状态（设计 §5.3）。 */
sealed interface AccountActivationState {
    /** 没有正式凭据、也没有待重试的激活请求：需要输入激活码。 */
    data object Unactivated : AccountActivationState

    /** 已落盘候选参数但结果未知：进入激活页重试同一请求。 */
    data class Submitting(val pending: PendingActivationBinding) : AccountActivationState

    /** 已有完整 patientId + PAT。 */
    data class Activated(val credential: PatientServerCredential) : AccountActivationState

    /** 只有 patientId 或只有 PAT：损坏状态，不开放训练。 */
    data object Damaged : AccountActivationState
}

data class AdmissionPrescriptionItem(
    val actionId: String,
    val actionName: String,
    val sets: Int,
    val reps: Int,
    val holdSeconds: Int
)

/**
 * SF-01：患者凭据（PAT）与服务器地址覆盖的本地存储。
 *
 * 阶段 C：患者先输入医生给出的激活码，核销成功后一次性写入 patientId、PAT、脱敏身份
 * 与首张处方；未完成核销前只保存 [PendingActivationBinding]。
 * M6.1 的「待审申请 / 线下就诊结论」读写接口已随旧审核流程一并删除。
 * 服务器地址覆盖用于医院现场换网络时由 IT 一次性配置，未填写时回退构建期地址。
 */
object PatientCredentials {
    private const val PREFERENCES_NAME = "spineflow_patient_credentials"
    private const val KEY_TOKEN_PREFIX = "patient_token_"
    private const val KEY_PATIENT_ID_PREFIX = "patient_id_"
    private const val KEY_PRESCRIPTION_PREFIX = "admission_prescription_"
    private const val KEY_ACTIVATION_PENDING_PREFIX = "activation_pending_"
    private const val KEY_IDENTITY_PREFIX = "activation_identity_"
    private const val KEY_INSTALLATION_ID = "installation_id"
    private const val KEY_SERVER_URL = "server_url_override"

    private fun prefs(context: Context) =
        context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    fun savePatientToken(context: Context, accountId: String, token: String) {
        val key = KEY_TOKEN_PREFIX + accountKey(accountId)
        check(prefs(context).edit().putString(key, SecureLocalData.seal(key, token.trim())).commit()) {
            "患者凭据未能安全保存"
        }
    }

    fun patientToken(context: Context, accountId: String): String? =
        SecureLocalData.read(prefs(context), KEY_TOKEN_PREFIX + accountKey(accountId))
            ?.takeIf { it.isNotBlank() }

    fun clearPatientToken(context: Context, accountId: String) {
        prefs(context).edit().remove(KEY_TOKEN_PREFIX + accountId.trim().lowercase()).apply()
    }

    private fun accountKey(accountId: String): String = accountId.trim().lowercase()

    fun clearAccount(context: Context, accountId: String) {
        val editor = prefs(context).edit()
        listOf(KEY_TOKEN_PREFIX, KEY_PATIENT_ID_PREFIX, KEY_PRESCRIPTION_PREFIX,
            KEY_ACTIVATION_PENDING_PREFIX, KEY_IDENTITY_PREFIX).forEach { editor.remove(it + accountKey(accountId)) }
        check(editor.commit()) { "患者绑定信息未能清理" }
    }

    fun installationId(context: Context): String {
        val preferences = prefs(context)
        return SecureLocalData.read(preferences, KEY_INSTALLATION_ID)?.takeIf(String::isNotBlank)
            ?: UUID.randomUUID().toString().also {
                check(preferences.edit().putString(
                    KEY_INSTALLATION_ID,
                    SecureLocalData.seal(KEY_INSTALLATION_ID, it)
                ).commit()) { "安装标识未能安全保存" }
            }
    }

    fun credential(context: Context, accountId: String): PatientServerCredential? {
        val key = accountKey(accountId)
        val patientId = SecureLocalData.read(prefs(context), KEY_PATIENT_ID_PREFIX + key)
            ?.takeIf(String::isNotBlank) ?: return null
        val token = patientToken(context, accountId) ?: return null
        return PatientServerCredential(patientId, token)
    }

    // ── 阶段 C：激活码核销 ──────────────────────────────────────────────────

    /** 请求前先落盘：超时或进程退出后可以用同一组参数重试（设计 §4.3）。 */
    fun saveActivationPending(context: Context, pending: PendingActivationBinding): Boolean {
        val json = JSONObject()
            .put("requestId", pending.requestId)
            .put("activationCode", pending.activationCode)
            .put("proposedToken", pending.proposedToken)
            .put("installationId", pending.installationId)
            .put("accountId", pending.accountId)
        val key = KEY_ACTIVATION_PENDING_PREFIX + accountKey(pending.accountId)
        return prefs(context).edit()
            .putString(key, SecureLocalData.seal(key, json.toString()))
            .commit()
    }

    fun activationPending(context: Context, accountId: String): PendingActivationBinding? =
        SecureLocalData.read(prefs(context), KEY_ACTIVATION_PENDING_PREFIX + accountKey(accountId))
            ?.let { raw ->
                runCatching {
                    val json = JSONObject(raw)
                    PendingActivationBinding(
                        requestId = json.getString("requestId"),
                        activationCode = json.getString("activationCode"),
                        proposedToken = json.getString("proposedToken"),
                        installationId = json.getString("installationId"),
                        accountId = json.getString("accountId")
                    )
                }.getOrNull()
            }

    /**
     * 核销成功：一个事务式 edit 写入 patientId + PAT + 首张处方 + 脱敏身份，
     * 并清除待重试信息里的明文激活码。
     */
    fun saveActivatedCredential(
        context: Context,
        accountId: String,
        credential: PatientServerCredential,
        prescription: AdmissionPrescriptionItem,
        identity: BoundPatientIdentity
    ): Boolean {
        val key = accountKey(accountId)
        return prefs(context).edit()
            .putString(KEY_PATIENT_ID_PREFIX + key,
                SecureLocalData.seal(KEY_PATIENT_ID_PREFIX + key, credential.patientId))
            .putString(KEY_TOKEN_PREFIX + key,
                SecureLocalData.seal(KEY_TOKEN_PREFIX + key, credential.patientToken))
            .putString(KEY_PRESCRIPTION_PREFIX + key,
                SecureLocalData.seal(KEY_PRESCRIPTION_PREFIX + key, prescription.toJson().toString()))
            .putString(KEY_IDENTITY_PREFIX + key,
                SecureLocalData.seal(KEY_IDENTITY_PREFIX + key, identity.toJson().toString()))
            .remove(KEY_ACTIVATION_PENDING_PREFIX + key)
            .commit()
    }

    fun boundIdentity(context: Context, accountId: String): BoundPatientIdentity? =
        SecureLocalData.read(prefs(context), KEY_IDENTITY_PREFIX + accountKey(accountId))?.let { raw ->
            runCatching {
                val json = JSONObject(raw)
                BoundPatientIdentity(
                    displayName = json.getString("displayName"),
                    idCardMasked = json.getString("idCardMasked"),
                    doctorUsername = json.optString("doctorUsername")
                )
            }.getOrNull()
        }

    /**
     * 本地激活状态：凭据是否完整、是否需要输入激活码、是否损坏（设计 §5.3）。
     *
     * 状态优先级抽成纯函数，存储（SharedPreferences）只负责提供三个原始值：
     * 1) 完整 patientId + PAT → 已激活；2) 只有一半 → 损坏；3) 有待重试请求 → 重试；4) 否则未激活。
     */
    fun resolveActivationState(
        patientId: String?,
        patientToken: String?,
        pending: PendingActivationBinding?
    ): AccountActivationState {
        val id = patientId?.takeIf(String::isNotBlank)
        val token = patientToken?.takeIf(String::isNotBlank)
        return when {
            id != null && token != null ->
                AccountActivationState.Activated(PatientServerCredential(id, token))
            id != null || token != null -> AccountActivationState.Damaged
            pending != null -> AccountActivationState.Submitting(pending)
            else -> AccountActivationState.Unactivated
        }
    }

    fun activationState(context: Context, accountId: String): AccountActivationState {
        val key = accountKey(accountId)
        return resolveActivationState(
            patientId = SecureLocalData.read(prefs(context), KEY_PATIENT_ID_PREFIX + key),
            patientToken = patientToken(context, accountId),
            pending = activationPending(context, accountId)
        )
    }

    fun clearActivationPending(context: Context, accountId: String) {
        prefs(context).edit().remove(KEY_ACTIVATION_PENDING_PREFIX + accountKey(accountId)).apply()
    }

    fun prescription(context: Context, accountId: String): AdmissionPrescriptionItem? =
        SecureLocalData.read(prefs(context), KEY_PRESCRIPTION_PREFIX + accountKey(accountId))?.let { raw ->
            runCatching {
                val json = JSONObject(raw)
                AdmissionPrescriptionItem(
                    actionId = json.getString("actionId"),
                    actionName = json.getString("actionName"),
                    sets = json.getInt("sets"),
                    reps = json.getInt("reps"),
                    holdSeconds = json.getInt("holdSeconds")
                )
            }.getOrNull()
        }

    fun saveServerUrlOverride(context: Context, url: String?) {
        val normalized = url?.takeIf(String::isNotBlank)?.let {
            ServerOriginPolicy.normalize(it, BuildConfig.SERVER_BASE_URL, BuildConfig.DEBUG)
                ?: throw IllegalArgumentException("服务器地址必须是受信 Origin")
        }
        prefs(context).edit().apply {
            if (normalized == null) {
                remove(KEY_SERVER_URL)
            } else {
                putString(KEY_SERVER_URL, normalized)
            }
        }.apply()
    }

    fun serverUrlOverride(context: Context): String? {
        val preferences = prefs(context)
        val old = preferences.getString(KEY_SERVER_URL, null)?.takeIf(String::isNotBlank) ?: return null
        val normalized = ServerOriginPolicy.normalize(old, BuildConfig.SERVER_BASE_URL, BuildConfig.DEBUG)
        if (normalized == null) {
            preferences.edit().remove(KEY_SERVER_URL).commit()
            return null
        }
        if (normalized != old) preferences.edit().putString(KEY_SERVER_URL, normalized).apply()
        return normalized
    }

    private fun AdmissionPrescriptionItem.toJson(): JSONObject = JSONObject()
        .put("actionId", actionId)
        .put("actionName", actionName)
        .put("sets", sets)
        .put("reps", reps)
        .put("holdSeconds", holdSeconds)

    private fun BoundPatientIdentity.toJson(): JSONObject = JSONObject()
        .put("displayName", displayName)
        .put("idCardMasked", idCardMasked)
        .put("doctorUsername", doctorUsername)
}
