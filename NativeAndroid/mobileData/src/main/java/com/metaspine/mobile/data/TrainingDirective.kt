package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject

/**
 * M7.1 §8.1：Server 当前有效训练指令快照的本地模型与严格解析器。
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

data class DownloadedPrescriptionItem(
    val position: Int,
    val actionId: String,
    val actionName: String,
    val sets: Int,
    val reps: Int,
    val holdSeconds: Int
)

/** 一条 active 指令（处方或停止训练）的完整内容；`orderId` 即版本号（设计 §4.3）。 */
data class DownloadedOrder(
    val orderId: Long,
    val reason: String,
    val createdBy: String,
    val createdAt: String,
    val receivedAt: String?,
    val items: List<DownloadedPrescriptionItem>
) {
    val singleItem: DownloadedPrescriptionItem? get() = items.singleOrNull()
}

data class TrainingDirectiveSnapshot(
    val patientId: String,
    val state: TrainingDirectiveState,
    val serverTime: String,
    val fetchedAtEpochMillis: Long,
    val message: String,
    val prescription: DownloadedOrder?,
    val stopOrder: DownloadedOrder?,
    val compatibilityMessage: String?
) {
    /** 只有 `ready` 且恰好一条动作时才给出可执行处方；其余状态一律不下发训练。 */
    fun trainableOrder(): DownloadedOrder? =
        if (state == TrainingDirectiveState.Ready) prescription?.takeIf { it.singleItem != null } else null
}

/** 响应结构不符合 M7.1 契约：不覆盖最后一次可展示缓存，本次训练失败关闭。 */
class DirectiveProtocolException(message: String) : RuntimeException(message)

/**
 * 严格解析器（设计 §8.1）。
 *
 * Mobile 不能只相信 `trainingState`：仍复核 JSON 类型、动作白名单、单动作数量与剂量范围。
 */
object TrainingDirectiveParser {
    /** 与 Server `MOBILE_ROUTE_ACTIONS` 一致：AR 当前只有这两个基础动作场景。 */
    val SUPPORTED_ACTION_IDS = setOf("dead_bug", "bird_dog")

    internal const val MAX_TEXT_LENGTH = 500
    internal const val MAX_NAME_LENGTH = 128
    internal const val MAX_TIME_LENGTH = 64
    internal const val MAX_ITEMS = 10
    internal const val MIN_SETS = 1
    internal const val MAX_SETS = 20
    internal const val MIN_REPS = 1
    internal const val MAX_REPS = 100
    internal const val MIN_HOLD_SECONDS = 0
    internal const val MAX_HOLD_SECONDS = 600

    fun parse(
        patientId: String,
        data: JSONObject,
        fetchedAtEpochMillis: Long
    ): TrainingDirectiveSnapshot {
        require(patientId.isNotBlank()) { "缺少本地患者身份，无法确认快照归属" }

        val serverTime = data.text("serverTime", MAX_TIME_LENGTH)
        val state = TrainingDirectiveState.fromWire(data.optString("trainingState"))
            ?: throw DirectiveProtocolException("未知的训练指令状态")
        val message = data.text("message", MAX_TEXT_LENGTH, required = false)
        val prescription = data.optJSONObject("activePrescription")?.let {
            parseOrder(it, expectItems = true)
        }
        val stopOrder = data.optJSONObject("activeStopOrder")?.let {
            parseOrder(it, expectItems = false)
        }
        val compatibility = data.optJSONObject("compatibility")
            ?.text("message", MAX_TEXT_LENGTH, required = false)
            ?.takeIf(String::isNotBlank)

        return when (state) {
            TrainingDirectiveState.Ready -> {
                if (stopOrder != null) {
                    throw DirectiveProtocolException("ready 状态不应同时下发停止训练指令")
                }
                val order = prescription
                    ?: throw DirectiveProtocolException("ready 状态缺少有效处方")
                if (order.items.size != 1) {
                    throw DirectiveProtocolException("ready 状态的处方必须只有一个动作")
                }
                snapshot(patientId, state, serverTime, fetchedAtEpochMillis, message, order, null, null)
            }

            TrainingDirectiveState.Stopped -> {
                val stop = stopOrder
                    ?: throw DirectiveProtocolException("stopped 状态缺少停止训练指令")
                // 防御：即使 Server 异常同时返回处方，也绝不把它当作可执行处方保留。
                snapshot(patientId, state, serverTime, fetchedAtEpochMillis, message, null, stop, null)
            }

            TrainingDirectiveState.WaitingPrescription -> {
                if (prescription != null || stopOrder != null) {
                    throw DirectiveProtocolException("waiting_prescription 不应携带任何有效指令")
                }
                snapshot(patientId, state, serverTime, fetchedAtEpochMillis, message, null, null, null)
            }

            TrainingDirectiveState.UnsupportedPrescription -> {
                val order = prescription
                    ?: throw DirectiveProtocolException("unsupported_prescription 缺少处方内容")
                if (order.items.isEmpty()) {
                    throw DirectiveProtocolException("unsupported_prescription 的处方没有动作")
                }
                snapshot(
                    patientId,
                    state,
                    serverTime,
                    fetchedAtEpochMillis,
                    message,
                    order,
                    null,
                    compatibility ?: message.takeIf(String::isNotBlank)
                )
            }
        }
    }

