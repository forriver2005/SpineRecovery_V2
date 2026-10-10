package com.metaspine.mobile.data

enum class AdmissionMovementType(val wireValue: String) {
    Flexion("fis"),
    Extension("eis")
}

enum class AdmissionPainResponse(val wireValue: String) {
    Relieved("relieved"),
    Unchanged("unchanged"),
    Worsened("worsened")
}

enum class AdmissionPainPhase(val wireValue: String) {
    Start("start"),
    Middle("middle"),
    End("end"),
    PainFree("pain_free")
}

data class AdmissionMovementResult(
    val movementType: AdmissionMovementType,
    val completionCount: Int,
    val painResponse: AdmissionPainResponse,
    val firstPainPhase: AdmissionPainPhase?,
    val painLevel: Int
)

data class MobileAdmissionAssessment(
    val flexion: AdmissionMovementResult,
    val extension: AdmissionMovementResult,
    val completedAt: String
) {
    /**
     * Product safety screening only. These values are configurable product
     * thresholds and must not be interpreted as a medical diagnosis.
     */
    fun clinicalReviewReasons(): List<String> = buildList {
        val maximumPainLevel = maxOf(flexion.painLevel, extension.painLevel)
        if (maximumPainLevel >= SEVERE_PAIN_THRESHOLD) {
            add("动作评估后的疼痛评分达到暂停训练阈值（$SEVERE_PAIN_THRESHOLD/10）")
        }
    }

    fun requiresClinicalReview(): Boolean = clinicalReviewReasons().isNotEmpty()

    fun directionPreferenceTag(): String? = when {
        extension.painResponse == AdmissionPainResponse.Relieved &&
            flexion.painResponse != AdmissionPainResponse.Relieved -> "伸展方向偏好"

        flexion.painResponse == AdmissionPainResponse.Relieved &&
            extension.painResponse != AdmissionPainResponse.Relieved -> "屈曲方向偏好"

        else -> null
    }

    fun hasHighPain(): Boolean = maxOf(flexion.painLevel, extension.painLevel) >= 7

    companion object {
        const val PLANNED_COMPLETION_COUNT = 10
        const val MIN_COMPLETION_COUNT = 5
        const val SEVERE_PAIN_THRESHOLD = 8
    }
}
