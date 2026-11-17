package com.metaspine.mobile.data

import org.json.JSONObject

enum class PatientAccountStatus { Active, Missing, Rejected, Unavailable }

/** Only the dedicated, identity-matched 410 contract authorizes local deletion. */
object PatientAccountClient {
    const val PATH = "/api/patient/account-status"

    suspend fun check(
        credential: PatientServerCredential,
        accountId: String,
        installationId: String,
        origin: String = SpineFlowServerConfig.BASE_URL
    ): PatientAccountStatus = patientRequestResult {
        val body = JSONObject().put("patientId", credential.patientId)
            .put("accountId", accountId).put("installationId", installationId)
        val response = PatientHttpTransport(origin = { origin }).request(
            PATH, "POST", credential.patientToken, body.toString(), readErrorStatuses = setOf(410)
        )
        parse(response.status, response.text, credential.patientId)
    }.getOrDefault(PatientAccountStatus.Unavailable)

    internal fun parse(httpStatus: Int, body: String, expectedPatientId: String): PatientAccountStatus {
        if (httpStatus == 401 || httpStatus == 403) return PatientAccountStatus.Rejected
        val response = runCatching { JSONObject(body) }.getOrNull() ?: return PatientAccountStatus.Unavailable
        val data = response.optJSONObject("data") ?: return PatientAccountStatus.Unavailable
        if (expectedPatientId.isBlank() || data.opt("patientId") != expectedPatientId) {
            return PatientAccountStatus.Unavailable
        }
        return when {
            httpStatus == 200 && response.opt("code") == 0 && data.opt("status") == "active" ->
                PatientAccountStatus.Active
            httpStatus == 410 && response.opt("code") == 410 && data.opt("status") == "missing" ->
                PatientAccountStatus.Missing
            else -> PatientAccountStatus.Unavailable
        }
    }
}
