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
        assertEquals(setOf("dead_bug", "bird_dog", "hip_thrust", "side_bridge", "side_bridge_right",
            "cat_camel", "curl_up", "hip_thrust_taiko", "hip_thrust_hold30", "side_bridge_hold30", "side_bridge_right_hold30"),
            BuiltInExercise.entries.map { it.key }.toSet())
        assertNull(BuiltInExercise.forKey("8"))
        assertNull(BuiltInExercise.forKey("unknown_exercise"))
        assertEquals(BuiltInExercise.BIRD_DOG, BuiltInExercise.forLegacy("8", "鸟狗"))
        assertNull(BuiltInExercise.forLegacy("8", "臀部拉伸"))
        assertNull(BuiltInExercise.forLegacy("5", "臀桥"))
        assertNull(BuiltInExercise.forLegacy("9", "鸟狗"))
    }

    @Test fun completeLibraryRoundTripsDoseRoutesAndUnavailableResults() {
        for (exercise in BuiltInExercise.entries) for (hold in listOf(0, 7, 600)) {
            val json = JSONObject(orderJson)
            json.getJSONArray("items").getJSONObject(0).put("action", exercise.key)
                .put("sets", 2).put("reps", 3).put("holdSeconds", hold).put("level", "hard")
            val parsed = AdmissionPrescriptionV2.fromJson(json)
            val order = parsed.copy(resourceManifestSha256 = parsed.computeManifestSha256())
            assertEquals(order, AdmissionPrescriptionV2.fromJson(order.toJson()))
            val actions = UnifiedPrescriptionV2.actionsFor(order)
            assertEquals(exercise.coach, actions.getJSONObject(0).getString("sceneName"))
            assertEquals(exercise.game, actions.getJSONObject(1).getString("sceneName"))
            for (index in 0..1) {
                val action = actions.getJSONObject(index)
                assertEquals(2, action.getInt("sets")); assertEquals(3, action.getInt("reps"))
                assertEquals(hold, action.getInt("holdSeconds")); assertEquals("hard", action.getString("level"))
            }
            val modes = ResultMode.ORDER.map { ResultModeResult(it, ResultSessionStatus.NOT_STARTED, 0, 0, null, ResultModeSource.NotStarted) }
            val result = PrescriptionResultV2("library_${exercise.key}","request1","patient1","2026-11-24T01:02:03.000Z",
                false,ResultSessionStatus.INTERRUPTED,0,19,order.resourceManifestSha256,1,ResultSnapshotSource.MobileNoDetails,
                listOf(ResultItem(7,0,exercise.key,null,null,null,ResultSessionStatus.NOT_STARTED,modes)),null)
            assertEquals(result, PrescriptionResultV2.parse(result.toJson(), "patient1"))
        }
    }

    @Test fun unsupportedFirstPrescriptionStillBindsPatient() {
        val response = JSONObject("""{"patientId":"pt_test","identity":{"displayName":"测试","idCardMasked":"***"},
            "prescription":null,"trainingState":"unsupported_prescription"}""")
        val binding = PatientActivationClient.parseRedeem(response)
        assertEquals("pt_test", binding.patientId)
        assertNull(binding.prescription)
    }

    @Test fun mixedActionResultKeepsEarlierItemsAndNeverTurnsMissingSensorsIntoScores() {
        for (batch in BuiltInExercise.entries.chunked(10)) {
            val items = batch.mapIndexed { index, exercise -> AdmissionPrescriptionItemV2(
                100L+index,index,exercise.key,exercise.names.first(),2,3,7,level="hard") }
            val parsed = AdmissionPrescriptionV2(19,2,"",items)
            val order = parsed.copy(resourceManifestSha256=parsed.computeManifestSha256())
            val plan = JSONObject().put("prescriptionContractVersion",2).put("prescriptionJson",order.toJson().toString())
                .put("startedAt","2026-11-24T01:02:03.000Z")
            val measurements = org.json.JSONArray()
            for (item in items) for (mode in if(item.position < items.lastIndex) listOf("coach","game") else listOf("coach")) measurements.put(JSONObject()
                .put("prescriptionItemId",item.itemId).put("trainingMode",mode)
                .put("prescriptionModeCompleted",item.position < items.lastIndex)
                .put("completedRepetitions",if(item.position < items.lastIndex) 6 else 0).put("durationSeconds",12)
                .put("scoreAvailable",false).put("scoreSampleCount",0).put("overallScore",0))
            val record = TrainingHistoryRecord("mixed","2026-11-24T01:02:04.000Z","",null,planJson=plan.toString())
            val wire = UnifiedPrescriptionV2.result(record,JSONObject().put("userId","patient1").put("actionResults",measurements))!!
            val result = PrescriptionResultV2.parse(wire,"patient1")
            assertFalse(result.completed)
            val rows = wire.getJSONArray("itemResults")
            assertEquals(items.size,rows.length())
            for (index in items.indices) {
                val row=rows.getJSONObject(index)
                assertEquals(items[index].action,row.getString("action"))
                assertEquals(items[index].itemId,row.getLong("itemId"))
                val modes=row.getJSONArray("modeResults")
                for (mode in 0..1) assertTrue(modes.getJSONObject(mode).isNull("score"))
            }
            // A played-through demonstration with no measured repetitions stays interrupted.
            val noInputRows=org.json.JSONArray().put(JSONObject(measurements.getJSONObject(0).toString())
                .put("prescriptionModeCompleted",true).put("completedRepetitions",0))
            val noInput = UnifiedPrescriptionV2.result(record,JSONObject().put("userId","patient1").put("actionResults",noInputRows))!!
            assertFalse(noInput.getBoolean("completed"))
            assertEquals(ResultSessionStatus.INTERRUPTED.wireValue,noInput.getJSONArray("itemResults").getJSONObject(0).getString("status"))
        }
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
