using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serializes coach-mode voice prompts and scoring beats so state changes cannot
/// restart or overlap the same announcement every frame.
/// </summary>
public class VoicePromptManager : MonoBehaviour
{
    public const float StabilityPromptGapSeconds = 0.15f;
    public const float StabilityNumberGapSeconds = 0.15f;
    public const float StabilityMissingNumberStepSeconds = 0.65f;

    private enum PromptKind
    {
        None,
        PracticeIntroduction,
        StabilityCountdown,
        ScoringBeats,
        ScoreFinished,
        NextAction,
        FollowCoach,
        FollowingGuidance,
        PhaseCue
    }

    [Header("语音文件路径 (Resources目录相对路径)")]
    [SerializeField] private string voiceFolder = "Audio/VoicePrompts";

    [Header("语音片段")]
    [Tooltip("'请保持住' 语音")]
    [SerializeField] private AudioClip keepStill;
    [Tooltip("'2' 语音")]
    [SerializeField] private AudioClip count2;
    [Tooltip("遗留序列化兼容字段；正式 Practice 不再播放独立的1")]
    [SerializeField] private AudioClip count1;
    [Tooltip("'开始' 语音")]
    [SerializeField] private AudioClip start;
    [Tooltip("'请跟随教练前往下一个动作' 语音")]
    [SerializeField] private AudioClip nextAction;
    [Tooltip("'请根据教练演示调整动作' 语音")]
    [SerializeField] private AudioClip adjustPose;

    [Header("Practice Introduction")]
    [Tooltip("Introduces the coach while only the coach avatar is visible.")]
    [SerializeField] private AudioClip coachIntroduction;
    [Tooltip("Introduces the tracked avatar while only the user's model is visible.")]
    [SerializeField] private AudioClip userModelIntroduction;
    [Tooltip("Plays the ready prompt before formal Practice begins.")]
    [SerializeField] private AudioClip readyCountdown;
    [Tooltip("Short visual lead-in before each introduction line begins.")]
    [SerializeField] [Min(0f)] private float introductionVisualLeadSeconds = 0.3f;
    [Tooltip("Pause between the coach and user introduction stages.")]
    [SerializeField] [Min(0f)] private float introductionStageGapSeconds = 0.35f;
    [Tooltip("Keeps a stage visible when its audio clip is unavailable.")]
    [SerializeField] [Min(0f)] private float missingIntroductionClipSeconds = 1.25f;
    [Tooltip("Patient preparation window after the recorded coach asks the patient to lie down.")]
    [SerializeField] [Min(0f)] private float recordedPreparationWaitSeconds = 2.5f;

    [Header("节拍音效")]
    [Tooltip("节拍音 'deng'")]
    [SerializeField] private AudioClip beatDeng;
    [Tooltip("结束音 'ding'")]
    [SerializeField] private AudioClip beatDing;

    [Header("播放设置")]
    [SerializeField] [Range(0f, 1f)] private float voiceVolume = 0.8f;
    [SerializeField] [Range(0f, 1f)] private float beatVolume = 0.6f;
    [Tooltip("同一种提示被取消后，允许再次触发前至少等待的秒数。")]
    [SerializeField] [Min(0f)] private float repeatGuardSeconds = 1.5f;

    private AudioSource voiceSource;
    private AudioSource beatSource;
    private Coroutine currentSequence;
    private PromptKind activePrompt;
    private PromptKind lastStartedPrompt;
    private float lastPromptStartedAt = float.NegativeInfinity;
    private PromptKind lastEndedPrompt;
    private float lastPromptEndedAt = float.NegativeInfinity;
    private bool followCoachAnnounced;
    private bool audioPaused;
    private bool phasePlaybackActive;
    private bool activePhase;
    private CoachVoicePhaseRequest activePhaseRequest;
    private bool phaseCancelRequested;
    private int lastGuidanceSegmentId = -1;
    private readonly Queue<CoachVoicePhaseRequest> phaseQueue =
        new Queue<CoachVoicePhaseRequest>();

