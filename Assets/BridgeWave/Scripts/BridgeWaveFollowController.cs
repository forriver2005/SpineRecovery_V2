using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BridgeWave
{
    /// <summary>Sensor-only transport and geometric judgement with the shared training menu.</summary>
    [DefaultExecutionOrder(300)]
    public sealed partial class BridgeWaveFollowController : MonoBehaviour
    {
        public BridgeWaveGame game;
        public BridgeWaveHeightInput input;
        public DeadBugStartMenuController startMenu;
        public BridgeWaveCoachPreview coachPreview;
        [Header("Temporary flow testing")]
        [Tooltip("Skip calibration, device and low-pose admission to test the full flow. No training hits or results are recorded. Disable to restore sensor training.")]
        public bool skipCalibrationForFlowTest;
        public GameObject visualRoot, finishPanel, countdownRoot;
        public TMP_Text calibrationStatus, countdownText, calibrationLabel, startLabel;
        public Text statusText, judgmentText, resultText, pauseLabel, totalCountText;
        public TMP_Text repetitionsText;
        public GameWorkoutScene workout;
        public event Action Completed;
        public bool ReadyForTraining => State == RunState.Ready && input.CanStart && input.Height <= .2f;
        public bool WorkoutRunning => workout != null && GameWorkoutSession.Instance.IsRunning;
        public Button startButton, pauseButton, calibrationButton, replayButton, menuButton;
        public enum RunState { Ready, Countdown, Playing, Paused, Finished }
        public RunState State { get; private set; }
        public BridgeWaveSession Session { get; private set; }
        public TrainingResultPanel ResultPanel { get; private set; }
        public float CountdownRemaining => countdown;
        public bool IsFlowTest => State == RunState.Ready ? skipCalibrationForFlowTest : sessionFlowTest;
        bool Prescribed => SpineRecovery.App.AppBootstrap.Instance?.Training.IsV2Prescription == true;
        public int CompletedRepetitions => Session.Finished ? Session.Repetitions :
            game.standardTrack.Completed(Session.Elapsed, Session.Repetitions);
        public bool CanStart => State == RunState.Ready && (skipCalibrationForFlowTest || ReadyForTraining) &&
            (!Prescribed || (!skipCalibrationForFlowTest &&
                SpineRecovery.App.AppBootstrap.Instance.Training.AllowsPrescriptionGame(gameObject.scene.name))) &&
            (workout == null || GameWorkoutSession.Instance.AllowsStart(workout));
        bool sessionFlowTest;
        float countdown;
        RunState beforePause;

        void Awake()
        {
            ResultPanel = TrainingResultPanel.Create(startLabel.font, ReturnToSetup);
            game.controlledClock = true;
            ApplyPrescriptionDose();
            ResetSession();
            if (SpineRecovery.App.AppBootstrap.Instance?.Training.IsPrescribedTraining == true)
            {
                var guide = startMenu.GetComponentInChildren<GamingGuideButtonController>(true);
                if (guide != null) guide.gameObject.SetActive(false);
            }
            startMenu.TrainingStartAllowed = () => CanStart;
            startMenu.ExternalTrainingStart = () => { Begin(); return State == RunState.Countdown; };
            pauseButton.onClick.AddListener(TogglePause);
            calibrationButton.onClick.AddListener(input.BeginCalibration);
            replayButton.onClick.AddListener(() => { if (!WorkoutRunning) ReturnToSetup(); });
            menuButton.onClick.AddListener(() =>
            {
                if (WorkoutRunning) GameWorkoutSession.Instance.Cancel();
                else ReturnToSetup();
            });
            RefreshHud();
        }
        void OnDestroy()
        {
            if (ResultPanel != null) Destroy(ResultPanel.gameObject);
            if (startMenu != null) { startMenu.TrainingStartAllowed = null; startMenu.ExternalTrainingStart = null; }
        }
        public bool SetRepetitionDose(int repetitions, out string error)
        {
            error = null;
            if (State != RunState.Ready || repetitions < 0 || repetitions > 100)
            { error = "臀桥次数须为 1–100，或 0 使用默认 4 次。"; return false; }
            game.totalCount = repetitions == 0 ? 4 : repetitions;
            ResetSession();
            RefreshHud();
            return true;
        }
        public void ResetSession()
        {
            Session = new BridgeWaveSession(game.standardTrack, game.totalCount, game.tolerance,
                game.speedPx, game.bottomY - game.topY, game.stepPx);
            game.presentationTime = 0;
            game.ResetFeedback();
            ResetFinalScore();
        }
        public void ApplyPrescriptionDose()
        {
            if (Prescribed)
            {
                skipCalibrationForFlowTest = false;
                game.totalCount = SpineFlowTrainingSession.GetPlannedTrainingVolume("game", 1, 4).TotalRepetitions;
            }
        }
        public void Begin()
        {
            input.Read();
            if (!CanStart) return;
            ApplyPrescriptionDose();
            // Repeat the standard chart to the prescribed count; difficulty changes corridor width.
            game.tolerance = startMenu.SelectedDifficultyKey == "simple" ? .11f :
                startMenu.SelectedDifficultyKey == "hard" ? .06f : .08f;
            sessionFlowTest = skipCalibrationForFlowTest;
            input.CancelCalibration();
            ResetSession();
            SpineFlowTrainingSession.BeginGameTraining(startMenu.SelectedDifficultyKey);
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
            ResultPanel.Hide();
            State = RunState.Ready;
            input.EditingAllowed = true;
            input.CancelCalibration();
            ResetSession();
            startMenu.ShowMenu();
            RefreshHud();
        }
        void LateUpdate()
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            input.Read();
            if (State == RunState.Countdown)
            {
                // If the sensor is lost, require a fresh calibrated low pose before the countdown resumes.
                if (IsFlowTest || (input.Valid && input.Height <= .2f)) countdown -= Time.unscaledDeltaTime;
                if (countdown <= 0) State = RunState.Playing;
            }
            if (State == RunState.Playing)
            {
                double before = Session.Elapsed;
                bool pulse = Session.Advance(Time.unscaledDeltaTime, input.Height, input.Valid && !IsFlowTest);
                ObserveFinalScore(before,Session.Elapsed,Time.unscaledDeltaTime);
                game.presentationTime = Session.Elapsed;
                if (pulse) game.PulseHit();
                if (Session.Finished)
                {
                    FinishFinalScore();
                    if (!IsFlowTest)
                        SpineFlowTrainingSession.SaveUnifiedFinalGameAssessment("hip_thrust_curve",
                            startMenu.SelectedDifficultyKey, FinalScore, validScoreSamples,
                            Mathf.CeilToInt(Session.Elapsed), CompletedRepetitions);
                    State = RunState.Finished;
                    Completed?.Invoke();
                }
            }
            game.height = input.Height;
            game.judging = State == RunState.Playing && !IsFlowTest;
            game.inTrack = game.judging && Session.Inside && input.Valid;
            RefreshHud();
        }
        public void RefreshHud()
        {
            bool ready = State == RunState.Ready;
            visualRoot.SetActive(!ready);
            finishPanel.SetActive(false);
            if(State == RunState.Finished) ResultPanel.Show("臀桥",FinalScore,IsFlowTest);
            else ResultPanel.Hide();
            calibrationButton.gameObject.SetActive(!IsFlowTest);
            calibrationStatus.gameObject.SetActive(!IsFlowTest);
            coachPreview.SetTrainingVisible(!ready && State != RunState.Finished);
            calibrationButton.interactable = ready && input.Fresh && !input.Calibrating;
            calibrationLabel.text = input.Calibrating ? "正在校准…" : input.Calibrated ? "重新躺姿校准" : "躺姿校准";
            startLabel.text = IsFlowTest ? "开始游戏" : !input.Fresh ? "请先连接传感器" : !input.Calibrated ? "请先完成躺姿校准" : "开始游戏";
            startButton.interactable = CanStart;
            pauseButton.interactable = State == RunState.Countdown || State == RunState.Playing || State == RunState.Paused;
            pauseLabel.text = State == RunState.Paused ? "▶" : "Ⅱ";
            replayButton.interactable = !WorkoutRunning;
            totalCountText.text = "/ " + game.totalCount;
            repetitionsText.text = game.totalCount.ToString();
            string status = IsFlowTest ? "流程测试 · 已跳过校准，本次不记录成绩" : input.Message;
            if (!IsFlowTest && input.CanStart && input.Height > .2f && (ready || State == RunState.Countdown))
                status = "请回到放平位置，准备开始训练";
            calibrationStatus.text = status;
            statusText.text = status;
            countdownRoot.SetActive(State == RunState.Countdown);
            countdownText.text = Mathf.CeilToInt(Mathf.Max(0, countdown)).ToString();
            if (State == RunState.Countdown) judgmentText.text = IsFlowTest || (input.Valid && input.Height <= .2f) ? "准备开始" : "请连接传感器并回到放平位置";
            else if (State == RunState.Paused) judgmentText.text = "已暂停";
            else if (ready) judgmentText.text = "臀桥";
            else if (State == RunState.Finished) judgmentText.text = "本组完成";
            else if (IsFlowTest) judgmentText.text = "流程测试 · 跟随轨道节奏";
            else if (!input.Valid) judgmentText.text = "信号中断 · 等待动作数据";
            else if (game.PhaseAt(Session.Elapsed) == BridgeWaveGame.Phase.Rest) judgmentText.text = "放平准备";
            else judgmentText.text = game.inTrack ? "HIT · 轨道内" : input.Height > game.Level(Session.Elapsed) ? "偏高 · 缓慢放低" : "偏低 · 适当抬高";
            judgmentText.color = game.inTrack ? new Color(1, .84f, .42f) : Color.white;
        }
    }
}
