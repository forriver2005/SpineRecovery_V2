package com.metaspine.mobile

import android.content.Context
import android.content.Intent
import android.os.Handler
import android.os.Looper
import com.metaspine.mobile.data.*
import kotlinx.coroutines.*
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.OffsetDateTime
import java.util.UUID
import java.util.concurrent.atomic.AtomicInteger

/** The Unity shell uses the same databases, Keystore and patient protocol as the phone app. */
object UnifiedMobileBridge {
    private lateinit var context: Context
    private lateinit var users: DemoUserStore
    private lateinit var sync: ServerSyncCoordinator
    private lateinit var loginGuard: AccountLoginGuard
    private lateinit var routineStore: RoutineQuestionnaireStore
    private lateinit var routine: RoutineQuestionnaireCoordinator
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val identityGeneration = AtomicInteger()
    private val main = Handler(Looper.getMainLooper())
    private data class PendingPlan(val token: String, val account: String, val generation: Int,
        val credential: PatientServerCredential, val coordinator: TrainingDirectiveCoordinator?,
        val prepared: DirectiveRefreshResult.Prepared?, val local: Boolean)
    private var pendingPlan: PendingPlan? = null

    @JvmStatic @Synchronized fun initialize(host: Context) {
        if (::context.isInitialized) return
        context = host.applicationContext
        users = DemoUserStore(context)
        SpineFlowServerConfig.baseUrlOverride = PatientCredentials.serverUrlOverride(context)
        sync = ServerSyncCoordinator(context, scope) { accountId ->
            // P2.4 启动重算：按账号加载可同步状态（过滤 LEGACY_IMPORT / imported / 种子数据）。
            runCatching { syncable(users.loadUser(accountId)) }.getOrNull()
        }
        loginGuard = AccountLoginGuard(context, users, sync::forgetAccount)
        loginGuard.recoverPendingDeletions()
        routineStore = RoutineQuestionnaireStore(context)
        routine = RoutineQuestionnaireCoordinator(routineStore)
    }

    @JvmStatic fun authenticate(account: String, password: String): Boolean =
        users.authenticate(account, password)

    @JvmStatic fun register(account: String, password: String): Int {
        val result = users.registerAccount(account, password)
        if (result == AccountRegistrationResult.Success) users.saveActiveAccountId(account.trim().lowercase())
        return result.ordinal
    }

    @JvmStatic @Synchronized fun load(account: String): String = toUnity(users.loadUser(account)).toString()