    public bool IsBusy => activePrompt != PromptKind.None;
    public bool IsPhasePlaybackActive => phasePlaybackActive;
    public bool IsPlayingPracticeIntroduction =>
        activePrompt == PromptKind.PracticeIntroduction;
    public bool IsPlayingStabilityCountdown => activePrompt == PromptKind.StabilityCountdown;
    public string ActivePromptName => activePrompt.ToString();
    public float StabilityCountdownDurationSeconds =>
        (keepStill != null ? keepStill.length + StabilityPromptGapSeconds : 0f) +
        (count2 != null ? count2.length + StabilityNumberGapSeconds : StabilityMissingNumberStepSeconds) +
        (start != null ? start.length : 0f);
    public float PracticeIntroductionDurationSeconds =>
        3f * Mathf.Max(0f, introductionVisualLeadSeconds) +
        2f * Mathf.Max(0f, introductionStageGapSeconds) +
        (LoadPracticeLessonClip("recorded_lie_down") != null
            ? LoadPracticeLessonClip("recorded_lie_down").length + recordedPreparationWaitSeconds
            : 0f) +
        (coachIntroduction != null ? coachIntroduction.length : Mathf.Max(0f, missingIntroductionClipSeconds)) +
        (userModelIntroduction != null ? userModelIntroduction.length : Mathf.Max(0f, missingIntroductionClipSeconds)) +
        (readyCountdown != null ? readyCountdown.length : Mathf.Max(0f, missingIntroductionClipSeconds));

    private void Awake()
    {
        voiceSource = CreateAudioSource();
        beatSource = CreateAudioSource();
    }

    private void OnEnable()
    {
        CoachEventBus.CoachVoicePhaseRequested += HandleVoicePhaseRequested;
        CoachEventBus.CoachVoicePhaseCancelRequested += HandleVoicePhaseCancelRequested;
        CoachEventBus.CoachSegmentStarted += HandleCoachSegmentStarted;
        CoachEventBus.CoachHardStopRequested += HandleCoachHardStopRequested;
        CoachEventBus.CoachSessionCompleted += HandleCoachSessionCompleted;
    }

    private void HandleCoachSegmentStarted(CoachSegmentEvent segment)
    {
        if (segment.segmentKind == TrainingSegmentKind.CoreAction ||
            segment.segmentKind == TrainingSegmentKind.ReturnTransition)
        {
            PlayGuidanceBeat();
        }
    }

    /// <summary>Plays a short non-blocking movement/countdown beat.</summary>
    public void PlayGuidanceBeat()
    {
        if (beatSource != null && beatDeng != null && !MobileCoachControls.IsPaused)
        {
            beatSource.PlayOneShot(beatDeng, beatVolume);
        }
    }

    private void HandleCoachHardStopRequested(CoachEndReason reason)
    {
        StopAll();
    }

    private void HandleCoachSessionCompleted(
        CoachTimelineStatus status,
        CoachEndReason reason)
    {
        if (status != CoachTimelineStatus.Completed)
        {
            StopAll();
        }
    }

    private void Update()
    {
        bool shouldPause = MobileCoachControls.IsPaused;
        if (shouldPause == audioPaused)
        {
            return;
        }

        audioPaused = shouldPause;
        if (audioPaused)
        {
            voiceSource?.Pause();
            beatSource?.Pause();
        }
        else
        {
            voiceSource?.UnPause();
            beatSource?.UnPause();
        }
    }

    /// <summary>
    /// Introduces the coach-only lesson, then counts down before formal
    /// playback starts. Visual callbacks are synchronized with the voice stage.
    /// </summary>
    public bool PlayPracticeIntroduction(
        Action showCoach,
        Action showUserModel,
        Action showBoth,
        Action onComplete)
    {
        if (activePrompt != PromptKind.None)
        {
            return false;
        }

        BeginSequence(
            PromptKind.PracticeIntroduction,
            PracticeIntroductionSequence(
                showCoach,
                showUserModel,
                showBoth,
                onComplete));
        return true;
    }

    public void CancelPracticeIntroduction()
    {
        if (activePrompt == PromptKind.PracticeIntroduction)
        {
            StopCurrentSequence(true);
        }
    }

    /// <summary>
    /// Plays "请保持住，2，1，开始" once. Returns false when an identical
    /// prompt is already active or still inside the repeat guard window.
    /// </summary>
    public bool PlayStabilityCountdown(Action onComplete = null)
    {
        // This prompt is entered once by the flow state machine after a full
        // continuous-stability window. A repeat-time guard here could strand
        // PreparingScore without a completion callback.
        if (activePrompt != PromptKind.None)
        {
            return false;
        }

        BeginSequence(PromptKind.StabilityCountdown, StabilityCountdownSequence(onComplete));
        return true;
    }

