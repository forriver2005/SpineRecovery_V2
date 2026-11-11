package com.metaspine.mobile.data

enum class DemoFlowType(
    val wireValue: String,
    val accountStatus: String
) {
    FirstAdmission(
        wireValue = "first_admission",
        accountStatus = "new_user"
    ),
    ReturningUser(
        wireValue = "returning_user",
        accountStatus = "returning_user"
    )
}

enum class ArTaskType(
    val wireValue: String
) {
    AdmissionAssessment("admission_assessment"),
    ThresholdTest("threshold_test"),
    CoachTraining("coach_training"),
    GameTraining("game_training")
}

fun DemoFlowType.primaryArTask(): ArTaskType {
    return when (this) {
        DemoFlowType.FirstAdmission -> ArTaskType.CoachTraining
        DemoFlowType.ReturningUser -> ArTaskType.CoachTraining
    }
}

fun DemoFlowType.arTaskSequence(): List<ArTaskType> {
    return when (this) {
        DemoFlowType.FirstAdmission -> listOf(
            ArTaskType.CoachTraining,
            ArTaskType.GameTraining
        )
        DemoFlowType.ReturningUser -> listOf(
            ArTaskType.CoachTraining,
            ArTaskType.GameTraining
        )
    }
}
