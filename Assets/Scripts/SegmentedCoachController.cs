using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 播放后端类型。Animator = 编辑期导入的 AnimatorController（现有动作）；
/// Keyframe = 运行时下发的 CoachMotionPackage JSON（服务器新动作）。
/// </summary>
public enum CoachMotionBackendType
{
    Animator,
    Keyframe
}

// One small, independent sub-clip of a training action. The full action is a
// sequence of playback phases. A phase may contain a fixed hold, but playback
// owns its duration and never waits for pose quality or a scoring callback.
[Serializable]
public class ActionSegment
{
    public string label;                 // e.g. "抬右腿" - shown in UI / logs
    public string clipStateName;         // Animator state name of this sub-clip
    [Min(1)] public int repeatCount = 1; // how many times this segment repeats
    public AudioClip voiceClip;          // optional cue emitted when this phase starts
    public string voiceClipName;         // optional Resources/Audio/VoicePrompts clip name

    [Tooltip("质量观察容错倍数。1 = 正常严格；>1 更宽松（兼容旧字段名）。")]
    [Min(0.1f)] public float scoreLeniency = 1f;

    [Tooltip("该段的保持/播放阶段时长（秒）。它属于课程编排，不是评分窗口。")]
    [Min(1f)] public float scoringDuration = 5f;

    [Tooltip("Coach limbs highlighted while this segment plays. Keep preparation, " +
        "return and finish segments at None.")]
    public CoachGuidanceLimb coachGuidanceLimbs = CoachGuidanceLimb.None;

    [Tooltip("Generic corrective guidance for this hold. Auto preserves the " +
        "legacy motion-inference fallback; Explicit uses the two region masks; " +
        "Disabled suppresses corrective cues.")]
    public SegmentPoseGuidanceRule poseGuidanceRule;

    [Tooltip("Compatibility flag used by server-authored motion packages. " +
        "The practice flow derives score mapping from the segment kind.")]
    public bool applyFormalActionScoreMapping;
}

public enum TrainingSegmentKind
{
    Preparation,
    CoreAction,
    ReturnTransition,
    Finish
}

[Flags]
public enum CoachGuidanceLimb
{
    None = 0,
    LeftArm = 1 << 0,
    RightArm = 1 << 1,
    LeftLeg = 1 << 2,
    RightLeg = 1 << 3
}

public struct TrainingSegmentPlanEntry
{
    public int segmentIndex;
    public int setIndex;
    public int repetitionIndex;
    public TrainingSegmentKind kind;
    public bool completesRepetition;
    public bool completesSet;

    public TrainingSegmentPlanEntry(
        int segmentIndex,
        int setIndex,
        int repetitionIndex,
        TrainingSegmentKind kind,
        bool completesRepetition,
        bool completesSet)
    {
        this.segmentIndex = segmentIndex;
        this.setIndex = setIndex;
        this.repetitionIndex = repetitionIndex;
        this.kind = kind;
        this.completesRepetition = completesRepetition;
        this.completesSet = completesSet;
    }
}

// Passed to OnSegmentHold when the coach enters a fixed playback hold. Quality
// observers may use it to update their current context, but never advance from it.
public struct SegmentHoldInfo
{
    public int segmentIndex;
    public int repeatIndex;   // 0-based repetition of this segment
    public int setIndex;      // 0-based set of the whole sequence
    public string label;
    public float scoreLeniency; // retained quality tolerance for diagnostics
    public float scoringDuration; // authored hold-phase seconds (legacy field name)
    public TrainingSegmentKind segmentKind;
    public bool applyFormalActionScoreMapping;
    public SegmentPoseGuidanceRule poseGuidanceRule;

    public SegmentHoldInfo(
        int segmentIndex,
        int repeatIndex,
        int setIndex,
        string label,
        float scoreLeniency,
        float scoringDuration,
        TrainingSegmentKind segmentKind = TrainingSegmentKind.Preparation)
        : this(
            segmentIndex,
            repeatIndex,
            setIndex,
            label,
            scoreLeniency,
            scoringDuration,
            segmentKind,
            SegmentPoseGuidanceRule.Auto)
    {
    }

    public SegmentHoldInfo(
        int segmentIndex,
        int repeatIndex,
        int setIndex,
        string label,
        float scoreLeniency,
        float scoringDuration,
        TrainingSegmentKind segmentKind,
        SegmentPoseGuidanceRule poseGuidanceRule)
    {
        this.segmentIndex = segmentIndex;
        this.repeatIndex = repeatIndex;
        this.setIndex = setIndex;
        this.label = label;
        this.scoreLeniency = scoreLeniency;
        this.scoringDuration = scoringDuration;
        this.segmentKind = segmentKind;
        this.poseGuidanceRule = poseGuidanceRule;
        applyFormalActionScoreMapping =
            segmentKind == TrainingSegmentKind.CoreAction;
    }

    public SegmentHoldInfo(
        int segmentIndex,
        int repeatIndex,
        int setIndex,
        string label,
        float scoreLeniency,
        float scoringDuration,
        bool applyFormalActionScoreMapping)
        : this(
            segmentIndex,
            repeatIndex,
            setIndex,
            label,
            scoreLeniency,
            scoringDuration,
            applyFormalActionScoreMapping,
            SegmentPoseGuidanceRule.Auto)
    {
    }

    public SegmentHoldInfo(
        int segmentIndex,
        int repeatIndex,
        int setIndex,
        string label,
        float scoreLeniency,
        float scoringDuration,
        bool applyFormalActionScoreMapping,
        SegmentPoseGuidanceRule poseGuidanceRule)
        : this(
            segmentIndex,
            repeatIndex,
            setIndex,
            label,
            scoreLeniency,
            scoringDuration,
            applyFormalActionScoreMapping
                ? TrainingSegmentKind.CoreAction
                : TrainingSegmentKind.ReturnTransition,
            poseGuidanceRule)
    {
        this.applyFormalActionScoreMapping = applyFormalActionScoreMapping;
    }
}

public struct PracticeSegmentPlaybackInfo
{
    public int segmentIndex;
    public int repetitionIndex;
    public int setIndex;
    public string label;
    public TrainingSegmentKind segmentKind;
    public CoachGuidanceLimb guidanceLimbs;

