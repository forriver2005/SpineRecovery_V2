package com.metaspine.mobile.data

import android.content.Context

class VersionManager(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    fun getConfigVersion(): Int = prefs.getInt(KEY_CONFIG_VERSION, 0)

    fun setConfigVersion(versionCode: Int) {
        prefs.edit().putInt(KEY_CONFIG_VERSION, versionCode).apply()
    }

    private companion object {
        const val PREFS_NAME = "spineflow_versions"
        const val KEY_CONFIG_VERSION = "config_version_code"
    }
}
