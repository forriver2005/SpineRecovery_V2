package com.metaspine.mobile.data

import org.json.JSONObject
import java.security.MessageDigest
import java.util.Locale
import java.util.UUID

data class RoutineScope(val accountId: String, val patientId: String, val fingerprint: String) {
    companion object {
        fun of(accountId: String, credential: PatientServerCredential, origin: String): RoutineScope {
            val hash = MessageDigest.getInstance("SHA-256").digest((origin + "\n" + credential.patientToken).toByteArray())
            return RoutineScope(accountId.trim().lowercase(Locale.ROOT), credential.patientId, hash.joinToString("") { "%02x".format(it) })
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
    val serverTime: String = ""
) {
    fun encode(): String = JSONObject().put("task", JSONObject(task.raw)).put("receiptId", receiptId)
        .put("receiptAt", receiptAt ?: JSONObject.NULL).put("answers", answers)
        .put("pendingBody", pendingBody ?: JSONObject.NULL).put("completedAt", completedAt ?: JSONObject.NULL)
        .put("availability", availability).put("problem", problem ?: JSONObject.NULL)
        .put("retryAt", retryAt).put("attempts", attempts).put("serverTime", serverTime).toString()
    companion object {
        fun decode(raw: String): LocalRoutineTask {
            val o = JSONObject(raw)
            // Legacy encrypted cache only: discard the removed combination metadata,
            // retaining answers, receipt IDs and the byte-for-byte pending request.
            o.getJSONObject("task").remove("planRevision")
            fun nullable(key: String): String? = if (o.isNull(key)) null else o.getString(key)
            return LocalRoutineTask(RoutineQuestionnaireParser.task(o.getJSONObject("task"), true), o.getString("receiptId"),
                nullable("receiptAt"), o.getString("answers"), nullable("pendingBody"), nullable("completedAt"),
                o.getString("availability"), nullable("problem"), o.getLong("retryAt"), o.getInt("attempts"), o.getString("serverTime"))
        }
    }
}

interface RoutineQuestionnaireStorage {
    /** Atomically purge this account's old tasks when patient, PAT or origin changes. */
    fun bind(scope: RoutineScope)
    fun matches(scope: RoutineScope): Boolean
    fun rejected(scope: RoutineScope): Boolean
    fun reject(scope: RoutineScope)
    fun read(scope: RoutineScope): List<LocalRoutineTask>
    /** Atomic batch, only if the binding still matches; false forbids receipts. */
    fun put(scope: RoutineScope, rows: List<LocalRoutineTask>): Boolean
}