    @JvmStatic @Synchronized fun save(raw: String) {
        val input = JSONObject(raw)
        val id = input.getString("accountId").trim().lowercase()
        val old = users.loadUser(id)
        val profile = input.optJSONObject("profile") ?: JSONObject()
        val points = strings(input.optJSONArray("painParts")).mapIndexed { index, part ->
            PainPoint(part, part, "front", input.optJSONArray("painLevels")?.optInt(index) ?: 0)
        }
        fun movement(type: AdmissionMovementType, prefix: String) = AdmissionMovementResult(
            type, input.optInt(prefix + "Completions"),
            AdmissionPainResponse.entries.firstOrNull { it.wireValue == input.optString(prefix + "Response") }
                ?: AdmissionPainResponse.Unchanged,
            AdmissionPainPhase.entries.firstOrNull { it.wireValue == input.optString(prefix + "PainPhase") },
            input.optInt(prefix + "PainLevel")
        )
        val records = objects(input.optJSONArray("trainingHistory")).map { row ->
            val feedback = if (row.optString("feedbackFeeling").isNotBlank() && row.optString("feedbackDifficulty").isNotBlank())
                PostTrainingFeedback(row.getString("feedbackFeeling"), row.getString("feedbackDifficulty"), row.optString("feedbackRecordedAt"))
                else old.history.find { it.sessionId == row.getString("sessionId") }?.feedback
            TrainingHistoryRecord(row.getString("sessionId"), row.optString("completedAt"),
                row.optString("resultJson"),
                feedback, row.optString("preTrainingEventId"), row.optString("planJson"),
                row.optBoolean("feedbackPending"), row.optString("endReason"), row.optString("replaySessionId"),
                row.optString("networkResultJson"), row.optString("networkResultError"), row.optString("actionId"))
        }

        val painRecords = objects(input.optJSONArray("painHistory")).map { row ->
            val previous = old.painHistory.find { it.id == row.getString("id") }
            val captured = row.optJSONArray("points")
            val originalPoints = if (row.optBoolean("pointsCaptured") && captured != null)
                objects(captured).map { point ->
                    PainPoint(point.getString("bodyPart"), point.optString("bodyPartLabel"),
                        point.optString("view"), point.getInt("painLevel").also { require(it in 0..10) })
                } else previous?.painPoints.orEmpty()
            val record = PainHistoryRecord(row.getString("id"), row.getString("recordedAt"),
                row.getInt("painLevel").also { require(it in 0..10) },
                PainRecordSource.entries.firstOrNull { it.name == row.optString("source") }
                    ?: PainRecordSource.DailyQuestionnaire, originalPoints,
                strings(row.optJSONArray("painParts")), row.optBoolean("pointsCaptured"), row.optBoolean("painFree"))
            require(previous == null || previous == record) { "疼痛历史不能改写" }
            record
        }
        val currentProfile = UserProfile(profile.optInt("heightCm"), profile.optInt("weightKg"),
            gender(profile.optString("gender")), profile.optInt("ageYears"))
        val healthEvents = HealthEventBridge.captureProfile(
            HealthEventBridge.merge(old.healthEventsJson, input.optJSONArray("healthEvents")), old.profile, currentProfile)
        fun latestAnswers(type: String): JSONObject? = objects(JSONArray(healthEvents))
            .lastOrNull { it.getString("type") == type }?.let { JSONObject(it.getString("answersJson")) }
        val capturedIntake = latestAnswers("admissionIntake")
        val capturedSurvey = latestAnswers("admissionSurvey")
        val saved = old.copy(
            lifecycle = if (input.optInt("lifecycle") == 1) UserLifecycle.ReturningUser else UserLifecycle.AdmissionPending,
            profile = currentProfile,
            admissionSurvey = capturedSurvey?.let { AdmissionSurvey(it.getInt("painLevel"), it.getBoolean("requiresClinicalReview")) } ?: old.admissionSurvey,
            admissionIntake = capturedIntake?.let { AdmissionIntake(it.getString("situation"), it.getString("symptom"), strings(it.optJSONArray("goals"))) } ?: old.admissionIntake,
            intakeReviewRequired = input.optBoolean("intakeReviewRequired", old.intakeReviewRequired),
            latestPreTrainingCheck = input.optString("preTrainingRecordedAt").takeIf { it.isNotBlank() }?.let {
                old.latestPreTrainingCheck?.takeIf { check -> check.recordedAt == it }
                    ?: DailyCheck(input.optInt("latestPainLevel"), input.optString("symptomChange"), false,
                        input.optBoolean("preTrainingCheckPassed"), it)
            } ?: old.latestPreTrainingCheck,
            latestPainPoints = points,
            mobileAdmissionAssessment = if (input.optBoolean("admissionCompleted"))
                MobileAdmissionAssessment(movement(AdmissionMovementType.Flexion, "flexion"),
                    movement(AdmissionMovementType.Extension, "extension"), input.optString("admissionCompletedAt"))
                else old.mobileAdmissionAssessment,
            history = records, painHistory = painRecords,
            healthEventsJson = healthEvents,
            syncBindingKey = old.syncBindingKey.ifBlank { BindingScopeStore.load(context, id)?.storageKey().orEmpty() },
            monthlyHistoryJson = input.optJSONArray("monthlyHistory")?.toString() ?: old.monthlyHistoryJson,
            trackingCalibratedAt = input.optString("trackingCalibratedAt", old.trackingCalibratedAt),
            imuCalibrationState = if (input.optString("trackingCalibratedAt").isNotBlank()) ImuCalibrationState.Calibrated else old.imuCalibrationState
        )
        // Mark before saving: a crash between commits rechecks the old state instead of losing new facts.
        sync.markDirty(id)
        users.saveUser(saved)
        main.post { sync.onUserChanged(syncable(saved)) }

        // P5.7：识别并提交 V2 训练结果（protocolVersion = 2）到 V2 上传通道
        for (record in records) {
            val resultJson = record.resultJson.takeIf { it.isNotBlank() } ?: continue
            val resultObj = runCatching { JSONObject(resultJson) }.getOrNull() ?: continue
            val protocolVersion = resultObj.optInt("protocolVersion", 1)

            if (protocolVersion == 2) {
                // V2 格式：解析并提交到 V2 结果存储
                val patientId = PatientCredentials.credential(context, id)?.patientId
                if (patientId != null) {
                    try {
                        val v2Result = PrescriptionResultV2.parse(resultObj, patientId)
                        sync.submitResult(id, v2Result)
                    } catch (error: ResultInputException) {
                        android.util.Log.w("UnifiedMobileBridge",
                            "V2 结果解析失败: sessionId=${record.sessionId}, error=${error.message}")
                    }
                }
            }
        }
    }

    @JvmStatic fun foreground() { main.post { sync.onForeground() } }

