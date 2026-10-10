Embedded SlimeVR Android runtime for SpineRecovery Unity

Source of truth: SpineFlowMobile/third_party/slimevr, SlimeVR commit
c72a30aca54abf01f0336a9bc6bd1b2df215f851; SolarXR Protocol commit
00c38a6dc28070b30850a89c26b17928e56245d4.

Included build outputs:
- slimevr-android-runtime.aar: release AAR; contains EmbeddedSlimeVrActivity,
  SlimeVrForegroundService, Android resources, and embedded web GUI assets.
- slimevr-core.jar and slimevr-protocol.jar: project dependencies not bundled
  inside the Android runtime AAR.
- mainTemplate.gradle and settingsTemplate.gradle: runtime Maven dependencies
  and the JitPack/Mozilla repositories used by the source Gradle project.

The Unity C# bridge starts the in-APK Android Activity at /checklist and passes
the profile height, matching SpineFlowMobile's EmbeddedSlimeVrLauncher. The
Activity itself starts the foreground SlimeVR service. This is one installed
APK, but the SlimeVR calibration UI remains its embedded Android UI, not a
Unity-rendered recreation.

Minimum Android SDK is 29; compile SDK must be at least 36 because the source
AAR declares minCompileSdk=36. Unity's Android SDK path must therefore provide
API 36 or newer. Source licenses and trademark/provenance notices are in
SlimeVR-Licenses/.

After changing the vendored source or dependency versions, rebuild the release
AAR and both JARs from the audited SpineFlowMobile checkout before replacing
these files. Do not edit generated AAR/JAR contents manually.
