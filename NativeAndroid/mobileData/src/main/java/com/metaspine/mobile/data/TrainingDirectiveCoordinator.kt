package com.metaspine.mobile.data

import org.json.JSONObject

/**
 * M7.1 §8.3：下行协调器 —— 执行「拉取 → 校验 → 保存 → 尝试回执」，并回答本次是否可用于启动训练。
 *
 * 关键规则（设计 §4.5）：
 * - 只有**本次在线刷新成功且 state=ready** 才返回 [DirectiveRefreshResult.Ready]；
 * - 失败/非 ready 时缓存只用于展示，训练一律失败关闭；
 * - 401/403 不回退缓存；
 * - 回执只在快照成功落盘后提交，失败不影响本次处方，下一次拉到同一 active order 时重试。
 */
class TrainingDirectiveCoordinator(
    private val storage: DirectiveSnapshotStorage,
    private val fetchDirective: suspend (String) -> Result<JSONObject> = PatientDirectiveClient::fetch,
    private val sendStopReceipt: suspend (String, Long) -> Result<PatientDirectiveClient.ReceiptInfo> =
        PatientDirectiveClient::postStopReceipt,
    private val nowMillis: () -> Long = System::currentTimeMillis
) {

    suspend fun refresh(
        accountId: String,
        credential: PatientServerCredential?
    ): DirectiveRefreshResult {
        val effective = credential
            ?: return DirectiveRefreshResult.NotActivated("尚未完成激活码绑定，请联系医生")

        // PAT 换绑到别的患者后，旧快照立即作废。
        storage.clearIfPatientChanged(accountId, effective.patientId)

        val fetched = fetchDirective(effective.patientToken)
        val data = fetched.getOrElse { error -> return failureResult(accountId, effective, error) }

        val snapshot = try {
            TrainingDirectiveParser.parse(
                patientId = effective.patientId,
                data = data,
                fetchedAtEpochMillis = nowMillis()
            )
        } catch (error: DirectiveProtocolException) {
            // 非法响应不覆盖最后一次可展示缓存，本次训练失败关闭。
            return DirectiveRefreshResult.Unavailable(
                message = "无法确认最新医生处方，请稍后重试",
                cached = storage.read(accountId, effective.patientId)
            )
        }

        // 200 的每种状态都整体覆盖旧快照：撤销/停止必须立刻让旧处方失效。
        storage.write(accountId, snapshot)
        // 回执失败不阻塞训练：已在本地保存的处方仍然安全，下次拉取会重试。
        submitReceiptIfNeeded(effective, snapshot)

        return when {
            // 解析器已拒绝非 2 版本；这里再挡一次，避免将来放宽解析时把旧快照当可训练。
            snapshot.state == TrainingDirectiveState.Ready &&
                snapshot.directiveVersion == PatientDirectiveClient.DIRECTIVE_VERSION ->
                DirectiveRefreshResult.Ready(snapshot)
            else -> DirectiveRefreshResult.NotTrainable(snapshot, DirectiveMessages.forState(snapshot))
        }
    }

    /**
     * P4.1 只保留停训简式回执；处方回执必须走两阶段（`directive_saved` / `assets_ready`），
     * 由 P4.5 的持久化队列提交——这里若发简式 `{orderId}` 会被服务器 400
     * `STOP_RECEIPT_ORDER_TYPE_MISMATCH`，且无法表达保存与资源就绪的区别。
     */
    private suspend fun submitReceiptIfNeeded(
        credential: PatientServerCredential,
        snapshot: TrainingDirectiveSnapshot
    ) {
        val stopOrder = snapshot.stopOrder ?: return
        // Server 已经记录过该版本（receivedAt 非空）时无需重复提交。
        if (!stopOrder.receivedAt.isNullOrBlank()) return
        // 停训回执是尽力而为：失败不能把已确认的状态误判为不可用。
        runCatching { sendStopReceipt(credential.patientToken, stopOrder.orderId) }
    }

    private fun failureResult(
        accountId: String,
        credential: PatientServerCredential,
        error: Throwable
    ): DirectiveRefreshResult {
        val cached = storage.read(accountId, credential.patientId)
        val status = (error as? ApiHttpException)?.httpStatus
        return when {
            status == 401 || status == 403 -> DirectiveRefreshResult.CredentialRejected(
                "患者凭据已失效，请联系医生或管理员"
            )
            status == 404 -> DirectiveRefreshResult.Unavailable(
                "无法确认最新医生处方，请刷新后重试；仍失败请联系工作人员",
                cached
            )
            else -> DirectiveRefreshResult.Unavailable(
                "无法确认最新医生处方，请连接医院局域网后重试",
                cached
            )
        }
    }
}

