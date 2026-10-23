package com.metaspine.mobile.data

import android.content.Context
import com.metaspine.mobile.ar.session.ArSessionRepository
import kotlinx.coroutines.CancellationException

sealed interface AccountLoginResult {
    data object Allowed : AccountLoginResult
    data class Blocked(val message: String) : AccountLoginResult
}

/** Shared by password login and session restoration. No local user is loaded before this check. */
class AccountLoginGuard(
    private val context: Context,
    private val userStore: DemoUserStore,
    private val forgetSync: (String) -> Unit,
    private val checkServer: suspend (PatientServerCredential, String, String) -> PatientAccountStatus =
        PatientAccountClient::check
) {
    suspend fun verify(accountId: String): AccountLoginResult {
        val id = accountId.trim().lowercase()
        return try {
            if (userStore.isDeleted(id)) {
                removeAccount(id)
                return AccountLoginResult.Blocked(DELETED_MESSAGE)
            }
            when (val state = PatientCredentials.activationState(context, id)) {
                AccountActivationState.Unactivated, is AccountActivationState.Submitting -> AccountLoginResult.Allowed
                AccountActivationState.Damaged -> AccountLoginResult.Blocked("账号绑定信息不完整，请联系工作人员处理")
                is AccountActivationState.Activated -> {
                    val origin = SpineFlowServerConfig.BASE_URL
                    val scopeAtRequest = BindingScopeStore.load(context, id)
                        ?: return AccountLoginResult.Blocked("医院绑定信息缺失，请恢复原医院绑定")
                    if (!scopeAtRequest.matchesOriginAndPatient(origin, state.credential.patientId)) {
                        return AccountLoginResult.Blocked("当前服务器不是账号绑定医院，本地数据已保留")
                    }
                    val hospital = ServiceIdentityClient.fetch().getOrThrow()
                    if (!scopeAtRequest.matchesHospital(hospital) || SpineFlowServerConfig.BASE_URL != origin) {
                        return AccountLoginResult.Blocked("医院身份不匹配，本地数据已保留")
                    }
                    val status = checkServer(state.credential, id, PatientCredentials.installationId(context))
                    // A late response cannot authorize deletion/login after another binding replaces this one.
                    if (PatientCredentials.credential(context, id) != state.credential ||
                        SpineFlowServerConfig.BASE_URL != origin ||
                        BindingScopeStore.load(context, id) != scopeAtRequest) {
                        return AccountLoginResult.Blocked("账号绑定已变化，请重新登录")
                    }
                    when (status) {
                        PatientAccountStatus.Active -> AccountLoginResult.Allowed
                        PatientAccountStatus.Missing -> {
                            removeAccount(id)
                            AccountLoginResult.Blocked(DELETED_MESSAGE)
                        }
                        PatientAccountStatus.Rejected -> AccountLoginResult.Blocked("服务器账号校验未通过，请联系医生或工作人员；本地数据已保留")
                        PatientAccountStatus.Unavailable -> AccountLoginResult.Blocked("无法完成服务器账号校验，请联网后重试；本地数据已保留")
                    }
                }
            }
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (_: Exception) {
            AccountLoginResult.Blocked("账号校验或清理未能完成，请重启后重试；当前无法登录")
        }
    }

    fun recoverPendingDeletions() {
        userStore.pendingAccountDeletions().forEach(::removeAccount)
    }

    private fun removeAccount(accountId: String) {
        userStore.beginAccountDeletion(accountId)
        forgetSync(accountId)
        // Read the identity before removing its credential. Shared AR/session caches are ownership checked.
        val patientId = PatientCredentials.credential(context, accountId)?.patientId
        val ownedIds = setOfNotNull(patientId, "demo_$accountId")
        val ar = ArSessionRepository(context)
        if (ar.current()?.userId in ownedIds) ar.clear()
        val phone = PhoneSessionStore(context)
        val startUserId = runCatching {
            org.json.JSONObject(phone.latestTrainingStartJson().orEmpty()).optString("userId")
        }.getOrNull()
        if (startUserId in ownedIds) phone.clearTemporaryTrainingData()
        TrainingDirectiveStore(context).clear(accountId)
        RoutineQuestionnaireStore(context).use { it.clearAccount(accountId) }
        BindingScopeStore.clear(context, accountId)
        PatientCredentials.clearAccount(context, accountId)
        userStore.finishAccountDeletion(accountId)
    }

    companion object {
        const val DELETED_MESSAGE = "服务器患者账号已不存在，对应本地账号已删除，请联系医生重新建档并注册"
    }
}
