using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TaikoNight
{
    public enum TaikoNightActionKind
    {
        Crunch,
        LyingCat
    }

    public static class TaikoNightPracticeClock
    {
        public static bool IsPaused { get; internal set; }
    }

    /// <summary>
    /// Runs the imported recorded-action coach and its package scorer while the
    /// Taiko Night prefab supplies the visual game layer.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TaikoNightActionRuntime : MonoBehaviour
    {
        public TaikoNightActionKind actionKind;
        public DeadBugStartMenuController menu;
        public SegmentedCoachController coach;
        public MotionRecorder recorder;
        public GameObject taikoRoot;
        public Camera presentationCamera;
        public TMP_FontAsset font;
        public string title = "动态训练";

        private ITaikoHoldScorer scorer;
        private ICoachMotionBackend motion;
        private TaikoNightChart chart;
        private TaikoNightCoachCard card;
        private GameObject overlayRoot, pausePanel;
        private Canvas overlayCanvas;
        private Button pauseButton, returnButton;
        private TaikoNightSelection selection;
        private TMP_Text scoreText, holdText;
        private float clock, countdownRemaining;
        private float lastCompletedScore;
        private bool running, countdownRunning;
        private int playbackSegment;
        private string difficultyKey = "standard";
        private Vector3 phaseSeconds;
        private GameWorkoutScene entry;
        private int repetitionCount = 8;
        private int completedRepetitions;
        private float holdExtension, activeSeconds;
        public GameTrainingAssessment Assessment { get; set; }
        public bool IsResultVisible { get; private set; }
        public string DifficultyKey => difficultyKey;
        public float ActiveSeconds => activeSeconds;
        public event Action Completed;
        public bool IsMenuVisible => !running && !countdownRunning;
        public string StartBlockedReason => motion == null || !motion.IsReady ? "动作资源尚未就绪" :
            !HasFreshTracker() ? "等待动作传感器连接" : !IsMenuVisible ? "游戏已开始" : null;
        public bool CanStartTraining => StartBlockedReason == null;
        public bool IsRunning => running;
        public bool IsCountingDown => countdownRunning;
        public int RepetitionCount => repetitionCount;
        public int CompletedRepetitions => completedRepetitions;

        private void Awake()
        {
            chart = taikoRoot.GetComponentInChildren<TaikoNightChart>(true);
            chart.UsePlayerJudgements();
            TaikoNightPracticeClock.IsPaused = false;
            DesignSpace.ExternalNow = 0f;
        }

        private void Start()
        {
            // The copied practice scene's legacy menu initializes after this
            // component. Keep that menu hidden so this scene owns the start flow.
            SetMenuRootVisible(false);
            RestoreMenuHiddenPresentation();
            SetLegacyActionMenuVisible(false);
            // Use the recorded coach and scorer belonging to this action. The copied
            // practice menu and its checkpoint scorer do not drive the drum session.
            var avatar = recorder.CoachAnimator.transform;
            avatar.SetParent(null, false);
            avatar.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            avatar.localScale = Vector3.one;
            entry = FindObjectOfType<GameWorkoutScene>();
            ConfigureAction();
            BuildOverlay();
            card = gameObject.AddComponent<TaikoNightCoachCard>();
            card.Initialize(overlayCanvas, chart, presentationCamera, recorder.CoachAnimator, font,
                actionKind == TaikoNightActionKind.Crunch ? new[] { "卷起", "保持", "回落" } : new[] { "拱背", "保持", "还原" });
            pausePanel.transform.SetAsLastSibling();
            pauseButton.gameObject.SetActive(false);
            returnButton.gameObject.SetActive(false);
            if (overlayCanvas != null) overlayCanvas.enabled = true;
            EnableTaikoRenderers(true);
            overlayRoot.SetActive(true);
            bool crunch = actionKind == TaikoNightActionKind.Crunch;
            selection = TaikoNightSelection.Create(recorder.UserAnimator, font, crunch ? "卷腹" : "卧位猫式",
                crunch ? "卷起　→　保持　→　回落" : "拱背　→　保持　→　还原", SelectDifficulty, () => repetitionCount,
                menu.StartTraining, () => entry.OpenWorkoutPanelAbove(2210), () => menu.IsFlowTestCalibrationBypassed,
                null, () => StartBlockedReason ??
                    (menu.TrainingStartAllowed?.Invoke() == false ? "请等待当前训练流程完成" : null));
            ApplyPrescription();
            overlayRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (scorer != null) scorer.RoundCompleted -= OnRoundCompleted;
            TaikoNightPracticeClock.IsPaused = false;
            DesignSpace.ExternalNow = null;
            if (overlayRoot != null) Destroy(overlayRoot);
            if (selection != null) Destroy(selection.gameObject);
        }

        private void ConfigureAction()
        {
            if (actionKind == TaikoNightActionKind.Crunch)
            {
                var recording = recorder.CoachAnimator.gameObject.AddComponent<RecordedCrunchCoachMotion>();
                recording.Configure(recorder.CoachAnimator, presentationCamera.transform);
                recording.CreateUserReferences(recorder.UserAnimator, out CrunchPose rest, out CrunchPose hold);
                scorer = recorder.UserAnimator.gameObject.GetComponent<CrunchHoldScorer>();
                if (scorer == null) scorer = recorder.UserAnimator.gameObject.AddComponent<CrunchHoldScorer>();
                ((CrunchHoldScorer)scorer).Configure(recorder.UserAnimator, rest, hold);
                ((CrunchHoldScorer)scorer).SetDifficultyMode(HipThrustDifficultyMode.Medium);
                motion = recording;
                phaseSeconds = RecordedCrunchCoachMotion.PhaseSeconds;
            }
            else
            {
                var recording = recorder.CoachAnimator.gameObject.AddComponent<RecordedLyingCatCoachMotion>();
                recording.Configure(recorder.CoachAnimator, presentationCamera.transform);
                recording.CreateUserReferences(recorder.UserAnimator, out LyingCatPose rest, out LyingCatPose hold);
                scorer = recorder.UserAnimator.gameObject.GetComponent<LyingCatHoldScorer>();
                if (scorer == null) scorer = recorder.UserAnimator.gameObject.AddComponent<LyingCatHoldScorer>();
                ((LyingCatHoldScorer)scorer).Configure(recorder.UserAnimator, rest, hold);
                ((LyingCatHoldScorer)scorer).SetDifficultyMode(HipThrustDifficultyMode.Medium);
                motion = recording;
                phaseSeconds = RecordedLyingCatCoachMotion.PhaseSeconds;
            }

            holdExtension = TaikoNightChart.AddedHoldSeconds;
            phaseSeconds.y += holdExtension;
            scorer.RoundCompleted += OnRoundCompleted;
        }

        private void ApplyPrescription()
        {
            var training = SpineRecovery.App.AppBootstrap.Instance?.Training;
            if (training?.IsV2Prescription != true) return;
            SelectDifficulty(training.CurrentPrescriptionDifficulty);
            repetitionCount = SpineFlowTrainingSession.GetPlannedTrainingVolume("game", 1, 1).TotalRepetitions;
            float recordedHold = phaseSeconds.y - holdExtension;
            phaseSeconds.y = SpineFlowTrainingSession.PlannedPrescriptionGameHoldSeconds;
            holdExtension = phaseSeconds.y - recordedHold;
            scorer.SetTargetHoldSeconds(phaseSeconds.y);
            selection.SyncDifficulty(difficultyKey);
        }

        private void OnRoundCompleted(float score)
        {
            if (!running) return;
            lastCompletedScore = score;
            if (scorer.TrackingAvailable)
                Assessment?.RecordTaikoRound(completedRepetitions, scorer.ValidHoldSeconds, scorer.TargetHoldSeconds);
            if (running && scorer.TrackingAvailable && scorer.ValidHoldSeconds >= scorer.TargetHoldSeconds)
                chart.ShowPlayerHit(score >= 90f);
        }

        public bool StartFromMenu()
        {
            if (!CanStartTraining) return false;
            completedRepetitions = 0;
            IsResultVisible = false;
            activeSeconds = 0;
            lastCompletedScore = 0f;
            clock = 0f;
            countdownRemaining = DeadBugCountdownController.DefaultCountdownSeconds;
            countdownRunning = true;
            motion.ReturnIdle();
            chart.UsePlayerJudgements();
            scoreText.text = "0";
            holdText.text = "等待动作数据";
            chart.SetPlayerNotes(new[] { new Vector2(phaseSeconds.x, phaseSeconds.x + phaseSeconds.y) });
            selection.SetResult("");
            selection.Show(false);
            overlayRoot.SetActive(true);
            returnButton.gameObject.SetActive(true);
            return true;
        }

        private void StopTraining()
        {
            if (running && Assessment != null && Assessment.IsCollecting) Assessment.Finish();
            IsResultVisible = false;
            TaikoNightPracticeClock.IsPaused = running = countdownRunning = false;
            scorer.CancelSession();
            motion.ReturnIdle();
            chart.UsePlayerJudgements();
            DesignSpace.ExternalNow = 0f;
            pauseButton.gameObject.SetActive(false);
            returnButton.gameObject.SetActive(false);
            overlayRoot.SetActive(false);
            selection.Show(true);
            SetPausePanel(false);
            scoreText.text = "0";
            holdText.text = title;
        }

        public void CancelTraining() => StopTraining();

        public void SetWorkoutDifficulty(string key)
        {
            SelectDifficulty(key);
            selection?.SyncDifficulty(difficultyKey);
            SetMenuRootVisible(false);
            RestoreMenuHiddenPresentation();
        }

        public bool SetRepetitionDose(int count, out string error)
        {
            error = null;
            if (SpineRecovery.App.AppBootstrap.Instance?.Training.IsV2Prescription == true)
            { error = "处方训练次数由医生指定。"; return false; }
            if (!IsMenuVisible) error = "请先返回菜单，再更换动作次数。";
            else if (count < 0 || count > 100) error = "动作次数须为 0–100。";
            if (error != null) return false;
            repetitionCount = count == 0 ? DefaultRepetitions(difficultyKey) : count;
            selection.SyncDifficulty(difficultyKey);
            return true;
        }

        public void SetGroupRest(float seconds) => selection?.SetGroupRest(seconds);

        private static int DefaultRepetitions(string key) => key == "simple" ? 4 : key == "hard" ? 12 : 8;

        private bool HasFreshTracker()
        {
            if (recorder == null || recorder.UserAnimator == null) return false;
            foreach (var candidate in FindObjectsOfType<AvatarMotionSource>())
                if (candidate.Model == recorder.UserAnimator.gameObject && candidate.isActiveAndEnabled && candidate.HasFreshFrame) return true;
            return false;
        }

        private void Update()
        {
            if (countdownRunning)
            {
                countdownRemaining -= Time.deltaTime;
                if (countdownRemaining > 0) return;
                countdownRunning = false;
                running = true;
                scorer.BeginContinuousSession();
                SpineFlowTrainingSession.BeginGameTraining(difficultyKey);
                Assessment?.BeginTaiko(repetitionCount, phaseSeconds, scorer.TargetHoldSeconds, difficultyKey);
                motion.PlaySegment(1);
                playbackSegment = 1;
                pauseButton.gameObject.SetActive(true);
                return;
            }
            if (!running || TaikoNightPracticeClock.IsPaused) return;
            float previous = clock;
            clock += Time.deltaTime;
            activeSeconds += Time.deltaTime;
            float holding = Mathf.Max(0, Mathf.Min(clock, phaseSeconds.x + phaseSeconds.y) - Mathf.Max(previous, phaseSeconds.x));
            Assessment?.ObserveTaiko(completedRepetitions, Time.deltaTime,
                HasFreshTracker() && scorer.TrackingAvailable, scorer.CurrentLiveComparisonScore, Mathf.Max(0, Time.deltaTime - holding));
            // Slow only the recorded plateau, including frames straddling either boundary.
            double top = phaseSeconds.x, lower = top + phaseSeconds.y;
            double extraBefore = HipThrustPhaseTimeline.ConsumedHoldExtension(previous, top, lower, holdExtension);
            double extraNow = HipThrustPhaseTimeline.ConsumedHoldExtension(clock, top, lower, holdExtension);
            motion.Tick((float)(clock - previous - (extraNow - extraBefore)));
            if (motion.HasReachedSegmentEnd())
            {
                if (playbackSegment == 1) { playbackSegment = 2; motion.PlaySegment(2); }
                else
                {
                    completedRepetitions++;
                    if (completedRepetitions < repetitionCount)
                    {
                        clock = 0f;
                        motion.ReturnIdle();
                        motion.PlaySegment(1);
                        playbackSegment = 1;
                        chart.UsePlayerJudgements();
                        chart.SetPlayerNotes(new[] { new Vector2(phaseSeconds.x, phaseSeconds.x + phaseSeconds.y) });
                        return;
                    }
                    Assessment?.Finish();
                    StopTraining();
                    selection.Show(false);
                    IsResultVisible = true;
                    Completed?.Invoke();
                }
            }
        }

        private void LateUpdate()
        {
            int stage = -1;
            float remaining = 0, progress = 0;
            if (running)
            {
                stage = playbackSegment == 2 ? 2 : clock < phaseSeconds.x ? 0 : 1;
                float end = stage == 0 ? phaseSeconds.x : stage == 1 ? phaseSeconds.x + phaseSeconds.y : phaseSeconds.x + phaseSeconds.y + phaseSeconds.z;
                remaining = Mathf.Max(0, end - clock);
                progress = phaseSeconds[stage] > 0 ? Mathf.Clamp01(1f - remaining / phaseSeconds[stage]) : 1f;
                float displayedScore = scorer.CurrentLiveComparisonScore > 0f
                    ? scorer.CurrentLiveComparisonScore : lastCompletedScore;
                scoreText.text = Mathf.FloorToInt(displayedScore).ToString();
                holdText.text = scorer.TrackingAvailable ? $"有效保持 {scorer.ValidHoldSeconds:0.0} / {scorer.TargetHoldSeconds:0.0} 秒" : "等待动作数据";
            }
            DesignSpace.ExternalNow = running ? clock : 0f;
            chart.SetPlayerTime(running ? clock : 0f);
            chart.SetMeasuredHold(running && stage == 1 && scorer.TrackingAvailable && scorer.CurrentLiveComparisonScore > 0f
                ? (scorer.TargetHoldSeconds > 0 ? scorer.ValidHoldSeconds / scorer.TargetHoldSeconds : 1f) : 0f);
            string count = countdownRunning ? Mathf.CeilToInt(countdownRemaining).ToString() : null;
            card.Show(running || countdownRunning, count, stage, progress, remaining, TaikoNightPracticeClock.IsPaused);
        }

        private void BuildOverlay()
        {
            overlayRoot = new GameObject("Taiko Night Action HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = overlayRoot.GetComponent<Canvas>();
            overlayCanvas = canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2200;
            var scaler = overlayRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            scoreText = Text("0", overlayRoot.transform, new Vector2(-80f, -36f), new Vector2(320f, 58f), 38f, TextAlignmentOptions.Right, Color.white);
            holdText = Text(title, overlayRoot.transform, new Vector2(-80f, -92f), new Vector2(320f, 42f), 22f, TextAlignmentOptions.Right, new Color(0.49f, 0.89f, 1f));
            pauseButton = ButtonAt("暂停", overlayRoot.transform, new Vector2(52f, -42f), new Vector2(150f, 58f), TogglePause);
            returnButton = ButtonAt("返回菜单", overlayRoot.transform, new Vector2(52f, -112f), new Vector2(170f, 58f), StopTraining);
            pausePanel = new GameObject("暂停面板", typeof(RectTransform), typeof(Image));
            pausePanel.transform.SetParent(overlayRoot.transform, false);
            var panelRect = pausePanel.GetComponent<RectTransform>();
            Anchor(panelRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 260f));
            pausePanel.GetComponent<Image>().color = new Color(0.035f, 0.05f, 0.16f, 0.97f);
            Text("训练已暂停", pausePanel.transform, new Vector2(0f, 68f), new Vector2(480f, 60f), 38f, TextAlignmentOptions.Center, Color.white);
            ButtonAt("继续训练", pausePanel.transform, new Vector2(-125f, -40f), new Vector2(220f, 64f), TogglePause);
            ButtonAt("返回菜单", pausePanel.transform, new Vector2(125f, -40f), new Vector2(220f, 64f), StopTraining);
            pausePanel.SetActive(false);

            overlayRoot.SetActive(true);

        }

        private void SelectDifficulty(string key)
        {
            if (running || countdownRunning) return;
            var training = SpineRecovery.App.AppBootstrap.Instance?.Training;
            if (training?.IsV2Prescription == true) key = training.CurrentPrescriptionDifficulty;
            difficultyKey = key == "simple" || key == "hard" ? key : "standard";
            HipThrustDifficultyMode mode = difficultyKey == "simple" ? HipThrustDifficultyMode.Easy :
                difficultyKey == "hard" ? HipThrustDifficultyMode.Hard : HipThrustDifficultyMode.Medium;
            scorer.SetDifficultyMode(mode);
        }

        private void TogglePause()
        {
            if (!running) return;
            TaikoNightPracticeClock.IsPaused = !TaikoNightPracticeClock.IsPaused;
            if (TaikoNightPracticeClock.IsPaused) motion.Freeze(); else motion.Resume();
            SetPausePanel(TaikoNightPracticeClock.IsPaused);
        }

        private TMP_Text Text(string value, Transform parent, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject("文字", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            Anchor(rect, parent == overlayRoot.transform ? new Vector2(1f, 1f) : new Vector2(0.5f, 0.5f), position, size);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = fontSize; text.color = color; text.alignment = alignment; text.raycastTarget = false;
            return text;
        }

        private Button ButtonAt(string value, Transform parent, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(value, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            Anchor(rect, parent == overlayRoot.transform ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f), position, size);
            var image = go.GetComponent<Image>(); image.color = new Color(0.08f, 0.24f, 0.38f, 0.96f);
            var button = go.GetComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            var label = Text(value, go.transform, Vector2.zero, size - new Vector2(12f, 8f), 22f, TextAlignmentOptions.Center, Color.white);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            return button;
        }

        private void SetMenuRootVisible(bool visible)
        {
            if (menu == null) return;
            var field = typeof(DeadBugStartMenuController).GetField("menuRoot", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.GetValue(menu) is GameObject root) root.SetActive(visible);
        }

        private static void SetLegacyActionMenuVisible(bool visible)
        {
            foreach (Transform transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || transform.gameObject.scene.IsValid() == false ||
                    !string.Equals(transform.name, "ActionMenu", System.StringComparison.Ordinal)) continue;
                transform.gameObject.SetActive(visible);
            }
        }

        private void RestoreMenuHiddenPresentation()
        {
            if (menu == null) return;
            InvokePrivate(menu, "RestoreModelsHiddenByMenu");
            InvokePrivate(menu, "RestoreSceneVisualsHiddenByMenu");
            InvokePrivate(menu, "RestoreOccludedWorldRoots");
        }

        private static void InvokePrivate(object target, string method)
        {
            if (target == null) return;
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info != null) info.Invoke(target, null);
        }

        private void EnableTaikoRenderers(bool visible)
        {
            if (taikoRoot == null) return;
            foreach (var sprite in taikoRoot.GetComponentsInChildren<SpriteRenderer>(true)) sprite.enabled = visible;
            foreach (var line in taikoRoot.GetComponentsInChildren<LineRenderer>(true)) line.enabled = visible;
        }

        private void SetPausePanel(bool visible)
        {
            if (pausePanel != null) pausePanel.SetActive(visible);
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
