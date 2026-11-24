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
            val exercise = requireNotNull(BuiltInExercise.forKey(item.action)) {
                "处方动作暂未接入训练场景：${item.actionName}"
            }
            for ((mode, scene) in listOf("coach" to exercise.coach, "game" to exercise.game)) {
                actions.put(JSONObject()
                    .put("actionId", "${exercise.key}_$mode")
                    .put("actionName", item.actionName)
                    .put("arTaskType", "${mode}_training")
                    .put("level", item.level ?: "standard")
                    .put("sets", item.sets)
                    .put("reps", item.reps)
                    .put("holdSeconds", item.holdSeconds)
                    .put("prescriptionItemId", item.itemId)
                    .put("prescriptionPosition", item.position)
                    .put("sceneName", scene))
            }
        }
        return actions
    }

    fun result(record: TrainingHistoryRecord, session: JSONObject): JSONObject? {
        if (record.planJson.isBlank()) return null
        val plan = JSONObject(record.planJson)
        if (plan.optInt("prescriptionContractVersion") != 2) return null
        val order = AdmissionPrescriptionV2.fromJson(JSONObject(plan.getString("prescriptionJson")))
        val measurements = session.getJSONArray("actionResults")
        val byMode = (0 until measurements.length()).associate { index ->
            val row = measurements.getJSONObject(index)
            (row.getLong("prescriptionItemId") to row.getString("trainingMode")) to row
        }
        val items = order.items.map { item ->
            val modes = ResultMode.ORDER.map { mode ->
                val row = byMode[item.itemId to mode.wireValue]
                // Playback completion alone is not evidence of a completed patient repetition.
                // The wire contract requires at least one repetition for COMPLETED.
                val completed = row?.optBoolean("prescriptionModeCompleted") == true &&
                    row.optInt("completedRepetitions") > 0
                val status = when {
                    completed -> ResultSessionStatus.COMPLETED
                    row != null -> ResultSessionStatus.INTERRUPTED
                    else -> ResultSessionStatus.NOT_STARTED
                }
                ResultModeResult(mode, status,
                    row?.optInt("completedRepetitions") ?: 0,
                    row?.optInt("durationSeconds") ?: 0,
                    row?.takeIf { it.optBoolean("scoreAvailable", it.optInt("scoreSampleCount") >= 10) }
                        ?.getDouble("overallScore"),
                    if (row == null) ResultModeSource.NotStarted else ResultModeSource.ArMeasured)
            }
            val status = when {
                modes.all { it.status == ResultSessionStatus.COMPLETED } -> ResultSessionStatus.COMPLETED
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
