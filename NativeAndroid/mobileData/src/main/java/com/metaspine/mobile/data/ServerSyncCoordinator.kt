package com.metaspine.mobile.data

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import kotlinx.coroutines.*
import org.json.JSONArray
import org.json.JSONObject

/** Persistent, silent S01/V2 outbox. Every request belongs to its original hospital binding. */
class ServerSyncCoordinator(
    private val context: Context,
    private val scope: CoroutineScope,
    private val loadSyncableUser: (String) -> DemoUserData? = { null },
) {
    private val store = PendingSyncStore(context)
    private val results = ResultRevisionStore(context)
    private var flushJob: Job? = null
    private val debounceJobs = mutableMapOf<String, Job>()
    private val pausedAccountIds = mutableSetOf<String>()
    private val forgottenAccountIds = mutableSetOf<String>()
    private val generations = mutableMapOf<String, Int>()
    private val connectivity = context.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
    private val callback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) { onForeground() }
    }
    var onCredentialsRejected: ((String) -> Unit)? = null

    init {
        connectivity?.registerDefaultNetworkCallback(callback)
        scope.launch { recover(); flush() }
    }

    fun close() { connectivity?.unregisterNetworkCallback(callback) }

    fun invalidateInflight(accountId: String) {
        generations[accountId] = (generations[accountId] ?: 0) + 1
        debounceJobs.remove(accountId)?.cancel()
    }

    fun forgetAccount(accountId: String) {
        invalidateInflight(accountId)
        forgottenAccountIds += accountId
        for (key in store.pendingAccountIds()) {
            if (bindingOf(key)?.accountId == accountId || key == accountId) store.clearAccount(key)
        }
        BindingScopeStore.load(context, accountId)?.let { results.clearScope(it.storageKey()) }
        store.clearAccount(accountId)
    }

    /** Called BEFORE saving local data, so restart cannot miss a committed edit. */
    fun markDirty(accountId: String) { if (accountId !in forgottenAccountIds) store.markDirty(accountId) }

    fun onUserChanged(user: DemoUserData) {
        if (user.accountId in forgottenAccountIds) return
        markDirty(user.accountId)
        debounceJobs.remove(user.accountId)?.cancel()
        debounceJobs[user.accountId] = scope.launch {
            delay(5_000)
            enqueueUser(user)
            flush()
        }
    }

    fun onForeground() { scope.launch { recover(); flush() } }

    fun resumeAfterCredentialsUpdate(user: DemoUserData? = null) {
        user?.let { forgottenAccountIds.remove(it.accountId); markDirty(it.accountId) }
        pausedAccountIds.clear()
        onForeground()
    }

    private fun current(binding: BindingScope, credential: PatientServerCredential? = null): Boolean =
        binding.accountId !in forgottenAccountIds && binding.matches(BindingScopeStore.load(context, binding.accountId)) &&
            binding.matchesOriginAndPatient(SpineFlowServerConfig.BASE_URL,
                PatientCredentials.credential(context, binding.accountId)?.patientId.orEmpty()) &&
            (credential == null || credential == PatientCredentials.credential(context, binding.accountId))

    private fun bindingOf(key: String): BindingScope? = runCatching { BindingScope.fromJson(JSONObject(key)) }.getOrNull()

    fun submitResult(accountId: String, result: PrescriptionResultV2): ResultRecord? {
        val binding = BindingScopeStore.load(context, accountId) ?: return null
        if (!current(binding)) return null
        val record = results.freeze(binding.storageKey(), result, binding.patientId)
        if (record.ackState == AckState.Acked) return null
        enqueueResult(binding, record)
        return record
    }

    private fun enqueueResult(binding: BindingScope, record: ResultRecord) {
        val key = binding.storageKey()
        val id = "$RESULT:${record.sessionId}:${record.resultRevision}"
        if (store.isQuarantined(key, id)) return
        for (id in store.pendingIds(key)) {
            val row = store.element(key, id) ?: continue
            val refs = QueueCursor.fromJson(row.optJSONObject("cursor")).resultReceipts
            if (row.optString("kind") == RESULT && refs.any { it.sessionId == record.sessionId && it.resultRevision < record.resultRevision }) store.remove(key, id)
        }
        if (!store.hasElement(key, id)) store.enqueue(key, queueElement(id, RESULT,
            JSONObject().put("sessions", JSONArray().put(JSONObject(record.body))),
            QueueCursor(resultScope = key, resultReceipts = listOf(record.receipt))))
    }

    private fun enqueueUser(user: DemoUserData) {
        val binding = BindingScopeStore.load(context, user.accountId) ?: return
        if (!current(binding) || (user.syncBindingKey.isNotBlank() && user.syncBindingKey != binding.storageKey())) return
        val key = binding.storageKey()
        if (store.isQueueCorrupted(key)) return
        val scopedUser = HealthEventBridge.forCurrentBinding(user)
        // Include queued cursors when computing new work. Existing event bodies are never replaced.
        var snapshot = store.snapshot(key)
        for (id in store.pendingIds(key)) {
            val cursor = QueueCursor.fromJson(store.element(key, id)?.optJSONObject("cursor"))
            snapshot = snapshot.afterSync(cursor.painIds, cursor.qHashes, cursor.profileHash,
                cursor.lifecycle, cursor.imuCalibrationState, cursor.feedbackEventIds)
        }
        for (item in HealthEventBridge.batches(scopedUser, snapshot, binding.patientId)) {
            val id = item.getString("id")
            if (!store.hasElement(key, id) && !store.isQuarantined(key, id)) store.enqueue(key, item)
        }
        for (record in scopedUser.history) {
            if (record.networkResultJson.isBlank()) continue
            try {
                val parsed = PrescriptionResultV2.parse(JSONObject(record.networkResultJson), binding.patientId)
                require(parsed.sessionId == record.sessionId && parsed.preTrainingEventId == record.preTrainingEventId) { "训练身份与历史不一致" }
                submitResult(user.accountId, parsed)
            } catch (error: Exception) {
                android.util.Log.w(TAG, "训练网络快照未满足合同，保留本地记录", error)
            }
        }
        // Covers freeze -> enqueue interruptions without regenerating the body/hash/revision.
        results.pending(key).forEach { enqueueResult(binding, it) }
        store.clearDirty(user.accountId)
    }

    private fun recover() {
        // V1 tasks are retired. Unscoped S01/V2 remain held, never retargeted to a new PAT.
        for (key in store.pendingAccountIds()) {
            for (id in store.pendingIds(key)) {
                if (store.element(key, id)?.optString("kind")?.startsWith("upload") == true) store.remove(key, id)
            }
        }
        for (account in PatientCredentials.boundAccountIds(context) + store.dirtyAccountIds()) {
            if (account in forgottenAccountIds) continue
            val user = loadSyncableUser(account) ?: continue
            runCatching { enqueueUser(user) }.onFailure { android.util.Log.w(TAG, "本地同步事实未满足合同，保留待发", it) }
        }
    }

    private fun next(): Pair<BindingScope, JSONObject>? {
        for (key in store.pendingAccountIds()) {
            val binding = bindingOf(key) ?: continue
            if (!current(binding) || binding.accountId in pausedAccountIds) continue
            val rows = store.pendingIds(key).mapNotNull { store.element(key, it) }
            // S01 always precedes T01; a result cannot starve its own pre-training event.
            val row = rows.firstOrNull { it.optString("kind") == "sync" } ?: rows.firstOrNull() ?: continue
            return binding to row
        }
        return null
    }

    private fun flush() {
        if (flushJob?.isActive == true) return
        flushJob = scope.launch {
            while (TrainingSyncClient.isOnline(context)) {
                val (binding, item) = next() ?: return@launch
                val credential = PatientCredentials.credential(context, binding.accountId) ?: return@launch
                val generation = generations[binding.accountId] ?: 0
                val stillCurrent = { current(binding, credential) && generation == (generations[binding.accountId] ?: 0) }
                val key = binding.storageKey()
                val id = item.getString("id")
                val body = item.getJSONObject("payload")
                val cursor = QueueCursor.fromJson(item.optJSONObject("cursor"))
                val isResult = item.getString("kind") == RESULT
                val response: Result<*> = if (isResult)
                    TrainingSyncClient.uploadV2Sessions(body.getJSONArray("sessions"), cursor.resultReceipts,
                        credential.patientToken, binding.origin)
                else PatientSyncClient.sync(body, credential.patientToken, binding.origin)
                if (!stillCurrent()) continue
                if (response.isSuccess) {
                    if (isResult) {
                        val acknowledged = cursor.resultReceipts.all { receipt ->
                            results.acknowledge(key, receipt.sessionId, receipt.resultRevision, receipt.resultContentSha256)
                        }
                        if (!acknowledged) { store.quarantine(key, id, "结果回执与本地事实不一致"); continue }
                    }
                    store.commitAck(key, id, cursor.toJson())
                    continue
                }
                val failure = response.exceptionOrNull()
                if (failure is ApiHttpException && failure.httpStatus in setOf(401, 403)) {
                    if (pausedAccountIds.add(binding.accountId)) onCredentialsRejected?.invoke(binding.accountId)
                    continue
                }
                if (failure is ApiHttpException && failure.errorCode == "PRE_TRAINING_EVENT_NOT_FOUND") {
                    val sessionId = cursor.resultReceipts.firstOrNull()?.sessionId.orEmpty()
                    val record = results.load(key, sessionId)
                    record?.preTrainingEventId?.let { eventId ->
                        results.blockOnDependency(key, sessionId, record.resultRevision, eventId)
                        val old = store.snapshot(key)
                        store.saveSnapshot(key, old.copy(questionnaireHashes = old.questionnaireHashes - "event:$eventId"))
                        loadSyncableUser(binding.accountId)?.let(::enqueueUser)
                    }
                    if (store.pendingIds(key).any { store.element(key, it)?.optString("kind") == "sync" }) continue
                } else if (failure is ApiHttpException && failure.httpStatus in setOf(400, 409, 413, 422)) {
                    store.quarantine(key, id, "HTTP ${failure.httpStatus}")
                    continue
                }
                val attempts = store.bumpAttempts(key, id).coerceAtLeast(1)
                val seconds = maxOf(listOf(15L, 30L, 60L, 120L, 300L).getOrElse(attempts - 1) { 300L },
                    (failure as? ApiHttpException)?.retryAfterSeconds ?: 0L)
                scope.launch { delay(seconds * 1000); flush() }
                return@launch
            }
        }
    }

    companion object {
        private const val TAG = "ServerSyncCoordinator"
        private const val RESULT = "result:v2"
    }
}

