package com.metaspine.mobile.data

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class BuiltInPrescriptionTest {
    private val orderJson = """{"orderId":19,"prescriptionContractVersion":2,
        "resourceManifestSha256":"c81d2e4f80d5ca2164b085390bec2ad9ab3c48b6a83acd5daf05de775ea503c2",
        "items":[{"itemId":7,"position":0,"action":"bird_dog","sets":1,"reps":4,"holdSeconds":0}]}"""

    @Test fun prescriptionNeedsOnlyTheActionStringAndDose() {
        val order = AdmissionPrescriptionV2.fromDirectiveJson(JSONObject(orderJson))
        assertEquals(order.resourceManifestSha256, order.computeManifestSha256())
        assertEquals("BirdDogPractice", BuiltInExercise.forKey(order.items.single().action)?.coach)
        val saved = order.toJson().getJSONArray("items").getJSONObject(0)
        assertFalse(saved.has("actionId"))
        assertFalse(saved.has("asset"))
        assertEquals(order, AdmissionPrescriptionV2.fromJson(order.toJson()))
        assertEquals(order, AdmissionPrescriptionV2.fromActivationJson(JSONObject(orderJson.replace("itemId", "id"))))
    }

    @Test fun onlyCompleteBuiltInRoutesAreSelectable() {
        assertEquals(setOf("dead_bug", "bird_dog", "hip_thrust", "side_bridge"), BuiltInExercise.entries.map { it.key }.toSet())
        assertNull(BuiltInExercise.forKey("8"))
        assertNull(BuiltInExercise.forKey("cat_camel"))
        assertEquals(BuiltInExercise.BIRD_DOG, BuiltInExercise.forLegacy("8", "鸟狗"))
        assertNull(BuiltInExercise.forLegacy("8", "臀部拉伸"))
        assertNull(BuiltInExercise.forLegacy("5", "臀桥"))
        assertNull(BuiltInExercise.forLegacy("9", "鸟狗"))
    }

    @Test fun unsupportedFirstPrescriptionStillBindsPatient() {
        val response = JSONObject("""{"patientId":"pt_test","identity":{"displayName":"测试","idCardMasked":"***"},
            "prescription":null,"trainingState":"unsupported_prescription"}""")
        val binding = PatientActivationClient.parseRedeem(response)
        assertEquals("pt_test", binding.patientId)
        assertNull(binding.prescription)
    }

    @Test fun builtInDirectiveKeepsReceiptAndResultIdentityWithoutDownloadingAFile() {
        val json = JSONObject(orderJson)
        val digest = "ad0f5f9a8326e37ed9aee1d815b189d7bb0473473c283b241c4c51a96cc77a92"
        json.put("resourceManifestSha256", digest)
        json.getJSONArray("items").getJSONObject(0).put("asset", JSONObject("""
            {"actionVersionId":3,"variantKey":"single","formatVersion":1,"runtimeContractVersion":1,
             "doseProfile":"sequence-coach-game-v1","supportedModes":["coach","game"],"fileSize":42,
             "sha256":"${"e".repeat(64)}"}
        """))
        val order = AdmissionPrescriptionV2.fromDirectiveJson(json)
        assertEquals(digest, order.computeManifestSha256())
        val frozen = order.toJson().getJSONArray("items").getJSONObject(0)
        assertEquals("bird_dog", frozen.getString("action"))
        assertFalse(frozen.has("actionId"))
        assertFalse(frozen.getJSONObject("asset").has("downloadPath"))
        // Shared server vector: optional difficulty is appended after dose in the manifest row.
        val hard = order.copy(items = order.items.map { it.copy(level = "hard") })
        assertEquals("47d7bbefa5350ef867e6eb21a61c4f725038b2eb29d8eceb78337fea7dbfe049", hard.computeManifestSha256())
        assertEquals(order, AdmissionPrescriptionV2.fromJson(order.toJson()))
    }

    @Test fun compactResultKeepsItemIdentityWithoutAFileVersion() {
        val modes = ResultMode.ORDER.map { ResultModeResult(it, ResultSessionStatus.NOT_STARTED, 0, 0, null, ResultModeSource.NotStarted) }
        val order = AdmissionPrescriptionV2.fromDirectiveJson(JSONObject(orderJson))
        val result = PrescriptionResultV2("session1","request1","patient1","2026-11-18T01:02:03.000Z",
            false,ResultSessionStatus.INTERRUPTED,0,19,order.resourceManifestSha256,1,ResultSnapshotSource.MobileNoDetails,
            listOf(ResultItem(7,0,"bird_dog",null,null,null,ResultSessionStatus.NOT_STARTED,modes)),null)
        val entry = result.toJson().getJSONArray("itemResults").getJSONObject(0)
        assertEquals("bird_dog",entry.getString("action"))
        assertFalse(entry.has("actionId"))
        assertFalse(entry.has("sha256"))
        assertEquals(result, PrescriptionResultV2.parse(result.toJson(), "patient1"))
    }

    @Test fun prescribedDifficultySurvivesStorageAndBothModes() {
        val json = JSONObject(orderJson)
        val first = json.getJSONArray("items").getJSONObject(0).put("level", "hard")
        json.getJSONArray("items").put(JSONObject(first.toString())
            .put("itemId", 8).put("position", 1).put("action", "dead_bug").put("level", "simple"))
        val order = AdmissionPrescriptionV2.fromJson(json)
        assertEquals(listOf("hard", "simple"), order.items.map { it.level })
        assertEquals(order, AdmissionPrescriptionV2.fromJson(order.toJson()))
        val actions = UnifiedPrescriptionV2.actionsFor(order)
        assertEquals(listOf("hard", "hard", "simple", "simple"),
            (0 until actions.length()).map { actions.getJSONObject(it).getString("level") })
        val changed = order.copy(items = order.items.map { it.copy(level = "standard") })
        assertNotEquals(order.computeManifestSha256(), changed.computeManifestSha256())
    }

    @Test fun unknownDifficultyFailsClosedAndLegacyKeepsStandard() {
        val json = JSONObject(orderJson)
        json.getJSONArray("items").getJSONObject(0).put("level", "expert")
        assertThrows(IllegalArgumentException::class.java) { AdmissionPrescriptionV2.fromJson(json) }
        val old = AdmissionPrescriptionV2.fromJson(JSONObject(orderJson))
        assertEquals("standard", UnifiedPrescriptionV2.actionsFor(old).getJSONObject(0).getString("level"))
        assertEquals(old.resourceManifestSha256, old.computeManifestSha256())
    }
}
