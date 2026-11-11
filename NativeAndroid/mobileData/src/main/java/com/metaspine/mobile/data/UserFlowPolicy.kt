package com.metaspine.mobile.data

import org.json.JSONObject

enum class UserEntryDestination {
    BasicProfile,
    BasicSurvey,
    PainSelect,
    MobileAdmission,
    AdmissionDecision,
    Home,
    PreTraining
}

/**
 * 登录后的落点（设计 §5.3、§9.3「登录路由先判断激活，再判断档案/首次评估生命周期」）。
 *
 * 这里的判定必须保持纯函数：MainActivity 只负责把状态读出来再执行跳转，
 * 否则「未激活账号能不能绕过激活页」就只能靠人工点一遍发现。
 */
sealed interface LoginDestination {
    /** 需要输入激活码（未激活或上次绑定未确认结果）。 */
    data object Activation : LoginDestination

    /** 只有 patientId 或只有 PAT：损坏状态，不开放训练，提示联系工作人员。 */
    data object BrokenCredential : LoginDestination

    /** 已激活但缺训练前安全检查：先补齐才能进 Home。 */
    data object PreTrainingCheck : LoginDestination

    /** 已激活的首次准入患者：直接进入日常流程。 */
    data object Home : LoginDestination

    /** 按档案/首次评估进度继续。 */
    data class Flow(val destination: UserEntryDestination) : LoginDestination
}

object UserFlowPolicy {
    /**
     * M6.1 修复：已获医生批准（持正式凭据）的首次准入患者，若缺「训练前安全检查」
     * （`latestPreTrainingCheck`），必须先进入 PreTraining 补齐，不能直接进 Home——
     * 否则 Home 的「进入AR训练」会因缺少检查而失败。
     *
     * 该状态真实存在：等待审核页退出/登录会经 `clearTemporaryTrainingData` 清掉检查，
     * 而正式凭据存在另一个 SharedPreferences 文件里会被保留；`new*` 演示账号的
     * 一次性重置也只清用户数据、不清凭据。
     */
    fun approvedFirstAdmissionNeedsPreTrainingCheck(
        user: DemoUserData,
        hasServerCredential: Boolean
    ): Boolean = user.lifecycle == UserLifecycle.AdmissionPending &&
        hasServerCredential &&
        user.latestPreTrainingCheck == null

    fun admissionIntakeBlockReasons(intake: AdmissionIntake): List<String> = buildList {
        if (intake.situation == "acute") {
            add("当前处于急性期发作，暂不建议进入训练")
        }
        if (intake.symptom == "无力") {
            add("当前存在无力症状，建议先咨询专业人员")
        }
    }

    fun entryDestination(user: DemoUserData): UserEntryDestination {
        if (user.profile?.isCompleteForTraining() != true) {
            return UserEntryDestination.BasicProfile
        }
        if (user.lifecycle == UserLifecycle.ReturningUser) {
            return UserEntryDestination.PreTraining
        }
        // AdmissionPending：按准入流程逐步路由；无论之前有无旧评估数据，
        // 始终要求完成手机端评估后才能升级，避免账号被永久锁死。
        if (user.admissionIntake == null || user.latestPreTrainingCheck == null) {
            return UserEntryDestination.BasicSurvey
        }
        if (user.latestPainPoints.isEmpty()) {
            return UserEntryDestination.PainSelect
        }
        return UserEntryDestination.MobileAdmission
    }

    /**
     * 已激活账号的落点（设计 §4.2、§9.3）。
     *
     * 激活发生在填写资料之前，因此「有 PAT」不再等于「已走完初始评估」：
     * 必须先按资料/问卷/疼痛/动作评估的顺序补齐，只有初始评估确实完成过的账号
     * （改造前已批准、后来训练前检查被清掉）才走「先补检查」这条 M6.1 老路径。
     */
    fun activatedDestination(user: DemoUserData): LoginDestination {
        if (user.profile?.isCompleteForTraining() != true) {
            return LoginDestination.Flow(UserEntryDestination.BasicProfile)
        }
        if (user.lifecycle == UserLifecycle.ReturningUser) {
            return LoginDestination.Flow(entryDestination(user))
        }
        val initialAssessmentCompleted = user.admissionIntake != null &&
            user.latestPainPoints.isNotEmpty() &&
            user.mobileAdmissionAssessment != null
        return when {
            !initialAssessmentCompleted -> LoginDestination.Flow(entryDestination(user))
            user.latestPreTrainingCheck == null -> LoginDestination.PreTrainingCheck
            else -> LoginDestination.Home
        }
    }

    /**
     * 阶段 C 登录路由：**先判断激活，再判断档案/首次评估生命周期**。
     *
     * 旧「提交申请 + 等待医生审核」路径已随阶段 C 移除：没有正式凭据的账号一律进入激活页，
     * 改造前遗留的待审申请不再由手机端查询（由医生建档发码后重新激活）。
     *
     * @param activation 本地激活状态（未激活 / 待重试 / 已激活 / 损坏）
     */
    fun loginDestination(
        user: DemoUserData,
        activation: AccountActivationState
    ): LoginDestination = when (activation) {
        is AccountActivationState.Damaged -> LoginDestination.BrokenCredential
        is AccountActivationState.Activated -> activatedDestination(user)
        AccountActivationState.Unactivated,
        is AccountActivationState.Submitting -> LoginDestination.Activation
    }

    fun lifecycleAfterTraining(
        current: UserLifecycle,
        flowType: String?,
        completed: Boolean,
        requiresClinicalReview: Boolean = false
    ): UserLifecycle {
        if (flowType != DemoFlowType.FirstAdmission.wireValue) return current
        return if (completed && !requiresClinicalReview) {
            UserLifecycle.ReturningUser
        } else {
            UserLifecycle.AdmissionPending
        }
    }

    /** Repairs records written by the old no-details completion path. */
    fun repairPersistedLifecycle(user: DemoUserData): DemoUserData {
        if (user.lifecycle != UserLifecycle.AdmissionPending ||
            user.profile == null ||
            user.admissionIntake == null
        ) {
            return user
        }
        val hasConfirmedLegacyCompletion = user.history.any { record ->
            runCatching {
                val result = JSONObject(record.resultJson)
                result.optString("detailStatus") == "AR_DETAILS_UNAVAILABLE" &&
                    result.optBoolean("completed", false) &&
                    result.optBoolean("userConfirmedCompleted", false)
            }.getOrDefault(false)
        }
        return if (hasConfirmedLegacyCompletion) {
            user.copy(lifecycle = UserLifecycle.ReturningUser)
        } else {
            user
        }
    }
}
