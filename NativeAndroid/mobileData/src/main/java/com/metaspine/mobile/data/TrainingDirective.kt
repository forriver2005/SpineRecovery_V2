package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject

/**
 * P4 §4：Server 当前有效训练指令快照的本地模型与严格解析器。
 *
 * 事实源是 Server 的 `doctor_orders`；本地只保存“最近一次成功拉取的快照”，
 * 用于展示与抗进程重启，**不作为离线训练授权**（设计 §4.5）。
 */
enum class TrainingDirectiveState(val wireValue: String) {
    Ready("ready"),
    Stopped("stopped"),
    WaitingPrescription("waiting_prescription"),
    UnsupportedPrescription("unsupported_prescription");

    companion object {
        fun fromWire(value: String?): TrainingDirectiveState? =
            entries.firstOrNull { it.wireValue == value }
    }
}

/** 停止训练指令：没有处方条目，也没有资源清单。 */
data class DownloadedStopOrder(
    val orderId: Long,
    val reason: String,
    val createdBy: String,
    val createdAt: String,
    val receivedAt: String?
)

/**
 * 一条 active 处方指令的完整内容：处方主体（含全部条目与冻结资源身份）加医生备注。
 *
 * [prescription] 与 A02 首方共用 [AdmissionPrescriptionV2]，`orderId`、条目顺序与
 * `resourceManifestSha256` 都只由它给出，这里不复制第二份。
 */
data class DownloadedOrder(
    val prescription: AdmissionPrescriptionV2,
    val reason: String,
    val createdBy: String,
    val createdAt: String,
    val receivedAt: String?,
    val assetsReadyAt: String?
) {
    val orderId: Long get() = prescription.orderId
    val items: List<AdmissionPrescriptionItemV2> get() = prescription.items
    val resourceManifestSha256: String get() = prescription.resourceManifestSha256

    /** 只对单个动作的旧执行路径有意义；多项处方返回 null。 */
    val singleItem: AdmissionPrescriptionItemV2? get() = items.singleOrNull()
}

data class TrainingDirectiveSnapshot(
    val patientId: String,
    val directiveVersion: Int,
    val state: TrainingDirectiveState,
    val serverTime: String,
    val fetchedAtEpochMillis: Long,
    val message: String,
    val prescription: DownloadedOrder?,
    val stopOrder: DownloadedStopOrder?,
    val compatibilityCode: String?,
    val compatibilityMessage: String?,
    val resourceLease: TrainingResourceLease? = null
) {
    /**
     * `ready` 且处方完整时的完整订单；动作白名单与单动作执行能力由交接层判定并给出
     * 面向患者的缺口说明，这里不截项、也不静默丢弃多动作处方。
     */
    fun trainableOrder(): DownloadedOrder? =
        if (state == TrainingDirectiveState.Ready) prescription?.takeIf { it.items.isNotEmpty() } else null
}

/** 响应结构不符合 M7.1 / P4 契约：不覆盖最后一次可展示缓存，本次训练失败关闭。 */
class DirectiveProtocolException(message: String) : RuntimeException(message)

/**
 * 严格解析器（设计 §8.1、P4 §4）。
 *
 * Mobile 不能只相信 `trainingState`：复核外层 `directiveVersion=2`、处方
 * `prescriptionContractVersion=2`、条目数量/顺序、剂量范围与资源身份。
 * 旧版处方直接失败，不截第一项、不构造默认动作。
 */
object TrainingDirectiveParser {
    /** 与 Server `MOBILE_ROUTE_ACTIONS` 一致：AR 执行层当前只有这两个基础动作。 */
    val SUPPORTED_ACTION_IDS = setOf("dead_bug", "bird_dog")

    /** Server `ACTION_ID`：小写标识符或纯数字，V2 处方里是可执行动作的冻结 ID。 */
    private val ACTION_ID_PATTERN = Regex("^(?:[a-z][a-z0-9_]{0,127}|[1-9][0-9]{0,5})$")
    private val SHA_PATTERN = Regex("^[a-f0-9]{64}$")

    internal const val MAX_TEXT_LENGTH = 500
    internal const val MAX_NAME_LENGTH = 128
    internal const val MAX_TIME_LENGTH = 64
    internal const val MIN_SETS = 1
    internal const val MAX_SETS = 20
    internal const val MIN_REPS = 1
    internal const val MAX_REPS = 100
    internal const val MIN_HOLD_SECONDS = 0
    internal const val MAX_HOLD_SECONDS = 600
    internal const val ASSET_FORMAT_VERSION = 1
    internal const val ASSET_CONTRACT_VERSION = 1
    internal const val DOSE_PROFILE = "sequence-coach-game-v1"
    internal const val MIN_FILE_BYTES = 1L
    internal const val MAX_FILE_BYTES = 16L * 1024 * 1024

