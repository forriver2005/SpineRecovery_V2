package com.metaspine.mobile.data

import kotlinx.coroutines.runBlocking
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import java.io.File

/** Loopback fixture adapter preserves business assertions without enabling direct transport in the app. */
class MinimalResultHttpTest {
    @Test fun unityProductionProjectionRevisionsReachKotlinAndHospital() = runBlocking {
        val path = System.getenv("SPINEFLOW_BRIDGE_FIXTURE")
        assumeTrue(!path.isNullOrBlank() && File(path!!).isFile)
        val root = File(path!!).parentFile
        val files = listOf("initial", "coach", "result").map { File(root, "stage3-unity-network-$it.json") }
        assumeTrue(files.all { it.isFile })
        val fixture = JSONObject(File(path).readText())
        val origin = fixture.getString("baseUrl")
        val api = FixturePatientApi(origin)
        require(origin.startsWith("http://127.0.0.1:"))
        val row = fixture.getJSONObject("healthCase")
        val patient = row.getString("patientId")
        val token = row.getJSONObject("credential").getString("patientToken")
        api.sync(row.getJSONObject("syncBody"), token).getOrThrow()
        var previous: ResultRecord? = null
        for (file in files) {
            val projection = JSONObject(file.readText())
            val result = PrescriptionResultV2.parse(projection, patient)
            val frozen = result.freeze(patient)
            previous = ResultRevisionPolicy.next("isolated-unity-probe", result, frozen, previous)
            assertEquals(CanonicalJson.canonicalize(projection), CanonicalJson.canonicalize(JSONObject(frozen.body)))
            val ack = api.uploadV2(JSONArray().put(JSONObject(frozen.body)),
                listOf(frozen.receipt), token).getOrThrow()
            assertEquals(listOf(frozen.receipt), ack)
        }
        assertEquals(3L, previous!!.resultRevision)
        // Repeating the terminal snapshot must keep the original revision and hash.
        val latest = previous!!
        assertEquals(listOf(latest.receipt), api.uploadV2(
            JSONArray().put(JSONObject(latest.body)), listOf(latest.receipt), token).getOrThrow())
    }

    @Test fun frozenTwoItemV2ResultAndExactReplayReachHospital() = runBlocking {
        val path = System.getenv("SPINEFLOW_BRIDGE_FIXTURE")
        assumeTrue(!path.isNullOrBlank() && File(path!!).isFile)
        val fixture = JSONObject(File(path!!).readText())
        val origin = fixture.getString("baseUrl")
        val api = FixturePatientApi(origin)
        require(origin.startsWith("http://127.0.0.1:"))
        val row = fixture.getJSONObject("resultCase")
        val patient = row.getString("patientId")
        val token = row.getJSONObject("credential").getString("patientToken")
        api.sync(row.getJSONObject("syncBody"), token).getOrThrow()
        val result = PrescriptionResultV2.parse(row.getJSONObject("resultSnapshot"), patient)
        val frozen = result.freeze(patient)
        assertEquals(row.getJSONObject("expectedReceipt").getString("resultContentSha256"), frozen.contentSha256)
        repeat(2) {
            val replies = api.uploadV2(JSONArray().put(JSONObject(frozen.body)),
                listOf(frozen.receipt), token).getOrThrow()
            assertEquals(listOf(frozen.receipt), replies)
        }
    }

    @Test fun ackRequiresExactIntegerCounts() {
        val receipt = ResultReceipt("s", 3, "a".repeat(64))
        fun response() = JSONObject().put("code", 0).put("data", JSONObject().put("uploaded", 1)
            .put("skipped", 0).put("resultReceipts", JSONArray().put(receipt.toJson())))
        assertEquals(listOf(receipt), TrainingSyncClient.verifyUploadResponse(response(), listOf(receipt)))
        for ((field, value) in listOf("uploaded" to 2, "uploaded" to 0, "uploaded" to "1", "skipped" to 0.5, "skipped" to "0")) {
            val invalid = response()
            invalid.getJSONObject("data").put(field, value)
            assertThrows(Exception::class.java) { TrainingSyncClient.verifyUploadResponse(invalid, listOf(receipt)) }
        }
        val invalid = response().put("code", "0")
        assertThrows(Exception::class.java) { TrainingSyncClient.verifyUploadResponse(invalid, listOf(receipt)) }
    }
}
