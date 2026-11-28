package com.metaspine.mobile.data

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

internal object UnifiedPrescriptionV2 {
    suspend fun prepare(
        context: Context,
        account: String,
        user: DemoUserData,
        credential: PatientServerCredential,
        directive: JSONObject,
        preEvent: String
    ): JSONObject {
        require(directive.getString("trainingState") == "ready") {
            directive.optString("message").ifBlank { "当前医生处方暂不可训练" }
        }
        val order = AdmissionPrescriptionV2.fromDirectiveJson(directive.getJSONObject("activePrescription"))
        require(order.orderId > 0 && order.computeManifestSha256() == order.resourceManifestSha256) {
            "医生处方资源清单校验失败"
        }
        require(order.items.map { it.itemId }.toSet().size == order.items.size)
        val profile = requireNotNull(user.profile) { "请先完善基础档案" }
        require(profile.isCompleteForTraining() && user.latestPreTrainingCheck?.allowedToTrain == true &&
            user.latestPainPoints.none { it.painLevel >= MobileAdmissionAssessment.SEVERE_PAIN_THRESHOLD }) {
            "请先通过训练前安全检查"
        }
        val origin = SpineFlowServerConfig.BASE_URL
        val folder = File(context.filesDir, "unified-prescriptions")
        check(folder.isDirectory || folder.mkdirs())
        val identity = ResultDigest.sha256Hex("$origin|$account|${credential.patientId}")
        val saved = android.util.AtomicFile(File(folder, "$identity.json"))
        val output = saved.startWrite()
        try {
            output.write(directive.toString().toByteArray(Charsets.UTF_8))
            saved.finishWrite(output)
        } catch (error: Exception) {
            saved.failWrite(output)
            throw error
        }
        PatientDirectiveClient.postV2Receipt(credential.patientToken, order, "directive_saved")

        val actions = actionsFor(order)
        check(SpineFlowServerConfig.BASE_URL == origin &&
            PatientCredentials.credential(context, account) == credential) { "患者绑定已变更，请重新获取处方" }
        PatientDirectiveClient.postV2Receipt(credential.patientToken, order, "assets_ready")
        return JSONObject()
            .put("accountId", account)
            .put("patientId", credential.patientId)
            .put("gender", profile.gender.wireValue)
            .put("actionId", order.items.first().action)
            .put("prescriptionOrderId", order.orderId)
            .put("prescriptionContractVersion", 2)
            .put("prescriptionJson", order.toJson().toString())
            .put("preTrainingEventId", preEvent)
            .put("actions", actions)
    }

    internal fun actionsFor(order: AdmissionPrescriptionV2): JSONArray {
        val actions = JSONArray()
        for (item in order.items) {
            val exercises = exercisesFor(item)
            fun add(exercise: BuiltInExercise, coach: Boolean, sets: Int, reps: Int) {
                val mode = if (coach) "coach" else "game"
                actions.put(JSONObject()
                    .put("actionId", "${exercise.key}_$mode")
                    .put("actionName", if (exercises.size == 2) "侧桥" else exercise.names.first())
                    .put("arTaskType", "${mode}_training")
                    .put("level", item.level ?: "standard")
                    .put("sets", sets).put("reps", reps)
                    .put("holdSeconds", if (coach) 0 else if (exercise.key.endsWith("hold30")) item.holdSeconds.takeIf { it > 0 } ?: 30 else 0)
                    .put("prescriptionItemId", item.itemId)
                    .put("prescriptionPosition", item.position)
                    .put("sceneName", if (coach) exercise.coach else exercise.game))
            }
            for (exercise in exercises) {
                add(exercise, true, 1, if (exercises.size == 2) 2 else 4)
                if (exercises.size == 2) repeat(item.sets) { add(exercise, false, 1, item.reps) }
                else add(exercise, false, item.sets, item.reps)
            }
        }
        return actions
    }

