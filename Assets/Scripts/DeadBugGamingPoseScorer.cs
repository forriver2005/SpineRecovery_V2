using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct DeadBugCheckpointScore
{
    public string stateName;
    public int loopIndex;
    public int loopCount;
    public int pairIndex;
    public bool isUp;
    public float averageScore;
    public float bestScore;
    public int beforeFrameCount;
    public int holdFrameCount;
    public int afterFrameCount;
    public List<float> frameScores;

    public int TotalFrameCount => beforeFrameCount + holdFrameCount + afterFrameCount;
}

public enum DeadBugScoreFrameRegion
{
    BeforeHold,
    Hold,
    AfterHold
}

public struct DeadBugFrameScore
{
    public string stateName;
    public int loopIndex;
    public int loopCount;
    public int pairIndex;
    public bool isUp;
    public DeadBugScoreFrameRegion region;
    public int frameIndexInCheckpoint;
    public float score;
}

// Scores the user's Dead Bug pose with the same body-relative anatomical engine
// used by zEvaluation. Standing calibration is required before a session starts.
public class DeadBugGamingPoseScorer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CoachActionController coachController;
    [SerializeField] private Animator coachAnimator;
    [SerializeField] private Animator userAnimator;

    [Header("Scoring Window")]
    [Tooltip("When enabled, checkpoint averages contain only frames while the coach holds the target pose.")]
    [SerializeField] private bool scoreHoldFramesOnly = true;
    [Min(1)] [SerializeField] private int framesBeforeHold = 20;
    [Min(1)] [SerializeField] private int framesAfterHold = 20;

    [Header("Evaluation Scoring")]
    [SerializeField] private AnatomicalMotionScoringSettings anatomicalScoring =
        new AnatomicalMotionScoringSettings();

    [Header("Single-Pose Calibration")]
    [Tooltip("Minimum valid humanoid bones required to calibrate.")]
    [Min(3)] [SerializeField] private int minimumTrackedBones = 8;
    [Tooltip("How long the standing pose must remain stable before sampling starts.")]
    [Min(0.1f)] [SerializeField] private float calibrationStableDuration = 0.5f;
    [Tooltip("Multi-frame sampling duration. No calibration is taken from a single frame.")]
    [Min(0.5f)] [SerializeField] private float calibrationSampleDuration = 1.2f;
    [Tooltip("Wearable motion below this speed is considered stable.")]
    [Min(1f)] [SerializeField] private float calibrationMotionLimit = 45f;
    [Tooltip("Fraction of moving bones tolerated as normal tracker jitter.")]
    [Range(0f, 0.6f)] [SerializeField] private float allowedMovingBoneFraction = 0.4f;
    [SerializeField] private bool autoCalibrateOnStart = true;

    [Header("Calibration Bones")]
    [SerializeField] private HumanBodyBones[] scoringBones =
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm
    };

    public event Action<DeadBugCheckpointScore> CheckpointScored;
    public event Action SessionStarted;
    public event Action<float> LiveScoreChanged;
    public event Action<DeadBugFrameScore> FrameScored;
    public event Action<float, IReadOnlyList<DeadBugCheckpointScore>> SessionScored;
    public event Action<AnatomicalLimbScores> LimbScoresChanged;
    public event Action<bool> CalibrationStateChanged;
    public event Action<float, string> CalibrationProgressChanged;

    private sealed class ActiveCheckpoint
    {
        public CoachActionController.ScoringCheckpointInfo info;
        public readonly List<float> scores = new List<float>();
        public int beforeFrameCount;
        public int holdFrameCount;
        public int afterFrameCount;
        public int afterFramesRemaining;
        public bool isHolding = true;
    }

    private readonly Queue<float> recentFrameScores = new Queue<float>();
    private readonly List<ActiveCheckpoint> activeCheckpoints = new List<ActiveCheckpoint>();
    private readonly List<DeadBugCheckpointScore> sessionScores = new List<DeadBugCheckpointScore>();
    private readonly AnimatorMotionEvidence sessionMotionEvidence = new AnimatorMotionEvidence();
    private readonly Dictionary<HumanBodyBones, Vector3> coachCalibrationDirections =
        new Dictionary<HumanBodyBones, Vector3>();
    private readonly Dictionary<HumanBodyBones, Vector3> userCalibrationDirections =
        new Dictionary<HumanBodyBones, Vector3>();
    private readonly Dictionary<HumanBodyBones, Quaternion> previousCalibrationRotations =
        new Dictionary<HumanBodyBones, Quaternion>();
    private bool sessionActive;
    private bool calibrated;
    private bool calibrating;
    private float lastSessionAverage;
    private float calibrationMotionSampleElapsed;
    private bool lastCalibrationStability = true;
    private Coroutine calibrationRoutine;
    private AnatomicalMotionScoringEngine scoringEngine;
    private AnatomicalPoseScore lastFrameResult;
    private AnatomicalLimbScores lastLimbScores;

    private sealed class DirectionAccumulator
    {
        private Vector3 sum;

        public int Count { get; private set; }

        public void Add(Vector3 value)
        {
            if (value.sqrMagnitude < 0.5f)
            {
                return;
            }

            sum += value.normalized;
            Count++;
        }

        public Vector3 Average()
        {
            return Count > 0 && sum.sqrMagnitude > 1e-8f
                ? sum.normalized
                : Vector3.zero;
        }
    }

    public IReadOnlyList<DeadBugCheckpointScore> SessionScores => sessionScores;
    public float LastSessionAverage => lastSessionAverage;
    public bool IsSessionActive => sessionActive;
    public AnatomicalPoseScore LastFrameScore => lastFrameResult;
    public AnatomicalLimbScores LastLimbScores => lastLimbScores;
    public bool IsCalibrated => calibrated;
    public bool IsCalibrating => calibrating;
    public Animator CoachAnimator => coachAnimator;
    public Animator UserAnimator => userAnimator;

    private void Awake()
    {
        scoringEngine = new AnatomicalMotionScoringEngine(anatomicalScoring);
        ResolveReferences();
    }

    private void Start()
    {
        if (autoCalibrateOnStart)
        {
            BeginCalibration();
        }
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToCoach();
    }

    private void OnDisable()
    {
        UnsubscribeFromCoach();
        sessionActive = false;
        recentFrameScores.Clear();
        activeCheckpoints.Clear();
        scoringEngine?.ResetSession();

        if (calibrationRoutine != null)
        {
            StopCoroutine(calibrationRoutine);
            calibrationRoutine = null;
        }

        calibrating = false;
    }

    private void LateUpdate()
    {
        if (!sessionActive || !calibrated || coachAnimator == null || userAnimator == null)
        {
            return;
        }

        sessionMotionEvidence.Sample();

        float frameScore = ScoreCurrentPose();
        float scoringSample = lastFrameResult.rawScore;
        LiveScoreChanged?.Invoke(frameScore);
        LimbScoresChanged?.Invoke(lastLimbScores);

        for (int index = activeCheckpoints.Count - 1; index >= 0; index--)
        {
            ActiveCheckpoint checkpoint = activeCheckpoints[index];
            if (checkpoint.isHolding)
            {
                checkpoint.scores.Add(scoringSample);
                checkpoint.holdFrameCount++;
                EmitFrameScore(checkpoint, scoringSample, DeadBugScoreFrameRegion.Hold);
            }
            else if (checkpoint.afterFramesRemaining > 0)
            {
                if (scoreHoldFramesOnly)
                {
                    continue;
                }

                checkpoint.scores.Add(scoringSample);
                checkpoint.afterFrameCount++;
                EmitFrameScore(checkpoint, scoringSample, DeadBugScoreFrameRegion.AfterHold);
                checkpoint.afterFramesRemaining--;

                if (checkpoint.afterFramesRemaining == 0)
                {
                    FinalizeCheckpoint(checkpoint);
                    activeCheckpoints.RemoveAt(index);
                }
            }
        }

        recentFrameScores.Enqueue(scoringSample);
        int maximumBufferedFrames = Mathf.Max(1, framesBeforeHold);
        while (recentFrameScores.Count > maximumBufferedFrames)
        {
            recentFrameScores.Dequeue();
        }
    }

    public void BeginCalibration()
    {
        if (sessionActive)
        {
            return;
        }

        if (calibrationRoutine != null)
        {
            StopCoroutine(calibrationRoutine);
        }

        calibrationRoutine = StartCoroutine(CalibrationSequence());
    }

    private IEnumerator CalibrationSequence()
    {
        ResolveReferences();
        if (scoringEngine == null)
        {
            scoringEngine = new AnatomicalMotionScoringEngine(anatomicalScoring);
        }

        calibrated = false;
        calibrating = true;
        CalibrationStateChanged?.Invoke(false);
        ReportCalibrationProgress(0f, "\u8bf7\u81ea\u7136\u7ad9\u7acb\u5e76\u4fdd\u6301\u7a33\u5b9a");
        scoringEngine.ResetSession();
        coachCalibrationDirections.Clear();
        userCalibrationDirections.Clear();

        if (coachAnimator == null || userAnimator == null || coachController == null)
        {
            calibrating = false;
            ReportCalibrationProgress(0f, "\u672a\u627e\u5230\u6559\u7ec3\u6216\u7528\u6237\u6a21\u578b");
            calibrationRoutine = null;
            yield break;
        }

        PrepareStandingCalibrationPose();
        yield return null;
        yield return CollectStandingCalibration();

        scoringEngine.SetStandingCalibration(
            userCalibrationDirections,
            coachCalibrationDirections);
        calibrated = scoringEngine.CalibratedSegmentCount >= 6;
        calibrating = false;
        CalibrationStateChanged?.Invoke(calibrated);
        ReportCalibrationProgress(
            calibrated ? 1f : 0f,
            calibrated
                ? "\u6821\u51c6\u5b8c\u6210"
                : "\u6709\u6548\u9aa8\u9abc\u4e0d\u8db3\uff0c\u8bf7\u68c0\u67e5\u4f20\u611f\u5668");

        Debug.Log(
            $"[DeadBugScore] Anatomical standing calibration complete: " +
            $"segments={scoringEngine.CalibratedSegmentCount}, success={calibrated}.",
            this);
        calibrationRoutine = null;
    }

    private IEnumerator CollectStandingCalibration()
    {
        var coachSamples = new Dictionary<HumanBodyBones, DirectionAccumulator>();
        var userSamples = new Dictionary<HumanBodyBones, DirectionAccumulator>();
        previousCalibrationRotations.Clear();
        calibrationMotionSampleElapsed = 0f;
        lastCalibrationStability = true;
        float stableElapsed = 0f;
        float sampleElapsed = 0f;
        float unstableDuringSample = 0f;
        bool samplingStarted = false;

        while (sampleElapsed < calibrationSampleDuration)
        {
            int validBones = CountValidCalibrationBones();
            int sharedSegments = CountSharedCalibrationSegments();
            bool trackingReady = sharedSegments >= 5 && validBones >= minimumTrackedBones;
            bool stable = CheckCalibrationStability();

            if (trackingReady && stable)
            {
                unstableDuringSample = 0f;
                stableElapsed += Time.unscaledDeltaTime;
                if (stableElapsed >= calibrationStableDuration)
                {
                    samplingStarted = true;
                    CaptureDirectionSample(coachSamples, userSamples);
                    sampleElapsed += Time.unscaledDeltaTime;
                }
            }
            else if (samplingStarted && trackingReady)
            {
                // Pause for short tracker spikes; restart only after sustained motion.
                unstableDuringSample += Time.unscaledDeltaTime;
                if (unstableDuringSample > 0.5f)
                {
                    stableElapsed = 0f;
                    sampleElapsed = 0f;
                    samplingStarted = false;
                    coachSamples.Clear();
                    userSamples.Clear();
                }
            }
            else
            {
                stableElapsed = 0f;
            }

            if (!trackingReady)
            {
                ReportCalibrationProgress(
                    0f,
                    $"\u8bf7\u68c0\u67e5\u4f20\u611f\u5668 ({validBones}/{minimumTrackedBones})");
            }
            else if (!stable)
            {
                ReportCalibrationProgress(
                    0f,
                    "\u68c0\u6d4b\u5230\u6643\u52a8\uff0c\u8bf7\u4fdd\u6301\u81ea\u7136\u7ad9\u7acb");
            }
            else if (stableElapsed < calibrationStableDuration)
            {
                ReportCalibrationProgress(
                    0f,
                    $"\u4fdd\u6301\u7ad9\u7acb {calibrationStableDuration - stableElapsed:0.0}s");
            }
            else
            {
                float progress = Mathf.Clamp01(sampleElapsed / calibrationSampleDuration);
                ReportCalibrationProgress(progress, $"\u6b63\u5728\u6821\u51c6 {progress:P0}");
            }

            yield return null;
        }

        StoreAveragedDirections(coachSamples, coachCalibrationDirections);
        StoreAveragedDirections(userSamples, userCalibrationDirections);
    }

    private int CountValidCalibrationBones()
    {
        int count = 0;
        foreach (HumanBodyBones bone in scoringBones)
        {
            if (coachAnimator.GetBoneTransform(bone) != null &&
                userAnimator.GetBoneTransform(bone) != null)
            {
                count++;
            }
        }

        return count;
    }

    private int CountSharedCalibrationSegments()
    {
        Dictionary<HumanBodyBones, Vector3> coachPose = scoringEngine.CaptureRawPose(coachAnimator);
        Dictionary<HumanBodyBones, Vector3> userPose = scoringEngine.CaptureRawPose(userAnimator);
        int count = 0;
        foreach (HumanBodyBones segment in coachPose.Keys)
        {
            if (userPose.ContainsKey(segment))
            {
                count++;
            }
        }

        return count;
    }

    private void CaptureDirectionSample(
        Dictionary<HumanBodyBones, DirectionAccumulator> coachSamples,
        Dictionary<HumanBodyBones, DirectionAccumulator> userSamples)
    {
        Dictionary<HumanBodyBones, Vector3> coachPose = scoringEngine.CaptureRawPose(coachAnimator);
        Dictionary<HumanBodyBones, Vector3> userPose = scoringEngine.CaptureRawPose(userAnimator);
        foreach (KeyValuePair<HumanBodyBones, Vector3> entry in coachPose)
        {
            if (!userPose.TryGetValue(entry.Key, out Vector3 userDirection))
            {
                continue;
            }

            if (!coachSamples.TryGetValue(entry.Key, out DirectionAccumulator coachAccumulator))
            {
                coachAccumulator = new DirectionAccumulator();
                coachSamples[entry.Key] = coachAccumulator;
            }

            if (!userSamples.TryGetValue(entry.Key, out DirectionAccumulator userAccumulator))
            {
                userAccumulator = new DirectionAccumulator();
                userSamples[entry.Key] = userAccumulator;
            }

            coachAccumulator.Add(entry.Value);
            userAccumulator.Add(userDirection);
        }
    }

    private static void StoreAveragedDirections(
        Dictionary<HumanBodyBones, DirectionAccumulator> samples,
        Dictionary<HumanBodyBones, Vector3> destination)
    {
        destination.Clear();
        foreach (KeyValuePair<HumanBodyBones, DirectionAccumulator> entry in samples)
        {
            if (entry.Value.Count > 0)
            {
                destination[entry.Key] = entry.Value.Average();
            }
        }
    }

    private bool CheckCalibrationStability()
    {
        calibrationMotionSampleElapsed += Time.unscaledDeltaTime;
        if (calibrationMotionSampleElapsed < 0.15f)
        {
            return lastCalibrationStability;
        }

        float duration = calibrationMotionSampleElapsed;
        calibrationMotionSampleElapsed = 0f;
        int checkedCount = 0;
        int movingCount = 0;
        foreach (HumanBodyBones bone in scoringBones)
        {
            Transform userBone = userAnimator.GetBoneTransform(bone);
            if (userBone == null)
            {
                continue;
            }

            Quaternion rotation = userBone.localRotation;
            if (previousCalibrationRotations.TryGetValue(bone, out Quaternion previous))
            {
                float speed = Quaternion.Angle(previous, rotation) / Mathf.Max(0.001f, duration);
                if (speed > calibrationMotionLimit)
                {
                    movingCount++;
                }
            }

            previousCalibrationRotations[bone] = rotation;
            checkedCount++;
        }

        int allowedMoving = Mathf.Max(
            1,
            Mathf.CeilToInt(checkedCount * allowedMovingBoneFraction));
        lastCalibrationStability = checkedCount > 0 && movingCount <= allowedMoving;
        return lastCalibrationStability;
    }

    private void PrepareStandingCalibrationPose()
    {
        coachAnimator.enabled = true;
        coachAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        coachAnimator.Rebind();
        coachAnimator.Update(0f);
        coachAnimator.speed = 0f;

        string stateName = coachController.StartSegmentStateName;
        if (!string.IsNullOrEmpty(stateName))
        {
            coachAnimator.Play(stateName, 0, 0f);
            coachAnimator.Update(0f);
        }

        coachAnimator.speed = 0f;
    }

    private void ReportCalibrationProgress(float progress, string message)
    {
        CalibrationProgressChanged?.Invoke(Mathf.Clamp01(progress), message);
    }

    private void HandleSessionStarted()
    {
        ResolveReferences();
        sessionScores.Clear();
        activeCheckpoints.Clear();
        recentFrameScores.Clear();
        lastSessionAverage = 0f;
        scoringEngine?.ResetSession();
        sessionActive = calibrated && coachAnimator != null && userAnimator != null;

        if (!sessionActive)
        {
            Debug.LogError(
                calibrated
                    ? "DeadBugGamingPoseScorer needs both coach and user Animators."
                    : "DeadBugGamingPoseScorer cannot start before standing calibration.",
                this);
            return;
        }

        sessionMotionEvidence.Begin(coachAnimator, userAnimator, scoringBones);
        SessionStarted?.Invoke();
    }

    private void HandleCheckpointStarted(CoachActionController.ScoringCheckpointInfo info)
    {
        if (!sessionActive)
        {
            return;
        }

        ActiveCheckpoint checkpoint = new ActiveCheckpoint
        {
            info = info,
            afterFramesRemaining = Mathf.Max(1, framesAfterHold)
        };

        // Simple difficulty has no hold. Use the arrival frames so its
        // checkpoints still receive a meaningful anatomical score.
        if (!scoreHoldFramesOnly || info.holdSeconds <= 0f)
        {
            foreach (float score in recentFrameScores)
            {
                checkpoint.scores.Add(score);
                checkpoint.beforeFrameCount++;
                EmitFrameScore(checkpoint, score, DeadBugScoreFrameRegion.BeforeHold);
            }
        }

        activeCheckpoints.Add(checkpoint);
    }

    private void HandleCheckpointEnded(CoachActionController.ScoringCheckpointInfo info)
    {
        for (int index = activeCheckpoints.Count - 1; index >= 0; index--)
        {
            ActiveCheckpoint checkpoint = activeCheckpoints[index];
            if (IsSameCheckpoint(checkpoint.info, info) && checkpoint.isHolding)
            {
                checkpoint.isHolding = false;

                if (scoreHoldFramesOnly)
                {
                    FinalizeCheckpoint(checkpoint);
                    activeCheckpoints.RemoveAt(index);
                }
                return;
            }
        }
    }

    private void HandleSessionCompleted()
    {
        if (!sessionActive)
        {
            return;
        }

        // Normally every post-hold window finishes during the following clip.
        // Finalize any partial window as a safeguard if a very short clip ends first.
        for (int index = activeCheckpoints.Count - 1; index >= 0; index--)
        {
            FinalizeCheckpoint(activeCheckpoints[index]);
        }
        activeCheckpoints.Clear();

        float total = 0f;
        foreach (DeadBugCheckpointScore score in sessionScores)
        {
            total += score.averageScore;
        }

        lastSessionAverage = sessionScores.Count > 0 ? total / sessionScores.Count : 0f;
        sessionMotionEvidence.Stop();
        sessionActive = false;

        Debug.Log(
            $"[DeadBugScore] Session complete: average={lastSessionAverage:0.0}, " +
            $"checkpoints={sessionScores.Count}.",
            this);
        SessionScored?.Invoke(lastSessionAverage, sessionScores);
        SpineFlowTrainingSession.CompleteGameTraining(
            sessionScores,
            sessionMotionEvidence.MaximumUserMotionDegrees,
            sessionMotionEvidence.MaximumCoachMotionDegrees);
    }

    private void FinalizeCheckpoint(ActiveCheckpoint checkpoint)
    {
        float best = 0f;
        foreach (float score in checkpoint.scores)
        {
            best = Mathf.Max(best, score);
        }

        DeadBugCheckpointScore result = new DeadBugCheckpointScore
        {
            stateName = checkpoint.info.stateName,
            loopIndex = checkpoint.info.loopIndex,
            loopCount = checkpoint.info.loopCount,
            pairIndex = checkpoint.info.pairIndex,
            isUp = checkpoint.info.isUp,
            averageScore = RobustPoseScoringEngine.CalculateRobustAverage(
                checkpoint.scores,
                0.1f),
            bestScore = best,
            beforeFrameCount = checkpoint.beforeFrameCount,
            holdFrameCount = checkpoint.holdFrameCount,
            afterFrameCount = checkpoint.afterFrameCount,
            frameScores = new List<float>(checkpoint.scores)
        };

        sessionScores.Add(result);
        Debug.Log(
            $"[DeadBugScore] {result.stateName}, loop={result.loopIndex + 1}/{result.loopCount}, " +
            $"average={result.averageScore:0.0}, best={result.bestScore:0.0}, " +
            $"frames={result.beforeFrameCount}+{result.holdFrameCount}+{result.afterFrameCount}.",
            this);
        CheckpointScored?.Invoke(result);
    }

    private void EmitFrameScore(
        ActiveCheckpoint checkpoint,
        float score,
        DeadBugScoreFrameRegion region)
    {
        FrameScored?.Invoke(new DeadBugFrameScore
        {
            stateName = checkpoint.info.stateName,
            loopIndex = checkpoint.info.loopIndex,
            loopCount = checkpoint.info.loopCount,
            pairIndex = checkpoint.info.pairIndex,
            isUp = checkpoint.info.isUp,
            region = region,
            frameIndexInCheckpoint = checkpoint.scores.Count - 1,
            score = score
        });
    }

    // Body-relative segment directions remove room heading, avatar proportions
    // and local bone-axis differences. The engine also handles jitter and delay.
    private float ScoreCurrentPose()
    {
        if (scoringEngine == null)
        {
            scoringEngine = new AnatomicalMotionScoringEngine(anatomicalScoring);
            scoringEngine.SetStandingCalibration(
                userCalibrationDirections,
                coachCalibrationDirections);
        }

        lastFrameResult = scoringEngine.Evaluate(
            userAnimator,
            coachAnimator,
            Time.deltaTime,
            out lastLimbScores);
        return lastFrameResult.score;
    }

    private void ResolveReferences()
    {
        if (coachController == null)
        {
            coachController = FindObjectOfType<CoachActionController>(true);
        }

        if (coachAnimator != null && userAnimator != null)
        {
            return;
        }

        Animator[] animators = FindObjectsOfType<Animator>(true);
        foreach (Animator animator in animators)
        {
            if (animator == null)
            {
                continue;
            }

            if (coachAnimator == null &&
                string.Equals(animator.gameObject.name, "coach", StringComparison.OrdinalIgnoreCase))
            {
                coachAnimator = animator;
            }
            else if (userAnimator == null &&
                string.Equals(animator.gameObject.name, "user", StringComparison.OrdinalIgnoreCase))
            {
                userAnimator = animator;
            }
        }
    }

    private void SubscribeToCoach()
    {
        if (coachController == null)
        {
            Debug.LogError("DeadBugGamingPoseScorer could not find CoachActionController.", this);
            return;
        }

        coachController.ScoringSessionStarted -= HandleSessionStarted;
        coachController.ScoringCheckpointStarted -= HandleCheckpointStarted;
        coachController.ScoringCheckpointEnded -= HandleCheckpointEnded;
        coachController.ScoringSessionCompleted -= HandleSessionCompleted;

        coachController.ScoringSessionStarted += HandleSessionStarted;
        coachController.ScoringCheckpointStarted += HandleCheckpointStarted;
        coachController.ScoringCheckpointEnded += HandleCheckpointEnded;
        coachController.ScoringSessionCompleted += HandleSessionCompleted;
    }

    private void UnsubscribeFromCoach()
    {
        if (coachController == null)
        {
            return;
        }

        coachController.ScoringSessionStarted -= HandleSessionStarted;
        coachController.ScoringCheckpointStarted -= HandleCheckpointStarted;
        coachController.ScoringCheckpointEnded -= HandleCheckpointEnded;
        coachController.ScoringSessionCompleted -= HandleSessionCompleted;
    }

    private static bool IsSameCheckpoint(
        CoachActionController.ScoringCheckpointInfo left,
        CoachActionController.ScoringCheckpointInfo right)
    {
        return left.loopIndex == right.loopIndex &&
            left.pairIndex == right.pairIndex &&
            left.isUp == right.isUp &&
            left.stateName == right.stateName;
    }
}
