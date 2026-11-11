package com.metaspine.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import kotlin.math.roundToInt

data class TrainingMetricSummary(
    val speed: Int,
    val smoothness: Int,
    val stability: Int,
    val symmetry: Int,
    val holdTime: Int,
    val rangeOfMotion: Int
)

data class TrainingWarning(
    val type: String,
    val message: String,
    val count: Int
)

data class ActionResult(
    val actionId: String,
    val actionName: String,
    val overallScore: Int,
    val metrics: TrainingMetricSummary,
    val warnings: List<TrainingWarning>,
    val passed: Boolean,
    val trainingMode: String = "",
    val completedRepetitions: Int = 0,
    val durationSeconds: Int = 0,
    val averageScore: Int = overallScore,
    val rawOverallScore: Int = overallScore,
    val rawAverageScore: Int = averageScore,
    val bestScore: Int = overallScore,
    val rawBestScore: Int = bestScore,
    val scoreSampleCount: Int = 0,
    val plannedRepetitions: Int = 0,
    val scoreMappingVersion: String = "",
    val metricsAvailable: Boolean = false,
    val minimumUserMotionDegrees: Double = 0.0,
    val minimumCoachMotionDegrees: Double = 0.0
)

data class GameResult(
    val gameId: String,
    val gameName: String,
    val score: Int,
    val level: String,
    val durationSeconds: Int,
    val averageScore: Int = score,
    val rawAverageScore: Int = averageScore,
    val bestScore: Int = score,
    val rawBestScore: Int = bestScore,
    val scoreSampleCount: Int = 0,
    val completedRepetitions: Int = 0,
    val scoreMappingVersion: String = ""
)