    internal fun exercisesFor(item: AdmissionPrescriptionItemV2): List<BuiltInExercise> {
        val holding = item.holdSeconds > 0 || item.action.endsWith("hold30")
        return when (item.action) {
            "side_bridge", "side_bridge_hold30" -> if (holding)
                listOf(BuiltInExercise.SIDE_BRIDGE_HOLD, BuiltInExercise.SIDE_BRIDGE_RIGHT_HOLD)
                else listOf(BuiltInExercise.SIDE_BRIDGE, BuiltInExercise.SIDE_BRIDGE_RIGHT)
            "hip_thrust", "hip_thrust_taiko", "hip_thrust_curve", "hip_thrust_hold30" -> listOf(if (holding)
                BuiltInExercise.HIP_THRUST_HOLD else requireNotNull(BuiltInExercise.forKey(item.action)))
            else -> listOf(requireNotNull(BuiltInExercise.forKey(
                if (holding && !item.action.endsWith("hold30")) "${item.action}_hold30" else item.action)))
        }
    }

    fun result(record: TrainingHistoryRecord, session: JSONObject): JSONObject? {
        if (record.planJson.isBlank()) return null
        val plan = JSONObject(record.planJson)
        if (plan.optInt("prescriptionContractVersion") != 2) return null
        val order = AdmissionPrescriptionV2.fromJson(JSONObject(plan.getString("prescriptionJson")))
        val measurements = session.getJSONArray("actionResults")
        val byMode = (0 until measurements.length()).map(measurements::getJSONObject)
            .groupBy { it.getLong("prescriptionItemId") to it.getString("trainingMode") }
        val planned = plan.optJSONArray("actions")
        val items = order.items.map { item ->
            val modes = ResultMode.ORDER.map { mode ->
                val rows = byMode[item.itemId to mode.wireValue].orEmpty()
                val expected = if (planned == null) 1 else (0 until planned.length()).count {
                    val action = planned.getJSONObject(it)
                    action.getLong("prescriptionItemId") == item.itemId &&
                        action.getString("arTaskType") == "${mode.wireValue}_training"
                }
                val settled = rows.size == expected && rows.all {
                    it.optBoolean("prescriptionModeSkipped") ||
                        it.optBoolean("prescriptionModeCompleted") && it.optInt("completedRepetitions") > 0
                }
                val status = when {
                    settled && rows.any { it.optBoolean("prescriptionModeSkipped") } -> ResultSessionStatus.SKIPPED
                    settled -> ResultSessionStatus.COMPLETED
                    rows.isNotEmpty() -> ResultSessionStatus.INTERRUPTED
                    else -> ResultSessionStatus.NOT_STARTED
                }
                val scores = rows.filter { it.optBoolean("scoreAvailable", it.optInt("scoreSampleCount") >= 10) }
                ResultModeResult(mode, status,
                    rows.sumOf { it.optInt("completedRepetitions") },
                    rows.sumOf { it.optInt("durationSeconds") },
                    scores.takeIf { it.isNotEmpty() }?.map { it.getDouble("overallScore") }?.average(),
                    if (rows.isEmpty()) ResultModeSource.NotStarted else ResultModeSource.ArMeasured)
            }
            val status = when {
                modes.all { it.status.isFinished } -> ResultSessionStatus.COMPLETED
                modes.all { it.status == ResultSessionStatus.NOT_STARTED } -> ResultSessionStatus.NOT_STARTED
                else -> ResultSessionStatus.INTERRUPTED
            }
            ResultItem(item.itemId, item.position, item.legacyId ?: item.action, item.asset?.actionVersionId,
                item.asset?.variantKey, item.asset?.sha256, status, modes)
        }
        val completed = items.all { it.status == ResultSessionStatus.COMPLETED }
        val result = PrescriptionResultV2(record.sessionId, record.sessionId, session.getString("userId"),
            plan.getString("startedAt"), completed,
            if (completed) ResultSessionStatus.COMPLETED else ResultSessionStatus.INTERRUPTED,
            items.sumOf { it.modeResults.sumOf(ResultModeResult::durationSeconds) },
            order.orderId, order.resourceManifestSha256, 1,
            if (byMode.isEmpty()) ResultSnapshotSource.MobileNoDetails else ResultSnapshotSource.ArMeasured,
            items, record.preTrainingEventId.takeIf(String::isNotBlank))
        return PrescriptionResultV2.parse(result.toJson(), result.userId).toJson()
    }
}
