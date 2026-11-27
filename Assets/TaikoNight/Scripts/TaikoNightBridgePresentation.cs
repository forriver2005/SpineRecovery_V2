using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TaikoNight
{
    /// <summary>
    /// Presentation bridge for the new dynamic hip-bridge scene. The existing
    /// controller remains responsible for scoring and its start flow. This
    /// component only drives the imported Taiko Night artwork from that clock.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class TaikoNightBridgePresentation : MonoBehaviour
    {
        public HipThrustTaikoController transport;
        public GameWorkoutScene entry;
        public TaikoNightChart chart;
        public Camera presentationCamera;
        public Canvas oldCanvas;
        public TMP_FontAsset font;

        private Canvas overlay;
        private GameObject overlayRoot;
        private TMP_Text scoreText;
        private TMP_Text comboText;
        private TaikoNightCoachCard card;
        private GameObject pausePanel;
        private bool wasRunning;
        private TaikoNightSelection selection;

        private static readonly Color Cyan = new Color(0.49f, 0.89f, 1f, 1f);

        private void Awake()
        {
            if (transport == null) transport = FindObjectOfType<HipThrustTaikoController>();
            if (entry == null) entry = FindObjectOfType<GameWorkoutScene>();
            if (chart == null) chart = GetComponentInChildren<TaikoNightChart>(true);
            transport.RequireLiftForReturnHit = true;
            transport.ExtraRecordedHoldSeconds = TaikoNightChart.AddedHoldSeconds;
            chart.UsePlayerJudgements();
            transport.HitFeedback += chart.ShowPlayerHit;
            BuildOverlay();
            var framing = System.Array.Find(FindObjectsOfType<HipThrustPreviewFraming>(true), f => f.avatar == transport.PreviewAnimator);
            card = gameObject.AddComponent<TaikoNightCoachCard>();
            card.Initialize(overlay, chart, presentationCamera, transport.PreviewAnimator, font, new[] { "抬起", "保持", "放下" }, framing.previewCamera);
            pausePanel.transform.SetAsLastSibling();
            DesignSpace.ExternalNow = 0f;
        }

        private void Start()
        {
            selection = TaikoNightSelection.Create(entry.scorer.UserAnimator, font, "敲鼓臀桥",
                key => {
                    if (key == "hard") entry.menu.SelectHardDifficulty();
                    else if (key == "simple") entry.menu.SelectSimpleDifficulty();
                    else entry.menu.SelectStandardDifficulty();
                }, () => int.TryParse(entry.menu.TimesText.text, out int reps) ? reps : transport.PhaseTimeline.cycles.Length,
                entry.menu.StartTraining, () => entry.OpenWorkoutPanelAbove(2210),
                () => entry.menu.IsFlowTestCalibrationBypassed, () => entry.scorer.CanStartTraining);
        }

        private void OnDestroy()
        {
            if (transport != null && chart != null) transport.HitFeedback -= chart.ShowPlayerHit;
            if (overlayRoot != null) Destroy(overlayRoot);
            if (selection != null) Destroy(selection.gameObject);
            DesignSpace.ExternalNow = null;
        }

        private void LateUpdate()
        {
            bool menuVisible = entry.menu.IsTrainingMenuVisible;
            selection.Show(menuVisible);
            selection.SetGroupRest(GameWorkoutSession.Instance.RestSecondsRemaining);
            if (menuVisible) selection.SyncDifficulty(entry.menu.SelectedDifficultyKey);
            bool session = oldCanvas.gameObject.activeInHierarchy && !menuVisible;
            string count = session ? entry.countdown.DisplayedNumber : null;
            bool running = session && transport.IsRunning;
            bool visible = running || count != null;
            overlayRoot.SetActive(visible);
            presentationCamera.enabled = session;
            if (running != wasRunning)
            {
                wasRunning = running;
                SetPausePanel(false);
                if (running)
                {
                    var reps = transport.AssessmentRepetitions();
                    chart.SetPlayerNotes(System.Array.ConvertAll(reps, rep => new Vector2((float)rep.top, (float)rep.lower)));
                }
                else chart.UsePlayerJudgements();
            }
            if (!visible) { DesignSpace.ExternalNow = 0f; return; }
            var timeline = transport.PhaseTimeline;
            var sample = timeline.Evaluate(transport.SourceTime * timeline.sourceClip.frameRate);
            int stage = running ? sample.phase : -1;
            float remaining = stage < 0 ? 0 : transport.PresentationPhaseDuration(sample) * (1 - sample.progress);
            card.Show(true, count, stage, sample.progress, remaining, transport.IsPaused);
            // Match the artwork's lift and return prompts to the recorded phase boundaries.
            DesignSpace.ExternalNow = running ? (float)transport.Elapsed : 0f;
            chart.SetPlayerTime(DesignSpace.ExternalNow.Value);
            var scoring = transport.RuleScoring;
            chart.SetMeasuredHold(stage == 1 && scoring.InputFresh && scoring.Rule.HoldingCorrectly
                ? scoring.Rule.ValidHoldSeconds / transport.PresentationPhaseDuration(sample) : 0f);
            scoreText.text = running ? transport.Score.ToString() : "0";
            comboText.text = running && transport.RuleScoring.InputFresh ? $"{transport.ConsecutiveHits} COMBO" : "等待动作数据";
        }

        private void BuildOverlay()
        {
            overlayRoot = new GameObject("Taiko Night Bridge HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            overlay = overlayRoot.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = 2200;
            var scaler = overlayRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            scoreText = Text("0", overlayRoot.transform, new Vector2(-80f, -36f), new Vector2(320f, 58f), 38f, TextAlignmentOptions.Right, Color.white);
            comboText = Text("0 COMBO", overlayRoot.transform, new Vector2(-80f, -92f), new Vector2(320f, 42f), 24f, TextAlignmentOptions.Right, Cyan);

            ButtonAt("暂停", overlayRoot.transform, new Vector2(52f, -42f), new Vector2(150f, 58f), () =>
            {
                if (transport != null) transport.TogglePause();
                SetPausePanel(transport != null && transport.IsPaused);
            });
            ButtonAt("返回菜单", overlayRoot.transform, new Vector2(52f, -112f), new Vector2(150f, 58f), () =>
            {
                if (entry != null) entry.StopTraining();
                SetPausePanel(false);
            });

            pausePanel = new GameObject("暂停面板", typeof(RectTransform), typeof(Image));
            pausePanel.transform.SetParent(overlayRoot.transform, false);
            var panelRect = pausePanel.GetComponent<RectTransform>();
            Anchor(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 260f));
            pausePanel.GetComponent<Image>().color = new Color(0.035f, 0.05f, 0.16f, 0.97f);
            Text("训练已暂停", pausePanel.transform, new Vector2(0f, 72f), new Vector2(480f, 60f), 38f, TextAlignmentOptions.Center, Color.white);
            ButtonAt("继续训练", pausePanel.transform, new Vector2(-125f, -40f), new Vector2(220f, 64f), () =>
            {
                if (transport != null) transport.TogglePause();
                SetPausePanel(false);
            });
            ButtonAt("返回菜单", pausePanel.transform, new Vector2(125f, -40f), new Vector2(220f, 64f), () =>
            {
                if (entry != null) entry.StopTraining();
                SetPausePanel(false);
            });
            pausePanel.SetActive(false);
            overlayRoot.SetActive(false);
        }

        private void SetPausePanel(bool visible)
        {
            if (pausePanel != null) pausePanel.SetActive(visible);
        }

        private TMP_Text Text(string value, Transform parent, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject("文字", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            Anchor(rect, new Vector2(1f, 1f), new Vector2(1f, 1f), position, size);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private Button ButtonAt(string value, Transform parent, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(value, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            Anchor(rect, parent == overlayRoot.transform ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f), parent == overlayRoot.transform ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f), position, size);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.08f, 0.24f, 0.38f, 0.96f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var label = Text(value, go.transform, Vector2.zero, size - new Vector2(12f, 8f), 22f, TextAlignmentOptions.Center, Color.white);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            return button;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = (min + max) * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
