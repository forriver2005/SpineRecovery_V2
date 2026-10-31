using UnityEngine;

// Teaching evidence from the existing VMC skeleton. These broad engineering
// tolerances do not measure pain, lumbar loading, muscle activation or foot pressure.
public struct HipThrustPoseGeometry
{
    public float hipFlexion;
    public float leftKneeBend;
    public float rightKneeBend;
    public float torsoTilt;
    public float pelvisTilt;
    public float torsoBend;
    public float hipAboveSupportLine;
    public float kneeHeight;
    public float footDrop;
    public float kneeAsymmetry;
    public float kneeWidthRatio;
}

public static class HipThrustPoseGuidance
{
    public static bool TryCapture(Animator animator, out HipThrustPoseGeometry geometry)
    {
        geometry = default;
        if (animator == null || !animator.isHuman) return false;
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest) ??
            animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform leftHip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightHip = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform leftKnee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rightKnee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (hips == null || chest == null || spine == null || leftHip == null ||
            rightHip == null || leftKnee == null || rightKnee == null ||
            leftFoot == null || rightFoot == null) return false;

        Vector3 torso = chest.position - hips.position;
        Vector3 knees = (leftKnee.position + rightKnee.position) * 0.5f;
        Vector3 thigh = knees - hips.position;
        Vector3 hipSide = rightHip.position - leftHip.position;
        float length = torso.magnitude;
        if (length < 0.0001f || thigh.sqrMagnitude < 0.000001f ||
            hipSide.sqrMagnitude < 0.000001f) return false;

        Vector3 support = knees - chest.position;
        if (support.sqrMagnitude < 0.000001f) return false;
        Vector3 onSupport = chest.position + support * Mathf.Clamp01(
            Vector3.Dot(hips.position - chest.position, support) / support.sqrMagnitude);
        geometry = new HipThrustPoseGeometry
        {
            hipFlexion = Vector3.Angle(-torso, thigh),
            leftKneeBend = Bend(leftHip.position, leftKnee.position, leftFoot.position),
            rightKneeBend = Bend(rightHip.position, rightKnee.position, rightFoot.position),
            torsoTilt = Mathf.Abs(90f - Vector3.Angle(torso, Vector3.up)),
            pelvisTilt = Mathf.Abs(90f - Vector3.Angle(hipSide, Vector3.up)),
            torsoBend = Vector3.Angle(spine.position - hips.position,
                chest.position - spine.position),
            hipAboveSupportLine = Vector3.Dot(hips.position - onSupport, Vector3.up) / length,
            kneeHeight = Mathf.Min(leftKnee.position.y - leftHip.position.y,
                rightKnee.position.y - rightHip.position.y) / length,
            footDrop = Mathf.Min(leftKnee.position.y - leftFoot.position.y,
                rightKnee.position.y - rightFoot.position.y) / length,
            kneeAsymmetry = Mathf.Abs(Vector3.Dot(
                rightKnee.position - leftKnee.position, Vector3.up)) / length,
            kneeWidthRatio = Vector3.Dot(rightKnee.position - leftKnee.position,
                hipSide.normalized) / hipSide.magnitude
        };
        return !float.IsNaN(geometry.hipFlexion) &&
            !float.IsInfinity(geometry.hipFlexion);
    }

    public static bool IsResting(HipThrustPoseGeometry pose)
    {
        return pose.torsoTilt <= 40f && pose.hipFlexion >= 20f &&
            pose.leftKneeBend >= 35f && pose.leftKneeBend <= 145f &&
            pose.rightKneeBend >= 35f && pose.rightKneeBend <= 145f &&
            pose.kneeHeight > 0.1f && pose.footDrop > 0.15f;
    }

    public static float LiftProgress(HipThrustPoseGeometry pose, HipThrustPoseGeometry rest)
    {
        return rest.hipFlexion - pose.hipFlexion;
    }

    public static PoseHighlightFrameDiagnostics EvaluateHeightTarget(
        HipThrustPoseGeometry pose,
        HipThrustPoseGeometry target,
        float enterTolerance,
        float exitTolerance)
    {
        float heightDifference = Mathf.Abs(
            pose.hipAboveSupportLine - target.hipAboveSupportLine);
        float enter = Mathf.Max(0f, enterTolerance);
        float exit = Mathf.Max(enter, exitTolerance);
        PoseHighlightRegionDiagnostic torso = new PoseHighlightRegionDiagnostic
        {
            part = BodyPart.Torso,
            isValid = true,
            shouldActivate = heightDifference <= enter,
            shouldClear = heightDifference > exit,
            worstRawErrorDegrees = heightDifference * 100f,
            severity = Mathf.Max(0f, exit - heightDifference),
            relevantBoneCount = 1,
            validBoneCount = 1,
            trackingCoverage = 1f
        };

        return new PoseHighlightFrameDiagnostics
        {
            leftArm = new PoseHighlightRegionDiagnostic { part = BodyPart.LeftArm },
            rightArm = new PoseHighlightRegionDiagnostic { part = BodyPart.RightArm },
            torso = torso,
            leftLeg = new PoseHighlightRegionDiagnostic { part = BodyPart.LeftLeg },
            rightLeg = new PoseHighlightRegionDiagnostic { part = BodyPart.RightLeg }
        };
    }

    private static float Bend(Vector3 hip, Vector3 knee, Vector3 foot) =>
        180f - Vector3.Angle(hip - knee, foot - knee);

}