    public void CancelStabilityCountdown()
    {
        if (activePrompt == PromptKind.StabilityCountdown)
        {
            StopCurrentSequence(true);
        }
    }

    /// <summary>
    /// Plays only the repeating scoring beats. The completion sound and optional
    /// next-action announcement are owned by FinishScoring so they cannot overlap.
    /// </summary>
    public bool PlayScoringBeats(
        float duration,
        Action onBeat = null,
        Action onComplete = null)
    {
        if (activePrompt != PromptKind.None)
        {
            return false;
        }

        BeginSequence(
            PromptKind.ScoringBeats,
            ScoringBeatsSequence(duration, onBeat, onComplete));
        return true;
    }

    /// <summary>
    /// Stops the scoring beats, plays the completion sound once, then optionally
    /// announces the next action. The final checkpoint passes false.
    /// </summary>
    public void FinishScoring(bool announceNextAction)
    {
        if (activePrompt != PromptKind.None &&
            activePrompt != PromptKind.ScoringBeats)
        {
            return;
        }

        BeginSequence(PromptKind.ScoreFinished, ScoreFinishedSequence(announceNextAction));
    }

    public bool PlayNextAction()
    {
        return PlaySinglePrompt(PromptKind.NextAction, ResolveRestGuidanceClip(), voiceVolume);
    }

    public bool PlayAdjustPose()
    {
        if (GetPracticeActionKey() != null) return false;
        // During formal playback the phase cue owns the voice channel. Pose
        // observation can still drive red/green feedback, but a generic
        // correction sentence must not jump in between action cues.
        if (phasePlaybackActive)
        {
            return false;
        }

        return PlayFollowCoach();
    }

    /// <summary>
    /// Announces the user-following phase once. Per-frame repeats are rejected
    /// by the active and repeat guards.
    /// </summary>
    public bool PlayFollowCoach()
    {
        AudioClip practiceAdjust = GetPracticeActionKey() == null
            ? null
            : LoadCoachClip($"Coach_{GetPracticeActionKey()}_adjust");
        bool started = PlaySinglePrompt(
            PromptKind.FollowCoach,
            practiceAdjust != null ? practiceAdjust : adjustPose,
            voiceVolume);
        followCoachAnnounced |= started;
        return started;
    }

    /// <summary>
    /// Preserved full guidance sequence used by the playback phase adapter.
    /// The original adjust/hold/countdown/start prompts are played in order and
    /// completion is reported only after the last clip has finished.
    /// </summary>
    public bool PlayFollowingGuidance(bool includeFollowPrompt)
    {
        if (activePrompt != PromptKind.None)
        {
            return false;
        }

        followCoachAnnounced |= includeFollowPrompt;
        BeginSequence(
            PromptKind.FollowingGuidance,
            FollowingGuidanceSequence(includeFollowPrompt));
        return true;
    }

    public void StopAll()
    {
        phaseQueue.Clear();
        phaseCancelRequested = false;
        pendingCorrection = null;
        correctionCandidate = null;
        correctionStableSeconds = 0f;
        phasePlaybackActive = false;
        lastGuidanceSegmentId = -1;
        StopCurrentSequence(true);
        followCoachAnnounced = false;
    }

    private string correctionCandidate;
    private float correctionStableSeconds;
    private AudioClip pendingCorrection;
    public float LessonRestDuration => (ResolveRestGuidanceClip() != null ? ResolveRestGuidanceClip().length : 0f) +
        (pendingCorrection != null ? pendingCorrection.length : 0f) + 0.1f;

    public void ClearLessonCorrection()
    {
        pendingCorrection = null;
        correctionCandidate = null;
        correctionStableSeconds = 0f;
    }

    public void ObserveLessonCorrection(string part, bool valid)
    {
        var coach = FindObjectOfType<SegmentedCoachController>();
        if (!valid || coach == null || !coach.HasStarted || coach.IsPreparingRepetition ||
            coach.CurrentSegmentKind != TrainingSegmentKind.CoreAction)
        {
            if (coach != null && coach.CurrentSegmentKind == TrainingSegmentKind.CoreAction)
                pendingCorrection = null;
            correctionCandidate = null;
            correctionStableSeconds = 0f;
            return;
        }
        if (correctionCandidate != part) { correctionCandidate = part; correctionStableSeconds = 0f; pendingCorrection = null; }
        correctionStableSeconds += Time.deltaTime;
        if (correctionStableSeconds >= 1f)
        {
            string action = GetPracticeActionKey();
            string suffix = action == "deadbug" || action == "birddog" ? "adjust_" + part : "adjust";
            pendingCorrection = LoadPracticeLessonClip(suffix);
        }
    }

