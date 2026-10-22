package com.metaspine.mobile.data

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject

/**
 * M3.6 Step 4：同步调度中枢。
 *
 * 用户状态变化 → 增量 diff → 入队 → 在线时 FIFO 冲刷 → 成功才推进游标；
 * 失败按 15/30/60/120/300s 退避重试，不弹窗、不阻塞 UI（尽力而为语义）。
 */
class ServerSyncCoordinator(
    private val context: Context,
    private val scope: CoroutineScope,
    // P2.4：启动/前台重算时按账号加载「可同步」用户状态（已过滤 LEGACY_IMPORT / 种子数据）。
    private val loadSyncableUser: (String) -> DemoUserData? = { null },
) {
    private val store = PendingSyncStore(context)
    // P5.3/P5.5：结果的修订号与 ACK 状态独立于同步游标，逐条核对回执。
    private val results = ResultRevisionStore(context)
    private var flushJob: Job? = null
    // 评审 #2：防抖任务按账号隔离——单一任务会让两个账号在 5 秒窗口内互相取消
    private val debounceJobs = mutableMapOf<String, Job>()
    // 评审 #6：持有回调与 CM 引用，Activity 销毁时注销，避免回调泄漏
    private var networkCallback: ConnectivityManager.NetworkCallback? = null
    private var connectivityManager: ConnectivityManager? = null

    // SF-01：凭据被拒（PAT 缺失或 401/403）时暂停冲刷的账号。
    // 暂停期间保留队列，不再退避重试；用户更新「服务器设置」后 resumeAfterCredentialsUpdate 恢复。
    private val pausedAccountIds = mutableSetOf<String>()
    private val forgottenAccountIds = mutableSetOf<String>()
    private val accountGenerations = mutableMapOf<String, Int>()

    /** In-flight replies from the old binding may not recreate a removed cursor/queue. */
    fun forgetAccount(accountId: String) {
        forgottenAccountIds.add(accountId)
        accountGenerations[accountId] = (accountGenerations[accountId] ?: 0) + 1
        debounceJobs.remove(accountId)?.cancel()
        pausedAccountIds.add(accountId)
        store.clearAccount(accountId)
    }

    /**
     * P2.7：重新激活/换绑后作废旧绑定的在途回包与防抖任务，但**保留**队列与游标
     * （同一医院重放激活时合法未确认的 V2 任务不能丢）。冲刷在发请求前后比对
     * [accountGenerations]，代际变化即丢弃旧回包，不修改新绑定的凭据/游标/缓存。
     */
    fun invalidateInflight(accountId: String) {
        accountGenerations[accountId] = (accountGenerations[accountId] ?: 0) + 1
        debounceJobs.remove(accountId)?.cancel()
    }

    /** P2.4：用户状态落盘后由桥接层同步调用，先于 5 秒防抖入队打持久 dirty 标记。 */
    fun markDirty(accountId: String) {
        if (accountId in forgottenAccountIds) return
        store.markDirty(accountId)
    }

    /** SF-01：凭据被拒时回调（每个账号只在暂停切换时触发一次），UI 据此提示用户。 */
    var onCredentialsRejected: ((accountId: String) -> Unit)? = null

    companion object {
        private const val TAG = "ServerSyncCoordinator"
        /** P5.7：V2 结果任务（与 V1 `upload:N` 批次任务并存于同一队列）。 */
        private const val KIND_RESULT_V2 = "result:v2"
    }

    init {
        connectivityManager =
            context.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                scope.launch { flush() }
            }
        }
        networkCallback = callback
        // 网络恢复回调（ACCESS_NETWORK_STATE 权限 Manifest 已有）
        connectivityManager?.registerDefaultNetworkCallback(callback)
        // P2.4：冷启动即重算 dirty 账号的待发 diff——覆盖「保存后防抖窗口内被强杀」的场景，
        // 不能只依赖已有队列 flush（此时队列可能为空）。
        scope.launch { recomputePending(); flush() }
    }

    /**
     * 用户状态变更入口（Step 5 由 MainActivity.saveUser 调用）。
     * 防抖 5s：saveUser 调用频繁，只对最终状态做一次 diff。
     * dirty 标记由桥接层在落盘后同步打上，防抖窗口内被杀仍可重启重算。
     */
    fun onUserChanged(user: DemoUserData) {
        if (user.accountId in forgottenAccountIds) return
        val accountId = user.accountId
        store.markDirty(accountId)
        debounceJobs[accountId]?.cancel()
        debounceJobs[accountId] = scope.launch {
            delay(5_000)
            enqueueUserDiff(user)
        }
    }

    /** App 回到前台（Step 5 接 activityResumed = true）。 */
    fun onForeground() {
        scope.launch { recomputePending(); flush() }
    }

    // ---------- P5.7：V2 结果通道 ----------

    /**
     * V2 结果的唯一出站入口：冻结 → 分配修订号 → 单条任务入队 → 冲刷。
     *
     * 一次训练可能产生多次修订（未开始快照 → 首模式完成 → 全部完成），
     * 每次提交都独立成任务，因为服务器要求修订号严格递增，**必须按序到达**：
     * 把多个修订打包进一个批次无法保证服务器接受顺序，也会让 ACK 粒度变粗。
     *
     * @return 实际入队的记录；内容未变（幂等重试）或已完成时返回 null。
     * @throws RevisionConflict 内容变化但修订号回退，或冻结身份被改写。
     * @throws ResultInputException.ResultInvalid 快照本身不合法。
     */
    fun submitResult(accountId: String, result: PrescriptionResultV2): ResultRecord? {
        if (accountId in forgottenAccountIds) return null
        val patientId = PatientCredentials.credential(context, accountId)?.patientId
        if (patientId == null) {
            // 摘要必须注入 patientId；没有身份就无法冻结——保留原结果，等激活完成后重新提交。
            android.util.Log.w(TAG, "缺少患者身份，无法冻结训练结果: sessionId=${result.sessionId}")
            return null
        }
        val record = results.freeze(accountScope(accountId), result, patientId)
        if (record.ackState == AckState.Acked) return null
        enqueueResult(accountId, record)
        scope.launch { flush() }
        return record
    }

    /** 同一 sessionId 只保留最新修订：旧修订先到会让服务器判定为回退冲突。 */
    private fun enqueueResult(accountId: String, record: ResultRecord) {
        // 冻结记录已存在时先清掉该会话更旧的待发任务（崩溃恢复、多修订并发提交都会产生）。
        val queued = store.pendingIds(accountId)
        for (id in queued) {
            val stale = store.element(accountId, id) ?: continue
            val cursor = QueueCursor.fromJson(stale.optJSONObject("cursor"))
            if (stale.optString("kind") != KIND_RESULT_V2) continue
            if (cursor.resultReceipts.none { it.sessionId == record.sessionId }) continue
            if (cursor.resultReceipts.all { it.resultRevision < record.resultRevision }) {
                store.remove(accountId, id)
            }
        }
        val id = "$KIND_RESULT_V2:${record.sessionId}:${record.resultRevision}"
        if (store.hasElement(accountId, id)) return
        store.enqueue(
            accountId,
            queueElement(
                id = id,
                kind = KIND_RESULT_V2,
                payload = JSONObject().put("sessions", JSONArray().put(JSONObject(record.body))),
                cursor = QueueCursor(
                    resultScope = record.scope,
                    resultReceipts = listOf(record.receipt),
                ),
            ),
        )
    }

    private fun accountScope(accountId: String) = "account:$accountId"

    /** 评审 #6：注销网络回调，由 MainActivity.onDestroy 调用。 */
    fun close() {
        networkCallback?.let { callback ->
            connectivityManager?.unregisterNetworkCallback(callback)
        }
        networkCallback = null
        connectivityManager = null
    }

    // ---------- diff 与入队 ----------

    private fun enqueueUserDiff(user: DemoUserData) {
        if (user.accountId in forgottenAccountIds) return
        val accountId = user.accountId
        val snapshot = store.snapshot(accountId)
        val diff = buildPendingDiff(
            user,
            snapshot,
            PatientCredentials.credential(context, accountId)?.patientId
        )

        // 评审 P4：不可上传记录是终态拒绝——直接推进游标，不入队、不无限重试
        if (diff.rejectedSessionIds.isNotEmpty()) {
            store.saveSnapshot(accountId, snapshot.afterUpload(diff.rejectedSessionIds))
        }

        // 入队顺序：先 sync 后 upload（患者行/档案先于训练落库）
        // 评审 #4：同类替换而非追加——离线期间 diff 是累积的，避免同一批事件重复入队
        diff.syncBody?.let { body ->
            store.replaceKind(
                accountId,
                "sync",
                queueElement(
                    id = "sync:${System.currentTimeMillis()}",
                    kind = "sync",
                    payload = body,
                    cursor = QueueCursor(
                        painIds = diff.newPainIds,
                        qHashes = diff.newQuestionnaireHashes,
                        profileHash = diff.newProfileHash,
                        lifecycle = diff.lifecycle,
                        imuCalibrationState = diff.imuCalibrationState,
                        feedbackEventIds = diff.newFeedbackEventIds,
                    ),
                ),
            )
        }
        // SF-06：upload 按 UTF-8 字节/条数双上限拆批——每 chunk 一个队列元素、
        // 独立 ACK、独立推进游标；超大单条（无法进入任何合规批次）与不可上传记录
        // 一并终态隔离（只推进游标），绝不发出必然 413 的整批无限重试。
        // 已入队与隔离的 sessionId 不重复入队，保留服务端 413 拆批后的 FIFO 状态。
        val quarantinedSessions = store.quarantinedUploadSessionIds(accountId)
        val queuedSessions = store.pendingUploadSessionIds(accountId)
        val plan = TrainingSyncClient.planUploadChunks(
            diff.uploadRecords.filterNot {
                it.sessionId in quarantinedSessions || it.sessionId in queuedSessions
            }
        )
        if (plan.oversizedSessionIds.isNotEmpty()) {
            store.saveSnapshot(accountId, snapshot.afterUpload(plan.oversizedSessionIds))
            android.util.Log.e(
                TAG,
                "隔离超大单条 ${plan.oversizedSessionIds.size} 条（超出单批 ${TrainingSyncClient.MAX_UPLOAD_BATCH_UTF8_BYTES} 字节），已推进游标"
            )
        }
        if (plan.chunks.isNotEmpty()) {
            var chunkIndex = 1
            for (chunk in plan.chunks) {
                val chunkSessionIds = chunk.map { it.sessionId }.toSet()
                store.enqueue(
                    accountId,
                    queueElement(
                        id = "upload:${chunkSessionIds.sorted().joinToString(",")}",
                        kind = "upload:$chunkIndex",
                        payload = TrainingSyncClient.buildUploadBody(chunk),
                        cursor = QueueCursor(sessionIds = chunkSessionIds),
                    ),
                )
                chunkIndex += 1
            }
        }
        val totalUploading = plan.chunks.sumOf { it.size }
        if (diff.syncBody != null || plan.chunks.isNotEmpty() ||
            diff.rejectedSessionIds.isNotEmpty() || plan.oversizedSessionIds.isNotEmpty()
        ) {
            android.util.Log.i(
                TAG,
                "入队 sync=${diff.syncBody != null} upload=${totalUploading}条(${plan.chunks.size}批) rejected=${diff.rejectedSessionIds.size}条 oversized=${plan.oversizedSessionIds.size}条"
            )
        }
        // P2.4：diff 已持久入队后清除 dirty 标记；队列损坏时保留标记以便诊断，不静默丢弃。
        if (!store.isQueueCorrupted(accountId)) store.clearDirty(accountId)
        scope.launch { flush() }
    }

    /**
     * P2.4：启动/前台重算所有 dirty 账号的待发 diff。
     * 幂等——buildPendingDiff 以游标快照为基准，upload 再去重已入队/已隔离的 sessionId，
     * sync 用 replaceKind；因此重复重算不会产生重复任务。损坏队列跳过并保留标记待人工诊断。
     */
    private fun recomputePending() {
        val dirty = store.dirtyAccountIds()
        if (dirty.isEmpty()) return
        for (accountId in dirty) {
            if (accountId in forgottenAccountIds) {
                store.clearDirty(accountId)
                continue
            }
            if (store.isQueueCorrupted(accountId)) {
                android.util.Log.e(TAG, "账号 $accountId 队列密文损坏，跳过自动重算，保留待人工诊断")
                continue
            }
            val user = loadSyncableUser(accountId)
            if (user == null) {
                // 账号已删除/无法加载——无待发可言，清标记避免每次启动空转。
                store.clearDirty(accountId)
                continue
            }
            android.util.Log.i(TAG, "启动重算待发 diff: $accountId")
            enqueueUserDiff(user)
        }
    }

    // ---------- 冲刷 ----------

    /** 单飞冲刷：FIFO 逐条处理；成功移除并推进游标；失败停止本轮并退避重试。 */
    private suspend fun flush() {
        if (flushJob?.isActive == true) return
        flushJob = scope.launch {
            while (true) {
                if (!TrainingSyncClient.isOnline(context)) return@launch

                val next = nextPending() ?: return@launch
                val accountId = next.first
                val item = next.second
                val id = item.optString("id")
                val payload = item.optJSONObject("payload")
                if (payload == null) {
                    // 防回归：损坏队列元素丢弃并继续，不能卡死整个冲刷
                    store.remove(accountId, id)
                    continue
                }

                // SF-01：缺 PAT 直接暂停该账号（不发请求，避免必然的 401 与无意义重试）
                val token = PatientCredentials.patientToken(context, accountId)
                if (token == null) {
                    pauseForCredentials(accountId)
                    return@launch
                }

                val generation = accountGenerations[accountId] ?: 0
                val kind = item.optString("kind")
                val cursor = QueueCursor.fromJson(item.optJSONObject("cursor"))
                val result = when {
                    kind == KIND_RESULT_V2 -> uploadFrozenResult(accountId, payload, cursor, token)
                    // SF-06：upload 拆批后 kind 为 "upload:1"、"upload:2"…（按批入队）
                    kind.startsWith("upload") -> TrainingSyncClient.upload(payload, token)
                    else -> PatientSyncClient.sync(payload, token)
                }
                if (generation != (accountGenerations[accountId] ?: 0)) continue
                if (result.isSuccess) {
                    // P2.5：移除队列元素与推进游标在同一次 commit() 内原子完成，
                    // 消除「已移除但游标未推进」的重发窗口。
                    // V2 的 ACK 已在 uploadFrozenResult 内逐条核对并落盘，这里只移除任务。
                    if (store.commitAck(accountId, id, item.optJSONObject("cursor"))) {
                        android.util.Log.i(TAG, "冲刷成功 kind=$kind")
                    } else {
                        android.util.Log.w(TAG, "ACK 提交时队列元素已不存在 kind=$kind")
                    }
                    continue
                }
                // SF-01：401/403 = 凭据被拒（缺失/撤销/归属不符）→ 暂停冲刷并提示，不无限重试
                val failure = result.exceptionOrNull()
                if (failure is ApiHttpException && (failure.httpStatus == 401 || failure.httpStatus == 403)) {
                    pauseForCredentials(accountId)
                    return@launch
                }
                // P5.6：练前问卷未确认——保留任务与原依赖重发，不隔离（隔离会永久丢结果）。
                if (failure is ApiHttpException && failure.errorCode == "PRE_TRAINING_EVENT_NOT_FOUND") {
                    val record = results.load(cursor.resultScope.orEmpty(),
                        cursor.resultReceipts.firstOrNull()?.sessionId.orEmpty())
                    if (record?.preTrainingEventId != null) {
                        results.blockOnDependency(
                            record.scope, record.sessionId, record.resultRevision,
                            record.preTrainingEventId
                        )
                    }
                    val attempts = store.bumpAttempts(accountId, id).coerceAtLeast(1)
                    val delaySeconds = maxOf(backoffSeconds(attempts), failure.retryAfterSeconds ?: 0L)
                    android.util.Log.i(TAG, "V2 结果等待练前事件确认，退避 ${delaySeconds}s")
                    scope.launch {
                        delay(delaySeconds * 1_000)
                        flush()
                    }
                    return@launch
                }
                if (failure is ApiHttpException && failure.httpStatus in setOf(400, 409, 413, 422)) {
                    val replacements = if (failure.httpStatus == 413 &&
                        item.optString("kind").startsWith("upload")) splitOversizeUploadItem(item) else null
                    if (replacements != null) {
                        check(store.replaceItem(accountId, id, replacements)) {
                            "413 批次已从队列消失"
                        }
                        android.util.Log.w(TAG, "上传批次收到 413，已拆成两批重试")
                    } else {
                        store.quarantine(accountId, id, "HTTP ${failure.httpStatus}")
                        android.util.Log.w(TAG, "永久失败批次已隔离，上传游标未推进")
                    }
                    continue
                }
                // 其余失败：attempts+1，退避后重试
                val attempts = store.bumpAttempts(accountId, id).coerceAtLeast(1)
                val delaySeconds = maxOf(backoffSeconds(attempts),
                    (failure as? ApiHttpException)?.retryAfterSeconds ?: 0L)
                android.util.Log.i(
                    TAG,
                    "冲刷失败 kind=${item.optString("kind")} attempts=$attempts 退避 ${delaySeconds}s"
                )
                scope.launch {
                    delay(delaySeconds * 1_000)
                    flush()
                }
                return@launch
            }
        }
    }

    /**
     * 发送一条冻结结果并按四元组逐条确认。
     *
     * 回执全部核对通过才算成功；本地记录已被新修订取代时，这条任务属于过期快照，
     * 直接移除（继续重发旧的递增修订号只会被服务器拒绝）。
     */
    private suspend fun uploadFrozenResult(
        accountId: String,
        payload: JSONObject,
        cursor: QueueCursor,
        token: String,
    ): Result<List<ResultReceipt>> {
        val sessions = payload.optJSONArray("sessions")
        if (sessions == null || sessions.length() == 0) {
            return Result.failure(IllegalArgumentException("V2 任务缺少冻结快照"))
        }
        val scope = cursor.resultScope
            ?: return Result.failure(IllegalArgumentException("V2 任务缺少 scope"))
        val expected = cursor.resultReceipts
        if (expected.isEmpty()) {
            return Result.failure(IllegalArgumentException("V2 任务缺少期望回执"))
        }
        val verified = TrainingSyncClient.uploadV2Sessions(sessions, expected, token)
        if (verified.isFailure) return verified
        for (receipt in verified.getOrThrow()) {
            val confirmed = results.acknowledge(
                scope, receipt.sessionId, receipt.resultRevision, receipt.resultContentSha256
            )
            if (!confirmed) {
                // 服务器回执与本机当前事实不符：本地已推进到更新的修订，旧任务必须作废。
                android.util.Log.e(
                    TAG,
                    "回执与本地记录不符，作废过期任务 ${receipt.sessionId}#${receipt.resultRevision}"
                )
                store.remove(accountId, "$KIND_RESULT_V2:${receipt.sessionId}:${receipt.resultRevision}")
                return Result.failure(IllegalStateException("回执与本地结果不一致"))
            }
            val record = results.load(scope, receipt.sessionId)
            if (record?.blockedBy != null) {
                results.clearDependency(scope, record.sessionId, record.blockedBy)
            }
        }
        return verified
    }

    private fun nextPending(): Pair<String, JSONObject>? {
        for (accountId in store.pendingAccountIds()) {
            if (accountId in pausedAccountIds) continue
            store.peek(accountId)?.let { return accountId to it }
        }
        return null
    }

    /** SF-01：暂停账号冲刷（首次暂停时回调一次 UI），队列原样保留。 */
    private fun pauseForCredentials(accountId: String) {
        val firstPause = pausedAccountIds.add(accountId)
        android.util.Log.w(TAG, "患者凭据缺失或被拒（401/403），暂停冲刷该账号")
        if (firstPause) onCredentialsRejected?.invoke(accountId)
    }

    /** PAT 获批后用正式 patientId 重建待发同步体，再解除暂停并立即冲刷。 */
    fun resumeAfterCredentialsUpdate(user: DemoUserData? = null) {
        user?.let { forgottenAccountIds.remove(it.accountId) }
        user?.let(::enqueueUserDiff)
        pausedAccountIds.clear()
        scope.launch { flush() }
    }

    /** 退避封顶：15/30/60/120/300 秒。 */
    private fun backoffSeconds(attempts: Int): Long =
        listOf(15L, 30L, 60L, 120L, 300L).getOrElse(attempts - 1) { 300L }
}

