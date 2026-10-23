package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant
import java.time.format.DateTimeFormatter

/**
 * T01 组合训练结果的 V2 事实（服务器 `lib/prescription-v2-results.js` 的对应物）。
 *
 * 只接收已经完整存在的 V2 快照：缺处方条目、缺逐模式事实时直接拒收，
 * 不从旧 `actionResults`/`gameResults` 推断，也不补默认分数。
 */
enum class ResultSessionStatus(val wireValue: String) {
    NOT_STARTED("NOT_STARTED"),
    INTERRUPTED("INTERRUPTED"),
    COMPLETED("COMPLETED");

    companion object {
        fun fromWire(value: String): ResultSessionStatus? = entries.firstOrNull { it.wireValue == value }
    }
}

enum class ResultMode(val wireValue: String) {
    Coach("coach"),
    Game("game");

    companion object {
        val ORDER: List<ResultMode> = listOf(Coach, Game)
    }
}

/** NOT_STARTED 必须写 not_started；其余模式必须写 ar_measured。 */
enum class ResultModeSource(val wireValue: String) {
    ArMeasured("ar_measured"),
    NotStarted("not_started");

    companion object {
        fun fromWire(value: String): ResultModeSource? = entries.firstOrNull { it.wireValue == value }
    }
}

/** 整条快照来源：mobile_no_details 只表达「全部未开始」的中断快照。 */
enum class ResultSnapshotSource(val wireValue: String) {
    ArMeasured("ar_measured"),
    MobileNoDetails("mobile_no_details");

    companion object {
        fun fromWire(value: String): ResultSnapshotSource? = entries.firstOrNull { it.wireValue == value }
    }
}

/** 一个条目的一种模式（coach 或 game）的真实测量结果。 */
data class ResultModeResult(
    val mode: ResultMode,
    val status: ResultSessionStatus,
    val completedReps: Int,
    val durationSeconds: Int,
    /** 保留真实 null；不把「未测量」写成 0。 */
    val score: Double?,
    val source: ResultModeSource
)

/** 处方中的一个条目及其两种模式。 */
data class ResultItem(
    val itemId: Long,
    val position: Int,
    val actionId: String,
    val actionVersionId: Long,
    val variantKey: String,
    val sha256: String,
    val status: ResultSessionStatus,
    val modeResults: List<ResultModeResult>
) {
    /** 与服务器一致：两种模式都完成才完成，都未开始才未开始，否则中断。 */
    val derivedStatus: ResultSessionStatus
        get() = when {
            modeResults.all { it.status == ResultSessionStatus.COMPLETED } -> ResultSessionStatus.COMPLETED
            modeResults.all { it.status == ResultSessionStatus.NOT_STARTED } -> ResultSessionStatus.NOT_STARTED
            else -> ResultSessionStatus.INTERRUPTED
        }
}

/**
 * 一份合法的 V2 结果快照。
 *
 * 身份字段（sessionId / requestToken / createdAt / prescriptionOrderId /
 * resourceManifestSha256）一经冻结不再改写；[resultRevision] 只在内容真正变化时递增。
 */
