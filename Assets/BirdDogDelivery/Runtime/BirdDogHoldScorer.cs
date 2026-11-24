using System;
using System.Collections.Generic;
using UnityEngine;

namespace BirdDogDelivery
{
// A/B recognition is intentionally more permissive than the form targets.
// Only the confirmed side can earn time; a new round requires all limbs in A.
[DefaultExecutionOrder(31000)]
public sealed class BirdDogHoldScorer : MonoBehaviour
{
    public const float MaximumDeduction = 5f;
    public float EntryBaseScore => DifficultyMode == HipThrustDifficultyMode.Hard ? 75f : DifficultyMode == HipThrustDifficultyMode.Easy ? 85f : 80f;
    public float MaximumHoldBonus => DifficultyMode == HipThrustDifficultyMode.Hard ? 25f : DifficultyMode == HipThrustDifficultyMode.Easy ? 15f : 20f;
    private Animator user;
    private AvatarMotionSource tracker;
    private BirdDogPose rest, right, left;
    private bool calibrated, sessionActive, scoringEnabled = true, armed, enteredB;
    private float confirmation, sideConfirmation, freshAge, missingAge, validHold, roundBest;
    private BirdDogSide candidate;
    private HipThrustPhase pending;
    private float bestDeduction;
    private string bestReasons = "无扣分";
    private BirdDogLimb previousArm, previousLeg;
    private bool hasPreviousHold;
    private float phaseElapsed, stalledElapsed, previousTransitionProgress, peakHoldProgress;
    private float progressFilterLeft, progressFilterRight, candidateMissSeconds;
    private float lowestLoweringProgress;
    private float peakArmProgress, peakLegProgress;
    private bool hasProgressFilter;
    private string waitingReason;
    private BirdDogAnatomicalConstraint constraint;
    public const float MaximumTransitionSeconds = 12f, StalledTransitionSeconds = 2f;

    public HipThrustPhase Phase { get; private set; } = HipThrustPhase.WaitingForRaise;
    public HipThrustDifficultyMode DifficultyMode { get; private set; } = HipThrustDifficultyMode.Medium;
    public BirdDogSide LockedSide { get; private set; }
    public bool TrackingAvailable { get; private set; }
    public bool IsArmed => armed;
    public float CurrentProgress { get; private set; }
    public float CurrentLiveComparisonScore { get; private set; }
    public float CurrentDeduction { get; private set; }
    public string DeductionReasons { get; private set; } = "无扣分";
    public string Guidance { get; private set; } = "请先回到四点支撑准备位";
    public float ValidHoldSeconds => validHold;
    public float TargetHoldSeconds => DifficultyMode == HipThrustDifficultyMode.Easy ? 3f : DifficultyMode == HipThrustDifficultyMode.Hard ? 7f : 5f;
    public float HoldBonus => MaximumHoldBonus * Mathf.Clamp01(validHold / TargetHoldSeconds);
    public float BaseScore => enteredB ? EntryBaseScore : 0f;
    public float LastRoundBestScore { get; private set; }
    public float LastRoundDeduction { get; private set; }
    public string LastRoundReasons { get; private set; } = "--";
    public bool HasCompletedRound { get; private set; }
    public float ArmHorizontalError { get; private set; }
    public float LegHorizontalError { get; private set; }
    public float ArmElevation { get; private set; }
    public float LegElevation { get; private set; }

    public event Action<HipThrustPhase> PhaseChanged;
    public event Action<int> SessionStarted;
    public event Action<float> LiveScoreChanged;
    public event Action<HipThrustDifficultyMode> DifficultyChanged;
    public event Action<float> RoundCompleted;

    public void Configure(Animator avatar, BirdDogPose a, BirdDogPose r, BirdDogPose l)
    {
        user = avatar; rest = a; right = r; left = l;
        calibrated = r.RightArm.Height - a.RightArm.Height > 0.08f &&
            r.LeftLeg.Height - a.LeftLeg.Height > 0.08f &&
            l.LeftArm.Height - a.LeftArm.Height > 0.08f && l.RightLeg.Height - a.RightLeg.Height > 0.08f;
        tracker = FindTracker();
        constraint = user != null ? user.GetComponent<BirdDogAnatomicalConstraint>() : null;
    }
    public bool BeginContinuousSession()
    {
        if (!calibrated || user == null || !user.isHuman) return false;
        sessionActive = scoringEnabled = true; freshAge = float.PositiveInfinity;
        HasCompletedRound = false; LastRoundBestScore = LastRoundDeduction = 0f; LastRoundReasons = "--";
        ResetRound(); SessionStarted?.Invoke(int.MaxValue); return true;
    }
    public void CancelSession() { sessionActive = false; ResetRound(); }
    public void SetScoringEnabled(bool enabled) { scoringEnabled = enabled; confirmation = sideConfirmation = 0f; }
    public void SetDifficultyMode(HipThrustDifficultyMode mode)
    {
        if (DifficultyMode == mode) return;
        DifficultyMode = mode; ResetRound(); DifficultyChanged?.Invoke(mode);
    }
    public bool IsRelaxedPreparationPose(Animator avatar, Vector3 up) =>
        calibrated && BirdDogPose.TryCapture(avatar, out var p) && IsRest(p);

