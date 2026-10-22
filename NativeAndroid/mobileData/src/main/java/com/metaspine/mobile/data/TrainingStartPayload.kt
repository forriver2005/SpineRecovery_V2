package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
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
    val prescriptionOrderId: Long? = null
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
            )

        return root.toString(2)
    }

    companion object {
        /** Only the prescription fetched for this online training attempt may enter the patient flow. */
        fun forPrescription(
            user: DemoUserData,
            flowType: DemoFlowType,
            patientId: String,
            prescription: DownloadedOrder
        ): TrainingStartPayload {
            require(patientId.isNotBlank()) { "尚未完成患者身份绑定，请联系医生" }
            require(prescription.orderId > 0) { "医生处方编号无效，请刷新处方" }
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
            val actions = trainingActionsForPrescription(prescription)
            return create(
                user = user,
                flowType = flowType,
                imuCalibrationRequired = true,
                painPoints = user.latestPainPoints,
                dailyCheck = dailyCheck,
                actions = actions,
                patientId = patientId,
                prescriptionOrderId = prescription.orderId
            )
        }

        /**
         * 医生处方 → AR 动作。单动作处方展开为现有教练/游戏两个动作；剂量全部取医生值。
         *
         * 多动作处方在 P4 只是完整下发与保存，执行层仍是一次一个动作：这里明确报告能力缺口，
         * 不截取第一项，也不假装已支持多动作执行。
         */
        private fun trainingActionsForPrescription(
            order: DownloadedOrder
        ): List<TrainingAction> {
            val item = requireNotNull(order.singleItem) {
                "医生处方包含多个动作，手机端暂不支持顺序执行，请联系医生调整。"
            }
            require(order.items.all { it.actionId in TrainingDirectiveParser.SUPPORTED_ACTION_IDS }) {
                "医生处方包含手机端不支持的动作"
            }
            require(
                item.sets in 1..20 &&
                    item.reps in 1..100 &&
                    item.holdSeconds in 0..600
            ) { "医生处方参数超出支持范围" }
            return coachAndGameActions(
                actionId = item.actionId,
                displayName = item.actionName,
                coachSets = item.sets,
                coachReps = item.reps,
                coachHoldSeconds = item.holdSeconds,
                transitionSeconds = 2,
                gameDifficultySeconds = ArTrainingDefaults.GAME_DIFFICULTY_OPTIONS_SECONDS
            )
        }

        private fun coachAndGameActions(
            actionId: String,
            displayName: String,
            coachSets: Int,
            coachReps: Int,
            coachHoldSeconds: Int,
            transitionSeconds: Int,
            gameDifficultySeconds: List<Int>
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
                demoFirst = true
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
                difficultyOptionsSeconds = gameDifficultySeconds
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
            prescriptionOrderId: Long
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
                prescriptionOrderId = prescriptionOrderId
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