    public PracticeSegmentPlaybackInfo(
        int segmentIndex,
        int repetitionIndex,
        int setIndex,
        string label,
        TrainingSegmentKind segmentKind,
        CoachGuidanceLimb guidanceLimbs)
    {
        this.segmentIndex = segmentIndex;
        this.repetitionIndex = repetitionIndex;
        this.setIndex = setIndex;
        this.label = label;
        this.segmentKind = segmentKind;
        this.guidanceLimbs = guidanceLimbs;
    }
}

// Practice-mode coach controller: plays an action as a sequence of segment
// sub-clips, holding at each authored phase-end pose while quality is observed
// in parallel. Kept separate from the gaming/demo CoachActionController so
// neither interferes with the other.
public class SegmentedCoachController : MonoBehaviour
{
    [SerializeField] private Animator coachAnimator;
    [SerializeField] private string idleStateName = "Idle";

    [Header("Action Segments")]
    [Tooltip("Ordered sub-clips of the action. Each plays to its end and holds for its configured timeline window. Each segment's clip should have Loop Time OFF.")]
    [SerializeField] private List<ActionSegment> segments = new List<ActionSegment>();
    [Tooltip("How many times the whole segment sequence runs (sets).")]
    [Min(1)] [SerializeField] private int setCount = 1;
    [Tooltip("How many complete up/down action pairs the user performs in each set.")]
    [Min(1)] [SerializeField] private int repetitionsPerSet = 4;
    [Tooltip("Legacy per-set boundaries. When enabled, preparation and finish " +
        "segments surround every set. Disable for one continuous training flow.")]
    [SerializeField] private bool repeatBoundarySegmentsPerSet;
    [Tooltip("Fixed-time pose blend used between adjacent training segments. " +
        "This keeps down-to-up set boundaries and the final down-to-finish " +
        "transition visually continuous even when the imported clips do not " +
        "share an identical boundary pose.")]
    [Min(0f)] [SerializeField] private float segmentTransitionBlendSeconds = 0.12f;
    [Min(0f)] [SerializeField] private float restAfterReturnSeconds = 4f;
    [SerializeField] private bool enableSegmentPausing = true;

    [Header("Dead Bug Preparation Target")]
    [Tooltip("Build the lying preparation target with both arms extended overhead. " +
        "The source clip ends with an unsuitable wide-arm pose, so each arm is " +
        "taken from the matching return-to-preparation endpoint.")]
    [SerializeField] private bool composeDeadBugPreparationArms;
    [SerializeField] private string deadBugPreparationStateName = "deadbug_start";
    [SerializeField] private string deadBugRightArmExtensionStateName = "deadbug_1_down";
    [SerializeField] private string deadBugLeftArmExtensionStateName = "deadbug_2_down";

    // Fired when the coach reaches (and freezes at) a segment-end pose - the
    // authored hold phase. Quality observers may consume this as context only.
    public event Action<SegmentHoldInfo> OnSegmentHold;
    // Fired when movement begins so guidance is visible during the whole clip.
    public event Action<PracticeSegmentPlaybackInfo> OnSegmentStarted;
    // Fired whenever another full set finishes. Values are completed/total.
    public event Action<int, int> OnSetProgressChanged;
    // Fired after the final segment of the final set is advanced past.
    public event Action OnAllComplete;

    private bool hasStarted;
    private bool isPaused;          // global pause (TogglePause)
    private bool isHolding;         // frozen at a segment-end pose during the timeline window
    private bool isComplete;        // whole action (all sets) done
    private bool skipRequested;
    private int currentSetIndex;
    private int currentSegmentIndex;
    private int currentRepeat;
    private int completedSetCount;
    private int completedRepetitionCount;
    private int currentPlanIndex;
    private Coroutine holdAdvanceRoutine;
    private Coroutine lessonRoutine;
    private bool preparingRepetition;
    public bool IsPreparingRepetition => preparingRepetition;
    private int voicePhaseRequestSequence;
    private int pendingVoicePhaseRequestId;
    private bool pendingVoicePhaseCompleted = true;
    private bool finishVoiceRequested;
    private bool currentPlanEntryCounted;
    private readonly List<TrainingSegmentPlanEntry> playbackPlan =
        new List<TrainingSegmentPlanEntry>();
    private readonly Dictionary<HumanBodyBones, Quaternion> deadBugPreparationArmRotations =
        new Dictionary<HumanBodyBones, Quaternion>();

    // 播放后端（Animator 或 Keyframe），由 SetMotionBackend() 注入
    private ICoachMotionBackend motionBackend;
    private CoachMotionBackendType backendType;
    private KeyframeCoachMotionBackend keyframeBackend; // 留引用以便 Dispose

    private static readonly HumanBodyBones[] LeftArmBones =
    {
        HumanBodyBones.LeftShoulder,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.LeftHand
    };

    private static readonly HumanBodyBones[] RightArmBones =
    {
        HumanBodyBones.RightShoulder,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.RightHand
    };

    private void Start()
    {
        EnsureDefaultBackend();
    }

    private void OnEnable()
    {
        CoachEventBus.CoachHardStopRequested += HandleHardStopRequested;
        CoachEventBus.CoachVoicePhaseCompleted += HandleVoicePhaseCompleted;
    }

    private void Update()
    {
        if (!hasStarted || isPaused || isComplete || isHolding || preparingRepetition || !enableSegmentPausing)
        {
            return;
        }

        if (skipRequested)
        {
            motionBackend?.SnapToSegmentEnd();
            HoldAtSegmentEnd();
            return;
        }

        // 关键帧后端需要每帧驱动播放进度
        motionBackend?.Tick(Time.deltaTime);

        CheckSegmentEnd();
    }

    private void OnDestroy()
    {
        keyframeBackend?.Dispose();
    }

    private void OnDisable()
    {
        if (lessonRoutine != null) { StopCoroutine(lessonRoutine); lessonRoutine = null; }
        preparingRepetition = false;
        CoachEventBus.CoachHardStopRequested -= HandleHardStopRequested;
        CoachEventBus.CoachVoicePhaseCompleted -= HandleVoicePhaseCompleted;
        if (hasStarted && !isComplete)
        {
            StopTimeline(
                CoachTimelineStatus.Interrupted,
                CoachEndReason.TechnicalError);
        }
    }