    private void HandleVoicePhaseRequested(CoachVoicePhaseRequest request)
    {
        phasePlaybackActive = true;
        // A phase request is tied to the current animation node. If playback
        // advances before the channel is free, an old FIFO queue makes stale
        // instructions speak during the next action. Keep only the newest
        // pending phase instead.
        phaseQueue.Clear();
        phaseQueue.Enqueue(request);
        StartNextPhaseIfIdle();
    }

    private void HandleVoicePhaseCancelRequested(int requestId)
    {
        if (activePhase && activePhaseRequest.requestId == requestId)
        {
            // Finish the sentence already in the listener's ear. The caller
            // has advanced the playback timeline, so discard the remainder of
            // this phase and any queued stale phase without cutting audio.
            phaseCancelRequested = true;
            phaseQueue.Clear();
            return;
        }

        if (phaseQueue.Count == 0)
        {
            return;
        }

        Queue<CoachVoicePhaseRequest> retained =
            new Queue<CoachVoicePhaseRequest>(phaseQueue.Count);
        while (phaseQueue.Count > 0)
        {
            CoachVoicePhaseRequest queued = phaseQueue.Dequeue();
            if (queued.requestId != requestId)
            {
                retained.Enqueue(queued);
            }
        }

        while (retained.Count > 0)
        {
            phaseQueue.Enqueue(retained.Dequeue());
        }
    }

    private void StartNextPhaseIfIdle()
    {
        if (activePrompt != PromptKind.None || phaseQueue.Count == 0)
        {
            return;
        }

        activePhaseRequest = phaseQueue.Dequeue();
        activePhase = true;
        phaseCancelRequested = false;
        StopCurrentSequence(true);
        activePhase = true;
        activePrompt = PromptKind.PhaseCue;
        lastStartedPrompt = PromptKind.PhaseCue;
        lastPromptStartedAt = Time.unscaledTime;
        Debug.Log("[CoachVoice] start=PhaseCue", this);
        currentSequence = StartCoroutine(PhaseSequence(activePhaseRequest));
    }

    private IEnumerator PhaseSequence(CoachVoicePhaseRequest request)
    {
        switch (request.phase)
        {
            case CoachVoicePhase.SegmentCue:
                AudioClip segmentClip = ResolveRequestedClip(request);
                if (segmentClip == null)
                {
                    string cueName = request.clip != null
                        ? request.clip.name
                        : request.clipName;
                    Debug.LogWarning(
                        $"[CoachVoice] Missing segment cue audio '{cueName}'. " +
                        "Check the motion package reference or Resources/Audio/VoicePrompts.",
                        this);
                    break;
                }
                yield return PlayAndWait(
                    voiceSource,
                    segmentClip,
                    voiceVolume);
                break;
            case CoachVoicePhase.HoldGuidance:
                yield return HoldGuidanceSequenceBody(request);
                break;
            case CoachVoicePhase.RestGuidance:
                AudioClip correction = pendingCorrection;
                pendingCorrection = null;
                yield return PlayAndWait(voiceSource, ResolveRestGuidanceClip(), voiceVolume);
                if (!phaseCancelRequested) yield return PlayAndWait(voiceSource, correction, voiceVolume);
                break;
            case CoachVoicePhase.SelfPracticeTransition:
                AudioClip selfPractice = Resources.Load<AudioClip>(
                    "CoachLessons/Coach_self_practice_transition");
                if (selfPractice != null)
                {
                    yield return PlayAndWait(voiceSource, selfPractice, voiceVolume);
                }
                break;
            case CoachVoicePhase.FinishCue:
                yield return PlayAndWait(beatSource, beatDing, beatVolume);
                break;
        }

        CompleteSequence(null);
    }

