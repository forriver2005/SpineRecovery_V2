using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeWave
{
    /// <summary>Sensor-only transport and geometric judgement with the shared training menu.</summary>
    [DefaultExecutionOrder(300)]
    public sealed class BridgeWaveFollowController : MonoBehaviour
    {
        public BridgeWaveGame game;
        public BridgeWaveHeightInput input;
        public DeadBugStartMenuController startMenu;
        public BridgeWaveCoachPreview coachPreview;
        public GameObject visualRoot, finishPanel, countdownRoot;
        public TMP_Text calibrationStatus, countdownText, calibrationLabel, startLabel;
        public Text statusText, scoreText, comboText, judgmentText, resultText, pauseLabel;
        public Button startButton, pauseButton, calibrationButton, replayButton, menuButton;
        public enum RunState { Ready, Countdown, Playing, Paused, Finished }
        public RunState State { get; private set; }
        public BridgeWaveSession Session { get; private set; }
        public float CountdownRemaining => countdown;
        public bool CanStart => State == RunState.Ready && input.CanStart && input.Height <= .2f;
        float countdown;
        RunState beforePause;

        void Awake()
        {
            game.controlledClock = true;
            ResetSession();
            startMenu.TrainingStartAllowed = () => CanStart;
            startMenu.ExternalTrainingStart = () => { Begin(); return State == RunState.Countdown; };
            pauseButton.onClick.AddListener(TogglePause);
            calibrationButton.onClick.AddListener(input.BeginCalibration);
            replayButton.onClick.AddListener(ReturnToSetup);
            menuButton.onClick.AddListener(ReturnToSetup);
        }
        void OnDestroy()
        {
            if (startMenu != null) { startMenu.TrainingStartAllowed = null; startMenu.ExternalTrainingStart = null; }
        }
        public void ResetSession()
        {
            Session = new BridgeWaveSession(game.standardTrack, game.totalCount, game.tolerance,
                game.speedPx, game.bottomY - game.topY, game.stepPx);
            game.presentationTime = 0;
            game.ResetFeedback();
        }
        public void Begin()
        {
            input.Read();
            if (!CanStart) return;
            // The four standard cycles stay fixed; difficulty changes the visible corridor and judgement together.
            game.tolerance = startMenu.SelectedDifficultyKey == "simple" ? .11f :
                startMenu.SelectedDifficultyKey == "hard" ? .06f : .08f;
            ResetSession();
            countdown = DeadBugCountdownController.DefaultCountdownSeconds;
            State = RunState.Countdown;
            input.EditingAllowed = false;
            startMenu.gameObject.SetActive(false);
            visualRoot.SetActive(true);
            RefreshHud();
        }
        public void TogglePause()
        {
            if (State == RunState.Paused) { State = beforePause; Session.Pause(false); }
            else if (State == RunState.Playing || State == RunState.Countdown)
            { beforePause = State; State = RunState.Paused; Session.Pause(true); }
            game.judging = game.inTrack = false;
            RefreshHud();
        }
        public void ReturnToSetup()
        {
            State = RunState.Ready;
            input.EditingAllowed = true;
            input.CancelCalibration();
            ResetSession();
            startMenu.ShowMenu();
            coachPreview.RestartPreview();
            RefreshHud();
        }
        void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            input.Read();
            if (State == RunState.Countdown)
            {
                // If the sensor is lost, require a fresh calibrated low pose before the countdown resumes.
                if (input.Valid && input.Height <= .2f) countdown -= Time.unscaledDeltaTime;
                if (countdown <= 0) State = RunState.Playing;
            }
            if (State == RunState.Playing)
            {
                bool pulse = Session.Advance(Time.unscaledDeltaTime, input.Height, input.Valid);
                game.presentationTime = Session.Elapsed;
                if (pulse) game.PulseHit();
                if (Session.Finished) State = RunState.Finished;
            }
            game.height = input.Height;
            game.judging = State == RunState.Playing;
            game.inTrack = game.judging && Session.Inside && input.Valid;
            RefreshHud();
        }
        public void RefreshHud()
        {
            bool ready = State == RunState.Ready;
            visualRoot.SetActive(!ready);
            finishPanel.SetActive(State == RunState.Finished);
            calibrationButton.interactable = ready && input.Fresh && !input.Calibrating;
            calibrationLabel.text = input.Calibrating ? "正在校准…" : input.Calibrated ? "重新躺姿校准" : "躺姿校准";
            startLabel.text = !input.Fresh ? "请先连接传感器" : !input.Calibrated ? "请先完成躺姿校准" : "开始训练";
            startButton.interactable = CanStart;
            pauseButton.interactable = State == RunState.Countdown || State == RunState.Playing || State == RunState.Paused;
            pauseLabel.text = State == RunState.Paused ? "▶" : "Ⅱ";
            scoreText.text = Session.Hits.ToString("D4") + " HIT";
            comboText.text = Session.Combo + " COMBO";
            string status = input.Message;
            if (input.CanStart && input.Height > .2f && (ready || State == RunState.Countdown))
                status = "请回到放平位置，准备开始训练";
            calibrationStatus.text = status;
            statusText.text = status;
            countdownRoot.SetActive(State == RunState.Countdown);
            countdownText.text = Mathf.CeilToInt(Mathf.Max(0, countdown)).ToString();
            if (State == RunState.Countdown) judgmentText.text = input.Valid && input.Height <= .2f ? "准备开始" : "请连接传感器并回到放平位置";
            else if (State == RunState.Paused) judgmentText.text = "已暂停";
            else if (ready) judgmentText.text = "臀桥 · 曲线跟随";
            else if (State == RunState.Finished) judgmentText.text = "本组完成";
            else if (!input.Valid) judgmentText.text = "信号中断 · 等待动作数据";
            else if (game.PhaseAt(Session.Elapsed) == BridgeWaveGame.Phase.Rest) judgmentText.text = "放平准备";
            else judgmentText.text = game.inTrack ? "HIT · 轨道内" : input.Height > game.Level(Session.Elapsed) ? "偏高 · 缓慢放低" : "偏低 · 适当抬高";
            judgmentText.color = game.inTrack ? new Color(1, .84f, .42f) : Color.white;
            float percent = Session.SampledSeconds > 0 ? Session.InTrackSeconds / Session.SampledSeconds * 100 : 0;
            resultText.text = "本组训练完成\n\n" +
                $"轨道完成 {game.totalCount} 次   ·   轨道内 {percent:0}%\n有效跟随 {Session.InTrackSeconds:0.0} 秒   ·   {Session.Hits} HIT";
        }
    }
}
