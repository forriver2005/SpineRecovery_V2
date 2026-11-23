using System;
using UnityEngine;

public struct CrunchPose
{
    public float Lift;
    public float CurlAngle;
    public float KneeAngle;
    public float NeckAngle;
    public float ShoulderTilt;
    public float ArmAngle;

    public static bool TryCapture(Animator avatar, out CrunchPose pose)
    {
        pose = default;
        if (avatar == null || !avatar.isHuman) return false;

        Transform hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
        Transform head = avatar.GetBoneTransform(HumanBodyBones.Head);
        Transform leftShoulder = avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform leftUpperLeg = avatar.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightUpperLeg = avatar.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform leftKnee = avatar.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rightKnee = avatar.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform leftFoot = avatar.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = avatar.GetBoneTransform(HumanBodyBones.RightFoot);
        Transform leftArm = avatar.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform rightArm = avatar.GetBoneTransform(HumanBodyBones.RightLowerArm);
        if (hips == null || head == null || leftShoulder == null || rightShoulder == null ||
            leftUpperLeg == null || rightUpperLeg == null || leftKnee == null || rightKnee == null ||
            leftFoot == null || rightFoot == null || leftArm == null || rightArm == null) return false;

        Vector3 shoulders = (leftShoulder.position + rightShoulder.position) * 0.5f;
        Vector3 knees = (leftKnee.position + rightKnee.position) * 0.5f;
        float torso = Vector3.Distance(hips.position, shoulders);
        if (torso < 0.05f) return false;

        // ViewFollowGroup pitches the shared presentation root for the lying
        // view. Measure against its up axis so camera presentation cannot
        // change the user's physical lift or shoulder symmetry.
        Vector3 poseUp = avatar.transform.parent != null ? avatar.transform.parent.up : Vector3.up;
        pose.Lift = Vector3.Dot(shoulders - hips.position, poseUp) / torso;
        pose.CurlAngle = Vector3.Angle(shoulders - hips.position, knees - hips.position);
        pose.KneeAngle = 0.5f * (
            Vector3.Angle(leftUpperLeg.position - leftKnee.position, leftFoot.position - leftKnee.position) +
            Vector3.Angle(rightUpperLeg.position - rightKnee.position, rightFoot.position - rightKnee.position));
        pose.NeckAngle = Vector3.Angle(head.position - shoulders, shoulders - hips.position);
        pose.ShoulderTilt = Mathf.Abs(Vector3.Dot(leftShoulder.position - rightShoulder.position, poseUp)) / torso;
        pose.ArmAngle = 0.5f * (
            Vector3.Angle(leftArm.position - leftShoulder.position, shoulders - hips.position) +
            Vector3.Angle(rightArm.position - rightShoulder.position, shoulders - hips.position));
        return true;
    }
}

/// <summary>
/// Player-driven crunch recognizer. A/B progress is calibrated from the recorded
/// motion, while difficulty changes required curl, form tolerance and valid hold.
/// </summary>
[DefaultExecutionOrder(31000)]
public sealed class CrunchHoldScorer : MonoBehaviour, ITaikoHoldScorer
{
    private Animator user;
    private AvatarMotionSource tracker;
    private CrunchPose restReference;
    private CrunchPose holdReference;
    private bool calibrated;
    private bool preparationSeen;
    private bool active;
    private float confirmation;
    private HipThrustPhase pendingPhase;
    private float validHoldSeconds;
    private float missingSeconds;
    private float previousProgress;
    private float progressSpeed;
    private bool hasPreviousProgress;
    private float postureTotal;
    private float stabilityTotal;
    private int validSamples;
    private bool roundSettled;

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
    public float CurrentLiveComparisonScore { get; private set; }
    public float CurrentCurlQuality { get; private set; }
    public float CurrentLiftQuality { get; private set; }
    public float CurrentSymmetryQuality { get; private set; }
    public float CurrentNeckQuality { get; private set; }
    public float CurrentLegQuality { get; private set; }
    public float CurrentKneeQuality { get; private set; }
    public float StabilityQuality { get; private set; }
    public float LastRoundScore { get; private set; }
    public bool HasCompletedRound { get; private set; }
    public bool TrackingAvailable { get; private set; }
    public float ValidHoldSeconds => validHoldSeconds;
    public float TargetHoldSeconds => targetHoldSeconds;

    public event Action<HipThrustPhase> PhaseChanged;
    public event Action<int> SessionStarted;
    public event Action<float> LiveScoreChanged;
    public event Action<HipThrustDifficultyMode> DifficultyChanged;
    public event Action<float> RoundCompleted;

    public void Configure(Animator trackedUser, CrunchPose restPose, CrunchPose holdPose)
    {
        user = trackedUser;
        restReference = restPose;
        holdReference = holdPose;
        calibrated = Mathf.Abs(holdPose.Lift - restPose.Lift) > 0.025f ||
            Mathf.Abs(holdPose.CurlAngle - restPose.CurlAngle) > 5f;
        tracker = FindTracker();
        ApplyDifficultyProfile(DifficultyMode);
    }

