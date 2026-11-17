package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter

data class PainPoint(
    val bodyPart: String,
    val bodyPartLabel: String,
    val view: String,
    val painLevel: Int
)

data class DailyCheck(
    val prePainLevel: Int,
    val symptomChange: String,
    val painLocationChanged: Boolean,
    val allowedToTrain: Boolean,
    /**
     * 事件身份（评审 P1-3）：训练前问卷是「每次训练一条」的事件，两次答案相同 ≠ 重复。
     * 由问卷提交处填 ISO 时间戳；为空时同步层退回内容键（兼容演示/旧数据）。
     */
    val recordedAt: String = ""
)

data class TrainingAction(
    val actionId: String,
    val actionName: String,
    val arTaskType: String,
    val level: String,
    val sets: Int,
    val reps: Int,
    val holdSeconds: Int,
    val transitionSeconds: Int = 0,
    val demoFirst: Boolean = false,
    val difficultyOptionsSeconds: List<Int> = emptyList(),
    /**
     * 动作数据包的 content:// URI。非空时 AR 端按这份关键帧数据驱动教练，
     * 不使用内置的导入片段。由 MotionPackageStore.uriFor() 签发。
     */
    val motionPackageUri: String? = null
)

/** Built-in scenes keep the same immutable Unity identity contract, without downloaded files. */
data class LocalTrainingPrescriptionSnapshot(val scope: BindingScope, val order: DownloadedOrder) {
    fun toJson(): JSONObject = TrainingPrescriptionHandoff(
        scope, order, order.prescription.resourceManifestSha256, emptyList(), emptyList()
    ).toJson()
}

/** A stopped, replaced or withdrawn prescription cannot reuse a previously prepared local plan. */
internal fun isSameLocalTrainingOrder(prepared: DownloadedOrder, latest: TrainingDirectiveSnapshot): Boolean =
    latest.state == TrainingDirectiveState.Ready && latest.prescription?.let {
        it.orderId == prepared.orderId && it.resourceManifestSha256 == prepared.resourceManifestSha256
    } == true

