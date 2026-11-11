package com.metaspine.mobile.ar.protocol

import com.metaspine.mobile.data.SpineFlowProtocol
import org.json.JSONObject

enum class ArResultStatus(val wireValue: String) {
    Completed(SpineFlowProtocol.STATUS_COMPLETED),
    Interrupted(SpineFlowProtocol.STATUS_INTERRUPTED),
    Failed(SpineFlowProtocol.STATUS_FAILED),
    Cancelled(SpineFlowProtocol.STATUS_CANCELLED);

    companion object {
        fun fromWireValue(value: String): ArResultStatus? = entries.firstOrNull {
            it.wireValue == value
        }
    }
}

data class ArResultEnvelope(
    val sessionId: String,
    val userId: String,
    val requestToken: String,
    val status: ArResultStatus,
    val endReason: String,
    val payloadJson: String
)

object ArProtocol {
    fun parseResultEnvelope(json: String): ArResultEnvelope {
        val root = JSONObject(json)
        requireVersion(root)
        require(root.optString("messageType") == SpineFlowProtocol.MESSAGE_SESSION_RESULT) {
            "Unexpected result messageType."
        }
        val sessionId = root.optString("sessionId")
        val userId = root.optString("userId")
        val requestToken = root.optString("requestToken")
        val status = ArResultStatus.fromWireValue(root.optString("status"))
            ?: throw IllegalArgumentException("Unsupported AR result status.")
        require(sessionId.isNotBlank()) { "Missing result sessionId." }
        require(userId.isNotBlank()) { "Missing result userId." }
        require(requestToken.isNotBlank()) { "Missing result requestToken." }
        return ArResultEnvelope(
            sessionId = sessionId,
            userId = userId,
            requestToken = requestToken,
            status = status,
            endReason = root.optString("endReason"),
            payloadJson = json
        )
    }

    private fun requireVersion(root: JSONObject) {
        require(root.optInt("protocolVersion", -1) == SpineFlowProtocol.PROTOCOL_VERSION) {
            "Unsupported MetaSpine protocol version."
        }
    }
}
