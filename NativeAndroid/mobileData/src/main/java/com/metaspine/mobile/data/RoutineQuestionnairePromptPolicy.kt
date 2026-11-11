package com.metaspine.mobile.data

/** Only pending tasks for the currently bound account enter the login prompt queue. */
fun pendingLoginQuestionnaires(rows: List<LocalRoutineTask>): List<LocalRoutineTask> = rows
    .filter { it.completedAt == null && it.task.schema != null &&
        (it.availability == "available" || it.pendingBody != null) }
    .sortedWith(compareBy<LocalRoutineTask> { it.problem != null }
        .thenBy { it.task.availableAt }.thenBy { it.task.taskId })
