using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class JsonPracticeCoachController : MonoBehaviour
{
    public event Action<SegmentHoldInfo> OnSegmentHold;
    public event Action<PracticeSegmentPlaybackInfo> OnSegmentStarted;
    public event Action OnAllComplete;
    public event Action<CoachTimelineStatus, CoachEndReason> OnTimelineStopped;

    private CoachMotionPackage package;
    private KeyframeCoachMotionBackend backend;
    private Coroutine playbackRoutine;
    private bool phaseHoldActive;
    private bool skipRequested;
    private bool isPaused;
    private bool complete;
    private int voicePhaseRequestSequence;
    private int pendingVoicePhaseRequestId;
    private bool pendingVoicePhaseCompleted = true;
    private int setIndex;
    private int segmentIndex;
    private int repeatIndex;
    private MotionRecorder motionRecorder;
    private Animator coachAnimator;
    private VoicePromptManager voicePromptManager;
    private DeadBugStartMenuController presentationController;
    private bool introductionStarted;

    // 介绍阶段的 Renderer 状态缓存
    private Renderer[] introductionCoachRenderers = System.Array.Empty<Renderer>();
    private Renderer[] introductionUserRenderers = System.Array.Empty<Renderer>();
    private readonly Dictionary<Renderer, bool> introductionRendererStates = new Dictionary<Renderer, bool>();

    public int TotalSets => Mathf.Max(1, package?.setCount ?? 1);
    public int TotalSegments => package?.segments?.Length ?? 0;
    public bool IsComplete => complete;
    public bool IsHoldingAtSegment => phaseHoldActive;
    public bool IsPaused => isPaused || MobileCoachControls.IsPaused;

    public void Initialize(
        Animator animator,
        CoachMotionPackage motionPackage,
        MotionRecorder recorder = null)
    {
        coachAnimator = animator;
        package = motionPackage;
        motionRecorder = recorder;

        // 不要立即创建 backend，等到播放时再创建
        // 这样教练模型在介绍阶段保持 T-pose（Animator 驱动的 Idle 状态）
        backend?.Dispose();
        backend = null;

        phaseHoldActive = false;
        skipRequested = false;
        isPaused = false;
        pendingVoicePhaseCompleted = true;
        complete = false;
        setIndex = 0;
        segmentIndex = 0;
        repeatIndex = 0;
        introductionStarted = false;

        // 查找 VoicePromptManager 和 PresentationController
        voicePromptManager = FindObjectOfType<VoicePromptManager>(true);
        presentationController = FindObjectOfType<DeadBugStartMenuController>(true);

        Debug.Log($"[JsonPracticeCoachController] 已初始化，package='{package.actionId}'，推迟创建 backend 直到播放时");
    }

    private void OnEnable()
    {
        CoachEventBus.CoachHardStopRequested += HandleHardStopRequested;
        CoachEventBus.CoachVoicePhaseCompleted += HandleVoicePhaseCompleted;
    }

    private void OnDisable()
    {
        CoachEventBus.CoachHardStopRequested -= HandleHardStopRequested;
        CoachEventBus.CoachVoicePhaseCompleted -= HandleVoicePhaseCompleted;
        if (!complete && playbackRoutine != null)
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

    /// <summary>
    /// 创建并初始化 backend（在播放前调用）
    /// </summary>
    private void EnsureBackendReady()
    {
        if (backend != null)
        {
            return;
        }

        if (coachAnimator == null || package == null)
        {
            Debug.LogError("[JsonPracticeCoachController] 无法创建 backend：animator 或 package 为空");
            return;
        }

        backend = new KeyframeCoachMotionBackend(coachAnimator, package, true);
        Debug.Log("[JsonPracticeCoachController] Backend 已创建并准备就绪");
    }

    public void PlayAction()
    {
        EnsureBackendReady();
        if (backend == null || !backend.IsReady)
        {
            Debug.LogError("JsonPracticeCoachController needs a valid motion package.", this);
            return;
        }

        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
        }

        phaseHoldActive = false;
        skipRequested = false;
        isPaused = false;
        pendingVoicePhaseCompleted = true;
        complete = false;
        setIndex = 0;
        segmentIndex = 0;
        repeatIndex = 0;
        introductionStarted = false;

        // 通知 presentation controller 开始播放前的准备
        presentationController?.BeginPracticePlaybackPresentation();

        playbackRoutine = StartCoroutine(PlayWithIntroduction());
    }

    /// <summary>
    /// 直接播放动作，不播放介绍（由 PracticeSessionController 在介绍后调用）
    /// </summary>
    public void PlayActionWithoutIntroduction()
    {
        // 现在创建 backend，接管 Animator
        EnsureBackendReady();

        if (backend == null || !backend.IsReady)
        {
            Debug.LogError("JsonPracticeCoachController needs a valid motion package.", this);
            return;
        }

        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
        }

        phaseHoldActive = false;
        skipRequested = false;
        isPaused = false;
        complete = false;
        setIndex = 0;
        segmentIndex = 0;
        repeatIndex = 0;

        Debug.Log("[JsonPracticeCoachController] 倒数结束，开始创建 backend 并播放 JSON 驱动的动作");

        // 直接开始播放动作
        playbackRoutine = StartCoroutine(PlayPackage());
    }

    public void AdvanceToNext()
    {
        // Explicit user skip. Normal progression is owned by the playback clock.
        skipRequested = true;
    }

    public void TogglePause()
    {
        bool pause = !IsPaused;
        MobileCoachControls mobileControls =
            FindObjectOfType<MobileCoachControls>(true);
        mobileControls?.SetPaused(pause);
        isPaused = pause;
        if (pause)
        {
            backend?.Freeze();
        }
        else
        {
            backend?.Resume();
        }
    }

    public void StopPractice()
    {
        StopTimeline(CoachTimelineStatus.EndedEarly, CoachEndReason.UserEnded);
        CancelPracticeIntroduction();
        motionRecorder?.CancelRecording();
        backend?.ReturnIdle();
    }

    public void StopTimeline(CoachTimelineStatus status, CoachEndReason reason)
    {
        if (complete || playbackRoutine == null) return;
        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
            playbackRoutine = null;
        }
        phaseHoldActive = false;
        skipRequested = true;
        isPaused = false;
        CancelPendingVoicePhase();
        CancelPracticeIntroduction();
        motionRecorder?.CancelRecording();
        complete = true;
        backend?.Freeze();
        if (reason == CoachEndReason.SafetyStop ||
            reason == CoachEndReason.HeadsetDisconnected)
        {
            CoachEventBus.PublishSafetyStopped(reason);
        }
        CoachEventBus.PublishSessionCompleted(status, reason);
        CoachEventBus.InvokeSafely(OnTimelineStopped, status, reason);
    }

    public void ReturnIdle() => StopPractice();

    private IEnumerator PlayWithIntroduction()
    {
        // 保存介绍阶段的 Renderer 状态
        CaptureIntroductionRendererStates();

        // 确保教练模型处于 Idle 状态（T-pose）
        backend.ReturnIdle();

        // 开始介绍阶段
        if (voicePromptManager != null)
        {
            voicePromptManager.StopAll();
            bool introductionPlayed = voicePromptManager.PlayPracticeIntroduction(
                ShowCoachIntroductionStage,
                ShowUserIntroductionStage,
                ShowBothIntroductionStage,
                BeginFormalPlayback);

            if (introductionPlayed)
            {
                Debug.Log("[JsonPracticeCoachController] 开始播放介绍和倒数");
                // VoicePromptManager calls BeginFormalPlayback only after all
                // original introduction clips have finished.
                yield break;
            }
        }

        // 如果没有语音管理器或介绍失败，直接开始播放
        Debug.LogWarning("[JsonPracticeCoachController] 未找到 VoicePromptManager，跳过介绍直接开始");
        ShowBothIntroductionStage();
        BeginFormalPlayback();
    }

    private void CaptureIntroductionRendererStates()
    {
        RestoreIntroductionRendererStates();

        Animator coachAnim = motionRecorder != null ? motionRecorder.CoachAnimator : coachAnimator;
        Animator userAnim = motionRecorder != null ? motionRecorder.UserAnimator : null;

        introductionCoachRenderers = GetAvatarRenderers(coachAnim);
        introductionUserRenderers = GetAvatarRenderers(userAnim);

        RememberRendererStates(introductionCoachRenderers);
        RememberRendererStates(introductionUserRenderers);
    }

    private static Renderer[] GetAvatarRenderers(Animator animator)
    {
        return animator != null
            ? animator.GetComponentsInChildren<Renderer>(true)
            : System.Array.Empty<Renderer>();
    }

    private void RememberRendererStates(Renderer[] renderers)
    {
        foreach (Renderer avatarRenderer in renderers)
        {
            if (avatarRenderer != null &&
                !introductionRendererStates.ContainsKey(avatarRenderer))
            {
                introductionRendererStates.Add(
                    avatarRenderer,
                    avatarRenderer.enabled);
            }
        }
    }

    private void ShowCoachIntroductionStage()
    {
        SetIntroductionAvatarVisible(introductionCoachRenderers, true);
        SetIntroductionAvatarVisible(introductionUserRenderers, false);
    }

    private void ShowUserIntroductionStage()
    {
        SetIntroductionAvatarVisible(introductionCoachRenderers, false);
        SetIntroductionAvatarVisible(introductionUserRenderers, true);
    }

    private void ShowBothIntroductionStage()
    {
        SetIntroductionAvatarVisible(introductionCoachRenderers, true);
        SetIntroductionAvatarVisible(introductionUserRenderers, true);
    }

    private void SetIntroductionAvatarVisible(Renderer[] renderers, bool visible)
    {
        foreach (Renderer avatarRenderer in renderers)
        {
            if (avatarRenderer == null ||
                !introductionRendererStates.TryGetValue(avatarRenderer, out bool wasEnabled))
            {
                continue;
            }

            avatarRenderer.enabled = visible && wasEnabled;
        }
    }

    private void RestoreIntroductionRendererStates()
    {
        foreach (KeyValuePair<Renderer, bool> rendererState in introductionRendererStates)
        {
            if (rendererState.Key != null)
            {
                rendererState.Key.enabled = rendererState.Value;
            }
        }

        introductionRendererStates.Clear();
        introductionCoachRenderers = System.Array.Empty<Renderer>();
        introductionUserRenderers = System.Array.Empty<Renderer>();
    }

    private void CancelPracticeIntroduction()
    {
        voicePromptManager?.CancelPracticeIntroduction();
        RestoreIntroductionRendererStates();
    }

    private void BeginFormalPlayback()
    {
        if (introductionStarted)
        {
            return;
        }

        introductionStarted = true;

        // 恢复原始 Renderer 状态
        RestoreIntroductionRendererStates();

        Debug.Log("[JsonPracticeCoachController] 介绍完成，开始正式训练");

        // 开始 SpineFlow 训练会话
        SpineFlowTrainingSession.BeginCoachTraining();

        // 开始录制
        if (motionRecorder != null &&
            !motionRecorder.TryBeginRecording(out string recordingError))
        {
            Debug.LogError(
                $"JSON practice replay could not start: {recordingError}",
                this);
            return;
        }

        // 开始播放动作
        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
        }
        playbackRoutine = StartCoroutine(PlayPackage());
    }

    private IEnumerator PlayPackage()
    {
        if (backend == null || !backend.IsReady)
        {
            Debug.LogError("JsonPracticeCoachController needs a valid motion package.", this);
            yield break;
        }

        for (setIndex = 0; setIndex < TotalSets; setIndex++)
        {
            for (segmentIndex = 0; segmentIndex < package.segments.Length; segmentIndex++)
            {
                CoachMotionSegment segment = package.segments[segmentIndex];
                int repeatCount = Mathf.Max(1, segment.repeatCount);
                for (repeatIndex = 0; repeatIndex < repeatCount; repeatIndex++)
                {
                    backend.PlaySegment(segmentIndex);
                    TrainingSegmentKind segmentKind = ResolveSegmentKind(
                        segmentIndex,
                        package.segments.Length,
                        segment.applyFormalActionScoreMapping);
                    string label = string.IsNullOrEmpty(segment.label)
                        ? segment.sourceStateName
                        : segment.label;
                    PracticeSegmentPlaybackInfo playbackInfo =
                        new PracticeSegmentPlaybackInfo(
                            segmentIndex,
                            repeatIndex,
                            setIndex,
                            label,
                            segmentKind,
                            CoachGuidanceLimb.None);
                    CoachEventBus.InvokeSafely(OnSegmentStarted, playbackInfo);
                    CoachSegmentEvent segmentEvent = new CoachSegmentEvent(
                        segmentIndex,
                        setIndex,
                        repeatIndex,
                        segmentKind,
                        label);
                    CoachEventBus.PublishSegmentStarted(segmentEvent);
                    if (!string.IsNullOrWhiteSpace(segment.voiceClipName))
                    {
                        RequestVoicePhase(
                            segmentEvent,
                            CoachVoicePhase.SegmentCue,
                            null,
                            segment.voiceClipName);
                    }
                    while (!backend.HasReachedSegmentEnd() && !skipRequested)
                    {
                        if (!isPaused && !MobileCoachControls.IsPaused)
                        {
                            backend.Tick(Time.unscaledDeltaTime);
                        }
                        yield return null;
                    }

                    backend.SnapToSegmentEnd();
                    backend.Freeze();
                    if (segmentKind == TrainingSegmentKind.Finish)
                    {
                        if (skipRequested)
                        {
                            CancelPendingVoicePhase();
                        }
                        else
                        {
                            RequestVoicePhase(
                                segmentEvent,
                                CoachVoicePhase.FinishCue);
                        }
                        while (isPaused || MobileCoachControls.IsPaused)
                        {
                            yield return null;
                        }
                        // A skip applies to this phase only. Clear it before
                        // the next authored phase so one button press cannot
                        // cascade through the remainder of the timeline.
                        skipRequested = false;
                        continue;
                    }

                    phaseHoldActive = true;
                    CoachEventBus.InvokeSafely(OnSegmentHold, new SegmentHoldInfo(
                        segmentIndex,
                        repeatIndex,
                        setIndex,
                        label,
                        Mathf.Max(0.1f, segment.scoreLeniency),
                        Mathf.Max(1f, segment.scoringDuration),
                        segmentKind,
                        segment.poseGuidanceRule));
                    if (skipRequested)
                    {
                        CancelPendingVoicePhase();
                    }
                    else
                    {
                        RequestVoicePhase(segmentEvent, CoachVoicePhase.HoldGuidance);
                    }
                    float remaining = Mathf.Max(1f, segment.scoringDuration);
                    while (!skipRequested && remaining > 0f)
                    {
                        if (!isPaused && !MobileCoachControls.IsPaused)
                        {
                            remaining -= Time.unscaledDeltaTime;
                        }
                        yield return null;
                    }
                    if (segmentKind == TrainingSegmentKind.ReturnTransition)
                    {
                        const float restDuration = 4f;
                        CoachEventBus.PublishRestStarted(segmentEvent, restDuration);
                        if (!skipRequested)
                        {
                            RequestVoicePhase(
                                segmentEvent,
                                CoachVoicePhase.RestGuidance);
                        }
                        float rest = restDuration;
                        while (!skipRequested && rest > 0f)
                        {
                            if (!isPaused && !MobileCoachControls.IsPaused)
                            {
                                rest -= Time.unscaledDeltaTime;
                            }
                            yield return null;
                        }
                    }
                    CancelPendingVoicePhase();
                    phaseHoldActive = false;
                    skipRequested = false;
                    backend.Resume();
                }
            }
        }

        complete = true;
        playbackRoutine = null;
        motionRecorder?.RequestFinish();
        CoachEventBus.PublishSessionCompleted(
            CoachTimelineStatus.Completed,
            CoachEndReason.NormalCompletion);
        CoachEventBus.InvokeSafely(OnAllComplete);
    }

    private static TrainingSegmentKind ResolveSegmentKind(
        int index,
        int count,
        bool formalScore)
    {
        if (index == 0) return TrainingSegmentKind.Preparation;
        if (index == count - 1) return TrainingSegmentKind.Finish;
        return formalScore ? TrainingSegmentKind.CoreAction : TrainingSegmentKind.ReturnTransition;
    }

    private void OnDestroy()
    {
        backend?.Dispose();
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
}
