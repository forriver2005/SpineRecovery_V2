using System;
using System.IO;
using System.Reflection;
using TaikoNight;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Batch entry point: -batchmode -executeMethod GameStartRegression.Run (without -quit).
// Uses editor-only telemetry in the real scenes; no production input bypass.
[InitializeOnLoad]
public static class GameStartRegression
{
    private const string Flag = "GameStartRegression.Active";
    private static readonly string[] Scenes = { "LyingCatPractice_TaikoNight", "CrunchPractice_TaikoNight" };
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int sceneIndex;
    private static double deadline;
    private static string report = "";

    static GameStartRegression()
    {
        if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick;
    }

    public static void Run()
    {
        sceneIndex = 0;
        deadline = 0;
        report = "";
        EditorSceneManager.OpenScene("Assets/Scenes/" + Scenes[0] + ".unity");
        SessionState.SetBool(Flag, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception(Scenes[sceneIndex] + ": " + message);
        report += "PASS " + Scenes[sceneIndex] + ": " + message + "\n";
    }

    private static void Refresh(TaikoNightSelection selection) =>
        typeof(TaikoNightSelection).GetMethod("RefreshInput", Private).Invoke(selection, null);

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 60;
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Scene startup timeout");
            if (SceneManager.GetActiveScene().name != Scenes[sceneIndex]) return;
            var runtime = UnityEngine.Object.FindObjectOfType<TaikoNightActionRuntime>();
            var selection = UnityEngine.Object.FindObjectOfType<TaikoNightSelection>();
            var entry = UnityEngine.Object.FindObjectOfType<GameWorkoutScene>();
            if (runtime == null || selection == null || entry == null) return;

            var button = (Button)typeof(TaikoNightSelection).GetField("start", Private).GetValue(selection);
            var label = button.GetComponentInChildren<TMP_Text>(true);
            AvatarMotionSource input = null;
            foreach (var source in UnityEngine.Object.FindObjectsOfType<AvatarMotionSource>())
            {
                source.SetAvailable(false);
                if (source.Model == runtime.recorder.UserAnimator.gameObject) input = source;
            }
            GameObject simulatedInput = null;
            if (input == null)
            {
                simulatedInput = new GameObject("Editor-only tracking input");
                input = simulatedInput.AddComponent<AvatarMotionSource>();
                input.Configure(runtime.recorder.UserAnimator);
                input.SetAvailable(false);
            }
            typeof(TaikoNightSelection).GetField("source", Private).SetValue(selection, input);
            Refresh(selection);
            entry.menu.StartTraining();
            Check(!runtime.IsCountingDown && !runtime.IsRunning && !button.interactable &&
                label.text == "等待动作传感器连接", "disconnected input blocks start with visible reason");

            input.ReportFrame(Time.unscaledTime, 24);
            Check(runtime.CanStartTraining, "action runtime ready with fresh input");
            var motionField = typeof(TaikoNightActionRuntime).GetField("motion", Private);
            object motion = motionField.GetValue(runtime);
            motionField.SetValue(runtime, null);
            Refresh(selection);
            entry.menu.StartTraining();
            Check(!runtime.IsCountingDown && !button.interactable && label.text == "动作资源尚未就绪",
                "missing motion resources block start with visible reason");
            motionField.SetValue(runtime, motion);

            var original = entry.menu.TrainingStartAllowed;
            entry.menu.TrainingStartAllowed = () => false;
            Refresh(selection);
            entry.menu.StartTraining();
            Check(!runtime.IsCountingDown && !button.interactable, "prescription/sequence guard still blocks");
            entry.menu.TrainingStartAllowed = original;
            Refresh(selection);
            Check(button.interactable && label.text == "开始游戏", "visible menu agrees with game readiness");
            int started = 0;
            entry.menu.TrainingStarted += () => started++;
            button.onClick.Invoke();
            Check(runtime.IsCountingDown && started == 1 && !selection.gameObject.activeSelf,
                "actual start button enters game countdown from copied practice menu");
            entry.menu.StartTraining();
            Check(started == 1, "duplicate click cannot start twice");
            runtime.CancelTraining();
            Refresh(selection);
            Check(selection.gameObject.activeSelf && button.interactable, "cancel returns to a usable game menu");
            input.SetAvailable(false);
            Refresh(selection);
            entry.menu.StartTraining();
            Check(!runtime.IsCountingDown && !button.interactable, "disconnect after returning to menu prevents restart");
            if (simulatedInput != null) UnityEngine.Object.Destroy(simulatedInput);
            sceneIndex++;
            if (sceneIndex == Scenes.Length) { Finish(0); return; }
            deadline = 0;
            SceneManager.LoadScene(Scenes[sceneIndex]);
        }
        catch (Exception exception)
        {
            report += "FAIL " + exception + "\n";
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void Finish(int code)
    {
        Directory.CreateDirectory("output/game-start");
        File.WriteAllText("output/game-start/regression.txt", report);
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
}
