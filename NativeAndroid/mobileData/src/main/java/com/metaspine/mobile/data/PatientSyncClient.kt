package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

object PatientSyncClient {
    private const val SYNC_PATH = "/api/patient/sync"

    /** 已同步游标快照（持久化结构） */
    data class SyncSnapshot(
        val syncedSessionIds: Set<String> = emptySet(),
        val syncedPainIds: Set<String> = emptySet(),
        val questionnaireHashes: Map<String, String> = emptyMap(),
        val profileHash: String? = null,
        // 评审 P1-1：上次成功同步时发出的 lifecycle / IMU 状态（null = 未同步过 → 视为变化）
        val lifecycle: String? = null,
        val imuCalibrationState: String? = null,
        // P3.5：已确认出站的训练后反馈事件（"post:<sessionId>"）。独立于 syncedSessionIds，
        // 使「训练已上传确认后才补填的晚反馈」仍能作为独立 S01 事件再次出站。
        val syncedFeedbackEventIds: Set<String> = emptySet()
    )

    /** 一次同步要携带的增量 **/
    data class SyncDelta(
        val profile: JSONObject? = null,                    // hash 变化才非空（含 recordedAt=now）
        val questionnaires: Map<String, JSONObject> = emptyMap(), // 同步字段名 → 内容
        val questionnaireKeys: Map<String, String> = emptyMap(),  // 评审 P1-3：字段 → 事件键（预训练用 recordedAt，其余用内容键）
        val painPoints: JSONArray? = null,                  // 新 painHistory id 展平
        val lifecycle: String? = null,                      // 评审 P1-1：仅状态变化时非空
        val imuCalibrationState: String? = null             // 评审 P1-1：仅状态变化时非空
    )

    /** 对比快照与用户状态，产出增量 */
    fun computeDelta(snapshot: SyncSnapshot, user: DemoUserData): SyncDelta {
        val profileJson = user.profile?.let { profileJson(it) }
        val profile = profileJson
            ?.takeIf { stableHash(it) != snapshot.profileHash }
            ?.also {
                // SF-07：档案事件身份 = 内容键（不含易变的 recordedAt/eventId）。
                // eventId 在「内容首次变化」时基于内容确定，重试/重启沿用同一值，
                // 服务器按 (patient, eventId) 幂等去重——不会因重建队列产生重复档案。
                it.put("eventId", "profile.${stableHash(it)}")
                it.put("recordedAt", nowIso())
            }

        val questionnaires = buildMap {
            user.admissionSurvey?.let { putIfChanged("admissionSurvey", it.toJson(), snapshot) }
            user.admissionIntake?.let { putIfChanged("admissionIntake", it.toJson(), snapshot) }
            user.mobileAdmissionAssessment?.let { putIfChanged("mobileAdmissionAssessment", it.toJson(), snapshot) }
            // 评审 P1-3：训练前问卷是「每次训练一条」的事件，两次答案相同 ≠ 重复。
            // 事件身份 = 填写时刻（recordedAt）或内容兜底键，统一由 preTrainingEventIdOf 派生。
            user.latestPreTrainingCheck?.let { check ->
                val eventKey = preTrainingEventIdOf(check)
                if (snapshot.questionnaireHashes["preTrainingQuestionnaire"] != eventKey) {
                    put("preTrainingQuestionnaire", check.toJson())
                }
            }
        }
        // 评审 P1-3：同步后快照里存的必须是「事件键」，而不是内容键
        val questionnaireKeys = buildMap {
            questionnaires.keys.forEach { field ->
                val json = questionnaires.getValue(field)
                val key = user.latestPreTrainingCheck
                    ?.takeIf { field == "preTrainingQuestionnaire" }
                    ?.let { check -> preTrainingEventIdOf(check) }
                    ?: stableHash(json)
                put(field, key)
            }
        }

        val newPain = user.painHistory.filter { it.id !in snapshot.syncedPainIds }
        val painPoints = if (newPain.isEmpty()) null else JSONArray().also { arr ->
            for (record in newPain) {
                // 评审 #1：eventId 必须细化到记录内的每个点（服务端按 eventId 唯一键去重，
                // 同一记录多部位共用 id 会导致除第一个点外全部被跳过）
                for ((index, point) in record.painPoints.withIndex()) {
                    if (point.painLevel !in 0..10) continue
                    arr.put(JSONObject()
                        .put("bodyPart", point.bodyPart)
                        .put("bodyPartLabel", point.bodyPartLabel)
                        .put("view", point.view)
                        .put("painLevel", point.painLevel)
                        .put("recordedAt", record.recordedAt)
                        // 评审 P1-2：疼痛事件幂等键（重试同批请求不会重复入库）
                        .put("eventId", "pain.${record.id}.$index"))
                }
            }
        }
        return SyncDelta(
            profile = profile,
            questionnaires = questionnaires,
            questionnaireKeys = questionnaireKeys,
            painPoints = painPoints,
            // 评审 P1-1：仅状态变化时携带，保证「只校准 IMU」这类变更也能同步
            lifecycle = user.lifecycle.name.takeIf { snapshot.lifecycle != it },
            imuCalibrationState = user.imuCalibrationState.name.takeIf { snapshot.imuCalibrationState != it }
        )
    }

