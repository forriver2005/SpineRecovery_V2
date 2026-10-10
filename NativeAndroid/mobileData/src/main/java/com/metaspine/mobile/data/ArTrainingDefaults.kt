package com.metaspine.mobile.data

/** Phone defaults plus the fixed repetition counts implemented by the Unity game profiles. */
object ArTrainingDefaults {
    const val COACH_SETS = 1
    const val COACH_REPS = 4

    // Product-facing total that includes the stability countdown and scoring window.
    const val COACH_HOLD_SECONDS = 10

    const val GAME_SIMPLE_REPS = 4
    const val GAME_STANDARD_REPS = 8
    const val GAME_HARD_REPS = 12

    // Retained for payload compatibility; Unity does not use these values as game durations.
    val GAME_DIFFICULTY_OPTIONS_SECONDS = listOf(5, 10, 20)
}