    private void LateUpdate() => Sample(Time.unscaledDeltaTime);
    internal void Sample(float deltaTime)
    {
        if (!sessionActive || !scoringEnabled) return;
        if (MobileCoachControls.IsPaused) { confirmation = sideConfirmation = 0f; return; }
        float dt = Mathf.Clamp(deltaTime, 0f, 0.1f);
        if (tracker == null) tracker = FindTracker();
        if (tracker != null)
        {
            // The host caches its last bone count: use packet age, never a stale nonzero count.
            freshAge = tracker.GetAvailable() > 0 && !tracker.Freeze && tracker.LastBonePacketCounterInFrame > 0
                ? Time.unscaledTime - tracker.LastFrameReceivedAt : freshAge + dt;
            if (freshAge > 0.35f) { LoseTracking(dt); return; }
        }
        else if (Application.isPlaying) { LoseTracking(dt); return; }
        // Score the measured input, not an improved pose manufactured by limits.
        BirdDogPose pose;
        if (constraint != null && constraint.IsReady)
        {
            if (!constraint.HasInputPose || constraint.RejectedInputThisFrame) { LoseTracking(dt); return; }
            pose = constraint.InputPose;
        }
        else if (!BirdDogPose.TryCapture(user, out pose)) { LoseTracking(dt); return; }
        SamplePose(pose, dt);
    }

