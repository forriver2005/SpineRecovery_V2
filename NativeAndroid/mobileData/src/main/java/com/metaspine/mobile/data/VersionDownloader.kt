package com.metaspine.mobile.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL

object VersionDownloader {
    private const val CONNECT_TIMEOUT_MS = 15_000
    private const val READ_TIMEOUT_MS = 30_000
    private const val MAX_CONFIG_BYTES = 10 * 1024 * 1024

    suspend fun downloadText(downloadUrl: String, expectedSize: Long? = null): Result<String> = withContext(Dispatchers.IO) {
        runCatching {
            val base = SpineFlowServerConfig.BASE_URL
            var current = resolveDownloadUrl(base, downloadUrl)
            repeat(4) { hop ->
                val connection = current.openConnection() as HttpURLConnection
                try {
                    connection.instanceFollowRedirects = false
                    connection.requestMethod = "GET"
                    connection.connectTimeout = CONNECT_TIMEOUT_MS
                    connection.readTimeout = READ_TIMEOUT_MS
                    connection.setRequestProperty("Accept", "application/json")
                    val status = connection.responseCode
                    if (status in 300..399) {
                        check(hop < 3) { "配置下载重定向过多" }
                        val location = connection.getHeaderField("Location")
                            ?: error("配置下载重定向缺少目标")
                        current = resolveDownloadUrl(base, current.toURI().resolve(location).toString())
                    } else {
                        check(status in 200..299) { "配置下载失败: HTTP $status" }
                        val declaredLength = connection.contentLengthLong
                        check(declaredLength <= MAX_CONFIG_BYTES) { "配置文件过大: $declaredLength bytes" }
                        val bytes = ByteArrayOutputStream()
                        connection.inputStream.use { input ->
                            val buffer = ByteArray(8192)
                            while (true) {
                                val count = input.read(buffer)
                                if (count < 0) break
                                check(bytes.size() + count <= MAX_CONFIG_BYTES) { "配置文件超过大小限制" }
                                bytes.write(buffer, 0, count)
                            }
                        }
                        if (expectedSize != null) {
                            check(bytes.size().toLong() == expectedSize) { "配置文件大小与发布清单不一致" }
                        }
                        return@runCatching bytes.toString(Charsets.UTF_8.name())
                    }
                } finally {
                    connection.disconnect()
                }
            }
            error("配置下载重定向过多")
        }
    }

    /** Server 的 download_url 只允许指向同一 Origin 的已发布版本。 */
    internal fun resolveDownloadUrl(baseUrl: String, downloadUrl: String): URL {
        val base = URI(baseUrl)
        val raw = URI(downloadUrl)
        require(raw.rawPath?.split('/')?.none { it == "." || it == ".." } == true) {
            "配置下载地址包含非法路径"
        }
        val resolved = base.resolve(raw)
        require(resolved.scheme == base.scheme && resolved.host == base.host &&
            resolved.port == base.port && resolved.rawUserInfo == null &&
            resolved.rawQuery == null && resolved.rawFragment == null &&
            resolved.rawPath?.matches(Regex("/api/version/download/[1-9][0-9]*")) == true
        ) { "配置下载地址不属于当前服务器的已发布版本" }
        return resolved.toURL()
    }
}
