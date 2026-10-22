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
            val feedback = row.optString("feedback").split(" / ", limit = 2)
            TrainingHistoryRecord(row.getString("sessionId"), row.optString("completedAt"),
                row.optString("resultJson"),
                feedback.takeIf { row.optString("feedback").isNotBlank() }?.let {
                    PostTrainingFeedback(it[0], it.getOrElse(1) { "" })
                }, row.optString("preTrainingEventId"), row.optString("planJson"),
                row.optBoolean("feedbackPending"), row.optString("endReason"), row.optString("replaySessionId"))
        }
        val painRecords = objects(input.optJSONArray("painHistory")).map { row ->
            PainHistoryRecord(row.getString("id"), row.getString("recordedAt"), row.optInt("painLevel"),
                PainRecordSource.entries.firstOrNull { it.name == row.optString("source") }
                    ?: PainRecordSource.DailyQuestionnaire,
                strings(row.optJSONArray("painParts")).map { part ->
                    points.find { it.bodyPart == part } ?: PainPoint(part, part, "front", row.optInt("painLevel"))
                })
        }
        val saved = old.copy(
            lifecycle = if (input.optInt("lifecycle") == 1) UserLifecycle.ReturningUser else UserLifecycle.AdmissionPending,
            profile = UserProfile(profile.optInt("heightCm"), profile.optInt("weightKg"),
                gender(profile.optString("gender")), profile.optInt("ageYears")),
            admissionSurvey = if (input.optBoolean("intakeCompleted"))
                AdmissionSurvey(input.optInt("latestPainLevel"), input.optBoolean("intakeReviewRequired")) else old.admissionSurvey,
            admissionIntake = if (input.optBoolean("intakeCompleted"))
                AdmissionIntake(input.optString("basicSituation"), strings(input.optJSONArray("symptoms")).firstOrNull().orEmpty(),
                    strings(input.optJSONArray("goals"))) else old.admissionIntake,
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
            history = records, painHistory = painRecords
        )
        users.saveUser(saved)
        // P2.4：落盘后同步打 dirty 标记，早于 5 秒防抖入队——防抖窗口内被强杀仍能重启重算。
        sync.markDirty(id)
        main.post { sync.onUserChanged(syncable(saved)) }
    }

    @JvmStatic fun foreground() { main.post { sync.onForeground() } }

    @JvmStatic fun logout() { identityGeneration.incrementAndGet(); users.saveActiveAccountId(null) }

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
                val priorScope = BindingScopeStore.load(context, account)
                val pending = PatientCredentials.activationPending(context, account)
                    ?: PendingActivationBinding(PatientActivationClient.newRequestId(), code,
                        PatientActivationClient.newProposedToken(), PatientCredentials.installationId(context), account)
                require(pending.activationCode == code) { "有未确认的激活请求，请先使用原手机号重试" }
                check(PatientCredentials.saveActivationPending(context, pending)) { "激活请求未能安全保存" }
                val redeemed = PatientActivationClient.redeem(PatientActivationClient.buildRedeemBody(
                    pending.requestId, pending.activationCode, pending.proposedToken, pending.installationId, account
                )).getOrThrow()
                // P2.7：网络回包在途期间若发生登出/切换（identityGeneration 变化），
                // 不得把旧请求的凭据落到当前身份上。
                require(identityGeneration.get() == generation) { "激活请求已取消，请重试" }
                val nextGeneration = BindingScopeStore.nextGeneration(context, account)
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
                if (bindingChanged) sync.forgetAccount(account) else sync.invalidateInflight(account)
                // P4.2：旧绑定的处方回执任务不得被新 PAT 重放。代际已变，这里只做物理清理。
                priorScope?.let { PrescriptionReceiptStore(context).clearScope(it) }
                check(PatientCredentials.saveActivatedCredential(context, account,
                    PatientServerCredential(redeemed.patientId, pending.proposedToken), redeemed.prescription,
                    redeemed.identity, redeemed.prescriptionV2))
                check(BindingScopeStore.save(context, newScope)) { "医院绑定作用域未能保存" }
                sync.resumeAfterCredentialsUpdate(syncable(users.loadUser(account)))
                return JSONObject(load(account))
            }
            "plan" -> {
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
                val assetStore = PrescriptionAssetStore.forContext(context, bindingScope)
                val refreshed = TrainingDirectiveCoordinator(
                    storage = TrainingDirectiveStore(context),
                    receipts = PrescriptionReceiptStore(context),
                    assets = assetStore,
                    handoffGate = { handoff -> TrainingStartPayload.capabilityGaps(handoff.order) }
                ).refresh(account, credential, bindingScope)
                require(refreshed is DirectiveRefreshResult.Ready) {
                    when (refreshed) {
                        is DirectiveRefreshResult.NotTrainable -> refreshed.message
                        is DirectiveRefreshResult.NotActivated -> refreshed.message
                        is DirectiveRefreshResult.Unavailable -> refreshed.message
                        is DirectiveRefreshResult.CredentialRejected -> refreshed.message
                        else -> "无法获取本次医生处方"
                    }
                }
                val handoff = requireNotNull(refreshed.handoff) { "处方资源尚未完成校验，请重新下拉处方" }
                val payload = TrainingStartPayload.forPrescription(user,
                    if (user.isReturningUser) DemoFlowType.ReturningUser else DemoFlowType.FirstAdmission,
                    credential.patientId, handoff) { file ->
                    PrescriptionAssetStore.contentUriFor(context, file)?.toString()
                }
                val source = JSONObject(payload.toJson())
                val order = handoff.order
                val action = requireNotNull(order.singleItem)
                val coachScene = when (action.actionId) {
                    "bird_dog" -> if (user.profile?.gender == UserGender.Male) "maleBirdDogPractice" else "BirdDogPractice"
                    else -> if (user.profile?.gender == UserGender.Male) "maleDeadBugPractice" else "DeadBugPractice"
                }
                val gameScene = if (action.actionId == "bird_dog") "BirdDogMinimal" else "SpineV2Minimal"
                val actions = source.getJSONObject("trainingPlan").getJSONArray("recommendedActions")
                objects(actions).forEach { it.put("sceneName", if (it.getString("arTaskType") == "coach_training") coachScene else gameScene) }
                return JSONObject().put("sessionId", UUID.randomUUID().toString()).put("accountId", account)
                    .put("patientId", credential.patientId).put("gender", user.profile?.gender?.wireValue)
                    .put("actionId", action.actionId).put("targetScene", coachScene).put("actions", actions)
                    .put("prescriptionOrderId", order.orderId).put("prescriptionJson", source.getJSONObject("trainingPlan").toString())
                    .put("preTrainingEventId", user.latestPreTrainingCheck?.recordedAt?.takeIf { it.isNotBlank() }?.let { "pre:$it" }.orEmpty())
            }
            "routineRefresh", "routineAnswer", "routineSubmit" -> {
                val credential = requireNotNull(PatientCredentials.credential(context, account)) { "请先激活医生档案" }
                val origin = SpineFlowServerConfig.BASE_URL
                val bound = RoutineScope.of(account, credential, origin)
                // P2.7：在途回包只有「凭据、活动账号、Origin 均未变」才可写回，
                // 切源后旧问卷回包不得修改新绑定的答卷/缓存。
                val current = { PatientCredentials.credential(context, account) == credential &&
                    users.activeAccountId() == account &&
                    SpineFlowServerConfig.BASE_URL == origin }
                var message = ""
                if (command == "routineRefresh") message = routine.refresh(bound, credential, current).message
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
                .put("completedAt", row.completedAt.orEmpty()).put("availability", row.availability)
                .put("problem", row.problem.orEmpty()).put("questions", questions))
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
            .put("symptomChange", daily?.symptomChange.orEmpty()).put("intakeCompleted", user.admissionIntake != null)
            .put("intakeReviewRequired", user.admissionSurvey?.requiresClinicalReview ?: false)
            .put("admissionCompleted", assessment != null).put("admissionReviewRequired", assessment?.requiresClinicalReview() ?: false)
            .put("preTrainingCheckPassed", daily?.allowedToTrain == true && user.history.none { it.preTrainingEventId == "pre:${daily.recordedAt}" }).put("preTrainingRecordedAt", daily?.recordedAt.orEmpty())
            .put("preTrainingEventId", daily?.recordedAt?.takeIf { it.isNotBlank() }?.let { "pre:$it" }.orEmpty())
            .put("admissionCompletedAt", assessment?.completedAt.orEmpty())
            .put("flexionPainLevel", assessment?.flexion?.painLevel ?: 0).put("extensionPainLevel", assessment?.extension?.painLevel ?: 0)
            .put("flexionResponse", assessment?.flexion?.painResponse?.wireValue.orEmpty()).put("extensionResponse", assessment?.extension?.painResponse?.wireValue.orEmpty())
            .put("flexionPainPhase", assessment?.flexion?.firstPainPhase?.wireValue.orEmpty()).put("extensionPainPhase", assessment?.extension?.firstPainPhase?.wireValue.orEmpty())
            .put("flexionCompletions", assessment?.flexion?.completionCount ?: 0).put("extensionCompletions", assessment?.extension?.completionCount ?: 0)
            .put("painHistory", JSONArray().also { array -> user.painHistory.forEach { row ->
                array.put(JSONObject().put("id", row.id).put("recordedAt", row.recordedAt).put("painLevel", row.painLevel)
                    .put("source", row.source.name).put("painParts", JSONArray(row.painPoints.map { it.bodyPart })))
            } })
            .put("trainingHistory", JSONArray().also { array -> user.history.forEach { row ->
                val result = runCatching { JSONObject(row.resultJson) }.getOrNull()
                array.put(JSONObject().put("sessionId", row.sessionId).put("completedAt", row.completedAt).put("resultJson", row.resultJson)
                    .put("actionId", result?.optJSONArray("actionResults")?.optJSONObject(0)?.optString("actionId").orEmpty())
                    .put("preTrainingEventId", row.preTrainingEventId).put("feedback", row.feedback?.let { "${it.feeling} / ${it.difficulty}" }.orEmpty())
                    .put("completed", result?.optBoolean("completed") ?: false).put("planJson", row.planJson)
                    .put("feedbackPending", row.feedbackPending).put("endReason", row.endReason).put("replaySessionId", row.replaySessionId))
            } })
    }

    private fun gender(value: String): UserGender = when (value) {
        "男", "male" -> UserGender.Male
        "女", "female" -> UserGender.Female
        else -> UserGender.Unspecified
    }
    private fun syncable(user: DemoUserData): DemoUserData = user.copy(
        // P3.5：结果上传不再被「反馈未填」阻塞——真实训练（planJson 非空）完成（endReason 非空）
        // 即可上传；反馈作为独立 S01 事件在填好后自行出站。仍排除 LEGACY_IMPORT。
        history = user.history.filter { it.endReason != "LEGACY_IMPORT" &&
            (it.planJson.isBlank() || it.endReason.isNotBlank()) },
        painHistory = user.painHistory.filterNot { it.id.startsWith("imported:") }
    )
    private fun objects(array: JSONArray?): List<JSONObject> =
        if (array == null) emptyList() else (0 until array.length()).map { array.getJSONObject(it) }
    private fun strings(array: JSONArray?): List<String> =
        if (array == null) emptyList() else (0 until array.length()).map { array.getString(it) }
}