    fun parse(
        patientId: String,
        data: JSONObject,
        fetchedAtEpochMillis: Long
    ): TrainingDirectiveSnapshot {
        require(patientId.isNotBlank()) { "缺少本地患者身份，无法确认快照归属" }

        // 外层版本必须显式为 2；缺失、字符串 "2" 或其它版本都不接受。
        val directiveVersion = data.intInRange(
            "directiveVersion",
            PatientDirectiveClient.DIRECTIVE_VERSION,
            PatientDirectiveClient.DIRECTIVE_VERSION
        )
        val serverTime = data.text("serverTime", MAX_TIME_LENGTH)
        val state = TrainingDirectiveState.fromWire(data.optString("trainingState"))
            ?: throw DirectiveProtocolException("未知的训练指令状态")
        val message = data.text("message", MAX_TEXT_LENGTH, required = false)
        val prescription = data.optJSONObject("activePrescription")?.let(::parseOrder)
        val stopOrder = data.optJSONObject("activeStopOrder")?.let(::parseStopOrder)
        val compatibility = data.optJSONObject("compatibility")
        val compatibilityCode = compatibility
            ?.text("code", MAX_NAME_LENGTH, required = false)?.takeIf(String::isNotBlank)
        val compatibilityMessage = compatibility
            ?.text("message", MAX_TEXT_LENGTH, required = false)?.takeIf(String::isNotBlank)

        val parsed = when (state) {
            TrainingDirectiveState.Ready -> {
                if (stopOrder != null) {
                    throw DirectiveProtocolException("ready 状态不应同时下发停止训练指令")
                }
                val order = prescription
                    ?: throw DirectiveProtocolException("ready 状态缺少有效处方")
                snapshot(patientId, directiveVersion, state, serverTime, fetchedAtEpochMillis,
                    message, order, null, null, null)
            }

            TrainingDirectiveState.Stopped -> {
                val stop = stopOrder
                    ?: throw DirectiveProtocolException("stopped 状态缺少停止训练指令")
                // 防御：即使 Server 异常同时返回处方，也绝不把它当作可执行处方保留。
                snapshot(patientId, directiveVersion, state, serverTime, fetchedAtEpochMillis,
                    message, null, stop, compatibilityCode, compatibilityMessage)
            }

            TrainingDirectiveState.WaitingPrescription -> {
                if (prescription != null || stopOrder != null) {
                    throw DirectiveProtocolException("waiting_prescription 不应携带任何有效指令")
                }
                snapshot(patientId, directiveVersion, state, serverTime, fetchedAtEpochMillis,
                    message, null, null, compatibilityCode, compatibilityMessage)
            }

            TrainingDirectiveState.UnsupportedPrescription -> {
                // 旧版处方或资源不可用时 Server 返回 activePrescription:null；
                // 若带了完整 V2 处方则保留用于展示，但绝不进入可训练状态。
                snapshot(
                    patientId,
                    directiveVersion,
                    state,
                    serverTime,
                    fetchedAtEpochMillis,
                    message,
                    prescription,
                    null,
                    compatibilityCode,
                    compatibilityMessage
                        ?: message.takeIf(String::isNotBlank)
                )
            }
        }
        val lease = data.optJSONObject("resourceLease")?.let {
            try { TrainingResourceLease.fromJson(it) }
            catch (error: RuntimeException) { throw DirectiveProtocolException(error.message ?: "资源租约不合法") }
        }
        return parsed.copy(resourceLease = lease)
    }

    private fun snapshot(
        patientId: String,
        directiveVersion: Int,
        state: TrainingDirectiveState,
        serverTime: String,
        fetchedAtEpochMillis: Long,
        message: String,
        prescription: DownloadedOrder?,
        stopOrder: DownloadedStopOrder?,
        compatibilityCode: String?,
        compatibilityMessage: String?
    ) = TrainingDirectiveSnapshot(
        patientId = patientId,
        directiveVersion = directiveVersion,
        state = state,
        serverTime = serverTime,
        fetchedAtEpochMillis = fetchedAtEpochMillis,
        message = message,
        prescription = prescription,
        stopOrder = stopOrder,
        compatibilityCode = compatibilityCode,
        compatibilityMessage = compatibilityMessage
    )

