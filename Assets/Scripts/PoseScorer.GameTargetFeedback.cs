using System.Collections.Generic;
using UnityEngine;

// Pose acceptance ported from the game formulas. Lesson phases and feedback
// remain owned by PoseScorer and the coaching playback controllers.
public partial class PoseScorer
{
    CoachTargetCriterion coachTargetCriterion;
    bool UsesCoachTargetCriterion => coachTargetCriterion != null;
    public void ConfigureCoachTargetCriterion(Animator coach, CoachMotionPackage package)
    {
        coachTargetCriterion = new CoachTargetCriterion(coach, userAnimator, package);
    }
    bool IsCoachTargetReached(BodyPart part, bool preparation = false) =>
        coachTargetCriterion != null && coachTargetCriterion.Reached(userAnimator,
            currentSegmentInfo.segmentIndex, part, preparation, coachingDifficultyKeyOverride ?? "simple");
    bool IsCoachPreparationReached()
    {
        if (!guidanceInputIsFresh || HasHardPoseConstraint()) return false;
        foreach (BodyPart part in AllGuidanceParts)
            if (part != BodyPart.Torso && (!HasPositiveTargetTracking(part) || !IsCoachTargetReached(part, true))) return false;
        return true;
    }
}

internal sealed class CoachTargetCriterion
{
    sealed class Reference
    {
        public Dictionary<HumanBodyBones, Vector3> rest, target;
        public float front;
        public HipThrustHoldGeometry.TargetPose hipRest, hipTarget;
        public SideBridgePoseGeometry.Pose sideRest, sideTarget;
    }
    readonly Reference[] references;
    readonly string action;
    readonly AnatomicalMotionScoringEngine capture = new AnatomicalMotionScoringEngine(new AnatomicalMotionScoringSettings());
    readonly HipThrustHoldGeometry hip = new HipThrustHoldGeometry();
    readonly SideBridgePoseGeometry side = new SideBridgePoseGeometry();

