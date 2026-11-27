package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject

data class ActionTrainingFeedback(
    val actionIndex: Int,
    val actionId: String,
    val actionName: String,
    val prescriptionItemId: Long,
    val recordedAt: String,
    val feeling: String,
    val difficulty: String,
) {
    fun eventId(sessionId: String) = "post:$sessionId:action:$actionIndex"

    fun toJson(): JSONObject = JSONObject()
        .put("actionIndex", actionIndex).put("actionId", actionId).put("actionName", actionName)
        .put("prescriptionItemId", prescriptionItemId).put("recordedAt", recordedAt)
        .put("feeling", feeling).put("difficulty", difficulty)

    companion object {
        fun fromJson(array: JSONArray?): List<ActionTrainingFeedback> = buildList {
            if (array == null) return@buildList
            for (index in 0 until array.length()) {
                val row = array.optJSONObject(index) ?: continue
                val position = row.optInt("actionIndex", -1)
                if (position < 0 || row.optString("actionId").isBlank() || row.optString("recordedAt").isBlank()) continue
                add(ActionTrainingFeedback(position, row.optString("actionId"), row.optString("actionName"),
                    row.optLong("prescriptionItemId"), row.optString("recordedAt"), row.optString("feeling"), row.optString("difficulty")))
            }
        }.distinctBy { it.actionIndex }
    }
}

fun List<ActionTrainingFeedback>.toFeedbackJson(): JSONArray = JSONArray().also { array -> forEach { array.put(it.toJson()) } }
