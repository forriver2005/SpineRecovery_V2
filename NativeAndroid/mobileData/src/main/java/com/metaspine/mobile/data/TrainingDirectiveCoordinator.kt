package com.metaspine.mobile.data

import kotlinx.coroutines.CancellationException
import org.json.JSONObject
import java.io.File

/**
 * 已核对并交接给本机执行层的固定资源引用。
 *
 * `localFile` 只用于本机读取，**不会**上传 T01；服务器侧只认条目、版本与摘要。
 */
data class HandedOffAsset(
    val item: AdmissionPrescriptionItemV2,
    val file: File,
    val byteCount: Long
)

/**
 * 完整处方交接：订单、清单、全部条目/剂量/资源身份，以及本地已验证文件引用。
 *
 * [blockingReasons] 非空表示“已保存但资源或能力不完整”，此时不得宣称 ready。
 */
data class TrainingPrescriptionHandoff(
    val scope: BindingScope,
    val order: DownloadedOrder,
    val manifestSha256: String,
    val assets: List<HandedOffAsset>,
    val blockingReasons: List<String>
) {
    val complete: Boolean get() = blockingReasons.isEmpty() && assets.size == order.items.size

    /**
     * 交给 AR 的完整处方事实。逐项写出与 manifest 相同的字段，
     * AR 只需按 [localFile] 读取已核对字节，不需要、也不允许自行拼路径或改名。
     */
    fun toJson(): JSONObject = JSONObject()
        .put("orderId", order.orderId)
        .put("reason", order.reason)
        .put("createdBy", order.createdBy)
        .put("createdAt", order.createdAt)
        .put("prescriptionContractVersion", order.prescription.prescriptionContractVersion)
        .put("resourceManifestSha256", manifestSha256)
        .put("localScope", JSONObject()
            .put("origin", scope.origin)
            .put("serviceId", scope.serviceId)
            .put("hospitalKey", scope.hospitalKey))
        .put("items", org.json.JSONArray(order.items.map { item ->
            val handoff = assets.firstOrNull { it.item.itemId == item.itemId }
            val asset = item.asset
            JSONObject()
                .put("itemId", item.itemId)
                .put("position", item.position)
                .put("actionId", item.actionId)
                .put("actionName", item.actionName)
                .put("sets", item.sets)
                .put("reps", item.reps)
                .put("holdSeconds", item.holdSeconds)
                .put("actionVersionId", asset.actionVersionId)
                .put("variantKey", asset.variantKey)
                .put("formatVersion", asset.formatVersion)
                .put("runtimeContractVersion", asset.runtimeContractVersion)
                .put("doseProfile", asset.doseProfile)
                .put("supportedModes", org.json.JSONArray(asset.supportedModes))
                .put("sha256", asset.sha256)
                .put("fileSize", asset.fileSize)
                .put("localFileName", handoff?.file?.name.orEmpty())
        }))
}

/**
 * P4：下行协调器 —— 「拉取 → 校验 → 保存 → 下载资源 → 两阶段回执 → 交接」。
 *
 * 关键规则：
 * - 只有**本次在线刷新成功且 state=ready、全部资源已验证、`assets_ready` 已被接受**
 *   才返回 [DirectiveRefreshResult.Ready]；
 * - `directive_saved` 只在快照与回执任务持久化成功后提交；
 * - `assets_ready` 只在全部固定文件核对通过、且消费者确认能力后提交；
 * - 缺能力或资源失败时保持 saved，并列出具体缺口；
 * - 停训仍走简式回执；回执失败不回退成“训练可用”。
 */
