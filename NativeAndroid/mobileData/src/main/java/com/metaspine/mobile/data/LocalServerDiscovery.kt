package com.metaspine.mobile.data

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.SystemClock
import com.metaspine.mobile.unified.BuildConfig
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.HttpURLConnection
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketTimeoutException
import java.net.URL
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.Collections
import java.util.WeakHashMap
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

/**
 * The stored hospital origins remain stable. Only the transport endpoint follows Wi-Fi changes.
 * Discovery never carries patient data; every resulting connection verifies the preconfigured
 * server certificate before transmitting HTTP headers, tokens or request bodies.
 */
object LocalServerDiscovery {
    @Volatile private var resolver: Resolver? = null
    val isEnabled: Boolean get() = BuildConfig.LOCAL_DISCOVERY_CERT_SHA256.isNotBlank()

    private fun configuredResolver(): Resolver? = if (!isEnabled) null else
        checkNotNull(resolver) { "本地服务器连接尚未初始化，请重启应用" }

    @Synchronized fun initialize(context: Context) {
        if (resolver != null || BuildConfig.LOCAL_DISCOVERY_CERT_SHA256.isBlank()) return
        resolver = Resolver(context.applicationContext, LocalServerTrust(
            BuildConfig.LOCAL_DISCOVERY_CERT_SHA256,
            BuildConfig.LOCAL_DISCOVERY_SERVICE_ID,
            BuildConfig.LOCAL_DISCOVERY_HOSPITAL_KEY))
    }

    fun registerBinding(binding: BindingScope): Boolean = configuredResolver()?.register(binding) ?: true

    fun matchesBinding(binding: BindingScope, origin: String, patientId: String): Boolean =
        configuredResolver()?.let { it.register(binding) && binding.patientId == patientId }
            ?: binding.matchesOriginAndPatient(origin, patientId)

    fun acceptsOrigin(origin: String, serviceId: String, hospitalKey: String): Boolean =
        configuredResolver()?.acceptOrigin(origin, serviceId, hospitalKey) ?: (origin == SpineFlowServerConfig.BASE_URL)

    fun openConnection(url: URL): HttpURLConnection =
        configuredResolver()?.open(url) ?: (url.openConnection() as HttpURLConnection)

    fun validateConnection(connection: HttpURLConnection) { configuredResolver()?.validate(connection) }
    fun invalidateConnection(connection: HttpURLConnection) { configuredResolver()?.invalidate(connection) }

    suspend fun prepareOperation(origin: String = SpineFlowServerConfig.BASE_URL): Long =
        withContext(Dispatchers.IO) { configuredResolver()?.prepare(origin) ?: 0L }

    fun operationEpoch(): Long = configuredResolver()?.currentEpoch() ?: 0L
    fun isCurrentOperation(epoch: Long): Boolean = configuredResolver()?.let { it.currentEpoch() == epoch } ?: true
    fun hasVerifiedBinding(binding: BindingScope): Boolean = configuredResolver()?.hasVerified(binding) ?: true
    fun markBindingVerified(binding: BindingScope) { configuredResolver()?.markVerified(binding) }

    private data class WifiSnapshot(val network: Network, val host: String, val prefix: Int) {
        val key: String get() = "${network.networkHandle}|$host/$prefix"
    }
    private data class Endpoint(val candidate: LocalServerCandidate, val wifi: WifiSnapshot,
                                val epoch: Long, val checkedAt: Long)

