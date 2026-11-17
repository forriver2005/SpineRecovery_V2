package com.metaspine.mobile.data

import org.json.JSONObject

/**
 * I01 服务身份（`GET /api/service-identity`）。
 *
 * 只有确认受信 Origin 对应 [role] == "hospital" 后才允许进入患者 API；central 不提供
 * 患者上传入口。`contractVersion` 是服务身份合同版本（当前 1），与已退役的移动训练 V1
 * 无关，不得改号。响应已由服务器加 `Cache-Control: no-store`。
 */
data class ServiceIdentity(
    val role: String,
    val serviceId: String,
    val hospitalKey: String,
    val hospitalName: String,
    val audience: String,
    val contractVersion: Int
) {
    val isHospital: Boolean get() = role == "hospital"
}

object ServiceIdentityClient {
    const val PATH = "/api/service-identity"

    suspend fun fetch(origin: String = SpineFlowServerConfig.BASE_URL): Result<ServiceIdentity> = patientRequestResult {
        val response = PatientHttpTransport(origin = { origin }).request(
            PATH, connectTimeoutMs = 8_000, readTimeoutMs = 10_000
        )
        response.requireSuccess("服务身份请求失败, HTTP " + response.status)
        parse(response.text)
    }

    internal fun parse(responseText: String): ServiceIdentity {
        val root = JSONObject(responseText)
        check(root.optInt("code", -1) == 0) { root.optString("message", "服务身份响应状态无效") }
        val data = root.getJSONObject("data")
        return ServiceIdentity(
            role = data.getString("role"),
            serviceId = data.getString("serviceId"),
            hospitalKey = data.optString("hospitalKey"),
            hospitalName = data.optString("hospitalName"),
            audience = data.optString("audience"),
            contractVersion = data.optInt("contractVersion", 1)
        )
    }

    /** 进入患者链路前的门禁：非医院角色一律拒绝。 */
    fun requireHospital(identity: ServiceIdentity): ServiceIdentity {
        check(identity.isHospital) { "当前服务身份不是医院角色，不能进入患者链路" }
        return identity
    }
}
