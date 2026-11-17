package com.metaspine.mobile.data

import java.net.HttpURLConnection
import java.net.URL

/** Explicit test-only transport; application defaults always use pinned LAN discovery. */
internal fun openLoopbackTestConnection(url: URL): HttpURLConnection {
    require(url.protocol == "http" && url.host == "127.0.0.1" && url.port in 1..65535)
    return url.openConnection() as HttpURLConnection
}