    private class Resolver(context: Context, private val trust: LocalServerTrust) {
        private val connectivity = context.getSystemService(ConnectivityManager::class.java)
        private val preferences = context.getSharedPreferences("spineflow_local_server_routes", Context.MODE_PRIVATE)
        private val origins = Collections.synchronizedSet(mutableSetOf(
            LocalServerProtocol.normalizeOrigin(BuildConfig.SERVER_BASE_URL)))
        private val connections = Collections.synchronizedMap(WeakHashMap<HttpURLConnection, Endpoint>())
        private val lock = Any()
        private val discoveryLock = Any()
        private var epoch = 1L
        private var networkKey: String? = null
        private var endpoint: Endpoint? = null
        private val ssl = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf<X509TrustManager>(object : X509TrustManager {
                override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
                    throw CertificateException("Client certificates are not supported")
                }
                override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
                override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
                    val leaf = chain?.firstOrNull() ?: throw CertificateException("服务器证书缺失")
                    leaf.checkValidity()
                    if (!trust.acceptsCertificate(leaf.encoded)) throw CertificateException("不是已配置的本地服务器")
                }
            }), SecureRandom())
        }

        fun register(binding: BindingScope): Boolean =
            acceptOrigin(binding.origin, binding.serviceId, binding.hospitalKey)

        fun acceptOrigin(origin: String, serviceId: String, hospitalKey: String): Boolean {
            if (serviceId != trust.serviceId || hospitalKey != trust.hospitalKey) return false
            return runCatching { origins.add(LocalServerProtocol.normalizeOrigin(origin)); true }.getOrDefault(false)
        }

        private fun verifiedKey(binding: BindingScope): String = "verified_" + LocalServerProtocol.sha256(
            (trust.certificateSha256 + "\n" + binding.storageKey()).toByteArray(Charsets.UTF_8))

        fun hasVerified(binding: BindingScope): Boolean = register(binding) &&
            preferences.getBoolean(verifiedKey(binding), false)

        fun markVerified(binding: BindingScope) {
            check(register(binding)) { "医院绑定与本地服务器不一致" }
            check(preferences.edit().putBoolean(verifiedKey(binding), true).commit()) { "连接校验未能保存，请重试" }
        }

        private fun wifi(): WifiSnapshot? {
            val networks = connectivity.allNetworks.toList().sortedByDescending { it == connectivity.activeNetwork }
            for (network in networks) {
                val capabilities = connectivity.getNetworkCapabilities(network) ?: continue
                if (!capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ||
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) continue
                val link = connectivity.getLinkProperties(network) ?: continue
                for (address in link.linkAddresses) {
                    val host = (address.address as? Inet4Address)?.hostAddress ?: continue
                    if (LocalServerProtocol.privateIpv4(host) && address.prefixLength in 1..30)
                        return WifiSnapshot(network, host, address.prefixLength)
                }
            }
            return null
        }

        private fun observe(): WifiSnapshot? {
            val current = wifi()
            synchronized(lock) {
                if (networkKey != current?.key) {
                    networkKey = current?.key
                    endpoint = null
                    epoch++
                }
            }
            return current
        }

        fun currentEpoch(): Long {
            observe()
            return synchronized(lock) { epoch }
        }

        private fun pinnedConnection(url: URL, wifi: WifiSnapshot): HttpsURLConnection {
            check(url.protocol == "https" && url.port == LocalServerProtocol.PORT)
            return (wifi.network.openConnection(url) as HttpsURLConnection).apply {
                sslSocketFactory = ssl.socketFactory
                hostnameVerifier = javax.net.ssl.HostnameVerifier { _, session ->
                    runCatching { trust.acceptsCertificate(session.peerCertificates.first().encoded) }.getOrDefault(false)
                }
                instanceFollowRedirects = false
                useCaches = false
            }
        }

        private fun probe(candidate: LocalServerCandidate, wifi: WifiSnapshot, timeout: Int): Boolean {
            val connection = pinnedConnection(URL("https://${candidate.host}:3001/api/service-identity"), wifi)
            return try {
                connection.connectTimeout = timeout
                connection.readTimeout = timeout
                if (connection.responseCode != 200) return false
                val bytes = connection.inputStream.use { input ->
                    val buffer = java.io.ByteArrayOutputStream()
                    val chunk = ByteArray(1024)
                    while (true) {
                        val count = input.read(chunk)
                        if (count < 0) break
                        require(buffer.size() + count <= 8192)
                        buffer.write(chunk, 0, count)
                    }
                    buffer.toByteArray()
                }
                trust.matches(ServiceIdentityClient.parse(bytes.toString(Charsets.UTF_8)))
            } catch (_: Exception) { false }
            finally { connection.disconnect() }
        }

        private fun discover(wifi: WifiSnapshot): LocalServerCandidate {
            val nonce = ByteArray(16).also { SecureRandom().nextBytes(it) }.joinToString("") { "%02x".format(it) }
            val request = LocalServerProtocol.request(nonce)
            val broadcasts = setOf(LocalServerProtocol.broadcast(wifi.host, wifi.prefix), "255.255.255.255")
            val deadline = SystemClock.elapsedRealtime() + 4000L
            val tried = mutableSetOf<String>()
            DatagramSocket(null).use { socket ->
                socket.reuseAddress = true
                socket.bind(InetSocketAddress(0))
                wifi.network.bindSocket(socket)
                socket.broadcast = true
                socket.soTimeout = 250
                var nextBroadcast = 0L
                while (SystemClock.elapsedRealtime() < deadline) {
                    if (observe()?.key != wifi.key) throw IOException("Wi-Fi 已变化，请重试")
                    val now = SystemClock.elapsedRealtime()
                    if (now >= nextBroadcast) {
                        for (address in broadcasts) runCatching {
                            socket.send(DatagramPacket(request, request.size, InetAddress.getByName(address), 3001))
                        }
                        nextBroadcast = now + 800L
                    }
                    val packet = DatagramPacket(ByteArray(LocalServerProtocol.MAX_PACKET_BYTES + 1),
                        LocalServerProtocol.MAX_PACKET_BYTES + 1)
                    try { socket.receive(packet) } catch (_: SocketTimeoutException) { continue }
                    val source = packet.address.hostAddress ?: continue
                    val candidate = LocalServerProtocol.parseCandidate(packet.data.copyOf(packet.length),
                        nonce, source, wifi.host, wifi.prefix, trust) ?: continue
                    if (tried.size >= 6 || !tried.add(candidate.host)) continue
                    val remaining = (deadline - SystemClock.elapsedRealtime()).coerceAtMost(700L).toInt()
                    if (remaining > 0 && probe(candidate, wifi, remaining)) return candidate
                }
            }
            throw IOException("未在当前 Wi-Fi 找到已配置的服务器，请确认电脑服务已启动且两端连接同一网络")
        }

        private fun resolve(origin: String): Endpoint = synchronized(discoveryLock) {
            check(origins.contains(LocalServerProtocol.normalizeOrigin(origin))) { "当前账号未授权连接此医院" }
            val current = observe() ?: throw IOException("请先连接服务器所在的 Wi-Fi")
            val old = synchronized(lock) { endpoint }
            if (old != null && old.wifi.key == current.key && SystemClock.elapsedRealtime() - old.checkedAt < 15_000)
                return@synchronized old
            val candidate = discover(current)
            if (observe()?.key != current.key) throw IOException("Wi-Fi 已变化，请重试")
            synchronized(lock) {
                if (old == null || old.candidate != candidate || old.wifi.key != current.key) epoch++
                Endpoint(candidate, current, epoch, SystemClock.elapsedRealtime()).also { endpoint = it }
            }
        }

        fun prepare(origin: String): Long = resolve(origin).epoch

        fun open(url: URL): HttpURLConnection {
            val captured = resolve(LocalServerProtocol.origin(url))
            val connection = pinnedConnection(LocalServerProtocol.physicalUrl(url, captured.candidate), captured.wifi)
            connections[connection] = captured
            validate(connection)
            return connection
        }

        fun validate(connection: HttpURLConnection) {
            val captured = connections[connection] ?: return
            val current = observe()
            if (current?.key != captured.wifi.key || synchronized(lock) { epoch != captured.epoch }) {
                connection.disconnect()
                throw IOException("网络连接已变化，请重试；本地数据已保留")
            }
        }

        fun invalidate(connection: HttpURLConnection) {
            val captured = connections.remove(connection) ?: return
            synchronized(lock) {
                if (endpoint?.epoch == captured.epoch) {
                    endpoint = null
                    epoch++
                }
            }
        }
    }
}
