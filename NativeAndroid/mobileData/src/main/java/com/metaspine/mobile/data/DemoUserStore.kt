package com.metaspine.mobile.data

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter

enum class UserLifecycle {
    AdmissionPending,
    ReturningUser
}

enum class ImuCalibrationState {
    NotCalibrated,
    Calibrated
}

enum class UserGender(val wireValue: String) {
    Male("male"),
    Female("female"),
    Unspecified("unspecified");

    companion object {
        fun fromWireValue(value: String): UserGender = entries.firstOrNull {
            it.wireValue == value
        } ?: Unspecified
    }
}

data class UserProfile(
    val heightCm: Int,
    val weightKg: Int,
    val gender: UserGender = UserGender.Unspecified,
    val ageYears: Int = 0
) {
    fun isCompleteForTraining(): Boolean =
        heightCm in 120..220 &&
            weightKg in 35..200 &&
            ageYears in 10..100 &&
            gender != UserGender.Unspecified
}

data class AdmissionSurvey(
    val painLevel: Int,
    val requiresClinicalReview: Boolean
)

data class AdmissionIntake(
    val situation: String,
    val symptom: String,
    val goals: List<String>
)

data class PostTrainingFeedback(
    val feeling: String,
    val difficulty: String,
    val recordedAt: String = ""
)

enum class PainRecordSource {
    AdmissionQuestionnaire,
    AdmissionActionQuestionnaire,
    DailyQuestionnaire
}

data class PainHistoryRecord(
    val id: String,
    val recordedAt: String,
    val painLevel: Int,
    val source: PainRecordSource,
    val painPoints: List<PainPoint>,
    val painParts: List<String> = painPoints.map { it.bodyPart },
    val pointsCaptured: Boolean = painPoints.isNotEmpty(),
    val painFree: Boolean = painLevel == 0
)

data class TrainingHistoryRecord(
    val sessionId: String,
    val completedAt: String,
    val resultJson: String,
    val feedback: PostTrainingFeedback?,
    /**
     * SF-08：本次训练所依据的训练前问卷事件 id（pre:<recordedAt>，与同步通道同一身份）。
     * 空串 = 该记录无已确认的 pre 关联（旧数据 / 未填写），医生端据此显示「未确认」。
     */
    val preTrainingEventId: String = "",
    val planJson: String = "",
    val feedbackPending: Boolean = false,
    val endReason: String = "",
    val replaySessionId: String = "",
    val networkResultJson: String = "",
    val networkResultError: String = "",
    val actionId: String = ""
)

data class RememberedLogin(
    val accountId: String,
    val password: String
)

enum class AccountRegistrationResult {
    Success,
    AccountAlreadyExists,
    InvalidInput,
    StorageFailure
}