    public bool BeginContinuousSession()
    {
        if (!calibrated || user == null || !user.isHuman) return false;
        active = true;
        TrackingAvailable = false;
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
        TrackingAvailable = false;
        ResetRound();
    }

    public void SetScoringEnabled(bool enabled) => active = enabled;

    public bool IsRelaxedPreparationPose(Animator avatar, Vector3 worldUp)
    {
        if (!calibrated || worldUp.sqrMagnitude < 0.0001f ||
            !CrunchPose.TryCapture(avatar, out CrunchPose pose)) return false;
        Transform hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
        Vector3 shoulders = (avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm).position +
            avatar.GetBoneTransform(HumanBodyBones.RightUpperArm).position) * 0.5f;
        Vector3 poseUp = avatar.transform.parent != null ? avatar.transform.parent.up : worldUp.normalized;
        // A crunch starts supine with bent knees. It does not require the
        // overhead hands of Dead Bug or the support geometry of Bird Dog.
        return Mathf.Abs(Vector3.Dot((shoulders - hips.position).normalized, poseUp)) <=
                Mathf.Sin(35f * Mathf.Deg2Rad) &&
            GetProgress(pose) <= restProgress + 0.08f &&
            Mathf.Abs(pose.KneeAngle - restReference.KneeAngle) <= 35f &&
            pose.ShoulderTilt <= 0.16f;
    }

    public void SetDifficultyMode(HipThrustDifficultyMode mode)
    {
        if (DifficultyMode != mode) ResetRound();
        DifficultyMode = mode;
        ApplyDifficultyProfile(mode);
        DifficultyChanged?.Invoke(mode);
    }

    private void ApplyDifficultyProfile(HipThrustDifficultyMode mode)
    {
        switch (mode)
        {
            case HipThrustDifficultyMode.Easy:
                startProgress = 0.08f; enterProgress = 0.46f; exitProgress = 0.34f; restProgress = 0.22f;
                minimumQuality = 42f; tolerance = 1.55f; targetHoldSeconds = 0.8f; maximumStableSpeed = 0.75f;
                minimumPartQuality = 35f;
                break;
            case HipThrustDifficultyMode.Hard:
                startProgress = 0.16f; enterProgress = 0.78f; exitProgress = 0.62f; restProgress = 0.28f;
                minimumQuality = 72f; tolerance = 0.88f; targetHoldSeconds = 2.0f; maximumStableSpeed = 0.38f;
                minimumPartQuality = 55f;
                break;
            default:
                startProgress = 0.12f; enterProgress = 0.62f; exitProgress = 0.48f; restProgress = 0.26f;
                minimumQuality = 58f; tolerance = 1.18f; targetHoldSeconds = 1.3f; maximumStableSpeed = 0.52f;
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
        if (!active) return;
        float dt = Mathf.Clamp(deltaTime, 0f, 0.1f);
        if (tracker == null) tracker = FindTracker();
        if (Application.isPlaying && (tracker == null || !tracker.isActiveAndEnabled ||
            tracker.Model != user.gameObject || !tracker.HasFreshFrame))
        {
            LoseTracking(dt);
            return;
        }
        if (!CrunchPose.TryCapture(user, out CrunchPose pose)) { LoseTracking(dt); return; }

        TrackingAvailable = true;
        missingSeconds = 0f;
        CurrentProgress = GetProgress(pose);
        float speed = hasPreviousProgress ? Mathf.Abs(CurrentProgress - previousProgress) / Mathf.Max(dt, 0.001f) : 0f;
        progressSpeed = Mathf.Lerp(progressSpeed, speed, 1f - Mathf.Exp(-8f * dt));
        previousProgress = CurrentProgress;
        hasPreviousProgress = true;

        CurrentCurlQuality = Score(Mathf.Abs(pose.CurlAngle - holdReference.CurlAngle), 4f * tolerance, 25f * tolerance);
        CurrentLiftQuality = Score(Mathf.Abs(pose.Lift - holdReference.Lift), 0.05f * tolerance, 0.32f * tolerance);
        CurrentSymmetryQuality = Score(pose.ShoulderTilt, 0.025f * tolerance, 0.16f * tolerance);
        CurrentNeckQuality = Score(Mathf.Abs(pose.NeckAngle - holdReference.NeckAngle), 6f * tolerance, 32f * tolerance);
        CurrentKneeQuality = Score(Mathf.Abs(pose.KneeAngle - holdReference.KneeAngle), 6f * tolerance, 35f * tolerance);
        CurrentLegQuality = 0.65f * CurrentKneeQuality +
            0.35f * Score(Mathf.Abs(pose.ArmAngle - holdReference.ArmAngle), 8f * tolerance, 42f * tolerance);
        StabilityQuality = Score(progressSpeed, maximumStableSpeed * 0.35f, maximumStableSpeed * 1.8f);
        float posture = 0.35f * CurrentCurlQuality + 0.25f * CurrentLiftQuality +
            0.15f * CurrentSymmetryQuality + 0.10f * CurrentNeckQuality + 0.15f * CurrentLegQuality;

        if (!preparationSeen)
        {
            preparationSeen = Confirm(HipThrustPhase.WaitingForRaise, CurrentProgress <= restProgress, dt, .10f);
            if (preparationSeen) restBaselineProgress = CurrentProgress;
            return;
        }
        UpdatePhase(posture, dt);
        if (Phase == HipThrustPhase.Holding && !roundSettled && validSamples > 0)
        {
            float durationScore = 100f * Mathf.Clamp01(validHoldSeconds / targetHoldSeconds);
            CurrentLiveComparisonScore = 0.75f * posture + 0.20f * durationScore + 0.05f * StabilityQuality;
        }
        else CurrentLiveComparisonScore = 0f;
        LiveScoreChanged?.Invoke(CurrentLiveComparisonScore);
    }

    private void UpdatePhase(float posture, float dt)
    {
        switch (Phase)
        {
            case HipThrustPhase.WaitingForRaise:
                // A may now be entered before the user fully relaxes. Require
                // fresh upward movement so that this wider A zone cannot
                // immediately start another raise while the user is resting.
                restBaselineProgress = Mathf.Min(restBaselineProgress, CurrentProgress);
                float departureProgress = Mathf.Max(startProgress, restBaselineProgress + 0.04f);
                if (Confirm(HipThrustPhase.Raising, CurrentProgress >= departureProgress, dt, 0.08f))
                    ChangePhase(HipThrustPhase.Raising);
                break;
            case HipThrustPhase.Raising:
                if (CurrentProgress >= enterProgress)
                {
                    if (Confirm(HipThrustPhase.Holding, true, dt, 0.10f))
                        ChangePhase(HipThrustPhase.Holding);
                }
                // Returning to A during an aborted raise requires a genuinely
                // relaxed pose. restProgress is only for the B-to-A descent;
                // using it here would immediately cancel a newly started raise.
                else if (Confirm(HipThrustPhase.WaitingForRaise,
                    CurrentProgress <= startProgress * 0.5f, dt, 0.10f)) ResetRound();
                break;
            case HipThrustPhase.Holding:
                // A high torso average cannot compensate for a severely
                // malformed neck or leg. Keep amplitude states permissive,
                // but only count time when all key body parts are acceptable.
                if (!roundSettled && posture >= minimumQuality &&
                    CurrentCurlQuality >= minimumPartQuality && CurrentLiftQuality >= minimumPartQuality &&
                    CurrentSymmetryQuality >= minimumPartQuality && CurrentNeckQuality >= minimumPartQuality &&
                    CurrentKneeQuality >= minimumPartQuality && CurrentLegQuality >= minimumPartQuality &&
                    progressSpeed <= maximumStableSpeed)
                {
                    validHoldSeconds += dt;
                    postureTotal += posture;
                    stabilityTotal += StabilityQuality;
                    validSamples++;
                }
                if (Confirm(HipThrustPhase.Lowering, CurrentProgress <= exitProgress, dt, 0.10f))
                {
                    CompleteRound();
                    ChangePhase(HipThrustPhase.Lowering);
                }
                break;
            case HipThrustPhase.Lowering:
                if (CurrentProgress <= restProgress)
                {
                    if (Confirm(HipThrustPhase.WaitingForRaise, true, dt, 0.10f)) ResetRound();
                }
                else if (Confirm(HipThrustPhase.Raising,
                    CurrentProgress >= enterProgress, dt, 0.08f)) ChangePhase(HipThrustPhase.Raising);
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
        LastRoundScore = 0.75f * posture +
            20f * Mathf.Clamp01(validHoldSeconds / targetHoldSeconds) + 0.05f * stability;
        HasCompletedRound = true;
        RoundCompleted?.Invoke(LastRoundScore);
    }

    private void LoseTracking(float dt)
    {
        TrackingAvailable = false;
        missingSeconds += dt;
        confirmation = 0f;
        hasPreviousProgress = false;
        CurrentLiveComparisonScore = 0f;
        LiveScoreChanged?.Invoke(0f);
        if (missingSeconds >= 0.35f) ResetRound();
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
        PhaseChanged?.Invoke(next);
    }

    private void ResetRound()
    {
        roundSettled = false;
        preparationSeen = false;
        restBaselineProgress = CurrentProgress;
        confirmation = validHoldSeconds = progressSpeed = postureTotal = stabilityTotal = 0f;
        validSamples = 0;
        hasPreviousProgress = false;
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

    private float GetProgress(CrunchPose pose) => Mathf.Clamp01(
        0.55f * ReferenceProgress(pose.Lift, restReference.Lift, holdReference.Lift) +
        0.45f * ReferenceProgress(pose.CurlAngle, restReference.CurlAngle, holdReference.CurlAngle));

    private static float Score(float error, float perfect, float zero) =>
        100f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(perfect, zero, error)));
}