data class TrainingStartPayload(
    val sessionId: String,
    val userId: String,
    val createdAt: String,
    val flowType: String,
    val accountStatus: String,
    val arTaskType: String,
    val arTaskSequence: List<String>,
    val imuCalibrationRequired: Boolean,
    val targetScene: String,
    val userProfile: UserProfile,
    val painPoints: List<PainPoint>,
    val dailyCheck: DailyCheck,
    val symptoms: List<String>,
    val goals: List<String>,
    val recommendedActions: List<TrainingAction>,
    val userTags: List<String>,
    /**
     * M7.1：生成本载荷所用的医生处方版本（Server `doctor_orders.id`）。
     * 只用于本机日志、AR 启动快照与排障，**不**在 M7.1 中推导“已执行”。
     */
    val prescriptionOrderId: Long? = null,
    /**
     * P4.5 交接事实：订单、清单、全部条目/剂量/资源身份与本地文件引用。
     *
     * 与 [recommendedActions] 并存：前者是 AR 场景要执行的动作，后者是“医生到底下了什么”，
     * 两者不一致时必须能在不查服务器的情况下发现。
     */
    val prescription: TrainingPrescriptionHandoff? = null,
    /** Local execution still carries the doctor's immutable order snapshot, without an asset URI. */
    val prescriptionSnapshot: LocalTrainingPrescriptionSnapshot? = null
) {
    fun toJson(): String {
        val root = JSONObject()
            .put("schemaVersion", "0.1")
            .put("sessionId", sessionId)
            .put("userId", userId)
            .put("createdAt", createdAt)
            .put("sourceApp", SpineFlowProtocol.MOBILE_PACKAGE)
            .put("flowType", flowType)
            .put("accountStatus", accountStatus)
            .put("arTaskType", arTaskType)
            .put("arTaskSequence", JSONArray(arTaskSequence))
            .put("imuCalibrationRequired", imuCalibrationRequired)
            .put("targetScene", targetScene)
            .put(
                "userProfile",
                JSONObject()
                    .put("gender", userProfile.gender.wireValue)
                    .put("ageYears", userProfile.ageYears)
                    .put("heightCm", userProfile.heightCm)
                    .put("weightKg", userProfile.weightKg)
            )
            .put(
                "assessment",
                JSONObject()
                    .put("painPoints", JSONArray().also { array ->
                        painPoints.forEach { point ->
                            array.put(
                                JSONObject()
                                    .put("bodyPart", point.bodyPart)
                                    .put("bodyPartLabel", point.bodyPartLabel)
                                    .put("view", point.view)
                                    .put("painLevel", point.painLevel)
                            )
                        }
                    })
                    .put(
                        "dailyCheck",
                        JSONObject()
                            .put("prePainLevel", dailyCheck.prePainLevel)
                            .put("symptomChange", dailyCheck.symptomChange)
                            .put("painLocationChanged", dailyCheck.painLocationChanged)
                            .put("allowedToTrain", dailyCheck.allowedToTrain)
                            // SF-08：发起训练时固化本份 pre 问卷的填写时刻。
                            // AR 结果返回后手机据此派生 pre:<recordedAt> 写入 session 上传，
                            // 与同步通道的问卷事件身份一致，服务器/医生端据此精确关联。
                            .apply {
                                dailyCheck.recordedAt.takeIf(String::isNotBlank)?.let {
                                    put("recordedAt", it)
                                }
                            }
                    )
                    .put("symptoms", JSONArray(symptoms))
                    .put("goals", JSONArray(goals))
            )
            .put(
                "trainingPlan",
                JSONObject()
                    // M7.1 §8.5：只读追踪字段，便于把 AR 结果与医生处方版本对账。
                    .apply {
                        prescriptionOrderId?.let { put("prescriptionOrderId", it) }
                    }
                    .put("recommendedActions", JSONArray().also { array ->
                        recommendedActions.forEach { action ->
                            array.put(
                                JSONObject()
                                    .put("actionId", action.actionId)
                                    .put("actionName", action.actionName)
                                    .put("arTaskType", action.arTaskType)
                                    .put("level", action.level)
                                    .put("sets", action.sets)
                                    .put("reps", action.reps)
                                    .put("holdSeconds", action.holdSeconds)
                                    .put("transitionSeconds", action.transitionSeconds)
                                    .put("demoFirst", action.demoFirst)
                                    .put(
                                        "difficultyOptionsSeconds",
                                        JSONArray(action.difficultyOptionsSeconds)
                                    )
                                    .apply {
                                        // 只在有数据包时写出该键，让 AR 端不必区分
                                        // 缺键与 JSON null。
                                        action.motionPackageUri?.let {
                                            put("motionPackageUri", it)
                                        }
                                    }
                            )
                        }
                    })
                    .put(
                        "userTags",
                        JSONArray(userTags)
                    )
                    .apply {
                        val identity = prescription?.toJson() ?: prescriptionSnapshot?.toJson()
                        identity?.let { put("prescription", it) }
                    }
            )

        return root.toString(2)
    }

    companion object {
        /**
         * Only the prescription fetched for this online training attempt may enter the patient flow.
         *
         * [handoff] 必须来自本次 prepare；生成载荷不代表 ready，消费者确认后才可开始训练。
         * [assetUri] 负责把已验证文件签成
         * AR 可读的 `content://`，签发失败同样视为能力缺口，不退回旧动作包。
         */
        /**
         * Built-in action path: carry only the doctor's order and dose metadata.
         * The Unity APK already contains the action scenes, so no motion package is downloaded.
         */
        fun forLocalPrescription(
            user: DemoUserData,
            flowType: DemoFlowType,
            patientId: String,
            order: DownloadedOrder,
            binding: BindingScope
        ): TrainingStartPayload {
            require(patientId.isNotBlank()) { "尚未完成患者身份绑定，请联系医生" }
            require(binding.patientId == patientId && binding.accountId == user.accountId) { "医院绑定与当前账号不一致" }
            require(order.items.size == 1) { "医生处方包含多个动作，手机端暂不支持顺序执行" }
            val item = requireNotNull(order.singleItem) { "医生处方参数不完整，请联系医生" }
            require(item.actionId in TrainingDirectiveParser.SUPPORTED_ACTION_IDS) { "医生处方包含手机端不支持的动作" }
            require(item.sets in 1..20 && item.reps in 1..100 && item.holdSeconds in 0..600) {
                "医生处方参数超出支持范围"
            }
            val dailyCheck = requireNotNull(user.latestPreTrainingCheck) {
                "请先完成训练前安全检查，再开始训练。"
            }
            require(dailyCheck.allowedToTrain) { "本次训练前安全检查未通过，暂时不能开始训练。" }
            require(user.latestPainPoints.none { it.painLevel >= MobileAdmissionAssessment.SEVERE_PAIN_THRESHOLD }) {
                "疼痛部位评分达到暂停训练阈值（${MobileAdmissionAssessment.SEVERE_PAIN_THRESHOLD}/10），不生成推荐动作。"
            }
            return create(
                user = user,
                flowType = flowType,
                imuCalibrationRequired = true,
                painPoints = user.latestPainPoints,
                dailyCheck = dailyCheck,
                actions = coachAndGameActions(
                    actionId = item.actionId,
                    displayName = item.actionName,
                    coachSets = item.sets,
                    coachReps = item.reps,
                    coachHoldSeconds = item.holdSeconds,
                    transitionSeconds = 2,
                    gameDifficultySeconds = ArTrainingDefaults.GAME_DIFFICULTY_OPTIONS_SECONDS,
                    motionPackageUri = null
                ),
                patientId = patientId,
                prescriptionOrderId = order.orderId,
                prescription = null,
                prescriptionSnapshot = LocalTrainingPrescriptionSnapshot(binding, order)
            )
        }

        fun forPrescription(
            user: DemoUserData,
            flowType: DemoFlowType,
            patientId: String,
            handoff: TrainingPrescriptionHandoff,
            assetUri: (File) -> String? = { null }
        ): TrainingStartPayload {
            val prescription = handoff.order
            require(patientId.isNotBlank()) { "尚未完成患者身份绑定，请联系医生" }
            require(prescription.orderId > 0) { "医生处方编号无效，请刷新处方" }
            require(handoff.complete && handoff.assets.size == prescription.items.size) {
                "处方资源尚未完成校验，请重新下拉处方后再开始训练。"
            }
            require(handoff.hasVerifiedFiles()) { "处方文件已变化，请重新获取资源" }
            // 这些 require 文案会经 Home 的「进入AR训练」失败提示直接呈现给患者，
            // 因此使用面向患者的中文说明（与下方疼痛阈值提示保持同一风格）。
            val dailyCheck = requireNotNull(user.latestPreTrainingCheck) {
                "请先完成训练前安全检查，再开始训练。"
            }
            require(dailyCheck.allowedToTrain) {
                "本次训练前安全检查未通过，暂时不能开始训练。"
            }
            require(
                user.latestPainPoints.none {
                    it.painLevel >= MobileAdmissionAssessment.SEVERE_PAIN_THRESHOLD
                }
            ) {
                "疼痛部位评分达到暂停训练阈值" +
                    "（${MobileAdmissionAssessment.SEVERE_PAIN_THRESHOLD}/10），不生成推荐动作。"
            }
            val actions = trainingActionsForPrescription(handoff, assetUri)
            return create(
                user = user,
                flowType = flowType,
                imuCalibrationRequired = true,
                painPoints = user.latestPainPoints,
                dailyCheck = dailyCheck,
                actions = actions,
                patientId = patientId,
                prescriptionOrderId = prescription.orderId,
                prescription = handoff
            )
        }

        /**
         * 执行能力缺口（面向患者）。返回空表示本机可以执行整份处方。
         *
         * 目前执行层一次只跑一个动作，且只认识 [TrainingDirectiveParser.SUPPORTED_ACTION_IDS]；
         * 新增能力前必须在这里显式放开，而不是让多动作处方悄悄只跑第一项。
         */
        fun capabilityGaps(order: DownloadedOrder): List<String> = buildList {
            if (order.items.size != 1) {
                add("医生处方包含多个动作，手机端暂不支持顺序执行，请联系医生调整。")
            }
            if (order.items.any { it.actionId !in TrainingDirectiveParser.SUPPORTED_ACTION_IDS }) {
                add("医生处方包含手机端不支持的动作")
            }
        }

        /**
         * 医生处方 → AR 动作。教练剂量取医生值；游戏保留现有单局与时长选择语义。
         *
         * 多动作处方在 P4 只是完整下发与保存，执行层仍是一次一个动作：这里明确报告能力缺口，
         * 不截取第一项，也不假装已支持多动作执行。
         */
        private fun trainingActionsForPrescription(
            handoff: TrainingPrescriptionHandoff,
            assetUri: (File) -> String?
        ): List<TrainingAction> {
            val order = handoff.order
            val gaps = capabilityGaps(order)
            require(gaps.isEmpty()) { gaps.first() }
            val item = requireNotNull(order.singleItem) { "医生处方参数不完整，请联系医生" }
            require(
                item.sets in 1..20 &&
                    item.reps in 1..100 &&
                    item.holdSeconds in 0..600
            ) { "医生处方参数超出支持范围" }
            // 冻结资源必须逐项对上：条目、版本、摘要、长度与清单一致才允许启动 AR。
            val asset = requireNotNull(handoff.assets.firstOrNull { it.item.itemId == item.itemId }) {
                "处方资源未完成校验，请重新下拉处方"
            }
            require(asset.item.asset.sha256 == item.asset.sha256 &&
                asset.byteCount == item.asset.fileSize &&
                asset.file.length() == item.asset.fileSize) {
                "处方资源与处方清单不一致，请重新下拉处方"
            }
            val uri = requireNotNull(assetUri(asset.file)) {
                "无法把处方资源交给 AR，请联系工作人员"
            }
            return coachAndGameActions(
                actionId = item.actionId,
                displayName = item.actionName,
                coachSets = item.sets,
                coachReps = item.reps,
                coachHoldSeconds = item.holdSeconds,
                transitionSeconds = 2,
                gameDifficultySeconds = ArTrainingDefaults.GAME_DIFFICULTY_OPTIONS_SECONDS,
                motionPackageUri = uri
            )
        }

        private fun coachAndGameActions(
            actionId: String,
            displayName: String,
            coachSets: Int,
            coachReps: Int,
            coachHoldSeconds: Int,
            transitionSeconds: Int,
            gameDifficultySeconds: List<Int>,
            motionPackageUri: String?
        ): List<TrainingAction> = listOf(
            TrainingAction(
                actionId = "${actionId}_coach",
                actionName = "${displayName}（教练模式）",
                arTaskType = ArTaskType.CoachTraining.wireValue,
                level = "recommended",
                sets = coachSets,
                reps = coachReps,
                holdSeconds = coachHoldSeconds,
                transitionSeconds = transitionSeconds,
                demoFirst = true,
                motionPackageUri = motionPackageUri
            ),
            TrainingAction(
                actionId = "${actionId}_game",
                actionName = "${displayName}（游戏模式）",
                arTaskType = ArTaskType.GameTraining.wireValue,
                level = "user_selected_duration",
                // 保持原有游戏动作的一局入口语义；游戏时长由用户在 AR 端选择。
                sets = 1,
                reps = 1,
                holdSeconds = 0,
                transitionSeconds = transitionSeconds,
                difficultyOptionsSeconds = gameDifficultySeconds,
                motionPackageUri = motionPackageUri
            )
        )

        private fun create(
            user: DemoUserData,
            flowType: DemoFlowType,
            imuCalibrationRequired: Boolean,
            painPoints: List<PainPoint>,
            dailyCheck: DailyCheck,
            actions: List<TrainingAction>,
            patientId: String,
            prescriptionOrderId: Long,
            prescription: TrainingPrescriptionHandoff?,
            prescriptionSnapshot: LocalTrainingPrescriptionSnapshot? = null
        ): TrainingStartPayload {
            val now = OffsetDateTime.now()
            val userId = patientId
            val profile = requireNotNull(user.profile) {
                "请先完善基础档案（性别/年龄/身高/体重）。"
            }
            require(profile.gender != UserGender.Unspecified) {
                "请先在基础档案中选择性别。"
            }
            require(profile.isCompleteForTraining()) {
                "基础档案不完整或数值不合理，请检查性别/年龄/身高/体重。"
            }
            return TrainingStartPayload(
                sessionId = "${DateTimeFormatter.ofPattern("yyyyMMdd_HHmmss").format(now)}_$userId",
                userId = userId,
                createdAt = now.format(DateTimeFormatter.ISO_OFFSET_DATE_TIME),
                flowType = flowType.wireValue,
                accountStatus = flowType.accountStatus,
                arTaskType = flowType.primaryArTask().wireValue,
                arTaskSequence = flowType.arTaskSequence().map { it.wireValue },
                imuCalibrationRequired = imuCalibrationRequired,
                targetScene = trainingTargetScene(profile, actions),
                userProfile = profile,
                painPoints = painPoints,
                dailyCheck = dailyCheck,
                symptoms = user.admissionIntake?.symptom
                    ?.takeIf(String::isNotBlank)
                    ?.let(::listOf)
                    ?: listOf("low_back_pain"),
                goals = user.admissionIntake?.goals
                    ?.takeIf { it.isNotEmpty() }
                    ?: listOf("core_stability", "move_safely"),
                recommendedActions = actions,
                userTags = user.userTags,
                prescriptionOrderId = prescriptionOrderId,
                prescription = prescription,
                prescriptionSnapshot = prescriptionSnapshot
            )
        }

        private fun trainingTargetScene(
            profile: UserProfile,
            actions: List<TrainingAction>
        ): String {
            // Resolve the exercise before selecting any avatar-specific scene;
            // a male Bird Dog plan must never be replaced by a Dead Bug route.
            val exercise = requireNotNull(
                RecommendedExercise.fromActionIds(actions.map(TrainingAction::actionId))
            ) {
                "训练动作无法匹配到支持的 AR 场景，暂时无法生成训练计划。"
            }
            return if (profile.gender == UserGender.Male &&
                exercise == RecommendedExercise.DeadBug
            ) {
                SpineFlowProtocol.MALE_DEAD_BUG_PRACTICE_TARGET_SCENE
            } else {
                exercise.targetScene
            }
        }

    }
}
