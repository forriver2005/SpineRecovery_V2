using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime presentation shared by the six formal Practice scenes. It keeps
/// patient-facing lesson controls and hold countdown visuals out of the
/// serialized scene variants so the same behavior cannot drift by gender.
/// </summary>
public sealed class CoachFlowPresentation : MonoBehaviour
{
    private const string ChineseFontResourcePath =
        "CoachFlow/SourceHanSansSC-Heavy SDF";
    private static readonly Dictionary<string, string> TechniqueCards =
        new Dictionary<string, string>
        {
            { "deadbug", "死虫式在仰卧位训练躯干抗伸展控制和对侧肢体协调。腹部轻收，保持腰背稳定；缓慢伸展对侧手臂和腿，回位时避免骨盆晃动。主要由腹横肌、腹直肌等躯干稳定肌群控制，并要求髋部协调发力。" },
            { "birddog", "鸟狗式在四点支撑位训练躯干抗旋转能力、髋部控制和对侧肩髋协调。腹部收紧，脊柱保持中立；缓慢伸展对侧手臂和腿，避免塌腰或骨盆旋转。动作中由臀大肌、背部伸肌及肩胛稳定肌群共同参与。" },
            { "hipthrust", "臀桥训练髋关节伸展和臀大肌发力，同时练习躯干稳定。双脚稳定压地，腹部轻收，由臀部抬起骨盆；避免用腰椎过度后仰代替髋伸展。回落时保持控制，主要感受臀大肌和腘绳肌的协同发力。" },
            { "catcamel", "卧位猫式通过小幅脊柱屈伸改善胸腰椎活动度，练习背部和腹部肌群的协调控制。四点支撑下随呼吸缓慢拱背，再回到中立位；动作范围以舒适为准，不追求极限弯曲或塌腰。" },
            { "sidebridge", "侧桥训练躯干抗侧屈能力和髋部侧向稳定。保持肩部、躯干和骨盆成线，由支撑侧躯干抬起骨盆并受控回落。主要训练腹斜肌、腹横肌、腰方肌和竖脊肌，同时要求髋部外侧稳定。" },
            { "curlup", "改良卷腹强调上背部小幅离地，用于训练腹直肌和腹部抗伸展控制，同时减少腰椎屈曲和颈部代偿。保持颈部放松，收紧腹部后抬起肩胛下角，再缓慢回落，避免用手拉颈部。" }
        };

    private PracticeSessionController session;
    private SegmentedCoachController coach;
    private Canvas canvas;
    private GameObject lessonRoot;
    private GameObject lessonCard;
    private TMP_Text lessonTitle;
    private TMP_Text lessonText;
    private Button skipButton;
    private GameObject dimRoot;
    private GameObject countdownRoot;
    private TMP_Text countdownText;
    private bool lessonVisible;
    private int lastCountdown = -1;
    private bool initialized;
    private float nextPauseLayoutAt;

    public void Initialize(PracticeSessionController owner, SegmentedCoachController controller)
    {
        session = owner;
        coach = controller;
        EnsureVisuals();
    }

    private void Start()
    {
        if (session == null) session = FindObjectOfType<PracticeSessionController>(true);
        if (coach == null) coach = FindObjectOfType<SegmentedCoachController>(true);
        EnsureVisuals();
        ShowLessonStage(false);
    }