    internal void SamplePose(BirdDogPose pose, float dt)
    {
        TrackingAvailable = true; missingAge = 0f;
        dt = Mathf.Clamp(dt, 0f, 0.1f);
        phaseElapsed += dt;
        bool atRest = IsRest(pose);
        float lp = PairProgress(pose, BirdDogSide.LeftArmRightLeg), rp = PairProgress(pose, BirdDogSide.RightArmLeftLeg);
        // 60 ms filtering removes packet-scale threshold chatter without
        // introducing a long delay when a real A or B endpoint is reached.
        float blend = dt / (0.06f + dt);
        progressFilterLeft = hasProgressFilter ? progressFilterLeft + blend * (lp - progressFilterLeft) : lp;
        progressFilterRight = hasProgressFilter ? progressFilterRight + blend * (rp - progressFilterRight) : rp;
        hasProgressFilter = true; lp = progressFilterLeft; rp = progressFilterRight;
        CurrentProgress = LockedSide == BirdDogSide.None ? Mathf.Max(lp, rp) : PairProgress(pose, LockedSide);
        CurrentLiveComparisonScore = CurrentDeduction = 0f; DeductionReasons = "无扣分";
        float enter = DifficultyMode == HipThrustDifficultyMode.Easy ? 0.38f : DifficultyMode == HipThrustDifficultyMode.Hard ? 0.50f : 0.44f;

        switch (Phase)
        {
            case HipThrustPhase.WaitingForRaise:
                Guidance = armed ? "伸出任意一组对角手腿" : waitingReason ?? "请先将双手、双膝回到四点支撑位";
                if (!armed)
                {
                    if (Confirm(HipThrustPhase.WaitingForRaise, atRest, dt, 0.20f)) { armed = true; confirmation = 0f; }
                    break;
                }
                // Departing A requires ANY limb to move; entering B still
                // requires the complete diagonal pair. Otherwise a single
                // raised arm/leg remains falsely labelled as ready A forever.
                float departure = Mathf.Max(Mathf.Max(ArmProgress(pose, BirdDogSide.LeftArmRightLeg), LegProgress(pose, BirdDogSide.LeftArmRightLeg)),
                    Mathf.Max(ArmProgress(pose, BirdDogSide.RightArmLeftLeg), LegProgress(pose, BirdDogSide.RightArmLeftLeg)));
                if (Confirm(HipThrustPhase.Raising, !atRest && departure > 0.15f, dt, 0.08f)) ChangePhase(HipThrustPhase.Raising);
                break;
            case HipThrustPhase.Raising:
                Guidance = "对应手臂和对侧腿一起伸展";
                BirdDogSide next = lp >= rp ? BirdDogSide.LeftArmRightLeg : BirdDogSide.RightArmLeftLeg;
                if (candidate == BirdDogSide.None ||
                    (next != candidate && (next == BirdDogSide.LeftArmRightLeg ? lp : rp) > PairProgress(pose, candidate) + 0.08f))
                { candidate = next; sideConfirmation = candidateMissSeconds = 0f; }
                bool ready = PairProgress(pose, candidate) >= enter && BroadB(pose, candidate);
                if (ready) { sideConfirmation += dt; candidateMissSeconds = 0f; }
                else { candidateMissSeconds += dt; if (candidateMissSeconds >= 0.08f) sideConfirmation = 0f; }
                if (sideConfirmation >= 0.20f)
                {
                    LockedSide = candidate; enteredB = true; validHold = roundBest = 0f;
                    peakHoldProgress = CurrentProgress;
                    peakArmProgress = ArmProgress(pose, LockedSide); peakLegProgress = LegProgress(pose, LockedSide);
                    ChangePhase(HipThrustPhase.Holding);
                    EvaluateScore(pose); // Entry gives its base immediately, with zero time bonus.
                }
                else if (Confirm(HipThrustPhase.WaitingForRaise, atRest, dt, 0.16f)) ResetRound(true);
                else RecoverStalledTransition(dt);
                break;
            case HipThrustPhase.Holding:
                peakHoldProgress = Mathf.Max(peakHoldProgress, CurrentProgress);
                float armProgress = ArmProgress(pose, LockedSide), legProgress = LegProgress(pose, LockedSide);
                peakArmProgress = Mathf.Max(peakArmProgress, armProgress); peakLegProgress = Mathf.Max(peakLegProgress, legProgress);
                bool coordinatedDescent = peakArmProgress - armProgress >= 0.18f && peakLegProgress - legProgress >= 0.18f;
                bool stillB = CurrentProgress >= enter - 0.13f && BroadB(pose, LockedSide, true) &&
                    !coordinatedDescent;
                Guidance = stillB ? "保持本侧伸展；手腿与身体方向平行、高度齐平" : "本侧已离开目标位，请四肢回到 A 后再换侧";
                if (stillB)
                {
                    if (IsStable(pose, dt)) validHold = Mathf.Min(TargetHoldSeconds, validHold + dt);
                    else Guidance = "保持手腿稳定后继续累计保持加分";
                    EvaluateScore(pose);
                }
                else hasPreviousHold = false;
                if (Confirm(HipThrustPhase.Lowering, !stillB, dt, 0.15f)) ChangePhase(HipThrustPhase.Lowering);
                break;
            case HipThrustPhase.Lowering:
                lowestLoweringProgress = Mathf.Min(lowestLoweringProgress, CurrentProgress);
                Guidance = "双手、双膝全部回位后结束本轮";
                if (Confirm(HipThrustPhase.WaitingForRaise, atRest, dt, 0.16f))
                {
                    if (enteredB)
                    {
                        LastRoundBestScore = roundBest; LastRoundDeduction = bestDeduction; LastRoundReasons = bestReasons;
                        HasCompletedRound = true; RoundCompleted?.Invoke(roundBest);
                    }
                    ResetRound(true);
                }
                else if (!atRest && PairProgress(pose, LockedSide) >= enter && BroadB(pose, LockedSide) &&
                    (CurrentProgress >= lowestLoweringProgress + 0.06f || CurrentProgress >= peakHoldProgress - 0.06f))
                {
                    sideConfirmation += dt;
                    if (sideConfirmation >= 0.16f)
                    {
                        // Resume the SAME attempt; never award another base or
                        // allow the other diagonal to inherit its hold time.
                        peakHoldProgress = CurrentProgress; hasPreviousHold = false;
                        peakArmProgress = ArmProgress(pose, LockedSide); peakLegProgress = LegProgress(pose, LockedSide);
                        ChangePhase(HipThrustPhase.Holding); EvaluateScore(pose);
                    }
                }
                else { sideConfirmation = 0f; RecoverStalledTransition(dt); }
                break;
        }
        LiveScoreChanged?.Invoke(CurrentLiveComparisonScore);
    }