data class DemoUserData(
    val accountId: String,
    val lifecycle: UserLifecycle,
    val profile: UserProfile?,
    val admissionSurvey: AdmissionSurvey?,
    val admissionIntake: AdmissionIntake? = null,
    val latestPreTrainingCheck: DailyCheck?,
    val latestPainPoints: List<PainPoint>,
    val imuCalibrationState: ImuCalibrationState,
    val history: List<TrainingHistoryRecord>,
    val painHistory: List<PainHistoryRecord> = emptyList(),
    val mobileAdmissionAssessment: MobileAdmissionAssessment? = null,
    val healthEventsJson: String = "[]",
    val monthlyHistoryJson: String = "[]",
    val trackingCalibratedAt: String = "",
    val syncBindingKey: String = "",
    val syncBaselineJson: String = "{}",
    val intakeReviewRequired: Boolean = admissionSurvey?.requiresClinicalReview ?: false
) {
    val isReturningUser: Boolean
        get() = lifecycle == UserLifecycle.ReturningUser

    val userTags: List<String>
        get() = buildList {
            add(if (isReturningUser) "老用户" else "新用户")
            mobileAdmissionAssessment?.directionPreferenceTag()?.let(::add)
            if (mobileAdmissionAssessment?.hasHighPain() == true) add("准入疼痛较高")
            addRegionPainTag(
                regionLabel = "腰部",
                aliases = listOf("waist", "low_back", "lumbar", "腰")
            )
            addRegionPainTag(
                regionLabel = "臀部",
                aliases = listOf("hip", "glute", "臀")
            )
            addRegionPainTag(
                regionLabel = "腿部",
                aliases = listOf("leg", "腿")
            )
            addRegionPainTag(
                regionLabel = "臂部",
                aliases = listOf("upper_arm", "arm", "臂")
            )
            addMotionPackageProbeTag()
        }

    /**
     * 联调哨兵：疼痛部位恰好是「左大臂 + 右腿」这两处时打标签。
     *
     * 上肢一侧配下肢对侧、且不涉及腰臀，真实处方不会这么开，所以拿它做进入
     * 统一练习场景的开关不会挤占正常 case。要求恰好两处、不多不少，避免真实
     * 用户多选时被误判。区域标签只有「臂部/腿部」粒度，分不出左右，因此这里
     * 直接看部位 key。
     */
    private fun MutableList<String>.addMotionPackageProbeTag() {
        val selectedParts = latestPainPoints
            .filter { it.painLevel > 0 }
            .map { it.bodyPart.trim().lowercase() }
            .toSet()
        if (selectedParts == setOf("left_upper_arm", "right_leg")) {
            add(MOTION_PACKAGE_PROBE_TAG)
        }
    }

    private fun MutableList<String>.addRegionPainTag(
        regionLabel: String,
        aliases: List<String>
    ) {
        val regionPainLevel = latestPainPoints
            .filter { point ->
                val searchableText = "${point.bodyPart} ${point.bodyPartLabel}".lowercase()
                aliases.any(searchableText::contains)
            }
            .maxOfOrNull { it.painLevel.coerceIn(0, 10) }
            ?.takeIf { it > 0 }
            ?: return
        // V1 prototype boundary: 5-10 is high, 1-4 is low, and 0 creates no tag.
        add("${regionLabel}疼痛指数${if (regionPainLevel >= 5) "高" else "低"}")
    }
}

internal fun DemoUserData.withoutPrototypeSeedData(): DemoUserData {
    val prototypeHistory = history.filter { it.sessionId.startsWith("seed_old_training") }
    val prototypeTimestamps = prototypeHistory.map(TrainingHistoryRecord::completedAt).toSet()
    val realHistory = history - prototypeHistory.toSet()
    val realPainHistory = painHistory.filterNot { record ->
        record.id.startsWith("seed_old_pain") ||
            (record.id.startsWith("legacy_") && record.recordedAt in prototypeTimestamps)
    }
    return if (realHistory == history && realPainHistory == painHistory) {
        this
    } else {
        copy(history = realHistory, painHistory = realPainHistory)
    }
}

