using System;
using UnityEngine;

// Muscle-space spine flexion is independent of camera yaw and body translation.
public struct LyingCatPose
{
    public float SpineBend, ChestBend, KneeAngle, NeckAngle, ShoulderTilt, ArmAngle, Twist, SideBend;
    public float LeftKneeAngle, RightKneeAngle, LeftElbowAngle, RightElbowAngle;
    public bool Supported;
    public static bool TryCapture(Animator avatar, out LyingCatPose pose)
    {
        pose = default;
        if (avatar == null || !avatar.isHuman || avatar.avatar == null || !avatar.avatar.isValid) return false;
        var reader = avatar.GetComponent<LyingCatPoseReader>();
        if (reader == null) reader = avatar.gameObject.AddComponent<LyingCatPoseReader>();
        if (!reader.Read(avatar, out HumanPose human)) return false;
        pose.SpineBend = human.muscles[System.Array.IndexOf(HumanTrait.MuscleName, "Spine Front-Back")];
        pose.ChestBend = human.muscles[System.Array.IndexOf(HumanTrait.MuscleName, "Chest Front-Back")];
        pose.Twist = human.muscles[System.Array.IndexOf(HumanTrait.MuscleName, "Spine Twist Left-Right")];
        pose.SideBend = human.muscles[System.Array.IndexOf(HumanTrait.MuscleName, "Spine Left-Right")];
        Transform hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
        Transform head = avatar.GetBoneTransform(HumanBodyBones.Head);
        Transform ls = avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm), rs = avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform le = avatar.GetBoneTransform(HumanBodyBones.LeftLowerArm), re = avatar.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform lh = avatar.GetBoneTransform(HumanBodyBones.LeftHand), rh = avatar.GetBoneTransform(HumanBodyBones.RightHand);
        Transform lt = avatar.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rt = avatar.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform lk = avatar.GetBoneTransform(HumanBodyBones.LeftLowerLeg), rk = avatar.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform lf = avatar.GetBoneTransform(HumanBodyBones.LeftFoot), rf = avatar.GetBoneTransform(HumanBodyBones.RightFoot);
        if (hips == null || head == null || ls == null || rs == null || le == null || re == null || lh == null || rh == null || lt == null || rt == null || lk == null || rk == null || lf == null || rf == null) return false;
        Vector3 shoulders = (ls.position + rs.position) * 0.5f;
        Vector3 up = avatar.transform.parent != null ? avatar.transform.parent.up : Vector3.up;
        float torso = Vector3.Distance(hips.position, shoulders);
        if (torso < 0.05f) return false;
        pose.NeckAngle = Vector3.Angle(head.position - shoulders, shoulders - hips.position);
        pose.LeftKneeAngle = Vector3.Angle(lt.position-lk.position, lf.position-lk.position);
        pose.RightKneeAngle = Vector3.Angle(rt.position-rk.position, rf.position-rk.position);
        pose.LeftElbowAngle = Vector3.Angle(ls.position-le.position, lh.position-le.position);
        pose.RightElbowAngle = Vector3.Angle(rs.position-re.position, rh.position-re.position);
        pose.KneeAngle = 0.5f * (pose.LeftKneeAngle + pose.RightKneeAngle);
        pose.ArmAngle = 0.5f * (pose.LeftElbowAngle + pose.RightElbowAngle);
        pose.ShoulderTilt = Mathf.Abs(Vector3.Dot(ls.position-rs.position, up)) / torso;
        pose.Supported = Mathf.Abs(Vector3.Dot((shoulders-hips.position).normalized, up)) < 0.85f &&
            Vector3.Dot(ls.position-lh.position, up) > 0.12f*torso &&
            Vector3.Dot(rs.position-rh.position, up) > 0.12f*torso &&
            Vector3.Dot(hips.position-lk.position, up) > 0.10f*torso &&
            Vector3.Dot(hips.position-rk.position, up) > 0.10f*torso;
        return true;
    }
}

/// <summary>
/// Player-driven lying_cat recognizer. A/B progress is calibrated from the recorded
/// motion, while difficulty changes required curl, form tolerance and valid hold.
/// </summary>
[DefaultExecutionOrder(31000)]
public sealed class LyingCatHoldScorer : MonoBehaviour, ITaikoHoldScorer
{
    private Animator user;
    private AvatarMotionSource tracker;
    private LyingCatAnatomicalConstraint constraint;
    private LyingCatPose restReference;
    private LyingCatPose holdReference;
    private bool calibrated;
    private bool active;
    private bool preparationSeen;
    private float confirmation;
    private HipThrustPhase pendingPhase;
    private float validHoldSeconds;
    private float missingSeconds;
    private float previousProgress;
    private float progressSpeed;
    private bool hasPreviousProgress;
    private float signedProgressSpeed, peakProgress, troughProgress, phaseSeconds;
    private bool progressInitialized;
    private float postureTotal;
    private float stabilityTotal;
    private int validSamples;
    private bool roundSettled;
    private bool hasPendingRound;
    private float pendingRoundScore;

