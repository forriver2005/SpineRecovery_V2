package com.metaspine.mobile.data

import org.json.JSONObject
import java.net.URI
import java.net.URL
import java.security.MessageDigest

/** Public discovery metadata only. Credentials are sent only over the pinned TLS connection. */
internal data class LocalServerTrust(
    val certificateSha256: String,
    val serviceId: String,
    val hospitalKey: String
) {
    init {
        require(certificateSha256.matches(Regex("[0-9a-f]{64}")))
        require(serviceId.isNotBlank() && hospitalKey.isNotBlank())
    }

    fun matches(identity: ServiceIdentity): Boolean = identity.isHospital &&
        identity.serviceId == serviceId && identity.hospitalKey == hospitalKey

    fun matches(binding: BindingScope): Boolean =
        binding.serviceId == serviceId && binding.hospitalKey == hospitalKey

    fun acceptsCertificate(encoded: ByteArray): Boolean =
        MessageDigest.isEqual(certificateSha256.toByteArray(Charsets.US_ASCII),
            LocalServerProtocol.sha256(encoded).toByteArray(Charsets.US_ASCII))
}

internal data class LocalServerCandidate(val host: String, val port: Int = 3001)

internal object LocalServerProtocol {
    const val PORT = 3001
    const val PROTOCOL = "spineflow-discovery"
    const val MAX_PACKET_BYTES = 2048

    fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes).joinToString("") { "%02x".format(it) }

    fun ipv4(value: String): Long? {
        val parts = value.split('.')
        if (parts.size != 4) return null
        val bytes = parts.map {
            if (!it.matches(Regex("0|[1-9][0-9]{0,2}"))) return null
            it.toIntOrNull()?.takeIf { octet -> octet in 0..255 } ?: return null
        }
        return bytes.fold(0L) { result, byte -> (result shl 8) or byte.toLong() }
    }

    fun privateIpv4(value: String): Boolean {
        val address = ipv4(value) ?: return false
        return address ushr 24 == 10L || address ushr 20 == 0xac1L || address ushr 16 == 0xc0a8L
    }

    fun sameSubnet(local: String, remote: String, prefix: Int): Boolean {
        if (prefix !in 1..30 || !privateIpv4(local) || !privateIpv4(remote)) return false
        val a = ipv4(local) ?: return false
        val b = ipv4(remote) ?: return false
        val mask = (0xffffffffL shl (32 - prefix)) and 0xffffffffL
        val network = a and mask
        val broadcast = network or (mask xor 0xffffffffL)
        return b and mask == network && b != network && b != broadcast
    }

    fun broadcast(local: String, prefix: Int): String {
        require(prefix in 1..30)
        val address = requireNotNull(ipv4(local))
        val mask = (0xffffffffL shl (32 - prefix)) and 0xffffffffL
        val result = address or (mask xor 0xffffffffL)
        return listOf(24, 16, 8, 0).joinToString(".") { ((result ushr it) and 255).toString() }
    }

    fun request(nonce: String): ByteArray {
        require(nonce.matches(Regex("[0-9a-f]{32}")))
        return JSONObject().put("protocol", PROTOCOL).put("version", 1).put("nonce", nonce)
            .toString().toByteArray(Charsets.UTF_8)
    }

    fun parseCandidate(bytes: ByteArray, nonce: String, source: String, local: String,
                       prefix: Int, trust: LocalServerTrust): LocalServerCandidate? = runCatching {
        require(bytes.size in 1..MAX_PACKET_BYTES)
        val json = JSONObject(bytes.toString(Charsets.UTF_8))
        require(json.getString("protocol") == PROTOCOL && json.getInt("version") == 1)
        require(json.getString("nonce") == nonce)
        require(json.getString("host") == source && sameSubnet(local, source, prefix))
        require(json.getInt("port") == PORT)
        require(json.getString("certificateSha256") == trust.certificateSha256)
        require(json.getString("serviceId") == trust.serviceId && json.getString("hospitalKey") == trust.hospitalKey)
        LocalServerCandidate(source)
    }.getOrNull()

    fun origin(url: URL): String {
        require(url.protocol in setOf("http", "https") && url.userInfo == null && url.ref == null)
        val port = if (url.port == -1) "" else ":${url.port}"
        return "${url.protocol}://${url.host}$port"
    }

    fun normalizeOrigin(value: String): String {
        val uri = URI(value)
        require(uri.scheme in setOf("http", "https") && !uri.host.isNullOrBlank() &&
            uri.rawUserInfo == null && uri.rawQuery == null && uri.rawFragment == null &&
            uri.rawPath.isNullOrEmpty() && uri.port in -1..65535)
        return origin(uri.toURL()).also { require(it == value) }
    }

    fun physicalUrl(logicalUrl: URL, candidate: LocalServerCandidate): URL {
        require(candidate.port == PORT && privateIpv4(candidate.host))
        origin(logicalUrl) // Reject user-info and fragments before rewriting the transport address.
        return URL("https", candidate.host, PORT, logicalUrl.file.ifEmpty { "/" })
    }
}
