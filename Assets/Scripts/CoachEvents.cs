using System;
using UnityEngine;

public enum CoachTrackingQuality
{
    Measured,
    Partial,
    Unavailable
}

public enum CoachTimelineStatus
{
    Completed,
    EndedEarly,
    Interrupted,
    SafetyStopped
}

public enum CoachEndReason
{
    NormalCompletion,
    UserEnded,
    HeadsetDisconnected,
    SafetyStop,
    TechnicalError
}

public enum CoachVoicePhase
{
    SegmentCue,
    HoldGuidance,
    RestGuidance,
    FinishCue
}

public readonly struct CoachSegmentEvent
{
    public readonly int segmentId;
    public readonly int setIndex;
    public readonly int repetitionIndex;
    public readonly TrainingSegmentKind segmentKind;
    public readonly string label;

    public CoachSegmentEvent(
        int segmentId,
        int setIndex,
        int repetitionIndex,
        TrainingSegmentKind segmentKind,
        string label)
    {
        this.segmentId = segmentId;
        this.setIndex = setIndex;
        this.repetitionIndex = repetitionIndex;
        this.segmentKind = segmentKind;
        this.label = label;
    }
}

public readonly struct CoachVoicePhaseRequest
{
    public readonly int requestId;
    public readonly CoachSegmentEvent segment;
    public readonly CoachVoicePhase phase;
    public readonly AudioClip clip;
    public readonly string clipName;

    public CoachVoicePhaseRequest(
        int requestId,
        CoachSegmentEvent segment,
        CoachVoicePhase phase,
        AudioClip clip = null,
        string clipName = null)
    {
        this.requestId = requestId;
        this.segment = segment;
        this.phase = phase;
        this.clip = clip;
        this.clipName = clipName;
    }
}

public static class CoachEventBus
{
    public static event Action<CoachSegmentEvent> CoachSegmentStarted;
    public static event Action<CoachSegmentEvent, float> CoachRestStarted;
    public static event Action<AudioClip> CoachVoiceCueRequested;
    public static event Action<string> CoachVoiceCueNameRequested;
    public static event Action<CoachVoicePhaseRequest> CoachVoicePhaseRequested;
    public static event Action<int> CoachVoicePhaseCancelRequested;
    public static event Action<int> CoachVoicePhaseCompleted;
    public static event Action<CoachEndReason> CoachHardStopRequested;
    public static event Action<CoachEndReason> CoachSafetyStopped;
    public static event Action<CoachTimelineStatus, CoachEndReason> CoachSessionCompleted;

    public static void PublishSegmentStarted(CoachSegmentEvent value) => InvokeEach(CoachSegmentStarted, value);
    public static void PublishRestStarted(CoachSegmentEvent value, float durationSeconds) => InvokeEach(CoachRestStarted, value, durationSeconds);
    public static void PublishVoiceCueRequested(AudioClip clip) => InvokeEach(CoachVoiceCueRequested, clip);
    public static void PublishVoiceCueRequested(string clipName) =>
        InvokeEach(CoachVoiceCueNameRequested, clipName);
    public static bool PublishVoicePhaseRequested(CoachVoicePhaseRequest request)
    {
        if (CoachVoicePhaseRequested == null)
        {
            return false;
        }

        foreach (Action<CoachVoicePhaseRequest> handler in
                 CoachVoicePhaseRequested.GetInvocationList())
        {
            try
            {
                handler(request);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        return true;
    }

    public static void CancelVoicePhase(int requestId) =>
        InvokeEach(CoachVoicePhaseCancelRequested, requestId);
    public static void PublishVoicePhaseCompleted(int requestId) =>
        InvokeEach(CoachVoicePhaseCompleted, requestId);
    // Safety/connection supervisors call this once for a non-recoverable stop.
    public static void RequestHardStop(CoachEndReason reason) => InvokeEach(CoachHardStopRequested, reason);
    public static void PublishSafetyStopped(CoachEndReason reason) => InvokeEach(CoachSafetyStopped, reason);
    public static void PublishSessionCompleted(CoachTimelineStatus status, CoachEndReason reason) =>
        InvokeEach(CoachSessionCompleted, status, reason);

    public static void InvokeSafely(Action handlers) => InvokeEach(handlers);
    public static void InvokeSafely<T>(Action<T> handlers, T value) => InvokeEach(handlers, value);
    public static void InvokeSafely<T1, T2>(Action<T1, T2> handlers, T1 first, T2 second) =>
        InvokeEach(handlers, first, second);

    private static void InvokeEach<T>(Action<T> handlers, T value)
    {
        if (handlers == null) return;
        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(value);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
    }

    private static void InvokeEach(Action handlers)
    {
        if (handlers == null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
    }

    private static void InvokeEach<T1, T2>(Action<T1, T2> handlers, T1 first, T2 second)
    {
        if (handlers == null) return;
        foreach (Action<T1, T2> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(first, second);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
    }

    private static void InvokeEach<T1, T2, T3>(
        Action<T1, T2, T3> handlers,
        T1 first,
        T2 second,
        T3 third)
    {
        if (handlers == null) return;
        foreach (Action<T1, T2, T3> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(first, second, third);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
    }
}
