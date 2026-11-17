package com.metaspine.mobile.data

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.security.MessageDigest

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

        val actions = JSONArray()
        for (item in order.items) {
            val (coach, game) = when (item.actionId) {
                "dead_bug" -> "DeadBugPractice" to "DeadBug_RingSkinC_Recorded"
                "bird_dog" -> "BirdDogPractice" to "BirdDog_RingSkinC_Recorded"
                "hip_thrust" -> "HipThrustPractice" to "HipThrust_RingBridge_NewScoring"
                "side_bridge" -> "SideBridgePractice" to "SideBridge_RingBridge_Dynamic"
                else -> error("处方动作暂未接入训练场景：${item.actionName}")
            }
            require(item.itemId > 0 && item.sets in 1..20 && item.reps in 1..100 && item.holdSeconds in 0..600)
            require(item.asset.variantKey == "single" && item.asset.formatVersion == 1 &&
                item.asset.runtimeContractVersion == 1 && item.asset.doseProfile == "sequence-coach-game-v1" &&
                item.asset.supportedModes == listOf("coach", "game")) { "处方动作资源格式不受支持" }
            val file = download(folder, origin, credential.patientToken, order.orderId, item)
            val motion = JSONObject(file.readText())
            val segments = motion.getJSONArray("segments")
            require(motion.optInt("formatVersion") == 1 && segments.length() > 0) { "处方动作资源无法播放" }
            for (index in 0 until segments.length()) {
                val frames = segments.getJSONObject(index).getJSONArray("frames")
                require(frames.length() > 0) { "处方动作资源缺少姿态帧" }
                if (segments.length() == 1) {
                    val (start, end) = when (item.actionId) {
                        "dead_bug" -> 6.5 to 21.5
                        "bird_dog" -> 12.0 to 26.5
                        "hip_thrust" -> 22.0 to 40.0
                        else -> 14.0 to 26.0
                    }
                    require(frames.getJSONObject(0).getDouble("time") <= start &&
                        frames.getJSONObject(frames.length() - 1).getDouble("time") >= end) {
                        "处方录制素材缺少完整动作"
                    }
                }
            }
            for ((mode, scene) in listOf("coach" to coach, "game" to game)) {
                actions.put(JSONObject()
                    .put("actionId", "${item.actionId}_$mode")
                    .put("actionName", item.actionName)
                    .put("arTaskType", "${mode}_training")
                    .put("level", "standard")
                    .put("sets", item.sets)
                    .put("reps", item.reps)
                    .put("holdSeconds", item.holdSeconds)
                    .put("prescriptionItemId", item.itemId)
                    .put("prescriptionPosition", item.position)
                    .put("sceneName", scene)
                    .put("prescriptionAssetPath", file.absolutePath))
            }
        }
        check(SpineFlowServerConfig.BASE_URL == origin &&
            PatientCredentials.credential(context, account) == credential) { "患者绑定已变更，请重新获取处方" }
        PatientDirectiveClient.postV2Receipt(credential.patientToken, order, "assets_ready")
        return JSONObject()
            .put("accountId", account)
            .put("patientId", credential.patientId)
            .put("gender", profile.gender.wireValue)
            .put("actionId", order.items.first().actionId)
            .put("prescriptionOrderId", order.orderId)
            .put("prescriptionContractVersion", 2)
            .put("prescriptionJson", order.toJson().toString())
            .put("preTrainingEventId", preEvent)
            .put("actions", actions)
    }

    private suspend fun download(
        folder: File, origin: String, token: String, orderId: Long, item: AdmissionPrescriptionItemV2
    ): File = withContext(Dispatchers.IO) {
        val asset = item.asset
        require(asset.sha256.matches(Regex("[a-f0-9]{64}")) && asset.fileSize in 1..16L * 1024 * 1024)
        require(asset.downloadPath == "/api/patient/training-directive/orders/$orderId/items/${item.itemId}/asset")
        val file = File(folder, "${asset.sha256}.json")
        if (file.isFile && file.length() == asset.fileSize &&
            MessageDigest.isEqual(MessageDigest.getInstance("SHA-256").digest(file.readBytes()),
                asset.sha256.chunked(2).map { it.toInt(16).toByte() }.toByteArray())) return@withContext file

        val connection = PatientHttpTransport.resolveUrl(origin, asset.downloadPath).openConnection() as HttpURLConnection
        val temp = File.createTempFile("asset-", ".part", folder)
        try {
            connection.instanceFollowRedirects = false
            connection.connectTimeout = 10000
            connection.readTimeout = 30000
            connection.setRequestProperty("Authorization", "Bearer $token")
            check(connection.responseCode == 200) { "处方动作资源下载失败" }
            val digest = MessageDigest.getInstance("SHA-256")
            var total = 0L
            connection.inputStream.use { input ->
                temp.outputStream().use { output ->
                    val buffer = ByteArray(8192)
                    while (true) {
                        val count = input.read(buffer)
                        if (count < 0) break
                        total += count
                        check(total <= asset.fileSize) { "处方动作资源大小不符" }
                        digest.update(buffer, 0, count)
                        output.write(buffer, 0, count)
                    }
                }
            }
            check(total == asset.fileSize && digest.digest().joinToString("") { "%02x".format(it) } == asset.sha256) {
                "处方动作资源校验失败"
            }
            android.system.Os.rename(temp.absolutePath, file.absolutePath)
            file
        } finally {
            connection.disconnect()
            temp.delete()
        }
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
                val completed = row?.optBoolean("prescriptionModeCompleted") == true
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
            ResultItem(item.itemId, item.position, item.actionId, item.asset.actionVersionId, item.asset.variantKey,
                item.asset.sha256, status, modes)
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
