package com.metaspine.mobile.data

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.net.URL

class LocalServerProtocolTest {
    private val trust = LocalServerTrust("a".repeat(64), "hospital-a", "hospital-a")
    private val nonce = "b".repeat(32)

    private fun reply(host: String = "192.168.31.42", mutate: (JSONObject) -> Unit = {}): ByteArray {
        val json = JSONObject().put("protocol", "spineflow-discovery").put("version", 1)
            .put("nonce", nonce).put("host", host).put("port", 3001)
            .put("certificateSha256", trust.certificateSha256)
            .put("serviceId", trust.serviceId).put("hospitalKey", trust.hospitalKey)
        mutate(json)
        return json.toString().toByteArray()
    }

    private fun parse(bytes: ByteArray, source: String = "192.168.31.42") =
        LocalServerProtocol.parseCandidate(bytes, nonce, source, "192.168.31.73", 24, trust)

    @Test fun networkChangeDoesNotRequireTheOldSubnet() {
        assertEquals("192.168.31.42", parse(reply())!!.host)
        assertTrue(LocalServerProtocol.sameSubnet("10.42.1.20", "10.42.200.9", 16))
        assertTrue(LocalServerProtocol.sameSubnet("172.20.4.5", "172.20.7.9", 20))
        assertEquals("10.42.255.255", LocalServerProtocol.broadcast("10.42.1.20", 16))
        assertEquals("192.168.31.255", LocalServerProtocol.broadcast("192.168.31.73", 24))
        assertFalse(LocalServerProtocol.sameSubnet("192.168.3.193", "192.168.31.42", 24))
    }

    @Test fun discoveryMetadataCannotChangeTheTrustedServerOrPort() {
        assertNull(parse(reply { it.put("nonce", "c".repeat(32)) }))
        assertNull(parse(reply { it.put("port", 3000) }))
        assertNull(parse(reply { it.put("certificateSha256", "c".repeat(64)) }))
        assertNull(parse(reply { it.put("serviceId", "hospital-b") }))
        assertNull(parse(reply { it.put("hospitalKey", "hospital-b") }))
        assertNull(parse(reply { it.put("protocol", "other") }))
        assertNull(parse(reply { it.put("version", 2) }))
        assertNull(parse(reply(), "192.168.31.99"))
        assertNull(parse(ByteArray(2049)))
        assertNull(parse("not json".toByteArray()))
    }

    @Test fun discoveryRejectsPublicLoopbackMalformedAndSubnetBoundaryAddresses() {
        for (address in listOf("8.8.8.8", "127.0.0.1", "192.168.31.0", "192.168.31.255",
            "192.168.31.256", "192.168.031.42", "192.168.31.42.evil", "192.168.3.16")) {
            assertNull(address, parse(reply(address), address))
        }
        assertFalse(LocalServerProtocol.privateIpv4("172.15.0.1"))
        assertFalse(LocalServerProtocol.privateIpv4("172.32.0.1"))
        assertTrue(LocalServerProtocol.privateIpv4("172.31.255.254"))
        assertFalse(LocalServerProtocol.sameSubnet("10.42.1.20", "10.42.200.9", 0))
    }

    @Test fun transportRewritePreservesOldLogicalOriginAndEncodedResourcePath() {
        val old = URL("http://192.168.3.16:33101/api/patient/assets/7?name=a%2Fb&v=4")
        val actual = LocalServerProtocol.physicalUrl(old, LocalServerCandidate("192.168.31.42"))
        assertEquals("https://192.168.31.42:3001/api/patient/assets/7?name=a%2Fb&v=4", actual.toString())
        assertEquals("http://192.168.3.16:33101", LocalServerProtocol.origin(old))
        assertEquals("http://spineflow.local:3001", LocalServerProtocol.normalizeOrigin("http://spineflow.local:3001"))
        assertThrows(IllegalArgumentException::class.java) {
            LocalServerProtocol.physicalUrl(URL("http://user:pass@192.168.3.16:33101/api"), LocalServerCandidate("192.168.31.42"))
        }
        assertThrows(IllegalArgumentException::class.java) {
            LocalServerProtocol.normalizeOrigin("http://192.168.3.16:3001/?other=1")
        }
    }

    @Test fun certificatePinAndHospitalIdentityAreBothRequired() {
        val certificate = "public certificate bytes".toByteArray()
        val pinned = trust.copy(certificateSha256 = LocalServerProtocol.sha256(certificate))
        assertTrue(pinned.acceptsCertificate(certificate))
        assertFalse(pinned.acceptsCertificate("another certificate".toByteArray()))
        val identity = ServiceIdentity("hospital", "hospital-a", "hospital-a", "A", "test", 1)
        assertTrue(pinned.matches(identity))
        assertFalse(pinned.matches(identity.copy(role = "central")))
        assertFalse(pinned.matches(identity.copy(hospitalKey = "hospital-b")))
        assertThrows(IllegalArgumentException::class.java) { trust.copy(certificateSha256 = "") }
    }

    @Test fun discoveryRequestContainsOnlyProtocolAndFreshNonce() {
        val request = JSONObject(LocalServerProtocol.request(nonce).toString(Charsets.UTF_8))
        assertEquals(setOf("protocol", "version", "nonce"), request.keys().asSequence().toSet())
        assertThrows(IllegalArgumentException::class.java) { LocalServerProtocol.request("short") }
    }
}
