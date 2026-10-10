package com.metaspine.mobile.data

import java.net.URI

/** 所有患者 API 共用的服务器 Origin；Release 只能使用构建时固定的 HTTPS 域名。 */
object ServerOriginPolicy {
    fun normalize(value: String?, releaseOrigin: String, debug: Boolean): String? {
        val candidate = parse(value) ?: return null
        if (debug) return candidate
        val expected = parse(releaseOrigin) ?: return null
        if (!isProductionOrigin(expected) || expected != releaseOrigin) return null
        return candidate.takeIf { it == expected }
    }

    fun isProductionOrigin(value: String): Boolean {
        val uri = runCatching { URI(value) }.getOrNull() ?: return false
        val host = uri.host ?: return false
        return parse(value) == value && uri.scheme == "https" && uri.port == -1 &&
            host.contains('.') && host != "localhost" &&
            !host.matches(Regex("[0-9.]+")) &&
            !host.endsWith(".invalid") && !host.endsWith(".test") && !host.endsWith(".example")
    }

    private fun parse(value: String?): String? {
        val input = value?.trim()?.removeSuffix("/") ?: return null
        if (input.isEmpty() || input.any(Char::isWhitespace)) return null
        val uri = runCatching { URI(input) }.getOrNull() ?: return null
        if (uri.scheme !in setOf("http", "https") || uri.host.isNullOrBlank() ||
            uri.rawUserInfo != null || uri.rawQuery != null || uri.rawFragment != null ||
            !uri.rawPath.isNullOrEmpty() || uri.port !in -1..65535
        ) return null
        val canonical = "${uri.scheme}://${uri.rawAuthority}"
        return canonical.takeIf { it == input }
    }
}
