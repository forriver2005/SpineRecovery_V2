package com.metaspine.mobile.ar.session

import android.content.Context
import org.json.JSONObject

enum class ArSessionState {
    Idle,
    WaitingForHeadset,
    ArLaunching,
    ArRunning,
    HeadsetDisconnected,
    WaitingForPhoneResume,
    QuestionnairePending,
    Completed,
    Interrupted,
    Failed,
    Cancelled
}

data class ArSessionRecord(
    val sessionId: String,
    val userId: String,
    val requestToken: String,
    val startPayloadJson: String,
    val arPackageName: String,
    val state: ArSessionState,
    val launchRequested: Boolean,
    val createdAtEpochMillis: Long,
    val headsetConnectedAtEpochMillis: Long?,
    val arLaunchedAtEpochMillis: Long?,
    val headsetDisconnectedAtEpochMillis: Long?,
    val questionnaireOpened: Boolean,
    val resultHandled: Boolean,
    val userConfirmedCompleted: Boolean?
) {
    val inferredDurationSeconds: Int
        get() {
            val start = arLaunchedAtEpochMillis ?: return 0
            val end = headsetDisconnectedAtEpochMillis ?: return 0
            return ((end - start).coerceAtLeast(0L) / 1_000L).toInt()
        }
}

class ArSessionRepository(context: Context) {
    private val preferences = context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    @Synchronized
    fun begin(
        trainingPayloadJson: String,
        arPackageName: String,
        forceNewSession: Boolean = false
    ): ArSessionRecord {
        val root = JSONObject(trainingPayloadJson)
        val sessionId = root.optString("sessionId")
        val userId = root.optString("userId")
        require(sessionId.isNotBlank()) { "Missing AR sessionId." }
        require(userId.isNotBlank()) { "Missing AR userId." }
        val canonicalPayload = ArSessionTokenContract.canonicalize(trainingPayloadJson)

        val existing = current()
        if (!forceNewSession && existing != null && existing.sessionId == sessionId &&
            existing.state != ArSessionState.Idle &&
            existing.arPackageName == arPackageName &&
            existing.state !in TERMINAL_STATES &&
            JSONObject(existing.startPayloadJson).optJSONObject("trainingPlan")
                ?.optJSONArray("recommendedActions")?.toString() ==
            root.optJSONObject("trainingPlan")
                ?.optJSONArray("recommendedActions")?.toString()
        ) return existing

        return ArSessionRecord(
            sessionId = sessionId,
            userId = userId,
            requestToken = canonicalPayload.requestToken,
            startPayloadJson = canonicalPayload.json,
            arPackageName = arPackageName,
            state = ArSessionState.WaitingForHeadset,
            launchRequested = false,
            createdAtEpochMillis = System.currentTimeMillis(),
            headsetConnectedAtEpochMillis = null,
            arLaunchedAtEpochMillis = null,
            headsetDisconnectedAtEpochMillis = null,
            questionnaireOpened = false,
            resultHandled = false,
            userConfirmedCompleted = null
        ).also(::save)
    }

    fun current(): ArSessionRecord? {
        val sessionId = preferences.getString(KEY_SESSION_ID, null) ?: return null
        val userId = preferences.getString(KEY_USER_ID, null) ?: return null
        val payload = preferences.getString(KEY_START_PAYLOAD, null) ?: return null
        return ArSessionRecord(
            sessionId = sessionId,
            userId = userId,
            requestToken = preferences.getString(KEY_REQUEST_TOKEN, "").orEmpty(),
            startPayloadJson = payload,
            arPackageName = preferences.getString(KEY_AR_PACKAGE, "").orEmpty(),
            state = runCatching {
                ArSessionState.valueOf(preferences.getString(KEY_STATE, "").orEmpty())
            }.getOrDefault(ArSessionState.Idle),
            launchRequested = preferences.getBoolean(KEY_LAUNCH_REQUESTED, false),
            createdAtEpochMillis = preferences.getLong(KEY_CREATED_AT, 0L),
            headsetConnectedAtEpochMillis = preferences.optionalLong(KEY_HEADSET_CONNECTED_AT),
            arLaunchedAtEpochMillis = preferences.optionalLong(KEY_AR_LAUNCHED_AT),
            headsetDisconnectedAtEpochMillis = preferences.optionalLong(KEY_HEADSET_DISCONNECTED_AT),
            questionnaireOpened = preferences.getBoolean(KEY_QUESTIONNAIRE_OPENED, false),
            resultHandled = preferences.getBoolean(KEY_RESULT_HANDLED, false),
            userConfirmedCompleted = preferences.optionalBoolean(KEY_USER_CONFIRMED_COMPLETED)
        )
    }