/** 只在游标与请求体的 sessionId 完全一致时拆分，避免错误推进游标。 */
internal fun splitOversizeUploadItem(item: JSONObject): List<JSONObject>? {
    val sessions = item.optJSONObject("payload")?.optJSONArray("sessions") ?: return null
    if (sessions.length() < 2) return null
    val expected = QueueCursor.fromJson(item.optJSONObject("cursor")).sessionIds
    val halves = listOf(JSONArray(), JSONArray())
    val ids = listOf(mutableSetOf<String>(), mutableSetOf<String>())
    for (index in 0 until sessions.length()) {
        val session = sessions.optJSONObject(index) ?: return null
        val id = session.optString("sessionId").ifBlank { session.optString("session_id") }
        if (id.isBlank()) return null
        val part = if (index < sessions.length() / 2) 0 else 1
        halves[part].put(session)
        ids[part].add(id)
    }
    if (ids[0].isEmpty() || ids[1].isEmpty() ||
        (ids[0] + ids[1]).size != sessions.length() ||
        ids[0] + ids[1] != expected) return null
    return halves.indices.map { part ->
        queueElement(
            id = "${item.optString("id")}:$part",
            kind = item.optString("kind"),
            payload = JSONObject().put("sessions", halves[part]),
            cursor = QueueCursor(sessionIds = ids[part])
        )
    }
}

