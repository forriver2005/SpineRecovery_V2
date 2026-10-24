using System.Collections.Generic;
using UnityEngine;

public partial class PoseScorer
{
    private LumbarCoachProfile lumbarTeachingProfile;
    private LumbarTeachingEvidence lumbarTeachingEvidence;
    private bool UsesLumbarTeaching => lumbarTeachingProfile != null;
    private bool UsesShortTeaching => UsesHipThrustTeaching || UsesLumbarTeaching;
    private IReadOnlyList<HumanBodyBones> EffectiveGuidanceBones =>
        UsesLumbarTeaching ? scoringBones : GuidanceBones;

    public void ConfigureLumbarTeaching(LumbarCoachProfile profile)
    {
        lumbarTeachingProfile = profile;
        lumbarTeachingEvidence?.Dispose();
        lumbarTeachingEvidence = new LumbarTeachingEvidence(profile);
        scoringBones = profile.scoringBones;
        guidanceExercise = PoseGuidanceExercise.Generic;
        useSessionStartCalibration = false;
        guidanceDiagnostics.targetAxisMinimumDegrees = 5f;
    }

    private bool IsTeachingPoseReady()
    {
        if (!CanSampleCurrentPose()) return false;
        bool core = currentSegmentInfo.segmentKind == TrainingSegmentKind.CoreAction;
        if (UsesHipThrustTeaching)
        {
            if (!HipThrustPoseGuidance.TryCapture(userAnimator, out HipThrustPoseGeometry pose))
                return false;
            if (core) return hasHipThrustRestPose && pose.torsoTilt <= 55f &&
                HipThrustPoseGuidance.LiftProgress(pose, hipThrustRestPose) >= 8f;
            return HipThrustPoseGuidance.IsResting(pose) && (!hasHipThrustRestPose ||
                HipThrustPoseGuidance.LiftProgress(pose, hipThrustRestPose) <= 5f);
        }
        if (!lumbarTeachingEvidence.Sample(userAnimator, coachAnimator, out float progress))
            return false;
        return core ? lumbarTeachingEvidence.HasBaseline && progress >= 0.15f :
            !lumbarTeachingEvidence.HasBaseline || progress <= 0.3f;
    }

    private void CaptureTeachingPreparation()
    {
        if (!UsesShortTeaching ||
            currentSegmentInfo.segmentKind != TrainingSegmentKind.Preparation ||
            !IsTeachingPoseReady()) return;
        // A timeout bypasses readiness but must not calibrate an incorrect pose.
        if (UsesHipThrustTeaching)
        {
            if (HipThrustPoseGuidance.TryCapture(userAnimator, out HipThrustPoseGeometry pose))
            {
                hipThrustRestPose = pose;
                hasHipThrustRestPose = true;
            }
        }
        else lumbarTeachingEvidence.CaptureBaseline();
    }

    private bool ShouldAnnounceNextAfterScore => coachController != null &&
        coachController.HasNextAfterCurrentHold &&
        !(UsesShortTeaching && currentSegmentInfo.segmentKind == TrainingSegmentKind.ReturnTransition);

    private void CompleteScoringCheckpoint()
    {
        if (UsesShortTeaching && currentSegmentInfo.segmentKind == TrainingSegmentKind.ReturnTransition)
        {
            // Rest follows the full shared confirmation/countdown/scoring flow.
            // Defer the next-action announcement until rest has finished.
            stateTimer = UsesHipThrustTeaching ? Mathf.Max(0f, hipThrustRestSeconds) : 4f;
            TransitionTo(State.Resting, "return scored; starting full rest");
            ResetVisualGuidanceEvidence();
            modelBodyPartHighlighter?.Hide();
        }
        else coachController?.AdvanceToNext();
    }

    private void UpdateTeachingRest()
    {
        // A real return can establish a missing baseline on later repetitions.
        if (IsTeachingPoseReady())
        {
            if (UsesHipThrustTeaching && !hasHipThrustRestPose &&
                HipThrustPoseGuidance.TryCapture(userAnimator, out HipThrustPoseGeometry pose))
            {
                hipThrustRestPose = pose;
                hasHipThrustRestPose = true;
            }
            else if (UsesLumbarTeaching && !lumbarTeachingEvidence.HasBaseline)
                lumbarTeachingEvidence.CaptureBaseline();
        }
        stateTimer -= Time.deltaTime;
        UpdateStatusText((UsesHipThrustTeaching ? "回落后放松休息" : lumbarTeachingProfile.recovery) +
            $"（休息 {Mathf.CeilToInt(Mathf.Max(0f, stateTimer))} 秒）");
        if (stateTimer > 0f) return;
        if (coachController != null && coachController.HasNextAfterCurrentHold)
            voiceManager?.PlayNextAction();
        TransitionTo(State.Idle, "teaching rest complete");
        coachController?.AdvanceToNext();
    }
}
