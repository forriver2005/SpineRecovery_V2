using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Quality summary retained for reports and post-session diagnostics.
public struct SegmentScore
{
    public int segmentIndex;
    public int repeatIndex;
    public int setIndex;
    public TrainingSegmentKind segmentKind;
    public string label;
    // Compatibility aliases for persisted reports. Practice scoring currently
    // keeps them equal to averageScore/bestScore so UI and statistics agree.
    public float rawAverageScore;
    public float rawBestScore;
    public float averageScore;  // 0-100 robust average over continuous samples
    public float bestScore;     // best single frame
    public int frameCount;
    public CoachTrackingQuality trackingQuality;
}

// World-space joints used by the deliberately broad Dead Bug preparation
// check. Keeping this as plain geometry makes the rule deterministic and easy
// to regression-test without a live tracker or a particular avatar rig.
public struct DeadBugPreparationPoseGeometry
{
    public Vector3 hips;
    public Vector3 upperTorso;
    public Vector3 leftShoulder;
    public Vector3 leftHand;
    public Vector3 rightShoulder;
    public Vector3 rightHand;
    public Vector3 leftUpperLeg;
    public Vector3 leftKnee;
    public Vector3 leftFoot;
    public Vector3 rightUpperLeg;
    public Vector3 rightKnee;
    public Vector3 rightFoot;
    public Vector3 worldUp;
}

[Serializable]
public sealed class FormalActionScoreMappingSettings
{
    [Tooltip("Raw scores at or below this value are not boosted.")]
    [Range(0f, 100f)] public float unchangedThrough = 50f;
    [Tooltip("The minimum acceptable raw formal-action score maps to an encouraging result.")]
    [Range(0f, 100f)] public float sourceEncouragement = 60f;
    [Range(0f, 100f)] public float mappedEncouragement = 70f;
    [Tooltip("A raw formal-action score at this point maps to the first target score.")]
    [Range(0f, 100f)] public float sourceGood = 70f;
    [Range(0f, 100f)] public float mappedGood = 80f;
    [Tooltip("A strong raw score enters the high 80s without being compressed into the high 90s.")]
    [Range(0f, 100f)] public float sourceVeryGood = 80f;
    [Range(0f, 100f)] public float mappedVeryGood = 88f;
    [Tooltip("A genuinely excellent raw score enters the low 90s.")]
    [Range(0f, 100f)] public float sourceExcellent = 90f;
    [Range(0f, 100f)] public float mappedExcellent = 93f;
    [Tooltip("High-end anchors preserve genuine 98, 99, and 100 scores.")]
    [Range(0f, 100f)] public float sourceOutstanding = 95f;
    [Range(0f, 100f)] public float mappedOutstanding = 96f;
    [Range(0f, 100f)] public float sourceElite = 98f;
    [Range(0f, 100f)] public float mappedElite = 98f;
    [Range(0f, 100f)] public float sourceNearPerfect = 99f;
    [Range(0f, 100f)] public float mappedNearPerfect = 99f;

    public float Map(float score)
    {
        float clamped = Mathf.Clamp(score, 0f, 100f);
        float floor = Mathf.Clamp(unchangedThrough, 0f, 99.92f);
        float encouragementSource = Mathf.Clamp(
            sourceEncouragement,
            floor + 0.01f,
            99.93f);
        float goodSource = Mathf.Clamp(
            sourceGood,
            encouragementSource + 0.01f,
            99.94f);
        float veryGoodSource = Mathf.Clamp(
            sourceVeryGood,
            goodSource + 0.01f,
            99.95f);
        float excellentSource = Mathf.Clamp(
            sourceExcellent,
            veryGoodSource + 0.01f,
            99.96f);
        float outstandingSource = Mathf.Clamp(
            sourceOutstanding,
            excellentSource + 0.01f,
            99.97f);
        float eliteSource = Mathf.Clamp(
            sourceElite,
            outstandingSource + 0.01f,
            99.98f);
        float nearPerfectSource = Mathf.Clamp(
            sourceNearPerfect,
            eliteSource + 0.01f,
            99.99f);
        float encouragementTarget = Mathf.Clamp(
            mappedEncouragement,
            encouragementSource,
            100f);
        float goodTarget = Mathf.Clamp(mappedGood, goodSource, 100f);
        goodTarget = Mathf.Max(goodTarget, encouragementTarget);
        float veryGoodTarget = Mathf.Clamp(
            mappedVeryGood,
            goodTarget,
            100f);
        veryGoodTarget = Mathf.Max(veryGoodTarget, veryGoodSource);
        float excellentTarget = Mathf.Clamp(
            mappedExcellent,
            veryGoodTarget,
            100f);
        excellentTarget = Mathf.Max(excellentTarget, excellentSource);
        float outstandingTarget = Mathf.Clamp(
            mappedOutstanding,
            excellentTarget,
            100f);
        outstandingTarget = Mathf.Max(
            outstandingTarget,
            outstandingSource);
        float eliteTarget = Mathf.Clamp(
            mappedElite,
            outstandingTarget,
            100f);
        eliteTarget = Mathf.Max(eliteTarget, eliteSource);
        float nearPerfectTarget = Mathf.Clamp(
            mappedNearPerfect,
            eliteTarget,
            100f);
        nearPerfectTarget = Mathf.Max(
            nearPerfectTarget,
            nearPerfectSource);

        if (clamped <= floor)
        {
            return clamped;
        }
        if (clamped <= encouragementSource)
        {
            return Mathf.Lerp(
                floor,
                encouragementTarget,
                Mathf.InverseLerp(
                    floor,
                    encouragementSource,
                    clamped));
        }
        if (clamped <= goodSource)
        {
            return Mathf.Lerp(
                encouragementTarget,
                goodTarget,
                Mathf.InverseLerp(
                    encouragementSource,
                    goodSource,
                    clamped));
        }
        if (clamped <= veryGoodSource)
        {
            return Mathf.Lerp(
                goodTarget,
                veryGoodTarget,
                Mathf.InverseLerp(goodSource, veryGoodSource, clamped));
        }
        if (clamped <= excellentSource)
        {
            return Mathf.Lerp(
                veryGoodTarget,
                excellentTarget,
                Mathf.InverseLerp(
                    veryGoodSource,
                    excellentSource,
                    clamped));
        }
        if (clamped <= outstandingSource)
        {
            return Mathf.Lerp(
                excellentTarget,
                outstandingTarget,
                Mathf.InverseLerp(
                    excellentSource,
                    outstandingSource,
                    clamped));
        }
        if (clamped <= eliteSource)
        {
            return Mathf.Lerp(
                outstandingTarget,
                eliteTarget,
                Mathf.InverseLerp(
                    outstandingSource,
                    eliteSource,
                    clamped));
        }
        if (clamped <= nearPerfectSource)
        {
            return Mathf.Lerp(
                eliteTarget,
                nearPerfectTarget,
                Mathf.InverseLerp(
                    eliteSource,
                    nearPerfectSource,
                    clamped));
        }
        return Mathf.Lerp(
            nearPerfectTarget,
            100f,
            Mathf.InverseLerp(nearPerfectSource, 100f, clamped));
    }

    // A near-perfect decimal score must not be rounded into a displayed 100.
    // Genuine mapped 98 and 99 remain available; only an actual 100 is shown
    // as the full score.
    public static int RoundForDisplay(float score)
    {
        float clamped = Mathf.Clamp(score, 0f, 100f);
        return clamped >= 100f
            ? 100
            : Mathf.Min(99, Mathf.RoundToInt(clamped));
    }
}

// Scores the IMU user's pose against the coach's frozen target pose at each
// playback phase. Coach mode observes tracking continuously while the playback
// timeline advances independently. Missing/invalid measurements are recorded as
// quality state and never block a phase.
public partial class PoseScorer : MonoBehaviour
{
    public const float DefaultProgressionTimeoutSeconds = 5f;
    [Header("References")]
    [SerializeField] private SegmentedCoachController coachController;
    [SerializeField] private Animator coachAnimator;
    [SerializeField] private Animator userAnimator;
    [SerializeField] private VoicePromptManager voiceManager; // 语音提示管理器
    [SerializeField] private ScorePopup scorePopup; // 分数弹出显示UI
    [SerializeField] private BodyPartFeedbackUI bodyPartFeedback; // 身体部位提示UI
    [SerializeField] private AvatarBodyPartHighlighter modelBodyPartHighlighter;
    [Tooltip("Shared tracker motor controller. It only consumes confirmed guidance parts.")]
    [SerializeField] private CoachHapticFeedbackController hapticFeedback;
    private readonly List<TrackerWearLocation> hapticLocationOverrides =
        new List<TrackerWearLocation>(2);
    private readonly List<BodyPart> hapticCandidateParts =
        new List<BodyPart>(5);
    private readonly List<BodyPart> selectedHapticParts =
        new List<BodyPart>(2);
    private readonly List<HapticTrackerCandidate> hapticTrackerCandidates =
        new List<HapticTrackerCandidate>(10);
    private TargetedExercisePoseGeometry latestTargetedGuidanceGeometry;
    private bool hasLatestTargetedGuidanceGeometry;
    [SerializeField] private bool enableModelSurfaceHighlight = true;
    [Tooltip("Dead Bug and Bird Dog use body-plane geometry instead of the " +
        "recorded coach pose. Generic preserves the reusable target comparer.")]
    [SerializeField] private PoseGuidanceExercise guidanceExercise =
        PoseGuidanceExercise.Generic;
    [Header("Hip Thrust Teaching")]
    [Tooltip("Legacy observation duration retained for scene compatibility; coach playback uses its own phase timing.")]
    [SerializeField, Range(0.5f, 3f)] private float hipThrustObservationSeconds = 2f;
    [SerializeField, Min(0f)] private float hipThrustRestSeconds = 4f;
    [Tooltip("Normalized hip-height difference needed to enter the green target band.")]
    [SerializeField, Min(0f)] private float hipThrustHeightEnterTolerance = 0.08f;
    [Tooltip("Normalized hip-height difference tolerated before leaving the green band.")]
    [SerializeField, Min(0f)] private float hipThrustHeightExitTolerance = 0.12f;
    [Tooltip("Independent, tighter hip-height band for the waist IMU target pulse.")]
    [SerializeField, Min(0f)] private float hipThrustHapticHeightTolerance = 0.05f;
    [Tooltip("Time the hip must remain in the haptic target band before one pulse.")]
    [SerializeField, Min(0f)] private float hipThrustHapticConfirmationSeconds = 0.18f;
    private static readonly Color HipThrustHeightReadyColor =
        new Color(0.05f, 1f, 0.25f, 0.95f);
    private HipThrustPoseGeometry hipThrustRestPose;
    private bool hasHipThrustRestPose;
    private bool hipThrustOptimalHeightPulseSent;
    private bool hipThrustOptimalHeightPulsePending;
    private bool hipThrustHapticHeightCandidateActive;
    private float hipThrustHapticHeightStableSeconds;
    private bool UsesHipThrustTeaching => guidanceExercise == PoseGuidanceExercise.HipThrust;
    [SerializeField] private TargetedExercisePoseGuidanceSettings
        targetedExerciseGuidance = new TargetedExercisePoseGuidanceSettings();
    [Tooltip("VMC receiver that drives the user avatar. The scorer resolves it " +
        "from the avatar when this reference is empty.")]
    [SerializeField] private AvatarMotionSource guidanceVmcReceiver;
    [Tooltip("Fail closed when the global VMC stream is unavailable or stale.")]
    [SerializeField] private bool enableVmcFreshnessGate = true;
    [Tooltip("Maximum time without a VMC frame, Humanoid bone packet, or advancing remote time.")]
    [SerializeField, Min(0.01f)] private float vmcFreshnessTimeoutSeconds = 0.25f;
    [HideInInspector]
    [Tooltip("Legacy serialized value retained for scene migration. The new " +
        "guidance path does not use regional scores.")]
    [SerializeField, Range(0f, 100f)] private float guidanceHighlightBelowScore = 70f;
    [HideInInspector]
    [SerializeField, Range(0f, 100f)] private float guidanceClearAtOrAboveScore = 75f;
    [HideInInspector]
    [SerializeField, Min(0.1f)] private float guidanceScoreLeniency = 1.5f;
    [Tooltip("Generic fallback IMU uncertainty, target-axis selection, and " +
        "diagnostic thresholds. Targeted Dead Bug / Bird Dog guidance does " +
        "not use these coach-target settings.")]
    [SerializeField] private PoseHighlightDiagnosticSettings guidanceDiagnostics =
        new PoseHighlightDiagnosticSettings();
    [Tooltip("A mismatch must persist for this long before its surface is highlighted.")]
    [SerializeField, Min(0f)] private float guidanceHighlightConfirmationSeconds = 0.5f;
    [Tooltip("A recovered or untracked region must remain clear for this long " +
        "before its surface highlight is removed.")]
    [SerializeField, Min(0f)] private float guidanceHighlightReleaseSeconds = 0.25f;
    [Tooltip("Every confirmed corrective highlight uses one stable red color.")]
    [SerializeField] private Color guidanceHighlightColor =
        new Color(1f, 0f, 0f, 0.95f);
    [Tooltip("Maximum regions shown together. Targeted Dead Bug / Bird Dog " +
        "scenes use four so every incorrectly positioned limb can be shown; " +
        "generic guidance can retain a lower visual cap.")]
    [SerializeField, Range(1, 5)] private int maximumSimultaneousGuidanceParts = 2;
    [HideInInspector]
    [Tooltip("Legacy serialized value retained for compatibility. Visible " +
        "regions are no longer replaced by severity challengers.")]
    [SerializeField, Min(0f)] private float guidanceDisplaySwitchMargin = 0.2f;

