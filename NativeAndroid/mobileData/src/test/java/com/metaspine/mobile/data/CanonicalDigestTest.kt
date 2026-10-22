package com.metaspine.mobile.data

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * P5.4 / P0 跨语言规范摘要样例。
 *
 * 样例来自服务器 `tests/fixtures/mobile-bridge/result-canonical-vectors.json`，由服务器
 * `stable()` + SHA-256 生成。测试必须比较**原始 wireJson**，不能先用自己的序列化器重建输入。
 */
class CanonicalDigestTest {

    private fun vectors(): List<JSONObject> {
        val text = javaClass.getResourceAsStream("/fixtures/result-canonical-vectors.json")!!
            .bufferedReader(Charsets.UTF_8).use { it.readText() }
        val root = JSONObject(text)
        val patientId = root.getString("patientId")
        val list = root.getJSONArray("vectors")
        return (0 until list.length()).map { list.getJSONObject(it).put("__patientId", patientId) }
    }

    @Test
    fun `样例文件可读且覆盖必要边界`() {
        val names = vectors().map { it.getString("name") }
        assertTrue("至少 6 条样例", names.size >= 6)
        assertTrue("覆盖整数 80 与 80.0", names.any { it.contains("integer-80") } && names.any { it.contains("decimal-80") })
        assertTrue("覆盖负零", names.any { it.contains("negative-zero") })
    }

    @Test
    fun `规范序列化与服务器 canonicalUtf8 逐字节一致`() {
        for (vector in vectors()) {
            val name = vector.getString("name")
            val wire = JSONObject(vector.getString("wireJson"))
            wire.put("userId", vector.getString("__patientId"))
            assertEquals("规范文本不一致: $name", vector.getString("canonicalUtf8"), CanonicalJson.canonicalize(wire))
        }
    }

    @Test
    fun `规范摘要与服务器 sha256 一致`() {
        for (vector in vectors()) {
            val name = vector.getString("name")
            val wire = JSONObject(vector.getString("wireJson"))
            assertEquals("摘要不一致: $name", vector.getString("sha256"), ResultDigest.contentSha256(wire, vector.getString("__patientId")))
        }
    }

    @Test
    fun `规范文本的 UTF-8 字节与 utf8Hex 一致`() {
        for (vector in vectors()) {
            val name = vector.getString("name")
            val wire = JSONObject(vector.getString("wireJson"))
            wire.put("userId", vector.getString("__patientId"))
            val hex = CanonicalJson.canonicalize(wire)
                .toByteArray(Charsets.UTF_8)
                .joinToString("") { "%02x".format(it) }
            assertEquals("UTF-8 字节不一致: $name", vector.getString("utf8Hex"), hex)
        }
    }

    @Test
    fun `回执字段与本地计算一致`() {
        for (vector in vectors()) {
            val receipt = vector.getJSONObject("receipt")
            val wire = JSONObject(vector.getString("wireJson"))
            assertEquals("回执 sessionId: ${vector.getString("name")}", receipt.getString("sessionId"), wire.getString("sessionId"))
            assertEquals("回执 revision: ${vector.getString("name")}", receipt.getInt("resultRevision"), wire.getInt("resultRevision"))
            assertEquals(
                "回执 hash: ${vector.getString("name")}",
                receipt.getString("resultContentSha256"),
                ResultDigest.contentSha256(wire, vector.getString("__patientId"))
            )
        }
    }

    @Test
    fun `数字格式遵循 JS stringify 语义`() {
        assertEquals("80", CanonicalJson.jsNumber(80.0))
        assertEquals("-0 输出 0", "0", CanonicalJson.jsNumber(-0.0))
        assertEquals("1e-7", CanonicalJson.jsNumber(1.0e-7))
        assertEquals("0.000001", CanonicalJson.jsNumber(0.000001))
        assertEquals("1e+21", CanonicalJson.jsNumber(1.0e21))
        assertEquals("1.5", CanonicalJson.jsNumber(1.5))
        assertEquals("100", CanonicalJson.jsNumber(100.0))
        assertEquals("123456789012345680000", CanonicalJson.jsNumber(1.2345678901234568e20))
    }

    @Test
    fun `字符串转义与键排序`() {
        val value = JSONObject()
            .put("b", 1)
            .put("a", "线\u0001\"\\\n中")
        assertEquals("{\"a\":\"线\\u0001\\\"\\\\\\n中\",\"b\":1}", CanonicalJson.canonicalize(value))
    }
}
