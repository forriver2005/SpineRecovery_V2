using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>Mirrors the left-side coach presentation for the right-side route.</summary>
public static class SideBridgeOppositePracticeBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= Configure;
        SceneManager.sceneLoaded += Configure;
        var host = new GameObject("SideBridgeOppositePracticeBootstrap");
        Object.DontDestroyOnLoad(host);
        host.AddComponent<DelayedConfigure>();
    }

    private sealed class DelayedConfigure : MonoBehaviour
    {
        private System.Collections.IEnumerator Start()
        {
            for (var i = 0; i < 30; i++)
            {
                yield return null;
                Configure(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            }
        }
    }

    private static void Configure(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "SideBridgeOppositePractice") return;
        foreach (var text in Object.FindObjectsOfType<TMP_Text>(true))
        {
            if (!string.IsNullOrEmpty(text.text) &&
                (text.text.Contains("Dead Bug") || text.text.Contains("死虫") ||
                text.text.Contains("Bird Dog") || text.text.Contains("鸟狗")))
                text.text = "侧桥";
        }
        var recorder = Object.FindObjectOfType<MotionRecorder>(true);
        if (recorder?.CoachAnimator != null)
        {
            var root = recorder.CoachAnimator.transform;
            var scale = root.localScale;
            scale.x = -Mathf.Abs(scale.x);
            root.localScale = scale;
        }
        var progress = Object.FindObjectOfType<CoachSetProgressUI>(true);
        progress?.ShowLessonStage("右侧支撑 · 躯干");
    }
}