    [Header("UI (Optional - 可以完全不配置)")]
    [Tooltip("可选：用于调试的文字显示")]
    [SerializeField] private Text statusText;
    [SerializeField] private Text scoreText;
    private TMP_Text mobileStatusText;
    private TMP_Text mobileScoreText;
    [SerializeField] private Button nextButton;

    [Header("Difficulty Configuration")]
    [Tooltip("难度配置 - 如果为空则使用下方的默认值")]
    [SerializeField] private DifficultyConfig stabilityDifficultyConfig;
    [SerializeField] private DifficultyConfig scoringDifficultyConfig;

    [Header("Legacy Stability Settings")]
    [Tooltip("Legacy serialized bones retained for scene compatibility; continuous coach observation does not wait for stillness.")]
    [SerializeField] private HumanBodyBones[] stabilityBones = new HumanBodyBones[]
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm
    };
    [Tooltip("Legacy serialized threshold retained for compatibility; it does not gate playback.")]
    [SerializeField] private float stabilityThresholdDegPerSec = 5f;
    [Tooltip("Legacy serialized duration retained for compatibility; it does not gate playback.")]
    [SerializeField] private float stabilityDuration = 2f;
    [Header("Dead Bug Preparation")]
    [Tooltip("The relaxed preparation pose only needs a short continuous confirmation. " +
        "It does not wait for the stricter IMU stillness gate used by formal actions.")]
    [Min(0.1f)] [SerializeField] private float preparationConfirmationDuration = 0.35f;
    [Tooltip("Maximum deviation from horizontal that still counts as lying down.")]
    [Range(0f, 90f)] [SerializeField] private float preparationMaxTorsoTiltFromHorizontal = 45f;
    [Tooltip("Each hand must extend this many torso lengths past its shoulder toward the head.")]
    [Min(0f)] [SerializeField] private float preparationMinHandExtensionTorsoRatio = 0.15f;
    [Tooltip("Maximum bend at each knee that still counts as a straight leg.")]
    [Range(0f, 90f)] [SerializeField] private float preparationMaxKneeBend = 40f;
    [Tooltip("For Bird Dog, hands and knees must drop at least this many torso lengths toward the floor.")]
    [Min(0f)] [SerializeField] private float preparationMinSupportDropTorsoRatio = 0.15f;
    [Tooltip("Legacy arm alignment setting retained for difficulty configuration; surface guidance uses the scorer-derived thresholds above.")]
    [SerializeField] private float stabilityAlignmentThreshold = 35f;
    [Tooltip("Legacy leg alignment setting retained for difficulty configuration; surface guidance uses the scorer-derived thresholds above.")]
    [SerializeField] private float stabilityAlignmentThresholdLegs = 50f;
    [Header("Quality Observation Compatibility")]
    [Tooltip("Legacy serialized value retained for compatibility; no fixed coach scoring window is started.")]
    [SerializeField] private float scoringDuration = 5f;
    [Tooltip("Bones compared for scoring. Rotation weight is higher than position.")]
    [SerializeField] private HumanBodyBones[] scoringBones = new HumanBodyBones[]
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

    [Header("Scoring Weights")]
    [Tooltip("Only rotation error is used for scoring (IMU tracking is more accurate for rotation than position).")]
    [SerializeField] private bool useRotationOnly = true;
    [Tooltip("Max angle difference (degrees) that maps to score=0. Smaller angles get higher scores. (默认值 - 可被难度配置覆盖)")]
    [SerializeField] private float maxAngleError = 30f;
    [Tooltip("Angle difference (degrees) that maps to score=100. Perfect alignment. (默认值 - 可被难度配置覆盖)")]
    [SerializeField] private float perfectAngleThreshold = 5f;

    [Header("Noise-Tolerant Scoring")]
    [SerializeField] private RobustPoseScoringSettings robustScoring = new RobustPoseScoringSettings();
    [Tooltip("Presentation-only remapping applied after main's scorer and robust " +
        "aggregation complete, and only on segments explicitly marked as formal actions.")]
    [SerializeField] private FormalActionScoreMappingSettings formalActionScoreMapping =
        new FormalActionScoreMappingSettings();
    [Tooltip("Compare normalized Humanoid muscle values so the user VMC rig " +
        "and coach animation rig can differ without producing whole-body " +
        "false errors. Root horizontal heading is excluded.")]
    [SerializeField] private bool useHumanoidMuscleSpace = true;
    [Tooltip("Remove fixed avatar and sensor-mounting offsets captured when training starts. " +
        "This is part of main's original robust scoring path.")]
    [SerializeField] private bool useSessionStartCalibration = true;
    [Tooltip("Legacy smoothing interval retained for compatibility; it does not gate playback.")]
    [Min(0.05f)] [SerializeField] private float stabilitySampleInterval = 0.18f;
    [Tooltip("Legacy IMU tolerance retained for compatibility; normal jitter never blocks playback.")]
    [Min(0f)] [SerializeField] private float stabilityNoiseAllowanceDegPerSec = 7f;
    [Tooltip("A small fraction of noisy trackers may move without blocking the user.")]
    [Range(0f, 0.5f)] [SerializeField] private float allowedUnstableBoneFraction = 0.3f;
    [Tooltip("Hard cap for noisy bones allowed by the fraction above.")]
    [Min(0)] [SerializeField] private int maximumUnstableBoneCount = 2;
    [Tooltip("Guidance latch confirmation time; it only affects feedback state, never playback.")]
    [Min(0f)] [SerializeField] private float coarseActionConfirmationSeconds = 0.3f;
    [Tooltip("Guidance latch release time; it only affects feedback state, never playback.")]
    [Min(0f)] [SerializeField] private float coarseActionReleaseSeconds = 0.5f;
    [Tooltip("Legacy tracking grace value retained for compatibility; it does not gate playback.")]
    [Min(0f)] [SerializeField] private float stabilityGraceDuration = 0.45f;
    [Tooltip("Legacy stability decay retained for compatibility; it does not gate playback.")]
    [Min(0f)] [SerializeField] private float stabilityProgressDecayRate = 0.35f;
    [Tooltip("Legacy recovery value retained for compatibility; there is no spoken scoring gate.")]
    [Min(0.1f)] [SerializeField] private float stabilityRecoveryDuration = 0.75f;
    [Tooltip("Shared deadline for preparation, following and pre-score countdown combined. Also used by return/rest and completion controls. A retry never restarts this deadline.")]
    [Min(0.5f)] [SerializeField] private float maximumFollowingWaitSeconds = DefaultProgressionTimeoutSeconds;
    public float ProgressionTimeoutSeconds => Mathf.Max(0.5f, maximumFollowingWaitSeconds);

    // Fired when the whole action is complete, with the continuous quality summary.
    public event Action<List<SegmentScore>> OnAllScored;

    private readonly List<SegmentScore> sessionScores = new List<SegmentScore>();
    private readonly AnimatorMotionEvidence sessionMotionEvidence = new AnimatorMotionEvidence();
    private RobustPoseScoringEngine scoringEngine;
    private PoseHighlightDiagnosticEngine guidanceDiagnosticEngine;
    private PoseFrameScore lastFrameResult;
    private readonly Dictionary<HumanBodyBones, Quaternion> coachCalibrationRotations =
        new Dictionary<HumanBodyBones, Quaternion>();
    private readonly Dictionary<HumanBodyBones, Quaternion> userCalibrationRotations =
        new Dictionary<HumanBodyBones, Quaternion>();
    private bool hasSessionStartCalibration;

    private struct HapticTrackerCandidate
    {
        public readonly TrackerWearLocation location;
        public readonly float severity;

        public HapticTrackerCandidate(
            TrackerWearLocation location,
            float severity)
        {
            this.location = location;
            this.severity = severity;
        }
    }

    private const string CurrentFlowPhaseName = "ContinuousObservation";
    private bool initialFollowInstructionAnnouncedEarly;
    private bool continuousObservationActive;
    private int hapticSetIndex = -1;
    private int hapticRepeatIndex = -1;
    private int continuousValidSamples;
    private int continuousUnavailableSamples;
    private float continuousFeedbackCooldown;
    private HumanBodyBones[] effectiveStabilityBones;

    // Current segment scoring results
    private List<float> frameScores = new List<float>();
    private readonly List<PoseBodyRegion> frameWorstRegions = new List<PoseBodyRegion>();
    private SegmentHoldInfo currentSegmentInfo;

    // Alignment feedback
    private static readonly HumanBodyBones[] GuidanceBones =
    {
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest
    };
    private readonly Dictionary<BodyPart, float> guidancePartErrors =
        new Dictionary<BodyPart, float>
        {
            { BodyPart.LeftArm, 0f },
            { BodyPart.RightArm, 0f },
            { BodyPart.LeftLeg, 0f },
            { BodyPart.RightLeg, 0f },
            { BodyPart.Torso, 0f }
        };
    private readonly Dictionary<BodyPart, float> guidancePartScores =
        new Dictionary<BodyPart, float>
        {
            { BodyPart.LeftArm, 100f },
            { BodyPart.RightArm, 100f },
            { BodyPart.LeftLeg, 100f },
            { BodyPart.RightLeg, 100f },
            { BodyPart.Torso, 100f }
        };
    private readonly Dictionary<BodyPart, float> guidancePartRawScores =
        new Dictionary<BodyPart, float>
        {
            { BodyPart.LeftArm, 100f },
            { BodyPart.RightArm, 100f },
            { BodyPart.LeftLeg, 100f },
            { BodyPart.RightLeg, 100f },
            { BodyPart.Torso, 100f }
        };
    private readonly Dictionary<BodyPart, float> guidancePartThresholds =
        new Dictionary<BodyPart, float>
        {
            { BodyPart.LeftArm, 0f },
            { BodyPart.RightArm, 0f },
            { BodyPart.LeftLeg, 0f },
            { BodyPart.RightLeg, 0f },
            { BodyPart.Torso, 0f }
        };
    private readonly Dictionary<BodyPart, float> guidancePartSeverities =
        new Dictionary<BodyPart, float>
        {
            { BodyPart.LeftArm, 0f },
            { BodyPart.RightArm, 0f },
            { BodyPart.LeftLeg, 0f },
            { BodyPart.RightLeg, 0f },
            { BodyPart.Torso, 0f }
        };
    private readonly Dictionary<BodyPart, bool> guidancePartValidity =
        new Dictionary<BodyPart, bool>
        {
            { BodyPart.LeftArm, false },
            { BodyPart.RightArm, false },
            { BodyPart.LeftLeg, false },
            { BodyPart.RightLeg, false },
            { BodyPart.Torso, false }
        };
    private readonly Dictionary<BodyPart, bool> guidancePartShouldClear =
        new Dictionary<BodyPart, bool>
        {
            { BodyPart.LeftArm, true },
            { BodyPart.RightArm, true },
            { BodyPart.LeftLeg, true },
            { BodyPart.RightLeg, true },
            { BodyPart.Torso, true }
        };
    private static readonly BodyPart[] AllGuidanceParts =
    {
        BodyPart.LeftArm,
        BodyPart.RightArm,
        BodyPart.LeftLeg,
        BodyPart.RightLeg,
        BodyPart.Torso
    };
    private readonly List<BodyPart> guidanceCandidateParts = new List<BodyPart>(5);
    private readonly List<BodyPart> guidancePartsToHighlight = new List<BodyPart>(5);
    private readonly Dictionary<BodyPart, Color> guidancePartColors =
        new Dictionary<BodyPart, Color>();
    private readonly HashSet<BodyPart> confirmedGuidanceParts = new HashSet<BodyPart>();
    private readonly Dictionary<BodyPart, float> guidanceMismatchSeconds =
        new Dictionary<BodyPart, float>();
    private readonly Dictionary<BodyPart, float> guidanceClearSeconds =
        new Dictionary<BodyPart, float>();
    private readonly List<PoseGuidanceRegionEvidence> guidanceEvidence =
        new List<PoseGuidanceRegionEvidence>(5);
    private PoseGuidanceRegionLatch guidanceRegionLatch;
    private CoarseActionReadinessLatch coarseActionReadinessLatch;
    private VmcPoseDataFreshnessMonitor vmcFreshnessMonitor;
    private bool guidanceInputIsFresh = true;
    private bool guidanceInputWasFresh;
    private bool hasLoggedMissingGuidanceReceiver;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private readonly HashSet<BodyPart> lastReportedGuidanceParts = new HashSet<BodyPart>();
    private bool hasReportedGuidanceState;
