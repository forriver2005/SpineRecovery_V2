package com.metaspine.mobile.data

import org.json.JSONObject
import java.net.URLEncoder
import com.metaspine.mobile.unified.BuildConfig

object SpineFlowServerConfig {
    // Old local origins remain scope aliases; all builds use pinned LAN discovery.
    @Volatile
    var baseUrlOverride: String? = null

    val BASE_URL: String
        get() = ServerOriginPolicy.normalize(
            baseUrlOverride?.takeIf { it.isNotBlank() } ?: BuildConfig.SERVER_BASE_URL
        ) ?: error("仅支持医院局域网服务器地址")
}

data class VersionInfo(
    val type: String,
    val versionCode: Int,
    val downloadUrl: String,
    val sha256: String = "", // SF-18：发布清单完整性哈希
    val fileSize: Long? = null
)

object VersionCheckClient {
    private const val CHECK_PATH = "/api/version/check"

    suspend fun checkUpdate(type: String, currentVersionCode: Int): Result<VersionInfo?> = patientRequestResult {
        val encodedType = URLEncoder.encode(type, Charsets.UTF_8.name())
        val response = PatientHttpTransport.shared.request(
            "$CHECK_PATH?type=$encodedType&version_code=$currentVersionCode",
            connectTimeoutMs = 8_000, readTimeoutMs = 10_000
        )
        response.requireSuccess("版本检查失败: HTTP "+response.status)
        parseVersionResponse(response.text, type)
    }

    internal fun parseVersionResponse(responseText: String, requestedType: String): VersionInfo? {
        val root = JSONObject(responseText)
        check(root.optInt("code", -1) == 0) { "版本检查响应状态无效" }
        val data = root.optJSONObject("data") ?: return null
        if (!data.optBoolean("has_update", false)) return null
        val latest = data.optJSONObject("latest_version")
            ?: error("更新响应缺少 latest_version")
        val type = latest.optString("type", requestedType)
        val versionCode = latest.optInt("version_code", -1)
        val downloadUrl = latest.optString("download_url")
        check(type == requestedType) { "更新类型不匹配: $type" }
        check(versionCode >= 0) { "更新版本号无效" }
        check(downloadUrl.isNotBlank()) { "更新下载地址为空" }
        return VersionInfo(type, versionCode, downloadUrl,
            latest.optString("sha256", ""),
            latest.optLong("file_size", -1L).takeIf { it >= 0L })
    }
}