data class TrainingResultPayload(
    val sessionId: String,
    val userId: String,
    val createdAt: String,
    val completed: Boolean,
    val durationSeconds: Int,
    val actionResults: List<ActionResult>,
    val gameResults: List<GameResult> = emptyList(),
    val admissionPainLevel: Int? = null,
    val dataOrigin: String = "",
    val overallScore: Int? = null,
    val rawOverallScore: Int? = null,
    val scoreMappingVersion: String = ""
) {
    val primaryScore: Int?
        get() {
            overallScore?.takeIf { it in 0..100 }?.let { return it }

            fun modeScore(mode: String): Int? {
                val values = actionResults.filter { action ->
                    val searchable = "${action.trainingMode} ${action.actionId}".lowercase()
                    mode in searchable
                }.map { it.averageScore.coerceIn(0, 100) }
                return values.takeIf { it.isNotEmpty() }?.average()?.roundToInt()
            }

            val coach = modeScore("coach")
            val game = modeScore("game")
            return when {
                coach != null && game != null ->
                    (coach * 0.7 + game * 0.3).roundToInt().coerceIn(0, 100)
                coach != null -> coach
                game != null -> game
                else -> actionResults
                    .map { it.averageScore.coerceIn(0, 100) }
                    .takeIf { it.isNotEmpty() }
                    ?.average()
                    ?.roundToInt()
            }
        }

    fun toJson(): String {
        return JSONObject()
            .put("schemaVersion", "0.1")
            .put("sessionId", sessionId)
            .put("userId", userId)
            .put("createdAt", createdAt)
            .put("sourceApp", "ar_app")
            .put("completed", completed)
            .put("durationSeconds", durationSeconds)
            .put("dataOrigin", dataOrigin)
            .also { root ->
                admissionPainLevel?.let { root.put("admissionPainLevel", it.coerceIn(1, 10)) }
                overallScore?.let { root.put("overallScore", it.coerceIn(0, 100)) }
                rawOverallScore?.let { root.put("rawOverallScore", it.coerceIn(0, 100)) }
                if (scoreMappingVersion.isNotBlank()) {
                    root.put("scoreMappingVersion", scoreMappingVersion)
                }
            }
            .put("actionResults", JSONArray().also { array ->
                actionResults.forEach { result ->
                    array.put(
                        JSONObject()
                            .put("actionId", result.actionId)
                            .put("actionName", result.actionName)
                            .put("overallScore", result.overallScore)
                            .put("rawOverallScore", result.rawOverallScore)
                            .put("trainingMode", result.trainingMode)
                            .put("plannedRepetitions", result.plannedRepetitions)
                            .put("completedRepetitions", result.completedRepetitions)
                            .put("durationSeconds", result.durationSeconds)
                            .put("averageScore", result.averageScore)
                            .put("rawAverageScore", result.rawAverageScore)
                            .put("bestScore", result.bestScore)
                            .put("rawBestScore", result.rawBestScore)
                            .put("scoreSampleCount", result.scoreSampleCount)
                            .put("scoreMappingVersion", result.scoreMappingVersion)
                            .put("metricsAvailable", result.metricsAvailable)
                            .put(
                                "minimumUserMotionDegrees",
                                result.minimumUserMotionDegrees
                            )
                            .put(
                                "minimumCoachMotionDegrees",
                                result.minimumCoachMotionDegrees
                            )
                            .put(
                                "metrics",
                                JSONObject()
                                    .put("speed", result.metrics.speed)
                                    .put("smoothness", result.metrics.smoothness)
                                    .put("stability", result.metrics.stability)
                                    .put("symmetry", result.metrics.symmetry)
                                    .put("holdTime", result.metrics.holdTime)
                                    .put("rangeOfMotion", result.metrics.rangeOfMotion)
                            )
                            .put("warnings", JSONArray().also { warnings ->
                                result.warnings.forEach { warning ->
                                    warnings.put(
                                        JSONObject()
                                            .put("type", warning.type)
                                            .put("message", warning.message)
                                            .put("count", warning.count)
                                    )
                                }
                            })
                            .put("passed", result.passed)
                            .put("cheatingDetected", false)
                    )
                }
            })
            .put("gameResults", JSONArray().also { array ->
                gameResults.forEach { result ->
                    array.put(
                        JSONObject()
                            .put("gameId", result.gameId)
                            .put("gameName", result.gameName)
                            .put("score", result.score)
                            .put("averageScore", result.averageScore)
                            .put("rawAverageScore", result.rawAverageScore)
                            .put("bestScore", result.bestScore)
                            .put("rawBestScore", result.rawBestScore)
                            .put("scoreSampleCount", result.scoreSampleCount)
                            .put("completedRepetitions", result.completedRepetitions)
                            .put("scoreMappingVersion", result.scoreMappingVersion)
                            .put("level", result.level)
                            .put("durationSeconds", result.durationSeconds)
                    )
                }
            })
            .put("abnormalEvents", JSONArray())
            .toString(2)
    }

    companion object {
        fun parseValidated(json: String): TrainingResultPayload {
            val root = JSONObject(json)
            require(root.optString("sessionId").isNotBlank()) { "Missing sessionId." }
            require(root.optString("userId").isNotBlank()) { "Missing userId." }
            require(root.has("completed")) { "Missing completed status." }
            require(root.has("durationSeconds")) { "Missing durationSeconds." }
            require(root.optBoolean("completed", false)) { "Training is not completed." }
            require(root.optInt("durationSeconds", 0) > 0) { "Invalid durationSeconds." }
            if (root.has("overallScore")) {
                require(root.optInt("overallScore", -1) in 0..100) {
                    "Invalid overallScore."
                }
            }
            val actions = root.optJSONArray("actionResults")
                ?: throw IllegalArgumentException("Missing actionResults.")
            require(actions.length() > 0) { "actionResults is empty." }
            val seenActionIds = mutableSetOf<String>()
            for (index in 0 until actions.length()) {
                val action = actions.optJSONObject(index)
                    ?: throw IllegalArgumentException("Invalid action result at index $index.")
                val actionId = action.optString("actionId")
                require(actionId.isNotBlank()) { "Missing actionId." }
                require(seenActionIds.add(actionId)) { "Duplicate actionId." }
                require(action.has("overallScore")) { "Missing overallScore." }
                require(action.optInt("overallScore", -1) in 0..100) {
                    "Invalid overallScore."
                }
            }
            return fromJson(json)
        }

        fun fromJson(json: String): TrainingResultPayload {
            val root = JSONObject(json)
            val actions = root.optJSONArray("actionResults") ?: JSONArray()
            val results = buildList {
                for (index in 0 until actions.length()) {
                    val item = actions.optJSONObject(index) ?: continue
                    val metrics = item.optJSONObject("metrics") ?: JSONObject()
                    val warnings = item.optJSONArray("warnings") ?: JSONArray()
                    add(
                        ActionResult(
                            actionId = item.optString("actionId", "unknown_action"),
                            actionName = item.optString("actionName", "训练动作"),
                            overallScore = item.optInt("overallScore", 0),
                            metrics = TrainingMetricSummary(
                                speed = metrics.optInt("speed", 0),
                                smoothness = metrics.optInt("smoothness", 0),
                                stability = metrics.optInt("stability", 0),
                                symmetry = metrics.optInt("symmetry", 0),
                                holdTime = metrics.optInt("holdTime", 0),
                                rangeOfMotion = metrics.optInt("rangeOfMotion", 0)
                            ),
                            warnings = buildList {
                                for (warningIndex in 0 until warnings.length()) {
                                    val warning = warnings.optJSONObject(warningIndex) ?: continue
                                    add(
                                        TrainingWarning(
                                            type = warning.optString("type", "warning"),
                                            message = warning.optString("message", "训练中出现提示"),
                                            count = warning.optInt("count", 1)
                                        )
                                    )
                                }
                            },
                            passed = item.optBoolean("passed", true),
                            trainingMode = item.optString("trainingMode"),
                            completedRepetitions = item.optInt("completedRepetitions", 0)
                                .coerceAtLeast(0),
                            durationSeconds = item.optInt("durationSeconds", 0)
                                .coerceAtLeast(0),
                            averageScore = item.optInt(
                                "averageScore",
                                item.optInt("overallScore", 0)
                            ).coerceIn(0, 100),
                            rawOverallScore = item.optInt(
                                "rawOverallScore",
                                item.optInt("overallScore", 0)
                            ).coerceIn(0, 100),
                            rawAverageScore = item.optInt(
                                "rawAverageScore",
                                item.optInt(
                                    "averageScore",
                                    item.optInt("overallScore", 0)
                                )
                            ).coerceIn(0, 100),
                            bestScore = item.optInt(
                                "bestScore",
                                item.optInt("overallScore", 0)
                            ).coerceIn(0, 100),
                            rawBestScore = item.optInt(
                                "rawBestScore",
                                item.optInt(
                                    "bestScore",
                                    item.optInt("overallScore", 0)
                                )
                            ).coerceIn(0, 100),
                            scoreSampleCount = item.optInt("scoreSampleCount", 0)
                                .coerceAtLeast(0),
                            plannedRepetitions = item.optInt("plannedRepetitions", 0)
                                .coerceAtLeast(0),
                            scoreMappingVersion = item.optString("scoreMappingVersion"),
                            metricsAvailable = item.optBoolean(
                                "metricsAvailable",
                                listOf(
                                    metrics.optInt("speed", 0),
                                    metrics.optInt("smoothness", 0),
                                    metrics.optInt("stability", 0),
                                    metrics.optInt("symmetry", 0),
                                    metrics.optInt("holdTime", 0),
                                    metrics.optInt("rangeOfMotion", 0)
                                ).any { it > 0 }
                            ),
                            minimumUserMotionDegrees = item.optDouble(
                                "minimumUserMotionDegrees",
                                0.0
                            ).coerceAtLeast(0.0),
                            minimumCoachMotionDegrees = item.optDouble(
                                "minimumCoachMotionDegrees",
                                0.0
                            ).coerceAtLeast(0.0)
                        )
                    )
                }
            }
            val games = root.optJSONArray("gameResults") ?: JSONArray()
            val gameResults = buildList {
                for (index in 0 until games.length()) {
                    val item = games.optJSONObject(index) ?: continue
                    val gameId = item.optString("gameId")
                    if (gameId.isBlank()) continue
                    add(
                        GameResult(
                            gameId = gameId,
                            gameName = item.optString("gameName", gameId),
                            score = item.optInt("score", 0),
                            level = item.optString("level"),
                            durationSeconds = item.optInt("durationSeconds", 0).coerceAtLeast(0),
                            averageScore = item.optInt(
                                "averageScore",
                                item.optInt("score", 0)
                            ).coerceAtLeast(0),
                            rawAverageScore = item.optInt(
                                "rawAverageScore",
                                item.optInt(
                                    "averageScore",
                                    item.optInt("score", 0)
                                )
                            ).coerceAtLeast(0),
                            bestScore = item.optInt(
                                "bestScore",
                                item.optInt("score", 0)
                            ).coerceAtLeast(0),
                            rawBestScore = item.optInt(
                                "rawBestScore",
                                item.optInt(
                                    "bestScore",
                                    item.optInt("score", 0)
                                )
                            ).coerceAtLeast(0),
                            scoreSampleCount = item.optInt("scoreSampleCount", 0)
                                .coerceAtLeast(0),
                            completedRepetitions = item.optInt(
                                "completedRepetitions",
                                0
                            ).coerceAtLeast(0),
                            scoreMappingVersion = item.optString("scoreMappingVersion")
                        )
                    )
                }
            }

            return TrainingResultPayload(
                sessionId = root.optString("sessionId", ""),
                userId = root.optString("userId", ""),
                createdAt = root.optString("createdAt", ""),
                completed = root.optBoolean("completed", false),
                durationSeconds = root.optInt("durationSeconds", 0),
                actionResults = results,
                gameResults = gameResults,
                admissionPainLevel = root.optInt("admissionPainLevel", 0)
                    .takeIf { it in 1..10 },
                dataOrigin = root.optString("dataOrigin"),
                overallScore = root.optInt("overallScore", -1)
                    .takeIf { it in 0..100 },
                rawOverallScore = root.optInt("rawOverallScore", -1)
                    .takeIf { it in 0..100 },
                scoreMappingVersion = root.optString("scoreMappingVersion")
            )
        }

    }
}

enum class PostTrainingDisposition {
    ContinueToQuestionnaire,
    DiscardInterruptedSession
}

object TrainingCompletionPolicy {
    fun disposition(userConfirmedCompleted: Boolean): PostTrainingDisposition =
        if (userConfirmedCompleted) {
            PostTrainingDisposition.ContinueToQuestionnaire
        } else {
            PostTrainingDisposition.DiscardInterruptedSession
        }

    fun completedResultOrNull(resultJson: String): TrainingResultPayload? = runCatching {
        TrainingResultPayload.fromJson(resultJson)
    }.getOrNull()?.takeIf(TrainingResultPayload::completed)
}
