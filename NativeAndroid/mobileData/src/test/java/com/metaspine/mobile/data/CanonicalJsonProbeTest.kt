package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Test

class CanonicalJsonProbeTest {

    private fun probeNumber(token: String): String = runCatching {
        val obj = JSONObject("{\"s\":$token}")
        val raw = obj.get("s")
        "${raw.javaClass.simpleName} (${obj}) canon=${CanonicalJson.canonicalize(obj)}"
    }.getOrElse { "ERR ${it::class.simpleName}: ${it.message}" }

    @Test
    fun probe() {
        val tokens = listOf(
            "-0.0", "0", "80", "80.0", "1e-7", "0.0000001",
            "9223372036854775807", "9223372036854775808", "-9223372036854775808",
            "1e400", "100000000000000000000", "1.7976931348623157e308"
        )
        tokens.forEach { println("PROBE $it -> " + probeNumber(it)) }
        val array = JSONArray("[1,2.0,3.5,-0.0]")
        println("PROBE array -> " + CanonicalJson.canonicalize(array))
        println("PROBE array roundtrip -> $array")
    }
}
