package com.metaspine.mobile.data

import com.sun.net.httpserver.HttpServer
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.io.File
import java.net.InetSocketAddress
import java.security.MessageDigest

/**
 * P4.3：D03 原始字节下载——长度、摘要、原子发布与拒绝半文件。
 */
class PrescriptionAssetClientTest {

    private lateinit var server: HttpServer
    private lateinit var root: File

    private val payload = ByteArray(300_000) { (it * 31 % 251).toByte() }
    private val sha = MessageDigest.getInstance("SHA-256").run {
        update(payload); digest().joinToString("") { "%02x".format(it) }
    }
    private val path = "/api/patient/training-directive/orders/5001/items/1001/asset"
    private var response: (com.sun.net.httpserver.HttpExchange) -> Unit = {}

    @Before
    fun setUp() {
        root = File.createTempFile("assets", "").let {
            it.delete(); it.mkdirs(); it
        }
        server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0).apply {
            createContext(path) { exchange ->
                response(exchange)
                exchange.close()
            }
            start()
        }
    }

    @After
    fun tearDown() {
        server.stop(0)
        root.deleteRecursively()
    }

    private fun origin() = "http://127.0.0.1:${server.address.port}"

    private fun scope() = BindingScope(origin(), "svc", "hospital_a", "医院A", "p_1001", "a1", 1)

    private fun store() = PrescriptionAssetStore(root, scope())

    private fun download(
        expectedSha: String = sha,
        expectedSize: Long = payload.size.toLong(),
        token: String = "pat_test",
        downloadPath: String = path,
        expectedLease: TrainingResourceLease? = null,
        publish: (File) -> File = { store().publish(5001, 1001, 317, "single", sha, it) }
    ) = runBlocking {
        PrescriptionAssetClient.download(
            patientToken = token,
            downloadPath = downloadPath,
            expectedSha256 = expectedSha,
            expectedFileSize = expectedSize,
            origin = ::origin,
            openConnection = ::openLoopbackTestConnection,
            expectedLease = expectedLease,
            expectedActionId = "dead_bug",
            expectedVersionId = 317,
            expectedVariantKey = "single",
            elapsedMillis = { 0L },
            publish = publish
        )
    }

    private fun respondBytes(bytes: ByteArray = payload) {
        response = { exchange ->
            exchange.responseHeaders.add("Authorization-Seen", exchange.requestHeaders
                .getFirst("Authorization") ?: "")
            exchange.sendResponseHeaders(200, bytes.size.toLong())
            exchange.responseBody.write(bytes)
        }
    }

    private val lease = TrainingResourceLease(1, "generation-a", 7, 100_000, 160_000)

    private fun respondAuthorizedBytes(
        bytes: ByteArray = payload,
        responseLease: TrainingResourceLease = lease,
        actionId: String = "dead_bug"
    ) {
        response = { exchange ->
            val headers = exchange.responseHeaders
            headers.add("X-Resource-Generation", responseLease.generation)
            headers.add("X-Resource-Revision", responseLease.revision.toString())
            headers.add("X-Resource-Server-Time", responseLease.serverTime.toString())
            headers.add("X-Resource-Lease-Expires", responseLease.expiresAt.toString())
            headers.add("X-Motion-Action-Id", actionId)
            headers.add("X-Motion-Version-Id", "317")
            headers.add("X-Motion-Variant-Key", "single")
            headers.add("X-Motion-SHA256", MessageDigest.getInstance("SHA-256").digest(bytes)
                .joinToString("") { "%02x".format(it) })
            exchange.sendResponseHeaders(200, bytes.size.toLong())
            exchange.responseBody.write(bytes)
        }
    }

    @Test
    fun `下载校验医院租约与固定动作响应身份`() {
        respondAuthorizedBytes()
        val downloaded = download(expectedLease = lease)
        assertEquals(lease, downloaded.leaseAnchor!!.lease)
        assertTrue(downloaded.leaseAnchor!!.isValidAt(59_999))
        assertFalse(downloaded.leaseAnchor!!.isValidAt(60_000))
    }

    @Test
    fun `缺租约代际失配或响应动作错误不发布文件`() {
        respondBytes()
        assertThrows(PrescriptionAssetException::class.java) { download(expectedLease = lease) }
        respondAuthorizedBytes(responseLease = lease.copy(generation = "other"))
        assertThrows(PrescriptionAssetException::class.java) { download(expectedLease = lease) }
        respondAuthorizedBytes(actionId = "bird_dog")
        assertThrows(PrescriptionAssetException::class.java) { download(expectedLease = lease) }
        assertTrue(root.walkTopDown().none { it.name.endsWith(".bin") })
    }

    @Test
    fun `十一MiB资源使用固定字节通道完整下载`() {
        val bytes = ByteArray(11 * 1024 * 1024) { (it % 251).toByte() }
        val largeSha = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
        respondAuthorizedBytes(bytes)
        val downloaded = download(expectedSha = largeSha, expectedSize = bytes.size.toLong(), expectedLease = lease,
            publish = { temp -> store().publish(5001, 1001, 317, "single", largeSha, temp) })
        assertEquals(bytes.size.toLong(), downloaded.byteCount)
        assertEquals(largeSha, PrescriptionAssetClient.sha256Hex(downloaded.file))
    }

    @Test
    fun `相同医院编号但不同Origin的同号资源索引不会覆盖`() {
        respondBytes()
        val first = download()
        val other = PrescriptionAssetStore(root, scope().copy(origin = "https://hospital-b.example.com"))
        val temp = File(root, "other-source.part").apply { writeBytes(payload) }
        val second = other.publish(5001, 1001, 317, "single", sha, temp)
        assertFalse(first.file.canonicalPath == second.canonicalPath)
        assertEquals(2, store().readIndex().size)
        assertNotNull(PrescriptionAssetStore(root, scope()).existing(5001, 1001, sha))
        assertNotNull(PrescriptionAssetStore(root, scope().copy(origin = "https://hospital-b.example.com"))
            .existing(5001, 1001, sha))
    }

    @Test
    fun `原始字节通过长度与摘要校验并原子发布`() {
        respondBytes()
        val seenAuth = StringBuilder()
        response = { exchange ->
            seenAuth.append(exchange.requestHeaders.getFirst("Authorization"))
            exchange.sendResponseHeaders(200, payload.size.toLong())
            exchange.responseBody.write(payload)
        }
        val result = download()
        assertEquals(payload.size.toLong(), result.byteCount)
        assertEquals(sha, result.sha256)
        assertTrue(result.file.isFile)
        assertTrue(result.file.readBytes().contentEquals(payload))
        assertEquals("Bearer pat_test", seenAuth.toString())
        // 临时文件不残留
        assertEquals(emptyList<String>(), result.file.parentFile!!.listFiles()!!
            .filter { it.name.endsWith(".part") }.map { it.name })
    }

    @Test
    fun `摘要不符时拒绝且不发布`() {
        respondBytes()
        val wrong = "0".repeat(64)
        assertThrows(PrescriptionAssetException::class.java) {
            download(expectedSha = wrong, publish = { it })
        }
        assertTrue(root.walkTopDown().none { it.name.endsWith(".bin") })
    }

    @Test
    fun `长度与清单不一致时拒绝`() {
        respondBytes()
        assertThrows(PrescriptionAssetException::class.java) {
            download(expectedSize = payload.size + 1L, publish = { it })
        }
    }

    @Test
    fun `截断响应比声明短时拒绝`() {
        response = { exchange ->
            exchange.sendResponseHeaders(200, payload.size.toLong())
            exchange.responseBody.write(payload.copyOf(payload.size / 2))
        }
        assertThrows(PrescriptionAssetException::class.java) { download() }
    }

    @Test
    fun `超过16MiB在下载前拒绝`() {
        assertThrows(PrescriptionAssetException::class.java) {
            download(expectedSize = 16L * 1024 * 1024 + 1)
        }
    }

    @Test
    fun `零字节资源拒绝`() {
        assertThrows(PrescriptionAssetException::class.java) { download(expectedSize = 0L) }
    }

    @Test
    fun `重定向拒绝且不跟随`() {
        response = { exchange ->
            exchange.responseHeaders.add("Location", "http://evil.example.com/x")
            exchange.sendResponseHeaders(302, -1)
        }
        assertThrows(PrescriptionAssetException::class.java) { download() }
    }

    @Test
    fun `跨来源与非法路径拒绝`() {
        assertThrows(PrescriptionAssetException::class.java) { download(downloadPath = "/api/version/download/1") }
        assertThrows(PrescriptionAssetException::class.java) { download(downloadPath = "../x") }
        assertThrows(PrescriptionAssetException::class.java) {
            PrescriptionAssetClient.resolveAssetUrl(
                "http://127.0.0.1:1", path.replace("orders/5001", "orders/../5001"))
        }
    }

    @Test
    fun `401与404保留HTTP语义`() {
        response = { exchange -> exchange.sendResponseHeaders(401, -1) }
        val unauthorized = assertThrows(ApiHttpException::class.java) { download() }
        assertEquals(401, unauthorized.httpStatus)
        response = { exchange -> exchange.sendResponseHeaders(404, -1) }
        val missing = assertThrows(ApiHttpException::class.java) { download() }
        assertEquals(404, missing.httpStatus)
    }

    @Test
    fun `发布后的文件可被存储层重新校验`() {
        respondBytes()
        val result = download()
        val record = store().recordFor(5001, 1001)
        assertNotNull(record)
        val stored = store().verifiedFile(record!!)
        assertNotNull(stored)
        assertEquals(payload.size.toLong(), stored!!.length())
    }

    @Test
    fun `被外部截断的缓存读取时删除`() {
        respondBytes()
        val result = download()
        result.file.writeBytes(byteArrayOf(1, 2, 3))
        val record = store().recordFor(5001, 1001)
        assertNotNull(record)
        assertNull(store().verifiedFile(record!!))
        assertFalse(result.file.exists())
    }

    @Test
    fun `其它医院作用域看不到本医院缓存`() {
        respondBytes()
        val result = download()
        val other = PrescriptionAssetStore(root, scope().copy(hospitalKey = "hospital_b"))
        assertNull(other.recordFor(5001, 1001))
        assertNull(other.verifiedFile(store().recordFor(5001, 1001)!!))
        assertTrue(result.file.exists())
    }
}
