using System.Collections.Generic;
using UnityEngine;

// Coaching-side bridge for the game target predicates. The game timing layer
// still owns HIT resolution; this bridge only samples the same target result
// continuously for positive coaching feedback.
public partial class PoseScorer
{
    DeadBugGamingPoseScorer coachingGamePoseScorer;
    RhythmPoseJudge coachingRhythmPoseJudge;
    RhythmMovementRange coachingMovementRange;
    HipThrustRuleScoring coachingRuleScoring;
    SideBridgePoseGeometry coachingSideGeometry;
    int coachingGameSegment = -1;
    int coachingGameRepeat = -1;
    bool coachingPreparationFeedbackLatched;
    bool coachingPreparationFeedbackCompleted;
    float coachingPreparationFeedbackStartedAt;
    readonly HashSet<BodyPart> coachingGameReadyParts = new HashSet<BodyPart>();
    readonly Dictionary<BodyPart, bool> previousCoachingGameReady =
        new Dictionary<BodyPart, bool>();

    bool UsesSideBridgeTeaching => guidanceExercise == PoseGuidanceExercise.SideBridge ||
        gameObject.scene.name.StartsWith("SideBridge");

    bool UsesGameTargetFeedback => guidanceExercise == PoseGuidanceExercise.DeadBug ||
        guidanceExercise == PoseGuidanceExercise.BirdDog ||
        guidanceExercise == PoseGuidanceExercise.HipThrust ||
        UsesSideBridgeTeaching;

    void UpdateGameTargetFeedback()
    {
        coachingGameReadyParts.Clear();
        bool preparation = currentSegmentInfo.segmentKind == TrainingSegmentKind.Preparation &&
            (guidanceExercise == PoseGuidanceExercise.DeadBug ||
             guidanceExercise == PoseGuidanceExercise.BirdDog);
        if (!continuousObservationActive || !UsesGameTargetFeedback ||
            (!preparation && currentSegmentInfo.segmentKind != TrainingSegmentKind.CoreAction) ||
            !guidanceInputIsFresh || HasHardPoseConstraint())
        {
            ResetCoachingGameReadyState();
            return;
        }

        if (preparation)
        {
            UpdatePreparationTargetFeedback();
        }
        else if (guidanceExercise == PoseGuidanceExercise.DeadBug ||
            guidanceExercise == PoseGuidanceExercise.BirdDog)
        {
            UpdateDeadBugBirdDogGameTargets();
        }
        else
        {
            UpdateBridgeGameTargets();
        }

        ApplyCoachingGameFeedback();
    }

    void EnsureCoachingDeadBugPredicate()
    {
        if (coachingGamePoseScorer == null)
        {
            coachingGamePoseScorer = gameObject.AddComponent<DeadBugGamingPoseScorer>();
            coachingGamePoseScorer.ConfigureExternalCoaching(
                coachAnimator,
                userAnimator,
                guidanceInputIsFresh,
                GetCoachingDifficultyKey());
        }

        if (coachingRhythmPoseJudge == null)
        {
            coachingRhythmPoseJudge = gameObject.AddComponent<RhythmPoseJudge>();
            coachingRhythmPoseJudge.ConfigurePoseScorer(coachingGamePoseScorer);
        }
        coachingGamePoseScorer.ConfigureExternalCoaching(
            coachAnimator,
            userAnimator,
            guidanceInputIsFresh,
            GetCoachingDifficultyKey());
    }

    void UpdateDeadBugBirdDogGameTargets()
    {
        EnsureCoachingDeadBugPredicate();
        coachingGamePoseScorer.SetExternalCoachingInputFresh(guidanceInputIsFresh);
        coachingGamePoseScorer.EvaluateExternalCoachingFrame(Time.deltaTime);

        EnsureCoachingMovementRange();

        foreach (BodyPart part in AllGuidanceParts)
        {
            if (part == BodyPart.Torso ||
                currentSegmentInfo.poseGuidanceRule.GetRole(part) !=
                    PoseGuidanceRegionRole.Active)
            {
                continue;
            }

            RhythmBodyTarget target = ToRhythmTarget(part);
            if (coachingRhythmPoseJudge.IsTargetReached(
                target,
                coachingMovementRange,
                out _))
            {
                coachingGameReadyParts.Add(part);
            }
        }
    }

