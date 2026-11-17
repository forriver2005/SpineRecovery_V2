using UnityEngine;

// Only the separately named recorded courses use these recorded end targets.
// Old DeadBug/BirdDog Coaching predicates and their thresholds are untouched.
public sealed class RecordedPracticePoseGuidance : MonoBehaviour
{
    private readonly Vector3[][] targets = new Vector3[2][];
    private static readonly HumanBodyBones[] Roots = { HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg };
    private static readonly HumanBodyBones[] Middles = { HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
    private static readonly HumanBodyBones[] Ends = { HumanBodyBones.LeftHand,
        HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
    private static readonly BodyPart[] Parts = { BodyPart.LeftArm, BodyPart.RightArm, BodyPart.LeftLeg, BodyPart.RightLeg };

    public void Initialize(Animator coach, CoachMotionPackage package)
    {
        using (var handler = new HumanPoseHandler(coach.avatar, coach.transform))
        {
            var saved = new HumanPose(); handler.GetHumanPose(ref saved);
            var pose = new HumanPose();
            for (int side = 0; side < 2; side++)
            {
                var frames = package.segments[side * 2 + 1].frames;
                frames[frames.Length - 1].ApplyTo(ref pose);
                handler.SetHumanPose(ref pose);
                targets[side] = Capture(coach);
            }
            handler.SetHumanPose(ref saved);
        }
    }

    // Remove heading only. Gravity remains meaningful: raised limbs cannot
    // match the new DeadBug's lowered target merely by turning the display.
    private static Vector3[] Capture(Animator animator)
    {
        if (animator == null) return null;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (hips == null || chest == null) return null;
        Vector3 forward = Vector3.ProjectOnPlane(chest.position - hips.position, Vector3.up);
        if (forward.sqrMagnitude < 0.000001f) return null;
        Quaternion rotation = Quaternion.Inverse(Quaternion.LookRotation(forward.normalized, Vector3.up));
        var result = new Vector3[8];
        for (int i = 0; i < 4; i++)
        {
            var root = animator.GetBoneTransform(Roots[i]);
            var middle = animator.GetBoneTransform(Middles[i]);
            var end = animator.GetBoneTransform(Ends[i]);
            if (root == null || middle == null || end == null) continue;
            result[i * 2] = rotation * (middle.position - root.position).normalized;
            result[i * 2 + 1] = rotation * (end.position - middle.position).normalized;
        }
        return result;
    }

    public PoseHighlightFrameDiagnostics Evaluate(Animator user, TrainingSegmentKind kind,
        SegmentPoseGuidanceRule rule, PoseHighlightDiagnosticSettings settings)
    {
        var result = new PoseHighlightFrameDiagnostics();
        if (kind != TrainingSegmentKind.CoreAction) return result;
        int side = (rule.activeRegions & PoseGuidanceRegionMask.RightArm) != 0 ? 0 : 1;
        var target = targets[side]; var actual = Capture(user);
        if (target == null || actual == null) return result;
        var diagnostics = new PoseHighlightRegionDiagnostic[4];
        for (int i = 0; i < 4; i++)
        {
            if (rule.GetRole(Parts[i]) != PoseGuidanceRegionRole.Active ||
                actual[i * 2].sqrMagnitude < .5f || actual[i * 2 + 1].sqrMagnitude < .5f) continue;
            float error = Mathf.Max(Vector3.Angle(actual[i * 2], target[i * 2]),
                Vector3.Angle(actual[i * 2 + 1], target[i * 2 + 1]));
            diagnostics[i] = new PoseHighlightRegionDiagnostic {
                part = Parts[i], isValid = true, relevantBoneCount = 2, validBoneCount = 2,
                trackingCoverage = 1f, worstRawErrorDegrees = error,
                shouldClear = error <= settings.limbClearExcessDegrees,
                shouldActivate = error >= settings.limbActivationExcessDegrees,
                severity = error / Mathf.Max(1f, settings.limbActivationExcessDegrees)
            };
        }
        result.leftArm = diagnostics[0]; result.rightArm = diagnostics[1];
        result.leftLeg = diagnostics[2]; result.rightLeg = diagnostics[3];
        return result;
    }
}
