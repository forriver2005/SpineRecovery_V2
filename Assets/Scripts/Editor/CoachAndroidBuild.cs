using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

[InitializeOnLoad]
public static class CoachAndroidBuild
{
    static CoachAndroidBuild() { EditorApplication.delayCall += RunRequested; }

    static void RunRequested()
    {
        const string request = ".codex/build-coach-apk";
        if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        Build();
    }

    [MenuItem("Spine Recovery/Build Coach APK")]
    public static void Build()
    {
        Directory.CreateDirectory("Builds/Android");
        string output = "Builds/Android/SpineRecovery-Coach-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".apk";
        bool bundle = EditorUserBuildSettings.buildAppBundle;
        bool export = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        try
        {
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            File.WriteAllText(".codex/coach-apk-build-result.txt",
                report.summary.result + "\n" + Path.GetFullPath(output) + "\nErrors=" +
                report.summary.totalErrors + "\nBytes=" + report.summary.totalSize);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Coach APK build failed; inspect Unity Editor.log.");
        }
        finally
        {
            EditorUserBuildSettings.buildAppBundle = bundle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = export;
        }
    }
}