    private float startProgress;
    private float enterProgress;
    private float exitProgress;
    private float restProgress;
    private float restBaselineProgress;
    private float minimumQuality;
    private float minimumPartQuality;
    private float tolerance;
    private float targetHoldSeconds;
    private float maximumStableSpeed;

    public HipThrustPhase Phase { get; private set; } = HipThrustPhase.WaitingForRaise;
    public HipThrustDifficultyMode DifficultyMode { get; private set; } = HipThrustDifficultyMode.Medium;
    public float CurrentProgress { get; private set; }
    public float RawProgress { get; private set; }
    public bool UnsafeInput => constraint != null && constraint.enabled && constraint.RejectedInputThisFrame;
    public bool PreparationRecognized => preparationSeen;
    public float CurrentLiveComparisonScore { get; private set; }
    public float CurrentCurlQuality { get; private set; }
    public float CurrentChestBendQuality { get; private set; }
    public float CurrentSymmetryQuality { get; private set; }
    public float CurrentNeckQuality { get; private set; }
    public float CurrentLegQuality { get; private set; }
    public float CurrentKneeQuality { get; private set; }
    public float CurrentArmQuality { get; private set; }
    public float StabilityQuality { get; private set; }
    public float LastRoundScore { get; private set; }
    public bool HasCompletedRound { get; private set; }
    public bool TrackingAvailable { get; private set; }
    public bool SupportAvailable { get; private set; }
    public float ValidHoldSeconds => validHoldSeconds;
    public float TargetHoldSeconds => targetHoldSeconds;
    // Read-only threshold for prescribed hold tasks; the existing recognizer is unchanged.
    public float HoldEntryProgress => enterProgress;

    public event Action<HipThrustPhase> PhaseChanged;
    public event Action<int> SessionStarted;
    public event Action<float> LiveScoreChanged;
    public event Action<HipThrustDifficultyMode> DifficultyChanged;
    public event Action<float> RoundCompleted;

    public void Configure(Animator trackedUser, LyingCatPose restPose, LyingCatPose holdPose)
    {
        user = trackedUser;
        restReference = restPose;
        holdReference = holdPose;
        calibrated = Mathf.Abs(holdPose.SpineBend - restPose.SpineBend) > 0.15f;
        tracker = FindTracker();
        constraint = user != null ? user.GetComponent<LyingCatAnatomicalConstraint>() : null;
        ApplyDifficultyProfile(DifficultyMode);
    }

    public bool BeginContinuousSession()
    {
        if (!calibrated || user == null || !user.isHuman) return false;
        active = true;
        preparationSeen = false;
        progressInitialized = false;
        TrackingAvailable = SupportAvailable = false;
        missingSeconds = 0f;
        HasCompletedRound = false;
        LastRoundScore = 0f;
        ResetRound();
        SessionStarted?.Invoke(int.MaxValue);
        return true;
    }

    public void CancelSession()
    {
        active = false;
        TrackingAvailable = SupportAvailable = false;
        ResetRound();
    }

    public void SetScoringEnabled(bool enabled) { active = enabled; if (!enabled) ResetRound(); }

    public bool IsRelaxedPreparationPose(Animator avatar, Vector3 worldUp)
    {
        if (!calibrated || worldUp.sqrMagnitude < 0.0001f ||
            !LyingCatPose.TryCapture(avatar, out LyingCatPose pose)) return false;
        return pose.Supported && GetProgress(pose) <= restProgress + 0.08f &&
            Mathf.Abs(pose.KneeAngle - restReference.KneeAngle) <= 35f;
    }

    public void SetDifficultyMode(HipThrustDifficultyMode mode)
    {
        if (DifficultyMode != mode) { preparationSeen = false; ResetRound(); }
        DifficultyMode = mode;
        ApplyDifficultyProfile(mode);
        DifficultyChanged?.Invoke(mode);
    }