    public CoachTargetCriterion(Animator coach, Animator user, CoachMotionPackage package)
    {
        action = package.actionId;
        references = new Reference[package.segments.Length];
        using (var patient = new PatientPoseReference(coach, user))
        using (var handler = new HumanPoseHandler(coach.avatar, coach.transform))
        {
            var saved = new HumanPose(); handler.GetHumanPose(ref saved);
            var pose = new HumanPose();
            try
            {
                for (int i = 0; i < references.Length; i++)
                {
                    var frames = package.segments[i].frames;
                    var r = new Reference();
                    frames[0].ApplyTo(ref pose); handler.SetHumanPose(ref pose);
                    r.rest = capture.CaptureRawPose(coach);
                    Floor(coach, out r.front, out _);
                    patient.Apply();
                    hip.TryMeasurePose(patient.Animator, out r.hipRest);
                    SideBridgePoseGeometry.TryMeasure(patient.Animator, out r.sideRest);
                    frames[frames.Length - 1].ApplyTo(ref pose); handler.SetHumanPose(ref pose);
                    r.target = capture.CaptureRawPose(coach);
                    patient.Apply();
                    hip.TryMeasurePose(patient.Animator, out r.hipTarget);
                    SideBridgePoseGeometry.TryMeasure(patient.Animator, out r.sideTarget);
                    references[i] = r;
                }
                if (action == "dead_bug" || action == "bird_dog")
                {
                    var ready = references[0];
                    ready.target = new Dictionary<HumanBodyBones, Vector3>(ready.rest);
                    for (int i = 1; i < references.Length - 1; i += 2)
                    {
                        var active = package.segments[i].poseGuidanceRule.activeRegions;
                        Copy(PoseGuidanceRegionMask.LeftArm, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
                        Copy(PoseGuidanceRegionMask.RightArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
                        Copy(PoseGuidanceRegionMask.LeftLeg, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg);
                        Copy(PoseGuidanceRegionMask.RightLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg);
                        void Copy(PoseGuidanceRegionMask mask, HumanBodyBones a, HumanBodyBones b)
                        {
                            if ((active & mask) == 0) return;
                            if (references[i].target.TryGetValue(a, out var value)) ready.target[a] = value;
                            if (references[i].target.TryGetValue(b, out value)) ready.target[b] = value;
                        }
                    }
                }
            }
            finally { handler.SetHumanPose(ref saved); }
        }
    }

    public bool Reached(Animator user, int segment, BodyPart part, bool preparation, string difficulty)
    {
        if (segment < 0 || segment >= references.Length) return false;
        var r = references[preparation ? 0 : segment];
        if (action == "hip_thrust") return part == BodyPart.Torso && HipReached(user, r, difficulty);
        if (action == "side_bridge") return part == BodyPart.Torso && SideReached(user, r, difficulty);
        // RhythmMovementRange endpoint quality and RhythmPoseJudge acceptance,
        // evaluated against the lesson's authored poses rather than game notes.
        if (part == BodyPart.Torso || !Floor(user, out float front, out float tilt) ||
            Mathf.Abs(tilt) >= .7f || Mathf.Abs(front) <= .35f || front * r.front <= 0) return false;
        Bones(part, out var primary, out var secondary);
        var actual = capture.CaptureRawPose(user);
        if (!actual.TryGetValue(primary, out var direction) || !r.rest.TryGetValue(primary, out var rest) ||
            !r.target.TryGetValue(primary, out var target)) return false;
        Vector3 excursion = target - rest;
        if (excursion.sqrMagnitude < .005f) return false;
        float progress = Mathf.Clamp01(Vector3.Dot(direction - rest, excursion) / excursion.sqrMagnitude);
        var expected = preparation ? r.rest : r.target;
        if (!actual.TryGetValue(secondary, out var distal) || !expected.TryGetValue(secondary, out var expectedDistal)) return false;
        bool leg = part == BodyPart.LeftLeg || part == BodyPart.RightLeg;
        if (leg && (Vector3.Angle(direction, expected[primary]) > 50f || Vector3.Angle(distal, expectedDistal) > 65f)) return false;
        float quality = .85f * DirectionQuality(direction, expected[primary]) + .15f * DirectionQuality(distal, expectedDistal);
        quality = Mathf.Min(quality, 100f * (preparation ? Mathf.InverseLerp(.6f, .1f, progress) : Mathf.InverseLerp(.4f, .9f, progress)));
        float minimum = leg ? (difficulty == "simple" ? .63f : difficulty == "hard" ? .72f : .66f)
            : (difficulty == "simple" ? .64f : difficulty == "hard" ? .76f : .70f);
        return (preparation ? progress <= 1f - minimum : progress >= minimum) &&
            quality >= (difficulty == "simple" ? 35f : difficulty == "hard" ? 60f : 45f);
    }

    bool HipReached(Animator user, Reference r, string difficulty)
    {
        // HipThrustRuleScoring: normalized lift error plus weighted pose quality.
        if (r.hipRest == null || r.hipTarget == null || !hip.TryMeasurePose(user, out var actual) ||
            !HipThrustHoldGeometry.HasSupineSupport(actual, r.hipTarget)) return false;
        hip.restTarget = r.hipRest; hip.holdTarget = r.hipTarget;
        float difference = Mathf.Abs(hip.CalculateProgress(actual) - 1f);
        float quality = Mathf.Min(100f * (1f - Mathf.InverseLerp(.12f, .5f, difference)),
            hip.CalculatePoseQuality(actual, r.hipTarget, false));
        return quality >= Following(difficulty) && difference <= (difficulty == "simple" ? .24f : difficulty == "hard" ? .16f : .20f);
    }
    bool SideReached(Animator user, Reference r, string difficulty)
    {
        // SideBridgePoseScoring: support, weighted quality and amplitude limits.
        if (r.sideRest == null || r.sideTarget == null || !SideBridgePoseGeometry.TryMeasure(user, out var actual) ||
            !side.SetTargets(r.sideRest, r.sideTarget, r.sideRest.lift, r.sideTarget.lift) ||
            !SideBridgePoseGeometry.HasStableSupport(actual, r.sideTarget)) return false;
        float progress = side.Progress(actual);
        float tolerance = difficulty == "simple" ? 1.6f : difficulty == "hard" ? 1.05f : 1.25f;
        float quality = Mathf.Min(side.Quality(actual, r.sideTarget, tolerance),
            100f * (1f - Mathf.InverseLerp(.18f, .55f, Mathf.Abs(progress - 1f))));
        float amplitude = difficulty == "simple" ? .75f : difficulty == "hard" ? .78f : .76f;
        float sustain = difficulty == "simple" ? .55f : difficulty == "hard" ? .65f : .60f;
        return quality >= Following(difficulty) && progress >= amplitude && progress >= sustain;
    }
    static float Following(string level) => level == "simple" ? 50f : level == "hard" ? 70f : 60f;
    static float DirectionQuality(Vector3 actual, Vector3 expected) => 100f * (1f - Mathf.InverseLerp(18f, 70f, Vector3.Angle(actual, expected)));
    static bool Floor(Animator avatar, out float front, out float tilt)
    {
        front = tilt = 0f;
        if (avatar == null || !AnatomicalMotionScoringEngine.TryGetBodyFrame(avatar, out var frame)) return false;
        // The coach presentation rotates avatar roots with its camera. Tracker
        // gravity is encoded in the rig pose, not that display-only rotation.
        frame = Quaternion.Inverse(avatar.transform.rotation) * frame;
        front = Vector3.Dot(frame * Vector3.forward, Vector3.up);
        tilt = Vector3.Dot(frame * Vector3.up, Vector3.up);
        return true;
    }
    static void Bones(BodyPart part, out HumanBodyBones primary, out HumanBodyBones secondary)
    {
        switch (part)
        {
            case BodyPart.LeftArm: primary = HumanBodyBones.LeftUpperArm; secondary = HumanBodyBones.LeftLowerArm; break;
            case BodyPart.RightArm: primary = HumanBodyBones.RightUpperArm; secondary = HumanBodyBones.RightLowerArm; break;
            case BodyPart.LeftLeg: primary = HumanBodyBones.LeftUpperLeg; secondary = HumanBodyBones.LeftLowerLeg; break;
            default: primary = HumanBodyBones.RightUpperLeg; secondary = HumanBodyBones.RightLowerLeg; break;
        }
    }
}