    private AudioClip ResolveRequestedClip(CoachVoicePhaseRequest request)
    {
        if (request.clip != null)
        {
            return request.clip;
        }

        if (string.IsNullOrWhiteSpace(request.clipName))
        {
            return null;
        }

        AudioClip clip = Resources.Load<AudioClip>(
            $"{voiceFolder.TrimEnd('/')}/{request.clipName}");
        clip ??= Resources.Load<AudioClip>(request.clipName);
        if (clip != null)
        {
            return clip;
        }

        AudioClip[] serializedClips =
        {
            keepStill,
            count2,
            count1,
            start,
            nextAction,
            adjustPose,
            coachIntroduction,
            userModelIntroduction,
            readyCountdown,
            beatDeng,
            beatDing
        };
        foreach (AudioClip serializedClip in serializedClips)
        {
            if (serializedClip != null && string.Equals(
                    serializedClip.name,
                    request.clipName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return serializedClip;
            }
        }

        return null;
    }

    private AudioSource CreateAudioSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }

    private AudioClip ResolveRestGuidanceClip()
    {
        string action = GetPracticeActionKey();
        if (action == "deadbug" || action == "birddog")
        {
            AudioClip recorded = Resources.Load<AudioClip>(
                "CoachLessons/Coach_self_practice_return");
            if (recorded != null)
            {
                return recorded;
            }
        }
        return nextAction;
    }

    private bool PlaySinglePrompt(PromptKind kind, AudioClip clip, float volume)
    {
        if (clip == null || !CanStart(kind))
        {
            return false;
        }

        BeginSequence(kind, SinglePromptSequence(clip, volume));
        return true;
    }

    private bool CanStart(PromptKind kind)
    {
        // Ordinary prompts never interrupt one another and are never queued.
        // If their event has already passed when audio becomes idle, the stale
        // prompt is simply omitted instead of playing late.
        if (activePrompt != PromptKind.None)
        {
            return false;
        }

        bool startGuardPassed =
            lastStartedPrompt != kind ||
            Time.unscaledTime - lastPromptStartedAt >= repeatGuardSeconds;
        bool endGuardPassed =
            lastEndedPrompt != kind ||
            Time.unscaledTime - lastPromptEndedAt >= repeatGuardSeconds;
        return startGuardPassed && endGuardPassed;
    }

    private void BeginSequence(PromptKind kind, IEnumerator sequence)
    {
        StopCurrentSequence(true);
        activePrompt = kind;
        lastStartedPrompt = kind;
        lastPromptStartedAt = Time.unscaledTime;
        Debug.Log($"[CoachVoice] start={kind}", this);
        currentSequence = StartCoroutine(sequence);
    }

    private void StopCurrentSequence(bool stopAudio)
    {
        PromptKind stoppedPrompt = activePrompt;
        if (currentSequence != null)
        {
            StopCoroutine(currentSequence);
            currentSequence = null;
        }

        activePrompt = PromptKind.None;
        activePhase = false;

        if (!stopAudio)
        {
            return;
        }

        if (voiceSource != null)
        {
            voiceSource.Stop();
            voiceSource.clip = null;
        }

        if (beatSource != null)
        {
            beatSource.Stop();
            beatSource.clip = null;
        }

        if (stoppedPrompt != PromptKind.None)
        {
            lastEndedPrompt = stoppedPrompt;
            lastPromptEndedAt = Time.unscaledTime;
            Debug.Log($"[CoachVoice] stop={stoppedPrompt}", this);
        }
    }

    private IEnumerator StabilityCountdownSequence(Action onComplete)
    {
        if (keepStill != null)
        {
            yield return PlayAndWait(voiceSource, keepStill, voiceVolume);
            yield return WaitRealtime(StabilityPromptGapSeconds);
        }

        if (count2 != null)
        {
            yield return PlayAndWait(voiceSource, count2, voiceVolume);
            yield return WaitRealtime(StabilityNumberGapSeconds);
        }
        else
        {
            yield return WaitRealtime(
                StabilityMissingNumberStepSeconds);
        }

        if (start != null)
        {
            yield return PlayAndWait(voiceSource, start, voiceVolume);
        }

        CompleteSequence(onComplete);
    }