/** 本次刷新对“能否开始训练”的结论。 */
sealed interface DirectiveRefreshResult {
    /** 本次在线刷新成功且 state=ready；仍需通过手机本地训练前安全检查。 */
    data class Ready(val snapshot: TrainingDirectiveSnapshot) : DirectiveRefreshResult

    /** 刷新成功但医生当前不允许训练（已停止 / 等待新处方 / 处方不兼容）。 */
    data class NotTrainable(
        val snapshot: TrainingDirectiveSnapshot,
        val message: String
    ) : DirectiveRefreshResult

    /** 本地没有可用凭据：先完成激活码绑定。 */
    data class NotActivated(val message: String) : DirectiveRefreshResult

    /** PAT 被服务端拒绝：不回退缓存授权。 */
    data class CredentialRejected(val message: String) : DirectiveRefreshResult

    /** 网络、5xx 或协议错误：缓存仅供展示，本次不生成训练载荷。 */
    data class Unavailable(
        val message: String,
        val cached: TrainingDirectiveSnapshot?
    ) : DirectiveRefreshResult

    val canStartTraining: Boolean get() = this is Ready
}

/** M7.1 §8.6：四种状态与刷新失败的面向患者文案（纯函数，便于单元测试）。 */
object DirectiveMessages {
    const val READY_HEADLINE = "医生处方已就绪"
    const val STOPPED_HEADLINE = "医生已暂停训练"
    const val WAITING_HEADLINE = "等待医生下达新处方"
    const val UNSUPPORTED_HEADLINE = "当前处方暂不支持手机执行"
    const val UNSUPPORTED_DETAIL = "当前处方包含手机端暂不支持的动作，请联系医生调整"
    const val STALE_CACHE_NOTICE = "当前无法确认最新状态，请连接医院局域网后刷新"
    const val EXECUTION_DISCLAIMER = "“手机已获取”只表示指令已保存到本机，不代表患者已开始或已完成训练。"

    fun forState(snapshot: TrainingDirectiveSnapshot): String = when (snapshot.state) {
        TrainingDirectiveState.Ready -> snapshot.message.ifBlank { READY_HEADLINE }
        TrainingDirectiveState.Stopped ->
            snapshot.stopOrder?.reason?.takeIf(String::isNotBlank)
                ?: snapshot.message.ifBlank { STOPPED_HEADLINE }
        TrainingDirectiveState.WaitingPrescription -> WAITING_HEADLINE
        TrainingDirectiveState.UnsupportedPrescription ->
            snapshot.compatibilityMessage?.takeIf(String::isNotBlank) ?: UNSUPPORTED_DETAIL
    }

    fun headline(snapshot: TrainingDirectiveSnapshot): String = when (snapshot.state) {
        TrainingDirectiveState.Ready -> READY_HEADLINE
        TrainingDirectiveState.Stopped -> STOPPED_HEADLINE
        TrainingDirectiveState.WaitingPrescription -> WAITING_HEADLINE
        TrainingDirectiveState.UnsupportedPrescription -> UNSUPPORTED_HEADLINE
    }

    /** 处方卡正文：动作、组数 × 次数、保持秒数与医生备注。 */
    fun prescriptionDetail(item: AdmissionPrescriptionItemV2): String =
        "${item.actionName} ${item.sets} 组 × ${item.reps} 次，保持 ${item.holdSeconds} 秒"

    /** 多项处方逐条展示，保持服务器下发的 position 顺序。 */
    fun prescriptionDetail(order: DownloadedOrder): String =
        order.items.joinToString("\n") { "${it.position + 1}. " + prescriptionDetail(it) }

    fun fetchedAtLabel(epochMillis: Long): String {
        if (epochMillis <= 0L) return "尚未刷新"
        val formatter = java.time.format.DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm")
        val local = java.time.Instant.ofEpochMilli(epochMillis)
            .atZone(java.time.ZoneId.systemDefault())
            .toLocalDateTime()
        return "上次刷新 ${formatter.format(local)}"
    }
}
