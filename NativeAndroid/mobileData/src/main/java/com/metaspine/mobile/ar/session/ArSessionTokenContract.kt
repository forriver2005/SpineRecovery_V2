package com.metaspine.mobile.ar.session

import com.metaspine.mobile.data.RecommendedExercise
import com.metaspine.mobile.data.SpineFlowProtocol
import com.metaspine.mobile.data.UserGender
import org.json.JSONObject
import java.util.UUID

internal data class CanonicalArStartPayload(
    val requestToken: String,
    val json: String
)

/**
 * Owns the single request-token transition from a saved start payload to the
 * exact payload launched into AR. A supplied administrator token is retained;
 * patient payloads that do not yet contain one receive exactly one token here.
 */
internal object ArSessionTokenContract {
    fun canonicalize(
        payloadJson: String,
        tokenFactory: () -> String = { UUID.randomUUID().toString() }
    ): CanonicalArStartPayload {
        val root = JSONObject(payloadJson)
        val requestToken = root.optString("requestToken")
            .trim()
            .takeIf(String::isNotBlank)
            ?: tokenFactory().trim().also {
                require(it.isNotBlank()) { "Generated AR requestToken is blank." }
            }
        return CanonicalArStartPayload(
            requestToken = requestToken,
            json = root.put("requestToken", requestToken).toString()
        )
    }

    fun forLaunch(startPayloadJson: String, requestToken: String): String {
        require(requestToken.isNotBlank()) { "AR requestToken is blank." }
        val root = JSONObject(startPayloadJson)
        val savedRequestToken = root.optString("requestToken").trim()
        require(savedRequestToken.isBlank() || savedRequestToken == requestToken) {
            "Saved and launch AR requestToken values differ."
        }
        applyMobileSceneRouting(root)
        return root
            .put("protocolVersion", SpineFlowProtocol.PROTOCOL_VERSION)
            .put("messageType", SpineFlowProtocol.MESSAGE_START_SESSION)
            .put("requestToken", requestToken)
            .put(
                "callback",
                JSONObject()
                    .put("packageName", SpineFlowProtocol.MOBILE_PACKAGE)
                    .put("action", SpineFlowProtocol.ACTION_AR_SESSION_RESULT)
            )
            .toString()
    }

    /**
     * The phone owns the handoff destination. Applying it at launch time also
     * upgrades a resumable session saved by an older phone build. The launcher
     * stays independent of the external AR project's source and implementation.
     */
    private fun applyMobileSceneRouting(root: JSONObject) {
        if (root.optString("flowType") in setOf(
                SpineFlowProtocol.FLOW_FIRST_ADMISSION,
                SpineFlowProtocol.FLOW_RETURNING_USER
            )
        ) {
            val actions = root
                .optJSONObject("trainingPlan")
                ?.optJSONArray("recommendedActions")
            val actionIds = buildList {
                if (actions != null) {
                    for (index in 0 until actions.length()) {
                        actions.optJSONObject(index)
                            ?.optString("actionId")
                            ?.takeIf(String::isNotBlank)
                            ?.let(::add)
                    }
                }
            }
            val exercise = requireNotNull(RecommendedExercise.fromActionIds(actionIds)) {
                "Patient recommendedActions do not map to one supported AR scene."
            }
            val gender = root
                .optJSONObject("userProfile")
                ?.optString("gender")
                ?.trim()
            val targetScene = if (gender == UserGender.Male.wireValue &&
                exercise == RecommendedExercise.DeadBug
            ) {
                SpineFlowProtocol.MALE_DEAD_BUG_PRACTICE_TARGET_SCENE
            } else {
                exercise.targetScene
            }
            root.put("targetScene", targetScene)
        }
    }
}
