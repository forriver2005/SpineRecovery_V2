#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpineRecovery.Editor
{
    public static class BuildAndroidApk
    {
        public static void PerformBuild()
        {
            string configuration = Environment.GetEnvironmentVariable("SPINE_BUILD_CONFIGURATION") ?? "Debug";
            bool release = string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase);
            if (!release && !string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase))
                throw new Exception("SPINE_BUILD_CONFIGURATION must be Debug or Release.");
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.metaspine.mobile");
            string output = Environment.GetEnvironmentVariable("SPINE_APK_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "SpineRecovery_SlimeVR_LogExport.apk");

            string fullOutput = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(fullOutput);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new Exception("No enabled scenes are configured in EditorBuildSettings.");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = fullOutput,
                target = BuildTarget.Android,
                options = release ? BuildOptions.None : BuildOptions.Development | BuildOptions.AllowDebugging,
            };

            if (string.Equals(Environment.GetEnvironmentVariable("SPINE_GRADLE_EXPORT"), "1", StringComparison.OrdinalIgnoreCase))
            {
                EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
                options.options = release ? BuildOptions.None : BuildOptions.Development;
                Debug.Log("[BuildAndroidApk] Exporting Gradle project " + fullOutput);
            }

            Debug.Log("[BuildAndroidApk] Building " + configuration + " " + fullOutput);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Android build failed: " + report.summary.result + "; errors=" + report.summary.totalErrors);

            Debug.Log("[BuildAndroidApk] Build succeeded: " + fullOutput + " (" + report.summary.totalSize + " bytes)");
        }
    }

    // Unity 6's Android Gradle Plugin requires duplicate Java metadata to be
    // excluded explicitly.  The embedded SlimeVR/Netty dependencies contain
    // the same META-INF files in several jars, so patch the generated launcher
    // project immediately before Gradle runs.
    public sealed class AndroidGradleMetadataFix : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string gradleRoot = Directory.GetParent(path).FullName;
            string launcher = Path.Combine(gradleRoot, "launcher", "build.gradle");
            Patch(launcher);
            Patch(Path.Combine(path, "build.gradle"));
        }

        private static void Patch(string file)
        {
            if (!File.Exists(file)) return;
            string text = File.ReadAllText(file);
            if (text.Contains("META-INF/INDEX.LIST")) return;
            const string marker = "    packaging {\n";
            const string legacyMarker = "    packagingOptions {\n";
            string block =
                "    packagingOptions {\n" +
                "        resources {\n" +
                "            excludes += ['META-INF/INDEX.LIST', 'META-INF/io.netty.versions.properties', 'META-INF/DEPENDENCIES', 'META-INF/LICENSE', 'META-INF/LICENSE.txt', 'META-INF/NOTICE', 'META-INF/NOTICE.txt']\n" +
                "        }\n";
            if (text.Contains(marker))
                text = text.Replace(marker, "    packaging {\n        resources {\n            excludes += ['META-INF/INDEX.LIST', 'META-INF/io.netty.versions.properties', 'META-INF/DEPENDENCIES', 'META-INF/LICENSE', 'META-INF/LICENSE.txt', 'META-INF/NOTICE', 'META-INF/NOTICE.txt']\n        }\n");
            else if (text.Contains(legacyMarker))
                text = text.Replace(legacyMarker, block);
            else
                return;
            File.WriteAllText(file, text);
            Debug.Log("[BuildAndroidApk] Patched duplicate Java resource exclusions in " + file);
        }
    }
}
#endif