    void UpdatePreparationTargetFeedback()
    {
        EnsureCoachingDeadBugPredicate();
        coachingGamePoseScorer.ConfigureExternalCoaching(
            coachAnimator,
            userAnimator,
            guidanceInputIsFresh,
            GetCoachingDifficultyKey());
        coachingGamePoseScorer.SetExternalCoachingInputFresh(guidanceInputIsFresh);
        coachingGamePoseScorer.EvaluateExternalCoachingFrame(Time.deltaTime);
        EnsureCoachingMovementRange();

        bool allReached = true;
        foreach (BodyPart part in AllGuidanceParts)
        {
            RhythmBodyTarget target = ToRhythmTarget(part);
            bool reached = guidanceExercise == PoseGuidanceExercise.DeadBug
                ? coachingRhythmPoseJudge.IsTargetReached(
                    target, coachingMovementRange, out _)
                : coachingRhythmPoseJudge.IsTargetAtRest(
                    target, coachingMovementRange, out _);
            allReached &= reached;
        }

        if (!coachingPreparationFeedbackLatched &&
            !coachingPreparationFeedbackCompleted && allReached)
        {
            coachingPreparationFeedbackLatched = true;
            coachingPreparationFeedbackStartedAt = Time.unscaledTime;
        }

        if (!coachingPreparationFeedbackLatched ||
            coachingPreparationFeedbackCompleted)
        {
            return;
        }

        if (Time.unscaledTime - coachingPreparationFeedbackStartedAt >=
            Mathf.Max(0.1f, recordedDeadBugSuccessFadeSeconds))
        {
            coachingPreparationFeedbackCompleted = true;
            return;
        }

        foreach (BodyPart part in AllGuidanceParts)
        {
            coachingGameReadyParts.Add(part);
        }
    }

    void EnsureCoachingMovementRange()
    {
        if (coachingGameSegment == currentSegmentInfo.segmentIndex &&
            coachingGameRepeat == currentSegmentInfo.repeatIndex &&
            coachingMovementRange != null)
        {
            return;
        }

        coachingGameSegment = currentSegmentInfo.segmentIndex;
        coachingGameRepeat = currentSegmentInfo.repeatIndex;
        coachingMovementRange = new RhythmMovementRange();
        AnimatorStateInfo state = coachAnimator != null
            ? coachAnimator.GetCurrentAnimatorStateInfo(0)
            : default;
        coachingMovementRange.BeginAtState(coachingGamePoseScorer, state.fullPathHash);
        coachingPreparationFeedbackLatched = false;
        coachingPreparationFeedbackCompleted = false;
        coachingPreparationFeedbackStartedAt = 0f;
    }

    void EnsureCoachingBridgePredicate()
    {
        if (coachingRuleScoring == null)
        {
            coachingRuleScoring = gameObject.AddComponent<HipThrustRuleScoring>();
            coachingRuleScoring.ConfigureExternalCoaching(
                EvaluateCoachingHipTarget,
                EvaluateCoachingSideBridgeTarget);
        }
    }

    void UpdateBridgeGameTargets()
    {
        EnsureCoachingBridgePredicate();
        bool reached = UsesSideBridgeTeaching
            ? coachingRuleScoring.MatchesSideBridgeCoach()
            : coachingRuleScoring.MatchesHipCoach();
        if (reached)
        {
            coachingGameReadyParts.Add(BodyPart.Torso);
        }
    }

    bool EvaluateCoachingHipTarget()
    {
        if (!guidanceInputIsFresh ||
            !HipThrustPoseGuidance.TryCapture(userAnimator, out HipThrustPoseGeometry user) ||
            !HipThrustPoseGuidance.TryCapture(coachAnimator, out HipThrustPoseGeometry coach))
        {
            return false;
        }

        PoseHighlightFrameDiagnostics diagnostics =
            HipThrustPoseGuidance.EvaluateHeightTarget(
                user,
                coach,
                GameDifficultyPolicy.BridgeProgressTolerance(GetCoachingDifficultyKey()),
                GameDifficultyPolicy.BridgeProgressTolerance(GetCoachingDifficultyKey()));
        return diagnostics.torso.isValid && diagnostics.torso.shouldActivate;
    }