    private void EvaluateScore(BirdDogPose p)
    {
        var arm = p.Arm(LockedSide); var leg = p.Leg(LockedSide);
        ArmHorizontalError = arm.HorizontalError; LegHorizontalError = leg.HorizontalError;
        ArmElevation = arm.Elevation; LegElevation = leg.Elevation;
        // Four non-overlapping terms share a TOTAL five-point cap. Targets are
        // physically horizontal/parallel, not the imperfections of the recording.
        var reasons = new List<string>(4);
        float deduction = 0f;
        AddPenalty(reasons, ref deduction, arm.HorizontalError, 12f, 60f, "手臂水平方向偏歪");
        AddPenalty(reasons, ref deduction, leg.HorizontalError, 12f, 60f, "腿部水平方向偏歪");
        AddPenalty(reasons, ref deduction, Mathf.Abs(arm.Elevation), 10f, 45f, arm.Elevation < 0f ? "手臂竖直高度不足" : "手臂抬得过高");
        AddPenalty(reasons, ref deduction, Mathf.Abs(leg.Elevation), 10f, 45f, leg.Elevation < 0f ? "腿部竖直高度不足" : "腿部抬得过高");
        CurrentDeduction = Mathf.Clamp(deduction, 0f, MaximumDeduction);
        DeductionReasons = reasons.Count == 0 ? "无扣分" : string.Join("\n", reasons);
        CurrentLiveComparisonScore = EntryBaseScore + HoldBonus - CurrentDeduction;
        if (CurrentLiveComparisonScore >= roundBest)
        {
            roundBest = CurrentLiveComparisonScore; bestDeduction = CurrentDeduction; bestReasons = DeductionReasons;
        }
    }
    private static void AddPenalty(List<string> reasons, ref float total, float error, float free, float full, string reason)
    {
        float amount = 1.25f * Mathf.InverseLerp(free, full, error);
        // Quantize once; the displayed breakdown sums exactly to the deduction.
        amount = Mathf.Round(amount * 100f) / 100f;
        total += amount;
        if (amount > 0f) reasons.Add(reason + " -" + amount.ToString("0.00") + "分");
    }
    private bool IsStable(BirdDogPose p, float dt)
    {
        var a = p.Arm(LockedSide); var l = p.Leg(LockedSide);
        float motion = hasPreviousHold ? Mathf.Max(LimbChange(a, previousArm), LimbChange(l, previousLeg)) / Mathf.Max(0.001f, dt) : 0f;
        previousArm = a; previousLeg = l; hasPreviousHold = true;
        return motion <= 1f;
    }
    private static float LimbChange(BirdDogLimb a, BirdDogLimb b) => Mathf.Max(
        Mathf.Max(Mathf.Abs(a.Height - b.Height), Mathf.Abs(a.Reach - b.Reach)),
        Mathf.Max(Mathf.Abs(a.HorizontalError - b.HorizontalError), Mathf.Abs(a.Elevation - b.Elevation)) / 180f);
    private bool BroadB(BirdDogPose p, BirdDogSide side, bool maintaining = false)
    {
        var a = p.Arm(side); var l = p.Leg(side);
        var sa = p.SupportArm(side); var sl = p.SupportLeg(side);
        return p.TorsoTilt <= (maintaining ? 44f : 40f) && a.Bend >= (maintaining ? 95f : 105f) && l.Bend >= (maintaining ? 100f : 110f) &&
            a.HorizontalError <= (maintaining ? 82f : 75f) && l.HorizontalError <= (maintaining ? 82f : 75f) && a.Reach > 0.15f && l.Reach > 0.15f &&
            a.Height > -0.70f && l.Height > -0.70f && a.Height < 0.65f && l.Height < 0.65f &&
            sa.Height < (maintaining ? -0.33f : -0.40f) && sl.Bend < (maintaining ? 138f : 130f) && sl.Height < (maintaining ? -0.18f : -0.25f);
    }
    private bool IsRest(BirdDogPose p) => p.TorsoTilt <= 40f &&
        p.LeftArm.Height < -0.55f && p.RightArm.Height < -0.55f &&
        p.LeftLeg.Bend < 130f && p.RightLeg.Bend < 130f &&
        Mathf.Abs(p.LeftArm.Height - rest.LeftArm.Height) < 0.30f &&
        Mathf.Abs(p.RightArm.Height - rest.RightArm.Height) < 0.30f &&
        Mathf.Abs(p.LeftLeg.Height - rest.LeftLeg.Height) < 0.30f &&
        Mathf.Abs(p.RightLeg.Height - rest.RightLeg.Height) < 0.30f &&
        Mathf.Abs(p.LeftLeg.Bend - rest.LeftLeg.Bend) < 40f &&
        Mathf.Abs(p.RightLeg.Bend - rest.RightLeg.Bend) < 40f;