    private void HandleVoicePhaseCompleted(int requestId)
    {
        if (requestId == pendingVoicePhaseRequestId)
        {
            pendingVoicePhaseCompleted = true;
        }
    }

    private int RequestVoicePhase(
        CoachSegmentEvent segment,
        CoachVoicePhase phase,
        AudioClip clip = null,
        string clipName = null)
    {
        int requestId = ++voicePhaseRequestSequence;
        pendingVoicePhaseRequestId = requestId;
        pendingVoicePhaseCompleted = false;
        bool dispatched = CoachEventBus.PublishVoicePhaseRequested(
            new CoachVoicePhaseRequest(
                requestId,
                segment,
                phase,
                clip,
                clipName));
        if (!dispatched)
        {
            pendingVoicePhaseCompleted = true;
        }
        return requestId;
    }

    private void CancelPendingVoicePhase()
    {
        if (pendingVoicePhaseCompleted)
        {
            return;
        }

        CoachEventBus.CancelVoicePhase(pendingVoicePhaseRequestId);
        pendingVoicePhaseCompleted = true;
    }

    private void HandleHardStopRequested(CoachEndReason reason)
    {
        CoachTimelineStatus status = reason == CoachEndReason.SafetyStop
                ? CoachTimelineStatus.SafetyStopped
                : reason == CoachEndReason.UserEnded
                    ? CoachTimelineStatus.EndedEarly
                    : CoachTimelineStatus.Interrupted;
        StopTimeline(status, reason);
    }

    private void CheckSegmentEnd()
    {
        if (motionBackend == null || !motionBackend.IsReady)
        {
            return;
        }

        if (motionBackend.HasReachedSegmentEnd())
        {
            HoldAtSegmentEnd();
        }
    }

    private void HoldAtSegmentEnd()
    {
        if (motionBackend == null)
        {
            return;
        }

        // Snap exactly to the phase-end pose, then enter the authored hold phase.
        motionBackend.SnapToSegmentEnd();
        motionBackend.Freeze();
        isHolding = true;

        string label = GetSegmentLabel(currentSegmentIndex);
        if (!HasHoldPhase(CurrentSegmentKind))
        {
            Debug.Log(
                $"SegmentedCoachController: Finished guidance-only segment " +
                $"{currentSegmentIndex + 1}/{segments.Count} '{label}'.");
            isHolding = false;
            holdAdvanceRoutine = StartCoroutine(
                ContinueAfterGuidancePhase());
            return;
        }

        float leniency = Mathf.Max(0.1f, segments[currentSegmentIndex].scoreLeniency);
        float duration = Mathf.Max(1f, segments[currentSegmentIndex].scoringDuration);
        Debug.Log($"SegmentedCoachController: Hold phase {currentSegmentIndex + 1}/{segments.Count} (rep {currentRepeat + 1}, set {currentSetIndex + 1}/{TotalSets}) '{label}' duration={duration}s");
        CoachEventBus.InvokeSafely(OnSegmentHold, new SegmentHoldInfo(
            currentSegmentIndex,
            currentRepeat,
            currentSetIndex,
            label,
            leniency,
            duration,
            CurrentSegmentKind,
            segments[currentSegmentIndex].poseGuidanceRule));
        CoachSegmentEvent segmentEvent = new CoachSegmentEvent(
            currentSegmentIndex,
            currentSetIndex,
            currentRepeat,
            CurrentSegmentKind,
            label);
        RequestVoicePhase(segmentEvent, CoachVoicePhase.HoldGuidance);
        holdAdvanceRoutine = StartCoroutine(AutoAdvanceHeldSegment(duration));
    }

    private IEnumerator ContinueAfterGuidancePhase()
    {
        CancelPendingVoicePhase();
        holdAdvanceRoutine = null;
        skipRequested = false;
        ContinueAfterHeldSegment();
        yield break;
    }

    private IEnumerator AutoAdvanceHeldSegment(float duration)
    {
        float remaining = Mathf.Max(0.1f, duration);
        while (isHolding && remaining > 0f && !skipRequested)
        {
            if (!isPaused && !MobileCoachControls.IsPaused)
            {
                remaining -= Time.unscaledDeltaTime;
            }
            yield return null;
        }

        if (!isHolding)
        {
            yield break;
        }

        if (skipRequested)
        {
            CancelPendingVoicePhase();
        }

        CountCurrentPlanEntry();
        if (CurrentSegmentKind == TrainingSegmentKind.ReturnTransition)
        {
            float restDuration = Mathf.Max(restAfterReturnSeconds,
                FindObjectOfType<VoicePromptManager>()?.LessonRestDuration ?? 0f);
            CoachSegmentEvent restEvent = new CoachSegmentEvent(
                currentSegmentIndex,
                currentSetIndex,
                currentRepeat,
                CurrentSegmentKind,
                GetSegmentLabel(currentSegmentIndex));
            CoachEventBus.PublishRestStarted(restEvent, restDuration);
            if (!skipRequested)
            {
                RequestVoicePhase(restEvent, CoachVoicePhase.RestGuidance);
            }
            while (isHolding && restDuration > 0f && !skipRequested)
            {
                if (!isPaused && !MobileCoachControls.IsPaused)
                {
                    restDuration -= Time.unscaledDeltaTime;
                }
                yield return null;
            }
        }
        CancelPendingVoicePhase();
        holdAdvanceRoutine = null;
        skipRequested = false;
        ContinueAfterHeldSegment();
    }

    private void PlaySegment(int index)
    {
        if (CurrentSegmentKind == TrainingSegmentKind.CoreAction && GetPracticeLessonClipName(index) != null)
        {
            lessonRoutine = StartCoroutine(PrepareRepetition(index));
            return;
        }
        BeginSegment(index);
    }