    private void ApplyDifficultyProfile(HipThrustDifficultyMode mode)
    {
        switch (mode)
        {
            case HipThrustDifficultyMode.Easy:
                startProgress = 0.03f; enterProgress = 0.40f; exitProgress = 0.28f; restProgress = 0.30f;
                minimumQuality = 42f; tolerance = 1.55f; targetHoldSeconds = 0.6f; maximumStableSpeed = 0.25f;
                minimumPartQuality = 35f;
                break;
            case HipThrustDifficultyMode.Hard:
                startProgress = 0.05f; enterProgress = 0.72f; exitProgress = 0.56f; restProgress = 0.38f;
                minimumQuality = 72f; tolerance = 0.88f; targetHoldSeconds = 1.5f; maximumStableSpeed = 0.15f;
                minimumPartQuality = 55f;
                break;
            default:
                startProgress = 0.04f; enterProgress = 0.56f; exitProgress = 0.40f; restProgress = 0.34f;
                minimumQuality = 58f; tolerance = 1.18f; targetHoldSeconds = 1.0f; maximumStableSpeed = 0.20f;
                minimumPartQuality = 45f;
                break;
        }
    }

    private void LateUpdate()
    {
        if (TaikoNight.TaikoNightPracticeClock.IsPaused) return;
        Sample(Time.unscaledDeltaTime);
    }

    internal void Sample(float deltaTime)
    {
        if (!active || TaikoNight.TaikoNightPracticeClock.IsPaused) return;
        float dt = Mathf.Clamp(deltaTime, 0f, 0.1f);
        if (dt <= 0f) return;
        if (constraint == null && user != null) constraint = user.GetComponent<LyingCatAnatomicalConstraint>();
        if (tracker == null) tracker = FindTracker();
        if (Application.isPlaying && (tracker == null || !tracker.isActiveAndEnabled ||
            tracker.Model != user.gameObject || !tracker.HasFreshFrame))
        {
            LoseTracking(dt);
            return;
        }
        if (UnsafeInput) { LoseTracking(dt); TrackingAvailable = true; return; }
        if (!LyingCatPose.TryCapture(user, out LyingCatPose pose)) { LoseTracking(dt); return; }

        SupportAvailable = pose.Supported;
        if (!pose.Supported) { LoseTracking(dt); TrackingAvailable = true; return; }
        TrackingAvailable = true;
        missingSeconds = 0f;
        RawProgress = GetProgress(pose);
        CurrentProgress = progressInitialized
            ? Mathf.Lerp(CurrentProgress, RawProgress, 1f - Mathf.Exp(-14f * dt)) : RawProgress;
        progressInitialized = true;
        float signedSpeed = hasPreviousProgress ? (CurrentProgress - previousProgress) / dt : 0f;
        float speed = Mathf.Abs(signedSpeed);
        signedProgressSpeed = Mathf.Lerp(signedProgressSpeed, signedSpeed, 1f - Mathf.Exp(-10f * dt));
        progressSpeed = Mathf.Lerp(progressSpeed, speed, 1f - Mathf.Exp(-8f * dt));
        previousProgress = CurrentProgress;
        hasPreviousProgress = true;

        CurrentCurlQuality = Score(Mathf.Abs(pose.SpineBend - holdReference.SpineBend), 0.08f * tolerance, 0.55f * tolerance);
        CurrentChestBendQuality = Score(Mathf.Abs(pose.ChestBend - holdReference.ChestBend), 0.08f * tolerance, 0.50f * tolerance);
        CurrentSymmetryQuality = 0.5f * Score(Mathf.Abs(pose.ShoulderTilt - holdReference.ShoulderTilt), 0.025f * tolerance, 0.16f * tolerance) +
            0.25f * Score(Mathf.Abs(pose.Twist - holdReference.Twist), 0.08f * tolerance, 0.45f * tolerance) +
            0.25f * Score(Mathf.Abs(pose.SideBend - holdReference.SideBend), 0.08f * tolerance, 0.45f * tolerance);
        CurrentNeckQuality = Score(Mathf.Abs(pose.NeckAngle - holdReference.NeckAngle), 6f * tolerance, 32f * tolerance);
        CurrentKneeQuality = Mathf.Min(
            Score(Mathf.Abs(pose.LeftKneeAngle - holdReference.LeftKneeAngle), 6f * tolerance, 35f * tolerance),
            Score(Mathf.Abs(pose.RightKneeAngle - holdReference.RightKneeAngle), 6f * tolerance, 35f * tolerance));
        CurrentArmQuality = Mathf.Min(
            Score(Mathf.Abs(pose.LeftElbowAngle - holdReference.LeftElbowAngle), 8f * tolerance, 42f * tolerance),
            Score(Mathf.Abs(pose.RightElbowAngle - holdReference.RightElbowAngle), 8f * tolerance, 42f * tolerance));
        CurrentLegQuality = 0.65f * CurrentKneeQuality + 0.35f * CurrentArmQuality;
        StabilityQuality = Score(progressSpeed, maximumStableSpeed * 0.35f, maximumStableSpeed * 1.8f);
        float posture = 0.45f * CurrentCurlQuality + 0.15f * CurrentChestBendQuality +
            0.15f * CurrentSymmetryQuality + 0.10f * CurrentNeckQuality + 0.15f * CurrentLegQuality;

        if (!preparationSeen)
        {
            preparationSeen = Confirm(HipThrustPhase.WaitingForRaise, CurrentProgress <= restProgress, dt, 0.10f);
            if (preparationSeen) restBaselineProgress = CurrentProgress;
            CurrentLiveComparisonScore = 0f;
            LiveScoreChanged?.Invoke(0f);
            return;
        }
        UpdatePhase(posture, dt);
        if (Phase == HipThrustPhase.Holding && !roundSettled && validSamples > 0 && FormAcceptable(posture))
        {
            float durationScore = 100f * Mathf.Clamp01(validHoldSeconds / targetHoldSeconds);
            CurrentLiveComparisonScore = 0.75f * posture + 0.20f * durationScore + 0.05f * StabilityQuality;
        }
        else CurrentLiveComparisonScore = 0f;
        LiveScoreChanged?.Invoke(CurrentLiveComparisonScore);
    }