#endif

    private void Start()
    {
        var mobileControls = FindObjectOfType<MobileCoachControls>();
        if (mobileControls != null)
        {
            mobileStatusText = mobileControls.transform.Find("MobileStatus")?.GetComponent<TMP_Text>();
            mobileScoreText = mobileControls.transform.Find("MobileScore")?.GetComponent<TMP_Text>();
        }
        scoringEngine = new RobustPoseScoringEngine(robustScoring);
        guidanceDiagnosticEngine = new PoseHighlightDiagnosticEngine(
            guidanceDiagnostics);
        EnsureGuidanceState();
        ResolveGuidanceVmcReceiver();
        BuildEffectiveStabilityBones();
        EnsureHapticFeedbackController();
        // 自动查找 VoicePromptManager
        if (voiceManager == null)
        {
            voiceManager = FindObjectOfType<VoicePromptManager>();
        }

        if (coachController == null)
        {
            coachController = FindObjectOfType<SegmentedCoachController>();
        }

        // 自动查找 ScorePopup
        if (scorePopup == null)
        {
            scorePopup = FindObjectOfType<ScorePopup>();
        }

        // 自动查找 BodyPartFeedbackUI
        if (bodyPartFeedback == null)
        {
            bodyPartFeedback = FindObjectOfType<BodyPartFeedbackUI>();
        }

        if (enableModelSurfaceHighlight && userAnimator != null)
        {
            if (modelBodyPartHighlighter == null)
            {
                modelBodyPartHighlighter =
                    userAnimator.GetComponent<AvatarBodyPartHighlighter>();
            }

            if (modelBodyPartHighlighter == null)
            {
                modelBodyPartHighlighter =
                    userAnimator.gameObject.AddComponent<AvatarBodyPartHighlighter>();
            }

            modelBodyPartHighlighter.Configure(userAnimator);
            // Diagnostic severity colors must remain visually stable in the
            // see-through display; pulsing changes perceived color/brightness.
            modelBodyPartHighlighter.SetPulseEnabled(false);
        }

        if (coachController != null)
        {
            coachController.OnAllComplete += HandleAllComplete;
            coachController.OnSegmentHold += HandleObservationHold;
        }
        CoachEventBus.CoachSegmentStarted += HandleSegmentStarted;

        if (nextButton != null)
        {
            nextButton.interactable = false;
        }

        UpdateStatusText("准备中...");
    }

    /// <summary>
    /// 设置稳定性难度配置（由DifficultySelectionUI调用）
    /// </summary>
    public void SetStabilityDifficulty(DifficultyConfig config)
    {
        if (config != null)
        {
            stabilityDifficultyConfig = config;
            Debug.Log($"PoseScorer: 稳定性难度已设置 - 漂移阈值={config.stabilityThresholdDegPerSec}°/s, " +
                      $"对齐阈值={config.stabilityAlignmentThreshold}°, 腿部阈值={config.stabilityAlignmentThresholdLegs}°");
        }
    }

    /// <summary>
    /// 设置评分难度配置（由DifficultySelectionUI调用）
    /// </summary>
    public void SetScoringDifficulty(DifficultyConfig config)
    {
        if (config != null)
        {
            scoringDifficultyConfig = config;
            Debug.Log($"PoseScorer: 评分难度已设置 - 最大误差={config.maxAngleError}°, 完美阈值={config.perfectAngleThreshold}°");
        }
    }

    /// <summary>
    /// 获取当前有效的稳定性漂移阈值
    /// </summary>
    private float GetEffectiveStabilityThreshold()
    {
        return stabilityDifficultyConfig != null
            ? stabilityDifficultyConfig.stabilityThresholdDegPerSec
            : stabilityThresholdDegPerSec;
    }

    /// <summary>
    /// 获取当前有效的稳定性对齐阈值
    /// </summary>
    private float GetEffectiveStabilityAlignment()
    {
        return stabilityDifficultyConfig != null
            ? stabilityDifficultyConfig.stabilityAlignmentThreshold
            : stabilityAlignmentThreshold;
    }

    /// <summary>
    /// 获取当前有效的腿部稳定性对齐阈值
    /// </summary>
    private float GetEffectiveStabilityAlignmentLegs()
    {
        return stabilityDifficultyConfig != null
            ? stabilityDifficultyConfig.stabilityAlignmentThresholdLegs
            : stabilityAlignmentThresholdLegs;
    }

    /// <summary>
    /// 获取当前有效的最大角度误差
    /// </summary>
    private float GetEffectiveMaxAngleError()
    {
        return scoringDifficultyConfig != null
            ? scoringDifficultyConfig.maxAngleError
            : maxAngleError;
    }

    /// <summary>
    /// 获取当前有效的完美角度阈值
    /// </summary>
    private float GetEffectivePerfectAngleThreshold()
    {
        return scoringDifficultyConfig != null
            ? scoringDifficultyConfig.perfectAngleThreshold
            : perfectAngleThreshold;
    }

    private void OnDestroy()
    {
        if (coachController != null)
        {
            coachController.OnAllComplete -= HandleAllComplete;
            coachController.OnSegmentHold -= HandleObservationHold;
        }
        CoachEventBus.CoachSegmentStarted -= HandleSegmentStarted;

        lumbarTeachingEvidence?.Dispose();
        scoringEngine?.Dispose();
        scoringEngine = null;
        guidanceDiagnosticEngine?.Dispose();
        guidanceDiagnosticEngine = null;
        hapticFeedback?.StopAllHaptics();
    }

    private void OnDisable()
    {
        hapticFeedback?.StopAllHaptics();
        ResetVisualGuidanceEvidence();
        modelBodyPartHighlighter?.Hide();
    }

    private void Update()
    {
        if (MobileCoachControls.IsPaused)
        {
            hapticFeedback?.StopAllHaptics();
            ResetLumbarTargetFeedback(false);
            pendingLumbarTargetPulses.Clear();
            return;
        }
        sessionMotionEvidence.Sample();

        if (continuousObservationActive)
        {
            SampleContinuousPose();
            UpdateContinuousObservation();
            UpdateHapticFeedback();

            return;
        }

        UpdateHapticFeedback();


    }

    private void HandleObservationHold(SegmentHoldInfo segment)
    {
        if (!continuousObservationActive || !UsesLumbarTeaching) return;
        // The segment-start pose precedes the movement. Refresh the observation
        // target at the authored endpoint without advancing or delaying playback.
        guidanceDiagnosticEngine?.BeginTarget(coachAnimator, EffectiveGuidanceBones,
            segment.segmentKind, segment.poseGuidanceRule);
    }

    private void HandleSegmentStarted(CoachSegmentEvent segment)
    {
        currentSegmentInfo = new SegmentHoldInfo(
            segment.segmentId,
            segment.repetitionIndex,
            segment.setIndex,
            segment.label,
            1f,
            0f,
            segment.segmentKind,
            FindObjectOfType<SegmentedCoachController>()?.CurrentPoseGuidanceRule ?? SegmentPoseGuidanceRule.Auto);
        ResetLumbarTargetFeedback(false);
        ResetVisualGuidanceEvidence();
        if (guidanceDiagnosticEngine == null)
        {
            guidanceDiagnosticEngine = new PoseHighlightDiagnosticEngine(guidanceDiagnostics);
        }
        if (UsesTargetedExerciseGuidance || UsesHipThrustTeaching)
        {
            guidanceDiagnosticEngine.ResetTarget();
        }
        else
        {
            guidanceDiagnosticEngine.BeginTarget(
                coachAnimator,
                EffectiveGuidanceBones,
                segment.segmentKind,
                SegmentPoseGuidanceRule.Auto);
        }
    }

    private void SampleContinuousPose()
    {
        continuousFeedbackCooldown = Mathf.Max(
            0f,
            continuousFeedbackCooldown - Time.unscaledDeltaTime);
        if (!CanSampleCurrentPose())
        {
            voiceManager?.ClearLessonCorrection();
            continuousUnavailableSamples++;
            return;
        }

        continuousValidSamples++;
        float score = CaptureScoringSample(Time.unscaledDeltaTime);
        if (continuousFeedbackCooldown <= 0f && score < guidanceHighlightBelowScore)
        {
            voiceManager?.PlayAdjustPose();
            continuousFeedbackCooldown = 4f;
        }
    }

    private void UpdateContinuousObservation()
    {
        UpdateGuidanceVmcFreshness(Time.unscaledDeltaTime);
        UpdateStabilityGuidance();
        // Playback no longer waits for a scored checkpoint. Keep the preparation
        // reference as passive observation so lumbar progress can still be measured.
        CaptureTeachingPreparation();
    }

    private void FinalizeContinuousObservation()
    {
        if (!continuousObservationActive ||
            (continuousValidSamples == 0 && continuousUnavailableSamples == 0))
        {
            return;
        }

        if (continuousValidSamples == 0)
        {
            sessionScores.Add(new SegmentScore
            {
                segmentIndex = currentSegmentInfo.segmentIndex,
                repeatIndex = currentSegmentInfo.repeatIndex,
                setIndex = currentSegmentInfo.setIndex,
                segmentKind = currentSegmentInfo.segmentKind,
                label = "连续质量观察",
                trackingQuality = CoachTrackingQuality.Unavailable,
                frameCount = 0
            });
            return;
        }

        float average = RobustPoseScoringEngine.CalculateRobustAverage(
            frameScores,
            robustScoring != null ? robustScoring.finalScoreTrimFraction : 0.1f);
        float best = 0f;
        foreach (float score in frameScores) best = Mathf.Max(best, score);
        sessionScores.Add(new SegmentScore
        {
            segmentIndex = currentSegmentInfo.segmentIndex,
            repeatIndex = currentSegmentInfo.repeatIndex,
            setIndex = currentSegmentInfo.setIndex,
            segmentKind = currentSegmentInfo.segmentKind,
            label = "连续质量观察",
            averageScore = average,
            bestScore = best,
            rawAverageScore = average,
            rawBestScore = best,
            frameCount = continuousValidSamples,
            trackingQuality = continuousUnavailableSamples > 0
                ? CoachTrackingQuality.Partial
                : CoachTrackingQuality.Measured
        });
    }

    private void HandleAllComplete()
    {
        FinalizeContinuousObservation();
        continuousObservationActive = false;
        hapticFeedback?.StopAllHaptics();
        sessionMotionEvidence.Stop();
        // The final transition can be shorter than the queued completion cue.
        // Let VoicePromptManager finish naturally; the completion controls stay
        // hidden until it is idle.
        ResetVisualGuidanceEvidence();
        modelBodyPartHighlighter?.Hide();

        Debug.Log($"PoseScorer: 连续质量观察结束，样本={continuousValidSamples}，不可用={continuousUnavailableSamples}");
        UpdateStatusText($"训练完成，完成 {CompletedRepetitions} 次练习流程");
        OnAllScored?.Invoke(sessionScores);

        if (nextButton != null)
        {
            nextButton.interactable = false;
        }
    }

    private void BuildEffectiveStabilityBones()
    {
        var bones = new List<HumanBodyBones>();
        var seen = new HashSet<HumanBodyBones>();

        if (stabilityBones != null)
        {
            foreach (HumanBodyBones bone in stabilityBones)
            {
                if (seen.Add(bone))
                {
                    bones.Add(bone);
                }
            }
        }

        // Include all guided limbs in motion tracking. These bones only answer
        // whether the user has finished moving; coarse action readiness is
        // evaluated separately from profile-driven region evidence.
        foreach (HumanBodyBones bone in EffectiveGuidanceBones)
        {
            if (seen.Add(bone))
            {
                bones.Add(bone);
            }
        }

        effectiveStabilityBones = bones.ToArray();
    }



    private bool CanSampleCurrentPose()
    {
        if (userAnimator == null || coachAnimator == null) return false;
        guidanceInputIsFresh = !Application.isPlaying ||
            UpdateGuidanceVmcFreshness(Time.unscaledDeltaTime);
        return guidanceInputIsFresh && !HasHardPoseConstraint();
    }

















    private float CaptureScoringSample(float sampleDeltaTime)
    {
        float score = ScoreCurrentPose(sampleDeltaTime);
        // Final statistics intentionally use the exact score shown in the UI.
        frameScores.Add(score);
        frameWorstRegions.Add(lastFrameResult.worstRegion);
        return score;
    }





    private bool IsCurrentUserInRelaxedPreparationPose()
    {
        if (UsesShortTeaching) return IsTeachingPoseReady();
        if (coachController != null &&
            coachController.UsesDeadBugPreparationPose)
        {
            if (!TryCapturePreparationPose(
                    out DeadBugPreparationPoseGeometry deadBugPose))
            {
                return false;
            }

            return MeetsRelaxedDeadBugPreparationPose(
                deadBugPose,
                preparationMaxTorsoTiltFromHorizontal,
                preparationMinHandExtensionTorsoRatio,
                preparationMaxKneeBend);
        }

        if (!TryCapturePreparationPose(
                out DeadBugPreparationPoseGeometry birdDogPose))
        {
            return false;
        }

        return MeetsRelaxedBirdDogPreparationPose(
            birdDogPose,
            preparationMaxTorsoTiltFromHorizontal,
            preparationMinSupportDropTorsoRatio);
    }

    private bool TryCapturePreparationPose(
        out DeadBugPreparationPoseGeometry pose)
    {
        return TryCapturePreparationPose(
            userAnimator,
            Vector3.up,
            out pose);
    }

    public static bool TryCapturePreparationPose(
        Animator animator,
        Vector3 worldUp,
        out DeadBugPreparationPoseGeometry pose)
    {
        pose = new DeadBugPreparationPoseGeometry();
        if (animator == null)
        {
            return false;
        }

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform upperTorso =
            animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
            animator.GetBoneTransform(HumanBodyBones.Chest) ??
            animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform leftShoulder =
            animator.GetBoneTransform(HumanBodyBones.LeftShoulder) ??
            animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder =
            animator.GetBoneTransform(HumanBodyBones.RightShoulder) ??
            animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform leftKnee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform rightKnee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

        if (hips == null || upperTorso == null ||
            leftShoulder == null || leftHand == null ||
            rightShoulder == null || rightHand == null ||
            leftUpperLeg == null || leftKnee == null ||
            rightUpperLeg == null || rightKnee == null)
        {
            return false;
        }

        pose = new DeadBugPreparationPoseGeometry
        {
            hips = hips.position,
            upperTorso = upperTorso.position,
            leftShoulder = leftShoulder.position,
            leftHand = leftHand.position,
            rightShoulder = rightShoulder.position,
            rightHand = rightHand.position,
            leftUpperLeg = leftUpperLeg.position,
            leftKnee = leftKnee.position,
            leftFoot = leftFoot != null
                ? leftFoot.position
                : leftKnee.position,
            rightUpperLeg = rightUpperLeg.position,
            rightKnee = rightKnee.position,
            rightFoot = rightFoot != null
                ? rightFoot.position
                : rightKnee.position,
            worldUp = worldUp.sqrMagnitude > 1e-6f
                ? worldUp.normalized
                : Vector3.up
        };
        return true;
    }

    public static bool MeetsRelaxedDeadBugDirectionLockPose(
        DeadBugPreparationPoseGeometry pose,
        float maxTorsoTiltFromHorizontal,
        float minimumHandExtensionTorsoRatio)
    {
        Vector3 torso = pose.upperTorso - pose.hips;
        Vector3 up = pose.worldUp;
        if (torso.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        float torsoLength = torso.magnitude;
        Vector3 headward = torso / torsoLength;
        float verticalShare = Mathf.Abs(
            Vector3.Dot(headward, up.normalized));
        float maximumVerticalShare = Mathf.Sin(
            Mathf.Clamp(maxTorsoTiltFromHorizontal, 0f, 90f) *
            Mathf.Deg2Rad);
        if (verticalShare > maximumVerticalShare)
        {
            return false;
        }

        float minimumHandExtension =
            Mathf.Max(0f, minimumHandExtensionTorsoRatio) * torsoLength;
        return Vector3.Dot(
                   pose.leftHand - pose.leftShoulder,
                   headward) >= minimumHandExtension &&
               Vector3.Dot(
                   pose.rightHand - pose.rightShoulder,
                   headward) >= minimumHandExtension;
    }

    public static bool MeetsRelaxedDeadBugPreparationPose(
        DeadBugPreparationPoseGeometry pose,
        float maxTorsoTiltFromHorizontal,
        float minimumHandExtensionTorsoRatio,
        float maximumKneeBend)
    {
        bool bothLegsStraight =
            IsLegStraight(
                pose.leftUpperLeg,
                pose.leftKnee,
                pose.leftFoot,
                maximumKneeBend) &&
            IsLegStraight(
                pose.rightUpperLeg,
                pose.rightKnee,
                pose.rightFoot,
                maximumKneeBend);

        return MeetsRelaxedDeadBugDirectionLockPose(
                   pose,
                   maxTorsoTiltFromHorizontal,
                   minimumHandExtensionTorsoRatio) &&
               bothLegsStraight;
    }

    public static bool MeetsRelaxedBirdDogPreparationPose(
        DeadBugPreparationPoseGeometry pose,
        float maxTorsoTiltFromHorizontal,
        float minimumSupportDropTorsoRatio)
    {
        Vector3 torso = pose.upperTorso - pose.hips;
        Vector3 up = pose.worldUp;
        if (torso.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        float torsoLength = torso.magnitude;
        Vector3 normalizedUp = up.normalized;
        float verticalShare = Mathf.Abs(
            Vector3.Dot(torso / torsoLength, normalizedUp));
        float maximumVerticalShare = Mathf.Sin(
            Mathf.Clamp(maxTorsoTiltFromHorizontal, 0f, 90f) *
            Mathf.Deg2Rad);
        bool torsoIsHorizontal = verticalShare <= maximumVerticalShare;

        float minimumSupportDrop =
            Mathf.Max(0f, minimumSupportDropTorsoRatio) * torsoLength;
        Vector3 floorward = -normalizedUp;
        bool bothHandsSupport =
            Vector3.Dot(pose.leftHand - pose.leftShoulder, floorward) >=
                minimumSupportDrop &&
            Vector3.Dot(pose.rightHand - pose.rightShoulder, floorward) >=
                minimumSupportDrop;
        bool bothKneesSupport =
            Vector3.Dot(pose.leftKnee - pose.leftUpperLeg, floorward) >=
                minimumSupportDrop &&
            Vector3.Dot(pose.rightKnee - pose.rightUpperLeg, floorward) >=
                minimumSupportDrop;

        return torsoIsHorizontal && bothHandsSupport && bothKneesSupport;
    }



    private static bool IsLegStraight(
        Vector3 upperLeg,
        Vector3 knee,
        Vector3 foot,
        float maximumKneeBend)
    {
        Vector3 upperSection = knee - upperLeg;
        Vector3 lowerSection = foot - knee;
        if (upperSection.sqrMagnitude < 1e-6f || lowerSection.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        return Vector3.Angle(upperSection, lowerSection) <=
            Mathf.Clamp(maximumKneeBend, 0f, 90f);
    }



    public static int CalculateAllowedUnstableBoneCount(
        int checkedBoneCount,
        float allowedFraction,
        int maximumCount)
    {
        int checkedCount = Mathf.Max(0, checkedBoneCount);
        int proportionalAllowance = Mathf.FloorToInt(
            checkedCount * Mathf.Clamp01(allowedFraction));
        return Mathf.Min(
            proportionalAllowance,
            Mathf.Max(0, maximumCount));
    }

    // Compares user's current pose to coach's frozen target pose. Returns a score 0-100.
    private float ScoreCurrentPose(float sampleDeltaTime)
    {
        if (userAnimator == null || coachAnimator == null)
        {
            return 0f;
        }

        // Per-segment leniency widens the tolerated error (e.g. transition poses).
        float leniency = Mathf.Max(0.1f, currentSegmentInfo.scoreLeniency);
        float effectiveMaxAngle = GetEffectiveMaxAngleError();
        float effectivePerfectAngle = GetEffectivePerfectAngleThreshold();
        if (scoringEngine == null)
        {
            scoringEngine = new RobustPoseScoringEngine(robustScoring);
        }

        lastFrameResult = scoringEngine.Evaluate(
            userAnimator,
            coachAnimator,
            scoringBones,
            effectivePerfectAngle,
            effectiveMaxAngle,
            leniency,
            userBaselines: useSessionStartCalibration && hasSessionStartCalibration
                ? userCalibrationRotations
                : null,
            coachBaselines: useSessionStartCalibration && hasSessionStartCalibration
                ? coachCalibrationRotations
                : null,
            deltaTime: Mathf.Max(0.0001f, sampleDeltaTime),
            ignoreHipsHeading: true,
            useHumanoidMuscleSpace: useHumanoidMuscleSpace);
        return ApplyFormalActionScoreMapping(
            lastFrameResult.score,
            currentSegmentInfo.segmentKind,
            formalActionScoreMapping);
    }

    public static float ApplyFormalActionScoreMapping(
        float rawScore,
        TrainingSegmentKind segmentKind,
        FormalActionScoreMappingSettings mapping)
    {
        return segmentKind == TrainingSegmentKind.CoreAction &&
               mapping != null
            ? mapping.Map(rawScore)
            : Mathf.Clamp(rawScore, 0f, 100f);
    }

    private void UpdateStatusText(string message)
    {
        if (mobileStatusText != null) mobileStatusText.text = message;
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void UpdateScoreText(string message)
    {
        // Coach follow mode deliberately has no live score or segment result UI.
    }

    // Public API for debugging / manual control
    public float GetCurrentScore()
    {
        if (frameScores.Count == 0)
        {
            return 0f;
        }

        return frameScores[frameScores.Count - 1];
    }

    public float GetAverageScore()
    {
        if (frameScores.Count == 0)
        {
            return 0f;
        }

        float sum = 0f;
        foreach (float s in frameScores)
        {
            sum += s;
        }
        return sum / frameScores.Count;
    }

    // All segment scores collected so far this session (read-only view).
    public IReadOnlyList<SegmentScore> SessionScores => sessionScores;
    public PoseFrameScore LastFrameScore => lastFrameResult;
    public float MaximumUserMotionDegrees => sessionMotionEvidence.MaximumUserMotionDegrees;
    public float MaximumCoachMotionDegrees => sessionMotionEvidence.MaximumCoachMotionDegrees;
    public string CurrentFlowPhase => CurrentFlowPhaseName;
    // One completed core/return pair is one practice attempt. Pose accuracy
    // belongs in its score and must not silently discard the repetition.
    public int CompletedRepetitions => coachController != null
        ? coachController.CompletedRepetitions : 0;
    public float StabilityRemainingSeconds => 0f;
    public bool CoarseActionReady => IsCoarseActionReady();

    public void BeginTrainingMeasurement()
    {
        if (effectiveStabilityBones == null || effectiveStabilityBones.Length == 0)
        {
            BuildEffectiveStabilityBones();
        }

        voiceManager?.StopAll();
        scorePopup?.HideImmediately();
        if (mobileScoreText != null) mobileScoreText.text = string.Empty;
        if (scoreText != null) scoreText.text = string.Empty;
        initialFollowInstructionAnnouncedEarly = false;
        sessionScores.Clear();
        continuousValidSamples = 0;
        continuousUnavailableSamples = 0;
        continuousFeedbackCooldown = 0f;
        continuousObservationActive = true;
        hapticSetIndex = -1;
        hapticRepeatIndex = -1;
        hapticFeedback?.ResetGuidancePulse();
        lumbarTeachingEvidence?.Reset();
        hasHipThrustRestPose = false;
        CaptureSessionStartCalibration();
        scoringEngine?.Reset();
        if (guidanceDiagnosticEngine == null)
        {
            guidanceDiagnosticEngine = new PoseHighlightDiagnosticEngine(
                guidanceDiagnostics);
        }
        if (UsesLumbarTeaching)
        {
            guidanceDiagnosticEngine.Dispose();
            guidanceDiagnosticEngine = new PoseHighlightDiagnosticEngine(guidanceDiagnostics);
        }
        else if (UsesTargetedExerciseGuidance || UsesHipThrustTeaching)
        {
            guidanceDiagnosticEngine.ResetTarget();
        }
        else
        {
            guidanceDiagnosticEngine.CaptureSessionReference(
                userAnimator,
                coachAnimator,
                EffectiveGuidanceBones);
        }
        sessionMotionEvidence.Begin(coachAnimator, userAnimator, scoringBones);
    }

    /// <summary>
    /// Tells the user to follow as soon as the first coach demonstration begins.
    /// The first hold checkpoint then suppresses its usual duplicate announcement.
    /// </summary>
    public void AnnounceInitialFollowInstruction()
    {
        initialFollowInstructionAnnouncedEarly =
            voiceManager != null && voiceManager.PlayFollowCoach();
    }

    public void StopTrainingMeasurement()
    {
        FinalizeContinuousObservation();
        continuousObservationActive = false;
        sessionMotionEvidence.Stop();
        voiceManager?.StopAll();
        hapticFeedback?.StopAllHaptics();
        scorePopup?.HideImmediately();
        initialFollowInstructionAnnouncedEarly = false;
        frameScores.Clear();
        frameWorstRegions.Clear();
        ResetVisualGuidanceEvidence();
        bodyPartFeedback?.Hide();
        modelBodyPartHighlighter?.Hide();
        scoringEngine?.Reset();
        guidanceDiagnosticEngine?.ResetTarget();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Starts a deterministic hold checkpoint for the local VMC simulator.
    /// This bypasses the coach clip lead-in but keeps the production
    /// stabilization, guidance, countdown, and scoring state machine intact.
    /// </summary>
    public void BeginEditorGuidanceCheckpoint()
    {
        if (coachAnimator == null || userAnimator == null)
        {
            Debug.LogWarning("PoseScorer: cannot start editor guidance checkpoint without both animators.");
            return;
        }

        coachAnimator.speed = 0f;
        CaptureSessionStartCalibration();
        if (guidanceDiagnosticEngine == null)
        {
            guidanceDiagnosticEngine = new PoseHighlightDiagnosticEngine(
                guidanceDiagnostics);
        }
        guidanceDiagnosticEngine.CaptureSessionReference(
            userAnimator,
            coachAnimator,
            GuidanceBones);
        HandleSegmentStarted(new CoachSegmentEvent(
            0,
            0,
            0,
            TrainingSegmentKind.CoreAction,
            "Editor guidance probe"));
        Debug.Log("PoseScorer: editor guidance checkpoint started.");
    }
#endif



    private void CaptureSessionStartCalibration()
    {
        coachCalibrationRotations.Clear();
        userCalibrationRotations.Clear();
        hasSessionStartCalibration = false;
        if (!useSessionStartCalibration || coachAnimator == null || userAnimator == null)
        {
            return;
        }

        foreach (HumanBodyBones bone in scoringBones)
        {
            Transform coachBone = coachAnimator.GetBoneTransform(bone);
            Transform userBone = userAnimator.GetBoneTransform(bone);
            if (coachBone == null || userBone == null)
            {
                continue;
            }

            coachCalibrationRotations[bone] = coachBone.localRotation;
            userCalibrationRotations[bone] = userBone.localRotation;
        }

        hasSessionStartCalibration = coachCalibrationRotations.Count > 0;
        Debug.Log($"PoseScorer: captured neutral calibration for {coachCalibrationRotations.Count} bones.");
    }

    private void UpdateStabilityGuidance()
    {
        guidanceInputIsFresh = !Application.isPlaying ||
            UpdateGuidanceVmcFreshness(Time.unscaledDeltaTime);
        ShowMisalignedBodyParts();
        UpdateCoarseActionReadiness(Time.deltaTime);
    }

    private void UpdateHipThrustTargetHaptic()
    {
        if (!UsesHipThrustTeaching ||
            currentSegmentInfo.segmentKind != TrainingSegmentKind.CoreAction ||
            !guidanceInputIsFresh ||
            !HipThrustPoseGuidance.TryCapture(
                userAnimator,
                out HipThrustPoseGeometry userPose) ||
            !HipThrustPoseGuidance.TryCapture(
                coachAnimator,
                out HipThrustPoseGeometry coachPose))
        {
            hipThrustHapticHeightCandidateActive = false;
            hipThrustHapticHeightStableSeconds = 0f;
            return;
        }

        if (hipThrustOptimalHeightPulseSent)
        {
            return;
        }

        float heightError = Mathf.Abs(
            userPose.hipAboveSupportLine - coachPose.hipAboveSupportLine);
        float enterTolerance = Mathf.Max(0f, hipThrustHapticHeightTolerance);
        float exitTolerance = enterTolerance + 0.03f;
        if (!hipThrustHapticHeightCandidateActive)
        {
            if (heightError > enterTolerance)
            {
                hipThrustHapticHeightStableSeconds = 0f;
                return;
            }

            hipThrustHapticHeightCandidateActive = true;
        }
        else if (heightError > exitTolerance)
        {
            hipThrustHapticHeightCandidateActive = false;
            hipThrustHapticHeightStableSeconds = 0f;
            return;
        }

        hipThrustHapticHeightStableSeconds += Mathf.Clamp(
            Time.deltaTime,
            0f,
            0.1f);
        if (hipThrustHapticHeightStableSeconds <
            Mathf.Max(0f, hipThrustHapticConfirmationSeconds))
        {
            return;
        }

        hipThrustOptimalHeightPulseSent = true;
        hipThrustOptimalHeightPulsePending = true;
    }

    private void EnsureHapticFeedbackController()
    {
        if (hapticFeedback == null)
        {
            hapticFeedback = FindObjectOfType<CoachHapticFeedbackController>(true);
        }

        if (hapticFeedback == null)
        {
            hapticFeedback = gameObject.AddComponent<CoachHapticFeedbackController>();
        }
    }

    private void UpdateHapticFeedback()
    {
        EnsureHapticFeedbackController();
        if (continuousObservationActive && currentSegmentInfo.segmentKind == TrainingSegmentKind.CoreAction &&
            (hapticSetIndex != currentSegmentInfo.setIndex || hapticRepeatIndex != currentSegmentInfo.repeatIndex))
        {
            hapticSetIndex = currentSegmentInfo.setIndex;
            hapticRepeatIndex = currentSegmentInfo.repeatIndex;
            hapticFeedback.ResetGuidancePulse();
        }
        BuildHapticLocationOverrides();
        bool guidancePhaseEnabled = continuousObservationActive;
        bool guidanceInputFresh = guidanceInputIsFresh;
        hapticFeedback.TickSelectedLocations(
            guidancePhaseEnabled,
            guidanceInputFresh,
            hapticLocationOverrides,
            MobileCoachControls.IsPaused);
    }

    private void DispatchPendingHipThrustOptimalHeightPulse()
    {
        if (!hipThrustOptimalHeightPulsePending)
        {
            return;
        }

        hipThrustOptimalHeightPulsePending = false;
        hapticFeedback.PulseOnce(TrackerWearLocation.Abdomen);
    }

    private void BuildHapticLocationOverrides()
    {
        hapticLocationOverrides.Clear();
        if (!continuousObservationActive || !guidanceInputIsFresh || HasHardPoseConstraint() ||
            currentSegmentInfo.segmentKind != TrainingSegmentKind.CoreAction)
            return;

        bool hasTarget = false;
        foreach (BodyPart part in AllGuidanceParts)
        {
            if (currentSegmentInfo.poseGuidanceRule.GetRole(part) != PoseGuidanceRegionRole.Active)
                continue;
            hasTarget = true;
            if (!guidancePartsToHighlight.Contains(part))
            {
                hapticLocationOverrides.Clear();
                return;
            }
            hapticLocationOverrides.Add(GetDefaultHapticLocation(part));
        }
        if (!hasTarget) hapticLocationOverrides.Clear();
    }

    private void AddHapticTrackerCandidate(
        TrackerWearLocation location,
        float severity)
    {
        hapticTrackerCandidates.Add(new HapticTrackerCandidate(location, severity));
    }

    private void SelectHapticGuidanceParts()
    {
        selectedHapticParts.Clear();
        if (UsesTargetedExerciseGuidance)
        {
            bool hasLeftArmRightLegPair =
                hapticCandidateParts.Contains(BodyPart.LeftArm) &&
                hapticCandidateParts.Contains(BodyPart.RightLeg);
            bool hasRightArmLeftLegPair =
                hapticCandidateParts.Contains(BodyPart.RightArm) &&
                hapticCandidateParts.Contains(BodyPart.LeftLeg);

            if (hasLeftArmRightLegPair || hasRightArmLeftLegPair)
            {
                bool chooseLeftArmRightLeg = hasLeftArmRightLegPair &&
                    (!hasRightArmLeftLegPair ||
                     GetGuidancePartSeverity(BodyPart.LeftArm) +
                     GetGuidancePartSeverity(BodyPart.RightLeg) >=
                     GetGuidancePartSeverity(BodyPart.RightArm) +
                     GetGuidancePartSeverity(BodyPart.LeftLeg));
                AddHapticPair(
                    chooseLeftArmRightLeg ? BodyPart.LeftArm : BodyPart.RightArm,
                    chooseLeftArmRightLeg ? BodyPart.RightLeg : BodyPart.LeftLeg);
                return;
            }
        }

        selectedHapticParts.AddRange(hapticCandidateParts);
    }

    private void AddHapticPair(BodyPart first, BodyPart second)
    {
        if (GetGuidancePartSeverity(first) >= GetGuidancePartSeverity(second))
        {
            selectedHapticParts.Add(first);
            selectedHapticParts.Add(second);
        }
        else
        {
            selectedHapticParts.Add(second);
            selectedHapticParts.Add(first);
        }
    }

    private float GetGuidancePartSeverity(BodyPart part)
    {
        return CurrentGuidancePartSeverities.TryGetValue(part, out float severity)
            ? severity
            : 0f;
    }

    private static TrackerWearLocation GetDefaultHapticLocation(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.LeftArm:
                return TrackerWearLocation.LeftUpperArm;
            case BodyPart.RightArm:
                return TrackerWearLocation.RightUpperArm;
            case BodyPart.LeftLeg:
                return TrackerWearLocation.LeftThigh;
            case BodyPart.RightLeg:
                return TrackerWearLocation.RightThigh;
            case BodyPart.Torso:
                return TrackerWearLocation.Abdomen;
            default:
                return default;
        }
    }

    private bool TryGetTargetedLimbHapticLocations(
        BodyPart part,
        out TrackerWearLocation proximal,
        out TrackerWearLocation distal,
        out bool useProximal,
        out bool useDistal,
        out float proximalSeverity,
        out float distalSeverity)
    {
        proximal = default;
        distal = default;
        useProximal = false;
        useDistal = false;
        proximalSeverity = 0f;
        distalSeverity = 0f;

        if (!TryGetTargetedLimbEvidence(
                part,
                out Vector3 direction,
                out bool directionTracked,
                out float bendDegrees,
                out bool bendTracked,
                out bool expectedActive,
                out proximal,
                out distal))
        {
            return false;
        }

        // A Bird Dog support failure may not identify one anatomical segment;
        // the group selector resolves the tied locations under its two-motor cap.
        if (guidanceExercise == PoseGuidanceExercise.BirdDog && !expectedActive)
        {
            useProximal = true;
            useDistal = true;
            proximalSeverity = GetGuidancePartSeverity(part);
            distalSeverity = proximalSeverity;
            return true;
        }

        proximalSeverity = CalculateProximalHapticSeverity(
            direction,
            directionTracked,
            expectedActive);
        distalSeverity = guidanceExercise == PoseGuidanceExercise.BirdDog &&
            expectedActive && bendTracked
            ? NormalizeAboveThreshold(
                bendDegrees,
                targetedExerciseGuidance.birdDogBendClearAtOrBelowDegrees,
                targetedExerciseGuidance.birdDogBendActivationAboveDegrees)
            : 0f;

        const float dominanceMargin = 0.15f;
        if (proximalSeverity >= distalSeverity + dominanceMargin)
        {
            useProximal = true;
        }
        else if (distalSeverity >= proximalSeverity + dominanceMargin)
        {
            useDistal = true;
        }
        else
        {
            // Do not guess when the visual region is confirmed but the source
            // is mixed or temporarily unavailable.
            useProximal = true;
            useDistal = true;
        }

        return true;
    }

    private bool TryGetTargetedLimbEvidence(
        BodyPart part,
        out Vector3 direction,
        out bool directionTracked,
        out float bendDegrees,
        out bool bendTracked,
        out bool expectedActive,
        out TrackerWearLocation proximal,
        out TrackerWearLocation distal)
    {
        direction = Vector3.zero;
        directionTracked = false;
        bendDegrees = 0f;
        bendTracked = false;
        expectedActive = IsExpectedActiveTargetedLimb(part);
        proximal = default;
        distal = default;

        bool birdDog = guidanceExercise == PoseGuidanceExercise.BirdDog;
        switch (part)
        {
            case BodyPart.LeftArm:
                direction = birdDog ? latestTargetedGuidanceGeometry.leftArmLineDirection : latestTargetedGuidanceGeometry.leftArmDirection;
                directionTracked = birdDog ? latestTargetedGuidanceGeometry.leftArmLineTracked : latestTargetedGuidanceGeometry.leftArmTracked;
                bendDegrees = latestTargetedGuidanceGeometry.leftArmBendDegrees;
                bendTracked = latestTargetedGuidanceGeometry.leftArmBendTracked;
                proximal = TrackerWearLocation.LeftUpperArm;
                distal = TrackerWearLocation.LeftForearm;
                return true;
            case BodyPart.RightArm:
                direction = birdDog ? latestTargetedGuidanceGeometry.rightArmLineDirection : latestTargetedGuidanceGeometry.rightArmDirection;
                directionTracked = birdDog ? latestTargetedGuidanceGeometry.rightArmLineTracked : latestTargetedGuidanceGeometry.rightArmTracked;
                bendDegrees = latestTargetedGuidanceGeometry.rightArmBendDegrees;
                bendTracked = latestTargetedGuidanceGeometry.rightArmBendTracked;
                proximal = TrackerWearLocation.RightUpperArm;
                distal = TrackerWearLocation.RightForearm;
                return true;
            case BodyPart.LeftLeg:
                direction = latestTargetedGuidanceGeometry.leftLegDirection;
                directionTracked = latestTargetedGuidanceGeometry.leftLegTracked;
                bendDegrees = latestTargetedGuidanceGeometry.leftLegBendDegrees;
                bendTracked = latestTargetedGuidanceGeometry.leftLegBendTracked;
                proximal = TrackerWearLocation.LeftThigh;
                distal = TrackerWearLocation.LeftLowerLeg;
                return true;
            case BodyPart.RightLeg:
                direction = latestTargetedGuidanceGeometry.rightLegDirection;
                directionTracked = latestTargetedGuidanceGeometry.rightLegTracked;
                bendDegrees = latestTargetedGuidanceGeometry.rightLegBendDegrees;
                bendTracked = latestTargetedGuidanceGeometry.rightLegBendTracked;
                proximal = TrackerWearLocation.RightThigh;
                distal = TrackerWearLocation.RightLowerLeg;
                return true;
            default:
                return false;
        }
    }

    private bool IsExpectedActiveTargetedLimb(BodyPart part)
    {
        if (currentSegmentInfo.segmentKind != TrainingSegmentKind.CoreAction ||
            currentSegmentInfo.poseGuidanceRule.mode != PoseGuidanceProfileMode.Explicit)
        {
            return false;
        }

        PoseGuidanceRegionMask mask = part == BodyPart.LeftArm
            ? PoseGuidanceRegionMask.LeftArm
            : part == BodyPart.RightArm
                ? PoseGuidanceRegionMask.RightArm
                : part == BodyPart.LeftLeg
                    ? PoseGuidanceRegionMask.LeftLeg
                    : part == BodyPart.RightLeg
                        ? PoseGuidanceRegionMask.RightLeg
                        : PoseGuidanceRegionMask.None;
        return (currentSegmentInfo.poseGuidanceRule.activeRegions & mask) != 0;
    }

    private float CalculateProximalHapticSeverity(
        Vector3 direction,
        bool directionTracked,
        bool expectedActive)
    {
        if (!directionTracked ||
            !TargetedExercisePoseGuidance.TryCalculateLiftDegrees(
                guidanceExercise,
                latestTargetedGuidanceGeometry.bodyPlaneNormal,
                direction,
                out float liftDegrees))
        {
            return 0f;
        }

        if (guidanceExercise == PoseGuidanceExercise.BirdDog && expectedActive)
        {
            return NormalizeBelowThreshold(
                liftDegrees,
                targetedExerciseGuidance.birdDogActiveLiftClearAtOrAboveDegrees,
                targetedExerciseGuidance.birdDogActiveLiftActivationBelowDegrees);
        }

        return expectedActive
            ? NormalizeBelowThreshold(
                liftDegrees,
                targetedExerciseGuidance.requiredLiftClearAtOrAboveDegrees,
                targetedExerciseGuidance.requiredLiftActivationBelowDegrees)
            : NormalizeAboveThreshold(
                liftDegrees,
                targetedExerciseGuidance.unexpectedLiftClearAtOrBelowDegrees,
                targetedExerciseGuidance.unexpectedLiftActivationAboveDegrees);
    }

    private static float NormalizeBelowThreshold(
        float value,
        float clearAtOrAbove,
        float activateBelow)
    {
        return Mathf.Clamp01((clearAtOrAbove - value) /
            Mathf.Max(1f, clearAtOrAbove - activateBelow));
    }

    private static float NormalizeAboveThreshold(
        float value,
        float clearAtOrBelow,
        float activateAbove)
    {
        return Mathf.Clamp01((value - clearAtOrBelow) /
            Mathf.Max(1f, activateAbove - clearAtOrBelow));
    }

    private bool RequiresCoarseActionReadiness =>
        currentSegmentInfo.segmentKind == TrainingSegmentKind.CoreAction &&
        currentSegmentInfo.poseGuidanceRule.mode ==
            PoseGuidanceProfileMode.Explicit;

    private bool IsCoarseActionReady()
    {
        if (UsesShortTeaching) return IsTeachingPoseReady();
        EnsureGuidanceState();
        return !RequiresCoarseActionReadiness ||
            coarseActionReadinessLatch.IsReady;
    }

    private void UpdateCoarseActionReadiness(float deltaTime)
    {
        EnsureGuidanceState();
        coarseActionReadinessLatch.Configure(
            coarseActionConfirmationSeconds,
            coarseActionReleaseSeconds);
        if (!RequiresCoarseActionReadiness)
        {
            coarseActionReadinessLatch.Reset();
            return;
        }

        coarseActionReadinessLatch.Update(
            currentSegmentInfo.poseGuidanceRule,
            guidanceEvidence,
            deltaTime,
            guidanceInputIsFresh);
    }

    /// <summary>
    /// Evaluates either targeted Dead Bug / Bird Dog body-plane geometry or
    /// the generic target-salient Humanoid comparer. Formal scores and score
    /// mappings are intentionally absent from both corrective paths.
    /// </summary>
    private void ShowMisalignedBodyParts()
    {
        if (userAnimator == null ||
            (!UsesTargetedExerciseGuidance && coachAnimator == null))
        {
            guidancePartsToHighlight.Clear();
            bodyPartFeedback?.Hide();
            modelBodyPartHighlighter?.Hide();
            return;
        }

        if (modelBodyPartHighlighter != null &&
            modelBodyPartHighlighter.TargetAnimator != userAnimator)
        {
            modelBodyPartHighlighter.Configure(userAnimator);
            ResetGuidanceLatchState();
            ResolveGuidanceVmcReceiver();
        }

        PoseHighlightFrameDiagnostics diagnostics;
        if (RecordedAlternatingCoachMode.IsRecordedScene(gameObject.scene.name))
        {
            hasLatestTargetedGuidanceGeometry = false;
            var recorded = GetComponent<RecordedPracticePoseGuidance>();
            diagnostics = recorded != null
                ? recorded.Evaluate(userAnimator, currentSegmentInfo.segmentKind,
                    currentSegmentInfo.poseGuidanceRule, guidanceDiagnostics)
                : default;
        }
        else if (UsesHipThrustTeaching)
        {
            hasLatestTargetedGuidanceGeometry = false;
            diagnostics = currentSegmentInfo.segmentKind == TrainingSegmentKind.CoreAction &&
                HipThrustPoseGuidance.TryCapture(userAnimator,
                    out HipThrustPoseGeometry pose) &&
                HipThrustPoseGuidance.TryCapture(coachAnimator,
                    out HipThrustPoseGeometry target)
                ? HipThrustPoseGuidance.EvaluateHeightTarget(
                    pose,
                    target,
                    hipThrustHeightEnterTolerance,
                    hipThrustHeightExitTolerance)
                : default;
        }
        else if (UsesTargetedExerciseGuidance)
        {
            hasLatestTargetedGuidanceGeometry = TryCaptureTargetedExerciseGeometry(
                out TargetedExercisePoseGeometry geometry);
            latestTargetedGuidanceGeometry = geometry;
            diagnostics = TargetedExercisePoseGuidance.Evaluate(
                guidanceExercise,
                currentSegmentInfo.segmentKind,
                currentSegmentInfo.poseGuidanceRule,
                geometry,
                targetedExerciseGuidance);
        }
        else
        {
            hasLatestTargetedGuidanceGeometry = false;
            if (guidanceDiagnosticEngine == null)
            {
                guidanceDiagnosticEngine = new PoseHighlightDiagnosticEngine(
                    guidanceDiagnostics);
            }

            if (!guidanceDiagnosticEngine.HasTarget &&
                !guidanceDiagnosticEngine.BeginTarget(
                    coachAnimator,
                    EffectiveGuidanceBones,
                    currentSegmentInfo.segmentKind,
                    currentSegmentInfo.poseGuidanceRule))
            {
                ResetVisualGuidanceEvidence();
                bodyPartFeedback?.Hide();
                modelBodyPartHighlighter?.Hide();
                return;
            }

            diagnostics = guidanceDiagnosticEngine.Evaluate(
                userAnimator,
                EffectiveGuidanceBones,
                Time.deltaTime);
        }

        guidanceCandidateParts.Clear();
        UpdateGuidancePart(BodyPart.LeftArm, FilterLumbarHighlightRegion(
            BodyPart.LeftArm, diagnostics.leftArm));
        UpdateGuidancePart(BodyPart.RightArm, FilterLumbarHighlightRegion(
            BodyPart.RightArm, diagnostics.rightArm));
        UpdateGuidancePart(BodyPart.LeftLeg, FilterLumbarHighlightRegion(
            BodyPart.LeftLeg, diagnostics.leftLeg));
        UpdateGuidancePart(BodyPart.RightLeg, FilterLumbarHighlightRegion(
            BodyPart.RightLeg, diagnostics.rightLeg));
        UpdateGuidancePart(BodyPart.Torso, FilterLumbarHighlightRegion(
            BodyPart.Torso, diagnostics.torso));
        UpdateConfirmedGuidanceParts(Time.deltaTime);
        string correctionPart = null;
        foreach (BodyPart part in AllGuidanceParts)
        {
            if (guidanceCandidateParts.Contains(part) && guidancePartValidity[part] &&
                HasPositiveTargetTracking(part) &&
                currentSegmentInfo.poseGuidanceRule.GetRole(part) == PoseGuidanceRegionRole.Active)
            { correctionPart = part.ToString(); break; }
        }
        voiceManager?.ObserveLessonCorrection(correctionPart,
            guidanceInputIsFresh && correctionPart != null);

        // Patient-facing guidance is positive-only. Keep the existing
        // diagnostics for corrective voice and haptic routing, but never
        // expose mismatch regions as red highlights or an error list.
        guidancePartsToHighlight.Clear();
        foreach (BodyPart part in AllGuidanceParts)
        {
            if (continuousObservationActive && guidanceInputIsFresh && !HasHardPoseConstraint() &&
                currentSegmentInfo.segmentKind == TrainingSegmentKind.CoreAction &&
                (!UsesLumbarTeaching || !lumbarTeachingProfile.positiveHighlightAtTarget) &&
                currentSegmentInfo.poseGuidanceRule.GetRole(part) ==
                    PoseGuidanceRegionRole.Active &&
                guidancePartValidity[part] && HasPositiveTargetTracking(part) &&
                (UsesHipThrustTeaching ? diagnostics.Get(part).shouldActivate : guidancePartShouldClear[part]))
            {
                guidancePartsToHighlight.Add(part);
            }
        }

        UpdateGuidancePartColors(diagnostics);
        bool showLumbarPositiveTarget = continuousObservationActive && UpdateLumbarPositiveTargetHighlight(
            diagnostics.torso);
        showLumbarPositiveTarget = showLumbarPositiveTarget &&
            currentSegmentInfo.poseGuidanceRule.GetRole(BodyPart.Torso) == PoseGuidanceRegionRole.Active;
        if (showLumbarPositiveTarget &&
            !guidancePartsToHighlight.Contains(BodyPart.Torso))
        {
            guidancePartsToHighlight.Add(BodyPart.Torso);
        }
        if (showLumbarPositiveTarget)
        {
            guidancePartColors[BodyPart.Torso] =
                new Color(0.05f, 1f, 0.25f, 0.95f);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ReportGuidanceChange();
#endif

        if (guidancePartsToHighlight.Count == 0)
        {
            bodyPartFeedback?.Hide();
            modelBodyPartHighlighter?.Hide();
            return;
        }

        BodyPart mostMisalignedPart = guidancePartsToHighlight[0];
        bool modelGuidanceShown = enableModelSurfaceHighlight &&
            modelBodyPartHighlighter != null &&
            (UsesHipThrustTeaching
                ? modelBodyPartHighlighter.ShowHipPelvis(HipThrustHeightReadyColor)
                : modelBodyPartHighlighter.Show(guidancePartColors));
        if (modelGuidanceShown)
        {
            // The surface overlay replaces the old picture card.
            bodyPartFeedback?.Hide();
        }
        else
        {
            // Do not show the diagnostic mismatch card in the patient UI.
            modelBodyPartHighlighter?.Hide();
            bodyPartFeedback?.Hide();
        }
    }

    private bool HasPositiveTargetTracking(BodyPart part)
    {
        if (guidanceVmcReceiver == null || hapticFeedback == null ||
            !hapticFeedback.HasValidTracking(part)) return false;
        var receiver = guidanceVmcReceiver.GetComponent<EVMC4U.ExternalReceiver>();
        if (receiver == null) return false;
        float timeout = Mathf.Max(0.01f, vmcFreshnessTimeoutSeconds);
        if (part == BodyPart.Torso)
        {
            return receiver.HasFreshGuidanceBone(HumanBodyBones.Hips, timeout) &&
                (receiver.HasFreshGuidanceBone(HumanBodyBones.Spine, timeout) ||
                 receiver.HasFreshGuidanceBone(HumanBodyBones.Chest, timeout) ||
                 receiver.HasFreshGuidanceBone(HumanBodyBones.UpperChest, timeout));
        }
        // Lumbar profiles (including the side bridge support arm) also use
        // limb targets. Only Bird Dog needs its additional bend geometry.
        if (!RecordedAlternatingCoachMode.IsRecordedScene(gameObject.scene.name) &&
            guidanceExercise == PoseGuidanceExercise.BirdDog &&
            !hasLatestTargetedGuidanceGeometry) return false;
        HumanBodyBones proximal, distal;
        bool bendTracked;
        switch (part)
        {
            case BodyPart.LeftArm:
                proximal = HumanBodyBones.LeftUpperArm; distal = HumanBodyBones.LeftLowerArm;
                bendTracked = latestTargetedGuidanceGeometry.leftArmBendTracked; break;
            case BodyPart.RightArm:
                proximal = HumanBodyBones.RightUpperArm; distal = HumanBodyBones.RightLowerArm;
                bendTracked = latestTargetedGuidanceGeometry.rightArmBendTracked; break;
            case BodyPart.LeftLeg:
                proximal = HumanBodyBones.LeftUpperLeg; distal = HumanBodyBones.LeftLowerLeg;
                bendTracked = latestTargetedGuidanceGeometry.leftLegBendTracked; break;
            case BodyPart.RightLeg:
                proximal = HumanBodyBones.RightUpperLeg; distal = HumanBodyBones.RightLowerLeg;
                bendTracked = latestTargetedGuidanceGeometry.rightLegBendTracked; break;
            default: return false;
        }
        if (!receiver.HasFreshGuidanceBone(proximal, timeout)) return false;
        if (RecordedAlternatingCoachMode.IsRecordedScene(gameObject.scene.name))
            return receiver.HasFreshGuidanceBone(distal, timeout);
        return guidanceExercise != PoseGuidanceExercise.BirdDog ||
            (bendTracked && receiver.HasFreshGuidanceBone(distal, timeout));
    }

    private bool UsesTargetedExerciseGuidance =>
        guidanceExercise == PoseGuidanceExercise.DeadBug ||
        guidanceExercise == PoseGuidanceExercise.BirdDog;

    private bool TryCaptureTargetedExerciseGeometry(
        out TargetedExercisePoseGeometry geometry)
    {
        geometry = new TargetedExercisePoseGeometry();
        if (userAnimator == null)
        {
            return false;
        }

        Transform hips = userAnimator.GetBoneTransform(HumanBodyBones.Hips);
        Transform upperTorso =
            userAnimator.GetBoneTransform(HumanBodyBones.UpperChest) ??
            userAnimator.GetBoneTransform(HumanBodyBones.Chest) ??
            userAnimator.GetBoneTransform(HumanBodyBones.Spine);
        Transform leftUpperArm =
            userAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightUpperArm =
            userAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform leftShoulder =
            userAnimator.GetBoneTransform(HumanBodyBones.LeftShoulder) ??
            leftUpperArm;
        Transform rightShoulder =
            userAnimator.GetBoneTransform(HumanBodyBones.RightShoulder) ??
            rightUpperArm;
        Transform leftLowerArm =
            userAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform rightLowerArm =
            userAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform leftHand =
            userAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightHand =
            userAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftUpperLeg =
            userAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightUpperLeg =
            userAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform leftLowerLeg =
            userAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rightLowerLeg =
            userAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform leftFoot =
            userAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot =
            userAnimator.GetBoneTransform(HumanBodyBones.RightFoot);

        float torsoLength = hips != null && upperTorso != null
            ? Vector3.Distance(hips.position, upperTorso.position)
            : 0f;

        geometry.bodyPlaneTracked = TryBuildBodyPlaneNormal(
            hips,
            upperTorso,
            leftUpperArm,
            rightUpperArm,
            leftUpperLeg,
            rightUpperLeg,
            out geometry.bodyPlaneNormal);
        geometry.torsoTiltTracked = TryGetTorsoTiltFromHorizontal(
            hips,
            upperTorso,
            out geometry.torsoTiltFromHorizontalDegrees);
        geometry.leftArmTracked = TryGetBoneDirection(
            leftUpperArm,
            leftLowerArm,
            out geometry.leftArmDirection);
        geometry.leftArmLineTracked = TryGetBoneDirection(
            leftShoulder,
            leftHand,
            out geometry.leftArmLineDirection);
        geometry.leftArmSupportTracked = TryGetSupportDropTorsoRatio(
            leftShoulder,
            leftHand,
            torsoLength,
            out geometry.leftArmSupportDropTorsoRatio);
        geometry.leftArmBendTracked = TryGetJointBendDegrees(
            leftUpperArm,
            leftLowerArm,
            leftHand,
            out geometry.leftArmBendDegrees);
        geometry.rightArmTracked = TryGetBoneDirection(
            rightUpperArm,
            rightLowerArm,
            out geometry.rightArmDirection);
        geometry.rightArmLineTracked = TryGetBoneDirection(
            rightShoulder,
            rightHand,
            out geometry.rightArmLineDirection);
        geometry.rightArmSupportTracked = TryGetSupportDropTorsoRatio(
            rightShoulder,
            rightHand,
            torsoLength,
            out geometry.rightArmSupportDropTorsoRatio);
        geometry.rightArmBendTracked = TryGetJointBendDegrees(
            rightUpperArm,
            rightLowerArm,
            rightHand,
            out geometry.rightArmBendDegrees);
        geometry.leftLegTracked = TryGetBoneDirection(
            leftUpperLeg,
            leftLowerLeg,
            out geometry.leftLegDirection);
        geometry.leftLegSupportTracked = TryGetSupportDropTorsoRatio(
            leftUpperLeg,
            leftLowerLeg,
            torsoLength,
            out geometry.leftLegSupportDropTorsoRatio);
        geometry.leftLegBendTracked = TryGetJointBendDegrees(
            leftUpperLeg,
            leftLowerLeg,
            leftFoot,
            out geometry.leftLegBendDegrees);
        geometry.rightLegTracked = TryGetBoneDirection(
            rightUpperLeg,
            rightLowerLeg,
            out geometry.rightLegDirection);
        geometry.rightLegSupportTracked = TryGetSupportDropTorsoRatio(
            rightUpperLeg,
            rightLowerLeg,
            torsoLength,
            out geometry.rightLegSupportDropTorsoRatio);
        geometry.rightLegBendTracked = TryGetJointBendDegrees(
            rightUpperLeg,
            rightLowerLeg,
            rightFoot,
            out geometry.rightLegBendDegrees);
        return geometry.bodyPlaneTracked;
    }

    private static bool TryBuildBodyPlaneNormal(
        Transform hips,
        Transform upperTorso,
        Transform leftUpperArm,
        Transform rightUpperArm,
        Transform leftUpperLeg,
        Transform rightUpperLeg,
        out Vector3 normal)
    {
        normal = Vector3.zero;
        if (hips == null || upperTorso == null)
        {
            return false;
        }

        Vector3 longitudinal = upperTorso.position - hips.position;
        if (longitudinal.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        Vector3 lateral = Vector3.zero;
        int lateralSamples = 0;
        AddLateralAxis(
            leftUpperArm,
            rightUpperArm,
            ref lateral,
            ref lateralSamples);
        AddLateralAxis(
            leftUpperLeg,
            rightUpperLeg,
            ref lateral,
            ref lateralSamples);
        if (lateralSamples == 0)
        {
            return false;
        }

        longitudinal.Normalize();
        lateral -= Vector3.Project(lateral, longitudinal);
        if (lateral.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        normal = Vector3.Cross(longitudinal, lateral.normalized);
        if (normal.sqrMagnitude <= 0.000001f)
        {
            normal = Vector3.zero;
            return false;
        }

        normal.Normalize();
        return true;
    }

    private static void AddLateralAxis(
        Transform left,
        Transform right,
        ref Vector3 sum,
        ref int sampleCount)
    {
        if (left == null || right == null)
        {
            return;
        }

        Vector3 candidate = right.position - left.position;
        if (candidate.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        candidate.Normalize();
        if (sampleCount > 0 && Vector3.Dot(sum, candidate) < 0f)
        {
            candidate = -candidate;
        }

        sum += candidate;
        sampleCount++;
    }

    private static bool TryGetBoneDirection(
        Transform proximal,
        Transform distal,
        out Vector3 direction)
    {
        direction = Vector3.zero;
        if (proximal == null || distal == null)
        {
            return false;
        }

        direction = distal.position - proximal.position;
        return direction.sqrMagnitude > 0.000001f;
    }

    private static bool TryGetTorsoTiltFromHorizontal(
        Transform hips,
        Transform upperTorso,
        out float tiltDegrees)
    {
        tiltDegrees = 0f;
        if (hips == null || upperTorso == null)
        {
            return false;
        }

        Vector3 torso = upperTorso.position - hips.position;
        if (torso.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        float verticalShare = Mathf.Clamp01(Mathf.Abs(Vector3.Dot(
            torso.normalized,
            Vector3.up)));
        tiltDegrees = Mathf.Asin(verticalShare) * Mathf.Rad2Deg;
        return true;
    }

    private static bool TryGetSupportDropTorsoRatio(
        Transform proximal,
        Transform endpoint,
        float torsoLength,
        out float dropRatio)
    {
        dropRatio = 0f;
        if (proximal == null || endpoint == null || torsoLength <= 0.000001f)
        {
            return false;
        }

        dropRatio = Vector3.Dot(
            endpoint.position - proximal.position,
            Vector3.down) / torsoLength;
        return true;
    }

    private static bool TryGetJointBendDegrees(
        Transform proximal,
        Transform joint,
        Transform distal,
        out float bendDegrees)
    {
        bendDegrees = 0f;
        if (proximal == null || joint == null || distal == null)
        {
            return false;
        }

        Vector3 towardProximal = proximal.position - joint.position;
        Vector3 towardDistal = distal.position - joint.position;
        if (towardProximal.sqrMagnitude <= 0.000001f ||
            towardDistal.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        bendDegrees = 180f - Vector3.Angle(towardProximal, towardDistal);
        return true;
    }

    private void UpdateGuidancePart(
        BodyPart part,
        PoseHighlightRegionDiagnostic diagnostic)
    {
        guidancePartValidity[part] = diagnostic.isValid;
        guidancePartShouldClear[part] = diagnostic.shouldClear;
        if (!diagnostic.isValid)
        {
            guidancePartErrors[part] = 0f;
            guidancePartRawScores[part] = 100f;
            guidancePartScores[part] = 100f;
            guidancePartThresholds[part] = 0f;
            guidancePartSeverities[part] = 0f;
            return;
        }

        guidancePartErrors[part] = diagnostic.worstRawErrorDegrees;
        guidancePartSeverities[part] = diagnostic.severity;

        // Keep the historical score-shaped debug properties available for
        // tooling during migration. They are derived telemetry and never feed
        // back into the highlighter decision.
        float compatibilityScore = Mathf.Clamp(
            100f - diagnostic.severity * 30f,
            0f,
            100f);
        guidancePartRawScores[part] = compatibilityScore;
        guidancePartScores[part] = compatibilityScore;
        guidancePartThresholds[part] = 70f;
        if (UsesHipThrustTeaching ? diagnostic.shouldClear : diagnostic.shouldActivate)
        {
            guidanceCandidateParts.Add(part);
        }
    }

    private void UpdateConfirmedGuidanceParts(float deltaTime)
    {
        EnsureGuidanceState();
        guidanceRegionLatch.Configure(
            guidanceHighlightConfirmationSeconds,
            guidanceHighlightReleaseSeconds,
            maximumSimultaneousGuidanceParts);

        guidanceEvidence.Clear();
        foreach (BodyPart part in AllGuidanceParts)
        {
            guidanceEvidence.Add(new PoseGuidanceRegionEvidence(
                part,
                guidancePartValidity[part],
                // Candidate mismatch diagnostics are not a positive state.
                // Green requires valid tracking and the existing clear rule.
                guidancePartValidity[part] && guidancePartShouldClear[part],
                guidancePartShouldClear[part],
                guidancePartSeverities[part]));
        }

        guidanceRegionLatch.Update(
            guidanceEvidence,
            deltaTime,
            guidanceInputIsFresh);
        SyncGuidanceLatchState();
    }

    private void SelectVisibleGuidanceParts()
    {
        EnsureGuidanceState();
        guidanceRegionLatch.Configure(
            guidanceHighlightConfirmationSeconds,
            guidanceHighlightReleaseSeconds,
            maximumSimultaneousGuidanceParts);
        SyncGuidanceLatchState();
    }

    private void UpdateGuidancePartColors(
        PoseHighlightFrameDiagnostics diagnostics)
    {
        guidancePartColors.Clear();
        foreach (BodyPart part in guidancePartsToHighlight)
        {
            guidancePartColors[part] = UsesHipThrustTeaching
                ? HipThrustHeightReadyColor
                : new Color(0.05f, 1f, 0.25f, 0.95f);
        }

    }

    private void EnsureGuidanceState()
    {
        if (guidanceRegionLatch == null)
        {
            guidanceRegionLatch = new PoseGuidanceRegionLatch(
                guidanceHighlightConfirmationSeconds,
                guidanceHighlightReleaseSeconds,
                maximumSimultaneousGuidanceParts);
        }

        if (coarseActionReadinessLatch == null)
        {
            coarseActionReadinessLatch = new CoarseActionReadinessLatch(
                coarseActionConfirmationSeconds,
                coarseActionReleaseSeconds);
        }

        if (vmcFreshnessMonitor == null)
        {
            vmcFreshnessMonitor = new VmcPoseDataFreshnessMonitor(
                vmcFreshnessTimeoutSeconds);
        }
    }

    private void SyncGuidanceLatchState()
    {
        confirmedGuidanceParts.Clear();
        foreach (BodyPart part in guidanceRegionLatch.ConfirmedParts)
        {
            confirmedGuidanceParts.Add(part);
        }

        guidancePartsToHighlight.Clear();
        foreach (BodyPart part in guidanceRegionLatch.VisibleParts)
        {
            guidancePartsToHighlight.Add(part);
        }

        guidanceMismatchSeconds.Clear();
        guidanceClearSeconds.Clear();
        foreach (BodyPart part in AllGuidanceParts)
        {
            float mismatch = guidanceRegionLatch.GetMismatchSeconds(part);
            if (mismatch > 0f)
            {
                guidanceMismatchSeconds[part] = mismatch;
            }

            float clear = guidanceRegionLatch.GetClearSeconds(part);
            if (clear > 0f)
            {
                guidanceClearSeconds[part] = clear;
            }
        }
    }

    private void ResetGuidanceLatchState()
    {
        EnsureGuidanceState();
        guidanceRegionLatch.Reset();
        coarseActionReadinessLatch.Reset();
        confirmedGuidanceParts.Clear();
        guidancePartsToHighlight.Clear();
        guidanceMismatchSeconds.Clear();
        guidanceClearSeconds.Clear();
    }

    private bool UpdateGuidanceVmcFreshness(float deltaTime)
    {
        EnsureGuidanceState();
        vmcFreshnessMonitor.Configure(vmcFreshnessTimeoutSeconds);
        ResolveGuidanceVmcReceiver();
        if (guidanceVmcReceiver == null)
        {
            if (!hasLoggedMissingGuidanceReceiver)
            {
                Debug.LogError(
                    "PoseScorer: VMC freshness is enabled but no receiver " +
                    "driving the user avatar could be resolved. Corrective " +
                    "guidance is disabled until a receiver is available.",
                    this);
                hasLoggedMissingGuidanceReceiver = true;
            }

            vmcFreshnessMonitor.Reset();
            guidanceInputWasFresh = false;
            return false;
        }

        hasLoggedMissingGuidanceReceiver = false;
        bool receiverAvailable = guidanceVmcReceiver.isActiveAndEnabled &&
            guidanceVmcReceiver.GetAvailable() > 0 &&
            !HasHardPoseConstraint();
        bool isFresh = vmcFreshnessMonitor.Update(
            receiverAvailable,
            guidanceVmcReceiver.GetRemoteTime(),
            // Android can update Humanoid bones without /VMC/Ext/T frames.
            Mathf.Max(
                guidanceVmcReceiver.LastPacketframeCounterInFrame,
                guidanceVmcReceiver.LastBonePacketCounterInFrame),
            deltaTime);
        if (isFresh && !guidanceInputWasFresh)
        {
            // A reconnect must earn confirmation from a clean state.
            ResetGuidanceLatchState();
        }

        guidanceInputWasFresh = isFresh;
        return isFresh;
    }

    private bool HasHardPoseConstraint()
    {
        if (guidanceVmcReceiver == null)
        {
            return false;
        }

        EVMC4U.ExternalReceiver receiver =
            guidanceVmcReceiver.GetComponent<EVMC4U.ExternalReceiver>();
        return receiver != null && receiver.HasHardPoseConstraint;
    }

    private void ResolveGuidanceVmcReceiver()
    {
        if (ReceiverDrivesGuidanceAnimator(
                guidanceVmcReceiver,
                userAnimator))
        {
            return;
        }

        guidanceVmcReceiver = null;
        if (userAnimator == null)
        {
            return;
        }

        foreach (AvatarMotionSource candidate in
            FindObjectsOfType<AvatarMotionSource>(true))
        {
            if (ReceiverDrivesGuidanceAnimator(candidate, userAnimator))
            {
                guidanceVmcReceiver = candidate;
                return;
            }
        }
    }

    private static bool ReceiverDrivesGuidanceAnimator(
        AvatarMotionSource receiver,
        Animator animator)
    {
        if (receiver == null || animator == null || receiver.Model == null)
        {
            return false;
        }

        Transform model = receiver.Model.transform;
        Transform target = animator.transform;
        return target == model || target.IsChildOf(model);
    }

    private void ResetVisualGuidanceEvidence()
    {
        guidanceCandidateParts.Clear();
        guidancePartColors.Clear();
        ResetGuidanceLatchState();
        EnsureGuidanceState();
        vmcFreshnessMonitor.Reset();
        guidanceInputWasFresh = false;
        guidanceInputIsFresh = true;
        foreach (BodyPart part in AllGuidanceParts)
        {
            guidancePartValidity[part] = false;
            guidancePartShouldClear[part] = true;
            guidancePartErrors[part] = 0f;
            guidancePartSeverities[part] = 0f;
            guidancePartScores[part] = 100f;
            guidancePartRawScores[part] = 100f;
            guidancePartThresholds[part] = 0f;
        }
    }

    public float GuidanceHighlightBelowScore =>
        guidanceHighlightBelowScore;
    public float GuidanceClearAtOrAboveScore =>
        Mathf.Max(guidanceHighlightBelowScore, guidanceClearAtOrAboveScore);
    public float GuidanceScoreLeniency => guidanceScoreLeniency;
    public IReadOnlyDictionary<BodyPart, float> CurrentGuidancePartErrors =>
        guidancePartErrors;
    public IReadOnlyDictionary<BodyPart, float> CurrentGuidancePartRawScores =>
        guidancePartRawScores;
    public IReadOnlyDictionary<BodyPart, float> CurrentGuidancePartScores =>
        guidancePartScores;
    public IReadOnlyDictionary<BodyPart, float> CurrentGuidancePartThresholds =>
        guidancePartThresholds;
    public IReadOnlyDictionary<BodyPart, float> CurrentGuidancePartSeverities =>
        guidancePartSeverities;
    public IReadOnlyDictionary<BodyPart, bool> CurrentGuidancePartValidity =>
        guidancePartValidity;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void ReportGuidanceChange()
    {
        bool unchanged = hasReportedGuidanceState &&
            lastReportedGuidanceParts.Count == guidancePartsToHighlight.Count;
        if (unchanged)
        {
            foreach (BodyPart part in guidancePartsToHighlight)
            {
                if (!lastReportedGuidanceParts.Contains(part))
                {
                    unchanged = false;
                    break;
                }
            }
        }

        if (unchanged)
        {
            return;
        }

        hasReportedGuidanceState = true;
        lastReportedGuidanceParts.Clear();
        foreach (BodyPart part in guidancePartsToHighlight)
        {
            lastReportedGuidanceParts.Add(part);
        }

        string highlighted = guidancePartsToHighlight.Count > 0
            ? string.Join(", ", guidancePartsToHighlight)
            : "None";
        string errors = string.Join(
            ", ",
            guidancePartErrors.Select(pair =>
                $"{pair.Key}=valid {guidancePartValidity[pair.Key]}, " +
                $"worst error {pair.Value:0.0}\u00b0, " +
                $"severity {guidancePartSeverities[pair.Key]:0.00}"));
        Debug.Log(
            $"[PoseGuidance] phase={CurrentFlowPhaseName}, highlighted={highlighted}, " +
            $"exercise={guidanceExercise}, diagnostics: {errors}",
            this);
    }
#endif
}