class DemoUserStore(
    context: Context,
    databaseName: String = SpineFlowUserDatabase.DEFAULT_DATABASE_NAME,
    preferencesName: String = PREFERENCES_NAME
) {
    private val preferences = context.getSharedPreferences(preferencesName, Context.MODE_PRIVATE)
    private val database = SpineFlowUserDatabase(context, databaseName)

    init {
        resetNewDemoAccountsOnce()
    }

    private fun resetNewDemoAccountsOnce() {
        if (database.loadMetadata(NEW_ACCOUNT_RESET_KEY) == NEW_ACCOUNT_RESET_EPOCH) return

        val newAccountIds = DemoAccountDatabase.newAccountIds()
        database.deleteUsers(newAccountIds)
        preferences.edit().apply {
            newAccountIds.forEach { accountId -> remove(userKey(accountId)) }
            if (preferences.getString(KEY_ACTIVE_ACCOUNT_ID, null) in newAccountIds) {
                remove(KEY_ACTIVE_ACCOUNT_ID)
            }
            if (preferences.getString(KEY_REMEMBERED_ACCOUNT_ID, null) in newAccountIds) {
                remove(KEY_REMEMBERED_ACCOUNT_ID)
                remove(KEY_REMEMBERED_PASSWORD)
            }
        }.commit()
        database.saveMetadata(NEW_ACCOUNT_RESET_KEY, NEW_ACCOUNT_RESET_EPOCH)
    }

    fun loadUser(accountId: String): DemoUserData {
        check(!isDeleted(accountId)) { "本地账号已删除，请重新注册" }
        val normalizedAccountId = accountId.trim().lowercase()
        val databaseUser = database.loadUserJson(normalizedAccountId)
            ?.let { json -> runCatching { parseUser(json) }.getOrNull() }
        if (databaseUser != null) {
            return databaseUser.repairAndPersistIfNeeded()
        }

        val legacyUser = SecureLocalData.read(preferences, userKey(normalizedAccountId))
            ?.let { json -> runCatching { parseUser(json) }.getOrNull() }
        return UserFlowPolicy.repairPersistedLifecycle(
            legacyUser ?: defaultUser(normalizedAccountId)
        ).withoutPrototypeSeedData().also(::saveUser)
    }

    fun saveUser(user: DemoUserData) {
        check(!isDeleted(user.accountId)) { "已删除账号不可重新写入" }
        val json = user.toJson().toString()
        database.saveUserSnapshot(user, json)
        // Keep one compatibility copy while existing installations migrate lazily.
        preferences.edit()
            .putString(userKey(user.accountId), SecureLocalData.seal(userKey(user.accountId), json))
            .apply()
    }

    fun close() {
        database.close()
    }

    private fun DemoUserData.repairAndPersistIfNeeded(): DemoUserData {
        val repaired = UserFlowPolicy.repairPersistedLifecycle(this).withoutPrototypeSeedData()
        if (repaired != this) saveUser(repaired)
        return repaired
    }

    fun activeAccountId(): String? = preferences.getString(KEY_ACTIVE_ACCOUNT_ID, null)

    fun saveActiveAccountId(accountId: String?) {
        preferences.edit().putString(KEY_ACTIVE_ACCOUNT_ID, accountId).apply()
    }

    fun authenticate(accountId: String, password: String): Boolean {
        if (isDeleted(accountId)) return false
        val normalizedAccountId = accountId.trim().lowercase()
        if (DemoAccountDatabase.authenticate(normalizedAccountId, password) != null) return true
        return registeredAccounts().optString(normalizedAccountId, null) == password
    }

    fun registerAccount(accountId: String, password: String): AccountRegistrationResult {
        val normalizedAccountId = accountId.trim().lowercase()
        if (normalizedAccountId.isBlank() || password.isBlank()) {
            return AccountRegistrationResult.InvalidInput
        }
        if (DemoAccountDatabase.find(normalizedAccountId) != null ||
            preferences.getBoolean("deletion_pending_$normalizedAccountId", false)) {
            return AccountRegistrationResult.AccountAlreadyExists
        }

        val accounts = registeredAccounts()
        if (accounts.has(normalizedAccountId)) {
            return AccountRegistrationResult.AccountAlreadyExists
        }
        accounts.put(normalizedAccountId, password)
        if (!preferences.edit().remove("deleted_$normalizedAccountId").putString(
            KEY_REGISTERED_ACCOUNTS,
            SecureLocalData.seal(KEY_REGISTERED_ACCOUNTS, accounts.toString())
        ).commit()) {
            return AccountRegistrationResult.StorageFailure
        }

        saveUser(defaultNewUser(normalizedAccountId))
        return AccountRegistrationResult.Success
    }

    private fun registeredAccounts(): JSONObject {
        val json = SecureLocalData.read(preferences, KEY_REGISTERED_ACCOUNTS) ?: return JSONObject()
        return runCatching { JSONObject(json) }.getOrElse { JSONObject() }
    }

    fun isDeleted(accountId: String): Boolean =
        preferences.getBoolean("deleted_${accountId.trim().lowercase()}", false)

    fun pendingAccountDeletions(): List<String> = preferences.all
        .filter { (key, value) -> key.startsWith("deletion_pending_") && value == true }
        .keys.map { it.removePrefix("deletion_pending_") }

    /** Persist before touching independent stores; a crash must not turn a bound account into a new one. */
    fun beginAccountDeletion(accountId: String) {
        val id = accountId.trim().lowercase()
        check(preferences.edit().putBoolean("deleted_$id", true)
            .putBoolean("deletion_pending_$id", true).commit()) { "账号删除状态未能保存" }
    }

    fun finishAccountDeletion(accountId: String) {
        val id = accountId.trim().lowercase()
        check(isDeleted(id))
        database.deleteUsers(listOf(id))
        val accounts = registeredAccounts().apply { remove(id) }
        val editor = preferences.edit().remove(userKey(id)).remove("deletion_pending_$id")
            .putString(KEY_REGISTERED_ACCOUNTS, SecureLocalData.seal(KEY_REGISTERED_ACCOUNTS, accounts.toString()))
        if (activeAccountId() == id) editor.remove(KEY_ACTIVE_ACCOUNT_ID)
        if (preferences.getString(KEY_REMEMBERED_ACCOUNT_ID, null) == id) {
            editor.remove(KEY_REMEMBERED_ACCOUNT_ID).remove(KEY_REMEMBERED_PASSWORD)
        }
        check(editor.commit()) { "本地账号未能清理" }
    }

    fun rememberedLogin(): RememberedLogin? {
        val accountId = preferences.getString(KEY_REMEMBERED_ACCOUNT_ID, null)
        val password = SecureLocalData.read(preferences, KEY_REMEMBERED_PASSWORD)
        return if (accountId.isNullOrBlank() || password.isNullOrBlank()) {
            null
        } else {
            RememberedLogin(accountId, password)
        }
    }

    fun saveRememberedLogin(accountId: String, password: String) {
        preferences.edit()
            .putString(KEY_REMEMBERED_ACCOUNT_ID, accountId.trim().lowercase())
            .putString(KEY_REMEMBERED_PASSWORD,
                SecureLocalData.seal(KEY_REMEMBERED_PASSWORD, password))
            .apply()
    }

    fun clearRememberedLogin() {
        preferences.edit()
            .remove(KEY_REMEMBERED_ACCOUNT_ID)
            .remove(KEY_REMEMBERED_PASSWORD)
            .apply()
    }

    fun clearTemporaryTrainingData(accountId: String) {
        val user = loadUser(accountId)
        saveUser(
            user.copy(
                latestPreTrainingCheck = null,
                imuCalibrationState = ImuCalibrationState.NotCalibrated
            )
        )
    }

    private fun defaultUser(accountId: String): DemoUserData {
        val account = DemoAccountDatabase.find(accountId)
            ?: return defaultNewUser(accountId)
        return when (account.initialLifecycle) {
            UserLifecycle.ReturningUser -> DemoUserData(
                accountId = account.accountId,
                lifecycle = UserLifecycle.ReturningUser,
                profile = UserProfile(
                    heightCm = 175,
                    weightKg = 70,
                    gender = if (account.accountId.removePrefix("old").toIntOrNull()?.rem(2) == 0) {
                        UserGender.Female
                    } else {
                        UserGender.Male
                    },
                    ageYears = 35
                ),
                admissionSurvey = null,
                latestPreTrainingCheck = DailyCheck(
                    prePainLevel = 3,
                    symptomChange = "same",
                    painLocationChanged = false,
                    allowedToTrain = true
                ),
                latestPainPoints = listOf(
                    PainPoint("left_waist", "左腰部", "back", 3)
                ),
                imuCalibrationState = ImuCalibrationState.NotCalibrated,
                history = emptyList(),
                painHistory = emptyList()
            )

            UserLifecycle.AdmissionPending -> DemoUserData(
                accountId = account.accountId,
                lifecycle = UserLifecycle.AdmissionPending,
                profile = null,
                admissionSurvey = null,
                latestPreTrainingCheck = null,
                latestPainPoints = emptyList(),
                imuCalibrationState = ImuCalibrationState.NotCalibrated,
                history = emptyList(),
                painHistory = emptyList()
            )
        }
    }

    private fun defaultNewUser(accountId: String) = DemoUserData(
        accountId = accountId,
        lifecycle = UserLifecycle.AdmissionPending,
        profile = null,
        admissionSurvey = null,
        latestPreTrainingCheck = null,
        latestPainPoints = emptyList(),
        imuCalibrationState = ImuCalibrationState.NotCalibrated,
        history = emptyList(),
        painHistory = emptyList()
    )

    private fun parseUser(json: String): DemoUserData {
        val root = JSONObject(json)
        val latestPainPoints = root.optJSONArray("latestPainPoints").toPainPoints()
        val painHistory = root.optJSONArray("painHistory").toPainHistoryRecords()
            .ifEmpty {
                val legacyPainLevel = latestPainPoints.maxOfOrNull { it.painLevel }
                if (legacyPainLevel == null) {
                    emptyList()
                } else {
                    listOf(
                        PainHistoryRecord(
                            id = "legacy_${root.optString("accountId", "user")}",
                            recordedAt = root.optJSONArray("history")
                                ?.optJSONObject(0)
                                ?.optString("completedAt")
                                .orEmpty()
                                .ifBlank { OffsetDateTime.now().format(DateTimeFormatter.ISO_OFFSET_DATE_TIME) },
                            painLevel = legacyPainLevel,
                            source = PainRecordSource.DailyQuestionnaire,
                            painPoints = latestPainPoints
                        )
                    )
                }
            }
        return DemoUserData(
            accountId = root.optString("accountId", "new"),
            lifecycle = root.optString("lifecycle", UserLifecycle.AdmissionPending.name)
                .toEnumOrDefault(UserLifecycle.AdmissionPending),
            profile = root.optJSONObject("profile")?.let {
                UserProfile(
                    heightCm = it.optInt("heightCm", 0),
                    weightKg = it.optInt("weightKg", 0),
                    gender = UserGender.fromWireValue(it.optString("gender", "unspecified")),
                    ageYears = it.optInt("ageYears", 0)
                )
            }?.takeIf {
                it.gender != UserGender.Unspecified || (it.heightCm > 0 && it.weightKg > 0)
            },
            admissionSurvey = root.optJSONObject("admissionSurvey")?.let {
                AdmissionSurvey(
                    painLevel = it.optInt("painLevel", 0),
                    requiresClinicalReview = it.optBoolean("requiresClinicalReview", false)
                )
            },
            admissionIntake = root.optJSONObject("admissionIntake")?.let {
                AdmissionIntake(
                    situation = it.optString("situation"),
                    symptom = it.optString("symptom"),
                    goals = it.optJSONArray("goals")?.let { goals ->
                        buildList {
                            for (index in 0 until goals.length()) {
                                goals.optString(index).takeIf(String::isNotBlank)?.let(::add)
                            }
                        }
                    }.orEmpty()
                )
            },
            latestPreTrainingCheck = root.optJSONObject("latestPreTrainingCheck")?.let {
                DailyCheck(
                    prePainLevel = it.optInt("prePainLevel", 0),
                    symptomChange = it.optString("symptomChange", "same"),
                    painLocationChanged = it.optBoolean("painLocationChanged", false),
                    allowedToTrain = it.optBoolean("allowedToTrain", true),
                    recordedAt = it.optString("recordedAt", "")
                )
            },
            latestPainPoints = latestPainPoints,
            imuCalibrationState = root.optString(
                "imuCalibrationState",
                ImuCalibrationState.NotCalibrated.name
            ).toEnumOrDefault(ImuCalibrationState.NotCalibrated),
            history = root.optJSONArray("history").toHistoryRecords(),
            painHistory = painHistory,
            mobileAdmissionAssessment = root.optJSONObject("mobileAdmissionAssessment")
                ?.toMobileAdmissionAssessment(),
            healthEventsJson = root.optJSONArray("healthEvents")?.toString() ?: "[]",
            monthlyHistoryJson = root.optJSONArray("monthlyHistory")?.toString() ?: "[]",
            trackingCalibratedAt = root.optString("trackingCalibratedAt"),
            syncBindingKey = root.optString("syncBindingKey"),
            syncBaselineJson = root.optJSONObject("syncBaseline")?.toString() ?: "{}",
            intakeReviewRequired = root.optBoolean("intakeReviewRequired", root.optJSONObject("admissionSurvey")?.optBoolean("requiresClinicalReview") ?: false)
        )
    }

    private fun DemoUserData.toJson(): JSONObject {
        return JSONObject()
            .put("accountId", accountId)
            .put("lifecycle", lifecycle.name)
            .put("profile", profile?.let {
                JSONObject()
                    .put("heightCm", it.heightCm)
                    .put("weightKg", it.weightKg)
                    .put("gender", it.gender.wireValue)
                    .put("ageYears", it.ageYears)
            })
            .put("admissionSurvey", admissionSurvey?.let {
                JSONObject()
                    .put("painLevel", it.painLevel)
                    .put("requiresClinicalReview", it.requiresClinicalReview)
            })
            .put("admissionIntake", admissionIntake?.let {
                JSONObject()
                    .put("situation", it.situation)
                    .put("symptom", it.symptom)
                    .put("goals", JSONArray(it.goals))
            })
            .put("latestPreTrainingCheck", latestPreTrainingCheck?.let {
                JSONObject()
                    .put("prePainLevel", it.prePainLevel)
                    .put("symptomChange", it.symptomChange)
                    .put("painLocationChanged", it.painLocationChanged)
                    .put("allowedToTrain", it.allowedToTrain)
                    .put("recordedAt", it.recordedAt)
            })
            .put("latestPainPoints", JSONArray().also { array ->
                latestPainPoints.forEach { point ->
                    array.put(
                        JSONObject()
                            .put("bodyPart", point.bodyPart)
                            .put("bodyPartLabel", point.bodyPartLabel)
                            .put("view", point.view)
                            .put("painLevel", point.painLevel)
                    )
                }
            })
            .put("imuCalibrationState", imuCalibrationState.name)
            .put("history", JSONArray().also { array ->
                history.forEach { record ->
                    array.put(
                        JSONObject()
                            .put("sessionId", record.sessionId)
                            .put("completedAt", record.completedAt)
                            .put("resultJson", record.resultJson)
                            // SF-08：训练前问卷事件关联随历史持久化，
                            // 重启后上传仍能声明 pre:<recordedAt>（旧数据缺键回退空串）。
                            .put("preTrainingEventId", record.preTrainingEventId)
                            .put("planJson", record.planJson)
                            .put("feedbackPending", record.feedbackPending)
                            .put("endReason", record.endReason)
                            .put("replaySessionId", record.replaySessionId)
                            .put("networkResultJson", record.networkResultJson)
                            .put("networkResultError", record.networkResultError)
                            .put("actionId", record.actionId)
                            .put("feedback", record.feedback?.let { feedback ->
                                JSONObject()
                                    .put("feeling", feedback.feeling)
                                    .put("difficulty", feedback.difficulty)
                                    .put("recordedAt", feedback.recordedAt)
                            })
                    )
                }
            })
            .put("painHistory", JSONArray().also { array ->
                painHistory.forEach { record ->
                    array.put(
                        JSONObject()
                            .put("id", record.id)
                            .put("recordedAt", record.recordedAt)
                            .put("painLevel", record.painLevel)
                            .put("source", record.source.name)
                            .put("painParts", JSONArray(record.painParts))
                            .put("pointsCaptured", record.pointsCaptured)
                            .put("painFree", record.painFree)
                            .put("painPoints", JSONArray().also { points ->
                                record.painPoints.forEach { point ->
                                    points.put(
                                        JSONObject()
                                            .put("bodyPart", point.bodyPart)
                                            .put("bodyPartLabel", point.bodyPartLabel)
                                            .put("view", point.view)
                                            .put("painLevel", point.painLevel)
                                    )
                                }
                            })
                    )
                }
            })
            .put("mobileAdmissionAssessment", mobileAdmissionAssessment?.toJson())
            .put("healthEvents", JSONArray(healthEventsJson))
            .put("monthlyHistory", JSONArray(monthlyHistoryJson))
            .put("trackingCalibratedAt", trackingCalibratedAt)
            .put("syncBindingKey", syncBindingKey)
            .put("syncBaseline", JSONObject(syncBaselineJson))
            .put("intakeReviewRequired", intakeReviewRequired)
    }

    private fun JSONArray?.toPainPoints(): List<PainPoint> = buildList {
        val array = this@toPainPoints ?: return@buildList
        for (index in 0 until array.length()) {
            val item = array.optJSONObject(index) ?: continue
            add(
                PainPoint(
                    bodyPart = item.optString("bodyPart"),
                    bodyPartLabel = item.optString("bodyPartLabel"),
                    view = item.optString("view", "back"),
                    painLevel = item.optInt("painLevel", 0)
                )
            )
        }
    }

    private fun JSONArray?.toHistoryRecords(): List<TrainingHistoryRecord> = buildList {
        val array = this@toHistoryRecords ?: return@buildList
        for (index in 0 until array.length()) {
            val item = array.optJSONObject(index) ?: continue
            add(
                TrainingHistoryRecord(
                    sessionId = item.optString("sessionId"),
                    completedAt = item.optString("completedAt"),
                    resultJson = item.optString("resultJson"),
                    // SF-08：读取持久化的 pre 事件关联（旧数据无键 → 空串 → 无声明）
                    preTrainingEventId = item.optString("preTrainingEventId"),
                    planJson = item.optString("planJson"),
                    feedbackPending = item.optBoolean("feedbackPending"),
                    endReason = item.optString("endReason"),
                    replaySessionId = item.optString("replaySessionId"),
                    networkResultJson = item.optString("networkResultJson"),
                    networkResultError = item.optString("networkResultError"),
                    actionId = item.optString("actionId"),
                    feedback = item.optJSONObject("feedback")?.let { feedback ->
                        PostTrainingFeedback(
                            feeling = feedback.optString("feeling"),
                            difficulty = feedback.optString("difficulty"),
                            recordedAt = feedback.optString("recordedAt")
                        )
                    }
                )
            )
        }
    }

    private fun JSONArray?.toPainHistoryRecords(): List<PainHistoryRecord> = buildList {
        val array = this@toPainHistoryRecords ?: return@buildList
        for (index in 0 until array.length()) {
            val item = array.optJSONObject(index) ?: continue
            val id = item.optString("id")
            val recordedAt = item.optString("recordedAt")
            if (id.isBlank() || recordedAt.isBlank()) continue
            add(
                PainHistoryRecord(
                    id = id,
                    recordedAt = recordedAt,
                    painLevel = item.optInt("painLevel", 0).coerceIn(0, 10),
                    source = item.optString("source", PainRecordSource.DailyQuestionnaire.name)
                        .toEnumOrDefault(PainRecordSource.DailyQuestionnaire),
                    painPoints = item.optJSONArray("painPoints").toPainPoints(),
                    painParts = item.optJSONArray("painParts")?.let { parts ->
                        (0 until parts.length()).map { parts.getString(it) }
                    } ?: item.optJSONArray("painPoints").toPainPoints().map { it.bodyPart },
                    pointsCaptured = item.optBoolean("pointsCaptured", item.optJSONArray("painPoints").toPainPoints().isNotEmpty()),
                    painFree = item.optBoolean("painFree", item.optInt("painLevel") == 0)
                )
            )
        }
    }

    private fun JSONObject.toMobileAdmissionAssessment(): MobileAdmissionAssessment? {
        val flexion = optJSONObject("flexion")?.toAdmissionMovementResult() ?: return null
        val extension = optJSONObject("extension")?.toAdmissionMovementResult() ?: return null
        val completedAt = optString("completedAt")
        if (completedAt.isBlank()) return null
        return MobileAdmissionAssessment(flexion, extension, completedAt)
    }

    private fun JSONObject.toAdmissionMovementResult(): AdmissionMovementResult? {
        val movementType = AdmissionMovementType.entries.firstOrNull {
            it.wireValue == optString("movementType")
        } ?: return null
        val painResponse = AdmissionPainResponse.entries.firstOrNull {
            it.wireValue == optString("painResponse")
        } ?: return null
        val painPhase = optString("firstPainPhase")
            .takeIf(String::isNotBlank)
            ?.let { value -> AdmissionPainPhase.entries.firstOrNull { it.wireValue == value } }
        return AdmissionMovementResult(
            movementType = movementType,
            completionCount = optInt("completionCount", 0).coerceIn(0, 10),
            painResponse = painResponse,
            firstPainPhase = painPhase,
            painLevel = getInt("painLevel").also { require(it in 0..10) }
        )
    }

    private fun MobileAdmissionAssessment.toJson(): JSONObject = JSONObject()
        .put("completedAt", completedAt)
        .put("flexion", flexion.toJson())
        .put("extension", extension.toJson())

    private fun AdmissionMovementResult.toJson(): JSONObject = JSONObject()
        .put("movementType", movementType.wireValue)
        .put("completionCount", completionCount.coerceIn(0, 10))
        .put("painResponse", painResponse.wireValue)
        .put("firstPainPhase", firstPainPhase?.wireValue ?: "")
        .put("painLevel", painLevel.also { require(it in 0..10) })

    private inline fun <reified T : Enum<T>> String.toEnumOrDefault(default: T): T {
        return runCatching { enumValueOf<T>(this) }.getOrDefault(default)
    }

    private fun userKey(accountId: String) = "user_$accountId"

    private companion object {
        const val PREFERENCES_NAME = "spineflow_demo_users"
        const val KEY_ACTIVE_ACCOUNT_ID = "active_account_id"
        const val KEY_REMEMBERED_ACCOUNT_ID = "remembered_account_id"
        const val KEY_REMEMBERED_PASSWORD = "remembered_password"
        const val KEY_REGISTERED_ACCOUNTS = "registered_accounts"
        const val NEW_ACCOUNT_RESET_KEY = "new_account_reset_epoch"
        const val NEW_ACCOUNT_RESET_EPOCH = "2026-07-14-gender-binary"
    }
}