    /** delta + 用户 → 同步请求体；无任何增量（含生命周期/IMU 变化）时返回 null */
    fun buildSyncBody(
        user: DemoUserData,
        delta: SyncDelta,
        patientId: String? = null
    ): JSONObject? {
        // 评审 P1-1：生命周期与 IMU 变化必须单独触发同步
        if (delta.profile == null && delta.questionnaires.isEmpty() &&
            delta.painPoints == null && delta.lifecycle == null && delta.imuCalibrationState == null
        ) return null
        val body = JSONObject()
            .put("userId", patientId ?: "demo_${user.accountId}")
            .put("accountId", user.accountId)
        // 评审 P1-1：只发送变化项（服务器按最近一条去重，未变化的不再携带）
        delta.lifecycle?.let { body.put("lifecycle", it) }
        delta.imuCalibrationState?.let { body.put("imuCalibrationState", it) }
        // 评审 P1-2：每个事件带 eventId，服务器按 eventId 幂等去重——
        // 「服务器已提交但 ACK 丢失」时的重试不会产生重复档案/问卷/疼痛。
        // SF-07：eventId 一律在 computeDelta 阶段用「内容键」派生并固化（同内容同 id），
        // 请求重建/重试/重启不重新随机——UUID 型 id 曾让重建队列产生重复记录。
        delta.profile?.let {
            if (!it.has("eventId")) {
                it.put("eventId", "profile.${stableHash(it)}")
            }
            body.put("profile", it)
        }
        delta.questionnaires.forEach { (field, json) ->
            // 评审#3：入组问卷/自述手机端无真实时间戳，以同步时刻近似（发送前附加、
            // 不参与内容键；payload 随队列持久化，重试时时间不变）
            if (field == "admissionSurvey" || field == "admissionIntake") {
                json.put("recordedAt", nowIso())
            }
            // SF-07：事件键优先（pre 问卷 = "pre:<recordedAt>"，幂等且稳定）；
            // 其余问卷用内容键加字段前缀（同内容同 id），不再生成随机 UUID。
            val key = delta.questionnaireKeys[field]
            val eventId = when {
                key != null && key.startsWith("pre:") -> key
                key != null -> "q.$field.$key"
                else -> "q.$field.${stableHash(json)}"
            }
            json.put("eventId", eventId)
            body.put(field, json)
        }
        delta.painPoints?.let { body.put("painPoints", it) }
        return body
    }

    private fun MutableMap<String, JSONObject>.putIfChanged(
        field: String, json: JSONObject, snapshot: SyncSnapshot
    ) {
        if (stableHash(json) != snapshot.questionnaireHashes[field]) put(field, json)
    }

