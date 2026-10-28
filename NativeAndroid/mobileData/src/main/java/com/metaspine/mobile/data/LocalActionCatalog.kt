package com.metaspine.mobile.data

/**
 * Server action id to the local execution route. The server still owns dose and order;
 * this catalog only decides which bundled Unity scene can execute that action.
 */
data class LocalActionDefinition(
    val serverActionId: String,
    val localKey: String,
    val femaleCoachScene: String?,
    val maleCoachScene: String?,
    val gameScene: String?,
    val embeddedMotionResource: String? = null
) {
    fun coachScene(male: Boolean): String? = if (male) maleCoachScene else femaleCoachScene
}

object LocalActionCatalog {
    private val definitions = listOf(
        LocalActionDefinition("dead_bug", "dead_bug", "DeadBugPractice", "maleDeadBugPractice", "SpineV2Minimal"),
        LocalActionDefinition("bird_dog", "bird_dog", "BirdDogPractice", "maleBirdDogPractice", "BirdDogMinimal"),
        LocalActionDefinition("1", "clip_01", null, null, null, "ActionLibrary/Motions/clip_01"),
        LocalActionDefinition("2", "clip_03", null, null, null, "ActionLibrary/Motions/clip_03"),
        LocalActionDefinition("3", "clip_05", null, null, null, "ActionLibrary/Motions/clip_05"),
        LocalActionDefinition("4", "clip_07", null, null, null, "ActionLibrary/Motions/clip_07"),
        LocalActionDefinition("5", "clip_09", null, null, null, "ActionLibrary/Motions/clip_09"),
        LocalActionDefinition("6", "clip_10", null, null, null, "ActionLibrary/Motions/clip_10"),
        LocalActionDefinition("7", "clip_15", null, null, null, "ActionLibrary/Motions/clip_15"),
        LocalActionDefinition("8", "clip_17", null, null, null, "ActionLibrary/Motions/clip_17"),
        LocalActionDefinition("9", "clip_19", null, null, null, "ActionLibrary/Motions/clip_19"),
        LocalActionDefinition("10", "clip_21", null, null, null, "ActionLibrary/Motions/clip_21"),
        LocalActionDefinition("11", "clip_22", null, null, null, "ActionLibrary/Motions/clip_22")
    )

    fun find(serverActionId: String?): LocalActionDefinition? =
        definitions.firstOrNull { it.serverActionId.equals(serverActionId?.trim(), ignoreCase = true) }

    fun isLocallyExecutable(serverActionId: String?): Boolean =
        find(serverActionId)?.let { !it.femaleCoachScene.isNullOrBlank() && !it.gameScene.isNullOrBlank() } == true
}