    private fun snapshot(
        patientId: String,
        state: TrainingDirectiveState,
        serverTime: String,
        fetchedAtEpochMillis: Long,
        message: String,
        prescription: DownloadedOrder?,
        stopOrder: DownloadedOrder?,
        compatibilityMessage: String?
    ) = TrainingDirectiveSnapshot(
        patientId = patientId,
        state = state,
        serverTime = serverTime,
        fetchedAtEpochMillis = fetchedAtEpochMillis,
        message = message,
        prescription = prescription,
        stopOrder = stopOrder,
        compatibilityMessage = compatibilityMessage
    )

    private fun parseOrder(json: JSONObject, expectItems: Boolean): DownloadedOrder {
        val orderId = json.positiveLong("orderId")
        val itemsJson = json.optJSONArray("items")
        val items = mutableListOf<DownloadedPrescriptionItem>()
        if (itemsJson != null) {
            if (itemsJson.length() > MAX_ITEMS) {
                throw DirectiveProtocolException("处方动作数量超过上限")
            }
            for (index in 0 until itemsJson.length()) {
                val item = itemsJson.optJSONObject(index)
                    ?: throw DirectiveProtocolException("处方动作结构不合法")
                items.add(parseItem(item))
            }
        }
        if (expectItems && items.isEmpty()) {
            throw DirectiveProtocolException("处方没有任何动作")
        }
        val positions = items.map { it.position }
        if (positions.size != positions.toSet().size) {
            throw DirectiveProtocolException("处方动作位置重复")
        }
        return DownloadedOrder(
            orderId = orderId,
            reason = json.text("reason", MAX_TEXT_LENGTH, required = false),
            createdBy = json.text("createdBy", MAX_NAME_LENGTH, required = false),
            createdAt = json.text("createdAt", MAX_TIME_LENGTH, required = false),
            receivedAt = json.optString("receivedAt").takeIf(String::isNotBlank),
            items = items
        )
    }

    private fun parseItem(json: JSONObject): DownloadedPrescriptionItem {
        val actionId = json.optString("actionId")
        if (actionId !in SUPPORTED_ACTION_IDS) {
            throw DirectiveProtocolException("处方包含手机端不支持的动作")
        }
        val position = json.optInt("position", -1)
        if (position < 0) throw DirectiveProtocolException("处方动作位置不合法")
        return DownloadedPrescriptionItem(
            position = position,
            actionId = actionId,
            actionName = json.text("actionName", MAX_NAME_LENGTH),
            sets = json.intInRange("sets", MIN_SETS, MAX_SETS),
            reps = json.intInRange("reps", MIN_REPS, MAX_REPS),
            holdSeconds = json.intInRange("holdSeconds", MIN_HOLD_SECONDS, MAX_HOLD_SECONDS)
        )
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
    .put("state", state.wireValue)
    .put("serverTime", serverTime)
    .put("fetchedAtEpochMillis", fetchedAtEpochMillis)
    .put("message", message)
    .put("prescription", prescription?.toStoreJson())
    .put("stopOrder", stopOrder?.toStoreJson())
    .put("compatibilityMessage", compatibilityMessage)

internal fun DownloadedOrder.toStoreJson(): JSONObject = JSONObject()
    .put("orderId", orderId)
    .put("reason", reason)
    .put("createdBy", createdBy)
    .put("createdAt", createdAt)
    .put("receivedAt", receivedAt)
    .put(
        "items",
        JSONArray().also { array ->
            items.forEach { item ->
                array.put(
                    JSONObject()
                        .put("position", item.position)
                        .put("actionId", item.actionId)
                        .put("actionName", item.actionName)
                        .put("sets", item.sets)
                        .put("reps", item.reps)
                        .put("holdSeconds", item.holdSeconds)
                )
            }
        }
    )

/** 读回本地快照；任何解析失败都当作“没有可用快照”，不抛出到界面。 */
internal fun directiveSnapshotFromStoreJson(raw: String): TrainingDirectiveSnapshot? = runCatching {
    val json = JSONObject(raw)
    val state = TrainingDirectiveState.fromWire(json.optString("state"))
        ?: return null
    TrainingDirectiveSnapshot(
        patientId = json.getString("patientId"),
        state = state,
        serverTime = json.optString("serverTime"),
        fetchedAtEpochMillis = json.optLong("fetchedAtEpochMillis", 0L),
        message = json.optString("message"),
        prescription = json.optJSONObject("prescription")?.let(::storedOrder),
        stopOrder = json.optJSONObject("stopOrder")?.let(::storedOrder),
        compatibilityMessage = json.optString("compatibilityMessage").takeIf(String::isNotBlank)
    )
}.getOrNull()

private fun storedOrder(json: JSONObject): DownloadedOrder = DownloadedOrder(
    orderId = json.optLong("orderId", 0L),
    reason = json.optString("reason"),
    createdBy = json.optString("createdBy"),
    createdAt = json.optString("createdAt"),
    receivedAt = json.optString("receivedAt").takeIf(String::isNotBlank),
    items = json.optJSONArray("items")?.let { array ->
        (0 until array.length()).mapNotNull { index ->
            array.optJSONObject(index)?.let { item ->
                DownloadedPrescriptionItem(
                    position = item.optInt("position", 0),
                    actionId = item.optString("actionId"),
                    actionName = item.optString("actionName"),
                    sets = item.optInt("sets", 0),
                    reps = item.optInt("reps", 0),
                    holdSeconds = item.optInt("holdSeconds", 0)
                )
            }
        }
    }.orEmpty()
)