    private fun parseOrder(json: JSONObject): DownloadedOrder {
        // 处方级校验（版本 2、1–10 项、position 连续、资源身份齐全）全部交给共享模型。
        val prescription = try {
            AdmissionPrescriptionV2.fromDirectiveJson(json)
        } catch (error: RuntimeException) {
            // 共享模型用 require 表达契约失败，org.json 缺字段抛 JSONException；
            // 统一转成协议异常，协调器才会保留最后一次可展示快照而不是崩溃。
            throw DirectiveProtocolException(error.message ?: "处方结构不合法")
        }
        prescription.items.forEach(::validateItem)
        return DownloadedOrder(
            prescription = prescription,
            reason = json.text("reason", MAX_TEXT_LENGTH, required = false),
            createdBy = json.text("createdBy", MAX_NAME_LENGTH, required = false),
            createdAt = json.text("createdAt", MAX_TIME_LENGTH, required = false),
            receivedAt = (json.opt("receivedAt") as? String)?.takeIf(String::isNotBlank),
            assetsReadyAt = (json.opt("assetsReadyAt") as? String)?.takeIf(String::isNotBlank)
        )
    }

    private fun parseStopOrder(json: JSONObject): DownloadedStopOrder = DownloadedStopOrder(
        orderId = json.positiveLong("orderId"),
        reason = json.text("reason", MAX_TEXT_LENGTH, required = false),
        createdBy = json.text("createdBy", MAX_NAME_LENGTH, required = false),
        createdAt = json.text("createdAt", MAX_TIME_LENGTH, required = false),
        receivedAt = (json.opt("receivedAt") as? String)?.takeIf(String::isNotBlank)
    )

    /**
     * 条目层复核：动作 ID 形态、剂量范围与冻结资源字段。
     *
     * 动作白名单属于执行能力，不在此处拒收——否则服务器下发的新动作 ID
     * 会让整份快照失效，而不是明确报告「本地无法执行」。
     */
    private fun validateItem(item: AdmissionPrescriptionItemV2) {
        if (!ACTION_ID_PATTERN.matches(item.actionId)) {
            throw DirectiveProtocolException("处方动作编号不合法")
        }
        if (item.actionName.isBlank() || item.actionName.length > MAX_NAME_LENGTH) {
            throw DirectiveProtocolException("处方动作名称不合法")
        }
        if (item.sets !in MIN_SETS..MAX_SETS) {
            throw DirectiveProtocolException("处方组数超出允许范围")
        }
        if (item.reps !in MIN_REPS..MAX_REPS) {
            throw DirectiveProtocolException("处方次数超出允许范围")
        }
        if (item.holdSeconds !in MIN_HOLD_SECONDS..MAX_HOLD_SECONDS) {
            throw DirectiveProtocolException("处方保持时间超出允许范围")
        }
        val asset = item.asset
        if (asset.variantKey != "single") throw DirectiveProtocolException("处方资源 variant 不支持")
        if (asset.formatVersion != ASSET_FORMAT_VERSION) {
            throw DirectiveProtocolException("处方资源格式版本不支持")
        }
        if (asset.runtimeContractVersion != ASSET_CONTRACT_VERSION) {
            throw DirectiveProtocolException("处方资源合同版本不支持")
        }
        if (asset.doseProfile != DOSE_PROFILE) throw DirectiveProtocolException("处方剂量方案不支持")
        if (asset.supportedModes != listOf("coach", "game")) {
            throw DirectiveProtocolException("处方资源模式不支持")
        }
        if (asset.fileSize !in MIN_FILE_BYTES..MAX_FILE_BYTES) {
            throw DirectiveProtocolException("处方资源大小超出允许范围")
        }
        if (!SHA_PATTERN.matches(asset.sha256)) throw DirectiveProtocolException("处方资源摘要格式不合法")
        if (!asset.downloadPath.startsWith("/api/")) {
            throw DirectiveProtocolException("处方资源下载路径不合法")
        }
    }

    private fun JSONObject.text(key: String, maxLength: Int, required: Boolean = true): String {
        if (!has(key) || isNull(key)) {
            // optional 字段允许缺失；但“存在却是错类型”一律拒绝，不做静默强转。
            if (required) throw DirectiveProtocolException("字段 $key 缺失或类型错误")
            return ""
        }
        val value = get(key)
        if (value !is String) throw DirectiveProtocolException("字段 $key 类型错误")
        if (required && value.isBlank()) throw DirectiveProtocolException("字段 $key 不能为空")
        if (value.length > maxLength) throw DirectiveProtocolException("字段 $key 长度超限")
        return value
    }

    /** `orderId` 必须是 JSON 数字且为正整数：字符串编号一律拒绝，不做强转。 */
    private fun JSONObject.positiveLong(key: String): Long {
        if (!has(key) || isNull(key)) throw DirectiveProtocolException("字段 $key 缺失")
        val value = opt(key)
        if (value !is Number) throw DirectiveProtocolException("字段 $key 类型错误")
        val number = value.toDouble()
        if (number != Math.floor(number) || number.isInfinite()) {
            throw DirectiveProtocolException("字段 $key 必须是整数")
        }
        if (number <= 0.0 || number > 9_007_199_254_740_991.0) {
            throw DirectiveProtocolException("字段 $key 不合法")
        }
        return number.toLong()
    }