    bool EvaluateCoachingSideBridgeTarget()
    {
        if (!guidanceInputIsFresh ||
            !SideBridgePoseGeometry.TryMeasure(userAnimator, out SideBridgePoseGeometry.Pose user) ||
            !SideBridgePoseGeometry.TryMeasure(coachAnimator, out SideBridgePoseGeometry.Pose coach) ||
            !coach.IsLeftSupport ||
            !SideBridgePoseGeometry.HasStableSupport(user, coach))
        {
            return false;
        }

        coachingSideGeometry ??= new SideBridgePoseGeometry();
        string difficulty = GetCoachingDifficultyKey();
        float tolerance = difficulty == "simple" ? 1.6f : difficulty == "hard" ? 1.05f : 1.25f;
        float quality = coachingSideGeometry.Quality(user, coach, tolerance);
        float amplitude = coach.lift <= 0f
            ? 1f
            : user.lift / coach.lift;
        return quality >= GameDifficultyPolicy.FollowingThreshold(difficulty) &&
            amplitude >= (difficulty == "simple" ? .75f : difficulty == "hard" ? .78f : .76f);
    }

    void ApplyCoachingGameFeedback()
    {
        EnsureHapticFeedbackController();
        guidancePartsToHighlight.Clear();
        guidancePartColors.Clear();
        Color successColor = GuidanceSuccessColor;
        bool preparationFeedback = currentSegmentInfo.segmentKind == TrainingSegmentKind.Preparation &&
            coachingPreparationFeedbackLatched;
        if (preparationFeedback)
        {
            float fade = 1f - Mathf.Clamp01(
                (Time.unscaledTime - coachingPreparationFeedbackStartedAt) /
                Mathf.Max(0.1f, recordedDeadBugSuccessFadeSeconds));
            successColor.a *= fade;
        }

        foreach (BodyPart part in coachingGameReadyParts)
        {
            guidancePartsToHighlight.Add(part);
            guidancePartColors[part] = successColor;
            bool wasReady = previousCoachingGameReady.TryGetValue(part, out bool previous) && previous;
            if (!wasReady)
            {
                hapticFeedback?.PulseOnce(GetDefaultHapticLocation(part));
            }
        }

        foreach (BodyPart part in AllGuidanceParts)
        {
            previousCoachingGameReady[part] = coachingGameReadyParts.Contains(part);
        }

        if (guidancePartsToHighlight.Count == 0)
        {
            modelBodyPartHighlighter?.Hide();
            return;
        }

        if (UsesHipThrustTeaching || UsesSideBridgeTeaching)
        {
            modelBodyPartHighlighter?.ShowHipPelvis(successColor);
        }
        else
        {
            modelBodyPartHighlighter?.Show(guidancePartColors);
        }
    }

    void ResetCoachingGameReadyState()
    {
        coachingGameReadyParts.Clear();
        coachingPreparationFeedbackLatched = false;
        coachingPreparationFeedbackCompleted = false;
        coachingPreparationFeedbackStartedAt = 0f;
        guidancePartsToHighlight.Clear();
        guidancePartColors.Clear();
        if (UsesGameTargetFeedback)
        {
            modelBodyPartHighlighter?.Hide();
        }
        foreach (BodyPart part in AllGuidanceParts)
        {
            previousCoachingGameReady[part] = false;
        }
    }

    static RhythmBodyTarget ToRhythmTarget(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.LeftArm: return RhythmBodyTarget.LeftHand;
            case BodyPart.RightArm: return RhythmBodyTarget.RightHand;
            case BodyPart.LeftLeg: return RhythmBodyTarget.LeftFoot;
            default: return RhythmBodyTarget.RightFoot;
        }
    }

    string GetCoachingDifficultyKey()
    {
        if (!string.IsNullOrEmpty(coachingDifficultyKeyOverride))
        {
            return coachingDifficultyKeyOverride;
        }
        CoachActionController action = FindObjectOfType<CoachActionController>(true);
        if (action != null)
        {
            return action.SelectedDifficultyKey;
        }

        switch (DifficultySettings.CurrentDifficulty)
        {
            case DifficultyLevel.Relaxed: return "simple";
            case DifficultyLevel.Strict: return "hard";
            default: return "standard";
        }
    }
}
