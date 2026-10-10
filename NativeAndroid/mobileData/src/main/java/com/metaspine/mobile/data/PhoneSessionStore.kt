package com.metaspine.mobile.data

import android.content.Context

class PhoneSessionStore(context: Context) {
    private val preferences = context.getSharedPreferences("spineflow_session", Context.MODE_PRIVATE)

    fun saveTrainingStart(json: String) {
        preferences.edit()
            .putString(KEY_START_JSON, json)
            .remove(KEY_RESULT_JSON)
            .commit()
    }

    fun saveTrainingResult(json: String) {
        preferences.edit().putString(KEY_RESULT_JSON, json).commit()
    }

    fun latestTrainingStartJson(): String? = preferences.getString(KEY_START_JSON, null)

    fun latestTrainingResultJson(): String? = preferences.getString(KEY_RESULT_JSON, null)

    fun clearTemporaryTrainingData() {
        check(preferences.edit()
            .remove(KEY_START_JSON)
            .remove(KEY_RESULT_JSON)
            .commit()) { "手机训练会话未能清理" }
    }

    companion object {
        private const val KEY_START_JSON = "latest_training_start_json"
        private const val KEY_RESULT_JSON = "latest_training_result_json"
    }
}
