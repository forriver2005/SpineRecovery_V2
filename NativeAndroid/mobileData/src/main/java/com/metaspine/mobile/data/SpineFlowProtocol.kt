package com.metaspine.mobile.data

object SpineFlowProtocol {
    const val MOBILE_PACKAGE = "com.metaspine.mobile"
    const val ADMIN_PACKAGE = "com.metaspine.mobile.admin"
    const val SLIMEVR_PACKAGE = "dev.slimevr.server.android"
    const val ACTION_AR_SESSION_RESULT = "com.metaspine.action.AR_SESSION_RESULT"
    const val ACTION_IMU_CALIBRATION_RETURN = "com.metaspine.action.IMU_CALIBRATION_RETURN"
    // Legacy administrator test actions retained for the embedded original
    // SpineMobileAdmin test screen. The production AR session flow below uses
    // ACTION_AR_SESSION_RESULT and the versioned START_SESSION contract.
    const val ACTION_START_TRAINING = "com.metaspine.ar.START_TRAINING"
    const val ACTION_ADMIN_START_TEST = "com.metaspine.ar.ADMIN_START_TEST"
    const val ACTION_ADMIN_TEST_RESULT = "com.metaspine.mobile.ADMIN_TEST_RESULT"
    const val ACTION_ADMIN_RECORD_MOTION = "com.metaspine.ar.ADMIN_RECORD_MOTION"
    const val ACTION_IMPORT_ADMIN_CONFIGURATION =
        "com.metaspine.mobile.action.IMPORT_ADMIN_CONFIGURATION"
    const val ADMIN_CONFIG_IMPORT_PERMISSION =
        "com.metaspine.mobile.permission.IMPORT_ADMIN_CONFIGURATION"
    const val MESSAGE_ADMIN_CONFIG_PUBLICATION = "ADMIN_CONFIG_PUBLICATION"
    const val MESSAGE_ADMIN_TEST = "ADMIN_TEST"
    const val MESSAGE_ADMIN_TEST_RESULT = "ADMIN_TEST_RESULT"
    const val EXTRA_SLIMEVR_RETURN_PACKAGE = "spineflow_return_package"
    const val EXTRA_SLIMEVR_RETURN_ACTION = "spineflow_return_action"
    const val EXTRA_SLIMEVR_CALIBRATION_RETURNED = "spineflow_calibration_returned"
    const val EXTRA_PAYLOAD_JSON = "spineflow_payload_json"
    const val EXTRA_PROTOCOL_VERSION = "spineflow_protocol_version"
    const val PROTOCOL_VERSION = 1
    const val MESSAGE_START_SESSION = "START_SESSION"
    const val MESSAGE_SESSION_RESULT = "SESSION_RESULT"
    const val SESSION_KIND_TRAINING = "TRAINING"
    const val SESSION_KIND_ADMIN_CONFIG_TEST = "ADMIN_CONFIG_TEST"
    const val FLOW_FIRST_ADMISSION = "first_admission"
    const val FLOW_RETURNING_USER = "returning_user"
    const val FLOW_ADMIN_CONFIG_TEST = "admin_config_test"
    const val TASK_ADMIN_CONFIG_TEST = "admin_config_test"
    const val STATUS_COMPLETED = "COMPLETED"
    const val STATUS_INTERRUPTED = "INTERRUPTED"
    const val STATUS_FAILED = "FAILED"
    const val STATUS_CANCELLED = "CANCELLED"
    const val END_NORMAL_COMPLETION = "NORMAL_COMPLETION"
    const val END_HEADSET_DISCONNECTED = "HEADSET_DISCONNECTED"
    const val END_USER_EXIT = "USER_EXIT"
    const val END_AR_APP_ERROR = "AR_APP_ERROR"
    const val END_INVALID_REQUEST = "INVALID_REQUEST"
    const val DATA_ORIGIN_POSE_MEASUREMENT_V2 = "ar_pose_measurement_v2"
    const val DATA_ORIGIN_SCORE_SUMMARY_V1 = "ar_score_summary_v1"
    const val DATA_ORIGIN_GAME_SESSION_V1 = "ar_game_session_v1"
    // V1 does not expose the legacy Unity Home scene.
    const val FIRST_TARGET_SCENE = "DeadBugPractice"

    /** 日常训练由推荐动作决定首次进入的教练场景。 */
    const val DEAD_BUG_PRACTICE_TARGET_SCENE = "DeadBugPractice"
    const val MALE_DEAD_BUG_PRACTICE_TARGET_SCENE = "maleDeadBugPractice"
    const val BIRD_DOG_PRACTICE_TARGET_SCENE = "BirdDogPractice"

    /** 数据驱动场景：动作由下发的动作包决定，而非场景内导入的动画。 */
    const val UNIFIED_PRACTICE_TARGET_SCENE = "testPractice"
}
