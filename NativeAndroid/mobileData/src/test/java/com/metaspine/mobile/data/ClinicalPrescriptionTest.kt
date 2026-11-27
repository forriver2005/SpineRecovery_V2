package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class ClinicalPrescriptionTest {
    private fun order(action: String = "side_bridge", hold: Int = 0): AdmissionPrescriptionV2 {
        val asset = AdmissionPrescriptionAsset(31, "single", 1, 1, "sequence-coach-game-v1",
            listOf("coach", "game"), 100, "a".repeat(64))
        val order = AdmissionPrescriptionV2(19, 2, "", listOf(
            AdmissionPrescriptionItemV2(7, 0, action, action, 2, 3, hold,
                legacyId = action, asset = asset, level = "hard")))
        return order.copy(resourceManifestSha256 = order.computeManifestSha256())
    }

    @Test fun sideBridgeRunsBothCoachesThenAlternatingGameSets() {
        for (sets in listOf(1, 2, 20)) {
            val order = order().let { it.copy(items = listOf(it.items.single().copy(sets = sets))) }
            val actions = UnifiedPrescriptionV2.actionsFor(order)
            assertEquals(2 + sets * 2, actions.length())
            for (index in 0 until actions.length()) {
                val step = actions.getJSONObject(index)
                assertEquals(7L, step.getLong("prescriptionItemId"))
                assertEquals("侧桥", step.getString("actionName"))
                assertEquals(1, step.getInt("sets"))
                assertEquals(if (index < 2) 2 else 3, step.getInt("reps"))
                assertEquals(if (index < 2) "coach_training" else "game_training", step.getString("arTaskType"))
                assertEquals(if (index < 2) (if (index == 0) "SideBridgePractice" else "SideBridgeOppositePractice")
                    else (if (index % 2 == 0) "SideBridge_RingBridge_Dynamic" else "SideBridgeRight_RingBridge_Dynamic"), step.getString("sceneName"))
            }
        }
        for (key in listOf("dead_bug", "bird_dog", "hip_thrust", "curl_up", "cat_camel")) {
            val steps = UnifiedPrescriptionV2.actionsFor(order(key))
            assertEquals(2, steps.length())
            assertEquals(4, steps.getJSONObject(0).getInt("reps"))
            assertEquals(2, steps.getJSONObject(1).getInt("sets"))
        }
    }

    @Test fun gameVariantsAndHoldDurationsSelectTheCorrectScenes() {
        assertEquals("HipThrust_TaikoNight_Dynamic", UnifiedPrescriptionV2.actionsFor(order("hip_thrust_taiko")).getJSONObject(1).getString("sceneName"))
        for (seconds in listOf(1, 30, 45, 600)) {
            val bridge = UnifiedPrescriptionV2.actionsFor(order("hip_thrust_taiko", seconds))
            assertEquals("HipThrust_RingBridge_Hold30", bridge.getJSONObject(1).getString("sceneName"))
            assertEquals(seconds, bridge.getJSONObject(1).getInt("holdSeconds"))
            val side = UnifiedPrescriptionV2.actionsFor(order("side_bridge", seconds))
            assertEquals("SideBridge_RingBridge_Hold30", side.getJSONObject(2).getString("sceneName"))
            assertEquals("SideBridgeRight_RingBridge_Hold30", side.getJSONObject(3).getString("sceneName"))
            assertEquals(seconds, side.getJSONObject(3).getInt("holdSeconds"))
        }
    }

    @Test fun remainingGamesUseHoldsAndCurveWithIndependentRepeatedItems() {
        val scenes = mapOf("dead_bug" to "DeadBug_RingBridge_Hold30", "bird_dog" to "BirdDog_RingBridge_Hold30",
            "curl_up" to "Crunch_RingBridge_Hold30", "cat_camel" to "LyingCat_RingBridge_Hold30")
        for ((key, scene) in scenes) for (seconds in listOf(1, 30, 47, 600)) {
            val actions = UnifiedPrescriptionV2.actionsFor(order(key, seconds))
            assertEquals(scene, actions.getJSONObject(1).getString("sceneName"))
            assertEquals(seconds, actions.getJSONObject(1).getInt("holdSeconds"))
            assertEquals(0, actions.getJSONObject(0).getInt("holdSeconds"))
        }
        assertEquals("BridgeWave_StandardFollow", UnifiedPrescriptionV2.actionsFor(order("hip_thrust_curve")).getJSONObject(1).getString("sceneName"))
        assertEquals("HipThrust_RingBridge_Hold30", UnifiedPrescriptionV2.actionsFor(order("hip_thrust_curve", 47)).getJSONObject(1).getString("sceneName"))
        val repeated = order("hip_thrust_curve").let { first -> first.copy(items = listOf(
            first.items.single(), first.items.single().copy(itemId = 8, position = 1, sets = 1, reps = 2))) }
        val decoded = AdmissionPrescriptionV2.fromJson(repeated.toJson())
        val actions = UnifiedPrescriptionV2.actionsFor(decoded)
        assertEquals(4, actions.length())
        assertEquals(listOf(7L, 7L, 8L, 8L), (0 until 4).map { actions.getJSONObject(it).getLong("prescriptionItemId") })
        val plan = JSONObject().put("prescriptionContractVersion", 2).put("prescriptionJson", decoded.toJson().toString())
            .put("actions", actions).put("startedAt", "2026-11-27T01:02:03.000Z")
        val measurements = JSONArray((0 until 4).map { index -> JSONObject()
            .put("prescriptionItemId", if (index < 2) 7 else 8).put("trainingMode", if (index % 2 == 0) "coach" else "game")
            .put("prescriptionModeCompleted", true).put("completedRepetitions", if (index % 2 == 0) 4 else if (index == 1) 6 else 2)
            .put("durationSeconds", 5).put("scoreAvailable", false) })
        val result = requireNotNull(UnifiedPrescriptionV2.result(TrainingHistoryRecord("repeated", "2026-11-27T01:03:03.000Z", "", null,
            planJson = plan.toString()), JSONObject().put("userId", "patient").put("actionResults", measurements)))
        assertTrue(result.getBoolean("completed"))
        val items = result.getJSONArray("itemResults")
        assertEquals(2, items.length())
        assertEquals(6, items.getJSONObject(0).getJSONArray("modeResults").getJSONObject(1).getInt("completedReps"))
        assertEquals(2, items.getJSONObject(1).getJSONArray("modeResults").getJSONObject(1).getInt("completedReps"))
    }

    private fun snapshot(count: Int, skip: Set<Int> = emptySet()): JSONObject {
        val order = order()
        val actions = UnifiedPrescriptionV2.actionsFor(order)
        val plan = JSONObject().put("prescriptionContractVersion", 2)
            .put("prescriptionJson", order.toJson().toString()).put("actions", actions)
            .put("startedAt", "2026-11-27T01:02:03.000Z")
        val measurements = JSONArray()
        for (index in 0 until count) measurements.put(JSONObject()
            .put("prescriptionItemId", 7).put("trainingMode", if (index < 2) "coach" else "game")
            .put("prescriptionModeCompleted", index !in skip).put("prescriptionModeSkipped", index in skip)
            .put("completedRepetitions", if (index in skip) 0 else if (index < 2) 2 else 3)
            .put("durationSeconds", 5).put("scoreAvailable", false))
        val record = TrainingHistoryRecord("bilateral", "2026-11-27T01:03:03.000Z", "", null, planJson = plan.toString())
        return requireNotNull(UnifiedPrescriptionV2.result(record, JSONObject()
            .put("userId", "patient").put("actionResults", measurements)))
    }

    @Test fun bilateralResultsSumBothSidesAndRequireBothGames() {
        val leftOnly = snapshot(2)
        assertFalse(leftOnly.getBoolean("completed"))
        val complete = snapshot(6)
        val modes = complete.getJSONArray("itemResults").getJSONObject(0).getJSONArray("modeResults")
        assertTrue(complete.getBoolean("completed"))
        assertEquals(4, modes.getJSONObject(0).getInt("completedReps"))
        assertEquals(12, modes.getJSONObject(1).getInt("completedReps"))
        assertTrue(modes.getJSONObject(1).isNull("score"))
    }

    @Test fun skippedCoachesRemainSkippedWhileCompletedGamesFinishPrescription() {
        val result = snapshot(6, setOf(0, 1))
        val modes = result.getJSONArray("itemResults").getJSONObject(0).getJSONArray("modeResults")
        assertTrue(result.getBoolean("completed"))
        assertEquals("SKIPPED", modes.getJSONObject(0).getString("status"))
        assertEquals(0, modes.getJSONObject(0).getInt("completedReps"))
        assertEquals("COMPLETED", modes.getJSONObject(1).getString("status"))
        // Reuse actual Kotlin output in the backend and renderer contract tests.
        System.getenv("SPINE_RX_FIXTURE_DIR")?.let { folder ->
            java.io.File(folder).mkdirs()
            java.io.File(folder, "bilateral-skipped.json").writeText(result.toString(2))
            java.io.File(folder, "bilateral-interrupted.json").writeText(snapshot(2).toString(2))
        }
    }
}