    private void RecoverStalledTransition(float dt)
    {
        if (Mathf.Abs(CurrentProgress - previousTransitionProgress) > 0.015f)
        { stalledElapsed = 0f; previousTransitionProgress = CurrentProgress; }
        else stalledElapsed += dt;
        if (phaseElapsed >= MaximumTransitionSeconds || stalledElapsed >= StalledTransitionSeconds)
            ResetRound(false, "动作未完成，已结束过渡等待；请先将四肢回到 A 再重试");
    }

    private float PairProgress(BirdDogPose p, BirdDogSide side)
    {
        var b = side == BirdDogSide.LeftArmRightLeg ? left : right;
        return Mathf.Min(LimbProgress(p.Arm(side), rest.Arm(side), b.Arm(side)),
            LimbProgress(p.Leg(side), rest.Leg(side), b.Leg(side)));
    }
    private float ArmProgress(BirdDogPose p, BirdDogSide side) =>
        LimbProgress(p.Arm(side), rest.Arm(side), (side == BirdDogSide.LeftArmRightLeg ? left : right).Arm(side));
    private float LegProgress(BirdDogPose p, BirdDogSide side) =>
        LimbProgress(p.Leg(side), rest.Leg(side), (side == BirdDogSide.LeftArmRightLeg ? left : right).Leg(side));
    private static float LimbProgress(BirdDogLimb p, BirdDogLimb a, BirdDogLimb b) => Mathf.Clamp01(
        (p.Height - a.Height) / Mathf.Max(0.08f, b.Height - a.Height));
    private bool Confirm(HipThrustPhase next, bool condition, float dt, float seconds)
    {
        if (pending != next) confirmation = 0f;
        pending = next; confirmation = condition ? confirmation + dt : 0f;
        return confirmation >= seconds;
    }
    private void ChangePhase(HipThrustPhase next)
    {
        confirmation = 0f;
        if (Phase == next) return;
        Phase = next; PhaseChanged?.Invoke(next);
        if (next == HipThrustPhase.Lowering) lowestLoweringProgress = CurrentProgress;
        phaseElapsed = stalledElapsed = sideConfirmation = candidateMissSeconds = 0f;
        previousTransitionProgress = CurrentProgress;
    }
    private void ResetRound(bool ready = false, string reason = null)
    {
        armed = ready; enteredB = false; LockedSide = candidate = BirdDogSide.None;
        hasPreviousHold = false;
        waitingReason = reason; phaseElapsed = stalledElapsed = peakHoldProgress = 0f;
        peakArmProgress = peakLegProgress = 0f;
        hasProgressFilter = false; candidateMissSeconds = 0f;
        confirmation = sideConfirmation = validHold = roundBest = CurrentLiveComparisonScore = CurrentDeduction = 0f;
        ArmHorizontalError = LegHorizontalError = ArmElevation = LegElevation = 0f;
        bestDeduction = 0f; bestReasons = DeductionReasons = "无扣分";
        Guidance = ready ? "伸出任意一组对角手腿" : "请先回到四点支撑准备位";
        ChangePhase(HipThrustPhase.WaitingForRaise);
        LiveScoreChanged?.Invoke(0f);
    }
    private void LoseTracking(float dt)
    {
        TrackingAvailable = false; missingAge += dt; confirmation = sideConfirmation = 0f;
        hasPreviousHold = false;
        CurrentLiveComparisonScore = 0f;
        if (missingAge >= 0.35f) ResetRound(); // Abort without committing a stale round.
        Guidance = "动作数据中断，等待恢复后先回到 A";
        LiveScoreChanged?.Invoke(0f);
    }
    private AvatarMotionSource FindTracker()
    {
        if (user == null) return null;
        var source = user.GetComponentInParent<AvatarMotionSource>();
        if (source != null) return source;
        foreach (var item in FindObjectsOfType<AvatarMotionSource>())
            if (item.Model == user.gameObject) return item;
        return null;
    }
    private void OnDisable() => CancelSession();
}
}