    private void UpdatePhase(float posture, float dt)
    {
        phaseSeconds += dt;
        switch (Phase)
        {
            case HipThrustPhase.WaitingForRaise:
                // Depart from the observed A baseline, including a partial A.
                // A stationary pose in the expanded rest zone never auto-starts.
                restBaselineProgress = Mathf.Min(restBaselineProgress, CurrentProgress);
                float minimumDeparture = DifficultyMode == HipThrustDifficultyMode.Easy ? 0.10f :
                    DifficultyMode == HipThrustDifficultyMode.Hard ? 0.14f : 0.12f;
                float departureProgress = Mathf.Max(minimumDeparture, restBaselineProgress + startProgress);
                if (Confirm(HipThrustPhase.Raising, CurrentProgress >= departureProgress && signedProgressSpeed > 0.015f, dt, 0.06f))
                    ChangePhase(HipThrustPhase.Raising);
                break;
            case HipThrustPhase.Raising:
                if (CurrentProgress >= enterProgress)
                {
                    if (Confirm(HipThrustPhase.Holding, true, dt, 0.08f))
                        ChangePhase(HipThrustPhase.Holding);
                }
                // A return trend can re-enter the wider A zone; continued
                // outward movement through that zone cannot cancel the raise.
                else if (Confirm(HipThrustPhase.WaitingForRaise,
                    CurrentProgress <= restProgress && (signedProgressSpeed < -0.015f ||
                    (phaseSeconds > 0.35f && Mathf.Abs(signedProgressSpeed) < 0.02f && progressSpeed < 0.04f)),
                    dt, 0.10f)) ResetRound();
                break;
            case HipThrustPhase.Holding:
                peakProgress = Mathf.Max(peakProgress, CurrentProgress);
                // A high torso average cannot compensate for a severely
                // malformed neck or leg. Keep amplitude states permissive,
                // but only count time when all key body parts are acceptable.
                if (!roundSettled && FormAcceptable(posture))
                {
                    validHoldSeconds += dt;
                    postureTotal += posture;
                    stabilityTotal += StabilityQuality;
                    validSamples++;
                }
                // Notice an intentional return near B, without waiting until
                // the user has crossed most of the full A/B span.
                bool returning = signedProgressSpeed < -0.04f && peakProgress - CurrentProgress >= 0.06f;
                if (Confirm(HipThrustPhase.Lowering, phaseSeconds >= 0.12f &&
                    (CurrentProgress <= exitProgress || returning), dt, 0.10f))
                {
                    CompleteRound();
                    ChangePhase(HipThrustPhase.Lowering);
                }
                break;
            case HipThrustPhase.Lowering:
                troughProgress = Mathf.Min(troughProgress, CurrentProgress);
                if (phaseSeconds >= 0.12f && CurrentProgress <= restProgress)
                {
                    if (Confirm(HipThrustPhase.WaitingForRaise, true, dt, 0.10f))
                    {
                        CommitRound();
                        ResetRound();
                    }
                }
                else if (Confirm(HipThrustPhase.Raising,
                    CurrentProgress >= enterProgress && CurrentProgress >= troughProgress + 0.05f &&
                    signedProgressSpeed > 0.04f, dt, 0.10f)) ChangePhase(HipThrustPhase.Raising);
                break;
        }
    }