    @JvmStatic fun logout() { identityGeneration.incrementAndGet(); pendingPlan = null; users.saveActiveAccountId(null) }

    @JvmStatic fun openLegacyImport(account: String) {
        require(users.activeAccountId() == account) { "请先登录要导入的账号" }
        context.startActivity(Intent(context, LegacyArchiveImportActivity::class.java)
            .putExtra("accountId", account).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }

    @JvmStatic fun importedReplayIds(account: String): String {
        val key = "replays:$account"
        return SecureLocalData.read(context.getSharedPreferences("unified_legacy_import", Context.MODE_PRIVATE), key) ?: "[]"
    }

    @JvmStatic @Synchronized fun addImportedReplays(account: String, raw: String) {
        require(users.activeAccountId() == account) { "账号已切换，请重新导入" }
        val ids = (strings(JSONArray(importedReplayIds(account))) + strings(JSONArray(raw))).distinct()
        val key = "replays:$account"
        check(context.getSharedPreferences("unified_legacy_import", Context.MODE_PRIVATE).edit()
            .putString(key, SecureLocalData.seal(key, JSONArray(ids).toString())).commit())
    }

    @JvmStatic @Synchronized fun importLegacyUser(account: String, raw: String): Int {
        require(users.activeAccountId() == account) { "账号已切换，请重新导入" }
        val legacy = JSONObject(raw)
        require(legacy.getString("accountId").trim().lowercase() == account) { "备份账号与当前账号不一致" }
        val current = JSONObject(load(account))
        var added = 0
        for ((field, identity) in listOf("trainingHistory" to "sessionId", "painHistory" to "id")) {
            val rows = current.getJSONArray(field)
            val known = objects(rows).map { it.getString(identity) }.toMutableSet()
            for (row in objects(legacy.optJSONArray(field))) {
                val original = row.optString(identity)
                val timestamp = row.optString(if (field == "trainingHistory") "completedAt" else "recordedAt")
                require(original.isNotBlank() && timestamp.isNotBlank()) { "旧记录缺少身份或时间" }
                val hash = java.security.MessageDigest.getInstance("SHA-256")
                    .digest("$account|$original|$timestamp".toByteArray()).joinToString("") { "%02x".format(it) }
                val id = "imported:$hash"
                if (!known.add(id)) continue
                row.put(identity, id)
                if (field == "trainingHistory") row.put("endReason", "LEGACY_IMPORT").put("feedbackPending", false).put("planJson", "")
                rows.put(row)
                added++
            }
        }
        save(current.toString())
        return added
    }

    @JvmStatic fun dispatch(requestId: String, command: String, raw: String, callbackObject: String) {
        val generation = identityGeneration.get()
        scope.launch {
            val response = JSONObject().put("requestId", requestId)
            try {
                val body = JSONObject(raw)
                val data = execute(command, body, generation)
                response.put("ok", true).put("dataJson", data.toString())
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                response.put("ok", false).put("error", error.message ?: "操作未完成，请重试")
            }
            Class.forName("com.unity3d.player.UnityPlayer")
                .getMethod("UnitySendMessage", String::class.java, String::class.java, String::class.java)
                .invoke(null, callbackObject, "OnNativeReply", response.toString())
        }
    }

    private suspend fun execute(command: String, body: JSONObject, generation: Int): JSONObject {
        val account = body.optString("accountId").trim().lowercase()
        when (command) {
            "login" -> {
                require(users.authenticate(account, body.getString("password"))) { "账号或密码错误" }
                val checked = loginGuard.verify(account)
                require(checked is AccountLoginResult.Allowed) { (checked as AccountLoginResult.Blocked).message }
                require(identityGeneration.get() == generation) { "登录请求已取消" }
                users.saveActiveAccountId(account)
                return JSONObject(load(account))
            }
            "previewActivation" -> {
                val preview = PatientActivationClient.preview(body.getString("code"),
                    PatientCredentials.installationId(context)).getOrThrow()
                return JSONObject().put("message", "请确认：${preview.displayName}，${preview.idCardMasked}，医生 ${preview.doctorUsername}")
            }
            "activate" -> {
                val code = PatientActivationClient.normalizeInput(body.getString("code"))
                require(PatientActivationClient.isComplete(code)) { "请输入有效的11位建档手机号" }
                val origin = SpineFlowServerConfig.BASE_URL
                val serviceIdentity = ServiceIdentityClient.requireHospital(
                    ServiceIdentityClient.fetch().getOrThrow())
                require(SpineFlowServerConfig.BASE_URL == origin) { "医院地址已变化，请重试" }
                val priorScope = BindingScopeStore.load(context, account)
                val existingPending = PatientCredentials.activationPending(context, account)
                    ?: PendingActivationBinding(PatientActivationClient.newRequestId(), code,
                        PatientActivationClient.newProposedToken(), PatientCredentials.installationId(context), account)
                require(existingPending.origin.isBlank() ||
                    (existingPending.origin == origin && existingPending.serviceId == serviceIdentity.serviceId &&
                        existingPending.hospitalKey == serviceIdentity.hospitalKey)) { "请连接原医院后重试激活" }
                val pending = existingPending.copy(origin = origin, serviceId = serviceIdentity.serviceId,
                    hospitalKey = serviceIdentity.hospitalKey)
                require(pending.activationCode == code) { "有未确认的激活请求，请先使用原手机号重试" }
                check(PatientCredentials.saveActivationPending(context, pending)) { "激活请求未能安全保存" }
                val redeemed = PatientActivationClient.redeem(PatientActivationClient.buildRedeemBody(
                    pending.requestId, pending.activationCode, pending.proposedToken, pending.installationId, account
                )).getOrThrow()
                // P2.7：网络回包在途期间若发生登出/切换（identityGeneration 变化），
                // 不得把旧请求的凭据落到当前身份上。
                require(identityGeneration.get() == generation && SpineFlowServerConfig.BASE_URL == origin &&
                    BindingScopeStore.load(context, account) == priorScope) { "激活请求已取消，请重试" }
                val sameBinding = priorScope?.matchesOriginAndPatient(origin, redeemed.patientId) == true &&
                    priorScope.matchesHospital(serviceIdentity) &&
                    PatientCredentials.credential(context, account)?.patientToken == pending.proposedToken
                val nextGeneration = if (sameBinding) priorScope!!.credentialGeneration
                    else BindingScopeStore.nextGeneration(context, account)
                val newScope = BindingScope(
                    origin = origin,
                    serviceId = serviceIdentity.serviceId,
                    hospitalKey = serviceIdentity.hospitalKey,
                    hospitalName = serviceIdentity.hospitalName,
                    patientId = redeemed.patientId,
                    accountId = account,
                    credentialGeneration = nextGeneration
                )
                // P2.7：换绑（Origin/医院/患者变化）时旧队列属于旧绑定，绝不能用新 PAT
                // 发往新医院——作废旧队列与在途回包；同院重放仅作废在途回包，保留合法未确认任务。
                val bindingChanged = priorScope != null && (priorScope.origin != newScope.origin ||
                    priorScope.serviceId != newScope.serviceId ||
                    priorScope.hospitalKey != newScope.hospitalKey ||
                    priorScope.patientId != newScope.patientId)
                sync.invalidateInflight(account)
                // P4.2：旧绑定的处方回执任务不得被新 PAT 重放。代际已变，这里只做物理清理。
                priorScope?.let { PrescriptionReceiptStore(context).clearScope(it) }
                check(PatientCredentials.saveActivatedCredential(context, account,
                    PatientServerCredential(redeemed.patientId, pending.proposedToken), redeemed.prescription,
                    redeemed.identity, redeemed.prescriptionV2, newScope))
                val local = users.loadUser(account)
                users.saveUser(HealthEventBridge.rebind(local, newScope.storageKey()))
                sync.resumeAfterCredentialsUpdate(syncable(users.loadUser(account)))
                return JSONObject(load(account))
            }
            "refreshDirective" -> {
                val credential = requireNotNull(PatientCredentials.credential(context, account))
                val binding = requireNotNull(BindingScopeStore.load(context, account))
                val current = { candidate: BindingScope ->
                    identityGeneration.get() == generation && users.activeAccountId() == account &&
                        candidate.matches(BindingScopeStore.load(context, account)) &&
                        candidate.matchesOriginAndPatient(SpineFlowServerConfig.BASE_URL, credential.patientId) &&
                        PatientCredentials.credential(context, account) == credential
                }
                require(current(binding)) { "医院绑定已变化" }
                val received = TrainingDirectiveCoordinator(
                    storage = TrainingDirectiveStore(context), receipts = PrescriptionReceiptStore(context),
                    isCurrent = current
                ).receiveOnly(account, credential, binding)
                require(received is DirectiveRefreshResult.Received) {
                    when (received) {
                        is DirectiveRefreshResult.Unavailable -> received.message
                        is DirectiveRefreshResult.CredentialRejected -> received.message
                        else -> "医生指令暂未更新"
                    }
                }
                val snapshot = received.snapshot
                return JSONObject().put("state", snapshot.state.wireValue)
                    .put("receiptPending", received.receiptPending)
                    .put("snapshotJson", snapshot.toStoreJson().toString())
                    .put("prescriptionJson", snapshot.prescription?.prescription?.toJson()?.toString().orEmpty())
            }
            "confirmPlan" -> {
                val plan = requireNotNull(pendingPlan) { "本次训练准备已失效" }
                require(plan.token == body.getString("planToken") && plan.account == account &&
                    plan.generation == generation && users.activeAccountId() == account && body.getBoolean("consumerConfirmed")) { "训练资源尚未完成交接" }
                if (!plan.local) {
                    val coordinator = requireNotNull(plan.coordinator)
                    val prepared = requireNotNull(plan.prepared)
                    val confirmed = coordinator.confirmReady(plan.credential, prepared, true)
                    require(confirmed is DirectiveRefreshResult.Ready) { "本次处方暂不可训练，请重新进入" }
                }
                pendingPlan = null
                return JSONObject().put("planToken", plan.token).put("ready", true)
            }
            "plan" -> {
                pendingPlan = null
                val user = users.loadUser(account)
                val preEvent = user.latestPreTrainingCheck?.recordedAt?.takeIf { it.isNotBlank() }?.let { "pre:$it" }
                require(preEvent != null && user.history.none { it.preTrainingEventId == preEvent }) {
                    "请重新完成本次训练前检查"
                }
                val credential = requireNotNull(PatientCredentials.credential(context, account)) { "请先激活医生档案" }
                // 处方文件只按当前绑定作用域存取；作用域与 PAT 患者不一致时不得交接。
                val bindingScope = requireNotNull(BindingScopeStore.load(context, account)) {
                    "医院绑定信息缺失，请重新激活医生档案"
                }
                require(bindingScope.patientId == credential.patientId &&
                    bindingScope.origin == SpineFlowServerConfig.BASE_URL) {
                    "医院绑定已变更，请重新激活医生档案"
                }
                val isCurrent: (BindingScope) -> Boolean = { candidate ->
                    identityGeneration.get() == generation && users.activeAccountId() == account &&
                        candidate.matches(BindingScopeStore.load(context, account)) &&
                        candidate.matchesOriginAndPatient(SpineFlowServerConfig.BASE_URL, credential.patientId) &&
                        PatientCredentials.credential(context, account) == credential
                }

                // Built-in doctor actions use the scenes bundled in the APK. Receive only the
                // prescription metadata and dose; do not download the motion package.
                val localCoordinator = TrainingDirectiveCoordinator(
                    storage = TrainingDirectiveStore(context),
                    receipts = PrescriptionReceiptStore(context),
                    isCurrent = isCurrent
                )
                val locallyReceived = localCoordinator.receiveOnly(account, credential, bindingScope)
                if (locallyReceived is DirectiveRefreshResult.Received) {
                    val localOrder = locallyReceived.snapshot.prescription
                    val localItem = localOrder?.singleItem
                    val localDefinition = localItem?.let { LocalActionCatalog.find(it.actionId) }
                    if (localOrder != null && localItem != null && localDefinition != null &&
                        LocalActionCatalog.isLocallyExecutable(localItem.actionId)) {
                        val payload = TrainingStartPayload.forLocalPrescription(
                            user,
                            if (user.isReturningUser) DemoFlowType.ReturningUser else DemoFlowType.FirstAdmission,
                            credential.patientId,
                            localOrder
                        )
                        val source = JSONObject(payload.toJson())
                        val actions = source.getJSONObject("trainingPlan").getJSONArray("recommendedActions")
                        val coachScene = requireNotNull(localDefinition.coachScene(user.profile?.gender == UserGender.Male))
                        val gameScene = requireNotNull(localDefinition.gameScene)
                        objects(actions).forEach { row ->
                            row.put("sceneName", if (row.getString("arTaskType") == "coach_training") coachScene else gameScene)
                        }
                        val sessionId = UUID.randomUUID().toString()
                        val planToken = UUID.randomUUID().toString()
                        pendingPlan = PendingPlan(planToken, account, generation, credential, localCoordinator, null, true)
                        val startedAt = java.time.Instant.now().truncatedTo(java.time.temporal.ChronoUnit.MILLIS).toString()
                        return JSONObject().put("sessionId", sessionId).put("requestToken", sessionId)
                            .put("planToken", planToken).put("startedAt", startedAt).put("accountId", account)
                            .put("patientId", credential.patientId).put("gender", user.profile?.gender?.wireValue)
                            .put("actionId", localItem.actionId).put("targetScene", coachScene).put("actions", actions)
                            .put("prescriptionOrderId", localOrder.orderId)
                            .put("prescriptionJson", source.getJSONObject("trainingPlan").toString())
                            .put("preTrainingEventId", user.latestPreTrainingCheck?.recordedAt?.takeIf { it.isNotBlank() }?.let { "pre:$it" }.orEmpty())
                    }
                }

                val assetStore = PrescriptionAssetStore.forContext(context, bindingScope)
                val coordinator = TrainingDirectiveCoordinator(
                    storage = TrainingDirectiveStore(context),
                    receipts = PrescriptionReceiptStore(context),
                    assets = assetStore,
                    handoffGate = { handoff -> TrainingStartPayload.capabilityGaps(handoff.order) },
                    isCurrent = isCurrent
                )
                val refreshed = coordinator.prepare(account, credential, bindingScope)
                require(refreshed is DirectiveRefreshResult.Prepared) {
                    when (refreshed) {
                        is DirectiveRefreshResult.NotTrainable -> refreshed.message
                        is DirectiveRefreshResult.NotActivated -> refreshed.message
                        is DirectiveRefreshResult.Unavailable -> refreshed.message
                        is DirectiveRefreshResult.CredentialRejected -> refreshed.message
                        else -> "无法获取本次医生处方"
                    }
                }
                val handoff = requireNotNull(refreshed.handoff) { "处方资源尚未完成校验，请重新下拉处方" }
                require(identityGeneration.get() == generation &&
                    bindingScope.matches(BindingScopeStore.load(context, account)) &&
                    bindingScope.origin == SpineFlowServerConfig.BASE_URL) { "医院绑定已变化" }
                val payload = TrainingStartPayload.forPrescription(user,
                    if (user.isReturningUser) DemoFlowType.ReturningUser else DemoFlowType.FirstAdmission,
                    credential.patientId, handoff) { file ->
                    PrescriptionAssetStore.readableContentUriFor(context, file)?.toString()
                }
                val source = JSONObject(payload.toJson())
                val order = handoff.order
                val action = requireNotNull(order.singleItem)
                val mapped = LocalActionCatalog.find(action.actionId)
                val coachScene = mapped?.coachScene(user.profile?.gender == UserGender.Male)
                    ?: if (action.actionId == "bird_dog") {
                        if (user.profile?.gender == UserGender.Male) "maleBirdDogPractice" else "BirdDogPractice"
                    } else {
                        if (user.profile?.gender == UserGender.Male) "maleDeadBugPractice" else "DeadBugPractice"
                    }
                val gameScene = mapped?.gameScene
                    ?: if (action.actionId == "bird_dog") "BirdDogMinimal" else "SpineV2Minimal"
                val actions = source.getJSONObject("trainingPlan").getJSONArray("recommendedActions")
                objects(actions).forEach { it.put("sceneName", if (it.getString("arTaskType") == "coach_training") coachScene else gameScene) }
                val sessionId = UUID.randomUUID().toString()
                val planToken = UUID.randomUUID().toString()
                pendingPlan = PendingPlan(planToken, account, generation, credential, coordinator, refreshed, false)
                val startedAt = java.time.Instant.now().truncatedTo(java.time.temporal.ChronoUnit.MILLIS).toString()
                return JSONObject().put("sessionId", sessionId).put("requestToken", sessionId)
                    .put("planToken", planToken).put("startedAt", startedAt).put("accountId", account)
                    .put("patientId", credential.patientId).put("gender", user.profile?.gender?.wireValue)
                    .put("actionId", action.actionId).put("targetScene", coachScene).put("actions", actions)
                    .put("prescriptionOrderId", order.orderId).put("prescriptionJson", source.getJSONObject("trainingPlan").toString())
                    .put("preTrainingEventId", user.latestPreTrainingCheck?.recordedAt?.takeIf { it.isNotBlank() }?.let { "pre:$it" }.orEmpty())
            }
            "routineRefresh", "routineAnswer", "routineSave", "routineSubmit" -> {
                val credential = requireNotNull(PatientCredentials.credential(context, account)) { "请先激活医生档案" }
                val origin = SpineFlowServerConfig.BASE_URL
                val binding = requireNotNull(BindingScopeStore.load(context, account))
                require(binding.matchesOriginAndPatient(origin, credential.patientId))
                val bound = RoutineScope.of(account, credential, origin, binding)
                // P2.7：在途回包只有「凭据、活动账号、Origin 均未变」才可写回，
                // 切源后旧问卷回包不得修改新绑定的答卷/缓存。
                val current = { identityGeneration.get() == generation && binding.matches(BindingScopeStore.load(context, account)) &&
                    PatientCredentials.credential(context, account) == credential &&
                    users.activeAccountId() == account &&
                    SpineFlowServerConfig.BASE_URL == origin }
                var message = ""
                if (command == "routineRefresh") message = routine.refresh(bound, credential, current).message
                if (command == "routineSave") {
                    routine.saveDraft(bound, body.getLong("taskId"), body.getString("answersJson"), current)
                }
                if (command == "routineAnswer") {
                    val taskId = body.getLong("taskId")
                    val row = routineStore.read(bound).find { it.task.taskId == taskId } ?: error("请先刷新问卷")
                    val answers = JSONObject(row.answers)
                    val key = body.getString("questionId")
                    val value: Any = when (body.getString("answerType")) {
                        "scale" -> body.getInt("numberValue")
                        "boolean" -> body.getBoolean("boolValue")
                        "multi_choice" -> body.getJSONArray("values")
                        else -> body.getString("textValue")
                    }
                    answers.put(key, value)
                    routine.saveDraft(bound, taskId, answers.toString(), current)
                }
                if (command == "routineSubmit") message = routine.submit(bound, credential, body.getLong("taskId"), current)
                return routineResponse(bound).put("message", message)
            }
            "sync" -> { sync.onForeground(); return JSONObject().put("message", "已请求同步，离线记录保留并自动重试") }
            "configUpdate" -> {
                val installed = ConfigUpdateCoordinator.checkOnce(context)
                return JSONObject().put("message", installed?.let { "动作包已更新：${it.displayName}" } ?: "已完成动作包检查")
            }
            else -> error("不支持的业务操作")
        }
    }

    private fun routineResponse(bound: RoutineScope): JSONObject = JSONObject().put("items", JSONArray().also { array ->
        routineStore.read(bound).forEach { row ->
            val schema = row.task.schema
            val questions = JSONArray().also { qs ->
                schema?.questions?.forEach { q ->
                    val answer = JSONObject(row.answers).opt(q.id)
                    qs.put(JSONObject().put("id", q.id).put("type", q.type).put("prompt", q.prompt)
                        .put("required", q.required).put("min", q.min).put("max", q.max).put("step", q.step)
                        .put("maxLength", q.maxLength).put("minSelections", q.minSelections).put("maxSelections", q.maxSelections)
                        .put("answerSet", answer != null).put("numberValue", (answer as? Number)?.toInt() ?: q.min)
                        .put("boolValue", answer as? Boolean ?: false).put("textValue", answer as? String ?: "")
                        .put("values", answer as? JSONArray ?: JSONArray())
                        .put("options", JSONArray().also { os -> q.options.forEach { os.put(JSONObject().put("value", it.value).put("label", it.label)) } }))
                }
            }
            array.put(JSONObject().put("taskId", row.task.taskId).put("name", schema?.title ?: row.task.name)
                .put("templateCode", row.task.code).put("availableAt", row.task.availableAt)
                .put("completedAt", row.completedAt.orEmpty()).put("availability", row.availability)
                .put("pendingSubmission", row.pendingBody != null)
                .put("problem", row.problem.orEmpty()).put("questions", questions)
                .put("templateVersion", row.task.version).put("schemaJson", JSONObject(row.task.raw).optJSONObject("template")?.optJSONObject("schema")?.toString().orEmpty())
                .put("answersJson", row.answers).put("recordId", row.recordId ?: JSONObject.NULL)
                .put("submissionStatus", row.submissionStatus.orEmpty()).put("problemCode", row.problemCode.orEmpty())
                .put("fieldErrors", row.fieldErrors.orEmpty()))
        }
    })

    private fun toUnity(user: DemoUserData): JSONObject {
        val credential = PatientCredentials.credential(context, user.accountId)
        val daily = user.latestPreTrainingCheck
        val assessment = user.mobileAdmissionAssessment
        return JSONObject().put("accountId", user.accountId).put("role", 0)
            .put("lifecycle", if (user.isReturningUser) 1 else 0)
            .put("profile", JSONObject().put("accountId", user.accountId).put("patientId", credential?.patientId.orEmpty())
                .put("activationBound", credential != null).put("gender", user.profile?.gender?.wireValue.orEmpty())
                .put("ageYears", user.profile?.ageYears ?: 0).put("heightCm", user.profile?.heightCm ?: 0)
                .put("weightKg", user.profile?.weightKg ?: 0))
            .put("userTags", JSONArray(user.userTags)).put("basicSituation", user.admissionIntake?.situation.orEmpty())
            .put("symptoms", JSONArray(listOfNotNull(user.admissionIntake?.symptom))).put("goals", JSONArray(user.admissionIntake?.goals.orEmpty()))
            .put("painParts", JSONArray(user.latestPainPoints.map { it.bodyPart })).put("painLevels", JSONArray(user.latestPainPoints.map { it.painLevel }))
            .put("painFree", user.latestPainPoints.none { it.painLevel > 0 }).put("latestPainLevel", daily?.prePainLevel ?: user.admissionSurvey?.painLevel ?: 0)
            .put("symptomChange", daily?.symptomChange.orEmpty()).put("intakeCompleted", user.admissionSurvey != null)
            .put("intakeReviewRequired", user.intakeReviewRequired)
            .put("admissionCompleted", assessment != null).put("admissionReviewRequired", assessment?.requiresClinicalReview() ?: false)
            .put("preTrainingCheckPassed", daily?.allowedToTrain == true && user.history.none { it.preTrainingEventId == "pre:${daily.recordedAt}" }).put("preTrainingRecordedAt", daily?.recordedAt.orEmpty())
            .put("preTrainingEventId", daily?.recordedAt?.takeIf { it.isNotBlank() }?.let { "pre:$it" }.orEmpty())
            .put("admissionCompletedAt", assessment?.completedAt.orEmpty())
            .put("flexionPainLevel", assessment?.flexion?.painLevel ?: 0).put("extensionPainLevel", assessment?.extension?.painLevel ?: 0)
            .put("flexionResponse", assessment?.flexion?.painResponse?.wireValue.orEmpty()).put("extensionResponse", assessment?.extension?.painResponse?.wireValue.orEmpty())
            .put("flexionPainPhase", assessment?.flexion?.firstPainPhase?.wireValue.orEmpty()).put("extensionPainPhase", assessment?.extension?.firstPainPhase?.wireValue.orEmpty())
            .put("flexionCompletions", assessment?.flexion?.completionCount ?: 0).put("extensionCompletions", assessment?.extension?.completionCount ?: 0)
            .put("healthEvents", JSONArray(user.healthEventsJson))
            .put("monthlyHistory", JSONArray(user.monthlyHistoryJson))
            .put("trackingCalibratedAt", user.trackingCalibratedAt)
            .put("painHistory", JSONArray().also { array -> user.painHistory.forEach { row ->
                array.put(JSONObject().put("id", row.id).put("recordedAt", row.recordedAt).put("painLevel", row.painLevel)
                    .put("source", row.source.name).put("painParts", JSONArray(row.painParts))
                    .put("pointsCaptured", row.pointsCaptured).put("painFree", row.painFree)
                    .put("points", JSONArray(row.painPoints.map { point -> JSONObject()
                        .put("bodyPart", point.bodyPart).put("bodyPartLabel", point.bodyPartLabel)
                        .put("view", point.view).put("painLevel", point.painLevel) })))
            } })
            .put("trainingHistory", JSONArray().also { array -> user.history.forEach { row ->
                val result = runCatching { JSONObject(row.resultJson) }.getOrNull()
                array.put(JSONObject().put("sessionId", row.sessionId).put("completedAt", row.completedAt).put("resultJson", row.resultJson)
                    .put("actionId", row.actionId)
                    .put("preTrainingEventId", row.preTrainingEventId).put("feedback", row.feedback?.let { "${it.feeling} / ${it.difficulty}" }.orEmpty())
                    .put("feedbackFeeling", row.feedback?.feeling.orEmpty()).put("feedbackDifficulty", row.feedback?.difficulty.orEmpty())
                    .put("feedbackRecordedAt", row.feedback?.recordedAt.orEmpty())
                    .put("completed", result?.optBoolean("completed") ?: false).put("planJson", row.planJson)
                    .put("feedbackPending", row.feedbackPending).put("endReason", row.endReason).put("replaySessionId", row.replaySessionId)
                    .put("networkResultJson", row.networkResultJson).put("networkResultError", row.networkResultError))
            } })
    }

    private fun gender(value: String): UserGender = when (value) {
        "男", "male" -> UserGender.Male
        "女", "female" -> UserGender.Female
        else -> UserGender.Unspecified
    }
    private fun syncable(user: DemoUserData): DemoUserData = user.copy(
        // Valid checkpoints can leave before session completion; feedback remains a separate S01 event.
        history = user.history.filter { it.endReason != "LEGACY_IMPORT" && !it.sessionId.startsWith("imported:") },
        painHistory = user.painHistory.filterNot { it.id.startsWith("imported:") }
    )
    private fun objects(array: JSONArray?): List<JSONObject> =
        if (array == null) emptyList() else (0 until array.length()).map { array.getJSONObject(it) }
    private fun strings(array: JSONArray?): List<String> =
        if (array == null) emptyList() else (0 until array.length()).map { array.getString(it) }
}