/** 训练上传成功后推进历史游标。 */
internal fun PatientSyncClient.SyncSnapshot.afterUpload(sessionIds: Collection<String>) =
    copy(syncedSessionIds = syncedSessionIds + sessionIds)

/** 患者同步成功后推进疼痛/问卷/档案/状态/反馈游标。 */
internal fun PatientSyncClient.SyncSnapshot.afterSync(
    painIds: Collection<String>,
    questionnaireHashes: Map<String, String>,
    profileHash: String?,
    lifecycle: String?,
    imuCalibrationState: String?,
    feedbackEventIds: Collection<String> = emptySet(),
) = copy(
    syncedPainIds = syncedPainIds + painIds,
    // 评审 #3：参数与属性同名遮蔽，必须合并 this.questionnaireHashes 与本次增量，
    // 否则部分问卷成功同步会清掉其余问卷的历史游标，导致旧问卷被重复发送
    questionnaireHashes = this.questionnaireHashes + questionnaireHashes,
    profileHash = profileHash ?: this.profileHash,
    lifecycle = lifecycle ?: this.lifecycle,
    imuCalibrationState = imuCalibrationState ?: this.imuCalibrationState,
    // P3.5：反馈事件游标独立累积，晚反馈确认后不再重复出站。
    syncedFeedbackEventIds = syncedFeedbackEventIds + feedbackEventIds,
)
