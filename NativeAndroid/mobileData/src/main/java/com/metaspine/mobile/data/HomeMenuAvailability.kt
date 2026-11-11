package com.metaspine.mobile.data

data class HomeMenuAvailability(
    val imuCalibrationEnabled: Boolean,
    val arTrainingEnabled: Boolean,
    val historyEnabled: Boolean,
    val logoutEnabled: Boolean
)

fun resolveHomeMenuAvailability(
    imuCalibrationState: ImuCalibrationState,
    hasTrainingData: Boolean
): HomeMenuAvailability {
    return HomeMenuAvailability(
        // Recalibration remains available after a successful calibration.
        imuCalibrationEnabled = true,
        arTrainingEnabled = imuCalibrationState == ImuCalibrationState.Calibrated,
        // History is only useful after at least one valid completed training session.
        historyEnabled = hasTrainingData,
        logoutEnabled = true
    )
}