    private fun JSONObject.intInRange(key: String, min: Int, max: Int): Int {
        if (!has(key) || isNull(key)) throw DirectiveProtocolException("字段 $key 缺失")
        val value = opt(key)
        if (value !is Number) throw DirectiveProtocolException("字段 $key 类型错误")
        val number = value.toDouble()
        if (number != Math.floor(number) || number.isInfinite()) {
            throw DirectiveProtocolException("字段 $key 必须是整数")
        }
        val intValue = number.toInt()
        if (intValue < min || intValue > max) throw DirectiveProtocolException("字段 $key 超出允许范围")
        return intValue
    }
}

// ── 本地持久化（设计 §8.2）：快照 JSON 的编码/解码保持纯函数，便于单元测试 ──────────────

internal fun TrainingDirectiveSnapshot.toStoreJson(): JSONObject = JSONObject()
    .put("patientId", patientId)
    .put("directiveVersion", directiveVersion)
    .put("state", state.wireValue)
    .put("serverTime", serverTime)
    .put("fetchedAtEpochMillis", fetchedAtEpochMillis)
    .put("message", message)
    .put("prescription", prescription?.toStoreJson())
    .put("stopOrder", stopOrder?.toStoreJson())
    .put("compatibilityCode", compatibilityCode)
    .put("compatibilityMessage", compatibilityMessage)
    .put("resourceLease", resourceLease?.toJson())

internal fun DownloadedOrder.toStoreJson(): JSONObject = JSONObject()
    .put("prescription", prescription.toJson())
    .put("reason", reason)
    .put("createdBy", createdBy)
    .put("createdAt", createdAt)
    .put("receivedAt", receivedAt)
    .put("assetsReadyAt", assetsReadyAt)

internal fun DownloadedStopOrder.toStoreJson(): JSONObject = JSONObject()
    .put("orderId", orderId)
    .put("reason", reason)
    .put("createdBy", createdBy)
    .put("createdAt", createdAt)
    .put("receivedAt", receivedAt)

/** 读回本地快照；任何解析失败都当作“没有可用快照”，不抛出到界面。 */
internal fun directiveSnapshotFromStoreJson(raw: String): TrainingDirectiveSnapshot? = runCatching {
    val json = JSONObject(raw)
    val state = TrainingDirectiveState.fromWire(json.optString("state"))
        ?: return null
    TrainingDirectiveSnapshot(
        patientId = json.getString("patientId"),
        // 旧格式快照没有这个字段：不补默认值，按 0 读入后由调用方判定为不可训练。
        directiveVersion = json.optInt("directiveVersion", 0),
        state = state,
        serverTime = json.optString("serverTime"),
        fetchedAtEpochMillis = json.optLong("fetchedAtEpochMillis", 0L),
        message = json.optString("message"),
        prescription = json.optJSONObject("prescription")?.let(::storedOrder),
        stopOrder = json.optJSONObject("stopOrder")?.let(::storedStopOrder),
        compatibilityCode = json.optString("compatibilityCode").takeIf(String::isNotBlank),
        compatibilityMessage = json.optString("compatibilityMessage").takeIf(String::isNotBlank),
        resourceLease = json.optJSONObject("resourceLease")?.let(TrainingResourceLease::fromJson)
    )
}.getOrNull()

/** 旧快照没有 `prescription` 对象（只有裸 items），V2 解析失败即视为无处方。 */
private fun storedOrder(json: JSONObject): DownloadedOrder? {
    val raw = json.optJSONObject("prescription") ?: return null
    val prescription = runCatching {
        AdmissionPrescriptionV2.fromJson(raw, itemIdKey = "itemId")
    }.getOrNull() ?: return null
    return DownloadedOrder(
        prescription = prescription,
        reason = json.optString("reason"),
        createdBy = json.optString("createdBy"),
        createdAt = json.optString("createdAt"),
        receivedAt = (json.opt("receivedAt") as? String)?.takeIf(String::isNotBlank),
        assetsReadyAt = (json.opt("assetsReadyAt") as? String)?.takeIf(String::isNotBlank)
    )
}

private fun storedStopOrder(json: JSONObject): DownloadedStopOrder? {
    val orderId = json.optLong("orderId", 0L)
    if (orderId <= 0L) return null
    return DownloadedStopOrder(
        orderId = orderId,
        reason = json.optString("reason"),
        createdBy = json.optString("createdBy"),
        createdAt = json.optString("createdAt"),
        receivedAt = (json.opt("receivedAt") as? String)?.takeIf(String::isNotBlank)
    )
}