data class PrescriptionResultV2(
    val sessionId: String,
    val requestToken: String,
    /** 可选；为空表示只依赖 PAT。摘要始终注入 PAT patientId。 */
    val userId: String?,
    val createdAt: String,
    val completed: Boolean,
    val status: ResultSessionStatus,
    val durationSeconds: Int,
    val prescriptionOrderId: Long,
    val resourceManifestSha256: String,
    val resultRevision: Long,
    val source: ResultSnapshotSource,
    val itemResults: List<ResultItem>,
    /** 可选练前事件；为空时序列化不写出该键。 */
    val preTrainingEventId: String?
) {
    /**
     * 严格白名单序列化。键顺序固定，不附加 feedback / metrics / 旧总分。
     *
     * 分数以 Double 写入，规范文本由 [CanonicalJson] 按 JS 语义输出，
     * 因此 `80` 不会被写成 `80.0`。
     */
    fun toJson(): JSONObject = JSONObject()
        .put("protocolVersion", PrescriptionResultV2.PROTOCOL_VERSION)
        .put("sessionId", sessionId)
        .put("requestToken", requestToken)
        .apply { userId?.let { put("userId", it) } }
        .put("createdAt", createdAt)
        .put("completed", completed)
        .put("status", status.wireValue)
        .put("durationSeconds", durationSeconds)
        .put("prescriptionOrderId", prescriptionOrderId)
        .put("resourceManifestSha256", resourceManifestSha256)
        .put("resultRevision", resultRevision)
        .put("source", source.wireValue)
        .apply { preTrainingEventId?.let { put("preTrainingEventId", it) } }
        .put("itemResults", JSONArray(itemResults.map { item ->
            JSONObject()
                .put("itemId", item.itemId)
                .put("position", item.position)
                .put("actionId", item.actionId)
                .put("actionVersionId", item.actionVersionId)
                .put("variantKey", item.variantKey)
                .put("sha256", item.sha256)
                .put("status", item.status.wireValue)
                .put("modeResults", JSONArray(item.modeResults.map { mode ->
                    JSONObject()
                        .put("mode", mode.mode.wireValue)
                        .put("status", mode.status.wireValue)
                        .put("completedReps", mode.completedReps)
                        .put("durationSeconds", mode.durationSeconds)
                        .put("score", mode.score ?: JSONObject.NULL)
                        .put("source", mode.source.wireValue)
                }))
        }))

    /** 规范摘要：注入 PAT patientId 作为 userId 后求 SHA-256（与服务器完全一致）。 */
    fun contentSha256(patientId: String): String =
        ResultDigest.contentSha256(toJson(), patientId)

    fun toReceipt(patientId: String): ResultReceipt =
        ResultReceipt(sessionId, resultRevision, contentSha256(patientId))

    /** 冻结体：同一份快照只序列化一次，正文与摘要此后不再重算。 */
    fun freeze(patientId: String): FrozenResult {
        val body = CanonicalJson.canonicalize(toJson())
        val canonical = CanonicalJson.canonicalize(
            JSONObject(body).put("userId", patientId)
        )
        return FrozenResult(
            body = body,
            canonicalUtf8 = canonical,
            contentSha256 = ResultDigest.sha256Hex(canonical),
            receipt = ResultReceipt(sessionId, resultRevision, ResultDigest.sha256Hex(canonical))
        )
    }

    companion object {
        const val PROTOCOL_VERSION = 2
        const val MAX_ITEMS = 10
        /** 与服务器上限一致：超出即视为异常输入。 */
        const val MAX_PRE_TRAINING_EVENT_ID_LENGTH = 512

        private val TOKEN = Regex("^[A-Za-z0-9_-]{1,128}$")
        private val SHA256 = Regex("^[a-f0-9]{64}$")
        private val UTC_INSTANT = Regex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d{3})?Z$")

        private val TOP_LEVEL_KEYS = setOf(
            "protocolVersion", "sessionId", "requestToken", "userId", "createdAt",
            "completed", "status", "durationSeconds", "prescriptionOrderId",
            "resourceManifestSha256", "resultRevision", "source", "itemResults",
            "preTrainingEventId"
        )
        private val ITEM_KEYS = setOf(
            "itemId", "position", "actionId", "actionVersionId", "variantKey",
            "sha256", "status", "modeResults"
        )
        private val MODE_KEYS = setOf(
            "mode", "status", "completedReps", "durationSeconds", "score", "source"
        )

        /**
         * 严格解析一份 V2 快照。
         *
         * @param expectedPatientId 当前 PAT 绑定的 patientId。提供时，显式 userId 必须一致。
         * @throws ResultInputException 任何字段、类型、UTC、状态或模式不合法。
         */
        fun parse(raw: JSONObject, expectedPatientId: String? = null): PrescriptionResultV2 {
            rejectUnknownKeys(raw, TOP_LEVEL_KEYS, "训练结果")

            val numericVersion = integer(raw.opt("protocolVersion"), 0, Int.MAX_VALUE.toLong())
                ?: throw ResultInputException.ProtocolUnsupported(
                    "训练结果协议版本不受支持（需要数字 $PROTOCOL_VERSION）"
                )
            if (numericVersion != PROTOCOL_VERSION.toLong()) {
                throw ResultInputException.ProtocolUnsupported("训练结果协议版本不受支持: $numericVersion")
            }

            val sessionId = token(raw, "sessionId")
            val requestToken = token(raw, "requestToken")
            val userId = optionalUserId(raw)
            if (expectedPatientId != null && userId != null && userId != expectedPatientId) {
                throw ResultInputException.PatientMismatch("训练结果的患者身份与本机绑定不一致")
            }
            val createdAt = raw.opt("createdAt")
            if (createdAt !is String || !isStrictUtcInstant(createdAt)) {
                throw invalid("训练结果时间必须是合法的 UTC 时刻（YYYY-MM-DDTHH:mm:ss[.SSS]Z）")
            }

            val statusValue = raw.opt("status")
            val status = (statusValue as? String)?.let(ResultSessionStatus::fromWire)
                ?: throw invalid("训练结果状态不合法: $statusValue")
            if (status == ResultSessionStatus.NOT_STARTED) throw invalid("会话状态只能是 INTERRUPTED 或 COMPLETED")
            val completed = raw.opt("completed") as? Boolean
                ?: throw invalid("训练结果缺少完成标记")
            if (completed != (status == ResultSessionStatus.COMPLETED)) {
                throw invalid("完成标记与训练结果状态矛盾")
            }

            val durationSeconds = integer(raw.opt("durationSeconds"), 0, 86_400)
                ?: throw invalid("训练总时长不合法")
            val prescriptionOrderId = integer(raw.opt("prescriptionOrderId"), 1, Long.MAX_VALUE)
                ?: throw invalid("训练结果未携带有效的处方编号")
            val manifest = raw.opt("resourceManifestSha256")
            if (manifest !is String || !SHA256.matches(manifest)) {
                throw invalid("训练结果缺少有效的处方清单摘要")
            }
            val revision = integer(raw.opt("resultRevision"), 1, Long.MAX_VALUE)
                ?: throw invalid("训练结果修订号不合法")
            val snapshotSource = (raw.opt("source") as? String)
                ?.let(ResultSnapshotSource::fromWire)
                ?: throw invalid("训练结果来源不合法")

            val preTrainingEventId = raw.opt("preTrainingEventId")
                ?.let { value ->
                    val text = value as? String
                        ?: throw invalid("练前事件标识必须是字符串")
                    if (text.isBlank() || text.length > MAX_PRE_TRAINING_EVENT_ID_LENGTH) {
                        throw invalid("练前事件标识长度不合法")
                    }
                    text
                }

            val items = parseItems(raw, snapshotSource)
            if (completed != items.all { it.status == ResultSessionStatus.COMPLETED }) {
                throw invalid("完成标记必须与所有条目完成等价")
            }
            if (items.sumOf { item -> item.modeResults.sumOf(ResultModeResult::durationSeconds) } >
                durationSeconds
            ) {
                throw invalid("训练总时长小于逐模式时长之和")
            }

            return PrescriptionResultV2(
                sessionId = sessionId,
                requestToken = requestToken,
                userId = userId,
                createdAt = createdAt,
                completed = completed,
                status = status,
                durationSeconds = durationSeconds.toInt(),
                prescriptionOrderId = prescriptionOrderId,
                resourceManifestSha256 = manifest,
                resultRevision = revision,
                source = snapshotSource,
                itemResults = items,
                preTrainingEventId = preTrainingEventId
            )
        }

        private fun parseItems(
            raw: JSONObject,
            snapshotSource: ResultSnapshotSource
        ): List<ResultItem> {
            val array = raw.opt("itemResults") as? JSONArray
                ?: throw invalid("训练结果缺少逐条目结果")
            if (array.length() !in 1..MAX_ITEMS) {
                throw invalid("训练结果条目数量不合法: ${array.length()}")
            }
            // 服务器顺序规则：第一处未完成的模式之后，所有模式必须 NOT_STARTED。
            var seenUnfinished = false
            return (0 until array.length()).map { index ->
                val item = array.optJSONObject(index)
                    ?: throw invalid("第 ${index + 1} 个训练条目格式不合法")
                rejectUnknownKeys(item, ITEM_KEYS, "训练条目")
                if (integer(item.opt("position"), 0, (MAX_ITEMS - 1).toLong()) != index.toLong()) {
                    throw invalid("训练条目顺序与处方不一致: 第 ${index + 1} 项")
                }
                if (item.opt("variantKey") != "single") {
                    throw invalid("训练条目变体不受支持")
                }
                val itemId = integer(item.opt("itemId"), 1, Long.MAX_VALUE)
                    ?: throw invalid("训练条目编号不合法")
                val actionId = (item.opt("actionId") as? String)
                    ?.takeIf { it.isNotBlank() && it.length <= 128 }
                    ?: throw invalid("训练条目动作标识不合法")
                val actionVersionId = integer(item.opt("actionVersionId"), 1, Long.MAX_VALUE)
                    ?: throw invalid("训练条目动作版本不合法")
                val sha256 = (item.opt("sha256") as? String)
                    ?.takeIf(SHA256::matches)
                    ?: throw invalid("训练条目资源摘要不合法")

                val modeArray = item.opt("modeResults") as? JSONArray
                    ?: throw invalid("训练条目缺少逐模式结果")
                if (modeArray.length() != ResultMode.ORDER.size) {
                    throw invalid("训练条目必须恰好包含 coach 与 game 两种模式")
                }
                val modes = ResultMode.ORDER.mapIndexed { modeIndex, expected ->
                    val mode = modeArray.optJSONObject(modeIndex)
                        ?: throw invalid("训练条目模式结果格式不合法")
                    rejectUnknownKeys(mode, MODE_KEYS, "模式结果")
                    if (mode.opt("mode") != expected.wireValue) {
                        throw invalid("训练条目模式顺序必须是 coach、game")
                    }
                    parseMode(expected, mode, snapshotSource, seenUnfinished).also { parsed ->
                        if (parsed.status != ResultSessionStatus.COMPLETED) seenUnfinished = true
                    }
                }
                val itemStatus = (item.opt("status") as? String)
                    ?.let(ResultSessionStatus::fromWire)
                    ?: throw invalid("训练条目状态不合法")
                val result = ResultItem(
                    itemId = itemId,
                    position = index,
                    actionId = actionId,
                    actionVersionId = actionVersionId,
                    variantKey = "single",
                    sha256 = sha256,
                    status = itemStatus,
                    modeResults = modes
                )
                if (result.status != result.derivedStatus) {
                    throw invalid("训练条目状态与逐模式结果不一致: 第 ${index + 1} 项")
                }
                result
            }
        }

        private fun parseMode(
            mode: ResultMode,
            json: JSONObject,
            snapshotSource: ResultSnapshotSource,
            seenUnfinished: Boolean
        ): ResultModeResult {
            val status = (json.opt("status") as? String)
                ?.let(ResultSessionStatus::fromWire)
                ?: throw invalid("${mode.wireValue} 模式状态不合法")
            val completedReps = integer(json.opt("completedReps"), 0, 2_000)
                ?: throw invalid("${mode.wireValue} 模式完成次数不合法")
            val durationSeconds = integer(json.opt("durationSeconds"), 0, 86_400)
                ?: throw invalid("${mode.wireValue} 模式时长不合法")
            if (!json.has("score")) throw invalid("${mode.wireValue} 模式缺少 score 字段")
            val score = when (val rawScore = json.opt("score")) {
                JSONObject.NULL -> null
                is Number -> rawScore.toDouble().also {
                    if (!it.isFinite() || it < 0.0 || it > 100.0) {
                        throw invalid("${mode.wireValue} 模式分数超出 0–100")
                    }
                }
                else -> throw invalid("${mode.wireValue} 模式分数必须是数字或 null")
            }
            val source = (json.opt("source") as? String)
                ?.let(ResultModeSource::fromWire)
                ?: throw invalid("${mode.wireValue} 模式数据来源不合法")

            if (status == ResultSessionStatus.NOT_STARTED) {
                if (source != ResultModeSource.NotStarted ||
                    completedReps != 0L || durationSeconds != 0L || score != null
                ) {
                    throw invalid("未开始的模式必须是 0 次、0 秒、无分数、来源 not_started")
                }
            } else {
                if (source != ResultModeSource.ArMeasured) {
                    throw invalid("已开始的模式来源必须是 ar_measured")
                }
                if (status == ResultSessionStatus.COMPLETED && completedReps == 0L) {
                    throw invalid("已完成的模式必须至少完成 1 次")
                }
            }
            if (seenUnfinished && status != ResultSessionStatus.NOT_STARTED) {
                throw invalid("首个未完成的模式之后必须全部未开始")
            }
            if (snapshotSource == ResultSnapshotSource.MobileNoDetails &&
                status != ResultSessionStatus.NOT_STARTED
            ) {
                throw invalid("mobile_no_details 快照只能包含未开始的模式")
            }
            return ResultModeResult(
                mode = mode,
                status = status,
                completedReps = completedReps.toInt(),
                durationSeconds = durationSeconds.toInt(),
                score = score,
                source = source
            )
        }

        private fun rejectUnknownKeys(json: JSONObject, allowed: Set<String>, label: String) {
            val unknown = json.keys().asSequence().filterNot(allowed::contains).toList()
            if (unknown.isNotEmpty()) {
                throw invalid("$label 包含不受支持的字段: ${unknown.sorted().joinToString(",")}")
            }
        }

        private fun token(json: JSONObject, key: String): String {
            val value = json.opt(key)
            if (value !is String || !TOKEN.matches(value)) {
                throw invalid("训练结果缺少合法的 $key（1–128 位字母、数字、下划线或连字符）")
            }
            return value
        }

        private fun optionalUserId(json: JSONObject): String? {
            val value = json.opt("userId") ?: return null
            if (value !is String || value.isBlank() || value.length > 128) {
                throw invalid("训练结果 userId 不合法")
            }
            return value
        }

        private fun integer(value: Any?, min: Long, max: Long): Long? = resultSafeInteger(value, min, max)

        /**
         * 服务器 `utcInstant()`：必须是真实存在的 UTC 时刻，且字符串原样往返一致。
         * 拒绝偏移量、1/2/7 位小数、2 月 30 日与 24:00:00。
         *
         * 精确到毫秒的字符串必须与 [Instant] 的规范输出一致（而不是被归一化成
         * 次日 00:00 或补齐/截断小数位）。`ISO_INSTANT` 省略零毫秒，因此
         * `.000Z` 先归一到不带小数的写法再比对。
         */
        internal fun isStrictUtcInstant(value: String): Boolean {
            if (!UTC_INSTANT.matches(value)) return false
            val parsed = runCatching { Instant.parse(value) }.getOrNull() ?: return false
            val expected = if (value.endsWith(".000Z")) value.dropLast(5) + "Z" else value
            return DateTimeFormatter.ISO_INSTANT.format(parsed) == expected
        }

        private fun invalid(message: String) = ResultInputException.ResultInvalid(message)
    }
}

