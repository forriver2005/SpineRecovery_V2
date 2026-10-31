package com.metaspine.mobile.data

import com.sun.net.httpserver.HttpServer
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.net.InetSocketAddress
import java.net.URL
import java.util.concurrent.atomic.AtomicInteger

class LocalServerCompatibilityTest {
    private fun binding(origin: String) = BindingScope(
        origin, "hospital-a", "hospital-a", "Hospital A", "test-patient", "test-account", 7L)

    @Test fun physicalAddressChangesPreserveLegacyScopeAndQuestionnaireFingerprint() {
        val legacy = binding("http://192.168.3.16:33101")
        val credential = PatientServerCredential(legacy.patientId, "test-token")
        val stored = legacy.toJson().toString()
        val storageKey = legacy.storageKey()
        val fingerprint = RoutineScope.of(legacy.accountId, credential, legacy.origin, legacy).fingerprint
        val request = URL(legacy.origin + "/api/patient/training-directive")
        val trust = LocalServerTrust("a".repeat(64), legacy.serviceId, legacy.hospitalKey)

        assertTrue(trust.matches(legacy))
        assertEquals("https://192.168.31.42:3001/api/patient/training-directive",
            LocalServerProtocol.physicalUrl(request, LocalServerCandidate("192.168.31.42")).toString())
        assertEquals("https://10.42.1.5:3001/api/patient/training-directive",
            LocalServerProtocol.physicalUrl(request, LocalServerCandidate("10.42.1.5")).toString())
        assertEquals(stored, legacy.toJson().toString())
        assertEquals(storageKey, legacy.storageKey())
        assertEquals(7L, legacy.credentialGeneration)
        assertEquals(fingerprint, RoutineScope.of(legacy.accountId, credential, legacy.origin, legacy).fingerprint)
        assertFalse(trust.matches(legacy.copy(serviceId = "hospital-b")))
        assertFalse(trust.matches(legacy.copy(hospitalKey = "hospital-b")))
    }

    @Test fun uninitializedDiscoveryNeverTreatsAnOldBindingAsVerified() {
        val legacy = binding("http://192.168.3.16:33101")
        val before = legacy.toJson().toString()
        assertThrows(IllegalStateException::class.java) { LocalServerDiscovery.registerBinding(legacy) }
        assertThrows(IllegalStateException::class.java) {
            LocalServerDiscovery.matchesBinding(legacy, legacy.origin, legacy.patientId)
        }
        assertThrows(IllegalStateException::class.java) { LocalServerDiscovery.hasVerifiedBinding(legacy) }
        assertThrows(IllegalStateException::class.java) {
            runBlocking { LocalServerDiscovery.prepareOperation(legacy.origin) }
        }
        assertEquals(before, legacy.toJson().toString())
    }

    @Test fun applicationDefaultNeverFallsBackToDirectHttpWhenDiscoveryIsUnavailable() {
        val calls = AtomicInteger()
        val server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.createContext("/api/patient/sync") { exchange ->
            calls.incrementAndGet()
            exchange.sendResponseHeaders(200, -1)
            exchange.close()
        }
        server.start()
        try {
            assertThrows(IllegalStateException::class.java) {
                runBlocking {
                    PatientHttpTransport(origin = { "http://127.0.0.1:${server.address.port}" })
                        .request("/api/patient/sync", "POST", "test-token", "{}")
                }
            }
            assertEquals(0, calls.get())
        } finally { server.stop(0) }
    }

    @Test fun explicitlyInjectedTestTransportKeepsPostBodyAndSendsOnlyOnce() = runBlocking {
        var calls = 0
        var method = ""
        var authorization = ""
        var receivedBody = ""
        val server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.createContext("/api/patient/sync") { exchange ->
            calls++
            method = exchange.requestMethod
            authorization = exchange.requestHeaders.getFirst("Authorization").orEmpty()
            receivedBody = exchange.requestBody.use { it.readBytes().toString(Charsets.UTF_8) }
            val reply = "{\"code\":0}".toByteArray()
            exchange.sendResponseHeaders(200, reply.size.toLong())
            exchange.responseBody.use { it.write(reply) }
            exchange.close()
        }
        server.start()
        try {
            val response = PatientHttpTransport(
                origin = { "http://127.0.0.1:${server.address.port}" },
                openConnection = ::openLoopbackTestConnection)
                .request("/api/patient/sync", "POST", "test-token", "{\"requestId\":\"unchanged\"}")
            assertEquals(200, response.status)
            assertEquals(1, calls)
            assertEquals("POST", method)
            assertEquals("Bearer test-token", authorization)
            assertEquals("{\"requestId\":\"unchanged\"}", receivedBody)
        } finally {
            server.stop(0)
        }
    }
}
