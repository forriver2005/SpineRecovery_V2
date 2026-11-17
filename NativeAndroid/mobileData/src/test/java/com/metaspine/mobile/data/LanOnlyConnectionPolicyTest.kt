package com.metaspine.mobile.data

import com.metaspine.mobile.unified.BuildConfig
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.net.URL

/** Runs in both build variants: neither variant can select a public transport. */
class LanOnlyConnectionPolicyTest {
    @Test fun everyVariantRequiresTheSameLogicalLanEndpointAndPinnedIdentity() {
        assertEquals("https://spineflow.local:3001", BuildConfig.SERVER_BASE_URL)
        assertTrue(BuildConfig.LOCAL_DISCOVERY_CERT_SHA256.matches(Regex("[0-9a-f]{64}")))
        assertTrue(BuildConfig.LOCAL_DISCOVERY_SERVICE_ID.isNotBlank())
        assertTrue(BuildConfig.LOCAL_DISCOVERY_HOSPITAL_KEY.isNotBlank())
        assertEquals(BuildConfig.SERVER_BASE_URL, ServerOriginPolicy.normalize(BuildConfig.SERVER_BASE_URL))
    }

    @Test fun privateIpv4AndTheLocalLogicalOriginRemainUsableAcrossAddressChanges() {
        listOf(
            "http://192.168.3.16:33101", "https://192.168.31.42:3001",
            "http://10.0.2.2:3101", "https://10.42.1.5:3001",
            "https://172.16.0.2:3001", "https://172.31.255.254:3001",
            "http://spineflow.local:3001", "https://spineflow.local:3001"
        ).forEach { origin -> assertEquals(origin, origin, ServerOriginPolicy.normalize(origin)) }
    }

    @Test fun publicDestinationsAndDnsNamesAreNeverAcceptedEvenWithHttps() {
        listOf(
            "https://api.metaspine.cn", "http://8.8.8.8:3001", "https://1.1.1.1",
            "https://172.15.255.254:3001", "https://172.32.0.1:3001",
            "https://192.169.1.2:3001", "http://100.64.0.1:3001",
            "https://hospital.example.org", "http://localhost:3001",
            "https://127.0.0.1:3001", "https://[::1]:3001", "https://[fd00::1]:3001",
            "http://spineflow.local.evil.example:3001", "http://other.local:3001",
            "https://spineflow.local", "https://spineflow.local:443"
        ).forEach { origin -> assertNull(origin, ServerOriginPolicy.normalize(origin)) }
    }

    @Test fun spoofedAddressesCredentialsAndNonOriginInputsAreRejected() {
        listOf(
            "http://192.168.1.2@8.8.8.8:3001", "http://user:pass@192.168.1.2:3001",
            "http://192.168.1.2.evil.example:3001", "http://192.168.1.2:0",
            "http://192.168.1.2:65536", "http://192.168.1.2:-1",
            "http://192.168.1.2:3001/api", "http://192.168.1.2:3001/?target=x",
            "http://192.168.1.2:3001#x", "http://192.168.001.2:3001",
            "http://0xc0a80102:3001", "http://3232235778:3001",
            "http://192.168.1.2\\@8.8.8.8:3001", "ftp://192.168.1.2:3001",
            "//192.168.1.2:3001", "192.168.1.2:3001", "", " ", "http://192.168. 1.2"
        ).forEach { origin -> assertNull(origin, ServerOriginPolicy.normalize(origin)) }
        assertNull(ServerOriginPolicy.normalize(null))
    }

    @Test fun aPublicOverrideCannotReplaceTheBuiltInLanEndpoint() {
        val before = SpineFlowServerConfig.baseUrlOverride
        try {
            SpineFlowServerConfig.baseUrlOverride = "https://api.metaspine.cn"
            assertThrows(IllegalStateException::class.java) { SpineFlowServerConfig.BASE_URL }
        } finally { SpineFlowServerConfig.baseUrlOverride = before }
    }

    @Test fun uninitializedRuntimeRejectsBothPublicAndPrivateConnectionsInsteadOfUsingDns() {
        listOf("https://api.metaspine.cn/api/service-identity",
            "https://spineflow.local:3001/api/service-identity",
            "http://192.168.1.2:3001/api/service-identity").forEach { value ->
            assertThrows(IllegalStateException::class.java) { LocalServerDiscovery.openConnection(URL(value)) }
        }
    }

    @Test fun resourceAndVersionDownloadDefaultsAlsoRequireInitializedLanDiscovery() = runBlocking {
        var published = false
        val failure = runCatching {
            PrescriptionAssetClient.download(
                patientToken = "test-token",
                downloadPath = "/api/patient/training-directive/orders/1/items/1/asset",
                expectedSha256 = "a".repeat(64), expectedFileSize = 1L,
                publish = { published = true; it })
        }.exceptionOrNull()
        assertTrue(failure is PrescriptionAssetException)
        assertFalse(published)
        assertTrue(VersionDownloader.downloadText("/api/version/download/1").isFailure)
    }

    @Test fun configDownloadCannotBeRedirectedToAPublicOrDifferentOrigin() {
        val base = "https://spineflow.local:3001"
        assertEquals("$base/api/version/download/7",
            VersionDownloader.resolveDownloadUrl(base, "/api/version/download/7").toString())
        listOf("https://api.metaspine.cn/api/version/download/7",
            "//8.8.8.8/api/version/download/7", "https://192.168.1.2:3001/api/version/download/7",
            "https://user:pass@spineflow.local:3001/api/version/download/7").forEach { location ->
            assertThrows(IllegalArgumentException::class.java) { VersionDownloader.resolveDownloadUrl(base, location) }
        }
    }
}
