package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject

/** Existing UI answers are frozen at submission; missing answers stay missing. */
internal object HealthEventBridge {
    val fields = setOf("profile", "admissionSurvey", "admissionIntake", "mobileAdmissionAssessment", "preTrainingQuestionnaire")

    fun rebind(user: DemoUserData, bindingKey: String): DemoUserData {
        if (user.syncBindingKey == bindingKey) return user
        val baseline = if (user.syncBindingKey.isBlank()) JSONObject() else JSONObject()
            .put("events", JSONArray((0 until JSONArray(user.healthEventsJson).length()).map {
                JSONArray(user.healthEventsJson).getJSONObject(it).getString("eventId") }))
            .put("pain", JSONArray(user.painHistory.map { it.id }))
            .put("sessions", JSONArray(user.history.map { it.sessionId }))
            .put("lifecycle", user.lifecycle.name).put("imuCalibrationState", user.imuCalibrationState.name)
        return user.copy(syncBindingKey = bindingKey, syncBaselineJson = baseline.toString())
    }

    /** Older binding facts remain local; only facts captured after the rebind enter its outbox. */
    fun forCurrentBinding(user: DemoUserData): DemoUserData {
        val baseline = JSONObject(user.syncBaselineJson)
        val excluded = baseline.optJSONArray("events").toStringSet()
        val events = JSONArray(user.healthEventsJson)
        return user.copy(healthEventsJson = JSONArray((0 until events.length()).map { events.getJSONObject(it) }
            .filter { it.getString("eventId") !in excluded }).toString(),
            painHistory = user.painHistory.filter { it.id !in baseline.optJSONArray("pain").toStringSet() },
            history = user.history.filter { it.sessionId !in baseline.optJSONArray("sessions").toStringSet() })
    }

    fun captureProfile(events: String, old: UserProfile?, current: UserProfile?): String {
        if (current == null || current == old || current.heightCm <= 0 || current.weightKg <= 0 ||
            current.ageYears <= 0 || current.gender == UserGender.Unspecified) return events
        val event = JSONObject().put("type", "profile").put("eventId", "profile:${java.util.UUID.randomUUID()}")
            .put("recordedAt", java.time.Instant.now().toString())
            .put("answersJson", PatientSyncClient.profileJson(current).toString())
            .put("rawRegions", JSONArray()).put("rawLeg", "")
        return merge(events, JSONArray().put(event))
    }

    fun merge(previous: String, incoming: JSONArray?): String {
        val events = linkedMapOf<String, JSONObject>()
        for (array in listOf(JSONArray(previous), incoming ?: JSONArray())) {
            for (index in 0 until array.length()) {
                val event = array.getJSONObject(index)
                val id = event.getString("eventId").also { require(it.isNotBlank()) }
                require(event.getString("type") in fields)
                require(event.getString("recordedAt").isNotBlank())
                JSONObject(event.getString("answersJson"))
                val old = events[id]
                require(old == null || CanonicalJson.canonicalize(old) == CanonicalJson.canonicalize(event)) {
                    "健康事件内容不能改写: $id"
                }
                events[id] = event
            }
        }
        return JSONArray(events.values.toList()).toString()
    }

    fun batches(user: DemoUserData, snapshot: PatientSyncClient.SyncSnapshot, patientId: String): List<JSONObject> {
        val output = mutableListOf<JSONObject>()
        fun envelope() = JSONObject().put("userId", patientId).put("accountId", user.accountId)
        fun add(id: String, body: JSONObject, cursor: QueueCursor) {
            require(body.toString().toByteArray(Charsets.UTF_8).size <= 200 * 1024) { "单个健康事件超过传输上限" }
            output += queueElement(id, "sync", body, cursor)
        }
        // Profiles and questionnaires only use captured events. State changes are partial updates.
        val baseUser = user.copy(profile = null, admissionSurvey = null, admissionIntake = null,
            mobileAdmissionAssessment = null, latestPreTrainingCheck = null, painHistory = emptyList())
        val baseline = JSONObject(user.syncBaselineJson)
        val stateSnapshot = snapshot.copy(lifecycle = snapshot.lifecycle ?: baseline.optString("lifecycle").takeIf { it.isNotBlank() },
            imuCalibrationState = snapshot.imuCalibrationState ?: baseline.optString("imuCalibrationState").takeIf { it.isNotBlank() })
        val delta = PatientSyncClient.computeDelta(stateSnapshot, baseUser)
        PatientSyncClient.buildSyncBody(baseUser, delta, patientId)?.let { body ->
            val profileHash = user.profile?.takeIf { delta.profile != null }?.let { PatientSyncClient.stableHash(PatientSyncClient.profileJson(it)) }
            add("sync:profile:${ResultDigest.sha256Hex(CanonicalJson.canonicalize(body))}", body,
                QueueCursor(profileHash = profileHash, lifecycle = delta.lifecycle, imuCalibrationState = delta.imuCalibrationState))
        }
        val events = JSONArray(user.healthEventsJson)
        for (index in 0 until events.length()) {
            val event = events.getJSONObject(index)
            val id = event.getString("eventId")
            val field = event.getString("type")
            require(field in fields)
            val answers = JSONObject(event.getString("answersJson"))
                .put("eventId", id).put("recordedAt", event.getString("recordedAt"))
            if (field == "mobileAdmissionAssessment") for (name in listOf("flexion", "extension")) {
                answers.optJSONObject(name)?.let { movement ->
                    if (movement.optString("firstPainPhase").isBlank()) movement.remove("firstPainPhase")
                }
            }
            val hash = CanonicalJson.canonicalize(answers)
            val key = "event:$id"
            val known = snapshot.questionnaireHashes[key]
            require(known == null || known == hash) { "已确认健康事件内容不一致: $id" }
            if (known == null) add("sync:event:$id", envelope().put(field, answers), QueueCursor(qHashes = mapOf(key to hash)))
        }
        for (record in user.painHistory.distinctBy { it.id }) {
            if (record.id in snapshot.syncedPainIds || record.painPoints.isEmpty()) continue
            require(record.painPoints.size <= 500 && record.painPoints.all { it.painLevel in 0..10 })
            val pain = PatientSyncClient.computeDelta(snapshot, baseUser.copy(profile = null, painHistory = listOf(record))).painPoints ?: continue
            for (index in 0 until pain.length()) {
                val point = pain.getJSONObject(index)
                if (point.optString("view").isBlank()) point.remove("view")
            }
            add("sync:pain:${record.id}", envelope().put("painPoints", pain), QueueCursor(painIds = setOf(record.id)))
        }
        for (record in user.history.distinctBy { it.sessionId }) {
            val feedback = record.feedback ?: continue
            if (feedback.recordedAt.isBlank()) continue
            val id = "post:${record.sessionId}"
            if (id in snapshot.syncedFeedbackEventIds) continue
            val value = JSONObject().put("eventId", id).put("sessionId", record.sessionId)
                .put("recordedAt", feedback.recordedAt).put("feeling", feedback.feeling).put("difficulty", feedback.difficulty)
            add("sync:$id", envelope().put("postTrainingFeedbacks", JSONArray().put(value)), QueueCursor(feedbackEventIds = setOf(id)))
        }
        return output
    }
}