    @Synchronized
    fun markHeadsetConnected(record: ArSessionRecord) {
        update(record) { current ->
            current.copy(
                state = if (current.launchRequested) current.state else ArSessionState.WaitingForHeadset,
                headsetConnectedAtEpochMillis = current.headsetConnectedAtEpochMillis
                    ?: System.currentTimeMillis()
            )
        }
    }

    @Synchronized
    fun markLaunchRequested(record: ArSessionRecord): Boolean {
        val current = current() ?: return false
        if (!current.matches(record) || current.launchRequested || current.state in TERMINAL_STATES) return false
        save(current.copy(state = ArSessionState.ArLaunching, launchRequested = true))
        return true
    }

    fun markRunning(record: ArSessionRecord) {
        update(record) { current ->
            current.copy(
                state = ArSessionState.ArRunning,
                arLaunchedAtEpochMillis = current.arLaunchedAtEpochMillis ?: System.currentTimeMillis()
            )
        }
    }

    fun markLaunchFailed(record: ArSessionRecord) {
        // Preserve launchRequested so repeated Display/USB events cannot auto-launch
        // this session again. A deliberate manual retry creates a fresh attempt.
        update(record) { it.copy(state = ArSessionState.Failed, launchRequested = true) }
    }

    @Synchronized
    fun markHeadsetDisconnected(record: ArSessionRecord): Boolean {
        val current = current() ?: return false
        if (!current.matches(record) || !current.launchRequested ||
            current.state !in setOf(
                ArSessionState.ArLaunching,
                ArSessionState.ArRunning,
                ArSessionState.WaitingForPhoneResume
            )
        ) return false
        save(
            current.copy(
                state = ArSessionState.HeadsetDisconnected,
                headsetDisconnectedAtEpochMillis = current.headsetDisconnectedAtEpochMillis
                    ?: System.currentTimeMillis()
            )
        )
        return true
    }

    @Synchronized
    fun markQuestionnairePending(record: ArSessionRecord): Boolean {
        val current = current() ?: return false
        if (!current.matches(record) || !current.launchRequested ||
            current.state !in setOf(
                ArSessionState.ArLaunching,
                ArSessionState.ArRunning,
                ArSessionState.WaitingForPhoneResume,
                ArSessionState.HeadsetDisconnected
            )
        ) return false
        save(
            current.copy(
                state = ArSessionState.QuestionnairePending,
                headsetDisconnectedAtEpochMillis = current.headsetDisconnectedAtEpochMillis
                    ?: System.currentTimeMillis()
            )
        )
        return true
    }

    @Synchronized
    fun consumeQuestionnairePending(): ArSessionRecord? {
        val current = current() ?: return null
        if (current.state != ArSessionState.QuestionnairePending || current.questionnaireOpened) return null
        val opened = current.copy(questionnaireOpened = true)
        save(opened)
        return opened
    }

    fun markQuestionnaireOutcome(record: ArSessionRecord, completed: Boolean) {
        update(record) {
            it.copy(
                state = if (completed) ArSessionState.Completed else ArSessionState.Interrupted,
                userConfirmedCompleted = completed
            )
        }
    }

