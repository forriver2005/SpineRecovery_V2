package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class HealthEventBridgeTest {
    private fun user(events: JSONArray = JSONArray()) = DemoUserData("local", UserLifecycle.ReturningUser,
        null, null, latestPreTrainingCheck = null, latestPainPoints = emptyList(),
        imuCalibrationState = ImuCalibrationState.NotCalibrated, history = emptyList(), healthEventsJson = events.toString())
    private fun event(id: String, score: Int) = JSONObject().put("eventId", id)
        .put("type", "preTrainingQuestionnaire").put("recordedAt", "2026-10-23T00:00:00.000Z")
        .put("answersJson", JSONObject().put("prePainLevel", score).put("allowedToTrain", false).toString())

    @Test fun separatePreEventsRetainOriginalAnswersAndMissingFields() {
        val first = event("pre:first", 3)
        val all = HealthEventBridge.merge(JSONArray().put(first).toString(), JSONArray().put(event("pre:second", 7)))
        val batches = HealthEventBridge.batches(user(JSONArray(all)), PatientSyncClient.SyncSnapshot(), "patient")
        val pre = batches.mapNotNull { it.getJSONObject("payload").optJSONObject("preTrainingQuestionnaire") }
        assertEquals(listOf(3, 7), pre.map { it.getInt("prePainLevel") })
        assertEquals(listOf("pre:first", "pre:second"), pre.map { it.getString("eventId") })
        assertTrue(pre.all { !it.has("symptomChange") && !it.has("painLocationChanged") && !it.getBoolean("allowedToTrain") })
    }

    @Test(expected = IllegalArgumentException::class) fun sameEventCannotChangeAnswers() {
        HealthEventBridge.merge(JSONArray().put(event("pre:first", 3)).toString(), JSONArray().put(event("pre:first", 7)))
    }

    @Test fun confirmedFirstDoesNotConfirmSecond() {
        val state = user(JSONArray().put(event("pre:first", 0)).put(event("pre:second", 0)))
        val first = HealthEventBridge.batches(state, PatientSyncClient.SyncSnapshot(), "patient")
            .first { it.getString("id") == "sync:event:pre:first" }
        val cursor = QueueCursor.fromJson(first.getJSONObject("cursor"))
        val next = HealthEventBridge.batches(state, PatientSyncClient.SyncSnapshot(questionnaireHashes = cursor.qHashes), "patient")
        assertFalse(next.any { it.getString("id") == "sync:event:pre:first" })
        assertTrue(next.any { it.getString("id") == "sync:event:pre:second" })
    }

    @Test fun historicalPainDoesNotUseLatestPointsAndLargeHistorySplits() {
        val records = (0..500).map { index -> PainHistoryRecord("p$index", "2026-10-23T00:00:00Z", index % 8,
            PainRecordSource.DailyQuestionnaire, listOf(PainPoint("back", "背部", "", index % 8))) }
        val state = user().copy(painHistory = records, latestPainPoints = listOf(PainPoint("back", "背部", "front", 10)))
        val batches = HealthEventBridge.batches(state, PatientSyncClient.SyncSnapshot(), "patient")
        val pain = batches.mapNotNull { it.getJSONObject("payload").optJSONArray("painPoints") }
        assertEquals(501, pain.sumOf { it.length() })
        assertEquals(3, pain[3].getJSONObject(0).getInt("painLevel"))
        assertEquals(7, pain[7].getJSONObject(0).getInt("painLevel"))
        assertTrue(pain.all { it.length() <= 500 && !it.getJSONObject(0).has("view") })
        assertTrue(batches.all { it.getJSONObject("payload").toString().toByteArray().size <= 200 * 1024 })
    }

    @Test fun noCapturedPointsDoesNotInventZeroPainRecords() {
        val state = user().copy(painHistory = listOf(PainHistoryRecord("p", "2026-10-23T00:00:00Z", 0,
            PainRecordSource.DailyQuestionnaire, emptyList())))
        assertFalse(HealthEventBridge.batches(state, PatientSyncClient.SyncSnapshot(), "patient")
            .any { it.getJSONObject("payload").has("painPoints") })
    }

    @Test fun syncReplayZeroCountsIsConfirmedOnlyForOriginalPatient() {
        val response = JSONObject("""{"code":0,"data":{"userId":"p","synced":{"profile":0,"questionnaires":0,"painRecords":0,"lifecycle":0}}}""")
        PatientSyncClient.verifyResponse(response, "p")
        assertThrows(IllegalStateException::class.java) { PatientSyncClient.verifyResponse(response, "other") }
    }

    @Test fun bindingKeySeparatesHospitalAndGenerationButNotDisplayName() {
        val a = BindingScope("http://a", "a", "a", "医院A", "p", "u", 1)
        assertEquals(a.storageKey(), a.copy(hospitalName = "新名称").storageKey())
        assertNotEquals(a.storageKey(), a.copy(origin = "http://b", serviceId = "b").storageKey())
        assertNotEquals(a.storageKey(), a.copy(credentialGeneration = 2).storageKey())
    }

    @Test fun rebindingHoldsOldFactsButAllowsNewFacts() {
        val first = user(JSONArray().put(event("pre:old", 3))).copy(syncBindingKey = "hospital-a")
        val rebound = HealthEventBridge.rebind(first, "hospital-b")
        assertEquals(0, JSONArray(HealthEventBridge.forCurrentBinding(rebound).healthEventsJson).length())
        val edited = rebound.copy(healthEventsJson = HealthEventBridge.merge(rebound.healthEventsJson,
            JSONArray().put(event("pre:new", 7))))
        val current = JSONArray(HealthEventBridge.forCurrentBinding(edited).healthEventsJson)
        assertEquals(1, current.length())
        assertEquals("pre:new", current.getJSONObject(0).getString("eventId"))
        assertEquals(2, JSONArray(edited.healthEventsJson).length()) // old local facts are retained
    }

    @Test fun profileBackToEarlierValuesHasANewEventAndZeroPlaceholdersAreNotUploaded() {
        val a = UserProfile(170, 65, UserGender.Male, 35)
        val b = a.copy(weightKg = 66)
        assertEquals("[]", HealthEventBridge.captureProfile("[]", null, UserProfile(0, 0)))
        var events = HealthEventBridge.captureProfile("[]", null, a)
        events = HealthEventBridge.captureProfile(events, a, b)
        events = HealthEventBridge.captureProfile(events, b, a)
        val array = JSONArray(events)
        assertEquals(3, array.length())
        assertEquals(3, (0 until array.length()).map { array.getJSONObject(it).getString("eventId") }.distinct().size)
        assertEquals(array.getJSONObject(0).getString("answersJson"), array.getJSONObject(2).getString("answersJson"))
        val requests = HealthEventBridge.batches(user(array), PatientSyncClient.SyncSnapshot(), "p")
            .mapNotNull { it.getJSONObject("payload").optJSONObject("profile") }
        assertEquals(listOf(65, 66, 65), requests.map { it.getInt("weightKg") })
    }
}
