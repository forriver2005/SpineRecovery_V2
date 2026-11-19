# Navigation module integration, 2026-11-19

Base: UI module `7f66df0`, on MetaspineApp `34d2b5a`.
Navigation source: integration `c6db88d`.
Work branch: `merge/integration-into-metaspineapp-20261119`.

## Approved scope and behavior

- Home and training-preparation action-library entries open the existing, packaged `ActionLibrary` scene. The imported UI referenced `CoachingChoose`, which is absent from the build.
- Auxiliary browsing remembers its originating app route. Library browsing returns to that route; history playback returns to History, even when an existing training plan remains in memory.
- Leaving the auxiliary scene for a practice scene clears the browsing context. Subsequent training exits use the existing training exit behavior.
- Training/game Home invokes the existing `TrainingSessionService.FinishAndReturn()`; the snapshot, checkpoint, feedback, storage and upload implementation is unchanged.
- Practice-to-library navigation keeps its retained-scene restoration as the first-priority return path. Existing roots and their previous active states are restored without recreating the practice scene.
- Duplicate Home callbacks are guarded across switcher instances in the same scene; library return and auxiliary scene return also guard repeated requests.

## Differences from a direct integration copy

- Detect auxiliary browsing from the actual entry context, not merely the presence of AppBootstrap, a PlayBack scene name, or a null training plan.
- Limit Home rebinding to known game/playback scenes and the owning switcher's existing single ReturnToMobileApp callback. Preserve practice menu ownership and its StopPractice callback. Do not add a second pointer-click handler.
- Do not adopt integration's missing-Gamescene fallback to DeadBugPractice.
- Preserve original prescription startup, monthly questionnaire, training progress and save/upload protocols. This commit is a selective module port, not a full integration branch merge.

## Validation

- Unity 2022.3 script compilation and `NavigationModuleVerification.Run` completed successfully (16 editor checks).
- The checks cover auxiliary return routes with an existing training result, repeat navigation guards, stale replay context, restricted Home binding, clearing browsing context on practice entry, and preservation of retained scene objects and active states.
- Android-conditioned runtime C# compilation passed; existing unused-field warnings remain.
- TrainingSessionService, SpineFlowTrainingSession, DeadBugStartMenuController, AppShellRoutineUi and NativeAndroid have no changes relative to the MetaspineApp base.
- Validation temporarily excluded the optional unity-mcp editor package as in the UI module; manifest, lock file and ProjectVersion were restored. Temporary test scenes were removed.
- The editor checks use synthetic scenes and invoke navigation handlers. They do not constitute full device scene-load, training-progress, disk persistence or upload acceptance. No APK was built in this module.

## Device acceptance still required

1. Open the action library from Home and TrainingPreparation; return to the originating page.
2. Open it during practice, then return; verify action, repetition and phase remain intact.
3. Open playback from History with and without a pending training result; return to History without altering that result.
4. Exit actual practice/game training via Home; verify the existing feedback/save flow and subsequent upload. Repeat-click Home and verify only one result is recorded.

No push or PR creation is included. Further modules require separate user confirmation.
