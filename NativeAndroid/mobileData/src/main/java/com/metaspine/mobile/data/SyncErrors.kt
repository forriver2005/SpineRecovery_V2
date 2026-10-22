package com.metaspine.mobile.data

/**
 * 服务器以非 2xx 响应时抛出。
 *
 * 协调器据此区分两类失败：
 * - 401/403：患者凭据被拒（缺失/撤销/归属不符）→ 暂停冲刷并提示用户更新「服务器设置」，不无限重试；
 * - 其余状态/网络错误：保持原有退避重试。
 *
 * [errorCode] 是服务器 `details.code`（如 `PRE_TRAINING_EVENT_NOT_FOUND`）：
 * 409 在 V2 里既可能是「练前事件未确认」（可等待重试），也可能是
 * `RESULT_REVISION_CONFLICT`（本地事实与服务器矛盾，必须隔离），
 * 只看状态码无法区分，绝不能一律当作可重试或一律隔离。
 */
class ApiHttpException(
    val httpStatus: Int,
    message: String,
    val retryAfterSeconds: Long? = null,
    val errorCode: String? = null,
) : RuntimeException(message)

internal fun parseRetryAfterSeconds(value: String?, nowMillis: Long = System.currentTimeMillis()): Long? {
    val raw = value?.trim()?.takeIf(String::isNotEmpty) ?: return null
    raw.toLongOrNull()?.let { return it.coerceIn(1L, 3600L) }
    val deadline = runCatching {
        java.time.ZonedDateTime.parse(raw, java.time.format.DateTimeFormatter.RFC_1123_DATE_TIME)
            .toInstant().toEpochMilli()
    }.getOrNull() ?: return null
    return ((deadline - nowMillis + 999L) / 1000L).coerceIn(1L, 3600L)
}
