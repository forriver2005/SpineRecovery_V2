package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal

/**
 * 服务器 `lib/prescription-v2-results.js` 中 `stable()` 的等价实现，用于结果规范摘要。
 *
 * 规则（P5 / 技术文档第 9 节）：
 * 1. 对象键递归排序，数组保持原顺序；
 * 2. 基本值按 JS `JSON.stringify` 语义输出数字与字符串；
 * 3. 紧凑 UTF-8，不含空白。
 *
 * 不能用 org.json 的默认文本直接求 hash——键序、数字格式（80 / 80.0 / -0）都与 JS 不同。
 */
internal object CanonicalJson {

    fun canonicalize(value: Any?): String = StringBuilder().also { append(value, it) }.toString()

    private fun append(value: Any?, out: StringBuilder) {
        when (value) {
            null, JSONObject.NULL -> out.append("null")
            is JSONObject -> appendObject(value, out)
            is JSONArray -> appendArray(value, out)
            is String -> appendString(value, out)
            is Boolean -> out.append(if (value) "true" else "false")
            is Double -> out.append(jsNumber(value))
            is Float -> out.append(jsNumber(value.toDouble()))
            is Int, is Long, is Short, is Byte -> out.append(value.toString())
            is Number -> out.append(jsNumber(value.toDouble()))
            else -> throw IllegalArgumentException("规范摘要不支持的值类型: ${value::class.java.name}")
        }
    }

    private fun appendObject(obj: JSONObject, out: StringBuilder) {
        val keys = obj.keys().asSequence().toList().sortedWith(String::compareTo)
        out.append('{')
        keys.forEachIndexed { index, key ->
            if (index > 0) out.append(',')
            appendString(key, out)
            out.append(':')
            append(obj.opt(key), out)
        }
        out.append('}')
    }

    private fun appendArray(array: JSONArray, out: StringBuilder) {
        out.append('[')
        for (index in 0 until array.length()) {
            if (index > 0) out.append(',')
            append(array.opt(index), out)
        }
        out.append(']')
    }

    /** JSON.stringify 的字符串转义：只转义引号、反斜杠和控制字符。 */
    private fun appendString(text: String, out: StringBuilder) {
        out.append('"')
        for (ch in text) {
            when (ch) {
                '"' -> out.append("\\\"")
                '\\' -> out.append("\\\\")
                '\b' -> out.append("\\b")
                '\u000C' -> out.append("\\f")
                '\n' -> out.append("\\n")
                '\r' -> out.append("\\r")
                '\t' -> out.append("\\t")
                else -> if (ch < ' ') {
                    out.append("\\u").append(ch.code.toString(16).padStart(4, '0'))
                } else {
                    out.append(ch)
                }
            }
        }
        out.append('"')
    }

    /**
     * JS `Number.prototype.toString()` 的最小实现：80.0 → "80"，-0.0 → "0"，
     * 1.0e-7 → "1e-7"，1.0e21 → "1e+21"。
     */
    internal fun jsNumber(value: Double): String {
        require(value.isFinite()) { "结果中不允许非有限数值" }
        if (value == 0.0) return "0"
        if (value % 1.0 == 0.0 && value >= -9.007199254740992E15 && value <= 9.007199254740992E15) {
            return value.toLong().toString()
        }
        val negative = value < 0
        // BigDecimal.valueOf(Double) 走 Double.toString 的最短往返表示，等价于 JS ToShortest。
        val decimal = BigDecimal.valueOf(value).stripTrailingZeros()
        val digits = decimal.unscaledValue().abs().toString()
        val k = digits.length
        val n = k - decimal.scale()          // 小数点位置：value = 0.digits × 10^n
        val sign = if (negative) "-" else ""

        if (k <= n && n <= 21) {
            return sign + digits + "0".repeat(n - k)
        }
        if (0 < n && n <= 21) {
            return sign + digits.substring(0, n) + "." + digits.substring(n)
        }
        if (-6 < n && n <= 0) {
            return sign + "0." + "0".repeat(-n) + digits
        }
        val exponent = n - 1
        val expText = if (exponent >= 0) "+$exponent" else exponent.toString()
        return if (k == 1) {
            sign + digits + "e" + expText
        } else {
            sign + digits.first() + "." + digits.substring(1) + "e" + expText
        }
    }
}

/**
 * 结果规范摘要：注入 PAT 对应的 userId → 规范序列化 → UTF-8 → SHA-256 小写十六进制。
 *
 * 与服务器 `prepareResult` 中 `hash(stable({...raw, userId: patientId}))` 完全一致；
 * 不是 HTTP 原文摘要，也不是 manifest 摘要。
 */
internal object ResultDigest {

    fun contentSha256(session: JSONObject, patientId: String): String {
        require(patientId.isNotBlank()) { "缺少患者身份，无法计算结果摘要" }
        // JSONObject 是浅拷贝，注入前复制一层，避免改写调用方持有的冻结正文。
        val normalized = JSONObject(session.toString())
        normalized.put("userId", patientId)
        return sha256Hex(CanonicalJson.canonicalize(normalized))
    }

    fun sha256Hex(text: String): String {
        val digest = java.security.MessageDigest.getInstance("SHA-256")
        val bytes = digest.digest(text.toByteArray(Charsets.UTF_8))
        return bytes.joinToString("") { "%02x".format(it) }
    }
}