    private void Update()
    {
        if (!initialized || coach == null)
        {
            return;
        }

        if (Time.unscaledTime >= nextPauseLayoutAt)
        {
            PositionAuthoredPauseButtons();
            nextPauseLayoutAt = Time.unscaledTime + 0.5f;
        }

        bool showHold = coach.HasStarted && !coach.IsComplete &&
            coach.IsHoldingAtSegment &&
            coach.CurrentSegmentKind == TrainingSegmentKind.CoreAction &&
            coach.CurrentRepeat >= 2;
        if (!showHold)
        {
            dimRoot?.SetActive(false);
            countdownRoot?.SetActive(false);
            lastCountdown = -1;
            return;
        }

        dimRoot?.SetActive(true);
        countdownRoot?.SetActive(true);
        float remaining = coach.CurrentHoldRemaining;
        // Self-practice keeps the full hold window for the patient. Show the
        // visual 3/2/1 only in its final three seconds so each number owns a
        // stable one-second interval.
        if (remaining > 3f)
        {
            dimRoot?.SetActive(false);
            countdownRoot?.SetActive(false);
            lastCountdown = -1;
            return;
        }
        int count = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0.01f, remaining)), 1, 3);
        if (count != lastCountdown)
        {
            lastCountdown = count;
            if (countdownText != null) countdownText.text = count.ToString();
            FindObjectOfType<VoicePromptManager>(true)?.PlayGuidanceBeat();
        }
    }

    public void ShowLessonStage(bool visible)
    {
        if (lessonRoot == null || canvas == null)
        {
            initialized = false;
        }
        EnsureVisuals();
        lessonVisible = visible;
        lessonRoot?.SetActive(visible);
        if (!visible)
        {
            dimRoot?.SetActive(false);
            countdownRoot?.SetActive(false);
            lastCountdown = -1;
            return;
        }

        string action = VoicePromptManager.GetPracticeActionKey();
        if (lessonTitle != null) lessonTitle.text = "动作介绍";
        if (lessonText != null)
        {
            lessonText.text = TechniqueCards.TryGetValue(action ?? string.Empty, out string tips)
                ? tips
                : "请按教练示范完成动作，保持呼吸自然并在舒适范围内控制幅度。";
        }
    }

    private void EnsureVisuals()
    {
        if (initialized && lessonRoot != null && canvas != null) return;
        // Existing Practice scenes contain several world-space canvases whose
        // cameras and activation state differ by scene. Use a dedicated
        // screen-space canvas so the lesson card and skip control always land
        // in the player's GameView.
        var canvasObject = new GameObject("CoachFlowCanvas", typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasObject.GetComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObject.GetComponent<CanvasScaler>().referenceResolution =
            new Vector2(1920f, 1080f);

        lessonRoot = new GameObject("CoachLessonPresentation", typeof(RectTransform));
        lessonRoot.transform.SetParent(canvas.transform, false);
        var lessonRect = lessonRoot.GetComponent<RectTransform>();
        lessonRect.anchorMin = Vector2.zero;
        lessonRect.anchorMax = Vector2.one;
        lessonRect.offsetMin = Vector2.zero;
        lessonRect.offsetMax = Vector2.zero;

        lessonCard = CreatePanel("TechniqueCard", lessonRoot.transform,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(500f, 0f), new Vector2(820f, 640f),
            Color.clear);
        // Keep the lesson prose directly on the scene background. The old
        // opaque card made the left side feel clipped and reduced the usable
        // reading width on the headset view.
        lessonCard.GetComponent<Image>().color = Color.clear;
        lessonTitle = CreateText("TechniqueTitle", lessonCard.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(18f, -86f), new Vector2(-18f, -18f), 42f,
            Color.white, TextAlignmentOptions.Center);
        lessonText = CreateText("TechniqueText", lessonCard.transform,
            new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(38f, 34f), new Vector2(-38f, -104f), 46f,
            new Color(0.87f, 0.96f, 1f), TextAlignmentOptions.Left);

        GameObject buttonObject = CreatePanel("SkipLessonButton", lessonRoot.transform,
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-240f, -40f), new Vector2(180f, 62f),
            new Color(0.06f, 0.25f, 0.37f, 0.95f));
        RectTransform skipRect = buttonObject.GetComponent<RectTransform>();
        skipRect.pivot = new Vector2(1f, 1f);
        skipRect.anchoredPosition = new Vector2(-240f, -40f);
        skipRect.sizeDelta = new Vector2(180f, 62f);
        skipButton = buttonObject.AddComponent<Button>();
        skipButton.targetGraphic = buttonObject.GetComponent<Image>();
        buttonObject.GetComponent<Image>().raycastTarget = true;
        skipButton.onClick.AddListener(() => session?.SkipPracticeIntroduction());
        CreateText("SkipLabel", buttonObject.transform,
            Vector2.zero, Vector2.one, new Vector2(4f, 2f), new Vector2(-4f, -2f),
            25f, Color.white, TextAlignmentOptions.Center).text = "跳过讲解";

        dimRoot = CreatePanel("CoachHoldDim", canvas.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            new Color(0f, 0f, 0f, 0.42f));
        dimRoot.transform.SetAsFirstSibling();

        countdownRoot = new GameObject("CoachHoldCountdown", typeof(RectTransform));
        countdownRoot.transform.SetParent(canvas.transform, false);
        var countRect = countdownRoot.GetComponent<RectTransform>();
        countRect.anchorMin = new Vector2(0.5f, 0.5f);
        countRect.anchorMax = new Vector2(0.5f, 0.5f);
        countRect.sizeDelta = new Vector2(360f, 260f);
        countdownText = CreateText("CountdownText", countdownRoot.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 150f,
            Color.white, TextAlignmentOptions.Center);
        TMP_FontAsset sceneFont = Resources.Load<TMP_FontAsset>(ChineseFontResourcePath) ??
            FindChineseSceneFont();
        if (sceneFont != null)
        {
            lessonTitle.font = sceneFont;
            lessonText.font = sceneFont;
            countdownText.font = sceneFont;
            TMP_Text skipLabel = buttonObject.GetComponentInChildren<TMP_Text>(true);
            if (skipLabel != null) skipLabel.font = sceneFont;
        }
        lessonText.enableWordWrapping = true;
        lessonText.fontStyle = FontStyles.Normal;
        lessonText.lineSpacing = 12f;
        PositionAuthoredPauseButtons();
        initialized = true;
        lessonRoot.SetActive(false);
        dimRoot.SetActive(false);
        countdownRoot.SetActive(false);
    }

    private static TMP_FontAsset FindChineseSceneFont()
    {
        TMP_Text[] texts = FindObjectsOfType<TMP_Text>(true);
        TMP_FontAsset fallback = null;
        foreach (TMP_Text text in texts)
        {
            TMP_FontAsset font = text != null ? text.font : null;
            if (font == null) continue;
            if (font.name.IndexOf("SourceHan", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                font.name.IndexOf("Noto", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return font;
            }
            if (fallback == null &&
                font.name.IndexOf("Liberation", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                fallback = font;
            }
        }
        return fallback;
    }

    private static void PositionAuthoredPauseButtons()
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        foreach (Button button in buttons)
        {
            if (button == null || button.gameObject.name != "Pause" &&
                button.gameObject.name != "MobilePause")
            {
                continue;
            }

            RectTransform rect = button.GetComponent<RectTransform>();
            if (rect == null) continue;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-40f, -40f);
            rect.sizeDelta = new Vector2(180f, 62f);

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.fontSize = 36f;
            }
        }
    }

    private static GameObject CreatePanel(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color color)
    {
        var objectRoot = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        objectRoot.transform.SetParent(parent, false);
        var rect = objectRoot.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = objectRoot.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return objectRoot;
    }

    private static TMP_Text CreateText(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
        float fontSize, Color color, TextAlignmentOptions alignment)
    {
        var objectRoot = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        objectRoot.transform.SetParent(parent, false);
        var rect = objectRoot.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        var text = objectRoot.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        return text;
    }

}
