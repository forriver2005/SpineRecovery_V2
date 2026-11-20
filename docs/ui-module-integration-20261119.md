# UI module integration, 2026-11-19

Base: MetaspineApp `34d2b5a`. Presentation source: integration `c6db88d`.
Work branch: `merge/integration-into-metaspineapp-20261119`.

## Approved scope

- Adopt integration's login, registration, activation, home, profile, training preparation, calibration and pre/post-training presentation, including the input focus fixes and raster resources.
- Adopt the quick-calibration bridge and embedded SlimeVR runtime.
- Preserve the existing between-action two-pose calibration, voice prompts and continuation callback; supply a matching light-card UI.
- Preserve the original prescription startup and four-action execution. Do not introduce `planToken`, `confirmPlan`, or the new directive protocol.
- Defer monthly questionnaires. Keep `AppShellRoutineUi.cs`, its page, commands and Android implementation unchanged. The new home entry opens that existing page.

## Compatibility decisions

- The new pre-training screen includes the existing symptom choices (`stable`, `slightly_worse`, `worse`). The admission condition remains pain <= 7 and symptom != `worse`; integration's 3-point threshold is not adopted.
- Intake/profile business handlers and native prescription/training/upload implementations remain on the MetaspineApp version.
- Post-training numeric responses use the existing feedback text transport. Captions avoid the transport's ` / ` separator and correctly describe a 0–10 scale. No new automatic post-training stop protocol is introduced.
- Navigation routes are appended, preserving existing enum values. The calibration confirmation timestamp is a presentation field; no new Android storage protocol is included.
- Dependencies, project settings, training assessment, original questionnaire code and Android source are unchanged. This is a module commit, not a merge of the entire integration history.

## Validation

- Unity 2022.3 project import and script compilation completed with exit code 0.
- Editor and Android-conditioned C# compilation completed successfully.
- The existing between-action Java executor compiled against integration's SlimeVR dependencies.
- Actual Unity UI renders checked: login, calibration, pre-training, and the new between-action page. Previews are under `D:/SpineFlow/tmp/pr-review-20261119/ui-previews`.
- Resource GUID collision check passed; no new prescription or questionnaire commands remain in AppShellUi.
- Unity validation temporarily excluded the optional `com.coplaydev.unity-mcp` editor package because its download failed certificate verification. Original package configuration was restored afterward. Existing sample-prefab and URP warnings remain outside this module.
- APK packaging and physical-device calibration/network/upload acceptance have not been performed.

Further functional modules require separate user confirmation before modification.
