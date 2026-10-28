package com.metaspine.mobile.data

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

/** 固定资源下载失败：调用方据此保留 saved 阶段，绝不把半文件当缓存。 */
class PrescriptionAssetException(message: String, cause: Throwable? = null) :
    RuntimeException(message, cause)

/**
 * P4 §4.1：D03 固定字节下载器。
 *
 * 与 JSON 传输和 APK 下载器分开：
 * - 使用处方清单里的相对 `downloadPath`，同源、同医院 PAT；
 * - 只接受原始字节，1 B–16 MiB；
 * - 先写临时文件，核对实际长度与原始字节 SHA-256 后才原子发布；
 * - 截断、超限、hash 不符、来源变更、零字节与残留半文件都不会成为可用缓存。
 */
object PrescriptionAssetClient {
    internal const val MIN_BYTES = 1L
    internal const val MAX_BYTES = 16L * 1024 * 1024
    internal const val CONNECT_TIMEOUT_MS = 10_000
    internal const val READ_TIMEOUT_MS = 30_000
    internal val DOWNLOAD_PATH_PATTERN =
        Regex("^/api/patient/training-directive/orders/[1-9][0-9]{0,15}/items/[1-9][0-9]{0,15}/asset$")
    internal val SHA_PATTERN = Regex("^[a-f0-9]{64}$")

    data class VerifiedAsset(
        val file: File,
        val byteCount: Long,
        val sha256: String,
        val leaseAnchor: ObservedResourceLease? = null
    )

