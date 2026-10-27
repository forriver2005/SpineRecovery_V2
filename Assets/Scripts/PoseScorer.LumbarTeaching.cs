using System.Collections.Generic;
using UnityEngine;

public partial class PoseScorer
{
    private LumbarCoachProfile lumbarTeachingProfile;
    private LumbarTeachingEvidence lumbarTeachingEvidence;
    private readonly HashSet<TrackerWearLocation> lumbarTargetHapticSent =
        new HashSet<TrackerWearLocation>();
    private readonly HashSet<TrackerWearLocation> lumbarTargetHapticCandidates =
        new HashSet<TrackerWearLocation>();
    private readonly Dictionary<TrackerWearLocation, float> lumbarTargetHapticStableSeconds =
        new Dictionary<TrackerWearLocation, float>();
    private readonly List<TrackerWearLocation> pendingLumbarTargetPulses =
        new List<TrackerWearLocation>(2);
    private float pendingLumbarTargetPulsesExpireAt;
    private bool lumbarPositiveHighlightCandidateActive;
    private bool lumbarPositiveHighlightConfirmed;
    private float lumbarPositiveHighlightStableSeconds;
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

    private void ResetLumbarTargetFeedback(bool resetHaptic)
    {
        lumbarPositiveHighlightCandidateActive = false;
        lumbarPositiveHighlightConfirmed = false;
        lumbarPositiveHighlightStableSeconds = 0f;
        if (!resetHaptic)
        {
            lumbarTargetHapticCandidates.Clear();
            lumbarTargetHapticStableSeconds.Clear();
            pendingLumbarTargetPulses.Clear();
            return;
        }

        lumbarTargetHapticSent.Clear();
        lumbarTargetHapticCandidates.Clear();
        lumbarTargetHapticStableSeconds.Clear();
        pendingLumbarTargetPulses.Clear();
        pendingLumbarTargetPulsesExpireAt = 0f;
    }

    private bool UpdateLumbarPositiveTargetHighlight(
        PoseHighlightRegionDiagnostic torsoDiagnostic)
    {
        if (!UsesLumbarTeaching ||
            !lumbarTeachingProfile.positiveHighlightAtTarget ||
            currentSegmentInfo.segmentKind != TrainingSegmentKind.CoreAction ||
            !guidanceInputIsFresh ||
            !torsoDiagnostic.isValid ||
            !torsoDiagnostic.shouldClear ||
            !lumbarTeachingEvidence.TryGetProgress(userAnimator, out float progress))
        {
            ResetLumbarPositiveHighlightCandidate();
            return false;
        }

        bool isInsideEnterBand = progress >= 0.88f && progress <= 1.12f;
        bool remainsInsideExitBand = progress >= 0.78f && progress <= 1.22f;
        if (!lumbarPositiveHighlightCandidateActive)
        {
            if (!isInsideEnterBand)
            {
                return false;
            }

            lumbarPositiveHighlightCandidateActive = true;
        }
        else if (!remainsInsideExitBand)
        {
            ResetLumbarPositiveHighlightCandidate();
            return false;
        }

        lumbarPositiveHighlightStableSeconds += Mathf.Clamp(
            Time.deltaTime,
            0f,
            0.1f);
        if (lumbarPositiveHighlightStableSeconds >= 0.2f)
        {
            lumbarPositiveHighlightConfirmed = true;
        }

        return lumbarPositiveHighlightConfirmed;
    }

    private void ResetLumbarPositiveHighlightCandidate()
    {
        lumbarPositiveHighlightCandidateActive = false;
        lumbarPositiveHighlightConfirmed = false;
        lumbarPositiveHighlightStableSeconds = 0f;
    }

    private PoseHighlightRegionDiagnostic FilterLumbarHighlightRegion(
        BodyPart part,
        PoseHighlightRegionDiagnostic diagnostic)
    {
        if (!UsesLumbarTeaching ||
            (lumbarTeachingProfile.active & GetLumbarRegionMask(part)) != 0)
        {
            return diagnostic;
        }

        return default;
    }

    private static PoseGuidanceRegionMask GetLumbarRegionMask(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.LeftArm:
                return PoseGuidanceRegionMask.LeftArm;
            case BodyPart.RightArm:
                return PoseGuidanceRegionMask.RightArm;
            case BodyPart.LeftLeg:
                return PoseGuidanceRegionMask.LeftLeg;
            case BodyPart.RightLeg:
                return PoseGuidanceRegionMask.RightLeg;
            case BodyPart.Torso:
                return PoseGuidanceRegionMask.Torso;
            default:
                return PoseGuidanceRegionMask.None;
        }
    }

    private void UpdateLumbarTargetHaptic()
    {
        if (!UsesLumbarTeaching ||
            currentSegmentInfo.segmentKind != TrainingSegmentKind.CoreAction ||
            !guidanceInputIsFresh)
        {
            lumbarTargetHapticCandidates.Clear();
            lumbarTargetHapticStableSeconds.Clear();
            return;
        }

        TrackerWearLocation[] locations =
            lumbarTeachingProfile.targetHapticLocations;
        if (locations == null)
        {
            return;
        }

        bool alreadyWaitingToDispatch = pendingLumbarTargetPulses.Count > 0;
        foreach (TrackerWearLocation location in locations)
        {
            if (lumbarTargetHapticSent.Contains(location))
            {
                continue;
            }

            if (!lumbarTeachingEvidence.TryGetProgress(
                    userAnimator,
                    location,
                    out float progress))
            {
                lumbarTargetHapticCandidates.Remove(location);
                lumbarTargetHapticStableSeconds.Remove(location);
                continue;
            }

            bool active = lumbarTargetHapticCandidates.Contains(location);
            if (!active && (progress < 0.88f || progress > 1.12f))
            {
                lumbarTargetHapticStableSeconds.Remove(location);
                continue;
            }
            if (active && (progress < 0.78f || progress > 1.22f))
            {
                lumbarTargetHapticCandidates.Remove(location);
                lumbarTargetHapticStableSeconds.Remove(location);
                continue;
            }

            lumbarTargetHapticCandidates.Add(location);
            lumbarTargetHapticStableSeconds.TryGetValue(
                location,
                out float stableSeconds);
            stableSeconds += Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            if (stableSeconds < 0.18f)
            {
                lumbarTargetHapticStableSeconds[location] = stableSeconds;
                continue;
            }

            lumbarTargetHapticSent.Add(location);
            lumbarTargetHapticCandidates.Remove(location);
            lumbarTargetHapticStableSeconds.Remove(location);
            pendingLumbarTargetPulses.Add(location);
        }
        if (!alreadyWaitingToDispatch && pendingLumbarTargetPulses.Count > 0)
        {
            pendingLumbarTargetPulsesExpireAt = Time.unscaledTime + 0.4f;
        }
    }

    private void DispatchPendingLumbarTargetPulses()
    {
        if (pendingLumbarTargetPulses.Count == 0)
        {
            return;
        }

        if (Time.unscaledTime > pendingLumbarTargetPulsesExpireAt)
        {
            pendingLumbarTargetPulses.Clear();
            return;
        }

        for (int index = pendingLumbarTargetPulses.Count - 1; index >= 0; index--)
        {
            if (hapticFeedback != null &&
                hapticFeedback.TryPulseOnce(pendingLumbarTargetPulses[index]))
            {
                pendingLumbarTargetPulses.RemoveAt(index);
            }
        }
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