    private IEnumerator PrepareRepetition(int index)
    {
        preparingRepetition = true;
        motionBackend.Freeze();
        string action = VoicePromptManager.GetPracticeActionKey();
        bool alternating = action == "deadbug" || action == "birddog";
        bool matchingSide = !alternating || (index / 2) % 2 == currentRepeat % 2;
        string key = currentRepeat < 4 && matchingSide
            ? (alternating ? $"rep{currentRepeat + 1}" : $"count{currentRepeat + 1}")
            : (alternating ? $"side{(index / 2) % 2 + 1}" : "prepare");
        AudioClip cue = VoicePromptManager.LoadCoachClip($"Coach_{action}_{key}");
        CoachEventBus.InvokeSafely(OnSegmentStarted, new PracticeSegmentPlaybackInfo(
            index, currentRepeat, currentSetIndex, "准备", CurrentSegmentKind, segments[index].coachGuidanceLimbs));
        var context = new CoachSegmentEvent(index, currentSetIndex, currentRepeat, CurrentSegmentKind, "准备");
        if (cue != null) RequestVoicePhase(context, CoachVoicePhase.SegmentCue, cue);
        float remaining = cue != null ? cue.length + 0.15f : 0f;
        while (remaining > 0f && !skipRequested)
        {
            if (!isPaused && !MobileCoachControls.IsPaused) remaining -= Time.unscaledDeltaTime;
            yield return null;
        }
        preparingRepetition = false;
        lessonRoutine = null;
        if (skipRequested) { skipRequested = false; ContinueAfterCurrentPlanEntry(); }
        else BeginSegment(index);
    }

    private void BeginSegment(int index)
    {
        if (motionBackend == null || !motionBackend.IsReady ||
            index < 0 || index >= motionBackend.SegmentCount)
        {
            return;
        }

        isHolding = false;
        motionBackend.PlaySegment(index);

        CoachEventBus.InvokeSafely(OnSegmentStarted, new PracticeSegmentPlaybackInfo(
            index,
            currentRepeat,
            currentSetIndex,
            GetSegmentLabel(index),
            CurrentSegmentKind,
            segments[index].coachGuidanceLimbs));
        CoachEventBus.PublishSegmentStarted(new CoachSegmentEvent(
            index,
            currentSetIndex,
            currentRepeat,
            CurrentSegmentKind,
            GetSegmentLabel(index)));
        CoachSegmentEvent segmentEvent = new CoachSegmentEvent(
            index,
            currentSetIndex,
            currentRepeat,
            CurrentSegmentKind,
            GetSegmentLabel(index));
        Debug.Log($"[CoachLesson] scene={gameObject.scene.name} rep={currentRepeat + 1}/{RepetitionsPerSet} segment={index} phase={CurrentSegmentKind} target={segments[index].coachGuidanceLimbs}");
        PlaySegmentVoice(segmentEvent, index);
    }

    private void PlaySegmentVoice(CoachSegmentEvent segmentEvent, int index)
    {
        if (segments == null || index < 0 || index >= segments.Count)
        {
            return;
        }

        AudioClip clip = segments[index].voiceClip;
        if (clip == null)
        {
            string clipName = segments[index].voiceClipName;
            if (string.IsNullOrWhiteSpace(clipName))
            {
                clipName = GetPracticeLessonClipName(index);
                if (!string.IsNullOrWhiteSpace(clipName))
                    clip = VoicePromptManager.LoadCoachClip($"{clipName}");
            }
            else
            {
                clip = Resources.Load<AudioClip>($"Audio/VoicePrompts/{clipName}") ??
                    Resources.Load<AudioClip>(clipName);
            }
        }
        if (clip != null)
        {
            RequestVoicePhase(segmentEvent, CoachVoicePhase.SegmentCue, clip);
        }
    }

    private string GetPracticeLessonClipName(int segmentIndex)
    {
        string scene = SceneManager.GetActiveScene().name.ToLowerInvariant();
        string action = scene.Contains("deadbug") ? "deadbug" :
            scene.Contains("birddog") ? "birddog" :
            scene.Contains("hipthrust") ? "hipthrust" :
            scene.Contains("catcamel") ? "catcamel" :
            scene.Contains("sidebridge") ? "sidebridge" :
            scene.Contains("curlup") ? "curlup" : null;
        if (action == null || segmentIndex < 0 || segmentIndex > 9)
        {
            return null;
        }

        if (CurrentSegmentKind == TrainingSegmentKind.Preparation || CurrentSegmentKind == TrainingSegmentKind.Finish) return null;
        if (CurrentSegmentKind == TrainingSegmentKind.ReturnTransition) return $"Coach_{action}_return";
        return $"Coach_{action}_move";
    }

    // Advance from the current held pose according to the explicit training
    // plan. One repetition is a complete up/down pair, not an entire imported
    // four-variant animation sequence.
    public void AdvanceToNext()
    {
        if (!hasStarted || isComplete || currentPlanIndex < 0 ||
            currentPlanIndex >= playbackPlan.Count)
        {
            return;
        }

        FindObjectOfType<VoicePromptManager>()?.StopAll();
        skipRequested = true;
        if (!isHolding)
        {
            return;
        }

        if (holdAdvanceRoutine != null)
        {
            StopCoroutine(holdAdvanceRoutine);
            holdAdvanceRoutine = null;
        }

        CancelPendingVoicePhase();
        isHolding = false;
        skipRequested = false;
        ContinueAfterHeldSegment();
    }

    private void ContinueAfterHeldSegment()
    {
        motionBackend?.Resume();
        CountCurrentPlanEntry();
        ContinueAfterCurrentPlanEntry();
    }

    private void CountCurrentPlanEntry()
    {
        if (currentPlanEntryCounted || currentPlanIndex < 0 || currentPlanIndex >= playbackPlan.Count)
        {
            return;
        }
        currentPlanEntryCounted = true;
        TrainingSegmentPlanEntry completedEntry = playbackPlan[currentPlanIndex];
        if (completedEntry.completesRepetition)
        {
            completedRepetitionCount = Mathf.Min(
                completedRepetitionCount + 1,
                TotalRepetitions);
        }

        if (completedEntry.completesSet)
        {
            completedSetCount = Mathf.Min(completedSetCount + 1, TotalSets);
            CoachEventBus.InvokeSafely(OnSetProgressChanged, completedSetCount, TotalSets);
        }

    }

