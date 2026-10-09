#if UNITY_EDITOR
using System;
using System.IO;
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
            string output = Environment.GetEnvironmentVariable("SPINE_APK_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.Combine("Builds", "SpineRecovery_SlimeVR_LogExport.apk");

            string fullOutput = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(fullOutput);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[]
                {
                    "Assets/Scenes/AppShell.unity",
                    "Assets/Scenes/SpineV2Minimal.unity",
                    "Assets/Scenes/BirdDogMinimal.unity",
                    "Assets/Scenes/HipThrustMinimal.unity",
                },
                locationPathName = fullOutput,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            };

            Debug.Log("[BuildAndroidApk] Building " + fullOutput);
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
            if (text.Contains("resources {\n            excludes +=")) return;
            const string marker = "    packaging {\n";
            if (!text.Contains(marker)) return;
            string block =
                "    packaging {\n" +
                "        resources {\n" +
                "            excludes += ['META-INF/INDEX.LIST', 'META-INF/io.netty.versions.properties', 'META-INF/DEPENDENCIES', 'META-INF/LICENSE', 'META-INF/LICENSE.txt', 'META-INF/NOTICE', 'META-INF/NOTICE.txt']\n" +
                "        }\n";
            text = text.Replace(marker, block);
            File.WriteAllText(file, text);
            Debug.Log("[BuildAndroidApk] Patched duplicate Java resource exclusions in " + file);
        }
    }
}
#endif
