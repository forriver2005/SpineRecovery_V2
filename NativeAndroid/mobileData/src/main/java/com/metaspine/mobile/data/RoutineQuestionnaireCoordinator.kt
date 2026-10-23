package com.metaspine.mobile.data

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONObject
import java.util.UUID

data class RoutineRefresh(val items: List<LocalRoutineTask>, val message: String, val available: Boolean)

class RoutineQuestionnaireCoordinator(
    private val store: RoutineQuestionnaireStorage,
    private val api: RoutineQuestionnaireApi = PatientQuestionnaireClient,
    private val now: () -> Long = System::currentTimeMillis
) {
    private val mutex = Mutex()
    private var nextRefreshAt = 0L
    private var refreshAttempts = 0
    private var boundScope: RoutineScope? = null

    suspend fun refresh(scope: RoutineScope, credential: PatientServerCredential, current: () -> Boolean): RoutineRefresh = mutex.withLock {
        if (!current()) return@withLock RoutineRefresh(emptyList(), "账号已切换", false)
        store.bind(scope)
        if (boundScope != scope) { boundScope = scope; nextRefreshAt = 0; refreshAttempts = 0 }
        if (store.rejected(scope)) return@withLock RoutineRefresh(emptyList(), "患者凭据已失效，请联系工作人员重新绑定", false)
        if (now() < nextRefreshAt) return@withLock RoutineRefresh(store.read(scope), "请稍后再刷新，草稿已保留", false)
        try {
            // Resolve unknown submission outcomes BEFORE fetching open tasks: a completed task is absent there.
            for (row in store.read(scope)) {
                if (!current()) return@withLock RoutineRefresh(emptyList(), "账号已切换", false)
                if (row.pendingBody != null && row.problem == null && now() >= row.retryAt) {
                    sendPending(scope, credential, row, current)
                }
            }
            val listing = RoutineQuestionnaireParser.open(scopedApi(scope).open(credential.patientToken))
            if (!current() || !store.matches(scope)) return@withLock RoutineRefresh(emptyList(), "账号已切换", false)
            val old = store.read(scope).associateBy { it.task.taskId }
            val downloaded = mutableListOf<LocalRoutineTask>()
            for (summary in listing.items) {
                val detail = try { RoutineQuestionnaireParser.detail(scopedApi(scope).detail(credential.patientToken, summary.taskId), summary.taskId) }
                catch (e: ApiHttpException) {
                    if (e.httpStatus in setOf(409, 410)) continue else throw e
                }
                require(detail.code == summary.code && detail.version == summary.version && detail.contentStatus == summary.contentStatus && detail.availableAt == summary.availableAt && detail.dueAt == summary.dueAt)
                val previous = old[detail.taskId]
                if (previous != null) require(previous.task.code == detail.code && previous.task.version == detail.version && previous.task.schema == detail.schema)
                downloaded += (previous ?: LocalRoutineTask(detail)).copy(task = detail,
                    receiptAt = detail.deliveredAt ?: previous?.receiptAt, availability = "available", serverTime = listing.serverTime)
                if (!current()) return@withLock RoutineRefresh(emptyList(), "账号已切换", false)
            }
            // An absent task may simply be beyond the server's 100-item cap. Never infer completion or delete a draft.
            val ids = downloaded.map { it.task.taskId }.toSet()
            val absent = old.values.filter { it.task.taskId !in ids && it.completedAt == null }
                .map { it.copy(availability = "unconfirmed") }
            if (!current() || !store.put(scope, downloaded + absent)) return@withLock RoutineRefresh(emptyList(), "账号已切换", false)
            for (row in downloaded) {
                if (!current()) return@withLock RoutineRefresh(emptyList(), "账号已切换", false)
                if (row.receiptAt == null && now() >= row.retryAt) sendReceipt(scope, credential, row, current)
            }
            nextRefreshAt = 0; refreshAttempts = 0
            RoutineRefresh(store.read(scope), "已刷新。获取回执不代表已填写；未确认任务与草稿会继续保留。", true)
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            if (!current()) return@withLock RoutineRefresh(emptyList(), "账号已切换", false)
            if (e is ApiHttpException && e.httpStatus in setOf(401, 403)) {
                store.reject(scope)
                RoutineRefresh(emptyList(), "患者凭据已失效，请联系工作人员重新绑定", false)
            } else {
                refreshAttempts++
                nextRefreshAt = now() + delayMillis(e, refreshAttempts)
                RoutineRefresh(store.read(scope), when ((e as? ApiHttpException)?.httpStatus) {
                    404 -> "问卷功能未开启或暂不可用；草稿已保留"
                    429 -> "请求较多，请稍后刷新；草稿已保留"
                    else -> "暂时无法刷新，显示本机缓存。草稿已保留，连接网络后重试"
                }, false)
            }
        }
    }

    suspend fun saveDraft(scope: RoutineScope, taskId: Long, answers: String, current: () -> Boolean) = mutex.withLock {
        if (!current() || store.rejected(scope)) return@withLock
        require(answers.toByteArray().size <= 65536)
        val row = store.read(scope).find { it.task.taskId == taskId } ?: return@withLock
        if (row.pendingBody != null || row.completedAt != null) return@withLock
        check(store.put(scope, listOf(row.copy(answers = JSONObject(answers).toString()))))
    }

    suspend fun submit(scope: RoutineScope, credential: PatientServerCredential, taskId: Long, current: () -> Boolean): String = mutex.withLock {
        if (!current() || store.rejected(scope)) return@withLock "患者凭据不可用"
        var row = store.read(scope).find { it.task.taskId == taskId } ?: return@withLock "请先刷新问卷"
        if (row.completedAt != null) return@withLock "已完成"
        if (row.problem != null) return@withLock row.problem
        if (now() < row.retryAt || now() < nextRefreshAt) return@withLock "请稍后再试，原提交请求已保留"
        if (row.pendingBody == null) {
            if (row.availability != "available") return@withLock "请先在线刷新确认任务"
            val answers = JSONObject(row.answers)
            if (!RoutineQuestionnaireParser.validateAnswers(requireNotNull(row.task.schema), answers)) return@withLock "请完成必填项并检查答案范围"
            row = row.copy(pendingBody = JSONObject().put("requestId", UUID.randomUUID().toString())
                .put("templateVersion", row.task.version).put("answers", answers).toString())
            if (!current() || !store.put(scope, listOf(row))) return@withLock "提交请求未保存，请重试"
        }
        try {
            sendPending(scope, credential, row, current)
            if (!current()) return@withLock "账号已切换"
            val saved = store.read(scope).find { it.task.taskId == taskId }
            if (saved?.completedAt != null) "已完成" else saved?.problem ?: "提交结果尚未确认，已保留原请求供重试"
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            if (current() && e is ApiHttpException && e.httpStatus in setOf(401, 403)) store.reject(scope)
            if (current() && e is ApiHttpException && e.httpStatus == 429) nextRefreshAt = now() + delayMillis(e, 1)
            "提交未确认；请检查网络或联系工作人员，原请求与草稿已保留"
        }
    }

    private suspend fun sendPending(scope: RoutineScope, credential: PatientServerCredential, row: LocalRoutineTask, current: () -> Boolean) {
        try {
            val result = scopedApi(scope).submit(credential.patientToken, row.task.taskId, JSONObject(requireNotNull(row.pendingBody)))
            RoutineQuestionnaireParser.exact(result, setOf("taskId", "recordId", "status", "completedAt"))
            require(RoutineQuestionnaireParser.integer(result, "taskId") == row.task.taskId && result.get("status") == "completed")
            val recordId = RoutineQuestionnaireParser.integer(result, "recordId")
            val time = RoutineQuestionnaireParser.timestamp(result, "completedAt")
            if (current()) check(store.put(scope, listOf(row.copy(
                completedAt = time, recordId = recordId, submissionStatus = "completed",
                confirmedSubmissionBody = row.pendingBody, pendingBody = null,
                availability = "completed", problem = null, problemCode = null, fieldErrors = null, retryAt = 0
            )))) { "问卷提交收据未能保存，原请求仍需重试" }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            if (e is ApiHttpException && e.httpStatus in setOf(401, 403)) throw e
            val blocked = (e as? ApiHttpException)?.httpStatus in setOf(400, 409, 410, 413, 422)
            if (current()) store.put(scope, listOf(row.copy(
                problem = if (blocked) "服务器拒绝本次提交（任务过期、状态变化或答案不合法），请联系工作人员；草稿保留" else null,
                problemCode = (e as? ApiHttpException)?.errorCode,
                fieldErrors = (e as? ApiHttpException)?.fieldErrors,
                retryAt = now() + delayMillis(e, row.attempts + 1), attempts = row.attempts + 1)))
            if (e is ApiHttpException && e.httpStatus == 429) throw e
        }
    }
    private suspend fun sendReceipt(scope: RoutineScope, credential: PatientServerCredential, row: LocalRoutineTask, current: () -> Boolean) {
        try {
            val data = scopedApi(scope).receipt(credential.patientToken, row.task.taskId, row.receiptId)
            RoutineQuestionnaireParser.exact(data, setOf("taskId", "deliveredAt"))
            require(RoutineQuestionnaireParser.integer(data, "taskId") == row.task.taskId)
            val at = RoutineQuestionnaireParser.timestamp(data, "deliveredAt")
            if (current()) store.put(scope, listOf(row.copy(receiptAt = at, retryAt = 0, attempts = 0)))
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            if (e is ApiHttpException && e.httpStatus in setOf(401, 403, 429)) throw e
            if (current()) store.put(scope, listOf(row.copy(retryAt = now() + delayMillis(e, row.attempts + 1), attempts = row.attempts + 1)))
        }
    }
    private fun scopedApi(scope: RoutineScope) = api.atOrigin(scope.origin.ifBlank { SpineFlowServerConfig.BASE_URL })

    private fun delayMillis(error: Exception, attempt: Int): Long =
        ((error as? ApiHttpException)?.retryAfterSeconds?.coerceIn(1, 3600)?.times(1000))
            ?: (5000L * (1L shl attempt.coerceIn(0, 9))).coerceAtMost(3600000)
}