    private void ContinueAfterCurrentPlanEntry()
    {
        if (currentPlanIndex + 1 < playbackPlan.Count)
        {
            currentPlanIndex++;
            currentPlanEntryCounted = false;
            ApplyPlanEntry(playbackPlan[currentPlanIndex]);
            PlaySegment(currentSegmentIndex);
            return;
        }

        if (!finishVoiceRequested)
        {
            finishVoiceRequested = true;
            CoachSegmentEvent finishEvent = new CoachSegmentEvent(
                currentSegmentIndex,
                currentSetIndex,
                currentRepeat,
                TrainingSegmentKind.Finish,
                GetSegmentLabel(currentSegmentIndex));
            RequestVoicePhase(finishEvent, CoachVoicePhase.FinishCue);
        }

        // Everything done - keep coach frozen on the final pose.
        isComplete = true;
        motionBackend?.Freeze();

        Debug.Log("SegmentedCoachController: All segments and sets complete.");
        CoachEventBus.PublishSessionCompleted(
            CoachTimelineStatus.Completed,
            CoachEndReason.NormalCompletion);
        CoachEventBus.InvokeSafely(OnAllComplete);
    }

    public void StopTimeline(CoachTimelineStatus status, CoachEndReason reason)
    {
        if (!hasStarted || isComplete)
        {
            return;
        }
        if (lessonRoutine != null) { StopCoroutine(lessonRoutine); lessonRoutine = null; }
        preparingRepetition = false;
        isComplete = true;
        hasStarted = false;
        isHolding = false;
        isPaused = false;
        CancelPendingVoicePhase();
        if (holdAdvanceRoutine != null)
        {
            StopCoroutine(holdAdvanceRoutine);
            holdAdvanceRoutine = null;
        }
        motionBackend?.Freeze();
        if (reason == CoachEndReason.SafetyStop ||
            reason == CoachEndReason.HeadsetDisconnected)
        {
            CoachEventBus.PublishSafetyStopped(reason);
        }
        CoachEventBus.PublishSessionCompleted(status, reason);
    }

    // Backward-compatible aliases (older UI / wiring).
    public void AdvanceToNextStage() => AdvanceToNext();
    public void AdvanceToNextKeyframe() => AdvanceToNext();

    public void PlayAction1()
    {
        TryPlayAction1(out _);
    }

    public bool TryPlayAction1(out string error)
    {
        EnsureDefaultBackend();
        if (motionBackend == null || !motionBackend.IsReady)
        {
            error = "SegmentedCoachController: motion backend not ready.";
            Debug.LogError(error);
            return false;
        }

        if (segments == null || segments.Count == 0)
        {
            error = "SegmentedCoachController has no segments configured.";
            Debug.LogError(error);
            return false;
        }

        List<TrainingSegmentPlanEntry> configuredPlan = BuildTrainingPlan(
            segments.Count,
            TotalSets,
            RepetitionsPerSet,
            repeatBoundarySegmentsPerSet);
        if (configuredPlan.Count == 0)
        {
            error =
                "SegmentedCoachController needs one preparation segment, one " +
                "finish segment, and an even number of core up/down segments.";
            Debug.LogError(error, this);
            return false;
        }

        // 确保 Animator 启用。CoachActionController 在初始化时会禁用 Animator，
        // 即使它本身被禁用，Animator 也可能仍处于禁用状态，导致动画无法播放。
        if (backendType == CoachMotionBackendType.Animator &&
            coachAnimator != null &&
            !coachAnimator.enabled)
        {
            coachAnimator.enabled = true;
        }

        CacheDeadBugPreparationArms();
        currentSetIndex = 0;
        currentSegmentIndex = 0;
        currentRepeat = 0;
        completedSetCount = 0;
        completedRepetitionCount = 0;
        currentPlanIndex = 0;
        currentPlanEntryCounted = false;
        skipRequested = false;
        pendingVoicePhaseCompleted = true;
        finishVoiceRequested = false;
        isHolding = false;
        isComplete = false;
        isPaused = false;
        hasStarted = true;
        playbackPlan.Clear();
        playbackPlan.AddRange(configuredPlan);
        ApplyPlanEntry(playbackPlan[0]);

        CoachEventBus.InvokeSafely(OnSetProgressChanged, 0, TotalSets);
        PlaySegment(currentSegmentIndex);
        error = null;
        return true;
    }

    /// <summary>
    /// Plays the authored coach motion once for the pre-training lesson.
    /// This deliberately does not create a training plan or publish completion.
    /// The caller owns the lesson timing and starts the real session afterwards.
    /// </summary>
    public bool PlayDemonstrationPreview(float seconds, Action completed)
    {
        EnsureDefaultBackend();
        if (motionBackend == null || !motionBackend.IsReady || segments == null || segments.Count == 0)
        {
            return false;
        }

        // The preview follows the authored motion timeline.  The legacy
        // parameter is retained for scene/prefab compatibility, but must not
        // compress or stretch a recording to a fixed wall-clock duration.
        lessonRoutine = StartCoroutine(DemonstrationPreviewRoutine(completed));
        return true;
    }

