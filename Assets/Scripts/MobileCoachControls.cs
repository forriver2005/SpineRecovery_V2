using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Present only in the migrated coach scenes. Never installed into game scenes.
public sealed class MobileCoachControls : MonoBehaviour
{
    [SerializeField] private PracticeSessionController session;
    [SerializeField] private AvatarMotionSource input;
    [SerializeField] private TMP_Text pauseLabel;
    private static MobileCoachControls pausedOwner;
    private float previousTimeScale;
    private bool previousAudioPause;
    private bool previousInputFreeze;
    public static bool IsPaused => pausedOwner != null;

    public void TogglePause()
    {
        SetPaused(!IsPaused);
    }

    public void SetPaused(bool pause)
    {
        if (pause == (pausedOwner == this)) return;
        if (pause)
        {
            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            previousInputFreeze = input != null && input.Freeze;
            pausedOwner = this;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            if (input != null) input.Freeze = true;
        }
        else
        {
            pausedOwner = null;
            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
            if (input != null) input.Freeze = previousInputFreeze;
        }
        if (pauseLabel != null) pauseLabel.text = pause ? "继续" : "暂停";
    }

    public void BackToSelection()
    {
        Stop();
        SceneManager.LoadScene("CoachingChoose");
    }

    public void BackToEntry()
    {
        Stop();
        SceneManager.LoadScene("CoachModeEntry");
    }

    public void ReturnToPhone()
    {
        Stop();
        if (!SpineFlowTrainingSession.TryReturnToMobile())
            SceneManager.LoadScene("CoachModeEntry");
    }

    public void RestartPractice()
    {
        Stop();
        SceneManager.LoadScene(gameObject.scene.name);
    }

    private void Stop()
    {
        SetPaused(false);
        session?.StopPractice();
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause && session != null) SetPaused(true);
    }

    private void OnDisable()
    {
        SetPaused(false);
    }
}
