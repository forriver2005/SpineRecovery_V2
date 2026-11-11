package com.metaspine.mobile.data

import android.content.Context
import android.util.Log
import com.metaspine.mobile.unified.BuildConfig
import org.json.JSONObject

data class InstalledMotionPackage(
    val actionId: String,
    val displayName: String
)

object ConfigUpdateCoordinator {
    private const val TAG = "ConfigUpdate"
    private const val CONFIG_TYPE = "config"
    private val retryRunner = ConfigUpdateRetryRunner()

    suspend fun checkOnce(context: Context): InstalledMotionPackage? {
        val appContext = context.applicationContext
        val versions = VersionManager(appContext)
        return retryRunner.run {
            val currentVersion = versions.getConfigVersion()
            val checked = VersionCheckClient.checkUpdate(CONFIG_TYPE, currentVersion)
            val error = checked.exceptionOrNull()
            if (error != null) {
                Log.w(TAG, "配置版本检查失败，保留本地动作包", error)
                Result.failure(error)
            } else {
                val update = checked.getOrNull()
                if (update == null) {
                    Log.i(TAG, "配置已是最新版本，当前版本=$currentVersion")
                    Result.success(null)
                } else {
                    install(appContext, versions, update)
                }
            }
        }
    }

    private suspend fun install(
        context: Context,
        versions: VersionManager,
        update: VersionInfo
    ): Result<InstalledMotionPackage> {
        if (!BuildConfig.DEBUG &&
            (!update.sha256.matches(Regex("[0-9a-fA-F]{64}")) ||
                update.fileSize == null || update.fileSize <= 0L)) {
            Log.w(TAG, "配置发布清单缺少有效大小或 SHA-256")
            return Result.failure(IllegalStateException("配置发布清单缺少有效大小或 SHA-256"))
        }
        return VersionDownloader.downloadText(update.downloadUrl, update.fileSize)
        .mapCatching { json ->
            // SF-18（安装侧校验）：结构非法 / 哈希与发布清单不一致 → 整体失败，
            // 不落盘、不推进版本，保留上一有效包与版本。
            structuralError(json)?.let { problem ->
                throw IllegalStateException("CoachMotionPackage 结构非法: $problem")
            }
            if (update.sha256.isNotBlank()) {
                check(sha256Hex(json).equals(update.sha256, ignoreCase = true)) {
                    "动作包 SHA-256 与发布清单不一致"
                }
            }
            val motionPackage = parseMotionPackage(json)
            check(MotionPackageStore.save(context, motionPackage.actionId, json) != null) {
                "动作包保存失败"
            }
            motionPackage
        }
        .onSuccess { motionPackage ->
            versions.setConfigVersion(update.versionCode)
            Log.i(
                TAG,
                "动作包更新成功: actionId=${motionPackage.actionId}, version=${update.versionCode}"
            )
        }
        .onFailure {
            Log.w(TAG, "动作包下载或安装校验失败，保留已有本地动作包与版本")
        }
    }

    internal fun parseMotionPackage(json: String): InstalledMotionPackage {
        val root = JSONObject(json)
        val actionId = root.optString("actionId").trim()
        check(actionId.isNotEmpty()) { "CoachMotionPackage 缺少非空 actionId" }
        return InstalledMotionPackage(
            actionId = actionId,
            displayName = root.optString("displayName").trim().ifEmpty { actionId }
        )
    }

    // ---------- SF-18：安装前校验（纯函数，可 JVM 单测；与服务端 lib/validate.validateCoachPackage 同规则） ----------

    private const val MAX_DEPTH = 20
    private const val MAX_KEYS = 512
    private const val MAX_LIST = 500
    private const val MAX_TEXT = 2048

    internal fun sha256Hex(text: String): String {
        val digest = java.security.MessageDigest.getInstance("SHA-256")
            .digest(text.toByteArray(Charsets.UTF_8))
        return digest.joinToString("") { String.format("%02x", it) }
    }

    private fun shapeError(value: Any?, path: String, depth: Int): String? {
        if (depth > MAX_DEPTH) return "$path 嵌套过深"
        if (value == null) return null
        return when (value) {
            is String -> if (value.length > MAX_TEXT) "$path 文本超长" else null
            // JSON 解析产物只可能是有限数值（Double/Int/Long），无需再校验 NaN/Infinity
            is Number -> null
            is Boolean -> null
            is org.json.JSONArray -> {
                if (value.length() > MAX_LIST) "$path 数组超长"
                else (0 until value.length()).firstNotNullOfOrNull { i ->
                    shapeError(value.opt(i), "$path[$i]", depth + 1)
                }
            }
            is org.json.JSONObject -> {
                if (value.length() > MAX_KEYS) "$path 键数超限"
                else value.keys().asSequence().firstNotNullOfOrNull { k ->
                    shapeError(value.opt(k), if (path.isEmpty()) k else "$path.$k", depth + 1)
                }
            }
            else -> "$path 不支持的类型"
        }
    }

    /** 返回 null = 结构合法；否则返回第一处问题描述。 */
    internal fun structuralError(json: String): String? {
        val root = runCatching { JSONObject(json) }.getOrElse { return "必须是合法 JSON 对象" }
        val actionId = root.optString("actionId").trim()
        if (actionId.isEmpty()) return "actionId 必须非空"
        if (actionId.length > 128) return "actionId 超长"
        val displayName = root.optString("displayName").trim()
        if (displayName.isNotEmpty() && displayName.length > 64) return "displayName 超长"
        return shapeError(root, "", 0)
    }
}
