package com.metaspine.mobile.data

import org.json.JSONObject
import java.security.MessageDigest
import java.util.Locale
import java.util.UUID

data class RoutineScope(val accountId: String, val patientId: String, val fingerprint: String, val origin: String = "") {
    companion object {
        fun of(accountId: String, credential: PatientServerCredential, origin: String, binding: BindingScope? = null): RoutineScope {
            val normalizedAccount = accountId.trim().lowercase(Locale.ROOT)
            if (binding != null) require(binding.origin == origin && binding.patientId == credential.patientId &&
                binding.accountId.trim().lowercase(Locale.ROOT) == normalizedAccount)
            val material = origin + "\n" + credential.patientToken + (binding?.let {
                "\n" + JSONObject().put("serviceId", it.serviceId).put("hospitalKey", it.hospitalKey)
                    .put("credentialGeneration", it.credentialGeneration).toString()
            } ?: "")
            val hash = MessageDigest.getInstance("SHA-256").digest(material.toByteArray(Charsets.UTF_8))
            return RoutineScope(normalizedAccount, credential.patientId, hash.joinToString("") { "%02x".format(it) }, origin)
        }
    }
}

/** Frozen pendingBody is retried byte-for-byte; completion is only set from a verified server acknowledgement. */
data class LocalRoutineTask(
    val task: RoutineTask,
    val receiptId: String = UUID.randomUUID().toString(),
    val receiptAt: String? = task.deliveredAt,
    val answers: String = "{}",
    val pendingBody: String? = null,
    val completedAt: String? = null,
    val availability: String = "available",
    val problem: String? = null,
    val retryAt: Long = 0,
    val attempts: Int = 0,
    val serverTime: String = "",
    val recordId: Long? = null,
    val submissionStatus: String? = null,
    val confirmedSubmissionBody: String? = null,
    val problemCode: String? = null,
    val fieldErrors: String? = null
) {
    fun encode(): String = JSONObject().put("task", JSONObject(task.raw)).put("receiptId", receiptId)
        .put("receiptAt", receiptAt ?: JSONObject.NULL).put("answers", answers)
        .put("pendingBody", pendingBody ?: JSONObject.NULL).put("completedAt", completedAt ?: JSONObject.NULL)
        .put("availability", availability).put("problem", problem ?: JSONObject.NULL)
        .put("retryAt", retryAt).put("attempts", attempts).put("serverTime", serverTime)
        .put("recordId", recordId ?: JSONObject.NULL).put("submissionStatus", submissionStatus ?: JSONObject.NULL)
        .put("confirmedSubmissionBody", confirmedSubmissionBody ?: JSONObject.NULL)
        .put("problemCode", problemCode ?: JSONObject.NULL).put("fieldErrors", fieldErrors ?: JSONObject.NULL).toString()
    companion object {
        fun decode(raw: String): LocalRoutineTask {
            val o = JSONObject(raw)
            // Legacy encrypted cache only: discard the removed combination metadata,
            // retaining answers, receipt IDs and the byte-for-byte pending request.
            o.getJSONObject("task").remove("planRevision")
            fun nullable(key: String): String? = if (o.isNull(key)) null else o.getString(key)
            return LocalRoutineTask(RoutineQuestionnaireParser.task(o.getJSONObject("task"), true), o.getString("receiptId"),
                nullable("receiptAt"), o.getString("answers"), nullable("pendingBody"), nullable("completedAt"),
                o.getString("availability"), nullable("problem"), o.getLong("retryAt"), o.getInt("attempts"), o.getString("serverTime"),
                if (o.isNull("recordId")) null else RoutineQuestionnaireParser.integer(o, "recordId"),
                nullable("submissionStatus"), nullable("confirmedSubmissionBody"), nullable("problemCode"), nullable("fieldErrors"))
        }
    }
}

interface RoutineQuestionnaireStorage {
    /** Quarantine the old binding; restore only records belonging to this exact original scope. */
    fun bind(scope: RoutineScope)
    fun matches(scope: RoutineScope): Boolean
    fun rejected(scope: RoutineScope): Boolean
    fun reject(scope: RoutineScope)
    fun read(scope: RoutineScope): List<LocalRoutineTask>
    /** Atomic batch, only if the binding still matches; false forbids receipts. */
    fun put(scope: RoutineScope, rows: List<LocalRoutineTask>): Boolean
}
