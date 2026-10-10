package com.metaspine.mobile.data

/** Pure validation for the explicit callback sent by the MetaSpine SlimeVR build. */
object ImuCalibrationReturnPolicy {
    fun shouldResumeCalibration(action: String?, calibrationReturned: Boolean): Boolean =
        action == SpineFlowProtocol.ACTION_IMU_CALIBRATION_RETURN && calibrationReturned
}