    private IEnumerator DemonstrationPreviewRoutine(Action completed)
    {
        bool wasEnabled = coachAnimator == null || coachAnimator.enabled;
        CacheDeadBugPreparationArms();
        if (coachAnimator != null && backendType == CoachMotionBackendType.Animator) coachAnimator.enabled = true;
        int segmentCount = Mathf.Max(1, motionBackend.SegmentCount);
        for (int i = 0; i < segmentCount; i++)
        {
            // The 10-node scenes contain four practice repetitions. Demonstrate
            // one complete right/left pair, then the authored finish, without
            // rehearsing the same two sides a second time.
            string action = VoicePromptManager.GetPracticeActionKey();
            if (segmentCount == 10 && i == 5 && (action == "deadbug" || action == "birddog")) i = 9;
            Debug.Log($"[CoachDemo] scene={gameObject.scene.name} segment={i} label={segments[i].label}");
            motionBackend.PlaySegment(i);
            // Tick the backend with unscaled frame time so the source frame
            // timestamps (including pauses/holds authored in the recording)
            // remain the sole timing source.  Observation and scoring are not
            // involved in this lesson-only preview.
            while (!motionBackend.HasReachedSegmentEnd())
            {
                if (MobileCoachControls.IsPaused) motionBackend.Freeze();
                else { motionBackend.Resume(); motionBackend.Tick(Time.unscaledDeltaTime); }
                yield return null;
            }
            motionBackend.SnapToSegmentEnd();
            motionBackend.Freeze();
            if (i > 0 && i < segmentCount - 1 && (i & 1) == 1)
            {
                float hold = Mathf.Max(1f, segments[i].scoringDuration);
                while (hold > 0f)
                {
                    if (!MobileCoachControls.IsPaused) hold -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }
        // Show the authored preparation pose throughout the intact countdown.
        motionBackend.PlaySegment(0);
        motionBackend.SnapToSegmentEnd();
        motionBackend.Freeze();
        if (coachAnimator != null) coachAnimator.enabled = wasEnabled;
        lessonRoutine = null;
        completed?.Invoke();
    }

    private void EnsureDefaultBackend()
    {
        // Public entry points can be invoked before Start (for example by a
        // scene bootstrapper or EditMode validation). Build the serialized
        // Animator backend lazily so the result never depends on call order.
        if (motionBackend == null &&
            coachAnimator != null &&
            segments != null &&
            segments.Count > 0)
        {
            motionBackend = new AnimatorCoachMotionBackend(
                coachAnimator,
                segments,
                idleStateName,
                segmentTransitionBlendSeconds);
            backendType = CoachMotionBackendType.Animator;
        }
    }

    private void ApplyPlanEntry(TrainingSegmentPlanEntry entry)
    {
        currentSegmentIndex = entry.segmentIndex;
        currentSetIndex = entry.setIndex;
        currentRepeat = entry.repetitionIndex;
    }

    public static List<TrainingSegmentPlanEntry> BuildTrainingPlan(
        int totalSegmentCount,
        int sets,
        int repetitionsPerSet,
        bool repeatBoundariesPerSet)
    {
        var plan = new List<TrainingSegmentPlanEntry>();
        int safeSets = Mathf.Max(1, sets);
        int safeRepetitions = Mathf.Max(1, repetitionsPerSet);
        // A complete recorded lesson is a single authored segment. Repeat the
        // recording at its original timestamps for each prescribed repetition;
        // preparation/countdown is owned by the session introduction.
        if (totalSegmentCount == 1)
        {
            for (int set = 0; set < safeSets; set++)
            {
                for (int repetition = 0; repetition < safeRepetitions; repetition++)
                {
                    plan.Add(new TrainingSegmentPlanEntry
                    {
                        segmentIndex = 0,
                        setIndex = set,
                        repetitionIndex = repetition,
                        kind = TrainingSegmentKind.CoreAction,
                        completesRepetition = true,
                        completesSet = repetition == safeRepetitions - 1
                    });
                }
            }
            return plan;
        }
        int coreSegmentCount = totalSegmentCount - 2;
        if (totalSegmentCount < 4 ||
            coreSegmentCount < 2 ||
            coreSegmentCount % 2 != 0)
        {
            return plan;
        }

        int variantCount = coreSegmentCount / 2;
        int globalRepetition = 0;
        if (!repeatBoundariesPerSet)
        {
            plan.Add(new TrainingSegmentPlanEntry(
                0,
                0,
                0,
                TrainingSegmentKind.Preparation,
                false,
                false));
        }

        for (int setIndex = 0; setIndex < safeSets; setIndex++)
        {
            if (repeatBoundariesPerSet)
            {
                plan.Add(new TrainingSegmentPlanEntry(
                    0,
                    setIndex,
                    0,
                    TrainingSegmentKind.Preparation,
                    false,
                    false));
            }

            for (int repetitionIndex = 0;
                 repetitionIndex < safeRepetitions;
                 repetitionIndex++)
            {
                int variantIndex = globalRepetition % variantCount;
                int actionSegmentIndex = 1 + variantIndex * 2;
                bool completesSet =
                    !repeatBoundariesPerSet &&
                    repetitionIndex == safeRepetitions - 1;

                plan.Add(new TrainingSegmentPlanEntry(
                    actionSegmentIndex,
                    setIndex,
                    repetitionIndex,
                    TrainingSegmentKind.CoreAction,
                    false,
                    false));
                plan.Add(new TrainingSegmentPlanEntry(
                    actionSegmentIndex + 1,
                    setIndex,
                    repetitionIndex,
                    TrainingSegmentKind.ReturnTransition,
                    true,
                    completesSet));
                globalRepetition++;
            }

            if (repeatBoundariesPerSet)
            {
                plan.Add(new TrainingSegmentPlanEntry(
                    totalSegmentCount - 1,
                    setIndex,
                    safeRepetitions - 1,
                    TrainingSegmentKind.Finish,
                    false,
                    true));
            }
        }

        if (!repeatBoundariesPerSet)
        {
            plan.Add(new TrainingSegmentPlanEntry(
                totalSegmentCount - 1,
                safeSets - 1,
                safeRepetitions - 1,
                TrainingSegmentKind.Finish,
                false,
                false));
        }

        return plan;
    }

    public static bool HasHoldPhase(TrainingSegmentKind segmentKind)
    {
        return segmentKind != TrainingSegmentKind.Finish;
    }

    private void CacheDeadBugPreparationArms()
    {
        deadBugPreparationArmRotations.Clear();

        // 关键帧后端导出时已把修正烧进数据，无需运行时合成
        if (!composeDeadBugPreparationArms ||
            motionBackend == null ||
            !motionBackend.RequiresRuntimeArmComposition)
        {
            return;
        }

        // Animator 后端走原有逻辑：播放两个手臂伸展状态、抓取旋转
        var animBackend = motionBackend as AnimatorCoachMotionBackend;
        if (animBackend == null || coachAnimator == null)
        {
            return;
        }

        if (!TryResolveStateHash(
                deadBugRightArmExtensionStateName,
                out int rightStateHash) ||
            !TryResolveStateHash(
                deadBugLeftArmExtensionStateName,
                out int leftStateHash))
        {
            Debug.LogWarning(
                "SegmentedCoachController cannot build the Dead Bug preparation " +
                "target because one of the arm-extension states is missing.",
                this);
            return;
        }

        coachAnimator.speed = 1f;
        coachAnimator.Play(rightStateHash, 0, 1f);
        coachAnimator.Update(0f);
        CaptureArmRotations(RightArmBones);

        coachAnimator.Play(leftStateHash, 0, 1f);
        coachAnimator.Update(0f);
        CaptureArmRotations(LeftArmBones);

        Debug.Log(
            $"SegmentedCoachController: built overhead preparation target from " +
            $"{deadBugRightArmExtensionStateName} and " +
            $"{deadBugLeftArmExtensionStateName}.",
            this);
    }

    private bool TryResolveStateHash(string stateName, out int stateHash)
    {
        stateHash = Animator.StringToHash(stateName);
        if (coachAnimator.HasState(0, stateHash))
        {
            return true;
        }

        string layerName = coachAnimator.GetLayerName(0);
        stateHash = Animator.StringToHash($"{layerName}.{stateName}");
        return coachAnimator.HasState(0, stateHash);
    }

    private void CaptureArmRotations(IEnumerable<HumanBodyBones> bones)
    {
        foreach (HumanBodyBones bone in bones)
        {
            Transform transform = coachAnimator.GetBoneTransform(bone);
            if (transform != null)
            {
                deadBugPreparationArmRotations[bone] = transform.localRotation;
            }
        }
    }

    private void ApplyDeadBugPreparationArms()
    {
        if (!composeDeadBugPreparationArms ||
            motionBackend == null ||
            !string.Equals(
                motionBackend.GetSegmentSourceName(currentSegmentIndex),
                deadBugPreparationStateName,
                StringComparison.Ordinal) ||
            deadBugPreparationArmRotations.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<HumanBodyBones, Quaternion> pair in
                 deadBugPreparationArmRotations)
        {
            Transform transform = coachAnimator.GetBoneTransform(pair.Key);
            if (transform != null)
            {
                transform.localRotation = pair.Value;
            }
        }
    }

    /// <summary>
    /// Captures the target used only by anatomical guidance. A known imported
    /// preparation-pose correction is applied and restored inside this call,
    /// so the Animator pose consumed by scoring is never changed.
    /// </summary>
    public Dictionary<HumanBodyBones, Vector3> CaptureGuidanceTargetPose(
        AnatomicalMotionScoringEngine guidanceEngine)
    {
        if (guidanceEngine == null || coachAnimator == null)
        {
            return new Dictionary<HumanBodyBones, Vector3>();
        }

        bool needsCorrection =
            isHolding &&
            composeDeadBugPreparationArms &&
            motionBackend != null &&
            string.Equals(
                motionBackend.GetSegmentSourceName(currentSegmentIndex),
                deadBugPreparationStateName,
                StringComparison.Ordinal) &&
            deadBugPreparationArmRotations.Count > 0;
        if (!needsCorrection)
        {
            return guidanceEngine.CaptureRawPose(coachAnimator);
        }

        var originalRotations =
            new Dictionary<HumanBodyBones, Quaternion>(
                deadBugPreparationArmRotations.Count);
        foreach (HumanBodyBones bone in deadBugPreparationArmRotations.Keys)
        {
            Transform transform = coachAnimator.GetBoneTransform(bone);
            if (transform != null)
            {
                originalRotations[bone] = transform.localRotation;
            }
        }

        ApplyDeadBugPreparationArms();
        Dictionary<HumanBodyBones, Vector3> targetPose =
            guidanceEngine.CaptureRawPose(coachAnimator);

        foreach (KeyValuePair<HumanBodyBones, Quaternion> pair in
                 originalRotations)
        {
            Transform transform = coachAnimator.GetBoneTransform(pair.Key);
            if (transform != null)
            {
                transform.localRotation = pair.Value;
            }
        }

        return targetPose;
    }

    public void SetTrainingVolume(int sets, int repetitions)
    {
        if (hasStarted)
        {
            Debug.LogWarning(
                "SegmentedCoachController cannot change training volume after playback starts.",
                this);
            return;
        }

        setCount = Mathf.Max(1, sets);
        repetitionsPerSet = Mathf.Max(1, repetitions);
    }

    public void SetTotalSequenceCycles(int cycleCount) =>
        SetTrainingVolume(cycleCount, RepetitionsPerSet);

    /// <summary>
    /// 设置动作分段序列（用于统一练习场景的运行时配置）。
    /// 必须在 PlayAction1() 之前调用。
    /// </summary>
    public void SetSegments(ActionSegment[] newSegments)
    {
        if (hasStarted)
        {
            Debug.LogWarning(
                "SegmentedCoachController cannot change segments after playback starts.",
                this);
            return;
        }

        if (newSegments == null || newSegments.Length == 0)
        {
            Debug.LogWarning("SegmentedCoachController.SetSegments: empty segments array", this);
            return;
        }

        segments = new List<ActionSegment>(newSegments);

        // segments 变了，Animator 后端要重建
        if (backendType == CoachMotionBackendType.Animator && coachAnimator != null)
        {
            motionBackend = new AnimatorCoachMotionBackend(
                coachAnimator,
                segments,
                idleStateName,
                segmentTransitionBlendSeconds);
        }

        Debug.Log($"SegmentedCoachController: segments updated to {segments.Count} items", this);
    }

    /// <summary>
    /// 设置关键帧播放后端（服务器下发动作）。segments 从 package 读取。
    /// 必须在 PlayAction1() 之前调用。
    /// </summary>
    public void SetMotionBackend(CoachMotionPackage package, bool lockRootPosition = false,
        float transitionBlendSeconds = 0f)
    {
        if (hasStarted)
        {
            Debug.LogWarning(
                "SegmentedCoachController cannot change backend after playback starts.",
                this);
            return;
        }

        if (package == null)
        {
            Debug.LogError("SegmentedCoachController: motion package is null", this);
            return;
        }

        if (!package.IsValid(out string error))
        {
            Debug.LogError($"SegmentedCoachController: invalid motion package - {error}", this);
            return;
        }

        // 清理旧后端
        keyframeBackend?.Dispose();
        keyframeBackend = null;

        keyframeBackend = new KeyframeCoachMotionBackend(
            coachAnimator, package, lockRootPosition, transitionBlendSeconds);
        motionBackend = keyframeBackend;
        backendType = CoachMotionBackendType.Keyframe;

        // 从 package 拉取 segments 参数（复制到 ActionSegment，PoseScorer 等会读）
        segments.Clear();
        foreach (CoachMotionSegment seg in package.segments)
        {
            segments.Add(new ActionSegment
            {
                label = seg.label,
                clipStateName = seg.sourceStateName,
                repeatCount = seg.repeatCount,
                scoreLeniency = seg.scoreLeniency,
                scoringDuration = seg.scoringDuration,
                applyFormalActionScoreMapping = seg.applyFormalActionScoreMapping,
                poseGuidanceRule = seg.poseGuidanceRule,
                coachGuidanceLimbs = (CoachGuidanceLimb)((int)seg.poseGuidanceRule.activeRegions & 15),
                voiceClipName = seg.voiceClipName,
                voiceClip = null // 语音按名查找，运行时加载
            });
        }

        setCount = Mathf.Max(1, package.setCount);

        Debug.Log(
            $"SegmentedCoachController: switched to Keyframe backend, " +
            $"{segments.Count} segments, {setCount} sets.",
            this);
    }

    public void ReturnIdle()
    {
        if (lessonRoutine != null) { StopCoroutine(lessonRoutine); lessonRoutine = null; }
        preparingRepetition = false;
        motionBackend?.ReturnIdle();

        if (holdAdvanceRoutine != null)
        {
            StopCoroutine(holdAdvanceRoutine);
            holdAdvanceRoutine = null;
        }
        hasStarted = false;
        isPaused = false;
        isHolding = false;
        isComplete = false;
        currentSetIndex = 0;
        currentSegmentIndex = 0;
        currentRepeat = 0;
        completedSetCount = 0;
        completedRepetitionCount = 0;
        currentPlanIndex = 0;
        currentPlanEntryCounted = false;
        playbackPlan.Clear();

        CoachEventBus.InvokeSafely(OnSetProgressChanged, 0, TotalSets);
    }

    public void TogglePause()
    {
        if (coachAnimator == null || !hasStarted || isComplete)
        {
            return;
        }

        bool pause = !IsPaused;
        MobileCoachControls mobileControls =
            FindObjectOfType<MobileCoachControls>(true);
        mobileControls?.SetPaused(pause);
        isPaused = pause;
        if (pause)
        {
            motionBackend.Freeze();
        }
        else
        {
            if (!isHolding)
            {
                motionBackend.Resume();
            }
        }
    }

    // --- Read-only progress for UI / scoring ---

    public CoachGuidanceLimb CurrentRepetitionTarget
    {
        get
        {
            int index = currentSegmentIndex;
            if (CurrentSegmentKind == TrainingSegmentKind.ReturnTransition) index--;
            return index >= 0 && index < segments.Count ? segments[index].coachGuidanceLimbs : CoachGuidanceLimb.None;
        }
    }
    public SegmentPoseGuidanceRule CurrentPoseGuidanceRule =>
        currentSegmentIndex >= 0 && currentSegmentIndex < segments.Count
            ? segments[currentSegmentIndex].poseGuidanceRule : SegmentPoseGuidanceRule.Auto;
    public bool IsHoldingAtSegment => isHolding;
    public bool IsPreparationHold =>
        isHolding && CurrentSegmentKind == TrainingSegmentKind.Preparation;
    public bool UsesDeadBugPreparationPose => composeDeadBugPreparationArms;
    public bool IsDeadBugPreparationHold =>
        IsPreparationHold &&
        composeDeadBugPreparationArms &&
        motionBackend != null &&
        motionBackend.RequiresRuntimeArmComposition &&
        string.Equals(
            motionBackend.GetSegmentSourceName(currentSegmentIndex),
            deadBugPreparationStateName,
            StringComparison.Ordinal);
    public bool IsWaitingForNext => isHolding; // backward-compatible alias
    public bool IsComplete => isComplete;
    public bool HasStarted => hasStarted;
    public bool IsPaused => isPaused || MobileCoachControls.IsPaused;
    public Animator CoachAnimator => coachAnimator;

    public bool HasNextAfterCurrentHold
    {
        get
        {
            if (!isHolding || isComplete || segments == null || segments.Count == 0)
            {
                return false;
            }

            return currentPlanIndex + 1 < playbackPlan.Count;
        }
    }

    public int CurrentSetIndex => currentSetIndex;
    public int TotalSets => Mathf.Max(1, setCount);
    public int CompletedSets => completedSetCount;
    public int RepetitionsPerSet => Mathf.Max(1, repetitionsPerSet);
    public int TotalRepetitions
    {
        get
        {
            long total = (long)TotalSets * RepetitionsPerSet;
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }
    }
    public int CompletedRepetitions => completedRepetitionCount;

    public int CurrentSegmentIndex => currentSegmentIndex;
    public int TotalSegments => segments?.Count ?? 0;

    public int CurrentRepeat => currentRepeat;
    public int CurrentSegmentRepeatCount =>
        currentPlanIndex >= 0 &&
        currentPlanIndex < playbackPlan.Count &&
        (playbackPlan[currentPlanIndex].kind == TrainingSegmentKind.CoreAction ||
         playbackPlan[currentPlanIndex].kind == TrainingSegmentKind.ReturnTransition)
            ? RepetitionsPerSet
            : 1;
    public TrainingSegmentKind CurrentSegmentKind =>
        currentPlanIndex >= 0 && currentPlanIndex < playbackPlan.Count
            ? playbackPlan[currentPlanIndex].kind
            : TrainingSegmentKind.Preparation;
    public bool RepeatsBoundarySegmentsPerSet => repeatBoundarySegmentsPerSet;
    public float SegmentTransitionBlendSeconds =>
        Mathf.Max(0f, segmentTransitionBlendSeconds);

    public string CurrentSegmentLabel => GetSegmentLabel(currentSegmentIndex);

    public string GetSegmentLabel(int index)
    {
        if (segments == null || index < 0 || index >= segments.Count)
        {
            return string.Empty;
        }

        return segments[index].label ?? string.Empty;
    }

    public CoachGuidanceLimb GetCoachGuidanceLimbs(int index)
    {
        return segments != null && index >= 0 && index < segments.Count
            ? segments[index].coachGuidanceLimbs
            : CoachGuidanceLimb.None;
    }

}