class TrainingDirectiveCoordinator(
    private val storage: DirectiveSnapshotStorage,
    private val receipts: PrescriptionReceiptStorage? = null,
    private val assets: PrescriptionAssetStorage? = null,
    private val handoffGate: (TrainingPrescriptionHandoff) -> List<String> = { emptyList() },
    private val fetchDirective: suspend (String) -> Result<JSONObject> = PatientDirectiveClient::fetch,
    private val sendStopReceipt: suspend (String, Long) -> Result<PatientDirectiveClient.ReceiptInfo> =
        PatientDirectiveClient::postStopReceipt,
    private val sendPrescriptionReceipt:
        suspend (String, Long, String, String) -> Result<PatientDirectiveClient.ReceiptInfo> =
        PatientDirectiveClient::postPrescriptionReceipt,
    private val nowMillis: () -> Long = System::currentTimeMillis
) {

    suspend fun refresh(
        accountId: String,
        credential: PatientServerCredential?,
        scope: BindingScope? = null
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

        if (snapshot.state == TrainingDirectiveState.Stopped) {
            submitStopReceiptIfNeeded(effective, snapshot)
            return DirectiveRefreshResult.NotTrainable(snapshot, DirectiveMessages.forState(snapshot))
        }

        val order = snapshot.prescription
        if (snapshot.state != TrainingDirectiveState.Ready || order == null || scope == null) {
            return DirectiveRefreshResult.NotTrainable(snapshot, DirectiveMessages.forState(snapshot))
        }

        return deliverPrescription(effective, snapshot, order, scope)
    }

    private suspend fun submitStopReceiptIfNeeded(
        credential: PatientServerCredential,
        snapshot: TrainingDirectiveSnapshot
    ) {
        val stopOrder = snapshot.stopOrder ?: return
        if (!stopOrder.receivedAt.isNullOrBlank()) return
        runCatching { sendStopReceipt(credential.patientToken, stopOrder.orderId) }
    }

    /**
     * 两阶段回执与交接。任何一步失败都保持可展示快照，但绝不返回 Ready。
     */
    private suspend fun deliverPrescription(
        credential: PatientServerCredential,
        snapshot: TrainingDirectiveSnapshot,
        order: DownloadedOrder,
        scope: BindingScope
    ): DirectiveRefreshResult {
        val manifest = order.resourceManifestSha256
        if (manifest != order.prescription.computeManifestSha256()) {
            // 本地重算不符：不提交任何回执，也不把处方交接出去。
            return DirectiveRefreshResult.NotTrainable(snapshot, "处方清单校验失败，请联系医生")
        }

        // ── 阶段 1：处方与清单已落盘后提交 directive_saved ──────────────────────
        var task = receipts?.read(scope, order.orderId)
            ?.takeIf { it.resourceManifestSha256 == manifest }
            ?: PrescriptionReceiptTask(
                orderId = order.orderId,
                resourceManifestSha256 = manifest,
                phase = ReceiptPhase.NotSubmitted,
                savedReceivedAt = null,
                assetsReadyReceivedAt = null,
                failedAssetItemIds = emptyList(),
                blockingReasons = emptyList()
            )
        if (task.phase == ReceiptPhase.NotSubmitted) {
            val saved = sendPrescriptionReceipt(
                credential.patientToken, order.orderId, PatientDirectiveClient.PHASE_SAVED, manifest
            )
            val info = saved.getOrElse { error ->
                // 保存阶段的失败必须保留任务，下一次刷新原样重试同一订单与摘要。
                return DirectiveRefreshResult.NotTrainable(
                    snapshot, receiptFailureMessage(error, "处方保存回执失败"))
            }
            task = task.copy(phase = ReceiptPhase.Saved, savedReceivedAt = info.receivedAt)
            receipts?.write(scope, task)
        }

        // ── 阶段 2：下载并核对全部固定文件 ──────────────────────────────────────
        val handedOff = mutableListOf<HandedOffAsset>()
        val failures = mutableListOf<Long>()
        for (item in order.items) {
            val asset = item.asset
            val cached = assets?.existing(order.orderId, item.itemId, asset.sha256)
            if (cached != null) {
                handedOff.add(HandedOffAsset(item, cached, asset.fileSize))
                continue
            }
            val downloaded = try {
                assets?.download(credential.patientToken, order, item)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (error: ApiHttpException) {
                // 凭据被拒是全局问题：继续下载其余条目没有意义，也不能把处方当成可用。
                if (error.httpStatus == 401 || error.httpStatus == 403) {
                    receipts?.write(scope, task.copy(
                        failedAssetItemIds = (failures + item.itemId).distinct(),
                        blockingReasons = listOf("患者凭据已失效，请重新激活")))
                    return DirectiveRefreshResult.CredentialRejected(
                        "患者凭据已失效，请联系医生或管理员")
                }
                null
            } catch (error: PrescriptionAssetException) {
                null
            }
            if (downloaded == null) {
                failures.add(item.itemId)
            } else {
                handedOff.add(HandedOffAsset(item, downloaded, asset.fileSize))
            }
        }
        val blocking = buildList {
            if (failures.isNotEmpty()) add("有资源未通过校验，已保持保存状态")
            addAll(handoffGate(TrainingPrescriptionHandoff(
                scope = scope,
                order = order,
                manifestSha256 = manifest,
                assets = handedOff,
                blockingReasons = emptyList()
            )))
        }
        if (blocking.isNotEmpty()) {
            receipts?.write(scope, task.copy(
                failedAssetItemIds = failures, blockingReasons = blocking))
            return DirectiveRefreshResult.NotTrainable(
                snapshot, blocking.firstOrNull() ?: "资源尚未就绪，请稍后重试")
        }

        // ── 阶段 3：全部条件满足才提交 assets_ready ─────────────────────────────
        if (task.phase != ReceiptPhase.AssetsReady) {
            val ready = sendPrescriptionReceipt(
                credential.patientToken, order.orderId,
                PatientDirectiveClient.PHASE_ASSETS_READY, manifest
            )
            val info = ready.getOrElse { error ->
                // 服务器可能已记录、只是响应丢失：保持 saved，下一次原样重放。
                return DirectiveRefreshResult.NotTrainable(
                    snapshot, receiptFailureMessage(error, "资源就绪回执失败"))
            }
            task = task.copy(
                phase = ReceiptPhase.AssetsReady,
                assetsReadyReceivedAt = info.assetsReadyAt ?: info.receivedAt,
                failedAssetItemIds = emptyList(),
                blockingReasons = emptyList()
            )
            receipts?.write(scope, task)
        }

        return DirectiveRefreshResult.Ready(
            snapshot,
            TrainingPrescriptionHandoff(scope, order, manifest, handedOff, emptyList())
        )
    }

    private fun receiptFailureMessage(error: Throwable, fallback: String): String =
        when {
            error is ApiHttpException && error.httpStatus == 401 ->
                "患者凭据已失效，请联系医生或管理员"
            error is DirectiveProtocolException && error.message != null -> error.message!!
            else -> fallback
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

/** 回执任务存储端口：单元测试用内存实现，Android 用 [PrescriptionReceiptStore]。 */
interface PrescriptionReceiptStorage {
    fun read(scope: BindingScope, orderId: Long): PrescriptionReceiptTask?
    fun write(scope: BindingScope, task: PrescriptionReceiptTask)
}

/** 固定资源端口：返回已发布的本地文件；失败必须抛出而不是返回半文件。 */
interface PrescriptionAssetStorage {
    fun existing(orderId: Long, itemId: Long, sha256: String): File?
    suspend fun download(patientToken: String, order: DownloadedOrder, item: AdmissionPrescriptionItemV2): File?
}

/** 本次刷新对“能否开始训练”的结论。 */
sealed interface DirectiveRefreshResult {
    /**
     * 本次在线刷新成功、资源已验证且 `assets_ready` 已被接受；
     * 仍需通过手机本地训练前安全检查。
     */
    data class Ready(
        val snapshot: TrainingDirectiveSnapshot,
        val handoff: TrainingPrescriptionHandoff? = null
    ) : DirectiveRefreshResult

    /** 刷新成功但医生当前不允许训练，或资源/能力尚未就绪。 */
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