// ==================== 纯逻辑（JVM 可测） ====================

/** 一次用户状态 diff 的产物。 */
internal data class PendingDiff(
    val uploadRecords: List<TrainingHistoryRecord>, // 可上传且未同步的历史记录
    val rejectedSessionIds: Set<String>,            // 终态拒绝（不可上传），只推进游标
    val syncBody: JSONObject?,                      // 患者同步体（无增量且无新反馈时 null）
    val newPainIds: Set<String>,                    // 本次同步携带的疼痛记录 id
    val newQuestionnaireHashes: Map<String, String>,// 评审 P1-3：本次同步携带的事件键
    val newProfileHash: String?,                    // 本次同步携带的档案内容键
    val lifecycle: String?,                         // 评审 P1-1：本次同步携带的生命周期（仅变化时非空）
    val imuCalibrationState: String?,               // 评审 P1-1：本次同步携带的 IMU 状态（仅变化时非空）
    val newFeedbackEventIds: Set<String> = emptySet(), // P3.5：本次同步携带的训练后反馈事件键
)

/**
 * 纯函数：对比游标快照与用户状态，产出待上传/待同步增量。
 */
internal fun buildPendingDiff(
    user: DemoUserData,
    snapshot: PatientSyncClient.SyncSnapshot,
    patientId: String? = null,
): PendingDiff {
    // 历史 diff：sessionId 游标 + 可上传校验（评审 P4）
    val newHistory = user.history
        .distinctBy { it.sessionId }
        .filter { it.sessionId !in snapshot.syncedSessionIds }
    val uploadRecords = newHistory.filter(TrainingSyncClient::isUploadable)
    val rejectedSessionIds = newHistory
        .filterNot(TrainingSyncClient::isUploadable)
        .map { it.sessionId }
        .toSet()

    // 患者同步增量
    val delta = PatientSyncClient.computeDelta(snapshot, user)
    var syncBody = PatientSyncClient.buildSyncBody(user, delta, patientId)
    val newQuestionnaireHashes = delta.questionnaireKeys
    val newProfileHash = user.profile
        ?.takeIf { delta.profile != null }
        ?.let { PatientSyncClient.stableHash(PatientSyncClient.profileJson(it)) }
    val newPainIds = user.painHistory
        .filter { it.id !in snapshot.syncedPainIds }
        .map { it.id }
        .toSet()

    // P3.5：反馈是**独立于训练上传**的事件——从全量历史派生，按已确认反馈游标过滤，
    // 因此「训练已上传确认后才补填的晚反馈」仍会出站，不再依赖 uploadRecords（会随
    // session 游标推进而消失）。eventId=post:<sessionId> 稳定，重试/重启不重新随机；
    // 显式携带 sessionId（服务器按事件独立入库，不宣称自动建训练外键）。
    val feedbackCandidates = user.history
        .distinctBy { it.sessionId }
        .filter { "post:${it.sessionId}" !in snapshot.syncedFeedbackEventIds }
    val feedbacks = feedbackCandidates.mapNotNull { record ->
        record.feedback?.let { feedback ->
            JSONObject()
                .put("feeling", feedback.feeling)
                .put("difficulty", feedback.difficulty)
                .put("recordedAt", record.completedAt)
                .put("sessionId", record.sessionId)
                .put("eventId", "post:${record.sessionId}")
        }
    }
    val newFeedbackEventIds = feedbackCandidates
        .mapNotNull { record -> record.feedback?.let { "post:${record.sessionId}" } }
        .toSet()
    if (feedbacks.isNotEmpty()) {
        if (syncBody == null) {
            syncBody = JSONObject()
                .put("userId", patientId ?: "demo_${user.accountId}")
                .put("accountId", user.accountId)
            // 评审 P1-1：无内容增量时也携带变化的状态（如训练刚结束的 lifecycle/IMU）
            delta.lifecycle?.let { syncBody.put("lifecycle", it) }
            delta.imuCalibrationState?.let { syncBody.put("imuCalibrationState", it) }
        }
        syncBody.put("postTrainingFeedbacks", JSONArray(feedbacks))
    }

    return PendingDiff(
        uploadRecords = uploadRecords,
        rejectedSessionIds = rejectedSessionIds,
        syncBody = syncBody,
        newPainIds = newPainIds,
        newQuestionnaireHashes = newQuestionnaireHashes,
        newProfileHash = newProfileHash,
        lifecycle = delta.lifecycle,
        imuCalibrationState = delta.imuCalibrationState,
        newFeedbackEventIds = newFeedbackEventIds,
    )
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
