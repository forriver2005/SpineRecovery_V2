# Local main integration, 2026-11-24

Branch: `integration/main-into-metaspineapp-20261124`.

Merge parents:

- Mobile base: `1478edda0bf28b77909014c955669552da267a12` (`MetaspineApp`). This already includes the six-file prescription difficulty change that was uncommitted during the initial assessment.
- Incoming main: `862ac44b258392c31f5477e5e1a454e0417c2f78` (`origin/main`).

This is a normal merge preserving both histories. The initial integration was completed locally. It is now included in the PR targeting `MetaspineApp`, together with the full action-library integration and acceptance report.

## Conflict resolutions

- `GameWorkoutScene`: retain the mobile prescription permission check and calibration policy alongside Taiko Night discovery, start readiness, completion events and cancellation. Handle a missing legacy scorer in Taiko scenes and obtain completed repetitions from the active runtime.
- `HipThrustHold30Timeline`: preserve the existing named `holdSeconds` argument and add a separate `extraRecordedHoldSeconds` argument. Prescription holds use the exact requested duration; recorded playback adds its extension only in recorded-timing mode.
- `HipThrustTaikoController`: prescription total repetitions and hold duration take priority. Non-prescription sequences retain main's exact-per-set recorded repetition support and additional hold time. Initialize the assessment repetitions after selecting the final timeline.

## Integration repairs

- Give `VmcGameModeSourceBridge.cs` its own GUID. Main supplied the same GUID as the existing `SlimeVrAvatarMotionAdapter.cs`; the old adapter GUID remains unchanged so serialized mobile scene references continue to resolve correctly.
- Fix tutorial scene paths, their GUIDs and the missing scene-list entry marker in Build Settings.
- Correct the stale BirdDogPractice GUID in Build Settings.
- Remove two dangling Delivery scene registrations. Neither Delivery scene is present in main; their source packages and scene-builder tools remain available. The existing recorded bird-dog/dead-bug scenes remain packaged.
- Preserve AppShell as the first scene and package the right-side-bridge and Taiko Night scenes.

## Validation

- Static asset audit: 36 build scenes, no missing scene paths or mismatched scene GUIDs, no unresolved serialized scene script GUIDs, and no duplicate tracked asset GUIDs.
- Unity 2022.3.62f3 batch verification: `MainIntegrationVerification.Run` passed 116 checks, including prescribed holds of 0, 7, 30 and 600 seconds, exact repetitions, recorded hold extension, wrap timing, tracking-script identity and game route registration.
- Existing `NavigationModuleVerification.Run`: 16 checks passed.
- Native Android Debug and Release unit tests: 11 tests per variant, zero failures/errors. Includes prescription difficulty and LAN training synchronization tests.
- Unity Android IL2CPP export succeeded, followed by the repository's `Tools/Build-Unity2022ExternalApk.ps1` Debug build: 120 Gradle tasks executed successfully.
- APK signature verification passed (v2); package `com.metaspine.mobile`, ARM64, min SDK 29, target SDK 36.
- Artifact: `D:/SpineFlow/tmp/main-integration-20261124/MetaSpine-integration-debug.apk`, 299,210,559 bytes, SHA-256 `0536758b5347f70f9dd4c68da365b4960e68bcba36a7f66428555e145d5b068e`.

The external Gradle build reports an NDK version mismatch during stripping of third-party native libraries and packages those libraries without stripping. The build and APK signature check succeed; this is not a clean production-release certification. Existing C# unused-field, Kotlin deprecation and Gradle deprecation warnings remain.

Validation artifacts: `D:/SpineFlow/tmp/main-integration-20261124/`.
The optional `com.coplaydev.unity-mcp` editor integration is temporarily excluded while running batch checks, following the existing local validation workflow. Package files and the project version are restored afterward. Validation uses the installed 2022.3.62f3 editor; the repository's 2022.3.62f3c1 version declaration is preserved.

## Remaining functional acceptance

This merges the branches and repairs integration defects; it does not extend the mobile/server prescription contract to new action types. The Android built-in V2 prescription catalog still supports dead bug, bird dog, hip thrust and side bridge. Right-side-bridge, crunch and lying-cat prescription mapping, and the independent Taiko action runtime's unified result reporting, need a separate functional integration before claiming complete mobile prescription support for those new actions.

No device installation, physical tracker test, live server upload or end-to-end patient session was performed. Device acceptance still needs to exercise coach-to-game transitions, calibration between actions, tutorial pause/resume, score persistence/upload and portrait restoration.