    internal fun stableHash(json: JSONObject): String {
        val keys = json.keys().asSequence().toList().sorted()
        return buildString {
            for (key in keys) {
                append(key).append('=').append(json.opt(key) ?: "null").append(';')
            }
        }
    }

    private fun nowIso(): String =
        java.time.OffsetDateTime.now().format(java.time.format.DateTimeFormatter.ISO_OFFSET_DATE_TIME)

    /** 档案 JSON */
    internal fun profileJson(p: UserProfile) = JSONObject()
        .put("gender", p.gender.wireValue)
        .put("ageYears", p.ageYears)
        .put("heightCm", p.heightCm)
        .put("weightKg", p.weightKg)

    // 内容键用纯内容（评审#3：recordedAt 在发送前才附加，不得参与内容键，
    // 否则易变时间会让每次 diff 都误判为「内容变化」导致无限重发）
    private fun AdmissionSurvey.toJson() = JSONObject()
        .put("painLevel", painLevel)
        .put("requiresClinicalReview", requiresClinicalReview)

    private fun AdmissionIntake.toJson() = JSONObject()
        .put("situation", situation)
        .put("symptom", symptom)
        .put("goals", JSONArray(goals))

    private fun MobileAdmissionAssessment.toJson() = JSONObject()
        .put("flexion", flexion.toJson()).put("extension", extension.toJson())
        .put("completedAt", completedAt)

    private fun AdmissionMovementResult.toJson() = JSONObject()
        .put("movementType", movementType.wireValue)
        .put("completionCount", completionCount)
        .put("painResponse", painResponse.wireValue)
        .also { firstPainPhase?.let { p -> it.put("firstPainPhase", p.wireValue) } }
        .put("painLevel", painLevel)

    // 评审#3：训练前问卷把真实 recordedAt 一并入库（此前只用作事件键，医生端拿不到时间）
    private fun DailyCheck.toJson() = JSONObject()
        .put("prePainLevel", prePainLevel)
        .put("symptomChange", symptomChange)
        .put("painLocationChanged", painLocationChanged)
        .put("allowedToTrain", allowedToTrain)
        .put("recordedAt", recordedAt)

    /**
     * SF-08：训练前问卷事件的稳定身份，同步通道与训练上传通道共用同一派生规则。
     * 有 recordedAt（问卷提交时刻）→ "pre:<recordedAt>"（与发起训练时固化的
     * preTrainingEventId 一致）；缺失（演示/旧数据）→ 内容键，保证同内容同 id。
     */
    fun preTrainingEventIdOf(check: DailyCheck): String =
        check.recordedAt.takeIf(String::isNotBlank)
            ?.let { "pre:$it" }
            ?: "pre:content:${stableHash(check.toJson())}"

    /** Transport handles bounds and cancellation; the coordinator owns retry/idempotency. */
    suspend fun sync(body: JSONObject, patientToken: String?, origin: String = SpineFlowServerConfig.BASE_URL): Result<Unit> = patientRequestResult {
        val response = PatientHttpTransport(origin = { origin }).request(SYNC_PATH, "POST", patientToken, body.toString())
        response.requireSuccess("同步失败，HTTP "+response.status)
        verifyResponse(JSONObject(response.text), body.getString("userId"))
    }
    internal fun verifyResponse(json: JSONObject, patientId: String) {
        val code = json.opt("code")
        check(code is Number && code.toDouble() == 0.0) { "同步响应状态无效" }
        val data = json.getJSONObject("data")
        check(data.getString("userId") == patientId) { "同步响应患者不一致" }
        val counts = data.getJSONObject("synced")
        for (name in listOf("profile", "questionnaires", "painRecords", "lifecycle")) {
            val count = counts.opt(name)
            check(count is Number && count.toDouble().isFinite() && count.toDouble() >= 0 &&
                count.toDouble() == count.toLong().toDouble()) { "同步响应计数无效" }
        }
    }
}