    private IEnumerator PracticeIntroductionSequence(
        Action showCoach,
        Action showUserModel,
        Action showBoth,
        Action onComplete)
    {
        // The action-specific lesson and authored demonstration share one
        // visible timeline. During the lesson only the coach is shown; the
        // tracked-user model is revealed when formal patient playback begins.
        showCoach?.Invoke();
        yield return WaitRealtime(introductionVisualLeadSeconds);
        AudioClip recordedLieDown = LoadPracticeLessonClip("recorded_lie_down");
        if (recordedLieDown != null)
        {
            yield return PlayIntroductionClipOrFallback(recordedLieDown);
            yield return WaitRealtime(recordedPreparationWaitSeconds);
        }
        AudioClip lessonSetup = LoadPracticeLessonClip("lesson");
        if (lessonSetup != null)
        {
            yield return PlayIntroductionClipOrFallback(lessonSetup);
        }
        else
        {
            // Legacy scenes without an action lesson retain the generic voice
            // prompts instead of showing an empty teaching window.
            yield return PlayIntroductionClipOrFallback(coachIntroduction);
            yield return WaitRealtime(introductionStageGapSeconds);
            yield return PlayIntroductionClipOrFallback(userModelIntroduction);
        }
        // Countdown belongs after the complete demonstration.

        CompleteSequence(onComplete);
    }

    public void PlayLessonCountdown(Action completed)
    {
        BeginSequence(PromptKind.PracticeIntroduction, LessonCountdownSequence(completed));
    }

    private IEnumerator LessonCountdownSequence(Action completed)
    {
        yield return PlayIntroductionClipOrFallback(readyCountdown);
        AudioClip recordedPreparation = LoadPracticeLessonClip("recorded_prepare");
        if (recordedPreparation != null)
        {
            yield return PlayIntroductionClipOrFallback(recordedPreparation);
        }
        // Formal Practice starts after the preparation phrase. Numeric
        // countdowns are reserved for the later self-practice hold overlay.
        CompleteSequence(completed);
    }

    private static AudioClip LoadPracticeLessonClip(string phase)
    {
        string action = GetPracticeActionKey();
        return string.IsNullOrEmpty(action)
            ? null
            : LoadCoachClip($"Coach_{action}_{phase}");
    }

    private IEnumerator PlayIntroductionClipOrFallback(AudioClip clip)
    {
        if (voiceSource == null || clip == null)
        {
            yield return WaitRealtime(missingIntroductionClipSeconds);
            yield break;
        }

        PlayClip(voiceSource, clip, voiceVolume);
        yield return WaitRealtime(clip.length);
    }

    private IEnumerator FollowingGuidanceSequence(bool includeFollowPrompt)
    {
        yield return FollowingGuidanceSequenceBody(includeFollowPrompt);
        CompleteSequence(null);
    }

    private IEnumerator FollowingGuidanceSequenceBody(bool includeFollowPrompt)
    {
        AudioClip practiceAdjust = GetPracticeActionKey() == null
            ? null
            : LoadCoachClip($"Coach_{GetPracticeActionKey()}_adjust");
        AudioClip guidanceClip = practiceAdjust != null ? practiceAdjust : adjustPose;
        if (includeFollowPrompt && guidanceClip != null)
        {
            followCoachAnnounced = true;
            yield return PlayAndWait(voiceSource, guidanceClip, voiceVolume);
        }

        if (keepStill != null)
        {
            yield return PlayAndWait(voiceSource, keepStill, voiceVolume);
            yield return WaitRealtime(StabilityPromptGapSeconds);
        }
        if (count2 != null)
        {
            yield return PlayAndWait(voiceSource, count2, voiceVolume);
            yield return WaitRealtime(StabilityNumberGapSeconds);
        }
        if (start != null)
        {
            yield return PlayAndWait(voiceSource, start, voiceVolume);
        }
    }

    private IEnumerator HoldGuidanceSequenceBody(CoachVoicePhaseRequest request)
    {
        if (request.segment.segmentKind != TrainingSegmentKind.CoreAction)
        {
            yield break;
        }

        string action = GetPracticeActionKey();
        int index = request.segment.segmentId;
        if (action != null && (index & 1) == 1)
        {
            string holdKey = "hold";
            AudioClip lessonHold = LoadCoachClip($"Coach_{action}_{holdKey}");
            if (lessonHold != null)
            {
                yield return PlayAndWait(voiceSource, lessonHold, voiceVolume);
                yield break;
            }
        }

        if (action == null && keepStill != null)
        {
            yield return PlayAndWait(voiceSource, keepStill, voiceVolume);
        }
    }