    private void CompleteRound()
    {
        if (roundSettled) return;
        // Even a zero-valid-sample attempt is closed until the user returns A.
        roundSettled = true;
        if (validSamples == 0) return;
        float posture = postureTotal / validSamples;
        float stability = stabilityTotal / validSamples;
        pendingRoundScore = 0.75f * posture +
            20f * Mathf.Clamp01(validHoldSeconds / targetHoldSeconds) + 0.05f * stability;
        hasPendingRound = true;
    }

    private void CommitRound()
    {
        // A repetition requires an observed return to A. If tracking or form
        // is lost during the return, ResetRound discards this pending result.
        if (!hasPendingRound) return;
        LastRoundScore = pendingRoundScore;
        HasCompletedRound = true;
        hasPendingRound = false;
        RoundCompleted?.Invoke(LastRoundScore);
    }

    private bool FormAcceptable(float posture) => posture >= minimumQuality &&
        CurrentCurlQuality >= minimumPartQuality && CurrentChestBendQuality >= minimumPartQuality &&
        CurrentSymmetryQuality >= minimumPartQuality && CurrentNeckQuality >= minimumPartQuality &&
        CurrentKneeQuality >= minimumPartQuality && CurrentArmQuality >= minimumPartQuality && CurrentLegQuality >= minimumPartQuality &&
        progressSpeed <= maximumStableSpeed;

    private void LoseTracking(float dt)
    {
        TrackingAvailable = false;
        SupportAvailable = false;
        missingSeconds += dt;
        confirmation = 0f;
        hasPreviousProgress = false;
        progressInitialized = false;
        signedProgressSpeed = 0f;
        CurrentLiveComparisonScore = 0f;
        LiveScoreChanged?.Invoke(0f);
        if (missingSeconds >= 0.35f) { preparationSeen = false; ResetRound(); }
    }

    private AvatarMotionSource FindTracker()
    {
        if (user == null) return null;
        AvatarMotionSource source = user.GetComponentInParent<AvatarMotionSource>();
        if (source != null && source.Model == user.gameObject) return source;
        foreach (AvatarMotionSource candidate in FindObjectsOfType<AvatarMotionSource>())
            if (candidate.Model == user.gameObject) return candidate;
        return null;
    }

    private bool Confirm(HipThrustPhase next, bool condition, float dt, float seconds)
    {
        // Time accumulated for one transition must not confirm another one.
        if (pendingPhase != next) confirmation = 0f;
        pendingPhase = next;
        confirmation = condition ? confirmation + dt : 0f;
        return confirmation >= seconds;
    }

    private void ChangePhase(HipThrustPhase next)
    {
        if (Phase == next) return;
        Phase = next;
        confirmation = 0f;
        phaseSeconds = 0f;
        if (next == HipThrustPhase.Holding) peakProgress = CurrentProgress;
        if (next == HipThrustPhase.Lowering) troughProgress = CurrentProgress;
        PhaseChanged?.Invoke(next);
    }

    private void ResetRound()
    {
        roundSettled = false;
        hasPendingRound = false;
        pendingRoundScore = 0f;
        restBaselineProgress = CurrentProgress;
        confirmation = validHoldSeconds = progressSpeed = postureTotal = stabilityTotal = 0f;
        validSamples = 0;
        hasPreviousProgress = false;
        signedProgressSpeed = 0f;
        peakProgress = troughProgress = CurrentProgress;
        CurrentLiveComparisonScore = 0f;
        ChangePhase(HipThrustPhase.WaitingForRaise);
        LiveScoreChanged?.Invoke(0f);
    }

    private void OnDisable() => CancelSession();

    private static float ReferenceProgress(float value, float rest, float hold)
    {
        float delta = hold - rest;
        return Mathf.Abs(delta) < 0.0001f ? 0f : (value - rest) / delta;
    }

    private float GetProgress(LyingCatPose pose) => Mathf.Clamp01(
        ReferenceProgress(pose.SpineBend, restReference.SpineBend, holdReference.SpineBend));

    private static float Score(float error, float perfect, float zero) =>
        100f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(perfect, zero, error)));
}
