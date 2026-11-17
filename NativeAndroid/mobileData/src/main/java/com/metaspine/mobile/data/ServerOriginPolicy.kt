package com.metaspine.mobile.data

import java.net.URI

/** LAN scope identifiers only. No DNS lookup, public origin or direct-network fallback. */
object ServerOriginPolicy {
    fun normalize(value: String?): String? {
        val input = value?.trim()?.removeSuffix("/") ?: return null
        if (input.isEmpty() || input.any(Char::isWhitespace)) return null
        val uri = runCatching { URI(input) }.getOrNull() ?: return null
        val host = uri.host ?: return null
        if (uri.scheme !in setOf("http", "https") ||
            uri.rawUserInfo != null || uri.rawQuery != null || uri.rawFragment != null ||
            !uri.rawPath.isNullOrEmpty() || uri.port !in -1..65535 || uri.port == 0
        ) return null
        if (!(host == "spineflow.local" && uri.port == LocalServerProtocol.PORT) &&
            !LocalServerProtocol.privateIpv4(host)) return null
        return "${uri.scheme}://${uri.rawAuthority}".takeIf { it == input }
    }
}