    public static AudioClip LoadCoachClip(string clipName)
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (RecordedAlternatingCoachMode.IsRecordedScene(scene))
        {
            string variant = clipName.Replace("Coach_deadbug_", "Coach_deadbug_recorded_")
                .Replace("Coach_birddog_", "Coach_birddog_recorded_");
            var clip = Resources.Load<AudioClip>("CoachLessons/" + variant);
            if (clip != null) return clip;
        }
        return Resources.Load<AudioClip>("CoachLessons/" + clipName);
    }

    public static string GetPracticeActionKey()
    {
        string scene = UnityEngine.SceneManagement.SceneManager
            .GetActiveScene().name.ToLowerInvariant();
        if (scene.Contains("deadbug")) return "deadbug";
        if (scene.Contains("birddog")) return "birddog";
        if (scene.Contains("hipthrust")) return "hipthrust";
        if (scene.Contains("catcamel")) return "catcamel";
        if (scene.Contains("sidebridge")) return "sidebridge";
        if (scene.Contains("curlup")) return "curlup";
        return null;
    }

    private static IEnumerator WaitRealtime(float seconds)
    {
        float duration = Mathf.Max(0f, seconds);
        while (duration > 0f)
        {
            yield return null;
            if (!MobileCoachControls.IsPaused) duration -= Time.unscaledDeltaTime;
        }
    }

    private IEnumerator ScoringBeatsSequence(
        float duration,
        Action onBeat,
        Action onComplete)
    {
        float safeDuration = Mathf.Max(0f, duration);
        int beatCount = Mathf.Max(1, Mathf.RoundToInt(safeDuration));
        float interval = beatCount > 0 ? safeDuration / beatCount : safeDuration;

        for (int i = 0; i < beatCount; i++)
        {
            if (beatDeng != null)
            {
                PlayClip(beatSource, beatDeng, beatVolume);
            }
            onBeat?.Invoke();
            yield return WaitRealtime(interval);
        }

        CompleteSequence(onComplete);
    }

    private IEnumerator ScoreFinishedSequence(bool announceNextAction)
    {
        if (beatDing != null)
        {
            yield return PlayAndWait(beatSource, beatDing, beatVolume);
        }

        if (announceNextAction && nextAction != null)
        {
            lastStartedPrompt = PromptKind.NextAction;
            lastPromptStartedAt = Time.unscaledTime;
            yield return PlayAndWait(voiceSource, nextAction, voiceVolume);
        }

        CompleteSequence(null);
    }

    private IEnumerator SinglePromptSequence(AudioClip clip, float volume)
    {
        yield return PlayAndWait(voiceSource, clip, volume);
        CompleteSequence(null);
    }

    private IEnumerator PlayAndWait(AudioSource source, AudioClip clip, float volume)
    {
        if (source == null || clip == null)
        {
            yield break;
        }

        PlayClip(source, clip, volume);
        yield return WaitRealtime(clip.length);
    }

    private static void PlayClip(AudioSource source, AudioClip clip, float volume)
    {
        source.Stop();
        source.clip = clip;
        source.volume = volume;
        source.Play();
        Debug.Log($"[CoachAudio] clip={clip.name} seconds={clip.length:F3} pitch={source.pitch:F1}");
    }

    private void CompleteSequence(Action onComplete)
    {
        PromptKind completedPrompt = activePrompt;
        bool completedPhase = activePhase;
        bool cancelledPhase = completedPhase && phaseCancelRequested;
        int completedPhaseId = activePhaseRequest.requestId;
        currentSequence = null;
        activePrompt = PromptKind.None;
        activePhase = false;
        phaseCancelRequested = false;
        lastEndedPrompt = completedPrompt;
        lastPromptEndedAt = Time.unscaledTime;
        Debug.Log($"[CoachVoice] complete={completedPrompt}", this);
        onComplete?.Invoke();
        if (completedPhase && !cancelledPhase)
        {
            CoachEventBus.PublishVoicePhaseCompleted(completedPhaseId);
        }
        // Phase requests queue behind any legacy follow/correction prompt so
        // the current sentence is never cut off. Start the next phase after
        // every sequence, regardless of which sequence released the channel.
        StartNextPhaseIfIdle();
    }

    private void OnDisable()
    {
        CoachEventBus.CoachVoicePhaseRequested -= HandleVoicePhaseRequested;
        CoachEventBus.CoachVoicePhaseCancelRequested -= HandleVoicePhaseCancelRequested;
        CoachEventBus.CoachSegmentStarted -= HandleCoachSegmentStarted;
        CoachEventBus.CoachHardStopRequested -= HandleCoachHardStopRequested;
        CoachEventBus.CoachSessionCompleted -= HandleCoachSessionCompleted;
        StopAll();
    }
}