/** 请求体里的一条逐条确认。 */
data class ResultReceipt(
    val sessionId: String,
    val resultRevision: Long,
    val resultContentSha256: String
) {
    fun toJson(): JSONObject = JSONObject()
        .put("sessionId", sessionId)
        .put("resultRevision", resultRevision)
        .put("resultContentSha256", resultContentSha256)

    companion object {
        fun fromJson(json: JSONObject): ResultReceipt? {
            val sessionId = (json.opt("sessionId") as? String)
                ?.takeIf { Regex("^[A-Za-z0-9_-]{1,128}$").matches(it) } ?: return null
            val revision = resultSafeInteger(json.opt("resultRevision"), 1, MAX_SAFE_RESULT_INTEGER) ?: return null
            val sha = (json.opt("resultContentSha256") as? String)
                ?.takeIf { Regex("^[a-f0-9]{64}$").matches(it) } ?: return null
            return ResultReceipt(sessionId, revision, sha)
        }
    }
}

internal const val MAX_SAFE_RESULT_INTEGER = 9_007_199_254_740_991L

/** 与 JS Number.isSafeInteger 相同：接受 2.0/1e0，拒绝字符串、小数及越界值。 */
internal fun resultSafeInteger(value: Any?, min: Long, max: Long): Long? {
    if (value !is Number) return null
    val number = value.toDouble()
    if (!number.isFinite() || number != kotlin.math.floor(number) ||
        number < min.toDouble() || number > minOf(max, MAX_SAFE_RESULT_INTEGER).toDouble()) return null
    return number.toLong()
}

/**
 * 冻结后的出站事实：正文、规范文本与摘要只计算一次。
 *
 * 摘要必须建立在**解析时就已冻结的规范文本**上，而不是每次重发时重新序列化——
 * 否则任何数字格式或键序漂移都会让服务器把同一份结果算成新内容。
 */
data class FrozenResult(
    val body: String,
    val canonicalUtf8: String,
    val contentSha256: String,
    val receipt: ResultReceipt
)

/** P5.1 输入边界拒收原因；面向患者文案由调用方决定。 */
sealed class ResultInputException(message: String) : Exception(message) {
    /** 缺版本、V1 或字符串 "2"：不能回退或换版本重试。 */
    class ProtocolUnsupported(message: String) : ResultInputException(message)

    /** 字段、类型、UTC、状态或模式非法：保存原因，修正真实输入。 */
    class ResultInvalid(message: String) : ResultInputException(message)

    /** 显式 userId 与当前 PAT 绑定不一致。 */
    class PatientMismatch(message: String) : ResultInputException(message)
}