    /**
     * 下载并验证一项资源，成功时返回已发布文件。
     *
     * [publish] 接收临时文件，由存储层完成“移到最终位置”的原子发布并返回最终文件；
     * 失败时客户端负责删除临时文件，不留下可被误认的半成品。
     */
    suspend fun download(
        patientToken: String,
        downloadPath: String,
        expectedSha256: String,
        expectedFileSize: Long,
        origin: () -> String = { SpineFlowServerConfig.BASE_URL },
        openConnection: (URL) -> HttpURLConnection = LocalServerDiscovery::openConnection,
        expectedLease: TrainingResourceLease? = null,
        expectedActionId: String? = null,
        expectedVersionId: Long? = null,
        expectedVariantKey: String? = null,
        elapsedMillis: () -> Long = { android.os.SystemClock.elapsedRealtime() },
        publish: (File) -> File
    ): VerifiedAsset = withContext(Dispatchers.IO) {
        var connection: HttpURLConnection? = null
        try {
            val url = resolveAssetUrl(origin(), downloadPath)
            requireAssetIdentity(expectedSha256, expectedFileSize)
            val temp = File.createTempFile("prescription-asset-", ".part")
            try {
                val requestConnection = openConnection(url)
                connection = requestConnection
                val observed = if (expectedLease != null) elapsedMillis() else 0L
                var leaseAnchor: ObservedResourceLease? = null
                val written = fetchBytes(requestConnection, patientToken, expectedFileSize, temp) { connection ->
                    if (expectedLease != null) {
                        val lease = TrainingResourceLease.fromJson(org.json.JSONObject()
                            .put("contractVersion", 1)
                            .put("generation", connection.getHeaderField("X-Resource-Generation"))
                            .put("revision", connection.getHeaderField("X-Resource-Revision")?.toLongOrNull())
                            .put("serverTime", connection.getHeaderField("X-Resource-Server-Time")?.toLongOrNull())
                            .put("expiresAt", connection.getHeaderField("X-Resource-Lease-Expires")?.toLongOrNull()))
                        lease.requireSuccessorOf(expectedLease)
                        leaseAnchor = ObservedResourceLease(lease, observed)
                        require(connection.getHeaderField("X-Motion-SHA256") == expectedSha256 &&
                            connection.getHeaderField("X-Motion-Action-Id") == expectedActionId &&
                            connection.getHeaderField("X-Motion-Version-Id") == expectedVersionId.toString() &&
                            connection.getHeaderField("X-Motion-Variant-Key") == expectedVariantKey) { "资源响应身份与处方不一致" }
                    }
                }
                val actualSha256 = sha256Hex(temp)
                if (written != expectedFileSize) {
                    throw PrescriptionAssetException(
                        "资源长度与处方清单不一致: $written / $expectedFileSize")
                }
                if (actualSha256 != expectedSha256) {
                    throw PrescriptionAssetException("资源摘要与处方清单不一致")
                }
                if (leaseAnchor != null && !leaseAnchor!!.isValidAt(elapsedMillis())) {
                    throw PrescriptionAssetException("资源下载期间租约已过期")
                }
                currentCoroutineContext().ensureActive()
                LocalServerDiscovery.validateConnection(requestConnection)
                VerifiedAsset(publish(temp), written, actualSha256, leaseAnchor)
            } finally {
                if (temp.exists()) temp.delete()
            }
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (error: PrescriptionAssetException) {
            connection?.let(LocalServerDiscovery::invalidateConnection)
            throw error
        } catch (error: ApiHttpException) {
            // 401/404/503 的语义由调用方区分（凭据、撤销、租约），不能被包成通用下载失败。
            throw error
        } catch (error: Exception) {
            connection?.let(LocalServerDiscovery::invalidateConnection)
            currentCoroutineContext().ensureActive()
            throw PrescriptionAssetException(error.message ?: "资源下载失败", error)
        } finally {
            connection?.disconnect()
        }
    }

    /** 只允许清单内的相对路径，且必须与当前 Origin 同源；禁止跨源与重定向。 */
    internal fun resolveAssetUrl(origin: String, downloadPath: String): URL {
        if (!DOWNLOAD_PATH_PATTERN.matches(downloadPath)) {
            throw PrescriptionAssetException("资源下载路径不合法")
        }
        val base = java.net.URI(origin)
        if (base.scheme !in setOf("https", "http") || base.host == null ||
            base.rawUserInfo != null || base.rawQuery != null || base.rawFragment != null ||
            base.rawPath.orEmpty() !in setOf("", "/")) {
            throw PrescriptionAssetException("服务器来源无效")
        }
        val url = PatientHttpTransport.resolveUrl(origin, downloadPath)
        if (url.protocol != base.scheme || url.host != base.host || url.port != base.port) {
            throw PrescriptionAssetException("资源下载地址不属于当前服务器")
        }
        return url
    }

    private fun requireAssetIdentity(expectedSha256: String, expectedFileSize: Long) {
        if (!SHA_PATTERN.matches(expectedSha256)) throw PrescriptionAssetException("资源摘要格式不合法")
        if (expectedFileSize !in MIN_BYTES..MAX_BYTES) {
            throw PrescriptionAssetException("资源大小超出允许范围: $expectedFileSize")
        }
    }

    private suspend fun fetchBytes(
        connection: HttpURLConnection,
        patientToken: String,
        expectedFileSize: Long,
        target: File,
        validateHeaders: (HttpURLConnection) -> Unit
    ): Long {
        val context = currentCoroutineContext()
        context.ensureActive()
            connection.instanceFollowRedirects = false
            connection.useCaches = false
            connection.requestMethod = "GET"
            connection.connectTimeout = CONNECT_TIMEOUT_MS
            connection.readTimeout = READ_TIMEOUT_MS
            connection.setRequestProperty("Accept", "application/octet-stream")
            if (patientToken.isNotBlank()) {
                connection.setRequestProperty("Authorization", "Bearer $patientToken")
            }
            val status = connection.responseCode
            LocalServerDiscovery.validateConnection(connection)
            when {
                status in 300..399 -> throw PrescriptionAssetException("资源下载不允许重定向")
                status == 401 || status == 403 ->
                    throw ApiHttpException(status, "患者凭据无权下载该资源")
                status == 404 -> throw ApiHttpException(status, "处方资源不存在或已撤销")
                status !in 200..299 -> throw ApiHttpException(status, "资源下载失败，HTTP $status")
            }
            validateHeaders(connection)
            val declared = connection.contentLengthLong
            if (declared > MAX_BYTES) throw PrescriptionAssetException("资源超过 16 MiB 上限")
            if (declared > 0L && declared != expectedFileSize) {
                throw PrescriptionAssetException(
                    "服务器声明的资源长度与处方清单不一致: $declared / $expectedFileSize")
            }
            return connection.inputStream.use { input ->
                FileOutputStream(target).use { output ->
                    val buffer = ByteArray(64 * 1024)
                    var total = 0L
                    while (true) {
                        context.ensureActive()
                        val count = input.read(buffer)
                        if (count < 0) break
                        total += count
                        if (total > MAX_BYTES || total > expectedFileSize) {
                            throw PrescriptionAssetException("资源超过处方声明的大小")
                        }
                        output.write(buffer, 0, count)
                    }
                    output.fd.sync()
                    LocalServerDiscovery.validateConnection(connection)
                    total
                }
            }
    }

    internal fun sha256Hex(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                digest.update(buffer, 0, count)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }
}
