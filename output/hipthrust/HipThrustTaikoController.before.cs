using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public sealed class HipThrustTaikoController : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Animator coachAnimator;
    [SerializeField] private Animator coachPreviewAnimator;
    [SerializeField] private RuntimeAnimatorController hipThrustAnimatorController;
    [SerializeField] private RhythmPoseJudge poseJudge;

    [Header("Replaceable Assets")]
    [SerializeField] private GameObject notePrefab;
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private Material laneMaterial;
    [SerializeField] private Material noteMaterial;
    [SerializeField] private Material judgementMaterial;

    [Header("GamingCanvas Assets")]
    [SerializeField] private Canvas gamingCanvas;
    [SerializeField] private Sprite backgroundSprite;
    [SerializeField] private Sprite trackSprite;
    [SerializeField] private Sprite hipUpSprite;
    [SerializeField] private Sprite hipDownSprite;
    [SerializeField] private Sprite sliderSprite;
    [SerializeField] private Sprite sunSprite;
    [SerializeField] private Sprite sunlightSprite;

    [Header("GamingCanvas Layout")]
    [SerializeField] private Vector2 uiTrackSize = new Vector2(1340f, 280f);
    [SerializeField] private Vector2 uiTrackPosition = new Vector2(247f, 205f);
    [SerializeField] private Vector2 uiJudgementPosition = new Vector2(-267f, 205f);
    [SerializeField] private Vector2 uiSpawnPosition = new Vector2(720f, 205f);
    [Min(120f)] [SerializeField] private float uiGroupSpan = 480f;
    [Min(40f)] [SerializeField] private float uiNoteSize = 104f;

    [Header("Editable Canvas Objects")]
    [SerializeField] private RectTransform canvasVisualRoot;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image trackImage;
    [SerializeField] private Image judgementImage;
    [SerializeField] private Image sunlightImage;
    [SerializeField] private RectTransform noteSpawnAnchor;
    [SerializeField] private RectTransform noteClipArea;

    [Header("View Placement")]
    [Min(0.5f)] [SerializeField] private float distanceFromView = 2.3f;
    [SerializeField] private float verticalOffset = -0.15f;
    [Min(0.1f)] [SerializeField] private float worldScale = 0.75f;
    [Min(0f)] [SerializeField] private float followSpeed = 12f;

    [Header("Lane Layout")]
    [SerializeField] private float judgementX = -0.82f;
    [SerializeField] private float spawnX = 1.25f;
    [Min(0.05f)] [SerializeField] private float laneHalfHeight = 0.2f;
    [Min(0.02f)] [SerializeField] private float noteRadius = 0.14f;
    [Min(0.02f)] [SerializeField] private float judgementRadius = 0.19f;
    [Min(0.002f)] [SerializeField] private float lineWidth = 0.018f;

    [Header("Beat Map")]
    [SerializeField] private string coachStateName = "animation_pose";
    [Min(1)] [SerializeField] private int noteCount = 12;
    [Min(20f)] [SerializeField] private float beatsPerMinute = 48f;
    [Min(1)] [SerializeField] private int beatsPerNote = 2;
    [Min(0f)] [SerializeField] private float firstHitDelay = 2.5f;
    [Min(0.1f)] [SerializeField] private float approachDuration = 2.2f;
    [Min(0f)] [SerializeField] private float lateJudgementWindow = 1.2f;

    [Header("Hip Thrust Timing")]
    [SerializeField] private string hipThrustStartStateName = "hipthrust_start";
    [SerializeField] private string hipThrustUpStateName = "animation_pose";
    [SerializeField] private string hipThrustHoldStateName = "hipthrust_hold";
    [SerializeField] private string hipThrustDownStateName = "hipthrust_down";
    [Min(0.05f)] [SerializeField] private float upDuration = 1.6333f;
    [Min(0.05f)] [SerializeField] private float holdDuration = 1.1f;
    [Min(0.05f)] [SerializeField] private float downDuration = 0.9333f;
    [Min(50f)] [SerializeField] private float sliderPixelsPerSecond = 588f;

    [Header("Feedback")]
    [SerializeField] private Color laneColor = new Color(0.25f, 0.78f, 1f, 0.75f);
    [SerializeField] private Color noteColor = new Color(0.1f, 0.82f, 1f, 1f);
    [SerializeField] private Color judgementColor = new Color(1f, 0.34f, 0.48f, 1f);
    [SerializeField] private Color hitColor = new Color(0.36f, 1f, 0.62f, 1f);
    [SerializeField] private Color missColor = Color.white;

    private sealed class ActiveNote
    {
        public GameObject gameObject;
        public float hitTime;
    }

    private sealed class ActiveUiGroup
    {
        public RectTransform root;
        public Image up;
        public Image hold;
        public Image down;
        public float arrivalTime;
        public bool scoreHeld = true;
        public bool resolved;
    }

    private readonly List<ActiveNote> activeNotes = new List<ActiveNote>();
    private readonly List<ActiveUiGroup> activeUiGroups = new List<ActiveUiGroup>();
    private Transform visualRoot;
    private RectTransform uiRoot;
    private RectTransform uiNoteClip;
    private Image uiSunlight;
    private bool usingCanvasVisuals;
    private LineRenderer judgementRing;
    private bool running;
    private bool startedForCurrentCoachRun;
    private bool waitingForStartPose;
    private float modeStartTime;
    private int nextNoteIndex;
    private int resolvedCount;
    private float judgementPulseUntil;
    private Material runtimeLaneMaterial;
    private Material runtimeNoteMaterial;
    private Material runtimeJudgementMaterial;
    private float coachSequenceStartTime;
    private int activeCoachSegment = -1;

    private float NoteInterval => 60f / Mathf.Max(20f, beatsPerMinute) * Mathf.Max(1, beatsPerNote);
    private float HipThrustSequenceDuration => Mathf.Max(0.1f, upDuration + holdDuration + downDuration);

    private void Awake()
    {
        ResolveReferences();
        ApplyHipThrustAnimatorControllers();
        usingCanvasVisuals = gamingCanvas != null && backgroundSprite != null && trackSprite != null;
        if (usingCanvasVisuals)
        {
            BuildCanvasVisuals();
        }
        else
        {
            BuildPlaceholderVisuals();
            visualRoot.gameObject.SetActive(false);
        }

        if (!Application.isPlaying && usingCanvasVisuals)
        {
            uiRoot.gameObject.SetActive(true);
        }
    }

    private void ApplyHipThrustAnimatorControllers()
    {
        if (hipThrustAnimatorController == null)
        {
            return;
        }
        if (coachAnimator != null)
        {
            coachAnimator.runtimeAnimatorController = hipThrustAnimatorController;
        }
        if (coachPreviewAnimator != null)
        {
            coachPreviewAnimator.runtimeAnimatorController = hipThrustAnimatorController;
        }
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }
        if (!running)
        {
            TryStartWithCoach();
            return;
        }

        float now = Time.time;
        UpdateCoachSegmentPlayback(now);
        if (usingCanvasVisuals)
        {
            SpawnDueUiGroups(now);
            UpdateUiGroups(now);
        }
        else
        {
            SpawnDueNotes(now);
            UpdateNotes(now);
        }
        UpdateJudgementPulse(now);
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
        {
            return;
        }
        ClearNotes();
        ClearUiGroups();
        running = false;
    }

    private void TryStartWithCoach()
    {
        if (coachAnimator == null || !coachAnimator.enabled)
        {
            return;
        }

        AnimatorStateInfo state = coachAnimator.GetCurrentAnimatorStateInfo(0);
        if (state.IsName(hipThrustStartStateName))
        {
            waitingForStartPose = true;
            if (state.normalizedTime >= 0.99f)
            {
                waitingForStartPose = false;
                StartMode();
            }
            return;
        }

        if (!waitingForStartPose && state.IsName(coachStateName) && !startedForCurrentCoachRun)
        {
            StartMode();
        }
    }

    public void StartMode()
    {
        ClearNotes();
        ClearUiGroups();
        modeStartTime = Time.time;
        coachSequenceStartTime = modeStartTime + firstHitDelay;
        activeCoachSegment = -1;
        nextNoteIndex = 0;
        resolvedCount = 0;
        running = true;
        startedForCurrentCoachRun = true;
        if (usingCanvasVisuals)
        {
            ConfigureNoteClip();
            uiRoot.gameObject.SetActive(true);
        }
        else
        {
            visualRoot.gameObject.SetActive(true);
        }
        PrepareCoachForLeadIn();
    }

    private void PrepareCoachForLeadIn()
    {
        PlayCoachState(hipThrustUpStateName, 0f);
        SetCoachPlaybackSpeed(0f);
    }

    private void UpdateCoachSegmentPlayback(float now)
    {
        if (coachAnimator == null || now < coachSequenceStartTime)
        {
            return;
        }

        float cycleDuration = HipThrustSequenceDuration;
        float cycleTime = Mathf.Repeat(now - coachSequenceStartTime, cycleDuration);
        int segment;
        string stateName;
        if (cycleTime < upDuration)
        {
            segment = 0;
            stateName = hipThrustUpStateName;
        }
        else if (cycleTime < upDuration + holdDuration)
        {
            segment = 1;
            stateName = hipThrustHoldStateName;
        }
        else
        {
            segment = 2;
            stateName = hipThrustDownStateName;
        }

        if (segment != activeCoachSegment)
        {
            activeCoachSegment = segment;
            PlayCoachState(stateName, 1f);
        }
    }

    private void PlayCoachState(string stateName, float speed)
    {
        PlayAnimatorState(coachAnimator, stateName, speed);
        PlayAnimatorState(coachPreviewAnimator, stateName, speed);
    }

    private static void PlayAnimatorState(Animator animator, string stateName, float speed)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName))
        {
            return;
        }
        animator.enabled = true;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.speed = speed;
        animator.Play(stateName, 0, 0f);
        animator.Update(0f);
    }

    private void SetCoachPlaybackSpeed(float speed)
    {
        if (coachAnimator != null)
        {
            coachAnimator.speed = speed;
        }
        if (coachPreviewAnimator != null)
        {
            coachPreviewAnimator.speed = speed;
        }
    }

    private void BuildCanvasVisuals()
    {
        UpdateGroupSpanFromHoldDuration();
        if (canvasVisualRoot == null)
        {
            canvasVisualRoot = new GameObject("HipThrustCanvasVisuals", typeof(RectTransform)).GetComponent<RectTransform>();
            canvasVisualRoot.SetParent(gamingCanvas.transform, false);
            canvasVisualRoot.anchorMin = Vector2.zero;
            canvasVisualRoot.anchorMax = Vector2.one;
            canvasVisualRoot.offsetMin = Vector2.zero;
            canvasVisualRoot.offsetMax = Vector2.zero;
            canvasVisualRoot.SetAsFirstSibling();
        }
        uiRoot = canvasVisualRoot;

        if (backgroundImage == null)
        {
            backgroundImage = CreateImage("HipThrustBackground", backgroundSprite, uiRoot);
            Stretch(backgroundImage.rectTransform);
            backgroundImage.transform.SetAsFirstSibling();
        }
        backgroundImage.sprite = backgroundSprite;

        if (trackImage == null)
        {
            trackImage = CreateImage("HipThrustTrack", trackSprite, uiRoot);
            SetRect(trackImage.rectTransform, uiTrackSize, uiTrackPosition, new Vector2(0.5f, 0.5f));
        }
        trackImage.sprite = trackSprite;
        trackImage.preserveAspect = true;

        if (judgementImage == null)
        {
            judgementImage = CreateImage("HipThrustJudgement", sunSprite, uiRoot);
            SetRect(judgementImage.rectTransform, new Vector2(150f, 150f), uiJudgementPosition, new Vector2(0.5f, 0.5f));
        }
        judgementImage.sprite = sunSprite;
        judgementImage.preserveAspect = true;

        if (sunlightImage == null)
        {
            sunlightImage = CreateImage("HipThrustSunlight", sunlightSprite, uiRoot);
            SetRect(sunlightImage.rectTransform, new Vector2(230f, 230f), uiJudgementPosition, new Vector2(0.5f, 0.5f));
        }
        sunlightImage.sprite = sunlightSprite;
        sunlightImage.preserveAspect = true;
        uiSunlight = sunlightImage;

        if (noteSpawnAnchor == null)
        {
            noteSpawnAnchor = new GameObject("NoteSpawn", typeof(RectTransform)).GetComponent<RectTransform>();
            noteSpawnAnchor.SetParent(uiRoot, false);
            SetRect(noteSpawnAnchor, new Vector2(32f, 32f), uiSpawnPosition, new Vector2(0.5f, 0.5f));
        }

        if (noteClipArea == null)
        {
            noteClipArea = new GameObject("NoteClipArea", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            noteClipArea.SetParent(uiRoot, false);
        }
        uiNoteClip = noteClipArea;
        ConfigureNoteClip();

        judgementImage.transform.SetAsLastSibling();
        sunlightImage.transform.SetAsLastSibling();
        if (Application.isPlaying)
        {
            uiSunlight.gameObject.SetActive(false);
            uiRoot.gameObject.SetActive(true);
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

    private void ConfigureNoteClip()
    {
        if (uiNoteClip == null || trackImage == null || judgementImage == null)
        {
            return;
        }
        float left = judgementImage.rectTransform.anchoredPosition.x - uiNoteSize * 0.5f;
        float right = trackImage.rectTransform.anchoredPosition.x + trackImage.rectTransform.sizeDelta.x * 0.5f;
        float width = Mathf.Max(100f, right - left);
        float height = Mathf.Max(trackImage.rectTransform.sizeDelta.y, uiNoteSize * 1.5f);
        SetRect(uiNoteClip, new Vector2(width, height),
            new Vector2((left + right) * 0.5f, trackImage.rectTransform.anchoredPosition.y),
            new Vector2(0.5f, 0.5f));
    }

    private void SpawnDueUiGroups(float now)
    {
        while (nextNoteIndex < noteCount)
        {
            float arrivalTime = modeStartTime + firstHitDelay + nextNoteIndex * HipThrustSequenceDuration;
            if (now < arrivalTime - approachDuration)
            {
                break;
            }

            activeUiGroups.Add(CreateUiGroup(nextNoteIndex, arrivalTime));
            nextNoteIndex++;
        }
    }

    private ActiveUiGroup CreateUiGroup(int index, float arrivalTime)
    {
        UpdateGroupSpanFromHoldDuration();
        ActiveUiGroup group = new ActiveUiGroup();
        group.root = new GameObject($"HipThrustGroup_{index + 1:00}", typeof(RectTransform)).GetComponent<RectTransform>();
        group.root.SetParent(uiNoteClip, false);
        group.root.anchorMin = new Vector2(0.5f, 0.5f);
        group.root.anchorMax = new Vector2(0.5f, 0.5f);
        group.root.pivot = new Vector2(0.5f, 0.5f);
        group.root.sizeDelta = new Vector2(uiGroupSpan + uiNoteSize, 150f);

        group.up = CreateImage("HipUp", hipUpSprite, group.root);
        group.down = CreateImage("HipDown", hipDownSprite, group.root);
        group.hold = CreateImage("HipHold", sliderSprite, group.root);
        SetRect(group.up.rectTransform, Vector2.one * uiNoteSize, Vector2.zero, new Vector2(0.5f, 0.5f));
        SetRect(group.down.rectTransform, Vector2.one * uiNoteSize, new Vector2(uiGroupSpan, 0f), new Vector2(0.5f, 0.5f));
        SetRect(group.hold.rectTransform, new Vector2(uiGroupSpan, 42f),
            new Vector2(uiGroupSpan * 0.5f, 0f), new Vector2(0.5f, 0.5f));
        group.up.preserveAspect = true;
        group.down.preserveAspect = true;
        group.hold.preserveAspect = true;
        group.hold.transform.SetAsFirstSibling();
        group.up.transform.SetAsLastSibling();
        group.down.transform.SetAsLastSibling();
        group.arrivalTime = arrivalTime;
        group.root.anchoredPosition = CanvasToClipPosition(GetSpawnPosition());
        return group;
    }

    private void UpdateUiGroups(float now)
    {
        bool sunlightActive = false;
        for (int index = activeUiGroups.Count - 1; index >= 0; index--)
        {
            ActiveUiGroup group = activeUiGroups[index];
            if (group.root == null)
            {
                activeUiGroups.RemoveAt(index);
                continue;
            }

            Vector2 spawnPosition = GetSpawnPosition();
            Vector2 judgementPosition = GetJudgementPosition();
            float travelProgress = Mathf.Clamp01(1f - (group.arrivalTime - now) / approachDuration);
            float phase = now - group.arrivalTime;
            float postArrivalDistance = CalculatePostArrivalDistance(phase);
            Vector2 canvasPosition = Vector2.Lerp(spawnPosition, judgementPosition, travelProgress);
            canvasPosition.x -= postArrivalDistance;
            group.root.anchoredPosition = CanvasToClipPosition(canvasPosition);
            bool inAction = phase >= 0f && phase <= upDuration + holdDuration + downDuration;
            if (inAction)
            {
                bool scoreOk = poseJudge != null && poseJudge.TryHit(RhythmBodyTarget.Waist, out _);
                group.scoreHeld &= scoreOk;
                sunlightActive |= scoreOk;
            }

            group.up.gameObject.SetActive(phase < 0f);
            group.hold.gameObject.SetActive(true);
            group.down.gameObject.SetActive(true);
            if (phase > upDuration + holdDuration + downDuration + lateJudgementWindow)
            {
                if (!group.resolved)
                {
                    group.resolved = true;
                    ShowUiFeedback(group.scoreHeld);
                }
                Destroy(group.root.gameObject);
                activeUiGroups.RemoveAt(index);
                resolvedCount++;
            }
        }

        if (uiSunlight != null)
        {
            uiSunlight.gameObject.SetActive(sunlightActive);
        }
        if (resolvedCount >= noteCount && nextNoteIndex >= noteCount)
        {
            running = false;
            SetCoachPlaybackSpeed(0f);
        }
    }

    private float CalculatePostArrivalDistance(float phase)
    {
        if (phase <= 0f)
        {
            return 0f;
        }

        float downStartTime = Mathf.Max(0.1f, upDuration + holdDuration);
        if (phase < downStartTime)
        {
            return uiGroupSpan * Mathf.Clamp01(phase / downStartTime);
        }

        float downProgress = Mathf.Clamp01((phase - downStartTime) / Mathf.Max(0.05f, downDuration));
        return uiGroupSpan + uiNoteSize * downProgress;
    }

    private void UpdateGroupSpanFromHoldDuration()
    {
        uiGroupSpan = Mathf.Max(uiNoteSize * 1.5f, holdDuration * sliderPixelsPerSecond);
    }

    private Vector2 GetSpawnPosition()
    {
        Vector2 spawnPosition = noteSpawnAnchor != null ? noteSpawnAnchor.anchoredPosition : uiSpawnPosition;
        spawnPosition.y = GetJudgementPosition().y;
        return spawnPosition;
    }

    private Vector2 GetJudgementPosition()
    {
        return judgementImage != null ? judgementImage.rectTransform.anchoredPosition : uiJudgementPosition;
    }

    private Vector2 CanvasToClipPosition(Vector2 canvasPosition)
    {
        return uiNoteClip != null ? canvasPosition - uiNoteClip.anchoredPosition : canvasPosition;
    }

    private void ShowUiFeedback(bool hit)
    {
        if (uiRoot == null)
        {
            return;
        }
        GameObject feedback = new GameObject(hit ? "HipHit" : "HipMiss", typeof(RectTransform));
        feedback.transform.SetParent(uiRoot, false);
        RectTransform rect = feedback.GetComponent<RectTransform>();
        SetRect(rect, new Vector2(180f, 70f), GetJudgementPosition() + new Vector2(0f, 105f), new Vector2(0.5f, 0.5f));
        Text text = feedback.AddComponent<Text>();
        text.text = hit ? "HIT" : "MISS";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 44;
        text.fontStyle = FontStyle.Bold;
        text.color = hit ? hitColor : missColor;
        Destroy(feedback, 0.65f);
    }

    private static Image CreateImage(string objectName, Sprite sprite, Transform parent)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(RectTransform rect, Vector2 size, Vector2 position, Vector2 pivot)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private void SpawnDueNotes(float now)
    {
        while (nextNoteIndex < noteCount)
        {
            float hitTime = modeStartTime + firstHitDelay + nextNoteIndex * NoteInterval;
            if (now < hitTime - approachDuration)
            {
                break;
            }

            activeNotes.Add(new ActiveNote
            {
                gameObject = CreateNote(nextNoteIndex),
                hitTime = hitTime
            });
            nextNoteIndex++;
        }
    }

    private void UpdateNotes(float now)
    {
        for (int index = activeNotes.Count - 1; index >= 0; index--)
        {
            ActiveNote note = activeNotes[index];
            if (note.gameObject == null)
            {
                activeNotes.RemoveAt(index);
                continue;
            }

            float progress = 1f - Mathf.Clamp01((note.hitTime - now) / approachDuration);
            note.gameObject.transform.localPosition = new Vector3(
                Mathf.Lerp(spawnX, judgementX, progress),
                0f,
                -0.01f);

            if (now < note.hitTime)
            {
                continue;
            }

            bool hit = poseJudge != null && poseJudge.TryHit(RhythmBodyTarget.Waist, out _);
            if (hit || now >= note.hitTime + lateJudgementWindow)
            {
                ResolveNote(index, hit);
            }
        }
    }

    private void ResolveNote(int index, bool hit)
    {
        ActiveNote note = activeNotes[index];
        activeNotes.RemoveAt(index);
        Vector3 localPosition = note.gameObject != null
            ? note.gameObject.transform.localPosition
            : new Vector3(judgementX, 0f, 0f);

        if (note.gameObject != null)
        {
            Destroy(note.gameObject);
        }

        ShowFeedback(hit, localPosition);
        if (hit)
        {
            PlayHitEffect(localPosition);
            judgementPulseUntil = Time.time + 0.18f;
        }

        resolvedCount++;
        if (resolvedCount >= noteCount)
        {
            running = false;
        }
    }

    private GameObject CreateNote(int index)
    {
        GameObject note;
        if (notePrefab != null)
        {
            note = Instantiate(notePrefab, visualRoot);
        }
        else
        {
            note = new GameObject($"WaistNote_{index + 1:00}");
            note.transform.SetParent(visualRoot, false);
            CreateCircle(note, noteRadius, noteColor, noteMaterial ?? runtimeNoteMaterial, lineWidth * 1.5f);
            CreateCircle(note, noteRadius * 0.58f, new Color(1f, 1f, 1f, 0.8f),
                noteMaterial ?? runtimeNoteMaterial, lineWidth);
        }

        note.transform.localPosition = new Vector3(spawnX, 0f, -0.01f);
        return note;
    }

    private void BuildPlaceholderVisuals()
    {
        visualRoot = new GameObject("TaikoVisuals").transform;
        visualRoot.SetParent(transform, false);

        runtimeLaneMaterial = CreateRuntimeMaterial("HipThrust Lane", laneColor);
        runtimeNoteMaterial = CreateRuntimeMaterial("HipThrust Note", noteColor);
        runtimeJudgementMaterial = CreateRuntimeMaterial("HipThrust Judgement", judgementColor);

        CreateLaneLine("LaneTop", laneHalfHeight);
        CreateLaneLine("LaneBottom", -laneHalfHeight);

        GameObject judgement = new GameObject("JudgementRing");
        judgement.transform.SetParent(visualRoot, false);
        judgement.transform.localPosition = new Vector3(judgementX, 0f, 0f);
        judgementRing = CreateCircle(
            judgement,
            judgementRadius,
            judgementColor,
            judgementMaterial ?? runtimeJudgementMaterial,
            lineWidth * 1.5f);

        GameObject spawnMarker = new GameObject("SpawnMarker");
        spawnMarker.transform.SetParent(visualRoot, false);
        spawnMarker.transform.localPosition = new Vector3(spawnX, 0f, 0.02f);
        CreateCircle(spawnMarker, noteRadius * 0.72f, new Color(laneColor.r, laneColor.g, laneColor.b, 0.3f),
            laneMaterial ?? runtimeLaneMaterial, lineWidth * 0.65f);
    }

    private void CreateLaneLine(string objectName, float y)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(visualRoot, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        ConfigureLine(line, laneMaterial ?? runtimeLaneMaterial, lineWidth, laneColor);
        line.positionCount = 2;
        line.useWorldSpace = false;
        line.SetPosition(0, new Vector3(judgementX - judgementRadius * 0.3f, y, 0.02f));
        line.SetPosition(1, new Vector3(spawnX + noteRadius, y, 0.02f));
    }

    private static LineRenderer CreateCircle(
        GameObject owner,
        float radius,
        Color color,
        Material material,
        float width)
    {
        GameObject lineObject = new GameObject($"CircleLine_{owner.transform.childCount + 1:00}");
        lineObject.transform.SetParent(owner.transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        ConfigureLine(line, material, width, color);
        const int segments = 64;
        line.loop = true;
        line.useWorldSpace = false;
        line.positionCount = segments;
        for (int index = 0; index < segments; index++)
        {
            float angle = index / (float)segments * Mathf.PI * 2f;
            line.SetPosition(index, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
        }
        return line;
    }

    private static void ConfigureLine(LineRenderer line, Material material, float width, Color color)
    {
        line.material = material;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    private void ShowFeedback(bool hit, Vector3 localPosition)
    {
        GameObject feedback = new GameObject(hit ? "HitFeedback" : "MissFeedback");
        feedback.transform.SetParent(visualRoot, false);
        feedback.transform.localPosition = localPosition + new Vector3(0f, 0.34f, -0.04f);
        TextMesh text = feedback.AddComponent<TextMesh>();
        text.text = hit ? "HIT" : "MISS";
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 64;
        text.characterSize = 0.08f;
        text.fontStyle = FontStyle.Bold;
        text.color = hit ? hitColor : missColor;
        Destroy(feedback, 0.65f);
    }

    private void PlayHitEffect(Vector3 localPosition)
    {
        if (hitEffectPrefab != null)
        {
            GameObject effect = Instantiate(hitEffectPrefab, visualRoot);
            effect.transform.localPosition = localPosition;
            Destroy(effect, 2f);
            return;
        }

        GameObject effectObject = new GameObject("HitParticles");
        effectObject.transform.SetParent(visualRoot, false);
        effectObject.transform.localPosition = localPosition;
        ParticleSystem particles = effectObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.25f;
        main.loop = false;
        main.startLifetime = 0.35f;
        main.startSpeed = 0.65f;
        main.startSize = 0.045f;
        main.startColor = hitColor;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = judgementRadius * 0.7f;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.material = noteMaterial ?? runtimeNoteMaterial;
        particles.Play();
        Destroy(effectObject, 1f);
    }

    private void UpdateJudgementPulse(float now)
    {
        if (judgementRing == null)
        {
            return;
        }
        bool pulsing = now < judgementPulseUntil;
        judgementRing.startWidth = pulsing ? lineWidth * 2.8f : lineWidth * 1.5f;
        judgementRing.endWidth = judgementRing.startWidth;
        Color color = pulsing ? hitColor : judgementColor;
        judgementRing.startColor = color;
        judgementRing.endColor = color;
    }

    private Material CreateRuntimeMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        Material material = new Material(shader)
        {
            name = materialName,
            color = color
        };
        return material;
    }

    private void ResolveReferences()
    {
        if (coachAnimator == null)
        {
            CoachActionController coach = FindObjectOfType<CoachActionController>();
            coachAnimator = coach != null ? coach.GetComponentInChildren<Animator>(true) : null;
        }
        if (poseJudge == null)
        {
            poseJudge = FindObjectOfType<RhythmPoseJudge>(true);
        }
    }

    private void ClearNotes()
    {
        for (int index = 0; index < activeNotes.Count; index++)
        {
            if (activeNotes[index].gameObject != null)
            {
                Destroy(activeNotes[index].gameObject);
            }
        }
        activeNotes.Clear();
    }

    private void ClearUiGroups()
    {
        for (int index = 0; index < activeUiGroups.Count; index++)
        {
            if (activeUiGroups[index].root != null)
            {
                Destroy(activeUiGroups[index].root.gameObject);
            }
        }
        activeUiGroups.Clear();
        if (uiSunlight != null)
        {
            uiSunlight.gameObject.SetActive(false);
        }
    }
}
