package com.metaspine.mobile.data

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction

/** Bounded JSON transport. Business status/DTOs and retry decisions belong to each client. */
internal class PatientHttpTransport(
    private val origin: () -> String = { SpineFlowServerConfig.BASE_URL },
    private val openConnection: (URL) -> HttpURLConnection = { it.openConnection() as HttpURLConnection }
) {
    data class Response(val status: Int, val bytes: ByteArray, val retryAfterSeconds: Long?) {
        val text: String get() = decodeUtf8(bytes)

        fun requireSuccess(message: String) {
            if (status !in 200..299) throw ApiHttpException(status, message, retryAfterSeconds)
        }
    }

    suspend fun request(
        path: String,
        method: String = "GET",
        token: String? = null,
        body: String? = null,
        connectTimeoutMs: Int = 10_000,
        readTimeoutMs: Int = 15_000,
        readErrorStatuses: Set<Int> = emptySet()
    ): Response = withContext(Dispatchers.IO) {
        val context = currentCoroutineContext()
        context.ensureActive()
        val url = resolveUrl(origin(), path)
        require(method == "GET" || method == "POST")
        val connection = openConnection(url)
        try {
            connection.instanceFollowRedirects = false
            connection.useCaches = false
            connection.requestMethod = method
            connection.connectTimeout = connectTimeoutMs
            connection.readTimeout = readTimeoutMs
            connection.setRequestProperty("Accept", "application/json")
            token?.takeIf(String::isNotBlank)?.let {
                connection.setRequestProperty("Authorization", "Bearer $it")
            }
            if (body != null) {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { it.write(body.toByteArray(Charsets.UTF_8)) }
            }
            context.ensureActive()
            val status = connection.responseCode
            val retryAfter = parseRetryAfterSeconds(connection.getHeaderField("Retry-After"))
            // 401/403/429 etc. remain HTTP errors even with malformed or oversized error pages.
            // Only account-status needs a validated error body (the dedicated 410 contract).
            val bytes = if (status in 200..299 || status in readErrorStatuses) {
                require(connection.contentLengthLong <= MAX_RESPONSE_BYTES) { "服务器响应过大" }
                val stream = if (status in 200..299) connection.inputStream else connection.errorStream
                stream?.use {
                    val output = ByteArrayOutputStream()
                    val buffer = ByteArray(8192)
                    while (true) {
                        context.ensureActive()
                        val count = it.read(buffer)
                        if (count < 0) break
                        require(output.size() + count <= MAX_RESPONSE_BYTES) { "服务器响应过大" }
                        output.write(buffer, 0, count)
                    }
                    output.toByteArray()
                } ?: byteArrayOf()
            } else byteArrayOf()
            context.ensureActive()
            decodeUtf8(bytes) // Never silently replace invalid UTF-8 with replacement characters.
            Response(status, bytes, retryAfter)
        } catch (error: Exception) {
            context.ensureActive() // Cancellation must not become an ordinary socket/IO failure.
            throw error
        } finally {
            connection.disconnect()
        }
    }

    companion object {
        const val MAX_RESPONSE_BYTES = 1024 * 1024
        val shared = PatientHttpTransport()

        internal fun resolveUrl(origin: String, path: String): URL {
            val base = URI(origin)
            require(base.scheme in setOf("https", "http") && base.host != null &&
                base.rawUserInfo == null && base.rawQuery == null && base.rawFragment == null &&
                base.rawPath.orEmpty() in setOf("", "/")) { "服务器来源无效" }
            val relative = URI(path)
            require(!relative.isAbsolute && relative.rawAuthority == null &&
                relative.rawFragment == null && relative.rawPath.startsWith("/api/") &&
                relative.normalize().rawPath == relative.rawPath &&
                !relative.rawPath.contains('%') && !relative.rawPath.contains('\\')) {
                "请求必须属于当前服务器 API"
            }
            return base.resolve(relative).toURL()
        }

        internal fun decodeUtf8(bytes: ByteArray): String = Charsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes)).toString()
    }
}

/** Result clients share cancellation propagation, never retry writes implicitly. */
internal suspend fun <T> patientRequestResult(block: suspend () -> T): Result<T> = try {
    Result.success(block())
} catch (cancelled: CancellationException) {
    throw cancelled
} catch (error: Exception) {
    currentCoroutineContext().ensureActive()
    Result.failure(error)
}
