using UnityEngine;

namespace DeadBugDelivery
{
public enum DeadBugSide { None, LeftArmRightLeg, RightArmLeftLeg }

// Measurements in the avatar's physical presentation plane, independent of
// camera yaw, model scale and the pitch applied by ViewFollowGroup.
public struct DeadBugLimb
{
    public float Height, Reach, Bend, HorizontalError, Elevation, ProximalElevation, DistalElevation;
}

public struct DeadBugPose
{
    public DeadBugLimb LeftArm, RightArm, LeftLeg, RightLeg;
    public float TorsoTilt;

    public DeadBugLimb Arm(DeadBugSide side) => side == DeadBugSide.LeftArmRightLeg ? LeftArm : RightArm;
    public DeadBugLimb Leg(DeadBugSide side) => side == DeadBugSide.LeftArmRightLeg ? RightLeg : LeftLeg;
    public DeadBugLimb SupportArm(DeadBugSide side) => side == DeadBugSide.LeftArmRightLeg ? RightArm : LeftArm;
    public DeadBugLimb SupportLeg(DeadBugSide side) => side == DeadBugSide.LeftArmRightLeg ? LeftLeg : RightLeg;

    public static bool TryCapture(Animator avatar, out DeadBugPose pose)
    {
        pose = default;
        if (avatar == null || !avatar.isHuman) return false;
        Transform hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
        Transform la = avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform ra = avatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (hips == null || la == null || ra == null) return false;
        if (!Finite(hips.position) || !Finite(la.position) || !Finite(ra.position)) return false;
        Vector3 torso = (la.position + ra.position) * 0.5f - hips.position;
        Vector3 up = avatar.transform.parent != null ? avatar.transform.parent.up : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(torso, up);
        if (torso.magnitude < 0.05f || forward.magnitude < 0.03f) return false;
        forward.Normalize();
        pose.TorsoTilt = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(torso.normalized, up), -1f, 1f)) * Mathf.Rad2Deg);
        return CaptureLimb(avatar, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                   forward, up, out pose.LeftArm) &&
               CaptureLimb(avatar, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                   forward, up, out pose.RightArm) &&
               CaptureLimb(avatar, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                   -forward, up, out pose.LeftLeg) &&
               CaptureLimb(avatar, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
                   -forward, up, out pose.RightLeg);
    }

    private static bool CaptureLimb(Animator avatar, HumanBodyBones root, HumanBodyBones joint, HumanBodyBones end,
        Vector3 direction, Vector3 up, out DeadBugLimb limb)
    {
        limb = default;
        Transform a = avatar.GetBoneTransform(root), b = avatar.GetBoneTransform(joint), c = avatar.GetBoneTransform(end);
        if (a == null || b == null || c == null) return false;
        if (!Finite(a.position) || !Finite(b.position) || !Finite(c.position)) return false;
        float length = Vector3.Distance(a.position, b.position) + Vector3.Distance(b.position, c.position);
        if (length < 0.05f) return false;
        Vector3 reach = c.position - a.position;
        Vector3 horizontal = Vector3.ProjectOnPlane(reach, up);
        limb.ProximalElevation = Vector3.Angle(b.position - a.position, up);
        limb.DistalElevation = Mathf.Abs(90f - Vector3.Angle(c.position - b.position, up));
        limb.Height = Vector3.Dot(reach, up) / length;
        limb.Reach = Vector3.Dot(reach, direction) / length;
        limb.Bend = Vector3.Angle(a.position - b.position, c.position - b.position);
        limb.HorizontalError = horizontal.sqrMagnitude < 0.0001f ? 180f : Vector3.Angle(horizontal, direction);
        limb.Elevation = Mathf.Atan2(Vector3.Dot(reach, up), horizontal.magnitude) * Mathf.Rad2Deg;
        return !(float.IsNaN(limb.Height) || float.IsInfinity(limb.Height));
    }
    private static bool Finite(Vector3 v) =>
        !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
        !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
        !float.IsNaN(v.z) && !float.IsInfinity(v.z);
}
}
