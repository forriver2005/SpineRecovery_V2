using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Non-interactive coach-mode progress indicator. Attach this to the existing
// follow-view feedback canvas so it remains visible without affecting gaze UI.
public class CoachSetProgressUI : MonoBehaviour
{
    [SerializeField] private SegmentedCoachController coachController;
    [SerializeField] private RectTransform progressRoot;
    [SerializeField] private TMP_Text progressText;

    [Header("Layout")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(0f, 180f);
    [SerializeField] private Vector2 size = new Vector2(450f, 96f);
    [SerializeField] private float fontSize = 34f;
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.22f);

    private void Awake()
    {
        ResolveController();
        EnsureVisuals();
        HideProgress();
    }

    private void OnEnable()
    {
        ResolveController();
        Subscribe();

        if (coachController != null && coachController.HasStarted && !coachController.IsComplete)
        {
            ShowProgress(coachController.CompletedSets, coachController.TotalSets);
        }
        else
        {
            HideProgress();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void ResolveController()
    {
        if (coachController == null)
        {
            coachController = FindObjectOfType<SegmentedCoachController>(true);
        }
    }

    private void Subscribe()
    {
        if (coachController == null)
        {
            return;
        }

        coachController.OnSetProgressChanged -= HandleSetProgressChanged;
        coachController.OnSetProgressChanged += HandleSetProgressChanged;
        coachController.OnSegmentStarted -= HandleSegmentStarted;
        coachController.OnSegmentStarted += HandleSegmentStarted;
        coachController.OnAllComplete -= HideProgress;
        coachController.OnAllComplete += HideProgress;
    }

    private void Unsubscribe()
    {
        if (coachController == null)
        {
            return;
        }

        coachController.OnSetProgressChanged -= HandleSetProgressChanged;
        coachController.OnSegmentStarted -= HandleSegmentStarted;
        coachController.OnAllComplete -= HideProgress;
    }

    private string previousPhase;
    private void Update()
    {
        if (coachController == null || !coachController.HasStarted || coachController.IsComplete) return;
        string phase = $"{coachController.CurrentRepeat}:{coachController.CompletedSets}:{coachController.CurrentSegmentKind}:{coachController.IsHoldingAtSegment}:{coachController.IsPreparingRepetition}";
        if (phase == previousPhase) return;
        previousPhase = phase;
        ShowProgress(coachController.CompletedSets, coachController.TotalSets, coachController.CurrentRepetitionTarget);
    }

    public void ShowLessonStage(string stage)
    {
        EnsureVisuals();
        string action = VoicePromptManager.GetPracticeActionKey();
        string name = action == "deadbug" ? "死虫" : action == "birddog" ? "鸟狗" :
            action == "hipthrust" ? "臀桥" : action == "catcamel" ? "卧位猫式" :
            action == "sidebridge" ? "侧桥" : action == "curlup" ? "卷腹" : "动作课程";
        progressText.text = stage == "动作讲解"
            ? GetLessonActionTitle(action)
            : $"{name} · {stage}";
        progressRoot.gameObject.SetActive(true);
    }

    private void HandleSetProgressChanged(int completedSets, int totalSets)
    {
        if (totalSets <= 0 ||
            coachController == null ||
            !coachController.HasStarted ||
            coachController.IsComplete)
        {
            HideProgress();
            return;
        }

        ShowProgress(completedSets, totalSets);
    }

    private void HandleSegmentStarted(PracticeSegmentPlaybackInfo info)
    {
        if (coachController == null || !coachController.HasStarted ||
            coachController.IsComplete)
        {
            HideProgress();
            return;
        }

        ShowProgress(
            coachController.CompletedSets,
            coachController.TotalSets,
            info.guidanceLimbs);
    }

    private void ShowProgress(
        int completedSets,
        int totalSets,
        CoachGuidanceLimb guidanceLimbs = CoachGuidanceLimb.None)
    {
        EnsureVisuals();

        int safeTotal = Mathf.Max(1, totalSets);
        int safeCompleted = Mathf.Clamp(completedSets, 0, safeTotal);
        int currentSet = Mathf.Min(safeCompleted + 1, safeTotal);
        int safeRepetitions = Mathf.Max(1, coachController.RepetitionsPerSet);
        int currentRepetition = Mathf.Clamp(
            coachController.CurrentRepeat + 1,
            1,
            safeRepetitions);
        string phaseLabel = coachController.IsPreparingRepetition ? "准备" :
            coachController.CurrentSegmentKind == TrainingSegmentKind.Finish ? "完成" :
            coachController.CurrentSegmentKind == TrainingSegmentKind.Preparation ? "准备" :
            coachController.CurrentSegmentKind == TrainingSegmentKind.ReturnTransition ? "回位" :
            coachController.IsHoldingAtSegment ? "保持" :
            VoicePromptManager.GetPracticeActionKey() == "catcamel" ? "向上拱背" :
            VoicePromptManager.GetPracticeActionKey() == "curlup" ? "卷起" :
            VoicePromptManager.GetPracticeActionKey() == "birddog" ? "伸展" : "抬起";
        if (guidanceLimbs == CoachGuidanceLimb.None) guidanceLimbs = coachController.CurrentRepetitionTarget;
        string target = FormatTarget(guidanceLimbs);
        progressText.text = safeTotal > 1
            ? $"第{currentSet}/{safeTotal}组 · 第{currentRepetition}/{safeRepetitions}次\n{phaseLabel}{target}"
            : $"第{currentRepetition}/{safeRepetitions}次\n{phaseLabel}{target}";
        progressRoot.gameObject.SetActive(true);
    }

    private static string FormatTarget(CoachGuidanceLimb limbs)
    {
        if (limbs == CoachGuidanceLimb.None)
        {
            switch (VoicePromptManager.GetPracticeActionKey())
            {
                case "hipthrust": return " · 臀部";
                case "catcamel": return " · 背部";
                case "sidebridge": return " · 左侧支撑 · 躯干";
                case "curlup": return " · 上背";
                default: return string.Empty;
            }
        }
        var parts = new System.Collections.Generic.List<string>(2);
        if ((limbs & CoachGuidanceLimb.LeftArm) != 0) parts.Add("左臂");
        if ((limbs & CoachGuidanceLimb.RightArm) != 0) parts.Add("右臂");
        if ((limbs & CoachGuidanceLimb.LeftLeg) != 0) parts.Add("左腿");
        if ((limbs & CoachGuidanceLimb.RightLeg) != 0) parts.Add("右腿");
        return parts.Count == 0 ? string.Empty : $" · {string.Join(" + ", parts)}";
    }

    private static string GetLessonActionTitle(string action)
    {
        switch (action)
        {
            case "deadbug": return "死虫式";
            case "birddog": return "鸟狗式";
            case "hipthrust": return "臀桥";
            case "catcamel": return "卧位猫式";
            case "sidebridge": return "侧桥";
            case "curlup": return "卷腹";
            default: return "动作";
        }
    }

    private void HideProgress()
    {
        if (progressRoot != null)
        {
            progressRoot.gameObject.SetActive(false);
        }
    }

    private void EnsureVisuals()
    {
        if (progressRoot == null)
        {
            Transform existingRoot = transform.Find("CoachSetProgress");
            if (existingRoot != null)
            {
                progressRoot = existingRoot as RectTransform;
            }
        }

        if (progressRoot == null)
        {
            GameObject rootObject = new GameObject(
                "CoachSetProgress",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            rootObject.layer = gameObject.layer;
            progressRoot = rootObject.GetComponent<RectTransform>();
            progressRoot.SetParent(transform, false);
        }

        progressRoot.anchorMin = new Vector2(0.5f, 0.5f);
        progressRoot.anchorMax = new Vector2(0.5f, 0.5f);
        progressRoot.pivot = new Vector2(0.5f, 0.5f);
        progressRoot.anchoredPosition = anchoredPosition;
        progressRoot.sizeDelta = size;
        progressRoot.localRotation = Quaternion.identity;
        progressRoot.localScale = Vector3.one;
        progressRoot.SetAsLastSibling();

        Image background = progressRoot.GetComponent<Image>();
        if (background == null)
        {
            background = progressRoot.gameObject.AddComponent<Image>();
        }
        background.color = backgroundColor;
        background.raycastTarget = false;

        if (progressText == null)
        {
            progressText = progressRoot.GetComponentInChildren<TMP_Text>(true);
        }

        if (progressText == null)
        {
            GameObject textObject = new GameObject(
                "SetProgressText",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.layer = gameObject.layer;
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(progressRoot, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 4f);
            textRect.offsetMax = new Vector2(-8f, -4f);
            textRect.localRotation = Quaternion.identity;
            textRect.localScale = Vector3.one;
            progressText = textObject.GetComponent<TextMeshProUGUI>();
        }

        progressText.alignment = TextAlignmentOptions.Center;
        progressText.fontSize = Mathf.Max(fontSize, 42f);
        var mobileControls = FindObjectOfType<MobileCoachControls>(true);
        if (mobileControls != null)
        {
            var phoneText = mobileControls.GetComponentInChildren<TMP_Text>(true);
            if (phoneText != null) progressText.font = phoneText.font;
            progressText.enableAutoSizing = true;
            progressText.fontSizeMin = 20f;
            progressText.fontSizeMax = fontSize;
        }
        progressText.fontSize = Mathf.Max(fontSize, 42f);
        progressText.fontStyle = FontStyles.Bold;
        progressText.color = Color.white;
        progressText.raycastTarget = false;
        progressText.text = "准备开始";

    }

    public RectTransform ProgressRoot => progressRoot;
    public TMP_Text ProgressText => progressText;
    public bool IsVisible => progressRoot != null && progressRoot.gameObject.activeInHierarchy;
}