    @Synchronized
    fun markResultHandled(record: ArSessionRecord, state: ArSessionState): Boolean {
        val current = current() ?: return false
        if (!current.matches(record) || current.resultHandled) return false
        save(current.copy(state = state, resultHandled = true))
        return true
    }

    fun clear() {
        check(preferences.edit().clear().commit()) { "AR 会话未能清理" }
    }

    @Synchronized
    private fun update(record: ArSessionRecord, transform: (ArSessionRecord) -> ArSessionRecord) {
        val current = current() ?: return
        if (current.matches(record)) save(transform(current))
    }

    private fun ArSessionRecord.matches(other: ArSessionRecord): Boolean =
        sessionId == other.sessionId && requestToken == other.requestToken

    private fun save(record: ArSessionRecord) {
        preferences.edit()
            .putString(KEY_SESSION_ID, record.sessionId)
            .putString(KEY_USER_ID, record.userId)
            .putString(KEY_REQUEST_TOKEN, record.requestToken)
            .putString(KEY_START_PAYLOAD, record.startPayloadJson)
            .putString(KEY_AR_PACKAGE, record.arPackageName)
            .putString(KEY_STATE, record.state.name)
            .putBoolean(KEY_LAUNCH_REQUESTED, record.launchRequested)
            .putLong(KEY_CREATED_AT, record.createdAtEpochMillis)
            .putOptionalLong(KEY_HEADSET_CONNECTED_AT, record.headsetConnectedAtEpochMillis)
            .putOptionalLong(KEY_AR_LAUNCHED_AT, record.arLaunchedAtEpochMillis)
            .putOptionalLong(KEY_HEADSET_DISCONNECTED_AT, record.headsetDisconnectedAtEpochMillis)
            .putBoolean(KEY_QUESTIONNAIRE_OPENED, record.questionnaireOpened)
            .putBoolean(KEY_RESULT_HANDLED, record.resultHandled)
            .putOptionalBoolean(KEY_USER_CONFIRMED_COMPLETED, record.userConfirmedCompleted)
            .apply()
    }

    private fun android.content.SharedPreferences.optionalLong(key: String): Long? =
        if (contains(key)) getLong(key, 0L) else null

    private fun android.content.SharedPreferences.optionalBoolean(key: String): Boolean? =
        if (contains(key)) getBoolean(key, false) else null

    private fun android.content.SharedPreferences.Editor.putOptionalLong(key: String, value: Long?) =
        if (value == null) remove(key) else putLong(key, value)

    private fun android.content.SharedPreferences.Editor.putOptionalBoolean(key: String, value: Boolean?) =
        if (value == null) remove(key) else putBoolean(key, value)

    private companion object {
        const val PREFERENCES_NAME = "spineflow_ar_session"
        const val KEY_SESSION_ID = "session_id"
        const val KEY_USER_ID = "user_id"
        const val KEY_REQUEST_TOKEN = "request_token"
        const val KEY_START_PAYLOAD = "start_payload"
        const val KEY_AR_PACKAGE = "ar_package"
        const val KEY_STATE = "state"
        const val KEY_LAUNCH_REQUESTED = "launch_requested"
        const val KEY_CREATED_AT = "created_at"
        const val KEY_HEADSET_CONNECTED_AT = "headset_connected_at"
        const val KEY_AR_LAUNCHED_AT = "ar_launched_at"
        const val KEY_HEADSET_DISCONNECTED_AT = "headset_disconnected_at"
        const val KEY_QUESTIONNAIRE_OPENED = "questionnaire_opened"
        const val KEY_RESULT_HANDLED = "result_handled"
        const val KEY_USER_CONFIRMED_COMPLETED = "user_confirmed_completed"
        val TERMINAL_STATES = setOf(
            ArSessionState.Completed,
            ArSessionState.Interrupted,
            ArSessionState.Failed,
            ArSessionState.Cancelled
        )
    }
}
