package com.metaspine.mobile.data

enum class RecommendedExercise(
    val actionIdPrefix: String,
    val displayName: String,
    val targetScene: String
) {
    DeadBug(
        actionIdPrefix = "dead_bug",
        displayName = "死虫式",
        targetScene = SpineFlowProtocol.DEAD_BUG_PRACTICE_TARGET_SCENE
    ),
    BirdDog(
        actionIdPrefix = "bird_dog",
        displayName = "鸟狗式",
        targetScene = SpineFlowProtocol.BIRD_DOG_PRACTICE_TARGET_SCENE
    ),

    /**
     * 联调用：由一个现实中不会出现的疼痛组合触发，进入数据驱动的统一练习场景。
     * 仅为旧会话的路由兼容保留；正式处方工厂不生成此动作。
     */
    MotionPackageProbe(
        actionIdPrefix = "motion_package_probe",
        displayName = "动作包联调",
        targetScene = SpineFlowProtocol.UNIFIED_PRACTICE_TARGET_SCENE
    );

    companion object {
        fun fromActionId(actionId: String): RecommendedExercise? {
            val normalized = actionId.trim().lowercase()
            return entries.firstOrNull { exercise ->
                normalized == exercise.actionIdPrefix ||
                    normalized.startsWith("${exercise.actionIdPrefix}_")
            }
        }

        fun fromActionIds(actionIds: Collection<String>): RecommendedExercise? {
            if (actionIds.isEmpty()) return null
            return actionIds
                .map { actionId -> fromActionId(actionId) ?: return null }
                .distinct()
                .singleOrNull()
        }
    }
}

/**
 * 由「左大臂 + 右腿」这一对疼痛部位产生，见
 * [com.metaspine.mobile.data.DemoUserData.userTags]。
 */
const val MOTION_PACKAGE_PROBE_TAG = "动作包联调哨兵"
