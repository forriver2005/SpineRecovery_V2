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
}
